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

public sealed class UnattendedAccessApiTests
{
    private static readonly string[] UnattendedScopes = ["VIEW_SCREEN", "UNATTENDED_SESSION"];
    private static readonly string[] UnattendedRoles = ["OWNER", "OPERATOR"];
    private static readonly string[] UnattendedSessionTypeArray = ["UNATTENDED"];

    [Fact]
    [Trait("Requirement", "AT-FR-UNA-001")]
    public async Task UnattendedSessionRequiresDeviceOptInFeatureFlagAndFreshMfaIndependently()
    {
        UnattendedClock clock = new(new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero));
        await using UnattendedFactory factory = new(clock);
        using HttpClient client = factory.CreateClient();
        TenantContract tenant = await CreateTenant(client, "unattended-owner", "owner@unattended.test", "unattended-tenant", clock);

        using UnattendedKey deviceKey = new();
        Guid deviceId = await EnrollDevice(client, tenant.Id, clock, deviceKey, "unattended-owner", "owner@unattended.test");
        using UnattendedKey operatorKey = new();

        // Fails before the tenant-level UNATTENDED_ACCESS feature is enabled, the policy
        // grants UNATTENDED_SESSION, or the device has completed enrollment: policy denies
        // regardless of any single missing gate.
        await AssertSessionCreationDenied(client, tenant.Id, deviceId, clock, operatorKey, mfa: true);

        await EnableUnattendedFeatureAndPolicy(client, tenant.Id, clock);
        // Policy and feature are now satisfied, but the device has not completed the
        // two-party unattended enrollment yet.
        await AssertSessionCreationDenied(client, tenant.Id, deviceId, clock, operatorKey, mfa: true);

        await EnrollDeviceForUnattended(client, tenant.Id, deviceId, clock, deviceKey, "unattended-owner", "owner@unattended.test");
        // Device is enrolled and policy/feature are satisfied, but this request lacks fresh MFA.
        await AssertSessionCreationDenied(client, tenant.Id, deviceId, clock, operatorKey, mfa: false);

        // All three gates satisfied together succeed.
        using HttpRequestMessage createSession = Request(HttpMethod.Post, $"/v1/devices/{deviceId:D}/sessions",
            "unattended-owner", "owner@unattended.test", tenant.Id, clock, mfa: true,
            new ManagedSessionRequest(UnattendedScopes, "UNATTENDED", operatorKey.Jwk, 1800));
        createSession.Headers.Add("Idempotency-Key", "create-unattended-session-0001");
        using HttpResponseMessage response = await client.SendAsync(createSession);
        response.EnsureSuccessStatusCode();
        ManagedSessionCreated created = (await response.Content.ReadFromJsonAsync<ManagedSessionCreated>())!;
        Assert.Equal("HOST_PENDING", created.Session.State);
        Assert.Equal("UNATTENDED", created.Session.SessionType);
    }

    [Fact]
    [Trait("Requirement", "AT-FR-UNA-002")]
    public async Task EnrollmentConfirmationRequiresTheEnrolledDevicesOwnKeyNotJustTheCode()
    {
        UnattendedClock clock = new(new DateTimeOffset(2026, 7, 4, 12, 30, 0, TimeSpan.Zero));
        await using UnattendedFactory factory = new(clock);
        using HttpClient client = factory.CreateClient();
        TenantContract tenant = await CreateTenant(client, "confirm-owner", "owner@confirm.test", "confirm-tenant", clock);
        using UnattendedKey deviceKey = new();
        Guid deviceId = await EnrollDevice(client, tenant.Id, clock, deviceKey, "confirm-owner", "owner@confirm.test");
        DeviceCredentialResult credential = await ExchangeCredential(client, deviceId, deviceKey, keyVersion: 1);

        UnattendedEnrollmentRequestResult pending;
        using (HttpRequestMessage requestEnrollment = Request(HttpMethod.Post,
                   $"/v1/devices/{deviceId:D}/unattended-enrollment-requests", "confirm-owner", "owner@confirm.test",
                   tenant.Id, clock, mfa: true))
        using (HttpResponseMessage response = await client.SendAsync(requestEnrollment))
        {
            response.EnsureSuccessStatusCode();
            pending = (await response.Content.ReadFromJsonAsync<UnattendedEnrollmentRequestResult>())!;
        }

        // An attacker who only knows the code, without the device's private key, cannot confirm.
        using UnattendedKey attackerKey = new();
        using (HttpRequestMessage forged = DeviceDpopRequest(client, HttpMethod.Post,
                   $"/v1/devices/{deviceId:D}/unattended-enrollment-confirmations", "forged-token", attackerKey, clock,
                   new UnattendedEnrollmentConfirmRequest(pending.RequestId, pending.ConfirmationCode)))
        using (HttpResponseMessage response = await client.SendAsync(forged))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        // The legitimate device, holding both the code and its own key, can confirm.
        using HttpRequestMessage genuine = DeviceDpopRequest(client, HttpMethod.Post,
            $"/v1/devices/{deviceId:D}/unattended-enrollment-confirmations", credential.DeviceCredential, deviceKey, clock,
            new UnattendedEnrollmentConfirmRequest(pending.RequestId, pending.ConfirmationCode));
        using HttpResponseMessage genuineResponse = await client.SendAsync(genuine);
        Assert.Equal(HttpStatusCode.NoContent, genuineResponse.StatusCode);

        using HttpRequestMessage read = Request(HttpMethod.Get, $"/v1/devices/{deviceId:D}", "confirm-owner",
            "owner@confirm.test", tenant.Id, clock, mfa: false);
        using HttpResponseMessage readResponse = await client.SendAsync(read);
        DeviceContract device = (await readResponse.Content.ReadFromJsonAsync<DeviceContract>())!;
        Assert.True(device.UnattendedEnabled);
    }

    [Fact]
    [Trait("Requirement", "AT-FR-UNA-003")]
    public async Task RevokingADeviceCancelsItsActiveUnattendedSession()
    {
        UnattendedClock clock = new(new DateTimeOffset(2026, 7, 4, 13, 0, 0, TimeSpan.Zero));
        await using UnattendedFactory factory = new(clock);
        using HttpClient client = factory.CreateClient();
        TenantContract tenant = await CreateTenant(client, "cancel-owner", "owner@cancel.test", "cancel-tenant", clock);
        using UnattendedKey deviceKey = new();
        Guid deviceId = await EnrollDevice(client, tenant.Id, clock, deviceKey, "cancel-owner", "owner@cancel.test");
        DeviceCredentialResult credential = await ExchangeCredential(client, deviceId, deviceKey, keyVersion: 1);
        await EnableUnattendedFeatureAndPolicy(client, tenant.Id, clock, "cancel-owner", "owner@cancel.test");
        await EnrollDeviceForUnattended(client, tenant.Id, deviceId, clock, deviceKey, "cancel-owner", "owner@cancel.test");

        using UnattendedKey operatorKey = new();
        ManagedSessionCreated created;
        using (HttpRequestMessage createSession = Request(HttpMethod.Post, $"/v1/devices/{deviceId:D}/sessions",
                   "cancel-owner", "owner@cancel.test", tenant.Id, clock, mfa: true,
                   new ManagedSessionRequest(UnattendedScopes, "UNATTENDED", operatorKey.Jwk, 1800)))
        {
            createSession.Headers.Add("Idempotency-Key", "create-cancel-session-0001");
            using HttpResponseMessage response = await client.SendAsync(createSession);
            response.EnsureSuccessStatusCode();
            created = (await response.Content.ReadFromJsonAsync<ManagedSessionCreated>())!;
        }

        PendingManagedSessionRequest item;
        using (HttpRequestMessage poll = DeviceDpopRequest(client, HttpMethod.Get,
                   $"/v1/devices/{deviceId:D}/pending-session-requests?waitSeconds=1", credential.DeviceCredential, deviceKey, clock))
        using (HttpResponseMessage response = await client.SendAsync(poll))
        {
            response.EnsureSuccessStatusCode();
            item = Assert.Single((await response.Content.ReadFromJsonAsync<PagedManagedSessionRequests>())!.Items);
        }

        using UnattendedKey hostKey = new();
        string hostThumbprint = ControlPlaneCrypto.Thumbprint(hostKey.Jwk);
        ManagedHostDecisionRequest decisionRequest = new(true, UnattendedScopes, item.ConsentNonce, hostKey.Jwk,
            new DetachedProof("d", "d", "d", "d"));
        string signature = deviceKey.SignLowS(AttendedSessionService.ManagedHostDecisionProofBytes(item.SessionId, decisionRequest, hostThumbprint));
        decisionRequest = decisionRequest with
        {
            DecisionProof = new DetachedProof("n", ControlPlaneCrypto.Thumbprint(deviceKey.Jwk), "ecdsa-p256-sha256-p1363", signature),
        };
        using (HttpRequestMessage decide = DeviceDpopRequest(client, HttpMethod.Post,
                   $"/v1/sessions/{item.SessionId:D}/managed-host-decision", credential.DeviceCredential, deviceKey, clock, decisionRequest))
        {
            decide.Headers.TryAddWithoutValidation("If-Match", $"\"{item.StateVersion}\"");
            using HttpResponseMessage response = await client.SendAsync(decide);
            response.EnsureSuccessStatusCode();
        }

        SessionAggregate authorized = factory.Services.GetRequiredService<AttendedSessionService>().Snapshot()
            .Single(session => session.Id == item.SessionId);
        Assert.Equal("AUTHORIZED", authorized.State);

        using (HttpRequestMessage revoke = Request(HttpMethod.Delete, $"/v1/devices/{deviceId:D}", "cancel-owner",
                   "owner@cancel.test", tenant.Id, clock, mfa: true))
        {
            revoke.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
            using HttpResponseMessage response = await client.SendAsync(revoke);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        SessionAggregate cancelled = factory.Services.GetRequiredService<AttendedSessionService>().Snapshot()
            .Single(session => session.Id == item.SessionId);
        Assert.Equal("CANCELLED", cancelled.State);
        Assert.Empty(cancelled.GrantedScopes);
    }

    private static async Task AssertSessionCreationDenied(HttpClient client, Guid tenantId, Guid deviceId,
        UnattendedClock clock, UnattendedKey operatorKey, bool mfa)
    {
        using HttpRequestMessage createSession = Request(HttpMethod.Post, $"/v1/devices/{deviceId:D}/sessions",
            "unattended-owner", "owner@unattended.test", tenantId, clock, mfa,
            new ManagedSessionRequest(UnattendedScopes, "UNATTENDED", operatorKey.Jwk, 1800));
        createSession.Headers.Add("Idempotency-Key", $"create-unattended-denied-{Guid.NewGuid():N}");
        using HttpResponseMessage response = await client.SendAsync(createSession);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task EnableUnattendedFeatureAndPolicy(HttpClient client, Guid tenantId, UnattendedClock clock,
        string subject = "unattended-owner", string email = "owner@unattended.test")
    {
        using HttpRequestMessage settings = Request(HttpMethod.Patch, "/v1/tenant/settings", subject,
            email, tenantId, clock, mfa: true,
            new TenantSettingsPatch(90, ["VIEW_SCREEN", "REMOTE_INPUT", "UNATTENDED_ACCESS"], null));
        settings.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        using HttpResponseMessage settingsResponse = await client.SendAsync(settings);
        settingsResponse.EnsureSuccessStatusCode();

        JsonElement document = JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 1,
            rules = new object[]
            {
                new
                {
                    id = "allow-unattended",
                    effect = "ALLOW",
                    subjects = new { roles = UnattendedRoles },
                    resources = new { allDevices = true },
                    sessionTypes = UnattendedSessionTypeArray,
                    scopes = UnattendedScopes,
                },
            },
        });
        PolicyContract policy;
        using (HttpRequestMessage create = Request(HttpMethod.Post, "/v1/tenant/policies", subject,
                   email, tenantId, clock, mfa: true, new PolicyDocumentRequest("Unattended", document, null)))
        {
            create.Headers.Add("Idempotency-Key", $"create-unattended-policy-{Guid.NewGuid():N}");
            using HttpResponseMessage response = await client.SendAsync(create);
            response.EnsureSuccessStatusCode();
            policy = (await response.Content.ReadFromJsonAsync<PolicyContract>())!;
        }
        using HttpRequestMessage activate = Request(HttpMethod.Post, $"/v1/tenant/policies/{policy.Id:D}/activate",
            subject, email, tenantId, clock, mfa: true, new ActivatePolicyVersionRequest(1));
        activate.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        using HttpResponseMessage activateResponse = await client.SendAsync(activate);
        activateResponse.EnsureSuccessStatusCode();
    }

    private static async Task EnrollDeviceForUnattended(HttpClient client, Guid tenantId, Guid deviceId,
        UnattendedClock clock, UnattendedKey deviceKey, string subject, string email)
    {
        DeviceCredentialResult credential = await ExchangeCredential(client, deviceId, deviceKey, keyVersion: 1);
        UnattendedEnrollmentRequestResult pending;
        using (HttpRequestMessage requestEnrollment = Request(HttpMethod.Post,
                   $"/v1/devices/{deviceId:D}/unattended-enrollment-requests", subject, email, tenantId, clock, mfa: true))
        using (HttpResponseMessage response = await client.SendAsync(requestEnrollment))
        {
            response.EnsureSuccessStatusCode();
            pending = (await response.Content.ReadFromJsonAsync<UnattendedEnrollmentRequestResult>())!;
        }
        using HttpRequestMessage confirm = DeviceDpopRequest(client, HttpMethod.Post,
            $"/v1/devices/{deviceId:D}/unattended-enrollment-confirmations", credential.DeviceCredential, deviceKey, clock,
            new UnattendedEnrollmentConfirmRequest(pending.RequestId, pending.ConfirmationCode));
        using HttpResponseMessage confirmResponse = await client.SendAsync(confirm);
        confirmResponse.EnsureSuccessStatusCode();
    }

    private static async Task<Guid> EnrollDevice(HttpClient client, Guid tenantId, UnattendedClock clock,
        UnattendedKey key, string subject, string email)
    {
        EnrollmentTokenResult token;
        using (HttpRequestMessage createToken = Request(HttpMethod.Post, "/v1/device-enrollment-tokens", subject,
                   email, tenantId, clock, mfa: true, new EnrollmentTokenRequest(3600, null, 1)))
        {
            createToken.Headers.Add("Idempotency-Key", $"create-device-token-{Guid.NewGuid():N}");
            using HttpResponseMessage response = await client.SendAsync(createToken);
            response.EnsureSuccessStatusCode();
            token = (await response.Content.ReadFromJsonAsync<EnrollmentTokenResult>())!;
        }
        DeviceEnrollmentRequest unsigned = new(token.EnrollmentToken, Guid.NewGuid(), key.Jwk,
            new DeviceInfo("Unattended Lab Host", "Windows 11 24H2", "x64", "0.14.0"),
            new ProofOfPossession($"enrollment-nonce-{Guid.NewGuid():N}", string.Empty, key.Jwk, "ecdsa-p256-sha256-p1363"));
        DeviceEnrollmentRequest signed = unsigned with
        {
            Proof = unsigned.Proof with
            {
                Signature = key.SignLowS(GovernanceService.CreateEnrollmentProofBytes(unsigned,
                    ControlPlaneCrypto.Thumbprint(key.Jwk))),
            },
        };
        using HttpRequestMessage request = new(HttpMethod.Post, "/v1/devices/enrollments") { Content = JsonContent.Create(signed) };
        request.Headers.Add("Idempotency-Key", $"enroll-unattended-{Guid.NewGuid():N}");
        using HttpResponseMessage enrollResponse = await client.SendAsync(request);
        enrollResponse.EnsureSuccessStatusCode();
        DeviceEnrollmentResult enrolled = (await enrollResponse.Content.ReadFromJsonAsync<DeviceEnrollmentResult>())!;
        return enrolled.DeviceId;
    }

    private static async Task<DeviceCredentialResult> ExchangeCredential(HttpClient client, Guid deviceId,
        UnattendedKey key, int keyVersion)
    {
        DeviceCredentialChallenge challenge;
        using (HttpRequestMessage challengeRequest = new(HttpMethod.Post, $"/v1/devices/{deviceId:D}/credential-challenges")
        { Content = JsonContent.Create(new DeviceCredentialChallengeRequest(keyVersion, "CREDENTIAL_REFRESH")) })
        using (HttpResponseMessage response = await client.SendAsync(challengeRequest))
        {
            response.EnsureSuccessStatusCode();
            challenge = (await response.Content.ReadFromJsonAsync<DeviceCredentialChallenge>())!;
        }
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
        string token, UnattendedKey key, UnattendedClock clock, object? content = null)
    {
        Uri target = new(client.BaseAddress!, path.Split('?')[0]);
        HttpRequestMessage request = new(method, path) { Content = content is null ? null : JsonContent.Create(content) };
        request.Headers.Authorization = new AuthenticationHeaderValue("DPoP", token);
        request.Headers.TryAddWithoutValidation("DPoP", key.CreateDpopProof(method.Method, target, token, clock.UtcNow,
            $"dpop-{Guid.NewGuid():N}"));
        return request;
    }

    private static async Task<TenantContract> CreateTenant(HttpClient client, string subject, string email,
        string slug, UnattendedClock clock)
    {
        using HttpRequestMessage request = Request(HttpMethod.Post, "/v1/tenants", subject, email, null, clock,
            mfa: true, new CreateTenantRequest(slug, slug, "KR"));
        request.Headers.Add("Idempotency-Key", $"create-{slug}-00000001");
        using HttpResponseMessage response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TenantContract>())!;
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string subject, string email,
        Guid? tenantId, UnattendedClock clock, bool mfa, object? content = null)
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

    private sealed class UnattendedFactory(UnattendedClock clock) : WebApplicationFactory<Program>
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

    private sealed class UnattendedClock(DateTimeOffset initial) : ISystemClock
    {
        public DateTimeOffset UtcNow { get; private set; } = initial;
        public void Advance(TimeSpan duration) => UtcNow += duration;
    }

    private sealed class UnattendedKey : IDisposable
    {
        private static readonly BigInteger Order = BigInteger.Parse(
            "00FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551",
            NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        private static readonly BigInteger HalfOrder = Order / 2;
        private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        public UnattendedKey()
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
            byte[] signature = key.SignData(bytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
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
                ath = ControlPlaneCrypto.Base64UrlEncode(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(token))),
            }));
            string input = encodedHeader + "." + encodedPayload;
            byte[] signature = key.SignData(System.Text.Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            return input + "." + ControlPlaneCrypto.Base64UrlEncode(signature);
        }

        public void Dispose() => key.Dispose();
    }
}
