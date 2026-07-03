using Google.Protobuf;
using RemoteSupport.Application;
using RemoteSupport.Protocol;
using RemoteSupport.Protocol.V1;
using System.Runtime.Versioning;

namespace RemoteSupport.Infrastructure;

[SupportedOSPlatform("windows")]
public sealed class PeerClipboardController
{
    private readonly PeerDataSession protocol;
    private readonly NativeAttendedSession transport;
    private readonly ClipboardSyncCoordinator clipboard;
    private readonly Dictionary<Guid, PendingClipboard> pending = [];

    public PeerClipboardController(PeerDataSession protocol, NativeAttendedSession transport, int maximumUtf8Bytes)
    {
        this.protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        clipboard = new ClipboardSyncCoordinator(protocol.LocalRole, protocol.PermissionGate,
            new ClipboardSyncPolicy(maximumUtf8Bytes));
    }

    public event Action<string>? TextReady;

    public void Offer(string text)
    {
        ClipboardOfferData? offer = clipboard.CreateOffer(text);
        if (offer is null) return;
        pending[offer.OfferId] = new PendingClipboard(text, offer);
        byte[] frame = protocol.Encode(PeerChannel.Clipboard, envelope => envelope.ClipboardOffer = new ClipboardOffer
        {
            OfferId = offer.OfferId.ToString("D"), ContentType = offer.ContentType,
            Utf8Size = checked((ulong)offer.Utf8Size), Sha256 = ByteString.CopyFrom(offer.Sha256),
            ClipboardRevision = offer.ClipboardRevision,
        });
        transport.Send("rsp.clipboard.v1", frame);
    }

    public void Handle(Envelope envelope)
    {
        SessionPeerRole remoteRole = protocol.LocalRole == SessionPeerRole.Host ? SessionPeerRole.Operator : SessionPeerRole.Host;
        switch (envelope.BodyCase)
        {
            case Envelope.BodyOneofCase.ClipboardOffer:
                ClipboardOffer value = envelope.ClipboardOffer;
                ClipboardOfferData offer = new(Guid.Parse(value.OfferId), value.ContentType, checked((int)value.Utf8Size),
                    value.Sha256.ToByteArray(), value.ClipboardRevision);
                ClipboardDecisionData decision = clipboard.EvaluateOffer(offer, remoteRole, envelope.PermissionRevision);
                transport.Send("rsp.clipboard.v1", protocol.Encode(PeerChannel.Clipboard, result => result.ClipboardDecision = new ClipboardDecision
                {
                    OfferId = decision.OfferId.ToString("D"), Accepted = decision.Accepted,
                    ReasonCode = decision.ReasonCode, MaxAcceptedBytes = checked((uint)decision.MaximumAcceptedBytes),
                }));
                break;
            case Envelope.BodyOneofCase.ClipboardDecision:
                ClipboardDecision remoteDecision = envelope.ClipboardDecision;
                Guid offerId = Guid.Parse(remoteDecision.OfferId);
                if (!remoteDecision.Accepted || !pending.Remove(offerId, out PendingClipboard? offered)) return;
                transport.Send("rsp.clipboard.v1", protocol.Encode(PeerChannel.Clipboard, result => result.ClipboardText = new ClipboardText
                {
                    OfferId = offerId.ToString("D"), Text = offered.Text, Sha256 = ByteString.CopyFrom(offered.Offer.Sha256),
                    ClipboardRevision = offered.Offer.ClipboardRevision,
                }));
                break;
            case Envelope.BodyOneofCase.ClipboardText:
                ClipboardText content = envelope.ClipboardText;
                ClipboardApplyResult applied = clipboard.Apply(new ClipboardTextData(Guid.Parse(content.OfferId), content.Text,
                    content.Sha256.ToByteArray(), content.ClipboardRevision), remoteRole, envelope.PermissionRevision);
                if (applied.Applied) TextReady?.Invoke(content.Text);
                break;
        }
    }

    private sealed record PendingClipboard(string Text, ClipboardOfferData Offer);
}
