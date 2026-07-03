using System.Security.Cryptography;
using System.Text;

namespace RemoteSupport.Application;

public sealed record ClipboardSyncPolicy(int MaximumUtf8Bytes = 256 * 1024)
{
    public const string TextContentType = "text/plain;charset=utf-8";

    public void Validate()
    {
        if (MaximumUtf8Bytes is < 1 or > 256 * 1024) throw new ArgumentOutOfRangeException(nameof(MaximumUtf8Bytes));
    }
}

public sealed record ClipboardOfferData(
    Guid OfferId,
    string ContentType,
    int Utf8Size,
    byte[] Sha256,
    ulong ClipboardRevision);

public sealed record ClipboardTextData(
    Guid OfferId,
    string Text,
    byte[] Sha256,
    ulong ClipboardRevision);

public sealed record ClipboardDecisionData(Guid OfferId, bool Accepted, string ReasonCode, int MaximumAcceptedBytes);

public sealed record ClipboardApplyResult(bool Applied, bool SuppressedAsLoop, ulong ClipboardRevision);

public sealed class ClipboardSyncCoordinator
{
    private const int RememberedOfferLimit = 1024;
    private readonly SessionPeerRole localRole;
    private readonly SessionPermissionGate permissions;
    private readonly ClipboardSyncPolicy policy;
    private readonly Dictionary<Guid, ClipboardOfferData> acceptedOffers = [];
    private readonly Queue<Guid> acceptedOrder = [];
    private byte[]? lastRemoteHash;
    private ulong localRevision;

    public ClipboardSyncCoordinator(SessionPeerRole localRole, SessionPermissionGate permissions, ClipboardSyncPolicy policy)
    {
        this.localRole = localRole;
        this.permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        this.policy = policy ?? throw new ArgumentNullException(nameof(policy));
        policy.Validate();
    }

    public ClipboardOfferData? CreateOffer(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        byte[] utf8 = StrictUtf8(text);
        PermissionSnapshot snapshot = permissions.Current;
        permissions.Demand(SessionPermissionGate.ClipboardScope(localRole), snapshot.Revision, "CLIPBOARD_POLICY_BLOCKED");
        if (utf8.Length > policy.MaximumUtf8Bytes)
        {
            throw new DataFeatureException("CLIPBOARD_POLICY_BLOCKED", "Clipboard text exceeds the configured limit.");
        }
        byte[] hash = SHA256.HashData(utf8);
        if (lastRemoteHash is not null && CryptographicOperations.FixedTimeEquals(hash, lastRemoteHash)) return null;
        ulong revision = checked(++localRevision);
        return new ClipboardOfferData(Guid.CreateVersion7(), ClipboardSyncPolicy.TextContentType, utf8.Length, hash, revision);
    }

    public ClipboardDecisionData EvaluateOffer(ClipboardOfferData offer, SessionPeerRole sourceRole, ulong permissionRevision)
    {
        ArgumentNullException.ThrowIfNull(offer);
        try
        {
            EnsureOppositeRole(sourceRole);
            permissions.Demand(SessionPermissionGate.ClipboardScope(sourceRole), permissionRevision, "CLIPBOARD_POLICY_BLOCKED");
            if (offer.OfferId == Guid.Empty || offer.ContentType != ClipboardSyncPolicy.TextContentType ||
                offer.Utf8Size < 0 || offer.Utf8Size > policy.MaximumUtf8Bytes || offer.Sha256.Length != 32 ||
                offer.ClipboardRevision == 0)
            {
                throw new DataFeatureException("CLIPBOARD_POLICY_BLOCKED", "Clipboard offer is unsupported or outside policy.");
            }
            Remember(offer);
            return new ClipboardDecisionData(offer.OfferId, true, string.Empty, policy.MaximumUtf8Bytes);
        }
        catch (DataFeatureException exception)
        {
            return new ClipboardDecisionData(offer.OfferId, false, exception.Code.Value, policy.MaximumUtf8Bytes);
        }
    }

    public ClipboardApplyResult Apply(ClipboardTextData content, SessionPeerRole sourceRole, ulong permissionRevision)
    {
        ArgumentNullException.ThrowIfNull(content);
        EnsureOppositeRole(sourceRole);
        permissions.Demand(SessionPermissionGate.ClipboardScope(sourceRole), permissionRevision, "CLIPBOARD_POLICY_BLOCKED");
        if (!acceptedOffers.Remove(content.OfferId, out ClipboardOfferData? offer))
        {
            throw new DataFeatureException("CLIPBOARD_CONTENT_MISMATCH", "Clipboard content has no accepted offer.");
        }
        byte[] utf8 = StrictUtf8(content.Text);
        byte[] actualHash = SHA256.HashData(utf8);
        if (content.Sha256.Length != 32 || utf8.Length != offer.Utf8Size ||
            content.ClipboardRevision != offer.ClipboardRevision ||
            !CryptographicOperations.FixedTimeEquals(content.Sha256, offer.Sha256) ||
            !CryptographicOperations.FixedTimeEquals(actualHash, offer.Sha256))
        {
            throw new DataFeatureException("CLIPBOARD_CONTENT_MISMATCH", "Clipboard content does not match its accepted offer.");
        }
        bool loop = lastRemoteHash is not null && CryptographicOperations.FixedTimeEquals(actualHash, lastRemoteHash);
        lastRemoteHash = actualHash;
        return new ClipboardApplyResult(!loop, loop, content.ClipboardRevision);
    }

    private void Remember(ClipboardOfferData offer)
    {
        if (!acceptedOffers.TryAdd(offer.OfferId, offer)) return;
        acceptedOrder.Enqueue(offer.OfferId);
        while (acceptedOrder.Count > RememberedOfferLimit)
        {
            acceptedOffers.Remove(acceptedOrder.Dequeue());
        }
    }

    private void EnsureOppositeRole(SessionPeerRole sourceRole)
    {
        if (sourceRole == localRole)
            throw new DataFeatureException("CLIPBOARD_POLICY_BLOCKED", "Clipboard direction is invalid.");
    }

    private static byte[] StrictUtf8(string text)
    {
        try
        {
            return new UTF8Encoding(false, true).GetBytes(text);
        }
        catch (EncoderFallbackException exception)
        {
            throw new DataFeatureException("CLIPBOARD_CONTENT_MISMATCH", $"Clipboard text is not valid Unicode: {exception.GetType().Name}.");
        }
    }
}
