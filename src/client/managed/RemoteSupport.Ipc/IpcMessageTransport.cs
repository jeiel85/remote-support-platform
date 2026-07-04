using System.Buffers.Binary;
using System.IO.Pipes;
using Google.Protobuf;
using RemoteSupport.Ipc.V1;

namespace RemoteSupport.Ipc;

/// <summary>
/// Length-prefixed Protobuf framing over a named-pipe stream, per
/// 01-architecture/windows-process-model.md IPC design: messages are
/// length-prefixed with an enforced maximum size bound.
/// </summary>
public sealed class IpcMessageTransport(PipeStream pipe, int maxMessageBytes = 262_144) : IAsyncDisposable
{
    private const int LengthPrefixBytes = 4;
    private readonly byte[] lengthBuffer = new byte[LengthPrefixBytes];

    public async Task SendAsync(IpcEnvelope envelope, CancellationToken cancellationToken)
    {
        byte[] payload = envelope.ToByteArray();
        if (payload.Length > maxMessageBytes)
            throw new InvalidOperationException($"IPC message of {payload.Length} bytes exceeds the {maxMessageBytes}-byte bound.");
        BinaryPrimitives.WriteUInt32BigEndian(lengthBuffer, (uint)payload.Length);
        await pipe.WriteAsync(lengthBuffer, cancellationToken).ConfigureAwait(false);
        await pipe.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IpcEnvelope?> ReceiveAsync(CancellationToken cancellationToken)
    {
        if (!await ReadExactAsync(lengthBuffer, cancellationToken).ConfigureAwait(false)) return null;
        uint length = BinaryPrimitives.ReadUInt32BigEndian(lengthBuffer);
        if (length == 0 || length > maxMessageBytes)
            throw new InvalidDataException($"IPC message length {length} was outside the permitted 1..{maxMessageBytes} range.");
        byte[] payload = new byte[length];
        if (!await ReadExactAsync(payload, cancellationToken).ConfigureAwait(false))
            throw new EndOfStreamException("IPC pipe closed mid-message.");
        return IpcEnvelope.Parser.ParseFrom(payload);
    }

    private async Task<bool> ReadExactAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await pipe.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken).ConfigureAwait(false);
            if (read == 0) return offset == 0 ? false : throw new EndOfStreamException("IPC pipe closed mid-message.");
            offset += read;
        }
        return true;
    }

    public ValueTask DisposeAsync()
    {
        pipe.Dispose();
        return ValueTask.CompletedTask;
    }
}
