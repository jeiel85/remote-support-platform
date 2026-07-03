using System.Text.Json;

namespace RemoteSupport.Observability;

public sealed class CrashRecoveryGuard : IDisposable
{
    private readonly string markerPath;
    private bool disposed;

    private CrashRecoveryGuard(string markerPath, bool recoveredFromCrash)
    {
        this.markerPath = markerPath;
        RecoveredFromCrash = recoveredFromCrash;
    }

    public bool RecoveredFromCrash { get; }

    public static CrashRecoveryGuard Start(string product, string version)
    {
        if (product.Length is < 1 or > 64 || version.Length is < 1 or > 64 || product.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            throw new ArgumentException("Crash marker product or version is invalid.");
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RemoteSupport", "crash-state");
        Directory.CreateDirectory(directory);
        string marker = Path.Combine(directory, product + ".json");
        bool recovered = File.Exists(marker);
        string staging = marker + ".new";
        File.WriteAllText(staging, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            product,
            version,
            processId = Environment.ProcessId,
            startedAt = DateTimeOffset.UtcNow,
        }));
        File.Move(staging, marker, overwrite: true);
        return new CrashRecoveryGuard(marker, recovered);
    }

    public void Dispose()
    {
        if (disposed) return;
        File.Delete(markerPath);
        disposed = true;
        GC.SuppressFinalize(this);
    }
}
