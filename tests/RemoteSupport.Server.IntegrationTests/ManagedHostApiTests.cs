using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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

namespace RemoteSupport.Server.IntegrationTests;

public sealed class ManagedHostApiTests
{
    private static readonly string[] ManagedScopes = ["VIEW_SCREEN", "CONTROL_POINTER"];
    private static readonly string[] OperatorRole = ["OWNER", "OPERATOR"];
    private static readonly string[] ManagedAttendedSessionType = ["MANAGED_ATTENDED"];

    [Fact]
    [Trait("Requirement", "AT-FR-MGT-002")]
    public async Task ManagedSessionFlowsThroughPolicyConsentAndPeerAuthorization()
    {
        ManagedHostClock clock = new(new DateTimeOffset(2026, 7, 4, 10, 0, 0, TimeSpan.Zero));
        await using ManagedHostFactory factory = new(clock);
        using HttpClient client = factory.CreateClient();
        TenantContract tenant = await CreateTenant(client, "managed-owner", "owner@managed.test", "managed-tenant", clock);
        await CreateAndActivatePolicy(client, tenant.Id, clock, "managed-owner", "owner@managed.test");

        using ManagedHostKey deviceKey = new();
        Guid deviceId = await EnrollDevice(client, tenant.Id, clock, deviceKey, "managed-owner", "owner@managed.test");

        DeviceCredentialResult credential = await ExchangeCredential(client, deviceId, deviceKey, keyVersion: 1);
        Assert.Equal(1, credential.KeyVersion);

        using HttpRequestMessage heartbeat = DeviceDpopRequest(client, HttpMethod.Post,
            $"/v1/devices/{deviceId:D}/heartbeat", credential.DeviceCredential, deviceKey, clock,
            new DeviceHeartbeat("0.10.0", "Windows 11 24H2", "HEALTHY", 1, clock.UtcNow));
        using HttpResponseMessage heartbeatResponse = await client.SendAsync(heartbeat);
        Assert.Equal(HttpStatusCode.NoContent, heartbeatResponse.StatusCode);

        using ManagedHostKey operatorKey = new();
        ManagedSessionCreated created;
        using (HttpRequestMessage createSession = Request(HttpMethod.Post, $"/v1/devices/{deviceId:D}/sessions",
                   "managed-owner", "owner@managed.test", tenant.Id, clock, true,
                   new ManagedSessionRequest(ManagedScopes, "MANAGED_ATTENDED", operatorKey.Jwk, 1800)))
        {
            createSession.Headers.Add("Idempotency-Key", "create-managed-session-0001");
            using HttpResponseMessage response = await client.SendAsync(createSession);
            response.EnsureSuccessStatusCode();
            created = (await response.Content.ReadFromJsonAsync<ManagedSessionCreated>())!;
        }
        Assert.Equal("HOST_PENDING", created.Session.State);
        Assert.True(created.LocalConsentRequired);
        Assert.Equal("QUEUED", created.HostDeliveryState);
        Assert.Equal(ManagedScopes.Order(StringComparer.Ordinal), created.Session.RequestedScopes.Order(StringComparer.Ordinal));

        PagedManagedSessionRequests pending;
        using (HttpRequestMessage poll = DeviceDpopRequest(client, HttpMethod.Get,
                   $"/v1/devices/{deviceId:D}/pending-session-requests?waitSeconds=1", credential.DeviceCredential, deviceKey, clock))
        using (HttpResponseMessage response = await client.SendAsync(poll))
        {
            response.EnsureSuccessStatusCode();
            pending = (await response.Content.ReadFromJsonAsync<PagedManagedSessionRequests>())!;
        }
        PendingManagedSessionRequest item = Assert.Single(pending.Items);
        Assert.Equal(created.Session.Id, item.SessionId);
        Assert.False(string.IsNullOrEmpty(item.PolicyDecisionHash));
        Assert.False(string.IsNullOrEmpty(item.ConsentNonce));

        using ManagedHostKey hostKey = new();
        string hostThumbprint = ControlPlaneCrypto.Thumbprint(hostKey.Jwk);
        ManagedHostDecisionRequest decisionRequest = new(true, ManagedScopes, item.ConsentNonce, hostKey.Jwk,
            new DetachedProof(string.Empty, string.Empty, string.Empty, string.Empty));
        string decisionSignature = deviceKey.SignLowS(AttendedSessionService.ManagedHostDecisionProofBytes(
            item.SessionId, decisionRequest, hostThumbprint));
        decisionRequest = decisionRequest with
        {
            DecisionProof = new DetachedProof("decision-nonce", ControlPlaneCrypto.Thumbprint(deviceKey.Jwk),
                "ecdsa-p256-sha256-p1363", decisionSignature),
        };
        ManagedHostDecisionResult decisionResult;
        using (HttpRequestMessage decide = DeviceDpopRequest(client, HttpMethod.Post,
                   $"/v1/sessions/{item.SessionId:D}/managed-host-decision", credential.DeviceCredential, deviceKey, clock,
                   decisionRequest))
        {
            decide.Headers.TryAddWithoutValidation("If-Match", $"\"{item.StateVersion}\"");
            using HttpResponseMessage response = await client.SendAsync(decide);
            response.EnsureSuccessStatusCode();
            decisionResult = (await response.Content.ReadFromJsonAsync<ManagedHostDecisionResult>())!;
        }
        Assert.Equal("AUTHORIZED", decisionResult.Session.State);
        Assert.NotEqual(Guid.Empty, decisionResult.HostPeerId);
        Assert.False(string.IsNullOrEmpty(decisionResult.HostBootstrapToken));

        PeerAuthorization hostAuthorization = await AuthorizePeer(client, factory, item.SessionId,
            decisionResult.HostBootstrapToken!, "HOST", hostKey);
        PeerAuthorization operatorAuthorization = await AuthorizePeer(client, factory, item.SessionId,
            created.OperatorBootstrapToken, "OPERATOR", operatorKey);
        Assert.Equal(ManagedScopes.Order(StringComparer.Ordinal), hostAuthorization.GrantedScopes.Order(StringComparer.Ordinal));
        Assert.Equal(hostAuthorization.RemotePeerId, operatorAuthorization.PeerId);
    }

    [Fact]
    [Trait("Requirement", "AT-FR-MGT-003")]
    public async Task DeviceKeyRotationAndRevocationInvalidateStaleCredentials()
    {
        ManagedHostClock clock = new(new DateTimeOffset(2026, 7, 4, 11, 0, 0, TimeSpan.Zero));
        await using ManagedHostFactory factory = new(clock);
        using HttpClient client = factory.CreateClient();
        TenantContract tenant = await CreateTenant(client, "rotate-owner", "owner@rotate.test", "rotate-tenant", clock);
        using ManagedHostKey deviceKeyV1 = new();
        Guid deviceId = await EnrollDevice(client, tenant.Id, clock, deviceKeyV1, "rotate-owner", "owner@rotate.test");
        DeviceCredentialResult credentialV1 = await ExchangeCredential(client, deviceId, deviceKeyV1, keyVersion: 1);

        DeviceCredentialChallenge rotationChallenge;
        using (HttpRequestMessage challenge = new(HttpMethod.Post, $"/v1/devices/{deviceId:D}/credential-challenges")
        { Content = JsonContent.Create(new DeviceCredentialChallengeRequest(2, "KEY_ROTATION")) })
        using (HttpResponseMessage response = await client.SendAsync(challenge))
        {
            response.EnsureSuccessStatusCode();
            rotationChallenge = (await response.Content.ReadFromJsonAsync<DeviceCredentialChallenge>())!;
        }
        Assert.Equal("RSP-DEVICE-KEY-ROTATION-V1", rotationChallenge.CanonicalizationVersion);

        using ManagedHostKey deviceKeyV2 = new();
        string newThumbprint = ControlPlaneCrypto.Thumbprint(deviceKeyV2.Jwk);
        byte[] rotationBytes = GovernanceService.CreateDeviceCredentialProofBytes("RSP-DEVICE-KEY-ROTATION-V1",
            deviceId, rotationChallenge.ChallengeId, "KEY_ROTATION", rotationChallenge.Nonce, newThumbprint);
        string currentKeySignature = deviceKeyV1.SignLowS(rotationBytes);
        DeviceKeyRotationRequest rotationRequest = new(deviceKeyV2.Jwk,
            new DetachedProof(rotationChallenge.Nonce, ControlPlaneCrypto.Thumbprint(deviceKeyV1.Jwk),
                "ecdsa-p256-sha256-p1363", currentKeySignature), rotationChallenge.ChallengeId);
        DeviceKeyRotationResult rotationResult;
        using (HttpRequestMessage rotate = DeviceDpopRequest(client, HttpMethod.Post,
                   $"/v1/devices/{deviceId:D}/keys/rotate", credentialV1.DeviceCredential, deviceKeyV1, clock, rotationRequest))
        using (HttpResponseMessage response = await client.SendAsync(rotate))
        {
            response.EnsureSuccessStatusCode();
            rotationResult = (await response.Content.ReadFromJsonAsync<DeviceKeyRotationResult>())!;
        }
        Assert.Equal(2, rotationResult.KeyVersion);
        Assert.True(rotationResult.CredentialChallengeRequired);

        using HttpRequestMessage staleHeartbeat = DeviceDpopRequest(client, HttpMethod.Post,
            $"/v1/devices/{deviceId:D}/heartbeat", credentialV1.DeviceCredential, deviceKeyV1, clock,
            new DeviceHeartbeat("0.10.0", "Windows 11 24H2", "HEALTHY", 0, clock.UtcNow));
        using HttpResponseMessage staleResponse = await client.SendAsync(staleHeartbeat);
        Assert.Equal(HttpStatusCode.Unauthorized, staleResponse.StatusCode);

        DeviceCredentialResult credentialV2 = await ExchangeCredential(client, deviceId, deviceKeyV2, keyVersion: 2);
        using HttpRequestMessage freshHeartbeat = DeviceDpopRequest(client, HttpMethod.Post,
            $"/v1/devices/{deviceId:D}/heartbeat", credentialV2.DeviceCredential, deviceKeyV2, clock,
            new DeviceHeartbeat("0.10.0", "Windows 11 24H2", "HEALTHY", 0, clock.UtcNow));
        using HttpResponseMessage freshResponse = await client.SendAsync(freshHeartbeat);
        Assert.Equal(HttpStatusCode.NoContent, freshResponse.StatusCode);

        using (HttpRequestMessage revoke = Request(HttpMethod.Delete, $"/v1/devices/{deviceId:D}",
                   "rotate-owner", "owner@rotate.test", tenant.Id, clock, true))
        {
            revoke.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
            using HttpResponseMessage response = await client.SendAsync(revoke);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
        using HttpRequestMessage pollAfterRevoke = DeviceDpopRequest(client, HttpMethod.Get,
            $"/v1/devices/{deviceId:D}/pending-session-requests?waitSeconds=0", credentialV2.DeviceCredential, deviceKeyV2, clock);
        using HttpResponseMessage revokedResponse = await client.SendAsync(pollAfterRevoke);
        Assert.Equal(HttpStatusCode.Unauthorized, revokedResponse.StatusCode);
    }

    private static async Task CreateAndActivatePolicy(HttpClient client, Guid tenantId, ManagedHostClock clock,
        string subject, string email)
    {
        JsonElement document = JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 1,
            rules = new object[]
            {
                new
                {
                    id = "allow-managed-attended",
                    effect = "ALLOW",
                    subjects = new { roles = OperatorRole },
                    resources = new { allDevices = true },
                    sessionTypes = ManagedAttendedSessionType,
                    scopes = ManagedScopes,
                    conditions = new { requireLocalConsent = true },
                    limits = new { maxSessionDurationSeconds = 1800 },
                },
            },
        });
        PolicyContract policy;
        using (HttpRequestMessage create = Request(HttpMethod.Post, "/v1/tenant/policies", subject,
                   email, tenantId, clock, true, new PolicyDocumentRequest("Managed", document, null)))
        {
            create.Headers.Add("Idempotency-Key", "create-managed-policy-0001");
            using HttpResponseMessage response = await client.SendAsync(create);
            response.EnsureSuccessStatusCode();
            policy = (await response.Content.ReadFromJsonAsync<PolicyContract>())!;
        }
        using HttpRequestMessage activate = Request(HttpMethod.Post, $"/v1/tenant/policies/{policy.Id:D}/activate",
            subject, email, tenantId, clock, true, new ActivatePolicyVersionRequest(1));
        activate.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        using HttpResponseMessage activateResponse = await client.SendAsync(activate);
        activateResponse.EnsureSuccessStatusCode();
    }

    private static async Task<Guid> EnrollDevice(HttpClient client, Guid tenantId, ManagedHostClock clock,
        ManagedHostKey key, string subject, string email)
    {
        EnrollmentTokenResult token;
        using (HttpRequestMessage createToken = Request(HttpMethod.Post, "/v1/device-enrollment-tokens", subject,
                   email, tenantId, clock, true, new EnrollmentTokenRequest(3600, null, 1)))
        {
            createToken.Headers.Add("Idempotency-Key", $"create-device-token-{Guid.NewGuid():N}");
            using HttpResponseMessage response = await client.SendAsync(createToken);
            response.EnsureSuccessStatusCode();
            token = (await response.Content.ReadFromJsonAsync<EnrollmentTokenResult>())!;
        }
        DeviceEnrollmentRequest unsigned = new(token.EnrollmentToken, Guid.NewGuid(), key.Jwk,
            new DeviceInfo("Managed Host Lab", "Windows 11 24H2", "x64", "0.10.0"),
            new ProofOfPossession("enrollment-nonce-managed-0001", string.Empty, key.Jwk, "ecdsa-p256-sha256-p1363"));
        DeviceEnrollmentRequest signed = unsigned with
        {
            Proof = unsigned.Proof with
            {
                Signature = key.SignLowS(GovernanceService.CreateEnrollmentProofBytes(unsigned,
                    ControlPlaneCrypto.Thumbprint(key.Jwk))),
            },
        };
        using HttpRequestMessage request = new(HttpMethod.Post, "/v1/devices/enrollments") { Content = JsonContent.Create(signed) };
        request.Headers.Add("Idempotency-Key", $"enroll-managed-{Guid.NewGuid():N}");
        using HttpResponseMessage enrollResponse = await client.SendAsync(request);
        enrollResponse.EnsureSuccessStatusCode();
        DeviceEnrollmentResult enrolled = (await enrollResponse.Content.ReadFromJsonAsync<DeviceEnrollmentResult>())!;
        return enrolled.DeviceId;
    }

    private static async Task<DeviceCredentialResult> ExchangeCredential(HttpClient client, Guid deviceId,
        ManagedHostKey key, int keyVersion)
    {
        DeviceCredentialChallenge challenge;
        using (HttpRequestMessage challengeRequest = new(HttpMethod.Post, $"/v1/devices/{deviceId:D}/credential-challenges")
        { Content = JsonContent.Create(new DeviceCredentialChallengeRequest(keyVersion, "CREDENTIAL_REFRESH")) })
        using (HttpResponseMessage response = await client.SendAsync(challengeRequest))
        {
            response.EnsureSuccessStatusCode();
            challenge = (await response.Content.ReadFromJsonAsync<DeviceCredentialChallenge>())!;
        }
        Assert.Equal("RSP-DEVICE-CREDENTIAL-V1", challenge.CanonicalizationVersion);
        byte[] proofBytes = GovernanceService.CreateDeviceCredentialProofBytes("RSP-DEVICE-CREDENTIAL-V1", deviceId,
            challenge.ChallengeId, "CREDENTIAL_REFRESH", challenge.Nonce);
        string signature = key.SignLowS(proofBytes);
        DeviceCredentialExchangeRequest exchange = new(challenge.ChallengeId, keyVersion,
            new DetachedProof(challenge.Nonce, ControlPlaneCrypto.Thumbprint(key.Jwk), "ecdsa-p256-sha256-p1363", signature));
        using HttpRequestMessage exchangeRequest = new(HttpMethod.Post, $"/v1/devices/{deviceId:D}/credentials")
        { Content = JsonContent.Create(exchange) };
        using HttpResponseMessage exchangeResponse = await client.SendAsync(exchangeRequest);
        exchangeResponse.EnsureSuccessStatusCode();
        return (await exchangeResponse.Content.ReadFromJsonAsync<DeviceCredentialResult>())!;
    }

    private static HttpRequestMessage DeviceDpopRequest(HttpClient client, HttpMethod method, string path,
        string token, ManagedHostKey key, ManagedHostClock clock, object? content = null)
    {
        Uri target = new(client.BaseAddress!, path.Split('?')[0]);
        HttpRequestMessage request = new(method, path) { Content = content is null ? null : JsonContent.Create(content) };
        request.Headers.Authorization = new AuthenticationHeaderValue("DPoP", token);
        request.Headers.TryAddWithoutValidation("DPoP", key.CreateDpopProof(method.Method, target, token,
            clock.UtcNow, $"dpop-{Guid.NewGuid():N}"));
        return request;
    }

    private static async Task<PeerAuthorization> AuthorizePeer(HttpClient client, ManagedHostFactory factory,
        Guid sessionId, string bootstrap, string role, ManagedHostKey key)
    {
        using HttpRequestMessage challengeRequest = new(HttpMethod.Post,
            $"/v1/sessions/{sessionId:D}/peer-authorization-challenges");
        challengeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bootstrap);
        using HttpResponseMessage challengeResponse = await client.SendAsync(challengeRequest);
        challengeResponse.EnsureSuccessStatusCode();
        PeerAuthorizationChallenge challenge = (await challengeResponse.Content.ReadFromJsonAsync<PeerAuthorizationChallenge>())!;
        SessionAggregate aggregate = factory.Services.GetRequiredService<AttendedSessionService>().Snapshot()
            .Single(session => session.Id == sessionId);
        ChallengeRecord record = aggregate.Challenges[challenge.ChallengeId];
        PeerRecord peer = role == "HOST" ? aggregate.Host! : aggregate.Operator!;
        string signature = key.SignLowS(ControlPlaneCrypto.PeerAuthorizationBytes(aggregate, record, peer, challenge.Nonce));
        PeerAuthorizationRequest body = new(challenge.ChallengeId, role,
            new ProofOfPossession(challenge.Nonce, signature, key.Jwk, "ecdsa-p256-sha256-p1363"));
        using HttpRequestMessage authorizeRequest = new(HttpMethod.Post, $"/v1/sessions/{sessionId:D}/peer-authorization")
        { Content = JsonContent.Create(body) };
        authorizeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bootstrap);
        using HttpResponseMessage response = await client.SendAsync(authorizeRequest);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PeerAuthorization>())!;
    }

    private static async Task<TenantContract> CreateTenant(HttpClient client, string subject, string email,
        string slug, ManagedHostClock clock)
    {
        using HttpRequestMessage request = Request(HttpMethod.Post, "/v1/tenants", subject, email, null, clock,
            true, new CreateTenantRequest(slug, slug, "KR"));
        request.Headers.Add("Idempotency-Key", $"create-{slug}-00000001");
        using HttpResponseMessage response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TenantContract>())!;
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string subject, string email,
        Guid? tenantId, ManagedHostClock clock, bool mfa, object? content = null)
    {
        HttpRequestMessage request = new(method, path);
        request.Headers.Add("X-Test-Operator-Subject", subject);
        request.Headers.Add("X-Test-Operator-Name", subject);
        request.Headers.Add("X-Test-Operator-Email", email);
        request.Headers.Add("X-Test-Tenant-Id", tenantId?.ToString("D") ?? string.Empty);
        if (tenantId is { } selectedTenant) request.Headers.Add("X-Tenant-Id", selectedTenant.ToString("D"));
        request.Headers.Add("X-Test-Tenant-Name", "Test Tenant");
        request.Headers.Add("X-Test-Tenant-Verified", "true");
        request.Headers.Add("X-Test-Mfa", mfa ? "true" : "false");
        request.Headers.Add("X-Test-Auth-Time", clock.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        if (content is not null) request.Content = JsonContent.Create(content);
        return request;
    }

    private sealed class ManagedHostFactory(ManagedHostClock clock) : WebApplicationFactory<Program>
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
                    ["Governance:ExportDirectory"] = "artifacts/test-governance-exports",
                    ["Governance:ClosureCoolingOffDays"] = "7",
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISystemClock>();
                services.AddSingleton<ISystemClock>(clock);
            });
        }
    }

    private sealed class ManagedHostClock(DateTimeOffset initial) : ISystemClock
    {
        public DateTimeOffset UtcNow { get; private set; } = initial;
        public void Advance(TimeSpan duration) => UtcNow += duration;
    }

    private sealed class ManagedHostKey : IDisposable
    {
        private static readonly BigInteger Order = BigInteger.Parse(
            "00FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551",
            NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        private static readonly BigInteger HalfOrder = Order / 2;
        private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        public ManagedHostKey()
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
