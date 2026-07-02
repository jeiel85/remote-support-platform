using System.Buffers;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace RemoteSupport.Server;

internal sealed record ValidatedSignalingMessage(string Type, long Sequence, string? Nonce, byte[] RelayBytes);

internal sealed class SignalingProtocolValidator(SignalingTicketService tickets, ISystemClock clock)
{
    private static readonly HashSet<string> EnvelopeFields =
    [
        "protocolVersion", "messageId", "sessionId", "peerId", "epoch", "sequence", "sentAt", "type", "payload",
    ];
    private static readonly HashSet<string> PeerTypes =
    [
        "HELLO", "SDP_OFFER", "SDP_ANSWER", "ICE_CANDIDATE", "ICE_COMPLETE", "PEER_CAPABILITIES",
        "SESSION_END", "PING", "PONG",
    ];

    public ValidatedSignalingMessage Validate(ReadOnlySpan<byte> utf8, SignalingConnectionBinding binding)
    {
        if (utf8.Length is < 2 or > 65_536) throw Invalid("SIGNAL_MESSAGE_SIZE_INVALID");
        try
        {
            using JsonDocument document = JsonDocument.Parse(utf8.ToArray(), new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 12,
            });
            JsonElement root = document.RootElement;
            RequireExactProperties(root, EnvelopeFields);
            if (root.GetProperty("protocolVersion").GetInt32() != 1) throw Invalid("SIGNAL_PROTOCOL_VERSION");
            Guid messageId = root.GetProperty("messageId").GetGuid();
            if (!IsVersion7(messageId)) throw Invalid("SIGNAL_MESSAGE_ID_INVALID");
            if (root.GetProperty("sessionId").GetGuid() != binding.SessionId ||
                root.GetProperty("peerId").GetGuid() != binding.PeerId ||
                root.GetProperty("epoch").GetInt64() != binding.TransportEpoch)
                throw Invalid("SIGNAL_IDENTITY_MISMATCH");
            long sequence = root.GetProperty("sequence").GetInt64();
            if (sequence <= 0) throw Invalid("SIGNAL_SEQUENCE_INVALID");
            DateTimeOffset sentAt = root.GetProperty("sentAt").GetDateTimeOffset();
            if (sentAt.Offset != TimeSpan.Zero || sentAt < clock.UtcNow - TimeSpan.FromMinutes(5) ||
                sentAt > clock.UtcNow + TimeSpan.FromSeconds(30))
                throw Invalid("SIGNAL_TIMESTAMP_INVALID");
            string type = root.GetProperty("type").GetString() ?? string.Empty;
            if (!PeerTypes.Contains(type)) throw Invalid("SIGNAL_TYPE_INVALID");
            JsonElement payload = root.GetProperty("payload");
            ValidatePayload(type, payload, binding.Role);
            tickets.AcceptSequence(binding, sequence, type == "ICE_CANDIDATE");
            string? nonce = type is "PING" or "PONG" ? payload.GetProperty("nonce").GetString() : null;
            return new ValidatedSignalingMessage(type, sequence, nonce,
                WriteRelay(messageId, binding, sequence, sentAt, type, payload));
        }
        catch (ControlPlaneException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException or
                                          KeyNotFoundException or ArgumentException)
        {
            throw Invalid("SIGNAL_MESSAGE_INVALID");
        }
    }

    private static void ValidatePayload(string type, JsonElement payload, string role)
    {
        if (payload.ValueKind != JsonValueKind.Object) throw Invalid("SIGNAL_PAYLOAD_INVALID");
        switch (type)
        {
            case "HELLO":
                RequireExactProperties(payload, ["appVersion", "protocolMin", "protocolMax", "capabilities"]);
                string appVersion = payload.GetProperty("appVersion").GetString() ?? string.Empty;
                int minimum = payload.GetProperty("protocolMin").GetInt32();
                int maximum = payload.GetProperty("protocolMax").GetInt32();
                JsonElement capabilities = payload.GetProperty("capabilities");
                if (appVersion.Length is < 1 or > 64 || minimum < 1 || maximum < minimum || maximum > 32 ||
                    minimum > 1 || capabilities.ValueKind != JsonValueKind.Array || capabilities.GetArrayLength() > 128 ||
                    capabilities.EnumerateArray().Any(value => value.ValueKind != JsonValueKind.String ||
                        (value.GetString()?.Length ?? 0) is < 1 or > 64))
                    throw Invalid("SIGNAL_HELLO_INVALID");
                break;
            case "SDP_OFFER":
            case "SDP_ANSWER":
                if ((type == "SDP_OFFER" && role != "OPERATOR") || (type == "SDP_ANSWER" && role != "HOST"))
                    throw Invalid("SIGNAL_NEGOTIATION_ROLE_INVALID");
                RequireExactProperties(payload, ["sdp"]);
                ValidateSdp(payload.GetProperty("sdp").GetString());
                break;
            case "ICE_CANDIDATE":
                ValidateCandidatePayload(payload);
                break;
            case "ICE_COMPLETE":
                RequireExactProperties(payload, []);
                break;
            case "PING":
            case "PONG":
                RequireExactProperties(payload, ["nonce"]);
                string nonce = payload.GetProperty("nonce").GetString() ?? string.Empty;
                if (nonce.Length is < 8 or > 64 || nonce.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
                    throw Invalid("SIGNAL_PING_INVALID");
                break;
            case "SESSION_END":
                RequireExactProperties(payload, ["reasonCode"]);
                string reason = payload.GetProperty("reasonCode").GetString() ?? string.Empty;
                if (reason.Length is < 1 or > 64 || reason.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
                    throw Invalid("SIGNAL_SESSION_END_INVALID");
                break;
            case "PEER_CAPABILITIES":
                if (payload.EnumerateObject().Count() > 32) throw Invalid("SIGNAL_CAPABILITIES_INVALID");
                break;
            default:
                throw Invalid("SIGNAL_TYPE_INVALID");
        }
    }

    private static void ValidateSdp(string? sdp)
    {
        if (string.IsNullOrEmpty(sdp) || sdp.Length > 32_768 || sdp.Contains('\0') ||
            !sdp.StartsWith("v=0\r\n", StringComparison.Ordinal) || sdp.Split("\r\n").Length > 512 ||
            sdp.Replace("\r\n", string.Empty, StringComparison.Ordinal).IndexOfAny(['\r', '\n']) >= 0)
            throw Invalid("SIGNAL_SDP_INVALID");
        string[] fingerprints = sdp.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("a=fingerprint:", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (fingerprints.Length is < 1 or > 4 || fingerprints.Any(line => !ValidFingerprint(line)))
            throw Invalid("SIGNAL_SDP_FINGERPRINT_INVALID");
    }

    private static bool ValidFingerprint(string line)
    {
        const string prefix = "a=fingerprint:sha-256 ";
        if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        string value = line[prefix.Length..];
        if (value.Length != 95) return false;
        for (int index = 0; index < value.Length; index++)
        {
            if (index % 3 == 2)
            {
                if (value[index] != ':') return false;
            }
            else if (!Uri.IsHexDigit(value[index])) return false;
        }
        return true;
    }

    private static void ValidateCandidatePayload(JsonElement payload)
    {
        HashSet<string> allowed = ["candidate", "sdpMid", "sdpMLineIndex"];
        if (payload.EnumerateObject().GroupBy(property => property.Name, StringComparer.Ordinal).Any(group => group.Count() != 1) ||
            payload.EnumerateObject().Any(property => !allowed.Contains(property.Name)) ||
            !payload.TryGetProperty("candidate", out JsonElement candidateProperty))
            throw Invalid("SIGNAL_ICE_INVALID");
        string candidate = candidateProperty.GetString() ?? string.Empty;
        if (candidate.Length is < 16 or > 2048 || candidate.IndexOfAny(['\0', '\r', '\n']) >= 0)
            throw Invalid("SIGNAL_ICE_INVALID");
        string[] fields = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 8 || !fields[0].StartsWith("candidate:", StringComparison.Ordinal) ||
            fields[0].Length > 42 || !int.TryParse(fields[1], out int component) || component is < 1 or > 2 ||
            fields[2] is not ("udp" or "UDP" or "tcp" or "TCP") || !uint.TryParse(fields[3], out _) ||
            fields[4].Length is < 1 or > 255 || fields[4].Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '.' and not ':' and not '-' and not '_') ||
            !ushort.TryParse(fields[5], out ushort port) || port == 0 || fields[6] != "typ" ||
            fields[7] is not ("host" or "srflx" or "prflx" or "relay"))
            throw Invalid("SIGNAL_ICE_INVALID");
        if (payload.TryGetProperty("sdpMid", out JsonElement mid) && mid.ValueKind != JsonValueKind.Null)
        {
            string value = mid.GetString() ?? string.Empty;
            if (value.Length > 64 || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
                throw Invalid("SIGNAL_ICE_INVALID");
        }
        if (payload.TryGetProperty("sdpMLineIndex", out JsonElement mline) && mline.ValueKind != JsonValueKind.Null &&
            mline.GetInt32() is < 0 or > 16) throw Invalid("SIGNAL_ICE_INVALID");
    }

    private static void RequireExactProperties(JsonElement element, IEnumerable<string> expected)
    {
        string[] expectedArray = expected.Order(StringComparer.Ordinal).ToArray();
        string[] actual = element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();
        if (!actual.SequenceEqual(expectedArray, StringComparer.Ordinal)) throw Invalid("SIGNAL_FIELDS_INVALID");
    }

    private static byte[] WriteRelay(Guid messageId, SignalingConnectionBinding binding, long sequence,
        DateTimeOffset sentAt, string type, JsonElement payload)
    {
        ArrayBufferWriter<byte> buffer = new();
        using Utf8JsonWriter writer = new(buffer);
        writer.WriteStartObject();
        writer.WriteNumber("protocolVersion", 1);
        writer.WriteString("messageId", messageId);
        writer.WriteString("sessionId", binding.SessionId);
        writer.WriteString("peerId", binding.PeerId);
        writer.WriteNumber("epoch", binding.TransportEpoch);
        writer.WriteNumber("sequence", sequence);
        writer.WriteString("sentAt", sentAt);
        writer.WriteString("type", type);
        writer.WritePropertyName("payload");
        payload.WriteTo(writer);
        writer.WriteEndObject();
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static bool IsVersion7(Guid value)
    {
        if (value == Guid.Empty) return false;
        Span<byte> bytes = stackalloc byte[16];
        value.TryWriteBytes(bytes, bigEndian: true, out _);
        return (bytes[6] >> 4) == 7;
    }

    private static ControlPlaneException Invalid(string code) =>
        new(400, code, "Signaling message was invalid.");
}

internal sealed class SignalingHub(SignalingProtocolValidator validator, ISystemClock clock)
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Connection>> sessions = new();

    public async Task RunAsync(WebSocket socket, SignalingConnectionBinding binding, CancellationToken cancellationToken)
    {
        ConcurrentDictionary<Guid, Connection> peers = sessions.GetOrAdd(binding.SessionId, _ => new());
        Connection connection = new(socket, binding);
        if (peers.TryGetValue(binding.PeerId, out Connection? prior))
        {
            peers[binding.PeerId] = connection;
            await prior.CloseAsync(WebSocketCloseStatus.PolicyViolation, "SIGNAL_CONNECTION_REPLACED", cancellationToken);
        }
        else if (!peers.TryAdd(binding.PeerId, connection))
        {
            await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "SIGNAL_CONNECTION_CONFLICT", cancellationToken);
            return;
        }

        try
        {
            bool hello = false;
            Queue<DateTimeOffset> messageWindow = new();
            Queue<DateTimeOffset> iceWindow = new();
            int iceTotal = 0;
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                byte[]? bytes = await ReceiveAsync(socket, cancellationToken);
                if (bytes is null) break;
                EnforceRate(messageWindow, 120, TimeSpan.FromMinutes(1));
                ValidatedSignalingMessage message = validator.Validate(bytes, binding);
                if (!hello && message.Type != "HELLO") throw new ControlPlaneException(400, "SIGNAL_HELLO_REQUIRED", "HELLO is required.");
                if (message.Type == "HELLO")
                {
                    if (hello) throw new ControlPlaneException(409, "SIGNAL_HELLO_REPLAYED", "HELLO was already received.");
                    hello = true;
                    await connection.SendServerAsync("HELLO_ACK", new
                    {
                        protocolVersion = 1,
                        heartbeatIntervalSeconds = 20,
                        binding.PermissionRevision,
                        binding.TransportEpoch,
                    }, clock.UtcNow, cancellationToken);
                    continue;
                }
                if (message.Type == "ICE_CANDIDATE")
                {
                    iceTotal++;
                    if (iceTotal > 256) throw new ControlPlaneException(429, "SIGNAL_ICE_LIMIT", "ICE candidate limit was exceeded.");
                    EnforceRate(iceWindow, 30, TimeSpan.FromSeconds(10));
                }
                if (message.Type == "PING")
                {
                    await connection.SendServerAsync("PONG", new { nonce = message.Nonce }, clock.UtcNow, cancellationToken);
                    continue;
                }
                Connection? destination = peers.Values.SingleOrDefault(peer => peer.Binding.PeerId != binding.PeerId);
                if (destination is null || destination.Socket.State != WebSocketState.Open)
                {
                    await connection.SendServerAsync("ERROR", new { code = "SIGNAL_PEER_UNAVAILABLE" }, clock.UtcNow, cancellationToken);
                    continue;
                }
                await destination.SendAsync(message.RelayBytes, cancellationToken);
            }
        }
        catch (ControlPlaneException exception)
        {
            await connection.CloseAsync(exception.StatusCode == 429 ? WebSocketCloseStatus.PolicyViolation : WebSocketCloseStatus.InvalidPayloadData,
                exception.Code, cancellationToken);
        }
        catch (WebSocketException)
        {
            // Socket loss does not mutate the independent WebRTC media transport.
        }
        finally
        {
            peers.TryRemove(new KeyValuePair<Guid, Connection>(binding.PeerId, connection));
            if (peers.IsEmpty) sessions.TryRemove(new KeyValuePair<Guid, ConcurrentDictionary<Guid, Connection>>(binding.SessionId, peers));
            connection.Dispose();
        }
    }

    private void EnforceRate(Queue<DateTimeOffset> window, int limit, TimeSpan duration)
    {
        DateTimeOffset threshold = clock.UtcNow - duration;
        while (window.TryPeek(out DateTimeOffset oldest) && oldest <= threshold) window.Dequeue();
        if (window.Count >= limit) throw new ControlPlaneException(429, "SIGNAL_RATE_LIMIT", "Signaling rate limit was exceeded.");
        window.Enqueue(clock.UtcNow);
    }

    private static async Task<byte[]?> ReceiveAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        ArrayBufferWriter<byte> message = new();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            while (true)
            {
                ValueWebSocketReceiveResult result = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close) return null;
                if (result.MessageType != WebSocketMessageType.Text)
                    throw new ControlPlaneException(400, "SIGNAL_TEXT_REQUIRED", "Text signaling message required.");
                if (message.WrittenCount + result.Count > 65_536)
                    throw new ControlPlaneException(400, "SIGNAL_MESSAGE_TOO_LARGE", "Signaling message was too large.");
                message.Write(buffer.AsSpan(0, result.Count));
                if (result.EndOfMessage) return message.WrittenSpan.ToArray();
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private sealed class Connection(WebSocket socket, SignalingConnectionBinding binding) : IDisposable
    {
        private readonly SemaphoreSlim sendGate = new(1, 1);
        private long serverSequence;
        public WebSocket Socket { get; } = socket;
        public SignalingConnectionBinding Binding { get; } = binding;

        public async Task SendAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
        {
            if (!await sendGate.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken))
                throw new WebSocketException("Signaling send backpressure timeout.");
            try
            {
                if (Socket.State == WebSocketState.Open)
                    await Socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).AsTask()
                        .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }
            finally
            {
                sendGate.Release();
            }
        }

        public Task SendServerAsync(string type, object payload, DateTimeOffset now, CancellationToken cancellationToken)
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                protocolVersion = 1,
                messageId = Guid.CreateVersion7(now),
                sessionId = Binding.SessionId,
                peerId = Binding.PeerId,
                epoch = Binding.TransportEpoch,
                sequence = Interlocked.Increment(ref serverSequence),
                sentAt = now,
                type,
                payload,
            });
            return SendAsync(bytes, cancellationToken);
        }

        public async Task CloseAsync(WebSocketCloseStatus status, string description, CancellationToken cancellationToken)
        {
            try
            {
                if (Socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    await Socket.CloseAsync(status, description[..Math.Min(description.Length, 100)], cancellationToken);
            }
            catch (WebSocketException)
            {
            }
        }

        public void Dispose() => sendGate.Dispose();
    }
}
