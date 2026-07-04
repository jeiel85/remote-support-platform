using System.Diagnostics;
using RemoteSupport.Observability;
using RemoteSupport.Security;

return await UpdaterProgram.RunAsync(args);

internal static class UpdaterProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        string requested = args.FirstOrDefault()?.TrimStart('-', '/').ToLowerInvariant() ?? "startup";
        string operation = requested is "apply" or "recover" ? requested : "startup";
        string? updateChannel = null;
        using Activity? activity = RemoteSupportTelemetry.Start("updater", operation);
        try
        {
            Dictionary<string, string> options = Parse(args);
            string command = Required(options, "command");
            string statePath = Required(options, "state");
            string setup = Required(options, "artifact");
            string health = Required(options, "health");
            AtomicUpdateStateStore state = new(statePath);
            SetupProcessActivation activation = new(setup, health, TimeSpan.FromMinutes(2));
            using SecureUpdateCoordinator coordinator = new(new UpdateArtifactVerifier(), state, activation,
                TimeSpan.FromSeconds(30));

            if (command == "recover")
            {
                await coordinator.RecoverAsync();
                RemoteSupportTelemetry.Record("updater", operation, "success", elapsed.Elapsed);
                Console.WriteLine("Interrupted update recovery completed.");
                return 0;
            }
            if (command != "apply") throw new ArgumentException("Command must be apply or recover.");

            updateChannel = Required(options, "channel");

            DateTimeOffset now = DateTimeOffset.UtcNow;
            UpdateTrustStore trust = UpdateTrustStore.LoadBootstrap(await File.ReadAllBytesAsync(Required(options, "root")), now);
            UpdateDeploymentState local = state.Read();
            VerifiedUpdateManifest manifest = trust.VerifyManifest(
                await File.ReadAllBytesAsync(Required(options, "manifest")),
                Required(options, "product"), updateChannel, Required(options, "architecture"),
                local.HighestSeenSequence, Required(options, "rollout-identity"), now);
            await coordinator.ApplyAsync(manifest, setup);
            RemoteSupportTelemetry.Record("updater", operation, "success", elapsed.Elapsed);
            RemoteSupportTelemetry.RecordUpdate(updateChannel, "success");
            Console.WriteLine($"Secure update {manifest.Version} ({manifest.ReleaseSequence}) committed.");
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or
            UpdateSecurityException or UpdateArtifactException or PlatformNotSupportedException)
        {
            string code = exception switch
            {
                UpdateSecurityException security => security.Code,
                UpdateArtifactException artifact => artifact.Code,
                _ => "UPDATE_OPERATION_FAILED",
            };
            RemoteSupportTelemetry.Record("updater", operation, "failure", elapsed.Elapsed, code);
            if (updateChannel is not null) RemoteSupportTelemetry.RecordUpdate(updateChannel, "failure", code);
            activity?.SetStatus(ActivityStatusCode.Error, code);
            Console.Error.WriteLine($"{code}: {exception.Message}");
            return 2;
        }
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        if (args.Length == 0) throw new ArgumentException(Usage);
        result["command"] = args[0].TrimStart('-', '/').ToLowerInvariant();
        for (int index = 1; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException(Usage);
            if (!result.TryAdd(args[index][2..], args[index + 1])) throw new ArgumentException("Duplicate updater option.");
        }
        return result;
    }

    private static string Required(Dictionary<string, string> options, string name) =>
        options.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value) ? value :
            throw new ArgumentException($"Missing --{name}. {Usage}");

    private const string Usage = "RemoteSupport.Updater apply --root FILE --manifest FILE --artifact SETUP.exe " +
        "--health APP.exe --state FILE --product NAME --channel CHANNEL --architecture ARCH --rollout-identity ID; " +
        "or recover --artifact SETUP.exe --health APP.exe --state FILE";
}
