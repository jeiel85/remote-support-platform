using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

namespace RemoteSupport.AdminPortal;

public sealed class ControlPlaneBffClient(HttpClient http, IHttpContextAccessor accessor)
{
    public const string TenantSessionKey = "selected-tenant-id";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<bool> CanSelectTenant(Guid tenantId)
    {
        using HttpRequestMessage request = await Create(HttpMethod.Get, "v1/tenant", tenantId);
        using HttpResponseMessage response = await http.SendAsync(request);
        return response.IsSuccessStatusCode;
    }

    public Task<TenantView?> GetTenant() => Get<TenantView>("v1/tenant");
    public Task<PagedMembershipsView?> GetMemberships() => Get<PagedMembershipsView>("v1/tenant/memberships");
    public Task<PagedDevicesView?> GetDevices() => Get<PagedDevicesView>("v1/devices");
    public Task<PolicyView[]?> GetPolicies() => Get<PolicyView[]>("v1/tenant/policies");
    public Task<PagedAuditView?> GetAudit() => Get<PagedAuditView>("v1/tenant/audit-events");
    public Task<TenantSettingsView?> GetSettings() => Get<TenantSettingsView>("v1/tenant/settings");

    public Task<InvitationView> CreateInvitation(string email, string role) => SendFor<InvitationView>(HttpMethod.Post,
        "v1/tenant/invitations", new { email, roles = new[] { role }, expiresInSeconds = 172800 });

    public async Task UpdateSettings(int retentionDays, long fileSizeLimitBytes, long version) =>
        await Send(HttpMethod.Patch, "v1/tenant/settings", new { retentionDays, fileSizeLimitBytes }, version);

    public async Task CreatePolicy(string name, string document)
    {
        using JsonDocument parsed = JsonDocument.Parse(document, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
        });
        await Send(HttpMethod.Post, "v1/tenant/policies", new { name, document = parsed.RootElement });
    }

    public async Task RequestExport() => await Send(HttpMethod.Post, "v1/tenant/data-exports",
        new { format = "JSONL" });

    public async Task RequestClosure(string phrase, string reason) => await Send(HttpMethod.Post,
        "v1/tenant/closure-requests", new { confirmationPhrase = phrase, reason });

    private async Task<T?> Get<T>(string path)
    {
        Guid? tenantId = SelectedTenant();
        if (tenantId is null) return default;
        using HttpRequestMessage request = await Create(HttpMethod.Get, path, tenantId.Value);
        using HttpResponseMessage response = await http.SendAsync(request);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return default;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(Json);
    }

    private async Task Send(HttpMethod method, string path, object body, long? ifMatch = null)
    {
        Guid tenantId = SelectedTenant() ?? throw new InvalidOperationException("Select a tenant before changing data.");
        using HttpRequestMessage request = await Create(method, path, tenantId);
        request.Headers.Add("Idempotency-Key", Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)));
        if (ifMatch is { } version) request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        request.Content = JsonContent.Create(body, options: Json);
        using HttpResponseMessage response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private async Task<T> SendFor<T>(HttpMethod method, string path, object body)
    {
        Guid tenantId = SelectedTenant() ?? throw new InvalidOperationException("Select a tenant before changing data.");
        using HttpRequestMessage request = await Create(method, path, tenantId);
        request.Headers.Add("Idempotency-Key", Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)));
        request.Content = JsonContent.Create(body, options: Json);
        using HttpResponseMessage response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(Json) ??
            throw new InvalidOperationException("The control plane returned an empty response.");
    }

    private async Task<HttpRequestMessage> Create(HttpMethod method, string path, Guid tenantId)
    {
        HttpContext context = accessor.HttpContext ?? throw new InvalidOperationException("An HTTP context is required.");
        string? token = await context.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("The OIDC access token is unavailable.");
        HttpRequestMessage request = new(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Tenant-Id", tenantId.ToString("D"));
        return request;
    }

    private Guid? SelectedTenant()
    {
        string? selected = accessor.HttpContext?.Session.GetString(TenantSessionKey);
        return Guid.TryParse(selected, out Guid id) ? id : null;
    }
}

public sealed record TenantView(Guid Id, string Name, string Slug, string Status, string PlanCode, string DataRegion,
    DateTimeOffset CreatedAt);
public sealed record MembershipView(Guid UserId, string DisplayName, string? Email, string[] Roles, string Status,
    long PrivilegeVersion);
public sealed record PagedMembershipsView(MembershipView[] Items);
public sealed record DeviceView(Guid Id, string DisplayName, string Status, string AppVersion, string OsVersion,
    bool UnattendedEnabled, DateTimeOffset EnrolledAt, DateTimeOffset? LastSeenAt, long AuthorizationVersion);
public sealed record PagedDevicesView(DeviceView[] Items);
public sealed record PolicyView(Guid Id, string Name, string Status, int? ActiveVersion, DateTimeOffset CreatedAt,
    long ResourceVersion);
public sealed record AuditEventView(Guid Id, long ChainSequence, string Category, string Action, string Outcome,
    string ActorType, string? ActorId, DateTimeOffset OccurredAt);
public sealed record PagedAuditView(AuditEventView[] Items, string? NextCursor, bool ChainValid);
public sealed record TenantSettingsView(long SettingsVersion, int RetentionDays, string[] AllowedFeatures,
    long FileSizeLimitBytes, bool RecordingEnabled);
public sealed record InvitationView(Guid Id, string Email, string[] Roles, string Status, DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt, string? AcceptanceToken);
