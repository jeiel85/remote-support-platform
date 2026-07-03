using System.Buffers;
using System.Net.WebSockets;
using System.Text.Json;

namespace RemoteSupport.Infrastructure;

public sealed record SignalingMessage(string Type, Guid PeerId, long Sequence, JsonElement Payload);

public sealed class SignalingClient : IAsyncDisposable
{
    private readonly ClientWebSocket socket = new();
    private readonly SemaphoreSlim sendGate = new(1, 1);
    private readonly Dictionary<Guid, long> receivedSequences = [];
    private readonly CancellationTokenSource lifetime = new();
    private readonly TaskCompletionSource helloAcknowledged = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private PeerAuthorizationResponse? authorization;
    private Task? receiveLoop;
    private long sendSequence;

    public event Func<SignalingMessage, Task>? MessageReceived;

    public async Task ConnectAsync(SignalingTicketResponse ticket, PeerAuthorizationResponse peer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(peer);
        if (ticket.ExpiresAt <= DateTimeOffset.UtcNow || ticket.TransportEpoch != peer.TransportEpoch ||
            ticket.SignalingUrl.Scheme != "wss" || !string.IsNullOrEmpty(ticket.SignalingUrl.Query))
            throw new ControlPlaneClientException("AUTH_SIGNALING_TICKET_INVALID", "Signaling ticket binding is invalid.", 0);
        authorization = peer;
        socket.Options.AddSubProtocol("rsp.signaling.v1");
        Uri target = new UriBuilder(ticket.SignalingUrl) { Query = "ticket=" + Uri.EscapeDataString(ticket.Ticket) }.Uri;
        await socket.ConnectAsync(target, cancellationToken).ConfigureAwait(false);
        receiveLoop = ReceiveLoopAsync(lifetime.Token);
        await SendAsync("HELLO", new
        {
            appVersion = ProductVersion.Current,
            protocolMin = 1,
            protocolMax = 1,
            capabilities = ProductCapabilities.Names,
        }, cancellationToken).ConfigureAwait(false);
        await helloAcknowledged.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
    }

    public Task SendDescriptionAsync(string type, string sdp, CancellationToken cancellationToken = default)
    {
        if (type is not ("SDP_OFFER" or "SDP_ANSWER") || string.IsNullOrEmpty(sdp) || sdp.Length > 32_768)
            throw new ArgumentException("SDP signaling message is invalid.");
        return SendAsync(type, new { sdp }, cancellationToken);
    }

    public Task SendCandidateAsync(string candidate, string? sdpMid, int? sdpMLineIndex,
        CancellationToken cancellationToken = default)
    {
        if (candidate.Length is < 16 or > 2048) throw new ArgumentException("ICE candidate is invalid.", nameof(candidate));
        return SendAsync("ICE_CANDIDATE", new { candidate, sdpMid, sdpMLineIndex }, cancellationToken);
    }

    public Task SendIceCompleteAsync(CancellationToken cancellationToken = default) =>
        SendAsync("ICE_COMPLETE", new { }, cancellationToken);

    public Task SendSessionEndAsync(string reasonCode, CancellationToken cancellationToken = default) =>
        SendAsync("SESSION_END", new { reasonCode }, cancellationToken);

    private async Task SendAsync(string type, object payload, CancellationToken cancellationToken)
    {
        PeerAuthorizationResponse peer = authorization ?? throw new InvalidOperationException("Signaling is not connected.");
        byte[] message = JsonSerializer.SerializeToUtf8Bytes(new
        {
            protocolVersion = 1,
            messageId = Guid.CreateVersion7(),
            sessionId = peer.SessionId,
            peerId = peer.PeerId,
            epoch = peer.TransportEpoch,
            sequence = Interlocked.Increment(ref sendSequence),
            sentAt = DateTimeOffset.UtcNow,
            type,
            payload,
        });
        await sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await socket.SendAsync(message, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            sendGate.Release();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(8192);
        ArrayBufferWriter<byte> message = new();
        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                ValueWebSocketReceiveResult result = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) break;
                if (result.MessageType != WebSocketMessageType.Text || message.WrittenCount + result.Count > 65_536)
                    throw new ControlPlaneClientException("SIGNAL_PROTOCOL_INVALID", "Signaling message framing is invalid.", 0);
                message.Write(buffer.AsSpan(0, result.Count));
                if (!result.EndOfMessage) continue;
                SignalingMessage parsed = Parse(message.WrittenSpan);
                message = new ArrayBufferWriter<byte>();
                if (parsed.Type == "HELLO_ACK") helloAcknowledged.TrySetResult();
                else if (parsed.Type == "ERROR")
                {
                    string code = parsed.Payload.TryGetProperty("code", out JsonElement value) ? value.GetString() ?? "SIGNAL_PROTOCOL_INVALID" : "SIGNAL_PROTOCOL_INVALID";
                    throw new ControlPlaneClientException(code, "Signaling server reported an error.", 0);
                }
                else if (MessageReceived is { } handler) await handler(parsed).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            helloAcknowledged.TrySetException(exception);
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private SignalingMessage Parse(ReadOnlySpan<byte> utf8)
    {
        PeerAuthorizationResponse peer = authorization ?? throw new InvalidOperationException();
        using JsonDocument document = JsonDocument.Parse(utf8.ToArray(), new JsonDocumentOptions { MaxDepth = 12 });
        JsonElement root = document.RootElement;
        Guid sessionId = root.GetProperty("sessionId").GetGuid();
        Guid peerId = root.GetProperty("peerId").GetGuid();
        long epoch = root.GetProperty("epoch").GetInt64();
        long sequence = root.GetProperty("sequence").GetInt64();
        string type = root.GetProperty("type").GetString() ?? string.Empty;
        if (root.GetProperty("protocolVersion").GetInt32() != 1 || sessionId != peer.SessionId || epoch != peer.TransportEpoch ||
            peerId != peer.PeerId && peerId != peer.RemotePeerId || sequence <= 0 || type.Length is < 1 or > 64)
            throw new ControlPlaneClientException("SIGNAL_PROTOCOL_INVALID", "Signaling message identity is invalid.", 0);
        if (receivedSequences.TryGetValue(peerId, out long previous) && sequence <= previous)
            throw new ControlPlaneClientException("SIGNAL_SEQUENCE_INVALID", "Signaling sequence was replayed.", 0);
        receivedSequences[peerId] = sequence;
        return new SignalingMessage(type, peerId, sequence, root.GetProperty("payload").Clone());
    }

    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        try
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "CLIENT_SHUTDOWN", CancellationToken.None).ConfigureAwait(false);
            if (receiveLoop is not null) await receiveLoop.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is WebSocketException or ControlPlaneClientException or
                                          OperationCanceledException or ObjectDisposedException)
        {
        }
        finally
        {
            socket.Dispose();
            sendGate.Dispose();
            lifetime.Dispose();
        }
    }
}
