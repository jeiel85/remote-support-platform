using RemoteSupport.Infrastructure;
using RemoteSupport.Protocol;
using RemoteSupport.Protocol.V1;

namespace RemoteSupport.UnitTests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class NativeProductBridgeTests
{
    [Fact]
    [Trait("Requirement", "NFR-SEC-011")]
    public async Task AuthorizedProductBridgesBindAndExchangePeerDataDirectly()
    {
        if (!OperatingSystem.IsWindows()) return;
        using EphemeralPeerIdentity hostIdentity = new();
        using EphemeralPeerIdentity operatorIdentity = new();
        Guid sessionId = Guid.NewGuid();
        Guid hostId = Guid.NewGuid();
        Guid operatorId = Guid.NewGuid();
        string context = Convert.ToBase64String(new byte[32]).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        PeerAuthorizationResponse hostAuthorization = Authorization(sessionId, hostId, "HOST", operatorId, "OPERATOR",
            operatorIdentity, context);
        PeerAuthorizationResponse operatorAuthorization = Authorization(sessionId, operatorId, "OPERATOR", hostId, "HOST",
            hostIdentity, context);
        TurnCredentialsResponse noRelay = new("local", [], DateTimeOffset.UtcNow.AddMinutes(5));
        using NativeAttendedSession host = new(hostAuthorization, hostIdentity, noRelay);
        using NativeAttendedSession oper = new(operatorAuthorization, operatorIdentity, noRelay);
        TaskCompletionSource hostBound = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource operatorBound = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<string> received = new(TaskCreationOptions.RunContinuationsAsynchronously);
        host.SecurityBindingChanged += state => { if (state == "TRANSPORT_BINDING_VERIFIED") hostBound.TrySetResult(); };
        oper.SecurityBindingChanged += state => { if (state == "TRANSPORT_BINDING_VERIFIED") operatorBound.TrySetResult(); };
        host.LocalDescriptionReady += description => oper.ApplyRemoteDescription(description);
        oper.LocalDescriptionReady += description => host.ApplyRemoteDescription(description);
        host.LocalIceCandidateReady += candidate => TryIce(oper, candidate);
        oper.LocalIceCandidateReady += candidate => TryIce(host, candidate);
        PeerDataSession hostProtocol = new(hostAuthorization);
        PeerDataSession operatorProtocol = new(operatorAuthorization);
        host.DataReceived += packet =>
        {
            Envelope envelope = hostProtocol.Decode(packet.Label, packet.Payload);
            if (envelope.BodyCase == Envelope.BodyOneofCase.ChatMessage) received.TrySetResult(envelope.ChatMessage.Text);
        };

        oper.StartNegotiation();
        await Task.WhenAll(hostBound.Task, operatorBound.Task).WaitAsync(TimeSpan.FromSeconds(15));
        byte[] frame = operatorProtocol.Encode(PeerChannel.Chat, envelope => envelope.ChatMessage = new ChatMessage
        {
            ChatMessageId = Guid.NewGuid().ToString("D"), Text = "native-bound-product-data",
            SentUtcUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        });
        await Task.Delay(100);
        oper.Send("rsp.chat.v1", frame);
        Assert.Equal("native-bound-product-data", await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private static void TryIce(NativeAttendedSession destination, NativeIceCandidate candidate)
    {
        try { destination.AddRemoteIce(candidate); }
        catch (InvalidOperationException) { }
    }

    private static PeerAuthorizationResponse Authorization(Guid sessionId, Guid peerId, string role, Guid remotePeerId,
        string remoteRole, EphemeralPeerIdentity remoteIdentity, string context) => new(sessionId, peerId, role,
        "native-product-test-token-000000000000000", ["CHAT", "VIEW_SCREEN"], 1, 1,
        DateTimeOffset.UtcNow.AddMinutes(5), remotePeerId, remoteRole, remoteIdentity.PublicJwk,
        remoteIdentity.KeyThumbprint, context);
}
