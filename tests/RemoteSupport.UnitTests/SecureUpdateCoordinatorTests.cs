using System.Security.Cryptography;
using RemoteSupport.Security;

namespace RemoteSupport.UnitTests;

public sealed class SecureUpdateCoordinatorTests
{
    [Fact]
    [Trait("Requirement", "AT-NFR-SEC-009")]
    public async Task ArtifactHashPublisherAndMonotonicSequenceAreEnforced()
    {
        using UpdateTestRoot root = new();
        byte[] payload = "signed-update-payload"u8.ToArray();
        string artifact = root.WriteArtifact(payload);
        VerifiedUpdateManifest manifest = Manifest(payload, sequence: 42);
        FakeActivation activation = new(healthy: true);
        AtomicUpdateStateStore store = new(root.StatePath);
        using SecureUpdateCoordinator coordinator = new(new UpdateArtifactVerifier(new FakeSigner()), store, activation);

        await coordinator.ApplyAsync(manifest, artifact);

        UpdateDeploymentState state = store.Read();
        Assert.Equal(42, state.HighestSeenSequence);
        Assert.Equal(42, state.Active?.ReleaseSequence);
        Assert.Null(state.Pending);
        Assert.Equal(["stage", "health", "commit"], activation.Calls);
        UpdateArtifactException replay = await Assert.ThrowsAsync<UpdateArtifactException>(() =>
            coordinator.ApplyAsync(manifest, artifact));
        Assert.Equal("UPDATE_ROLLBACK_BLOCKED", replay.Code);

        await File.WriteAllTextAsync(artifact, "tampered-update-payload");
        UpdateArtifactException tampered = await Assert.ThrowsAsync<UpdateArtifactException>(() =>
            new UpdateArtifactVerifier(new FakeSigner()).VerifyAsync(Manifest("tampered-update-payload"u8.ToArray(), 43)
                with { Artifact = manifest.Artifact }, artifact));
        Assert.True(tampered.Code is "UPDATE_ARTIFACT_SIZE_INVALID" or "UPDATE_ARTIFACT_HASH_INVALID");
    }

    [Fact]
    [Trait("Requirement", "AT-NFR-REL-007")]
    public async Task FailedHealthRollsBackButRetainsSecurityFloor()
    {
        using UpdateTestRoot root = new();
        byte[] firstPayload = "known-good"u8.ToArray();
        string first = root.WriteArtifact(firstPayload);
        AtomicUpdateStateStore store = new(root.StatePath);
        FakeActivation activation = new(healthy: true);
        using SecureUpdateCoordinator coordinator = new(new UpdateArtifactVerifier(new FakeSigner()), store, activation);
        await coordinator.ApplyAsync(Manifest(firstPayload, 10), first);

        byte[] badPayload = "bad-canary"u8.ToArray();
        string bad = root.WriteArtifact(badPayload, "bad.exe");
        activation.Healthy = false;
        UpdateArtifactException failure = await Assert.ThrowsAsync<UpdateArtifactException>(() =>
            coordinator.ApplyAsync(Manifest(badPayload, 11), bad));

        Assert.Equal("UPDATE_HEALTH_CHECK_FAILED", failure.Code);
        UpdateDeploymentState state = store.Read();
        Assert.Equal(11, state.HighestSeenSequence);
        Assert.Equal(10, state.Active?.ReleaseSequence);
        Assert.Null(state.Pending);
        Assert.Equal("rollback", activation.Calls[^1]);
    }

    [Fact]
    [Trait("Requirement", "AT-NFR-REL-007")]
    public async Task InterruptedPendingActivationIsRecoveredIdempotently()
    {
        using UpdateTestRoot root = new();
        InstalledUpdate prior = new("OPERATOR_CONSOLE", "1.0.0", 50, "prior.exe");
        InstalledUpdate candidate = new("OPERATOR_CONSOLE", "1.1.0", 51, "candidate.exe");
        AtomicUpdateStateStore store = new(root.StatePath);
        store.Write(new UpdateDeploymentState(1, 51, prior,
            new PendingUpdate(candidate, prior, DateTimeOffset.UnixEpoch)));
        FakeActivation activation = new(healthy: true);
        using SecureUpdateCoordinator coordinator = new(new UpdateArtifactVerifier(new FakeSigner()), store, activation);

        await coordinator.RecoverAsync();
        await coordinator.RecoverAsync();

        Assert.Equal(["rollback"], activation.Calls);
        Assert.Equal(50, store.Read().Active?.ReleaseSequence);
        Assert.Equal(51, store.Read().HighestSeenSequence);
    }

    private static VerifiedUpdateManifest Manifest(byte[] payload, long sequence) => new(
        "OPERATOR_CONSOLE", "canary", $"1.0.{sequence}", sequence, 1, 1,
        DateTimeOffset.UtcNow.AddDays(1), new UpdateArtifact("x64", "OPERATOR_INSTALLER",
            new Uri("https://updates.example/operator.exe"), payload.Length,
            Convert.ToHexStringLower(SHA256.HashData(payload)), new string('B', 40), "10.0.19045"));

    private sealed class FakeSigner : IAuthenticodeSignatureVerifier
    {
        public ValueTask<string> VerifyPublisherAsync(string artifactPath, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new string('B', 40));
    }

    private sealed class FakeActivation(bool healthy) : IUpdateActivation
    {
        public bool Healthy { get; set; } = healthy;
        public List<string> Calls { get; } = [];
        public Task StageAndActivateAsync(string verifiedArtifactPath, CancellationToken cancellationToken)
        { Calls.Add("stage"); return Task.CompletedTask; }
        public Task<bool> ProbeHealthAsync(CancellationToken cancellationToken)
        { Calls.Add("health"); return Task.FromResult(Healthy); }
        public Task CommitAsync(CancellationToken cancellationToken)
        { Calls.Add("commit"); return Task.CompletedTask; }
        public Task RollbackAsync(CancellationToken cancellationToken)
        { Calls.Add("rollback"); return Task.CompletedTask; }
    }

    private sealed class UpdateTestRoot : IDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), "rsp-update-tests", Guid.NewGuid().ToString("N"));
        public UpdateTestRoot() => Directory.CreateDirectory(path);
        public string StatePath => Path.Combine(path, "state.json");
        public string WriteArtifact(byte[] payload, string name = "update.exe")
        { string result = Path.Combine(path, name); File.WriteAllBytes(result, payload); return result; }
        public void Dispose() => Directory.Delete(path, recursive: true);
    }
}
