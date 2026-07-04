using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

return await OperatorSetup.RunAsync(args);

internal static class OperatorSetup
{
    private const string PayloadResource = "RemoteSupport.Operator.Payload.zip";
    private const string ManifestResource = "RemoteSupport.Operator.Payload.json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public static async Task<int> RunAsync(string[] args)
    {
        string command = args.Length == 0 ? "install" : args[0].TrimStart('-', '/').ToLowerInvariant();
        try
        {
            SetupPaths paths = SetupPaths.Create();
            Recover(paths);
            return command switch
            {
                "install" => await InstallAsync(paths, repair: false, transactional: false),
                "repair" => await InstallAsync(paths, repair: true, transactional: false),
                "stage" => await InstallAsync(paths, repair: false, transactional: true),
                "commit" => Commit(paths),
                "rollback" => Rollback(paths),
                "uninstall" => Uninstall(paths),
                _ => throw new SetupException("Usage: RemoteSupport.Operator.Setup [install|repair|stage|commit|rollback|uninstall]"),
            };
        }
        catch (SetupException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            Console.Error.WriteLine($"Setup failed safely: {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> InstallAsync(SetupPaths paths, bool repair, bool transactional)
    {
        using Stream manifestStream = Resource(ManifestResource);
        PayloadManifest manifest = await JsonSerializer.DeserializeAsync<PayloadManifest>(manifestStream, Json)
            ?? throw new SetupException("Embedded payload manifest is missing.");
        ValidateManifest(manifest);
        InstalledState? installed = ReadState(paths.StateFile);
        if (File.Exists(paths.PendingFile))
            throw new SetupException("A pending update must be committed or rolled back first.");
        if (!repair && installed is not null && manifest.ReleaseSequence < installed.ReleaseSequence)
            throw new SetupException("Downgrade blocked: installed release sequence is newer.");
        if (repair && installed is not null && manifest.ReleaseSequence != installed.ReleaseSequence)
            throw new SetupException("Repair payload must match the installed release sequence.");

        string staging = Path.Combine(paths.StagingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        string backup = paths.InstallDirectory + ".previous";
        bool activated = false;
        try
        {
            using Stream payload = Resource(PayloadResource);
            await VerifyAndExtractAsync(payload, staging, manifest);
            Directory.CreateDirectory(Path.GetDirectoryName(paths.InstallDirectory)!);
            WriteJournal(paths, new SetupJournal("staged", staging, backup, installed));
            if (Directory.Exists(backup)) Directory.Delete(backup, recursive: true);
            if (Directory.Exists(paths.InstallDirectory)) Directory.Move(paths.InstallDirectory, backup);
            WriteJournal(paths, new SetupJournal("swapping", staging, backup, installed));
            Directory.Move(staging, paths.InstallDirectory);
            activated = true;
            InstalledState state = new(manifest.Product, manifest.Version, manifest.ReleaseSequence,
                manifest.Architecture, DateTimeOffset.UtcNow, manifest.Files.Select(file => file.Path).ToArray());
            AtomicJson(paths.StateFile, state);
            File.Delete(paths.JournalFile);
            if (transactional)
                AtomicJson(paths.PendingFile, new PendingInstall(manifest.ReleaseSequence, installed, Directory.Exists(backup)));
            else if (Directory.Exists(backup)) Directory.Delete(backup, recursive: true);
            Console.WriteLine(transactional
                ? $"Remote Support Operator Console {manifest.Version} staged pending health confirmation."
                : $"Remote Support Operator Console {manifest.Version} installed for the current user.");
            return 0;
        }
        catch
        {
            if (activated && Directory.Exists(paths.InstallDirectory)) Directory.Delete(paths.InstallDirectory, recursive: true);
            if (!Directory.Exists(paths.InstallDirectory) && Directory.Exists(backup)) Directory.Move(backup, paths.InstallDirectory);
            if (installed is null) File.Delete(paths.StateFile); else AtomicJson(paths.StateFile, installed);
            File.Delete(paths.PendingFile);
            throw;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static int Commit(SetupPaths paths)
    {
        PendingInstall pending = ReadJson<PendingInstall>(paths.PendingFile)
            ?? throw new SetupException("No update is pending health confirmation.");
        InstalledState state = ReadState(paths.StateFile) ?? throw new SetupException("Pending update state is missing.");
        if (state.ReleaseSequence != pending.ReleaseSequence)
            throw new SetupException("Pending update sequence does not match the installed state.");
        string backup = paths.InstallDirectory + ".previous";
        // Removing the pending marker is the commit point. Failure to clean an
        // obsolete backup after this point must not turn a healthy install into
        // an ambiguous rollback request; the next transaction also cleans it.
        File.Delete(paths.PendingFile);
        try { if (Directory.Exists(backup)) Directory.Delete(backup, recursive: true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { Console.Error.WriteLine($"Committed update retained an obsolete backup for later cleanup: {exception.GetType().Name}."); }
        Console.WriteLine($"Remote Support Operator Console {state.Version} health confirmation committed.");
        return 0;
    }

    private static int Rollback(SetupPaths paths)
    {
        PendingInstall? pending = ReadJson<PendingInstall>(paths.PendingFile);
        if (pending is null)
        {
            Console.WriteLine("No staged update required rollback.");
            return 0;
        }
        string backup = paths.InstallDirectory + ".previous";
        if (pending.HadPreviousInstall && (!Directory.Exists(backup) || pending.PreviousState is null))
            throw new SetupException("Last-known-good installation backup or state is missing; current installation was preserved.");
        if (Directory.Exists(paths.InstallDirectory)) Directory.Delete(paths.InstallDirectory, recursive: true);
        if (pending.HadPreviousInstall)
        {
            Directory.Move(backup, paths.InstallDirectory);
            AtomicJson(paths.StateFile, pending.PreviousState!);
        }
        else File.Delete(paths.StateFile);
        File.Delete(paths.PendingFile);
        Console.WriteLine("Remote Support Operator Console rolled back to the last-known-good installation.");
        return 0;
    }

    private static int Uninstall(SetupPaths paths)
    {
        InstalledState? state = ReadState(paths.StateFile);
        if (state is null && !Directory.Exists(paths.InstallDirectory))
        {
            Console.WriteLine("Remote Support Operator Console is not installed.");
            return 0;
        }
        EnsureUnder(paths.ProgramsRoot, paths.InstallDirectory);
        if (Directory.Exists(paths.InstallDirectory)) Directory.Delete(paths.InstallDirectory, recursive: true);
        string backup = paths.InstallDirectory + ".previous";
        EnsureUnder(paths.ProgramsRoot, backup);
        if (Directory.Exists(backup)) Directory.Delete(backup, recursive: true);
        File.Delete(paths.StateFile);
        File.Delete(paths.JournalFile);
        File.Delete(paths.PendingFile);
        Console.WriteLine("Remote Support Operator Console was removed. No service or scheduled task was installed.");
        return 0;
    }

    private static async Task VerifyAndExtractAsync(Stream payload, string staging, PayloadManifest manifest)
    {
        string temporaryZip = Path.Combine(staging, ".payload.zip");
        await using (FileStream output = new(temporaryZip, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan)) await payload.CopyToAsync(output);
        string payloadHash = await HashFileAsync(temporaryZip);
        if (!FixedHex(payloadHash, manifest.PayloadSha256)) throw new SetupException("Embedded payload hash mismatch.");
        string extraction = Path.Combine(staging, "content");
        Directory.CreateDirectory(extraction);
        using (ZipArchive archive = ZipFile.OpenRead(temporaryZip))
        {
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                string destination = Path.GetFullPath(Path.Combine(extraction, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                EnsureUnder(extraction, destination);
                if (entry.FullName.Contains(':', StringComparison.Ordinal) || (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000)
                    throw new SetupException("Payload contains an unsafe path or symbolic link.");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: false);
            }
        }
        foreach (PayloadFile expected in manifest.Files)
        {
            string path = Path.GetFullPath(Path.Combine(extraction, expected.Path.Replace('/', Path.DirectorySeparatorChar)));
            EnsureUnder(extraction, path);
            if (!File.Exists(path) || new FileInfo(path).Length != expected.Size ||
                !FixedHex(await HashFileAsync(path), expected.Sha256))
                throw new SetupException($"Payload file verification failed: {expected.Path}");
        }
        string[] actual = Directory.GetFiles(extraction, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(extraction, path).Replace('\\', '/')).Order(StringComparer.Ordinal).ToArray();
        string[] declared = manifest.Files.Select(file => file.Path).Order(StringComparer.Ordinal).ToArray();
        if (!actual.SequenceEqual(declared, StringComparer.Ordinal)) throw new SetupException("Payload has undeclared files.");
        File.Delete(temporaryZip);
        foreach (string path in Directory.GetFileSystemEntries(extraction))
        {
            string destination = Path.Combine(staging, Path.GetFileName(path));
            if (Directory.Exists(path)) Directory.Move(path, destination);
            else File.Move(path, destination);
        }
        Directory.Delete(extraction);
    }

    private static void ValidateManifest(PayloadManifest manifest)
    {
        string architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => "unsupported",
        };
        if (manifest.SchemaVersion != 1 || manifest.Product != "OPERATOR_CONSOLE" || manifest.ReleaseSequence < 1 ||
            manifest.Version.Length is < 1 or > 64 || manifest.Architecture != architecture ||
            manifest.PayloadSha256.Length != 64 || manifest.Files.Count is < 1 or > 10_000 ||
            manifest.Files.Select(file => file.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Files.Count)
            throw new SetupException("Embedded payload manifest is invalid or for another architecture.");
    }

    private static void Recover(SetupPaths paths)
    {
        SetupJournal? journal = ReadJson<SetupJournal>(paths.JournalFile);
        if (journal is null) return;
        EnsureUnder(paths.StagingRoot, journal.StagingDirectory);
        EnsureUnder(paths.ProgramsRoot, journal.BackupDirectory);
        if (journal.Phase == "swapping")
        {
            if (Directory.Exists(paths.InstallDirectory)) Directory.Delete(paths.InstallDirectory, recursive: true);
            if (Directory.Exists(journal.BackupDirectory)) Directory.Move(journal.BackupDirectory, paths.InstallDirectory);
            if (journal.PreviousState is null) File.Delete(paths.StateFile); else AtomicJson(paths.StateFile, journal.PreviousState);
        }
        else if (!Directory.Exists(paths.InstallDirectory) && Directory.Exists(journal.BackupDirectory))
            Directory.Move(journal.BackupDirectory, paths.InstallDirectory);
        if (Directory.Exists(journal.StagingDirectory)) Directory.Delete(journal.StagingDirectory, recursive: true);
        File.Delete(paths.JournalFile);
    }

    private static Stream Resource(string name) => typeof(OperatorSetup).Assembly.GetManifestResourceStream(name)
        ?? throw new SetupException("This setup executable does not contain a release payload. Run the attended packaging target.");
    private static InstalledState? ReadState(string path) => ReadJson<InstalledState>(path);
    private static T? ReadJson<T>(string path) => File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) : default;
    private static void WriteJournal(SetupPaths paths, SetupJournal journal) => AtomicJson(paths.JournalFile, journal);
    private static void AtomicJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".new";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, Json));
        File.Move(temporary, path, overwrite: true);
    }
    private static void EnsureUnder(string root, string path)
    {
        string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new SetupException("Setup path escaped its per-user product root.");
    }
    private static bool FixedHex(string left, string right)
    {
        byte[] a;
        byte[] b;
        try { a = Convert.FromHexString(left); b = Convert.FromHexString(right); }
        catch (FormatException) { return false; }
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
    private static async Task<string> HashFileAsync(string path)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
    }
}

internal sealed record PayloadManifest(int SchemaVersion, string Product, string Version, long ReleaseSequence,
    string Architecture, string PayloadSha256, IReadOnlyList<PayloadFile> Files);
internal sealed record PayloadFile(string Path, long Size, string Sha256);
internal sealed record InstalledState(string Product, string Version, long ReleaseSequence, string Architecture,
    DateTimeOffset InstalledAt, IReadOnlyList<string> Files);
internal sealed record SetupJournal(string Phase, string StagingDirectory, string BackupDirectory, InstalledState? PreviousState);
internal sealed record PendingInstall(long ReleaseSequence, InstalledState? PreviousState, bool HadPreviousInstall);
internal sealed record SetupPaths(string ProgramsRoot, string InstallDirectory, string StateFile, string JournalFile,
    string PendingFile, string StagingRoot)
{
    public static SetupPaths Create()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string? testRoot = Environment.GetEnvironmentVariable("RS_SETUP_TEST_ROOT");
        if (!string.IsNullOrWhiteSpace(testRoot))
        {
            string allowed = Path.Combine(Path.GetTempPath(), "remote-support-package-test");
            string candidate = Path.GetFullPath(testRoot);
            string prefix = Path.GetFullPath(allowed).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new SetupException("Isolated setup test root must remain under the operating-system temporary directory.");
            local = candidate;
        }
        string programs = Path.Combine(local, "Programs", "RemoteSupport");
        string data = Path.Combine(local, "RemoteSupport");
        return new SetupPaths(programs, Path.Combine(programs, "Operator"), Path.Combine(data, "operator-install.json"),
            Path.Combine(data, "operator-install.journal.json"), Path.Combine(data, "operator-update-pending.json"),
            Path.Combine(data, ".staging"));
    }
}
internal sealed class SetupException(string message) : Exception(message);
