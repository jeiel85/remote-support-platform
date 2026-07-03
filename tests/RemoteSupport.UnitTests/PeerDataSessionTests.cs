using RemoteSupport.Infrastructure;
using RemoteSupport.Protocol;
using RemoteSupport.Protocol.V1;

namespace RemoteSupport.UnitTests;

public sealed class PeerDataSessionTests
{
    [Fact]
    public void AuthorizedPeersRoundTripProductFramesAndRejectReplay()
    {
        using EphemeralPeerIdentity hostKey = new();
        using EphemeralPeerIdentity operatorKey = new();
        Guid session = Guid.NewGuid();
        Guid host = Guid.NewGuid();
        Guid oper = Guid.NewGuid();
        string context = EphemeralPeerIdentityForTest.ContextHash();
        PeerAuthorizationResponse hostAuthorization = Authorization(session, host, "HOST", oper, "OPERATOR",
            operatorKey.PublicJwk, operatorKey.KeyThumbprint, context);
        PeerAuthorizationResponse operatorAuthorization = Authorization(session, oper, "OPERATOR", host, "HOST",
            hostKey.PublicJwk, hostKey.KeyThumbprint, context);
        PeerDataSession sender = new(hostAuthorization);
        PeerDataSession receiver = new(operatorAuthorization);
        byte[] frame = sender.Encode(PeerChannel.Chat, envelope => envelope.ChatMessage = new ChatMessage
        {
            ChatMessageId = Guid.NewGuid().ToString("D"), Text = "bound peer message",
        });

        Envelope decoded = receiver.Decode("rsp.chat.v1", frame);
        Assert.Equal("bound peer message", decoded.ChatMessage.Text);
        Assert.Equal("SIGNAL_SEQUENCE_INVALID", Assert.Throws<PeerProtocolException>(() =>
            receiver.Decode("rsp.chat.v1", frame)).Code.Value);
    }

    [Fact]
    public void HostPermissionStateAdvancesRevisionAndOnlyRemovesScopes()
    {
        using EphemeralPeerIdentity hostKey = new();
        using EphemeralPeerIdentity operatorKey = new();
        Guid session = Guid.NewGuid();
        Guid host = Guid.NewGuid();
        Guid oper = Guid.NewGuid();
        string context = EphemeralPeerIdentityForTest.ContextHash();
        PeerDataSession sender = new(Authorization(session, host, "HOST", oper, "OPERATOR",
            operatorKey.PublicJwk, operatorKey.KeyThumbprint, context));
        PeerDataSession receiver = new(Authorization(session, oper, "OPERATOR", host, "HOST",
            hostKey.PublicJwk, hostKey.KeyThumbprint, context));
        sender.RestrictLocalPermissions(["VIEW_SCREEN"]);
        sender.ApplyLocalPermissionRevision(2, ["VIEW_SCREEN"]);
        byte[] frame = sender.Encode(PeerChannel.Control, envelope => envelope.PermissionState = new PermissionState
        {
            Revision = 2,
            ReasonCode = "LOCAL_USER_REVOKED",
            ActiveScopes = { RemoteSupport.Protocol.V1.CapabilityScope.ViewScreen },
            RevokedScopes = { RemoteSupport.Protocol.V1.CapabilityScope.Chat },
        });

        receiver.Decode("rsp.control.v1", frame);

        Assert.Equal(2UL, receiver.PermissionGate.Current.Revision);
        Assert.True(receiver.PermissionGate.Current.ActiveScopes.Contains(RemoteSupport.Domain.CapabilityScope.ViewScreen));
        Assert.False(receiver.PermissionGate.Current.ActiveScopes.Contains(RemoteSupport.Domain.CapabilityScope.Chat));
        Assert.Equal("AUTHZ_SCOPE_DENIED", Assert.Throws<RemoteSupport.Application.DataFeatureException>(() =>
            receiver.PermissionGate.Demand(RemoteSupport.Domain.CapabilityScope.Chat, 2, "AUTHZ_SCOPE_DENIED")).Code.Value);
    }

    private static PeerAuthorizationResponse Authorization(Guid session, Guid local, string role, Guid remote,
        string remoteRole, PeerPublicJwk remoteKey, string remoteThumbprint, string context) => new(session, local, role,
        "peer-token-not-used-in-unit-test-000000000000000", ["CHAT", "VIEW_SCREEN"], 1, 1,
        DateTimeOffset.UtcNow.AddMinutes(5), remote, remoteRole, remoteKey, remoteThumbprint, context);

    private static class EphemeralPeerIdentityForTest
    {
        public static string ContextHash() => Convert.ToBase64String(new byte[32]).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
