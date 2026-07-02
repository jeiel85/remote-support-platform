using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RemoteSupport.Server;

internal sealed record SignalingConnectionBinding(Guid SessionId, Guid PeerId, string Role,
    string KeyThumbprint, string[] GrantedScopes, long PermissionRevision, long TransportEpoch);

internal sealed class SignalingTicketService(
    IAttendedSessionStore store,
    ControlPlaneCrypto crypto,
    ControlPlaneOptions options,
    ISystemClock clock)
{
    public SignalingTicket Issue(Guid sessionId, PeerAccessContext access, HttpRequest request)
    {
        string ticket = crypto.SignalingRoutingKey(sessionId) + "." + ControlPlaneCrypto.GenerateSecret();
        string hash = crypto.LookupHash("signaling-ticket\0" + ticket);
        DateTimeOffset expiresAt = Min(clock.UtcNow + options.SignalingTicketLifetime, access.TokenExpiresAt);
        long epoch = store.Execute(collection =>
        {
            SessionAggregate session = RequireAuthorized(collection, sessionId, access, clock.UtcNow);
            expiresAt = Min(expiresAt, session.ExpiresAt);
            foreach (string expired in session.SignalingTickets.Where(item =>
                         item.Value.ExpiresAt <= clock.UtcNow || item.Value.Consumed).Select(item => item.Key).ToArray())
                session.SignalingTickets.Remove(expired);
            if (session.SignalingTickets.Count >= 64)
                throw new ControlPlaneException(429, "SIGNAL_TICKET_LIMIT", "Signaling ticket limit was exceeded.");
            session.SignalingTickets.Add(hash, new SignalingTicketRecord(access.PeerId, access.Role,
                access.KeyThumbprint, access.GrantedScopes, access.PermissionRevision, access.TransportEpoch, expiresAt));
            AttendedSessionService.AppendLifecycle(session, "SIGNALING_TICKET_ISSUED", access.Role,
                access.PeerId.ToString("D"), clock.UtcNow, new { access.PeerId, access.TransportEpoch, expiresAt });
            return session.TransportEpoch;
        });
        return new SignalingTicket(ticket, PublicSignalingUrl(request), expiresAt, epoch);
    }

    public SignalingConnectionBinding Consume(string? ticket)
    {
        if (string.IsNullOrWhiteSpace(ticket) || ticket.Length is < 32 or > 128)
            throw new ControlPlaneException(401, "SIGNAL_TICKET_INVALID", "Signaling authentication failed.");
        string hash = crypto.LookupHash("signaling-ticket\0" + ticket);
        return store.Execute(collection =>
        {
            SessionAggregate? session = collection.ById.Values.SingleOrDefault(candidate =>
                candidate.SignalingTickets.ContainsKey(hash));
            if (session is null || !session.SignalingTickets.TryGetValue(hash, out SignalingTicketRecord? record) ||
                record.Consumed || record.ExpiresAt <= clock.UtcNow || session.State != "AUTHORIZED" ||
                session.ExpiresAt <= clock.UtcNow || session.PermissionRevision != record.PermissionRevision ||
                session.TransportEpoch != record.TransportEpoch ||
                !session.GrantedScopes.SequenceEqual(record.GrantedScopes, StringComparer.Ordinal))
                throw new ControlPlaneException(401, "SIGNAL_TICKET_INVALID", "Signaling authentication failed.");
            PeerRecord? peer = record.Role == "HOST" ? session.Host : session.Operator;
            if (peer is null || peer.PeerId != record.PeerId || peer.KeyThumbprint != record.KeyThumbprint)
                throw new ControlPlaneException(401, "SIGNAL_TICKET_INVALID", "Signaling authentication failed.");
            record.Consumed = true;
            AttendedSessionService.AppendLifecycle(session, "SIGNALING_CONNECTED", record.Role,
                record.PeerId.ToString("D"), clock.UtcNow, new { record.PeerId, record.TransportEpoch });
            return new SignalingConnectionBinding(session.Id, record.PeerId, record.Role, record.KeyThumbprint,
                record.GrantedScopes, record.PermissionRevision, record.TransportEpoch);
        });
    }

    public void AcceptSequence(SignalingConnectionBinding binding, long sequence, bool iceCandidate)
    {
        store.Execute(collection =>
        {
            if (!collection.ById.TryGetValue(binding.SessionId, out SessionAggregate? session) ||
                session.State != "AUTHORIZED" || session.TransportEpoch != binding.TransportEpoch ||
                session.PermissionRevision != binding.PermissionRevision)
                throw new ControlPlaneException(409, "SIGNAL_AUTHORIZATION_STALE", "Signaling authorization is stale.");
            long prior = session.SignalingSequences.GetValueOrDefault(binding.PeerId);
            if (sequence != prior + 1) throw new ControlPlaneException(409, "SIGNAL_SEQUENCE_INVALID", "Signaling sequence was invalid.");
            DateTimeOffset now = clock.UtcNow;
            if (!session.SignalingRates.TryGetValue(binding.PeerId, out SignalingRateRecord? rate) ||
                rate.TransportEpoch != binding.TransportEpoch)
            {
                rate = new SignalingRateRecord(binding.TransportEpoch, now, now);
                session.SignalingRates[binding.PeerId] = rate;
            }
            if (now - rate.MessageWindowStartedAt >= TimeSpan.FromMinutes(1))
            {
                rate = rate with { MessageWindowStartedAt = now };
                rate.MessageCount = 0;
                session.SignalingRates[binding.PeerId] = rate;
            }
            if (rate.MessageCount >= 120)
                throw new ControlPlaneException(429, "SIGNAL_RATE_LIMIT", "Signaling rate limit was exceeded.");
            if (iceCandidate)
            {
                if (now - rate.IceWindowStartedAt >= TimeSpan.FromSeconds(10))
                {
                    rate = rate with { IceWindowStartedAt = now };
                    rate.IceWindowCount = 0;
                    session.SignalingRates[binding.PeerId] = rate;
                }
                if (rate.IceWindowCount >= 30 || rate.IceTotal >= 256)
                    throw new ControlPlaneException(429, "SIGNAL_ICE_LIMIT", "ICE candidate limit was exceeded.");
                rate.IceWindowCount++;
                rate.IceTotal++;
            }
            rate.MessageCount++;
            session.SignalingSequences[binding.PeerId] = sequence;
            return 0;
        });
    }

    private string PublicSignalingUrl(HttpRequest request)
    {
        if (!string.IsNullOrWhiteSpace(options.SignalingPublicUrl)) return options.SignalingPublicUrl;
        string scheme = request.Scheme == "https" ? "wss" : "ws";
        return $"{scheme}://{request.Host}{request.PathBase}/v1/signaling";
    }

    private static SessionAggregate RequireAuthorized(SessionCollection collection, Guid sessionId,
        PeerAccessContext access, DateTimeOffset now)
    {
        if (!collection.ById.TryGetValue(sessionId, out SessionAggregate? session) || session.State != "AUTHORIZED" ||
            session.PermissionRevision != access.PermissionRevision || session.TransportEpoch != access.TransportEpoch ||
            session.ExpiresAt <= now)
            throw new ControlPlaneException(409, "SESSION_AUTHORIZATION_STALE", "Session authorization is stale.");
        return session;
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left <= right ? left : right;
}

internal sealed class TurnCredentialService(
    IAttendedSessionStore store,
    ControlPlaneOptions options,
    ISystemClock clock)
{
    private static readonly JsonSerializerOptions UsageJsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };
    private readonly byte[] turnSecret = options.GetTurnSharedSecret();
    private readonly byte[] meteringKey = options.GetTurnMeteringKey();

    public TurnCredentials Issue(Guid sessionId, PeerAccessContext access)
    {
        DateTimeOffset expiresAt = Min(clock.UtcNow + options.TurnCredentialLifetime,
            Min(access.TokenExpiresAt, store.Execute(collection =>
                RequireAuthorized(collection, sessionId, access, clock.UtcNow).ExpiresAt)));
        string opaque = ControlPlaneCrypto.GenerateSecret(18);
        string username = expiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) + ":" + opaque;
#pragma warning disable CA5350 // coturn REST time-limited credential interoperability mandates HMAC-SHA1.
        string credential = Convert.ToBase64String(HMACSHA1.HashData(turnSecret, Encoding.UTF8.GetBytes(username)));
#pragma warning restore CA5350
        store.Execute(collection =>
        {
            SessionAggregate session = RequireAuthorized(collection, sessionId, access, clock.UtcNow);
            foreach (string expired in session.RelayCredentials.Where(item =>
                         item.Value.ExpiresAt + TimeSpan.FromHours(24) <= clock.UtcNow).Select(item => item.Key).ToArray())
                session.RelayCredentials.Remove(expired);
            if (session.RelayCredentials.Count >= 32)
                throw new ControlPlaneException(429, "TURN_CREDENTIAL_LIMIT", "TURN credential limit was exceeded.");
            session.RelayCredentials[opaque] = new RelayCredentialRecord(access.PeerId, access.Role,
                options.TurnRegion, clock.UtcNow, expiresAt);
            AttendedSessionService.AppendLifecycle(session, "TURN_CREDENTIAL_ISSUED", access.Role,
                access.PeerId.ToString("D"), clock.UtcNow,
                new { access.PeerId, region = options.TurnRegion, expiresAt });
            return 0;
        });
        IceServer[] routes = options.TurnUrls.Select(url =>
            new IceServer([url], username, credential)).ToArray();
        return new TurnCredentials(options.TurnRegion, routes, expiresAt);
    }

    public TurnUsageAccepted AcceptUsage(ReadOnlySpan<byte> body, string timestampText, string signature)
    {
        if (!long.TryParse(timestampText, NumberStyles.None, CultureInfo.InvariantCulture, out long timestampSeconds))
            throw UnauthorizedMetering();
        DateTimeOffset timestamp;
        try { timestamp = DateTimeOffset.FromUnixTimeSeconds(timestampSeconds); }
        catch (ArgumentOutOfRangeException) { throw UnauthorizedMetering(); }
        if (timestamp < clock.UtcNow - TimeSpan.FromMinutes(5) || timestamp > clock.UtcNow + TimeSpan.FromSeconds(30))
            throw UnauthorizedMetering();
        byte[] prefix = Encoding.ASCII.GetBytes(timestampText + "\n");
        byte[] signed = new byte[prefix.Length + body.Length];
        prefix.CopyTo(signed, 0);
        body.CopyTo(signed.AsSpan(prefix.Length));
        string expected = ControlPlaneCrypto.Base64UrlEncode(HMACSHA256.HashData(meteringKey, signed));
        if (!FixedEquals(expected, signature)) throw UnauthorizedMetering();

        TurnUsageReport report;
        try
        {
            report = JsonSerializer.Deserialize<TurnUsageReport>(body, UsageJsonOptions)
                ?? throw new JsonException("Missing report.");
        }
        catch (JsonException)
        {
            throw new ControlPlaneException(400, "TURN_USAGE_INVALID", "TURN usage report was invalid.");
        }
        ValidateReport(report);
        if (string.IsNullOrWhiteSpace(report.Username))
            throw new ControlPlaneException(400, "TURN_USAGE_INVALID", "TURN usage report was invalid.");
        string[] username = report.Username.Split(':');
        string opaque = username[1];
        long usernameExpiry = long.Parse(username[0], CultureInfo.InvariantCulture);
        return store.Execute(collection =>
        {
            SessionAggregate? session = collection.ById.Values.SingleOrDefault(candidate =>
                candidate.RelayCredentials.ContainsKey(opaque));
            if (session is null || !session.RelayCredentials.TryGetValue(opaque, out RelayCredentialRecord? issued) ||
                issued.ExpiresAt.ToUnixTimeSeconds() != usernameExpiry || issued.Region != report.Region ||
                report.StartedAt < issued.IssuedAt - TimeSpan.FromMinutes(1) ||
                report.EndedAt > issued.ExpiresAt + TimeSpan.FromHours(24))
                throw new ControlPlaneException(404, "TURN_USAGE_CREDENTIAL_UNKNOWN", "TURN usage credential was unknown.");
            RelayUsageRecord value = new(report.EventId, opaque, report.Region, report.Transport, report.NodeId,
                report.BytesFromClient, report.BytesToClient, report.StartedAt, report.EndedAt);
            if (session.RelayUsage.TryGetValue(report.EventId, out RelayUsageRecord? previous))
            {
                if (previous != value) throw new ControlPlaneException(409, "TURN_USAGE_EVENT_CONFLICT", "TURN usage event conflicted.");
            }
            else
            {
                if (session.RelayUsage.Count >= 256)
                    throw new ControlPlaneException(429, "TURN_USAGE_LIMIT", "TURN usage event limit was exceeded.");
                session.RelayUsage.Add(report.EventId, value);
            }
            return new TurnUsageAccepted(report.EventId, session.Id, session.TenantId, report.Region,
                report.Transport, checked(report.BytesFromClient + report.BytesToClient));
        });
    }

    private static void ValidateReport(TurnUsageReport report)
    {
        string[] username = string.IsNullOrWhiteSpace(report.Username) ? [] : report.Username.Split(':');
        if (report.EventId == Guid.Empty || username.Length != 2 ||
            !long.TryParse(username[0], NumberStyles.None, CultureInfo.InvariantCulture, out _) ||
            username[1].Length != 24 || !username[1].All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_') ||
            string.IsNullOrWhiteSpace(report.Region) || report.Region.Length > 64 ||
            report.Transport is not ("UDP" or "TCP" or "TLS") ||
            string.IsNullOrWhiteSpace(report.NodeId) || report.NodeId.Length > 64 ||
            !report.NodeId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.') ||
            report.EndedAt < report.StartedAt || report.EndedAt - report.StartedAt > TimeSpan.FromHours(1) ||
            report.BytesFromClient > 1_000_000_000_000 || report.BytesToClient > 1_000_000_000_000)
            throw new ControlPlaneException(400, "TURN_USAGE_INVALID", "TURN usage report was invalid.");
    }

    private static SessionAggregate RequireAuthorized(SessionCollection collection, Guid sessionId,
        PeerAccessContext access, DateTimeOffset now)
    {
        if (!collection.ById.TryGetValue(sessionId, out SessionAggregate? session) || session.State != "AUTHORIZED" ||
            session.PermissionRevision != access.PermissionRevision || session.TransportEpoch != access.TransportEpoch ||
            session.ExpiresAt <= now)
            throw new ControlPlaneException(409, "SESSION_AUTHORIZATION_STALE", "Session authorization is stale.");
        return session;
    }

    private static bool FixedEquals(string left, string right) => CryptographicOperations.FixedTimeEquals(
        Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));

    private static ControlPlaneException UnauthorizedMetering() =>
        new(401, "TURN_USAGE_AUTHENTICATION_FAILED", "TURN usage authentication failed.");

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left <= right ? left : right;
}
