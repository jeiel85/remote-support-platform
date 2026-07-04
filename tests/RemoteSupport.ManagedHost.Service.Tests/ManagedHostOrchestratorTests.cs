using Microsoft.Extensions.Logging.Abstractions;
using RemoteSupport.Ipc.V1;
using RemoteSupport.ManagedHost.Service;

namespace RemoteSupport.ManagedHost.Service.Tests;

public sealed class ManagedHostOrchestratorTests
{
    [Fact]
    public async Task ApprovedConsentSubmitsASignedApprovalWithGrantedScopes()
    {
        using CngDeviceIdentityKey deviceKey = CngDeviceIdentityKey.CreateEphemeral();
        FakeManagedHostServer server = new(deviceKey.PublicJwk);
        using HttpClient httpClient = new(server) { BaseAddress = new Uri("https://managed-host.test") };
        DeviceCredentialClient credentialClient = new(httpClient, server.DeviceId);
        FakeAgentLauncher launcher = new(approve: true, grantedScopes: ["VIEW_SCREEN"]);
        ManagedHostDeviceState state = new(server.DeviceId, deviceKey, 1, "0.13.0", "Windows 11 24H2");
        ManagedHostOrchestrator orchestrator = new(credentialClient, launcher, state,
            NullLogger<ManagedHostOrchestrator>.Instance, TimeSpan.FromSeconds(5));

        await orchestrator.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, server.DecisionCallCount);
        Assert.True(server.LastDecisionApproved);
        Assert.Equal(["VIEW_SCREEN"], server.LastDecisionGrantedScopes);
        Assert.NotNull(state.Credential);
    }

    [Fact]
    public async Task DeniedConsentSubmitsARejectionWithNoGrantedScopes()
    {
        using CngDeviceIdentityKey deviceKey = CngDeviceIdentityKey.CreateEphemeral();
        FakeManagedHostServer server = new(deviceKey.PublicJwk);
        using HttpClient httpClient = new(server) { BaseAddress = new Uri("https://managed-host.test") };
        DeviceCredentialClient credentialClient = new(httpClient, server.DeviceId);
        FakeAgentLauncher launcher = new(approve: false, grantedScopes: []);
        ManagedHostDeviceState state = new(server.DeviceId, deviceKey, 1, "0.13.0", "Windows 11 24H2");
        ManagedHostOrchestrator orchestrator = new(credentialClient, launcher, state,
            NullLogger<ManagedHostOrchestrator>.Instance, TimeSpan.FromSeconds(5));

        await orchestrator.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, server.DecisionCallCount);
        Assert.False(server.LastDecisionApproved);
        Assert.Empty(server.LastDecisionGrantedScopes);
    }

    [Fact]
    public async Task ConsentTimeoutIsTreatedAsDenied()
    {
        using CngDeviceIdentityKey deviceKey = CngDeviceIdentityKey.CreateEphemeral();
        FakeManagedHostServer server = new(deviceKey.PublicJwk);
        using HttpClient httpClient = new(server) { BaseAddress = new Uri("https://managed-host.test") };
        DeviceCredentialClient credentialClient = new(httpClient, server.DeviceId);
        FakeAgentLauncher launcher = new(approve: null, grantedScopes: []);
        ManagedHostDeviceState state = new(server.DeviceId, deviceKey, 1, "0.13.0", "Windows 11 24H2");
        ManagedHostOrchestrator orchestrator = new(credentialClient, launcher, state,
            NullLogger<ManagedHostOrchestrator>.Instance, TimeSpan.FromSeconds(5));

        await orchestrator.RunOnceAsync(CancellationToken.None);

        Assert.False(server.LastDecisionApproved);
    }

    private sealed class FakeAgentLauncher(bool? approve, string[] grantedScopes) : IInteractiveAgentLauncher
    {
        public Task<ManagedSessionConsentResult?> RequestConsentAsync(ManagedSessionConsentRequest request,
            TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (approve is null) return Task.FromResult<ManagedSessionConsentResult?>(null);
            ManagedSessionConsentResult result = new() { SessionId = request.SessionId, Approved = approve.Value };
            foreach (string scope in grantedScopes)
                result.GrantedScopes.Add(Enum.Parse<RemoteSupport.Protocol.V1.CapabilityScope>(ToPascalCase(scope)));
            return Task.FromResult<ManagedSessionConsentResult?>(result);
        }

        private static string ToPascalCase(string scope) => string.Concat(scope.Split('_')
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));
    }
}
