using RemoteSupport.Domain;
using RemoteSupport.Protocol.V1;

namespace RemoteSupport.Protocol;

public sealed class PeerProtocolSession(
    Guid sessionId,
    Guid remotePeerId,
    PeerRole remoteRole,
    ulong transportEpoch,
    ulong permissionRevision)
{
    private readonly Dictionary<PeerChannel, ulong> lastSequences = [];
    private readonly object gate = new();
    private bool reciprocalHello;
    private bool transportBindingVerified;

    public void MarkReciprocalHelloComplete() => reciprocalHello = true;
    public void MarkTransportBindingVerified() => transportBindingVerified = true;

    public Envelope Accept(ReadOnlySpan<byte> frame, PeerChannel channel, int negotiatedLimit)
    {
        DecodedPeerFrame decoded = PeerFrameCodec.Decode(frame, channel, negotiatedLimit);
        Envelope envelope = decoded.Envelope;
        lock (gate)
        {
            if (!Guid.TryParse(envelope.SessionId, out Guid parsedSession) || parsedSession != sessionId ||
                !Guid.TryParse(envelope.SenderPeerId, out Guid parsedPeer) || parsedPeer != remotePeerId ||
                envelope.SenderRole != remoteRole || envelope.TransportEpoch != transportEpoch)
            {
                throw Invalid("SESSION_EPOCH_STALE", "Peer envelope identity or transport epoch is stale.");
            }
            bool permissionTransition = envelope.BodyCase == Envelope.BodyOneofCase.PermissionState &&
                remoteRole == PeerRole.Host && envelope.PermissionState.Revision == envelope.PermissionRevision &&
                envelope.PermissionRevision == permissionRevision + 1;
            if (envelope.PermissionRevision != permissionRevision && !permissionTransition)
            {
                throw Invalid("SESSION_PERMISSION_REVISION_STALE", "Peer permission revision is stale.");
            }
            if (lastSequences.TryGetValue(channel, out ulong previous) && envelope.Sequence <= previous)
            {
                throw Invalid("SIGNAL_SEQUENCE_INVALID", "Peer channel sequence was replayed or decreased.");
            }
            bool negotiation = envelope.BodyCase is Envelope.BodyOneofCase.ProtocolHello or
                Envelope.BodyOneofCase.ProtocolHelloAck or Envelope.BodyOneofCase.TransportBinding or
                Envelope.BodyOneofCase.TransportBindingAck;
            if (!reciprocalHello && !negotiation)
            {
                throw Invalid("TRANSPORT_CHANNEL_NEGOTIATION_FAILED", "Protocol hello is incomplete.");
            }
            if (!transportBindingVerified && !negotiation)
            {
                throw Invalid("TRANSPORT_BINDING_REQUIRED", "Transport binding is incomplete.");
            }
            lastSequences[channel] = envelope.Sequence;
            if (permissionTransition) permissionRevision = envelope.PermissionRevision;
            return envelope;
        }
    }

    private static PeerProtocolException Invalid(string code, string message) => new(code, message);
}
