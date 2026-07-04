using System.Diagnostics;
using System.Text.Json;

namespace RemoteSupport.Security;

public sealed record InstalledUpdate(string Product, string Version, long ReleaseSequence, string ArtifactPath);
public sealed record PendingUpdate(InstalledUpdate Candidate, InstalledUpdate? Previous, DateTimeOffset StartedAt);
public sealed record UpdateDeploymentState(int SchemaVersion, long HighestSeenSequence, InstalledUpdate? Active,
    PendingUpdate? Pending)
{
    public static UpdateDeploymentState Empty { get; } = new(1, 0, null, null);
}

public interface IUpdateActivation
{
    Task StageAndActivateAsync(string verifiedArtifactPath, CancellationToken cancellationToken);
    Task<bool> ProbeHealthAsync(CancellationToken cancellationToken);
    Task CommitAsync(CancellationToken cancellationToken);
    Task RollbackAsync(CancellationToken cancellationToken);
}

public sealed class AtomicUpdateStateStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };
    private readonly string path;

    public AtomicUpdateStateStore(string path) => this.path = Path.GetFullPath(path);

    public UpdateDeploymentState Read()
    {
        if (!File.Exists(path)) return UpdateDeploymentState.Empty;
        try
        {
            UpdateDeploymentState state = JsonSerializer.Deserialize<UpdateDeploymentState>(File.ReadAllText(path), Json)
                ?? throw new JsonException("Update state was empty.");
            Validate(state);
            return state;
        }
        catch (Exception exception) when (exception is JsonException or IOException or ArgumentException)
        {
            throw new UpdateArtifactException("UPDATE_STATE_INVALID",
                $"Update state could not be validated: {exception.GetType().Name}.");
        }
    }

    public void Write(UpdateDeploymentState state)
    {
        Validate(state);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".new";
        using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, state, Json);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: true);
    }

    private static void Validate(UpdateDeploymentState state)
    {
        if (state.SchemaVersion != 1 || state.HighestSeenSequence < 0 ||
            state.Active is { ReleaseSequence: < 1 } || state.Pending is { Candidate.ReleaseSequence: < 1 } ||
            (state.Active is not null && state.HighestSeenSequence < state.Active.ReleaseSequence) ||
            (state.Pending is not null && state.HighestSeenSequence != state.Pending.Candidate.ReleaseSequence))
            throw new ArgumentException("Update state invariants are invalid.");
    }
}

public sealed class SecureUpdateCoordinator(UpdateArtifactVerifier artifactVerifier, AtomicUpdateStateStore stateStore,
    IUpdateActivation activation, TimeSpan? healthTimeout = null) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly TimeSpan healthTimeout = healthTimeout ?? TimeSpan.FromSeconds(30);

    public async Task ApplyAsync(VerifiedUpdateManifest manifest, string artifactPath,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            UpdateDeploymentState state = stateStore.Read();
            if (state.Pending is not null)
                throw new UpdateArtifactException("UPDATE_RECOVERY_REQUIRED", "An interrupted update must be recovered first.");
            if (manifest.ReleaseSequence <= state.HighestSeenSequence)
                throw new UpdateArtifactException("UPDATE_ROLLBACK_BLOCKED", "Update sequence does not exceed the local security floor.");

            await artifactVerifier.VerifyAsync(manifest, artifactPath, cancellationToken).ConfigureAwait(false);
            InstalledUpdate candidate = new(manifest.Product, manifest.Version, manifest.ReleaseSequence,
                Path.GetFullPath(artifactPath));
            state = state with
            {
                HighestSeenSequence = candidate.ReleaseSequence,
                Pending = new PendingUpdate(candidate, state.Active, DateTimeOffset.UtcNow),
            };
            stateStore.Write(state);

            try
            {
                await activation.StageAndActivateAsync(candidate.ArtifactPath, cancellationToken).ConfigureAwait(false);
                using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(healthTimeout);
                if (!await activation.ProbeHealthAsync(timeout.Token).ConfigureAwait(false))
                    throw new UpdateArtifactException("UPDATE_HEALTH_CHECK_FAILED", "Updated product did not pass its health check.");
                await activation.CommitAsync(cancellationToken).ConfigureAwait(false);
                stateStore.Write(state with { Active = candidate, Pending = null });
            }
            catch
            {
                await activation.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                stateStore.Write(state with { Active = state.Pending.Previous, Pending = null });
                throw;
            }
        }
        finally { gate.Release(); }
    }

    public async Task RecoverAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            UpdateDeploymentState state = stateStore.Read();
            if (state.Pending is null) return;
            await activation.RollbackAsync(cancellationToken).ConfigureAwait(false);
            stateStore.Write(state with { Active = state.Pending.Previous, Pending = null });
        }
        finally { gate.Release(); }
    }

    public void Dispose() => gate.Dispose();
}

public sealed class SetupProcessActivation(string setupExecutablePath, string healthExecutablePath, TimeSpan processTimeout) : IUpdateActivation
{
    private string setupPath = Path.GetFullPath(setupExecutablePath);

    public Task StageAndActivateAsync(string verifiedArtifactPath, CancellationToken cancellationToken)
    {
        setupPath = Path.GetFullPath(verifiedArtifactPath);
        return RunAsync(verifiedArtifactPath, "stage", cancellationToken);
    }

    public async Task<bool> ProbeHealthAsync(CancellationToken cancellationToken)
    {
        try { await RunAsync(healthExecutablePath, "--smoke-test", cancellationToken).ConfigureAwait(false); return true; }
        catch (UpdateArtifactException) { return false; }
    }

    public Task CommitAsync(CancellationToken cancellationToken) =>
        RunAsync(setupPath, "commit", cancellationToken);

    public Task RollbackAsync(CancellationToken cancellationToken) =>
        RunAsync(setupPath, "rollback", cancellationToken);

    private async Task RunAsync(string executable, string arguments, CancellationToken cancellationToken)
    {
        ProcessStartInfo start = new(Path.GetFullPath(executable), arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        using Process process = Process.Start(start) ?? throw new UpdateArtifactException("UPDATE_PROCESS_FAILED", "Update process did not start.");
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(processTimeout);
        try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw new UpdateArtifactException("UPDATE_PROCESS_TIMEOUT", "Update process exceeded its bounded timeout.");
        }
        if (process.ExitCode != 0)
            throw new UpdateArtifactException("UPDATE_PROCESS_FAILED", $"Update process failed with exit code {process.ExitCode}.");
    }
}
