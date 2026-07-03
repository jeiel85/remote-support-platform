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

public sealed class GovernanceApiTests
{
    private static readonly string[] PolicySubjects = ["OWNER", "OPERATOR"];
    private static readonly string[] AttendedSessionTypes = ["ATTENDED"];
    private static readonly string[] ViewPointerScopes = ["VIEW_SCREEN", "CONTROL_POINTER"];
    private static readonly string[] PointerScope = ["CONTROL_POINTER"];
    private static readonly string[] TransportFeatures = ["transport-binding-v1"];
    private static readonly string[] H264Codecs = ["H264"];
    private static readonly string[] ViewScope = ["VIEW_SCREEN"];
    private static readonly string[] ChatScope = ["CHAT"];
    private static readonly string[] Saturday = ["SA"];

    [Fact]
    [Trait("Requirement", "AT-FR-ADM-001")]
    [Trait("Requirement", "AT-FR-ADM-007")]
    [Trait("Requirement", "AT-NFR-SEC-010")]
    public async Task RoleMatrixMembershipWorkflowAndTenantIsolationFailClosed()
    {
        GovernanceClock clock = new(new DateTimeOffset(2026, 7, 3, 0, 0, 0, TimeSpan.Zero));
        await using GovernanceFactory factory = new(clock);
        using HttpClient client = factory.CreateClient();
        TenantContract alpha = await CreateTenant(client, "owner-alpha", "owner@alpha.test", "alpha-tenant", clock);
        TenantContract beta = await CreateTenant(client, "owner-beta", "owner@beta.test", "beta-tenant", clock);

        using HttpRequestMessage crossTenant = Request(HttpMethod.Get, "/v1/tenant", "owner-alpha",
            "owner@alpha.test", beta.Id, clock, true);
        using HttpResponseMessage crossResponse = await client.SendAsync(crossTenant);
        Assert.Equal(HttpStatusCode.NotFound, crossResponse.StatusCode);

        InvitationContract invitation;
        using (HttpRequestMessage invite = Request(HttpMethod.Post, "/v1/tenant/invitations", "owner-alpha",
                   "owner@alpha.test", alpha.Id, clock, true, new InvitationRequest("auditor@alpha.test",
                       ["SECURITY_AUDITOR"], 7200)))
        {
            invite.Headers.Add("Idempotency-Key", "invite-auditor-alpha-0001");
            using HttpResponseMessage response = await client.SendAsync(invite);
            response.EnsureSuccessStatusCode();
            invitation = (await response.Content.ReadFromJsonAsync<InvitationContract>())!;
        }
        Assert.NotNull(invitation.AcceptanceToken);

        using (HttpRequestMessage accept = Request(HttpMethod.Post, $"/v1/invitations/{invitation.Id:D}/accept",
                   "auditor-alpha", "auditor@alpha.test", null, clock, false,
                   new InvitationAcceptanceRequest(invitation.AcceptanceToken!)))
        using (HttpResponseMessage response = await client.SendAsync(accept))
        {
            response.EnsureSuccessStatusCode();
        }

        using (HttpRequestMessage read = Request(HttpMethod.Get, "/v1/tenant/memberships", "auditor-alpha",
                   "auditor@alpha.test", alpha.Id, clock, false))
        using (HttpResponseMessage response = await client.SendAsync(read))
        {
            response.EnsureSuccessStatusCode();
            PagedMemberships page = (await response.Content.ReadFromJsonAsync<PagedMemberships>())!;
            Assert.Equal(2, page.Items.Count);
        }

        using (HttpRequestMessage forbidden = Request(HttpMethod.Patch, "/v1/tenant/settings", "auditor-alpha",
                   "auditor@alpha.test", alpha.Id, clock, true, new TenantSettingsPatch(30, null, null)))
        {
            forbidden.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
            using HttpResponseMessage response = await client.SendAsync(forbidden);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        using HttpRequestMessage foreignResource = Request(HttpMethod.Get,
            $"/v1/devices/{Guid.NewGuid():D}", "owner-alpha", "owner@alpha.test", alpha.Id, clock, true);
        using HttpResponseMessage foreignResponse = await client.SendAsync(foreignResource);
        Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);

        using GovernanceKey hostKey = new();
        using GovernanceKey operatorKey = new();
        AttendedSessionCreated session;
        using (HttpRequestMessage createSession = new(HttpMethod.Post, "/v1/attended-sessions")
        {
            Content = JsonContent.Create(new CreateAttendedSessionRequest(hostKey.Jwk, "1.0.0",
                       new ClientCapabilities(1, 0, TransportFeatures, H264Codecs), null, "ko-KR")),
        })
        {
            createSession.Headers.Add("Idempotency-Key", "tenant-isolation-session-0001");
            using HttpResponseMessage response = await client.SendAsync(createSession);
            response.EnsureSuccessStatusCode();
            session = (await response.Content.ReadFromJsonAsync<AttendedSessionCreated>())!;
        }
        using (HttpRequestMessage resolve = Request(HttpMethod.Post, "/v1/attended-sessions/resolve", "owner-alpha",
                   "owner@alpha.test", alpha.Id, clock, false, new ResolveAttendedSessionRequest(session.SupportCode,
                       ViewScope, operatorKey.Jwk, "1.0.0",
                       new ClientCapabilities(1, 0, TransportFeatures, H264Codecs))))
        using (HttpResponseMessage response = await client.SendAsync(resolve))
        {
            response.EnsureSuccessStatusCode();
        }
        using HttpRequestMessage foreignSession = Request(HttpMethod.Get, $"/v1/sessions/{session.SessionId:D}",
            "owner-beta", "owner@beta.test", beta.Id, clock, false);
        using HttpResponseMessage foreignSessionResponse = await client.SendAsync(foreignSession);
        Assert.Equal(HttpStatusCode.NotFound, foreignSessionResponse.StatusCode);
    }

    [Fact]
    [Trait("Requirement", "AT-FR-ADM-002")]
    [Trait("Requirement", "AT-FR-ADM-004")]
    public async Task PolicyEvaluationIsDeterministicDenyOverridesAndMfaFailsClosed()
    {
        GovernanceClock clock = new(new DateTimeOffset(2026, 7, 3, 1, 0, 0, TimeSpan.Zero));
        await using GovernanceFactory factory = new(clock);
        using HttpClient client = factory.CreateClient();
        TenantContract tenant = await CreateTenant(client, "policy-owner", "owner@policy.test", "policy-tenant", clock);
        JsonElement document = JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 1,
            rules = new object[]
            {
                new
                {
                    id = "allow-operator",
                    effect = "ALLOW",
                    subjects = new { roles = PolicySubjects },
                    resources = new { allDevices = true },
                    sessionTypes = AttendedSessionTypes,
                    scopes = ViewPointerScopes,
                    conditions = new { requireMfa = true, maxAuthenticationAgeSeconds = 600, requireLocalConsent = true },
                    limits = new { maxSessionDurationSeconds = 1800, maxFileBytes = 1024L },
                },
                new
                {
                    id = "deny-pointer",
                    effect = "DENY",
                    subjects = new { roles = PolicySubjects },
                    resources = new { allDevices = true },
                    sessionTypes = AttendedSessionTypes,
                    scopes = PointerScope,
                },
                new
                {
                    id = "weekend-chat",
                    effect = "ALLOW",
                    subjects = new { roles = PolicySubjects },
                    resources = new { allDevices = true },
                    sessionTypes = AttendedSessionTypes,
                    scopes = ChatScope,
                    conditions = new
                    {
                        schedule = new
                        {
                            timezone = "UTC",
                            windows = new[]
                            {
                                new { daysOfWeek = Saturday, startLocal = "00:00", endLocal = "23:59" },
                            },
                        },
                    },
                },
            },
        });
        PolicyContract policy;
        using (HttpRequestMessage create = Request(HttpMethod.Post, "/v1/tenant/policies", "policy-owner",
                   "owner@policy.test", tenant.Id, clock, true, new PolicyDocumentRequest("Default", document, null)))
        {
            create.Headers.Add("Idempotency-Key", "create-default-policy-0001");
            using HttpResponseMessage response = await client.SendAsync(create);
            response.EnsureSuccessStatusCode();
            policy = (await response.Content.ReadFromJsonAsync<PolicyContract>())!;
        }
        using (HttpRequestMessage activate = Request(HttpMethod.Post,
                   $"/v1/tenant/policies/{policy.Id:D}/activate", "policy-owner", "owner@policy.test", tenant.Id,
                   clock, true, new ActivatePolicyVersionRequest(1)))
        {
            activate.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
            using HttpResponseMessage response = await client.SendAsync(activate);
            response.EnsureSuccessStatusCode();
        }

        PolicyDecisionContract first = await Evaluate(client, tenant.Id, clock, true,
            ["VIEW_SCREEN", "CONTROL_POINTER"]);
        PolicyDecisionContract second = await Evaluate(client, tenant.Id, clock, true,
            ["VIEW_SCREEN", "CONTROL_POINTER"]);
        Assert.True(first.Allow);
        Assert.Equal(["VIEW_SCREEN"], first.GrantedScopes);
        Assert.Contains(first.DeniedScopes, value => value.Scope == "CONTROL_POINTER" && value.ReasonCode == "DENY_RULE");
        Assert.True(first.RequiresLocalConsent);
        Assert.Equal(first.InputHash, second.InputHash);
        Assert.Equal(first.PolicyVersionIds, second.PolicyVersionIds);

        PolicyDecisionContract noMfa = await Evaluate(client, tenant.Id, clock, false, ["VIEW_SCREEN"]);
        Assert.False(noMfa.Allow);
        Assert.True(noMfa.RequiresStepUpMfa);
        Assert.Equal("MFA_REQUIRED", Assert.Single(noMfa.DeniedScopes).ReasonCode);

        PolicyDecisionContract outsideSchedule = await Evaluate(client, tenant.Id, clock, true, ChatScope);
        Assert.False(outsideSchedule.Allow);
        Assert.Equal("NO_MATCHING_ALLOW_RULE", Assert.Single(outsideSchedule.DeniedScopes).ReasonCode);

        using HttpRequestMessage settings = Request(HttpMethod.Patch, "/v1/tenant/settings", "policy-owner",
            "owner@policy.test", tenant.Id, clock, true,
            new TenantSettingsPatch(30, ["VIEW_SCREEN", "FILE_TRANSFER"], 2048));
        settings.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        using HttpResponseMessage settingsResponse = await client.SendAsync(settings);
        settingsResponse.EnsureSuccessStatusCode();
        TenantSettingsContract updated = (await settingsResponse.Content.ReadFromJsonAsync<TenantSettingsContract>())!;
        Assert.False(updated.RecordingEnabled);
    }

    [Fact]
    [Trait("Requirement", "AT-FR-ADM-003")]
    [Trait("Requirement", "AT-NFR-SEC-006")]
    public async Task AuditVerifierDetectsModificationDeletionAndReordering()
    {
        GovernanceClock clock = new(new DateTimeOffset(2026, 7, 3, 2, 0, 0, TimeSpan.Zero));
        await using GovernanceFactory factory = new(clock);
        using HttpClient client = factory.CreateClient();
        TenantContract tenant = await CreateTenant(client, "audit-owner", "owner@audit.test", "audit-tenant", clock);
        IGovernanceStore store = factory.Services.GetRequiredService<IGovernanceStore>();

        using (HttpRequestMessage verify = Request(HttpMethod.Get, "/v1/tenant/audit-events/verification",
                   "audit-owner", "owner@audit.test", tenant.Id, clock, false))
        using (HttpResponseMessage response = await client.SendAsync(verify))
        {
            response.EnsureSuccessStatusCode();
            Assert.True((await response.Content.ReadFromJsonAsync<AuditVerificationContract>())!.Valid);
        }

        TenantAggregate aggregate = store.Snapshot(tenant.Id)!;
        aggregate.AuditEvents[0] = aggregate.AuditEvents[0] with { EventHash = new string('0', 64) };
        AuditVerificationContract modified = GovernanceAudit.Verify(aggregate, clock.UtcNow);
        Assert.False(modified.Valid);
        Assert.Equal("AUDIT_EVENT_HASH_MISMATCH", modified.ErrorCode);

        aggregate.AuditEvents[0] = aggregate.AuditEvents[0] with { EventHash = new string('1', 64), Sequence = 2 };
        AuditVerificationContract gap = GovernanceAudit.Verify(aggregate, clock.UtcNow);
        Assert.False(gap.Valid);
        Assert.Equal("AUDIT_SEQUENCE_GAP", gap.ErrorCode);
    }

    [Fact]
    [Trait("Requirement", "AT-FR-ADM-006")]
    public async Task PlatformSupportRequiresScopedUnexpiredGrantAndEveryReadIsAudited()
    {
        GovernanceClock clock = new(new DateTimeOffset(2026, 7, 3, 2, 30, 0, TimeSpan.Zero));
        await using GovernanceFactory factory = new(clock);
        using HttpClient client = factory.CreateClient();
        TenantContract tenant = await CreateTenant(client, "support-owner", "owner@support.test", "support-tenant", clock);
        SupportGrantContract grant;
        using (HttpRequestMessage create = Request(HttpMethod.Post, "/v1/tenant/support-grants", "support-owner",
                   "owner@support.test", tenant.Id, clock, true,
                   new SupportGrantRequest(tenant.Id, "platform-support-1", "CASE_1001", 15, false)))
        using (HttpResponseMessage response = await client.SendAsync(create))
        {
            response.EnsureSuccessStatusCode();
            grant = (await response.Content.ReadFromJsonAsync<SupportGrantContract>())!;
        }

        using HttpRequestMessage denied = Request(HttpMethod.Get,
            $"/internal/v1/support/tenants/{tenant.Id:D}?grantId={grant.Id:D}", "wrong-support",
            "support@platform.test", null, clock, true);
        denied.Headers.Add("X-Test-Platform-Role", "PLATFORM_SUPPORT");
        using HttpResponseMessage deniedResponse = await client.SendAsync(denied);
        Assert.Equal(HttpStatusCode.NotFound, deniedResponse.StatusCode);

        using HttpRequestMessage allowed = Request(HttpMethod.Get,
            $"/internal/v1/support/tenants/{tenant.Id:D}?grantId={grant.Id:D}", "platform-support-1",
            "support@platform.test", null, clock, true);
        allowed.Headers.Add("X-Test-Platform-Role", "PLATFORM_SUPPORT");
        using HttpResponseMessage allowedResponse = await client.SendAsync(allowed);
        allowedResponse.EnsureSuccessStatusCode();
        TenantAggregate aggregate = factory.Services.GetRequiredService<IGovernanceStore>().Snapshot(tenant.Id)!;
        Assert.Contains(aggregate.AuditEvents, value => value.Action == "CUSTOMER_METADATA_READ" &&
            value.ActorType == "PLATFORM_SUPPORT");
    }

    [Fact]
    [Trait("Requirement", "AT-FR-MGT-001")]
    public async Task EnrollmentProofBindsDeviceAndRevocationInvalidatesInventoryState()
    {
        GovernanceClock clock = new(new DateTimeOffset(2026, 7, 3, 3, 0, 0, TimeSpan.Zero));
        await using GovernanceFactory factory = new(clock);
        using HttpClient client = factory.CreateClient();
        TenantContract tenant = await CreateTenant(client, "device-owner", "owner@device.test", "device-tenant", clock);
        EnrollmentTokenResult token;
        using (HttpRequestMessage createToken = Request(HttpMethod.Post, "/v1/device-enrollment-tokens", "device-owner",
                   "owner@device.test", tenant.Id, clock, true, new EnrollmentTokenRequest(3600, null, 1)))
        {
            createToken.Headers.Add("Idempotency-Key", "create-device-token-0001");
            using HttpResponseMessage response = await client.SendAsync(createToken);
            response.EnsureSuccessStatusCode();
            token = (await response.Content.ReadFromJsonAsync<EnrollmentTokenResult>())!;
        }
        using GovernanceKey key = new();
        DeviceEnrollmentRequest unsigned = new(token.EnrollmentToken, Guid.NewGuid(), key.Jwk,
            new DeviceInfo("Lab Host", "Windows 11 24H2", "x64", "0.10.0"),
            new ProofOfPossession("enrollment-nonce-0001", string.Empty, key.Jwk, "ecdsa-p256-sha256-p1363"));
        DeviceEnrollmentRequest signed = unsigned with
        {
            Proof = unsigned.Proof with
            {
                Signature = key.Sign(GovernanceService.CreateEnrollmentProofBytes(unsigned,
                    ControlPlaneCrypto.Thumbprint(key.Jwk))),
            },
        };
        DeviceEnrollmentResult enrolled;
        using (HttpRequestMessage request = new(HttpMethod.Post, "/v1/devices/enrollments")
        { Content = JsonContent.Create(signed) })
        {
            request.Headers.Add("Idempotency-Key", "enroll-device-alpha-0001");
            using HttpResponseMessage response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            enrolled = (await response.Content.ReadFromJsonAsync<DeviceEnrollmentResult>())!;
        }

        using (HttpRequestMessage revoke = Request(HttpMethod.Delete, $"/v1/devices/{enrolled.DeviceId:D}",
                   "device-owner", "owner@device.test", tenant.Id, clock, true))
        {
            revoke.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
            using HttpResponseMessage response = await client.SendAsync(revoke);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
        using HttpRequestMessage read = Request(HttpMethod.Get, $"/v1/devices/{enrolled.DeviceId:D}",
            "device-owner", "owner@device.test", tenant.Id, clock, false);
        using HttpResponseMessage readResponse = await client.SendAsync(read);
        DeviceContract device = (await readResponse.Content.ReadFromJsonAsync<DeviceContract>())!;
        Assert.Equal("REVOKED", device.Status);
        Assert.Equal(2, device.AuthorizationVersion);
    }

    [Fact]
    [Trait("Requirement", "AT-FR-ADM-008")]
    [Trait("Requirement", "AT-NFR-PRV-005")]
    public async Task ExportAndClosureWorkersAreIdempotentAuditedAndTrackable()
    {
        GovernanceClock clock = new(new DateTimeOffset(2026, 7, 3, 4, 0, 0, TimeSpan.Zero));
        await using GovernanceFactory factory = new(clock);
        using HttpClient client = factory.CreateClient();
        TenantContract tenant = await CreateTenant(client, "privacy-owner", "owner@privacy.test", "privacy-tenant", clock);
        using (HttpRequestMessage retention = Request(HttpMethod.Patch, "/v1/tenant/settings", "privacy-owner",
                   "owner@privacy.test", tenant.Id, clock, true, new TenantSettingsPatch(1, null, null)))
        {
            retention.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
            using HttpResponseMessage response = await client.SendAsync(retention);
            response.EnsureSuccessStatusCode();
        }
        DataExportResult queued;
        using (HttpRequestMessage export = Request(HttpMethod.Post, "/v1/tenant/data-exports", "privacy-owner",
                   "owner@privacy.test", tenant.Id, clock, true, new DataExportRequest("JSONL")))
        {
            export.Headers.Add("Idempotency-Key", "privacy-export-request-0001");
            using HttpResponseMessage response = await client.SendAsync(export);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            queued = (await response.Content.ReadFromJsonAsync<DataExportResult>())!;
        }
        factory.Services.GetRequiredService<GovernanceMaintenanceService>().RunOnce();
        DataExportResult ready;
        using (HttpRequestMessage status = Request(HttpMethod.Get, $"/v1/tenant/data-exports/{queued.RequestId:D}",
                   "privacy-owner", "owner@privacy.test", tenant.Id, clock, false))
        using (HttpResponseMessage response = await client.SendAsync(status))
        {
            response.EnsureSuccessStatusCode();
            ready = (await response.Content.ReadFromJsonAsync<DataExportResult>())!;
        }
        Assert.Equal("READY", ready.State);
        Assert.NotNull(ready.DownloadUrl);
        Uri downloadUri = new(ready.DownloadUrl!);
        using (HttpRequestMessage download = Request(HttpMethod.Get, downloadUri.PathAndQuery, "privacy-owner",
                   "owner@privacy.test", tenant.Id, clock, false))
        using (HttpResponseMessage response = await client.SendAsync(download))
        {
            response.EnsureSuccessStatusCode();
            Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync());
        }

        TenantClosureResult closure;
        using (HttpRequestMessage close = Request(HttpMethod.Post, "/v1/tenant/closure-requests", "privacy-owner",
                   "owner@privacy.test", tenant.Id, clock, true,
                   new TenantClosureRequest("privacy-tenant", "No longer needed")))
        {
            close.Headers.Add("Idempotency-Key", "privacy-close-request-0001");
            using HttpResponseMessage response = await client.SendAsync(close);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            closure = (await response.Content.ReadFromJsonAsync<TenantClosureResult>())!;
        }
        clock.Advance(TimeSpan.FromDays(8));
        factory.Services.GetRequiredService<GovernanceMaintenanceService>().RunOnce();
        using HttpRequestMessage closedStatus = Request(HttpMethod.Get,
            $"/v1/tenant/closure-requests/{closure.RequestId:D}", "privacy-owner", "owner@privacy.test",
            tenant.Id, clock, false);
        using HttpResponseMessage closedResponse = await client.SendAsync(closedStatus);
        closedResponse.EnsureSuccessStatusCode();
        Assert.Equal("COMPLETED", (await closedResponse.Content.ReadFromJsonAsync<TenantClosureResult>())!.State);
        TenantAggregate aggregate = factory.Services.GetRequiredService<IGovernanceStore>().Snapshot(tenant.Id)!;
        Assert.Equal("CLOSED", aggregate.Tenant.Status);
        Assert.NotNull(aggregate.AuditCheckpoint);
        Assert.True(GovernanceAudit.Verify(aggregate, clock.UtcNow).Valid);
    }

    private static async Task<PolicyDecisionContract> Evaluate(HttpClient client, Guid tenantId,
        GovernanceClock clock, bool mfa, IReadOnlyList<string> scopes)
    {
        using HttpRequestMessage request = Request(HttpMethod.Post, "/v1/tenant/policy-evaluations", "policy-owner",
            "owner@policy.test", tenantId, clock, mfa,
            new PolicyEvaluationRequest(null, "ATTENDED", scopes, 3600, true));
        using HttpResponseMessage response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PolicyDecisionContract>())!;
    }

    private static async Task<TenantContract> CreateTenant(HttpClient client, string subject, string email,
        string slug, GovernanceClock clock)
    {
        using HttpRequestMessage request = Request(HttpMethod.Post, "/v1/tenants", subject, email, null, clock,
            true, new CreateTenantRequest(slug, slug, "KR"));
        request.Headers.Add("Idempotency-Key", $"create-{slug}-00000001");
        using HttpResponseMessage response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TenantContract>())!;
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string subject, string email,
        Guid? tenantId, GovernanceClock clock, bool mfa, object? content = null)
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

    private sealed class GovernanceFactory(GovernanceClock clock) : WebApplicationFactory<Program>
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

    private sealed class GovernanceClock(DateTimeOffset initial) : ISystemClock
    {
        public DateTimeOffset UtcNow { get; private set; } = initial;
        public void Advance(TimeSpan duration) => UtcNow += duration;
    }

    private sealed class GovernanceKey : IDisposable
    {
        private static readonly BigInteger Order = BigInteger.Parse(
            "00FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551",
            NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        private static readonly BigInteger HalfOrder = Order / 2;
        private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        public GovernanceKey()
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

        public string Sign(byte[] bytes)
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
