using System.Security.Cryptography;
using System.Text;
using RemoteSupport.Application;
using RemoteSupport.Domain;

namespace RemoteSupport.UnitTests;

public sealed class ClipboardAndChatTests
{
    [Fact]
    public void ClipboardRequiresDirectionScopeAndStopsImmediatelyOnRevoke()
    {
        SessionPermissionGate permissions = new(1, ScopeSet.From(CapabilityScope.SyncClipboardTextHostToOperator));
        ClipboardSyncCoordinator clipboard = new(SessionPeerRole.Host, permissions, new ClipboardSyncPolicy(1024));

        Assert.NotNull(clipboard.CreateOffer("allowed"));
        permissions.Replace(2, ScopeSet.Empty);

        DataFeatureException exception = Assert.Throws<DataFeatureException>(() => clipboard.CreateOffer("blocked-canary"));
        Assert.Equal("CLIPBOARD_POLICY_BLOCKED", exception.Code.Value);
    }

    [Fact]
    public void ClipboardAcceptsOnlyBoundedTextAndSuppressesEchoLoop()
    {
        SessionPermissionGate permissions = new(7, ScopeSet.From(CapabilityScope.SyncClipboardTextHostToOperator,
            CapabilityScope.SyncClipboardTextOperatorToHost));
        ClipboardSyncCoordinator receiver = new(SessionPeerRole.Operator, permissions, new ClipboardSyncPolicy(64));
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("안녕하세요 👋"));
        ClipboardOfferData offer = new(Guid.NewGuid(), ClipboardSyncPolicy.TextContentType, 20, hash, 11);

        ClipboardDecisionData decision = receiver.EvaluateOffer(offer, SessionPeerRole.Host, 7);
        Assert.True(decision.Accepted);
        ClipboardApplyResult result = receiver.Apply(new ClipboardTextData(offer.OfferId, "안녕하세요 👋", hash, 11),
            SessionPeerRole.Host, 7);

        Assert.True(result.Applied);
        Assert.Null(receiver.CreateOffer("안녕하세요 👋"));
        ClipboardDecisionData rich = receiver.EvaluateOffer(offer with { OfferId = Guid.NewGuid(), ContentType = "text/html" },
            SessionPeerRole.Host, 7);
        Assert.False(rich.Accepted);
        Assert.Equal("CLIPBOARD_POLICY_BLOCKED", rich.ReasonCode);
    }

    [Fact]
    public void ClipboardRejectsContentThatDoesNotMatchOffer()
    {
        SessionPermissionGate permissions = new(1, ScopeSet.From(CapabilityScope.SyncClipboardTextOperatorToHost));
        ClipboardSyncCoordinator receiver = new(SessionPeerRole.Host, permissions, new ClipboardSyncPolicy());
        byte[] hash = SHA256.HashData("safe"u8);
        ClipboardOfferData offer = new(Guid.NewGuid(), ClipboardSyncPolicy.TextContentType, 4, hash, 1);
        Assert.True(receiver.EvaluateOffer(offer, SessionPeerRole.Operator, 1).Accepted);

        DataFeatureException exception = Assert.Throws<DataFeatureException>(() => receiver.Apply(
            new ClipboardTextData(offer.OfferId, "evil", hash, 1), SessionPeerRole.Operator, 1));
        Assert.Equal("CLIPBOARD_CONTENT_MISMATCH", exception.Code.Value);
    }

    [Fact]
    public void ChatIsIdempotentAndScopeBound()
    {
        SessionPermissionGate permissions = new(4, ScopeSet.From(CapabilityScope.Chat));
        PeerChatService chat = new(SessionPeerRole.Host, permissions, 128);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        PeerChatMessage message = new(Guid.NewGuid(), "동일 메시지", now);

        Assert.True(chat.Receive(message, SessionPeerRole.Operator, 4, now).Delivered);
        Assert.False(chat.Receive(message, SessionPeerRole.Operator, 4, now).Delivered);
        permissions.Replace(5, ScopeSet.Empty);
        Assert.Equal("AUTHZ_SCOPE_DENIED", Assert.Throws<DataFeatureException>(() =>
            chat.Receive(message with { MessageId = Guid.NewGuid() }, SessionPeerRole.Operator, 5, now)).Code.Value);
    }
}
