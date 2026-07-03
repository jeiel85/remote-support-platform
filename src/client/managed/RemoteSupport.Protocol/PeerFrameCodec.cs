using System.Buffers.Binary;
using Google.Protobuf;
using RemoteSupport.Domain;
using RemoteSupport.Protocol.V1;

namespace RemoteSupport.Protocol;

public enum PeerChannel
{
    Control,
    InputFast,
    InputReliable,
    Clipboard,
    FileControl,
    FileData,
    Chat,
}

public sealed class PeerProtocolException(string code, string message, Exception? innerException = null) : Exception(message, innerException)
{
    public ErrorCode Code { get; } = new(code);
}

public sealed record DecodedPeerFrame(Envelope Envelope, ushort Flags, ulong Sequence);

public static class PeerFrameCodec
{
    public const int HeaderSize = 24;
    public const ushort FramingMajor = 1;

    public static int HardLimit(PeerChannel channel) => channel switch
    {
        PeerChannel.InputReliable => 64 * 1024,
        PeerChannel.InputFast => 8 * 1024,
        PeerChannel.FileData => 1024 * 1024,
        _ => 256 * 1024,
    };

    public static byte[] Encode(Envelope envelope, PeerChannel channel, ushort flags = 0)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ValidateBodyForChannel(envelope.BodyCase, channel);
        byte[] body = envelope.ToByteArray();
        if (body.Length > HardLimit(channel))
        {
            throw Invalid("SIGNAL_MESSAGE_TOO_LARGE", "Peer frame exceeds the channel hard limit.");
        }

        byte[] frame = GC.AllocateUninitializedArray<byte>(HeaderSize + body.Length);
        "RSP1"u8.CopyTo(frame);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4), FramingMajor);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(6), checked((ushort)envelope.BodyCase));
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(8), flags);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(10), 0);
        BinaryPrimitives.WriteUInt64BigEndian(frame.AsSpan(12), envelope.Sequence);
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(20), checked((uint)body.Length));
        body.CopyTo(frame, HeaderSize);
        return frame;
    }

    public static DecodedPeerFrame Decode(ReadOnlySpan<byte> frame, PeerChannel channel, int negotiatedLimit)
    {
        int limit = Math.Min(HardLimit(channel), negotiatedLimit);
        if (limit <= 0 || frame.Length < HeaderSize || !frame[..4].SequenceEqual("RSP1"u8))
        {
            throw Invalid("SIGNAL_PROTOCOL_INVALID", "Peer frame header is invalid.");
        }
        if (BinaryPrimitives.ReadUInt16BigEndian(frame[4..]) != FramingMajor ||
            BinaryPrimitives.ReadUInt16BigEndian(frame[10..]) != 0)
        {
            throw Invalid("SIGNAL_PROTOCOL_INVALID", "Peer framing version or reserved field is invalid.");
        }

        ushort hint = BinaryPrimitives.ReadUInt16BigEndian(frame[6..]);
        ushort flags = BinaryPrimitives.ReadUInt16BigEndian(frame[8..]);
        ulong sequence = BinaryPrimitives.ReadUInt64BigEndian(frame[12..]);
        uint payloadLength = BinaryPrimitives.ReadUInt32BigEndian(frame[20..]);
        if (payloadLength > limit || payloadLength != frame.Length - HeaderSize)
        {
            throw Invalid(payloadLength > limit ? "SIGNAL_MESSAGE_TOO_LARGE" : "SIGNAL_PROTOCOL_INVALID",
                "Peer frame length is invalid.");
        }

        Envelope envelope;
        try
        {
            envelope = Envelope.Parser.ParseFrom(frame[HeaderSize..].ToArray());
        }
        catch (InvalidProtocolBufferException exception)
        {
            throw new PeerProtocolException("SIGNAL_PROTOCOL_INVALID", "Peer protobuf payload is invalid.", exception);
        }

        if (envelope.ProtocolMajor != 1 || envelope.Sequence == 0 || envelope.Sequence != sequence ||
            envelope.BodyCase == Envelope.BodyOneofCase.None || hint != checked((ushort)envelope.BodyCase))
        {
            throw Invalid("SIGNAL_PROTOCOL_INVALID", "Peer envelope and frame header disagree.");
        }
        ValidateBodyForChannel(envelope.BodyCase, channel);
        return new DecodedPeerFrame(envelope, flags, sequence);
    }

    public static void ValidateBodyForChannel(Envelope.BodyOneofCase body, PeerChannel channel)
    {
        bool valid = channel switch
        {
            PeerChannel.Clipboard => body is Envelope.BodyOneofCase.ClipboardOffer or
                Envelope.BodyOneofCase.ClipboardDecision or Envelope.BodyOneofCase.ClipboardText,
            PeerChannel.FileControl => body is Envelope.BodyOneofCase.FileOffer or
                Envelope.BodyOneofCase.FileDecision or Envelope.BodyOneofCase.FileAck or Envelope.BodyOneofCase.FileCancel,
            PeerChannel.FileData => body is Envelope.BodyOneofCase.FileChunk,
            PeerChannel.Chat => body is Envelope.BodyOneofCase.ChatMessage or Envelope.BodyOneofCase.ChatAck,
            PeerChannel.InputFast => body is Envelope.BodyOneofCase.PointerEvent,
            PeerChannel.InputReliable => body is Envelope.BodyOneofCase.PointerEvent or
                Envelope.BodyOneofCase.KeyboardEvent or Envelope.BodyOneofCase.ReleaseAllInput or Envelope.BodyOneofCase.InputAck,
            PeerChannel.Control => body is Envelope.BodyOneofCase.ProtocolHello or Envelope.BodyOneofCase.ProtocolHelloAck or
                Envelope.BodyOneofCase.DisplayTopology or Envelope.BodyOneofCase.SelectDisplay or
                Envelope.BodyOneofCase.SelectDisplayResult or Envelope.BodyOneofCase.PermissionState or
                Envelope.BodyOneofCase.Heartbeat or Envelope.BodyOneofCase.TransportStats or
                Envelope.BodyOneofCase.ProtocolError or Envelope.BodyOneofCase.SessionEnd or
                Envelope.BodyOneofCase.TransportBinding or Envelope.BodyOneofCase.TransportBindingAck,
            _ => false,
        };
        if (!valid) throw Invalid("SIGNAL_PROTOCOL_INVALID", "Message type is not valid on this channel.");
    }

    private static PeerProtocolException Invalid(string code, string message) => new(code, message);

}
