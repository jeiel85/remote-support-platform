using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RemoteSupport.Server;
using RemoteSupport.Infrastructure;

namespace RemoteSupport.Server.IntegrationTests;

public sealed class AttendedSessionApiTests
{
    private static readonly string[] HelloCapabilities = ["h264"];
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    [Trait("Requirement", "FR-SES-004")]
    public async Task ProductClientCompletesSignedAttendedAuthorizationFlow()
    {
        await using ControlPlaneFactory factory = new(new ManualClock(DateTimeOffset.UtcNow));
        using HttpClient http = factory.CreateClient();
        http.DefaultRequestHeaders.Add("X-Test-Operator-Subject", "operator-product-client");
        http.DefaultRequestHeaders.Add("X-Test-Operator-Name", "Product Operator");
        http.DefaultRequestHeaders.Add("X-Test-Tenant-Id", Guid.NewGuid().ToString("D"));
        http.DefaultRequestHeaders.Add("X-Test-Tenant-Name", "Product Tenant");
        http.DefaultRequestHeaders.Add("X-Test-Tenant-Verified", "true");
        AttendedControlPlaneClient client = new(http);
        using EphemeralPeerIdentity hostIdentity = new();
        using EphemeralPeerIdentity operatorIdentity = new();

        HostSessionCreated host = await client.CreateHostSessionAsync(hostIdentity, "ko-KR");
        string[] scopes = ["VIEW_SCREEN", "CONTROL_POINTER", "CHAT"];
        OperatorResolvedSession resolved = await client.ResolveAsync(host.SupportCode, scopes, operatorIdentity, "test-oidc-token");
        PendingConsentResponse pending = (await client.GetPendingConsentAsync(host))!;
        Assert.Equal("Product Operator", pending.Operator.DisplayName);

        ConsentSessionResponse consent = await client.DecideConsentAsync(host, pending, hostIdentity, true, scopes);
        Assert.Equal("AUTHORIZED", consent.State);
        PeerAuthorizationResponse hostPeer = await client.AuthorizePeerAsync(host.SessionId, host.HostBootstrapToken, hostIdentity);
        PeerAuthorizationResponse operatorPeer = await client.AuthorizePeerAsync(host.SessionId, resolved.OperatorBootstrapToken, operatorIdentity);

        Assert.Equal("HOST", hostPeer.Role);
        Assert.Equal("OPERATOR", operatorPeer.Role);
        Assert.Equal(scopes.Order(StringComparer.Ordinal), hostPeer.GrantedScopes.Order(StringComparer.Ordinal));
        Assert.NotEqual(hostPeer.PeerToken, operatorPeer.PeerToken);
        Assert.Equal(hostPeer.AuthorizationContextSha256, operatorPeer.AuthorizationContextSha256);
        Assert.Equal(operatorPeer.PeerId, hostPeer.RemotePeerId);
        Assert.Equal(hostPeer.PeerId, operatorPeer.RemotePeerId);
        SignalingTicketResponse ticket = await client.GetSignalingTicketAsync(hostPeer, hostIdentity);
        TurnCredentialsResponse turn = await client.GetTurnCredentialsAsync(hostPeer, hostIdentity);
        Assert.NotEmpty(ticket.Ticket);
        Assert.NotEmpty(turn.IceServers);
        ConsentSessionResponse reduced = await client.RevokeScopesAsync(hostPeer, hostIdentity, consent.StateVersion,
            ["CONTROL_POINTER"]);
        Assert.Equal(consent.PermissionRevision + 1, reduced.PermissionRevision);
        Assert.DoesNotContain("CONTROL_POINTER", reduced.GrantedScopes);
        await Assert.ThrowsAsync<ControlPlaneClientException>(() => client.GetTurnCredentialsAsync(operatorPeer, operatorIdentity));
        hostPeer = await client.AuthorizePeerAsync(host.SessionId, host.HostBootstrapToken, hostIdentity);
        operatorPeer = await client.AuthorizePeerAsync(host.SessionId, resolved.OperatorBootstrapToken, operatorIdentity);
        Assert.Equal(reduced.PermissionRevision, hostPeer.PermissionRevision);
        ConsentSessionResponse terminated = await client.TerminateAsync(hostPeer, hostIdentity, "LOCAL_USER_DISCONNECT");
        Assert.Equal("TERMINATED", terminated.State);
        await Assert.ThrowsAsync<ControlPlaneClientException>(() => client.GetTurnCredentialsAsync(operatorPeer, operatorIdentity));
    }

    [Fact]
    [Trait("Requirement", "FR-SES-001")]
    public async Task FullAttendedFlowRequiresOidcConsentProofAndIndependentPeerProofs()
    {
        ManualClock clock = new(new DateTimeOffset(2026, 7, 2, 0, 0, 0, TimeSpan.Zero));
        await using ControlPlaneFactory factory = new(clock);
        using HttpClient client = factory.CreateClient();
        using PeerKey hostKey = new();
        using PeerKey operatorKey = new();

        AttendedSessionCreated created = await CreateAsync(client, hostKey);
        Assert.Matches("^[0-9A-HJKMNP-TV-Z]{5}-[0-9A-HJKMNP-TV-Z]{5}$", created.SupportCode);
        Assert.Equal("WAITING_FOR_OPERATOR", created.State);
        Assert.True(created.ExpiresAt <= clock.UtcNow + TimeSpan.FromMinutes(10));

        using HttpRequestMessage codeOnly = ResolveRequest(created.SupportCode, operatorKey);
        codeOnly.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.SupportCode);
        using HttpResponseMessage codeOnlyResponse = await client.SendAsync(codeOnly);
        Assert.Equal(HttpStatusCode.Unauthorized, codeOnlyResponse.StatusCode);

        using HttpRequestMessage resolve = ResolveRequest(created.SupportCode, operatorKey);
        AddOperator(resolve);
        using HttpResponseMessage resolveResponse = await client.SendAsync(resolve);
        resolveResponse.EnsureSuccessStatusCode();
        ConsentRequest consent = (await resolveResponse.Content.ReadFromJsonAsync<ConsentRequest>())!;
        Assert.Equal("Verified Operator", consent.Operator.DisplayName);
        Assert.Equal("Example Tenant", consent.Operator.TenantDisplayName);
        Assert.True(consent.Operator.VerifiedTenant);
        Assert.Equal(["CONTROL_POINTER", "VIEW_SCREEN"], consent.RequestedScopes);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.HostBootstrapToken);
        PendingConsent pending = (await client.GetFromJsonAsync<PendingConsent>(
            $"/v1/attended-sessions/{created.SessionId:D}/pending-consent"))!;
        Assert.Equal(consent.ConsentRequestId, pending.ConsentRequestId);
        Assert.Equal(consent.Operator, pending.Operator);

        AttendedSessionService service = factory.Services.GetRequiredService<AttendedSessionService>();
        SessionAggregate aggregate = Assert.Single(service.Snapshot());
        ConsentDecision unsigned = new(pending.ConsentRequestId, true, ["VIEW_SCREEN", "CONTROL_POINTER"],
            pending.ConsentNonce, new DetachedProof(pending.ConsentNonce, pending.HostEphemeralKeyThumbprint,
                "ecdsa-p256-sha256-p1363", "pending"));
        string consentSignature = hostKey.SignLowS(ControlPlaneCrypto.ConsentBytes(aggregate, unsigned));
        ConsentDecision decision = unsigned with { DecisionProof = unsigned.DecisionProof with { Signature = consentSignature } };

        Task<HttpResponseMessage>[] concurrent =
        [
            PostConsent(client, created.SessionId, pending.StateVersion, decision),
            PostConsent(client, created.SessionId, pending.StateVersion, decision),
        ];
        HttpResponseMessage[] decisions = await Task.WhenAll(concurrent);
        Assert.Contains(decisions, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Contains(decisions, response => response.StatusCode == HttpStatusCode.Conflict);
        foreach (HttpResponseMessage response in decisions) response.Dispose();

        PeerAuthorization hostAuthorization = await AuthorizePeer(client, factory, created.SessionId,
            created.HostBootstrapToken, "HOST", hostKey);
        PeerAuthorization operatorAuthorization = await AuthorizePeer(client, factory, created.SessionId,
            consent.OperatorBootstrapToken, "OPERATOR", operatorKey);
        Assert.NotEqual(hostAuthorization.PeerToken, operatorAuthorization.PeerToken);
        Assert.Equal(created.HostPeerId, hostAuthorization.PeerId);
        Assert.Equal(["CONTROL_POINTER", "VIEW_SCREEN"], hostAuthorization.GrantedScopes);
        ControlPlaneCrypto tokenVerifier = factory.Services.GetRequiredService<ControlPlaneCrypto>();
        Assert.True(tokenVerifier.VerifyPeerToken(hostAuthorization.PeerToken, clock.UtcNow, out JsonDocument? tokenPayload));
        using (tokenPayload)
        {
            Assert.Equal(created.SessionId, tokenPayload!.RootElement.GetProperty("sessionId").GetGuid());
            Assert.Equal(created.HostPeerId, tokenPayload.RootElement.GetProperty("peerId").GetGuid());
            Assert.Equal("HOST", tokenPayload.RootElement.GetProperty("role").GetString());
        }

        SessionAggregate completed = Assert.Single(service.Snapshot());
        Assert.Equal("AUTHORIZED", completed.State);
        Assert.True(completed.AuditEvents.Count >= 5);
        Assert.Equal(completed.AuditEvents.Count, completed.OutboxMessages.Count);
        for (int index = 0; index < completed.AuditEvents.Count; index++)
        {
            Assert.Equal(index + 1, completed.AuditEvents[index].Sequence);
            Assert.Equal(index == 0 ? null : completed.AuditEvents[index - 1].EventHash,
                completed.AuditEvents[index].PreviousHash);
            Assert.Equal(64, completed.AuditEvents[index].EventHash.Length);
        }
        string stored = JsonSerializer.Serialize(completed);
        SessionAggregate roundTripped = JsonSerializer.Deserialize<SessionAggregate>(stored)!;
        Assert.Equal(completed.BootstrapCredentials.Count, roundTripped.BootstrapCredentials.Count);
        Assert.Equal(completed.Challenges.Count, roundTripped.Challenges.Count);
        Assert.DoesNotContain(created.SupportCode.Replace("-", string.Empty, StringComparison.Ordinal),
            stored, StringComparison.Ordinal);
        Assert.DoesNotContain(created.HostBootstrapToken, stored, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "FR-SES-001")]
    public void SupportCodeGeneratorProducesOneHundredThousandUniqueFiftyBitCodes()
    {
        HashSet<string> codes = new(StringComparer.Ordinal);
        for (int index = 0; index < 100_000; index++)
        {
            string code = ControlPlaneCrypto.GenerateSupportCode();
            Assert.Matches("^[0-9A-HJKMNP-TV-Z]{5}-[0-9A-HJKMNP-TV-Z]{5}$", code);
            Assert.True(codes.Add(code), $"Support-code collision at sample {index}.");
        }
    }

    [Fact]
    [Trait("Requirement", "SEC-API-002")]
    public async Task CreateRequiresIdempotencyKeyAndReplaysTheOriginalSecretResponse()
    {
        await using ControlPlaneFactory factory = new(new ManualClock(
            new DateTimeOffset(2026, 7, 2, 0, 0, 0, TimeSpan.Zero)));
        using HttpClient client = factory.CreateClient();
        using PeerKey host = new();
        CreateAttendedSessionRequest body = CreateBody(host);

        using HttpResponseMessage missing = await client.PostAsJsonAsync("/v1/attended-sessions", body);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        const string key = "attended-create-idempotency-0001";
        AttendedSessionCreated first = await SendCreate(client, body, key);
        AttendedSessionCreated replay = await SendCreate(client, body, key);
        Assert.Equal(first, replay);
        Assert.Single(factory.Services.GetRequiredService<AttendedSessionService>().Snapshot());

        using PeerKey differentHost = new();
        using HttpRequestMessage reused = new(HttpMethod.Post, "/v1/attended-sessions")
        {
            Content = JsonContent.Create(CreateBody(differentHost)),
        };
        reused.Headers.Add("Idempotency-Key", key);
        using HttpResponseMessage conflict = await client.SendAsync(reused);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    [Fact]
    [Trait("Requirement", "AT-NFR-SEC-003")]
    [Trait("Requirement", "AT-NFR-REL-004")]
    public async Task DpopSignalingTurnAndMeteringAreBoundSingleUseAndReconnectSafe()
    {
        ManualClock clock = new(new DateTimeOffset(2026, 7, 2, 1, 0, 0, TimeSpan.Zero));
        await using ControlPlaneFactory factory = new(clock);
        using HttpClient client = factory.CreateClient();
        using PeerKey host = new();
        using PeerKey oper = new();
        AuthorizedPair pair = await CreateAuthorizedPair(client, factory, host, oper);

        const string replayJti = "dpop-proof-replay-00000001";
        using HttpRequestMessage hostTicketRequest = DpopRequest(client, HttpMethod.Post,
            $"/v1/sessions/{pair.Created.SessionId:D}/signaling-tickets", pair.Host.PeerToken, host, clock, replayJti);
        using HttpResponseMessage hostTicketResponse = await client.SendAsync(hostTicketRequest);
        hostTicketResponse.EnsureSuccessStatusCode();
        SignalingTicket hostTicket = (await hostTicketResponse.Content.ReadFromJsonAsync<SignalingTicket>())!;
        Assert.True(hostTicket.ExpiresAt <= clock.UtcNow + TimeSpan.FromSeconds(60));

        using HttpRequestMessage replay = DpopRequest(client, HttpMethod.Post,
            $"/v1/sessions/{pair.Created.SessionId:D}/signaling-tickets", pair.Host.PeerToken, host, clock, replayJti);
        using HttpResponseMessage replayResponse = await client.SendAsync(replay);
        Assert.Equal(HttpStatusCode.Unauthorized, replayResponse.StatusCode);

        using HttpRequestMessage crossSession = DpopRequest(client, HttpMethod.Post,
            $"/v1/sessions/{Guid.NewGuid():D}/turn-credentials", pair.Host.PeerToken, host, clock);
        using HttpResponseMessage crossSessionResponse = await client.SendAsync(crossSession);
        Assert.Equal(HttpStatusCode.Unauthorized, crossSessionResponse.StatusCode);

        SignalingTicket operatorTicket = await IssueTicket(client, pair.Created.SessionId,
            pair.Operator.PeerToken, oper, clock);
        Assert.Equal(hostTicket.Ticket[..16], operatorTicket.Ticket[..16]);
        Assert.NotEqual(hostTicket.Ticket, operatorTicket.Ticket);
        using HttpRequestMessage turnRequest = DpopRequest(client, HttpMethod.Post,
            $"/v1/sessions/{pair.Created.SessionId:D}/turn-credentials", pair.Host.PeerToken, host, clock);
        using HttpResponseMessage turnResponse = await client.SendAsync(turnRequest);
        turnResponse.EnsureSuccessStatusCode();
        TurnCredentials credentials = (await turnResponse.Content.ReadFromJsonAsync<TurnCredentials>())!;
        Assert.Equal(3, credentials.IceServers.Count);
        string[] issuedUrls = credentials.IceServers.SelectMany(server => server.Urls).ToArray();
        Assert.Contains(issuedUrls, url => url.StartsWith("turn:", StringComparison.Ordinal) && url.Contains("transport=udp", StringComparison.Ordinal));
        Assert.Contains(issuedUrls, url => url.StartsWith("turn:", StringComparison.Ordinal) && url.Contains("transport=tcp", StringComparison.Ordinal));
        Assert.Contains(issuedUrls, url => url.StartsWith("turns:", StringComparison.Ordinal));
        IceServer ice = credentials.IceServers[0];
        ControlPlaneOptions options = factory.Services.GetRequiredService<ControlPlaneOptions>();
#pragma warning disable CA5350 // coturn REST interoperability test vector.
        string expectedCredential = Convert.ToBase64String(HMACSHA1.HashData(options.GetTurnSharedSecret(),
            Encoding.UTF8.GetBytes(ice.Username)));
#pragma warning restore CA5350
        Assert.Equal(expectedCredential, ice.Credential);
        Assert.True(credentials.ExpiresAt <= clock.UtcNow + TimeSpan.FromMinutes(10));

        using WebSocket hostSocket = await ConnectSignaling(factory, hostTicket);
        using WebSocket operatorSocket = await ConnectSignaling(factory, operatorTicket);
        Microsoft.AspNetCore.TestHost.WebSocketClient consumedClient = factory.Server.CreateWebSocketClient();
        consumedClient.SubProtocols.Add("rsp.signaling.v1");
        await Assert.ThrowsAnyAsync<Exception>(() => consumedClient.ConnectAsync(
            new Uri($"ws://localhost/v1/signaling?ticket={Uri.EscapeDataString(hostTicket.Ticket)}"), CancellationToken.None));
        await SendSignal(hostSocket, pair, pair.Host.PeerId, 1, "HELLO",
            new { appVersion = "1.0.0", protocolMin = 1, protocolMax = 1, capabilities = HelloCapabilities }, clock);
        await SendSignal(operatorSocket, pair, pair.Operator.PeerId, 1, "HELLO",
            new { appVersion = "1.0.0", protocolMin = 1, protocolMax = 1, capabilities = HelloCapabilities }, clock);
        Assert.Equal("HELLO_ACK", (await ReceiveSignal(hostSocket)).GetProperty("type").GetString());
        Assert.Equal("HELLO_ACK", (await ReceiveSignal(operatorSocket)).GetProperty("type").GetString());

        string fingerprint = string.Join(':', Enumerable.Repeat("AA", 32));
        string sdp = $"v=0\r\no=- 1 1 IN IP4 127.0.0.1\r\ns=-\r\nt=0 0\r\na=fingerprint:sha-256 {fingerprint}\r\n";
        await SendSignal(operatorSocket, pair, pair.Operator.PeerId, 2, "SDP_OFFER", new { sdp }, clock);
        JsonElement relayed = await ReceiveSignal(hostSocket);
        Assert.Equal("SDP_OFFER", relayed.GetProperty("type").GetString());
        Assert.Equal(pair.Operator.PeerId, relayed.GetProperty("peerId").GetGuid());

        operatorSocket.Dispose();
        SignalingTicket reconnectTicket = await IssueTicket(client, pair.Created.SessionId,
            pair.Operator.PeerToken, oper, clock);
        using WebSocket reconnected = await ConnectSignaling(factory, reconnectTicket);
        await SendSignal(reconnected, pair, pair.Operator.PeerId, 3, "HELLO",
            new { appVersion = "1.0.0", protocolMin = 1, protocolMax = 1, capabilities = HelloCapabilities }, clock);
        Assert.Equal("HELLO_ACK", (await ReceiveSignal(reconnected)).GetProperty("type").GetString());
        await SendSignal(hostSocket, pair, pair.Host.PeerId, 2, "PING", new { nonce = "ping-nonce-0001" }, clock);
        Assert.Equal("PONG", (await ReceiveSignal(hostSocket)).GetProperty("type").GetString());
        SessionAggregate afterReconnect = Assert.Single(factory.Services.GetRequiredService<AttendedSessionService>().Snapshot());
        Assert.Equal(1, afterReconnect.TransportEpoch);

        TurnUsageReport usage = new(Guid.NewGuid(), ice.Username, credentials.Region, "TLS", "turn-seoul-a",
            1000, 2000, clock.UtcNow, clock.UtcNow + TimeSpan.FromSeconds(30));
        byte[] usageBody = JsonSerializer.SerializeToUtf8Bytes(usage, WebJsonOptions);
        string timestamp = clock.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        byte[] usagePrefix = Encoding.ASCII.GetBytes(timestamp + "\n");
        string usageSignature = ControlPlaneCrypto.Base64UrlEncode(HMACSHA256.HashData(options.GetTurnMeteringKey(),
            usagePrefix.Concat(usageBody).ToArray()));
        using HttpRequestMessage usageRequest = new(HttpMethod.Post, "/internal/v1/turn-usage")
        {
            Content = new ByteArrayContent(usageBody),
        };
        usageRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        usageRequest.Headers.Add("X-RSP-Turn-Timestamp", timestamp);
        usageRequest.Headers.Add("X-RSP-Turn-Signature", usageSignature);
        using HttpResponseMessage usageResponse = await client.SendAsync(usageRequest);
        usageResponse.EnsureSuccessStatusCode();
        TurnUsageAccepted accepted = (await usageResponse.Content.ReadFromJsonAsync<TurnUsageAccepted>())!;
        Assert.Equal(3000UL, accepted.TotalBytes);
        SessionAggregate metered = Assert.Single(factory.Services.GetRequiredService<AttendedSessionService>().Snapshot());
        Assert.Single(metered.RelayUsage);
        string persisted = JsonSerializer.Serialize(metered);
        Assert.DoesNotContain(sdp, persisted, StringComparison.Ordinal);
        Assert.DoesNotContain(fingerprint, persisted, StringComparison.Ordinal);
        Assert.DoesNotContain(hostTicket.Ticket, persisted, StringComparison.Ordinal);
        Assert.DoesNotContain(ice.Credential, persisted, StringComparison.Ordinal);
        Assert.DoesNotContain(replayJti, persisted, StringComparison.Ordinal);

        SignalingTicket unusedTicket = await IssueTicket(client, pair.Created.SessionId,
            pair.Host.PeerToken, host, clock);
        clock.Advance(TimeSpan.FromSeconds(61));
        Microsoft.AspNetCore.TestHost.WebSocketClient expiredClient = factory.Server.CreateWebSocketClient();
        expiredClient.SubProtocols.Add("rsp.signaling.v1");
        await Assert.ThrowsAnyAsync<Exception>(() => expiredClient.ConnectAsync(
            new Uri($"ws://localhost/v1/signaling?ticket={Uri.EscapeDataString(unusedTicket.Ticket)}"), CancellationToken.None));
    }

    [Fact]
    [Trait("Requirement", "AT-NFR-SEC-003")]
    public async Task SignalingRejectsSequenceReplayMalformedSdpAndMalformedIceAndExpiredAuthorization()
    {
        ManualClock clock = new(new DateTimeOffset(2026, 7, 2, 2, 0, 0, TimeSpan.Zero));
        await using ControlPlaneFactory factory = new(clock);
        using HttpClient client = factory.CreateClient();
        using PeerKey host = new();
        using PeerKey oper = new();
        AuthorizedPair pair = await CreateAuthorizedPair(client, factory, host, oper);

        SignalingTicket firstTicket = await IssueTicket(client, pair.Created.SessionId, pair.Operator.PeerToken, oper, clock);
        using WebSocket first = await ConnectSignaling(factory, firstTicket);
        await SendHello(first, pair, pair.Operator.PeerId, 1, clock);
        _ = await ReceiveSignal(first);
        string fingerprint = string.Join(':', Enumerable.Repeat("AA", 32));
        string validSdp = $"v=0\r\no=- 1 1 IN IP4 127.0.0.1\r\ns=-\r\nt=0 0\r\na=fingerprint:sha-256 {fingerprint}\r\n";
        await SendSignal(first, pair, pair.Operator.PeerId, 3, "SDP_OFFER", new { sdp = validSdp }, clock);
        await AssertClosed(first, "SIGNAL_SEQUENCE_INVALID");

        SignalingTicket secondTicket = await IssueTicket(client, pair.Created.SessionId, pair.Operator.PeerToken, oper, clock);
        using WebSocket second = await ConnectSignaling(factory, secondTicket);
        await SendHello(second, pair, pair.Operator.PeerId, 2, clock);
        _ = await ReceiveSignal(second);
        await SendSignal(second, pair, pair.Operator.PeerId, 3, "SDP_OFFER",
            new { sdp = "v=0\r\na=ice-ufrag:redacted\r\n" }, clock);
        await AssertClosed(second, "SIGNAL_SDP_FINGERPRINT_INVALID");

        SignalingTicket thirdTicket = await IssueTicket(client, pair.Created.SessionId, pair.Operator.PeerToken, oper, clock);
        using WebSocket third = await ConnectSignaling(factory, thirdTicket);
        await SendHello(third, pair, pair.Operator.PeerId, 3, clock);
        _ = await ReceiveSignal(third);
        await SendSignal(third, pair, pair.Operator.PeerId, 4, "ICE_CANDIDATE",
            new { candidate = "candidate:bad\r\ninjected", sdpMid = "0", sdpMLineIndex = 0 }, clock);
        await AssertClosed(third, "SIGNAL_ICE_INVALID");

        clock.Advance(TimeSpan.FromMinutes(11));
        using HttpRequestMessage expired = DpopRequest(client, HttpMethod.Post,
            $"/v1/sessions/{pair.Created.SessionId:D}/turn-credentials", pair.Host.PeerToken, host, clock);
        using HttpResponseMessage expiredResponse = await client.SendAsync(expired);
        Assert.Equal(HttpStatusCode.Unauthorized, expiredResponse.StatusCode);
    }

    [Fact]
    [Trait("Requirement", "AT-NFR-CST-003")]
    public async Task SignalingRateAndIceLimitsPersistAcrossReconnectWindows()
    {
        ManualClock clock = new(new DateTimeOffset(2026, 7, 2, 3, 0, 0, TimeSpan.Zero));
        await using ControlPlaneFactory factory = new(clock);
        using HttpClient client = factory.CreateClient();
        using PeerKey host = new();
        using PeerKey oper = new();
        AuthorizedPair pair = await CreateAuthorizedPair(client, factory, host, oper);
        SessionAggregate aggregate = Assert.Single(factory.Services.GetRequiredService<AttendedSessionService>().Snapshot());
        SignalingConnectionBinding binding = new(pair.Created.SessionId, pair.Host.PeerId, "HOST",
            aggregate.Host.KeyThumbprint, aggregate.GrantedScopes, aggregate.PermissionRevision, aggregate.TransportEpoch);
        SignalingTicketService tickets = factory.Services.GetRequiredService<SignalingTicketService>();

        for (int sequence = 1; sequence <= 120; sequence++) tickets.AcceptSequence(binding, sequence, false);
        ControlPlaneException messageLimit = Assert.Throws<ControlPlaneException>(() =>
            tickets.AcceptSequence(binding, 121, false));
        Assert.Equal(429, messageLimit.StatusCode);

        clock.Advance(TimeSpan.FromMinutes(1));
        for (int sequence = 121; sequence <= 150; sequence++) tickets.AcceptSequence(binding, sequence, true);
        ControlPlaneException iceLimit = Assert.Throws<ControlPlaneException>(() =>
            tickets.AcceptSequence(binding, 151, true));
        Assert.Equal(429, iceLimit.StatusCode);
        Assert.Equal(150, Assert.Single(factory.Services.GetRequiredService<AttendedSessionService>().Snapshot())
            .SignalingSequences[pair.Host.PeerId]);
    }

    [Fact]
    [Trait("Requirement", "FR-SES-006")]
    public async Task ExpiredAndUnknownCodesUseTheSameGenericFailureAndResolveIsRateLimited()
    {
        ManualClock clock = new(new DateTimeOffset(2026, 7, 2, 0, 0, 0, TimeSpan.Zero));
        await using ControlPlaneFactory factory = new(clock, resolveLimit: 2);
        using HttpClient client = factory.CreateClient();
        using PeerKey host = new();
        using PeerKey oper = new();
        AttendedSessionCreated created = await CreateAsync(client, host);
        clock.Advance(TimeSpan.FromMinutes(11));

        using HttpRequestMessage expired = ResolveRequest(created.SupportCode, oper);
        AddOperator(expired);
        using HttpResponseMessage expiredResponse = await client.SendAsync(expired);
        using HttpRequestMessage unknown = ResolveRequest("00000-00000", oper);
        AddOperator(unknown);
        using HttpResponseMessage unknownResponse = await client.SendAsync(unknown);
        Assert.Equal(HttpStatusCode.NotFound, expiredResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownResponse.StatusCode);
        ProblemContract expiredProblem = (await expiredResponse.Content.ReadFromJsonAsync<ProblemContract>())!;
        ProblemContract unknownProblem = (await unknownResponse.Content.ReadFromJsonAsync<ProblemContract>())!;
        Assert.Equal(expiredProblem.Code, unknownProblem.Code);
        Assert.Equal(expiredProblem.Message, unknownProblem.Message);

        HttpStatusCode finalStatus = HttpStatusCode.NotFound;
        for (int attempt = 0; attempt < 30 && finalStatus != HttpStatusCode.TooManyRequests; attempt++)
        {
            using HttpRequestMessage limited = ResolveRequest("11111-11111", oper);
            AddOperator(limited);
            using HttpResponseMessage limitedResponse = await client.SendAsync(limited);
            finalStatus = limitedResponse.StatusCode;
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, finalStatus);
    }

    private static async Task<AuthorizedPair> CreateAuthorizedPair(HttpClient client, ControlPlaneFactory factory,
        PeerKey host, PeerKey oper)
    {
        AttendedSessionCreated created = await CreateAsync(client, host);
        using HttpRequestMessage resolve = ResolveRequest(created.SupportCode, oper);
        AddOperator(resolve);
        using HttpResponseMessage resolveResponse = await client.SendAsync(resolve);
        resolveResponse.EnsureSuccessStatusCode();
        ConsentRequest consent = (await resolveResponse.Content.ReadFromJsonAsync<ConsentRequest>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.HostBootstrapToken);
        PendingConsent pending = (await client.GetFromJsonAsync<PendingConsent>(created.PendingEventsUrl))!;
        SessionAggregate aggregate = Assert.Single(factory.Services.GetRequiredService<AttendedSessionService>().Snapshot());
        ConsentDecision unsigned = new(consent.ConsentRequestId, true, consent.RequestedScopes,
            pending.ConsentNonce, new DetachedProof(pending.ConsentNonce, pending.HostEphemeralKeyThumbprint,
                "ecdsa-p256-sha256-p1363", string.Empty));
        ConsentDecision decision = unsigned with
        {
            DecisionProof = unsigned.DecisionProof with
            {
                Signature = host.SignLowS(ControlPlaneCrypto.ConsentBytes(aggregate, unsigned)),
            },
        };
        using HttpResponseMessage decisionResponse = await PostConsent(client, created.SessionId, pending.StateVersion, decision);
        decisionResponse.EnsureSuccessStatusCode();
        PeerAuthorization hostAuthorization = await AuthorizePeer(client, factory, created.SessionId,
            created.HostBootstrapToken, "HOST", host);
        PeerAuthorization operatorAuthorization = await AuthorizePeer(client, factory, created.SessionId,
            consent.OperatorBootstrapToken, "OPERATOR", oper);
        client.DefaultRequestHeaders.Authorization = null;
        return new AuthorizedPair(created, hostAuthorization, operatorAuthorization);
    }

    private static HttpRequestMessage DpopRequest(HttpClient client, HttpMethod method, string path, string token,
        PeerKey key, ManualClock clock, string? jti = null)
    {
        Uri target = new(client.BaseAddress!, path);
        HttpRequestMessage request = new(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("DPoP", token);
        request.Headers.TryAddWithoutValidation("DPoP", key.CreateDpopProof(method.Method, target, token,
            clock.UtcNow, jti ?? $"dpop-{Guid.NewGuid():N}"));
        return request;
    }

    private static async Task<SignalingTicket> IssueTicket(HttpClient client, Guid sessionId, string token,
        PeerKey key, ManualClock clock)
    {
        using HttpRequestMessage request = DpopRequest(client, HttpMethod.Post,
            $"/v1/sessions/{sessionId:D}/signaling-tickets", token, key, clock);
        using HttpResponseMessage response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SignalingTicket>())!;
    }

    private static async Task<WebSocket> ConnectSignaling(ControlPlaneFactory factory, SignalingTicket ticket)
    {
        Microsoft.AspNetCore.TestHost.WebSocketClient client = factory.Server.CreateWebSocketClient();
        client.SubProtocols.Add("rsp.signaling.v1");
        return await client.ConnectAsync(new Uri(
            $"ws://localhost/v1/signaling?ticket={Uri.EscapeDataString(ticket.Ticket)}"), CancellationToken.None);
    }

    private static Task SendSignal(WebSocket socket, AuthorizedPair pair, Guid peerId, long sequence, string type,
        object payload, ManualClock clock)
    {
        byte[] message = JsonSerializer.SerializeToUtf8Bytes(new
        {
            protocolVersion = 1,
            messageId = Guid.CreateVersion7(clock.UtcNow),
            sessionId = pair.Created.SessionId,
            peerId,
            epoch = 1,
            sequence,
            sentAt = clock.UtcNow,
            type,
            payload,
        });
        return socket.SendAsync(message, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static Task SendHello(WebSocket socket, AuthorizedPair pair, Guid peerId, long sequence,
        ManualClock clock) => SendSignal(socket, pair, peerId, sequence, "HELLO",
        new { appVersion = "1.0.0", protocolMin = 1, protocolMax = 1, capabilities = HelloCapabilities }, clock);

    private static async Task AssertClosed(WebSocket socket, string expectedCode)
    {
        byte[] buffer = new byte[1024];
        ValueWebSocketReceiveResult result = await socket.ReceiveAsync(buffer.AsMemory(), CancellationToken.None);
        Assert.Equal(WebSocketMessageType.Close, result.MessageType);
        Assert.Equal(expectedCode, socket.CloseStatusDescription);
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "ack", CancellationToken.None);
    }

    private static async Task<JsonElement> ReceiveSignal(WebSocket socket)
    {
        byte[] buffer = new byte[65_536];
        ValueWebSocketReceiveResult result = await socket.ReceiveAsync(buffer.AsMemory(), CancellationToken.None);
        Assert.Equal(WebSocketMessageType.Text, result.MessageType);
        Assert.True(result.EndOfMessage);
        using JsonDocument document = JsonDocument.Parse(buffer.AsMemory(0, result.Count));
        return document.RootElement.Clone();
    }

    private sealed record AuthorizedPair(AttendedSessionCreated Created, PeerAuthorization Host,
        PeerAuthorization Operator);

    private static async Task<AttendedSessionCreated> CreateAsync(HttpClient client, PeerKey key)
    {
        return await SendCreate(client, CreateBody(key), $"test-create-{Guid.NewGuid():N}");
    }

    private static CreateAttendedSessionRequest CreateBody(PeerKey key) => new(key.Jwk, "1.0.0",
        new ClientCapabilities(1, 0, ["transport-binding-v1"], ["H264"]), null, "ko-KR");

    private static async Task<AttendedSessionCreated> SendCreate(HttpClient client,
        CreateAttendedSessionRequest request, string idempotencyKey)
    {
        using HttpRequestMessage message = new(HttpMethod.Post, "/v1/attended-sessions")
        {
            Content = JsonContent.Create(request),
        };
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        using HttpResponseMessage response = await client.SendAsync(message);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AttendedSessionCreated>())!;
    }

    private static HttpRequestMessage ResolveRequest(string code, PeerKey key) => new(HttpMethod.Post,
        "/v1/attended-sessions/resolve")
    {
        Content = JsonContent.Create(new ResolveAttendedSessionRequest(code,
            ["VIEW_SCREEN", "CONTROL_POINTER"], key.Jwk, "1.0.0",
            new ClientCapabilities(1, 0, ["transport-binding-v1"], ["H264"])))
    };

    private static void AddOperator(HttpRequestMessage request)
    {
        request.Headers.Add("X-Test-Operator-Subject", "operator-123");
        request.Headers.Add("X-Test-Operator-Name", "Verified Operator");
        request.Headers.Add("X-Test-Tenant-Id", "11111111-1111-1111-1111-111111111111");
        request.Headers.Add("X-Test-Tenant-Name", "Example Tenant");
        request.Headers.Add("X-Test-Tenant-Verified", "true");
    }

    private static Task<HttpResponseMessage> PostConsent(HttpClient client, Guid sessionId, long version, ConsentDecision decision)
    {
        HttpRequestMessage request = new(HttpMethod.Post, $"/v1/attended-sessions/{sessionId:D}/consent")
        {
            Content = JsonContent.Create(decision),
        };
        request.Headers.Authorization = client.DefaultRequestHeaders.Authorization;
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        return client.SendAsync(request);
    }

    private static async Task<PeerAuthorization> AuthorizePeer(HttpClient client, ControlPlaneFactory factory,
        Guid sessionId, string bootstrap, string role, PeerKey key)
    {
        using HttpRequestMessage challengeRequest = new(HttpMethod.Post,
            $"/v1/sessions/{sessionId:D}/peer-authorization-challenges");
        challengeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bootstrap);
        using HttpResponseMessage challengeResponse = await client.SendAsync(challengeRequest);
        challengeResponse.EnsureSuccessStatusCode();
        PeerAuthorizationChallenge challenge = (await challengeResponse.Content.ReadFromJsonAsync<PeerAuthorizationChallenge>())!;
        SessionAggregate aggregate = Assert.Single(factory.Services.GetRequiredService<AttendedSessionService>().Snapshot());
        ChallengeRecord record = aggregate.Challenges[challenge.ChallengeId];
        PeerRecord peer = role == "HOST" ? aggregate.Host : aggregate.Operator!;
        string signature = key.SignLowS(ControlPlaneCrypto.PeerAuthorizationBytes(aggregate, record, peer, challenge.Nonce));
        PeerAuthorizationRequest body = new(challenge.ChallengeId, role,
            new ProofOfPossession(challenge.Nonce, signature, key.Jwk, "ecdsa-p256-sha256-p1363"));
        using HttpRequestMessage authorizeRequest = new(HttpMethod.Post, $"/v1/sessions/{sessionId:D}/peer-authorization")
        {
            Content = JsonContent.Create(body),
        };
        authorizeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bootstrap);
        using HttpResponseMessage response = await client.SendAsync(authorizeRequest);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PeerAuthorization>())!;
    }

    private sealed class ControlPlaneFactory(ManualClock clock, int resolveLimit = 20) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ControlPlane:LookupKeyBase64"] = Convert.ToBase64String(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray()),
                    ["ControlPlane:TokenSigningKeyBase64"] = Convert.ToBase64String(Enumerable.Range(33, 32).Select(value => (byte)value).ToArray()),
                    ["ControlPlane:UseInMemoryStore"] = "true",
                    ["ControlPlane:ResolveRequestsPerMinute"] = resolveLimit.ToString(CultureInfo.InvariantCulture),
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISystemClock>();
                services.AddSingleton<ISystemClock>(clock);
            });
        }
    }

    private sealed class ManualClock(DateTimeOffset initial) : ISystemClock
    {
        public DateTimeOffset UtcNow { get; private set; } = initial;
        public void Advance(TimeSpan duration) => UtcNow += duration;
    }

    private sealed class PeerKey : IDisposable
    {
        private static readonly BigInteger Order = BigInteger.Parse(
            "00FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551",
            NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        private static readonly BigInteger HalfOrder = Order / 2;
        private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        public PeerKey()
        {
            ECParameters parameters = key.ExportParameters(false);
            Jwk = JsonSerializer.SerializeToElement(new
            {
                kty = "EC",
                crv = "P-256",
                x = ControlPlaneCrypto.Base64UrlEncode(parameters.Q.X!),
                y = ControlPlaneCrypto.Base64UrlEncode(parameters.Q.Y!),
            });
        }

        public JsonElement Jwk { get; }

        public string SignLowS(byte[] bytes)
        {
            byte[] signature = key.SignData(bytes, HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            BigInteger s = new(signature.AsSpan(32, 32), isUnsigned: true, isBigEndian: true);
            if (s > HalfOrder)
            {
                s = Order - s;
                byte[] normalized = s.ToByteArray(isUnsigned: true, isBigEndian: true);
                signature.AsSpan(32, 32).Clear();
                normalized.CopyTo(signature.AsSpan(64 - normalized.Length));
            }
            return ControlPlaneCrypto.Base64UrlEncode(signature);
        }

        public string CreateDpopProof(string method, Uri target, string token, DateTimeOffset issuedAt, string jti)
        {
            string encodedHeader = ControlPlaneCrypto.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
            {
                typ = "dpop+jwt",
                alg = "ES256",
                jwk = Jwk,
            }));
            string htu = target.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped);
            string encodedPayload = ControlPlaneCrypto.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
            {
                jti,
                htm = method,
                htu,
                iat = issuedAt.ToUnixTimeSeconds(),
                ath = ControlPlaneCrypto.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(token))),
            }));
            string input = encodedHeader + "." + encodedPayload;
            byte[] signature = key.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            return input + "." + ControlPlaneCrypto.Base64UrlEncode(signature);
        }

        public void Dispose() => key.Dispose();
    }
}
