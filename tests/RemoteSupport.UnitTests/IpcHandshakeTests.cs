using System.IO.Pipes;
using RemoteSupport.Ipc;
using RemoteSupport.Ipc.V1;

namespace RemoteSupport.UnitTests;

public sealed class IpcHandshakeTests
{
    [Fact]
    public async Task ClientAndServiceCompleteMutualHandshakeOverALoopbackPipe()
    {
        string pipeName = $"rsp-test-{Guid.NewGuid():N}";
        byte[] launchSecret = IpcHandshake.GenerateNonce();
        await using NamedPipeServerStream server = new(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await using NamedPipeClientStream client = new(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        Task acceptTask = server.WaitForConnectionAsync();
        await client.ConnectAsync(5000);
        await acceptTask;

        await using IpcMessageTransport serverTransport = new(server);
        await using IpcMessageTransport clientTransport = new(client);

        Task<ClientHello> serviceHandshake = IpcHandshake.RunServiceHandshakeAsync(serverTransport, launchSecret,
            "1.0.0-service", 262_144, [BrokerCapability.HealthReporting], DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeMilliseconds(),
            CancellationToken.None);
        Task<ServiceHello> clientHandshake = IpcHandshake.RunClientHandshakeAsync(clientTransport, launchSecret,
            "1.0.0-agent", new byte[32], [BrokerCapability.HealthReporting], CancellationToken.None);

        ClientHello receivedHello = await serviceHandshake;
        ServiceHello receivedServiceHello = await clientHandshake;

        Assert.Equal("1.0.0-agent", receivedHello.ExecutableVersion);
        Assert.Equal("1.0.0-service", receivedServiceHello.ServiceVersion);
        Assert.Contains(BrokerCapability.HealthReporting, receivedServiceHello.GrantedCapabilities);
    }

    [Fact]
    public void MismatchedLaunchSecretRejectsClientHello()
    {
        byte[] launchSecret = IpcHandshake.GenerateNonce();
        byte[] wrongSecret = IpcHandshake.GenerateNonce();
        byte[] clientNonce = IpcHandshake.GenerateNonce();
        ClientHello hello = IpcHandshake.CreateClientHello(launchSecret, clientNonce, "1.0.0", new byte[32], []);
        Assert.Throws<IpcHandshakeException>(() => IpcHandshake.VerifyClientHello(wrongSecret, hello));
    }

    [Fact]
    public void MismatchedServiceNonceRejectsChallengeResponse()
    {
        byte[] launchSecret = IpcHandshake.GenerateNonce();
        byte[] serviceNonce = IpcHandshake.GenerateNonce();
        ChallengeResponse response = IpcHandshake.CreateChallengeResponse(launchSecret, serviceNonce);
        Assert.Throws<IpcHandshakeException>(() =>
            IpcHandshake.VerifyChallengeResponse(launchSecret, IpcHandshake.GenerateNonce(), response));
    }

    [Fact]
    public async Task TransportRejectsMessageBeyondTheConfiguredBound()
    {
        string pipeName = $"rsp-test-{Guid.NewGuid():N}";
        await using NamedPipeServerStream server = new(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await using NamedPipeClientStream client = new(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        Task acceptTask = server.WaitForConnectionAsync();
        await client.ConnectAsync(5000);
        await acceptTask;

        await using IpcMessageTransport sender = new(client, maxMessageBytes: 16);
        IpcEnvelope oversized = new() { ClientHello = new ClientHello { ExecutableVersion = new string('x', 64) } };
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(oversized, CancellationToken.None));
    }
}
