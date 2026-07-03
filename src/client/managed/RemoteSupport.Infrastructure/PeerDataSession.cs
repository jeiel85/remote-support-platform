using System.Diagnostics;
using RemoteSupport.Application;
using RemoteSupport.Domain;
using RemoteSupport.Protocol;
using RemoteSupport.Protocol.V1;
using DomainScope = RemoteSupport.Domain.CapabilityScope;

namespace RemoteSupport.Infrastructure;

public sealed class PeerDataSession
{
    private readonly PeerAuthorizationResponse authorization;
    private readonly SessionPeerRole localRole;
    private readonly Dictionary<PeerChannel, ulong> outgoingSequences = [];
    private readonly PeerProtocolSession incoming;
    private readonly object gate = new();

    public PeerDataSession(PeerAuthorizationResponse authorization)
    {
        this.authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        localRole = authorization.Role == "HOST" ? SessionPeerRole.Host : SessionPeerRole.Operator;
        PermissionGate = new SessionPermissionGate(checked((ulong)authorization.PermissionRevision),
            ScopeSet.From(authorization.GrantedScopes.Select(ParseScope).ToArray()));
        incoming = new PeerProtocolSession(authorization.SessionId, authorization.RemotePeerId,
            authorization.RemoteRole == "HOST" ? PeerRole.Host : PeerRole.Operator,
            checked((ulong)authorization.TransportEpoch), checked((ulong)authorization.PermissionRevision));
        incoming.MarkReciprocalHelloComplete();
        incoming.MarkTransportBindingVerified();
    }

    public SessionPermissionGate PermissionGate { get; }
    public SessionPeerRole LocalRole => localRole;

    public byte[] Encode(PeerChannel channel, Action<Envelope> setBody)
    {
        ArgumentNullException.ThrowIfNull(setBody);
        ulong sequence;
        lock (gate)
        {
            outgoingSequences.TryGetValue(channel, out ulong previous);
            sequence = checked(previous + 1);
            outgoingSequences[channel] = sequence;
        }
        Envelope envelope = new()
        {
            ProtocolMajor = 1,
            ProtocolMinor = 0,
            MessageId = Guid.CreateVersion7().ToString("D"),
            SessionId = authorization.SessionId.ToString("D"),
            SenderPeerId = authorization.PeerId.ToString("D"),
            SenderRole = localRole == SessionPeerRole.Host ? PeerRole.Host : PeerRole.Operator,
            TransportEpoch = checked((ulong)authorization.TransportEpoch),
            PermissionRevision = PermissionGate.Current.Revision,
            Sequence = sequence,
            MonotonicTimestampNs = checked((long)(Stopwatch.GetTimestamp() * (1_000_000_000d / Stopwatch.Frequency))),
            CorrelationId = authorization.SessionId.ToString("N"),
        };
        setBody(envelope);
        return PeerFrameCodec.Encode(envelope, channel);
    }

    public Envelope Decode(string channelLabel, ReadOnlySpan<byte> frame)
    {
        PeerChannel channel = Channel(channelLabel);
        Envelope envelope = incoming.Accept(frame, channel, PeerFrameCodec.HardLimit(channel));
        if (envelope.BodyCase == Envelope.BodyOneofCase.PermissionState) ApplyRemotePermissionState(envelope.PermissionState);
        return envelope;
    }

    public void RestrictLocalPermissions(IReadOnlyCollection<string> activeScopes) =>
        PermissionGate.RestrictCurrent(ScopeSet.From(activeScopes.Select(ParseScope).ToArray()));

    public void ApplyLocalPermissionRevision(ulong revision, IReadOnlyCollection<string> activeScopes)
    {
        ScopeSet next = ScopeSet.From(activeScopes.Select(ParseScope).ToArray());
        PermissionSnapshot current = PermissionGate.Current;
        if (revision != current.Revision + 1 || next.Values.Except(current.ActiveScopes.Values).Any())
            throw new DataFeatureException("PERMISSION_STATE_INVALID", "Local permission update was not a monotonic scope reduction.");
        PermissionGate.Replace(revision, next);
    }

    private void ApplyRemotePermissionState(PermissionState state)
    {
        PermissionSnapshot current = PermissionGate.Current;
        ScopeSet active = ScopeSet.From(state.ActiveScopes.Select(ParseScope).ToArray());
        ScopeSet revoked = ScopeSet.From(state.RevokedScopes.Select(ParseScope).ToArray());
        DomainScope[] combined = active.Values.Concat(revoked.Values).Distinct().ToArray();
        if (state.Revision != current.Revision + 1 ||
            active.Values.Intersect(revoked.Values).Any() ||
            ScopeSet.From(combined) != current.ActiveScopes ||
            state.ReasonCode.Length is < 1 or > 64)
            throw new DataFeatureException("PERMISSION_STATE_INVALID", "Remote permission update was not a monotonic scope reduction.");
        PermissionGate.Replace(state.Revision, active);
    }

    public static PeerChannel Channel(string label) => label switch
    {
        "rsp.input.fast.v1" => PeerChannel.InputFast,
        "rsp.control.v1" => PeerChannel.Control,
        "rsp.input.reliable.v1" => PeerChannel.InputReliable,
        "rsp.clipboard.v1" => PeerChannel.Clipboard,
        "rsp.file.control.v1" => PeerChannel.FileControl,
        "rsp.file.data.v1" => PeerChannel.FileData,
        "rsp.chat.v1" => PeerChannel.Chat,
        _ => throw new PeerProtocolException("TRANSPORT_CHANNEL_NEGOTIATION_FAILED", "Unknown product data channel."),
    };

    private static DomainScope ParseScope(string scope) => scope switch
    {
        "VIEW_SCREEN" => DomainScope.ViewScreen,
        "CONTROL_POINTER" => DomainScope.ControlPointer,
        "CONTROL_KEYBOARD" => DomainScope.ControlKeyboard,
        "SYNC_CLIPBOARD_TEXT_HOST_TO_OPERATOR" => DomainScope.SyncClipboardTextHostToOperator,
        "SYNC_CLIPBOARD_TEXT_OPERATOR_TO_HOST" => DomainScope.SyncClipboardTextOperatorToHost,
        "TRANSFER_FILE_HOST_TO_OPERATOR" => DomainScope.TransferFileHostToOperator,
        "TRANSFER_FILE_OPERATOR_TO_HOST" => DomainScope.TransferFileOperatorToHost,
        "CHAT" => DomainScope.Chat,
        "SWITCH_MONITOR" => DomainScope.SwitchMonitor,
        "REQUEST_REBOOT" => DomainScope.RequestReboot,
        "RECONNECT_AFTER_REBOOT" => DomainScope.ReconnectAfterReboot,
        "UNATTENDED_SESSION" => DomainScope.UnattendedSession,
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown authorization scope."),
    };

    private static DomainScope ParseScope(RemoteSupport.Protocol.V1.CapabilityScope scope) => scope switch
    {
        RemoteSupport.Protocol.V1.CapabilityScope.ViewScreen => DomainScope.ViewScreen,
        RemoteSupport.Protocol.V1.CapabilityScope.ControlPointer => DomainScope.ControlPointer,
        RemoteSupport.Protocol.V1.CapabilityScope.ControlKeyboard => DomainScope.ControlKeyboard,
        RemoteSupport.Protocol.V1.CapabilityScope.SyncClipboardTextHostToOperator => DomainScope.SyncClipboardTextHostToOperator,
        RemoteSupport.Protocol.V1.CapabilityScope.SyncClipboardTextOperatorToHost => DomainScope.SyncClipboardTextOperatorToHost,
        RemoteSupport.Protocol.V1.CapabilityScope.TransferFileHostToOperator => DomainScope.TransferFileHostToOperator,
        RemoteSupport.Protocol.V1.CapabilityScope.TransferFileOperatorToHost => DomainScope.TransferFileOperatorToHost,
        RemoteSupport.Protocol.V1.CapabilityScope.Chat => DomainScope.Chat,
        RemoteSupport.Protocol.V1.CapabilityScope.SwitchMonitor => DomainScope.SwitchMonitor,
        RemoteSupport.Protocol.V1.CapabilityScope.RequestReboot => DomainScope.RequestReboot,
        RemoteSupport.Protocol.V1.CapabilityScope.ReconnectAfterReboot => DomainScope.ReconnectAfterReboot,
        RemoteSupport.Protocol.V1.CapabilityScope.UnattendedSession => DomainScope.UnattendedSession,
        _ => throw new DataFeatureException("PERMISSION_STATE_INVALID", "Permission state contained an unknown scope."),
    };
}
