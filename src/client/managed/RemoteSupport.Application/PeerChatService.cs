using System.Text;
using RemoteSupport.Domain;

namespace RemoteSupport.Application;

public sealed record PeerChatMessage(Guid MessageId, string Text, DateTimeOffset SentAt);
public sealed record PeerChatReceipt(Guid MessageId, DateTimeOffset ReceivedAt, bool Delivered);

public sealed class PeerChatService(SessionPeerRole localRole, SessionPermissionGate permissions, int maximumUtf8Bytes = 64 * 1024)
{
    private const int RememberedMessages = 4096;
    private readonly HashSet<Guid> received = [];
    private readonly Queue<Guid> receivedOrder = [];

    public PeerChatMessage Create(string text, DateTimeOffset now)
    {
        ValidateScope(permissions.Current.Revision);
        ValidateText(text);
        return new PeerChatMessage(Guid.CreateVersion7(now), text, now);
    }

    public PeerChatReceipt Receive(PeerChatMessage message, SessionPeerRole sourceRole, ulong permissionRevision, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (sourceRole == localRole) throw new DataFeatureException("AUTHZ_SCOPE_DENIED", "Chat source role is invalid.");
        ValidateScope(permissionRevision);
        ValidateText(message.Text);
        if (message.MessageId == Guid.Empty || message.SentAt > now + TimeSpan.FromMinutes(1) ||
            message.SentAt < now - TimeSpan.FromDays(1))
            throw new DataFeatureException("SIGNAL_PROTOCOL_INVALID", "Chat metadata is invalid.");
        bool delivered = received.Add(message.MessageId);
        if (delivered)
        {
            receivedOrder.Enqueue(message.MessageId);
            while (receivedOrder.Count > RememberedMessages) received.Remove(receivedOrder.Dequeue());
        }
        return new PeerChatReceipt(message.MessageId, now, delivered);
    }

    private void ValidateScope(ulong revision) => permissions.Demand(CapabilityScope.Chat, revision, "AUTHZ_SCOPE_DENIED");

    private void ValidateText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrWhiteSpace(text) || Encoding.UTF8.GetByteCount(text) > maximumUtf8Bytes)
            throw new DataFeatureException("SIGNAL_MESSAGE_TOO_LARGE", "Chat text is empty or exceeds the configured limit.");
    }
}
