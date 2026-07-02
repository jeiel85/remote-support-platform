using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RemoteSupport.Server;

namespace RemoteSupport.Server.IntegrationTests;

public sealed class AttendedSessionApiTests
{
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

        public void Dispose() => key.Dispose();
    }
}
