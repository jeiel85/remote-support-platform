using RemoteSupport.Protocol;
using RemoteSupport.Protocol.V1;

namespace RemoteSupport.UnitTests;

public sealed class PeerFrameCodecTests
{
    [Fact]
    public void FrameRoundTripsAndRejectsWrongChannel()
    {
        Envelope envelope = EnvelopeFor(1);
        envelope.ChatMessage = new ChatMessage { ChatMessageId = Guid.NewGuid().ToString("D"), Text = "hello" };
        byte[] frame = PeerFrameCodec.Encode(envelope, PeerChannel.Chat);

        Assert.Equal(envelope, PeerFrameCodec.Decode(frame, PeerChannel.Chat, 256 * 1024).Envelope);
        Assert.Equal("SIGNAL_PROTOCOL_INVALID", Assert.Throws<PeerProtocolException>(() =>
            PeerFrameCodec.Decode(frame, PeerChannel.Clipboard, 256 * 1024)).Code.Value);
    }

    [Fact]
    public void FrameRejectsTrailingTruncatedAndMismatchedHint()
    {
        Envelope envelope = EnvelopeFor(1);
        envelope.ChatAck = new ChatAck { ChatMessageId = Guid.NewGuid().ToString("D") };
        byte[] frame = PeerFrameCodec.Encode(envelope, PeerChannel.Chat);
        Assert.Throws<PeerProtocolException>(() => PeerFrameCodec.Decode([.. frame, 0], PeerChannel.Chat, 256 * 1024));
        Assert.Throws<PeerProtocolException>(() => PeerFrameCodec.Decode(frame.AsSpan(0, frame.Length - 1), PeerChannel.Chat, 256 * 1024));
        frame[7] ^= 1;
        Assert.Throws<PeerProtocolException>(() => PeerFrameCodec.Decode(frame, PeerChannel.Chat, 256 * 1024));
    }

    [Fact]
    public void SessionRequiresBindingAndMonotonicSequence()
    {
        Guid session = Guid.NewGuid();
        Guid peer = Guid.NewGuid();
        PeerProtocolSession state = new(session, peer, PeerRole.Operator, 2, 3);
        Envelope envelope = EnvelopeFor(1, session, peer, PeerRole.Operator, 2, 3);
        envelope.ChatMessage = new ChatMessage { ChatMessageId = Guid.NewGuid().ToString("D"), Text = "bound" };
        byte[] frame = PeerFrameCodec.Encode(envelope, PeerChannel.Chat);
        Assert.Equal("TRANSPORT_CHANNEL_NEGOTIATION_FAILED", Assert.Throws<PeerProtocolException>(() =>
            state.Accept(frame, PeerChannel.Chat, 256 * 1024)).Code.Value);
        state.MarkReciprocalHelloComplete();
        Assert.Equal("TRANSPORT_BINDING_REQUIRED", Assert.Throws<PeerProtocolException>(() =>
            state.Accept(frame, PeerChannel.Chat, 256 * 1024)).Code.Value);
        state.MarkTransportBindingVerified();
        Assert.Equal(envelope, state.Accept(frame, PeerChannel.Chat, 256 * 1024));
        Assert.Equal("SIGNAL_SEQUENCE_INVALID", Assert.Throws<PeerProtocolException>(() =>
            state.Accept(frame, PeerChannel.Chat, 256 * 1024)).Code.Value);
    }

    [Fact]
    public void ParserSurvivesDeterministicMalformedCorpus()
    {
        Random random = new(0x52535031);
        for (int iteration = 0; iteration < 10_000; iteration++)
        {
            byte[] bytes = new byte[random.Next(0, 2048)];
            random.NextBytes(bytes);
            try { _ = PeerFrameCodec.Decode(bytes, (PeerChannel)random.Next(0, 7), 1024); }
            catch (PeerProtocolException) { }
        }
    }

    private static Envelope EnvelopeFor(ulong sequence, Guid? session = null, Guid? peer = null,
        PeerRole role = PeerRole.Host, ulong epoch = 1, ulong revision = 1) => new()
    {
        ProtocolMajor = 1,
        ProtocolMinor = 0,
        MessageId = Guid.NewGuid().ToString("D"),
        SessionId = (session ?? Guid.NewGuid()).ToString("D"),
        SenderPeerId = (peer ?? Guid.NewGuid()).ToString("D"),
        SenderRole = role,
        TransportEpoch = epoch,
        PermissionRevision = revision,
        Sequence = sequence,
    };
}
