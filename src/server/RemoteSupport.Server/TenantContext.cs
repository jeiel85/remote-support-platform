using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace RemoteSupport.Server;

internal sealed record TenantActor(Guid UserId, string Subject, string DisplayName, string? Email,
    string ActorType, DateTimeOffset? AuthenticationTime, string[] MfaMethods, string? PlatformRole)
{
    public bool HasFreshMfa(DateTimeOffset now, TimeSpan maximumAge) =>
        MfaMethods.Length > 0 && AuthenticationTime is { } authenticated && authenticated <= now &&
        now - authenticated <= maximumAge;

    public static TenantActor FromPrincipal(ClaimsPrincipal principal)
    {
        string subject = principal.FindFirstValue("sub") ??
            throw new ControlPlaneException(401, "AUTHENTICATION_REQUIRED", "Authentication is required.");
        string displayName = principal.FindFirstValue("name") ?? subject;
        if (subject.Length is < 1 or > 256 || displayName.Length is < 1 or > 200)
            throw new ControlPlaneException(403, "IDENTITY_CLAIMS_INVALID", "Identity claims were invalid.");
        string issuer = principal.FindFirstValue("iss") ?? "local";
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes($"RSP-USER-ID-V1\0{issuer}\0{subject}"));
        byte[] idBytes = digest[..16];
        idBytes[7] = (byte)((idBytes[7] & 0x0F) | 0x40);
        idBytes[8] = (byte)((idBytes[8] & 0x3F) | 0x80);
        DateTimeOffset? authTime = long.TryParse(principal.FindFirstValue("auth_time"), out long seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
        string[] mfa = principal.FindAll("amr").Select(value => value.Value)
            .Where(value => value is "mfa" or "otp" or "hwk" or "fido")
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        string? email = principal.FindFirstValue("email")?.Trim().ToLowerInvariant();
        return new TenantActor(new Guid(idBytes, bigEndian: true), subject, displayName,
            string.IsNullOrWhiteSpace(email) ? null : email, "USER", authTime, mfa,
            principal.FindFirstValue("platform_role"));
    }
}

internal sealed record TenantRequestContext(Guid TenantId, Guid UserId, string[] Roles, string[] Groups,
    long PrivilegeVersion, long AuthorizationVersion, TenantActor Actor)
{
    public string? SourceIp { get; init; }

    public bool HasRole(params string[] roles) => Roles.Intersect(roles, StringComparer.Ordinal).Any();

    public void RequireRole(params string[] roles)
    {
        if (!HasRole(roles))
            throw new ControlPlaneException(403, "AUTHORIZATION_DENIED", "The action is not authorized.");
    }

    public void RequireFreshMfa(DateTimeOffset now)
    {
        if (!Actor.HasFreshMfa(now, TimeSpan.FromMinutes(10)))
            throw new ControlPlaneException(403, "MFA_STEP_UP_REQUIRED", "Fresh multi-factor authentication is required.");
    }
}

internal sealed class TenantContextMiddleware(RequestDelegate next)
{
    public const string ItemKey = "RemoteSupport.TenantContext";

    public async Task InvokeAsync(HttpContext httpContext, GovernanceService governance)
    {
        if (RequiresTenantContext(httpContext.Request.Path))
        {
            if (!Guid.TryParse(httpContext.Request.Headers["X-Tenant-Id"].ToString(), out Guid tenantId) || tenantId == Guid.Empty)
                throw new ControlPlaneException(400, "TENANT_CONTEXT_REQUIRED", "A valid X-Tenant-Id header is required.");
            TenantActor actor = TenantActor.FromPrincipal(httpContext.User);
            bool allowClosed = httpContext.Request.Path.StartsWithSegments(
                "/v1/tenant/closure-requests", StringComparison.OrdinalIgnoreCase);
            httpContext.Items[ItemKey] = governance.ResolveContext(tenantId, actor, allowClosed) with
            {
                SourceIp = httpContext.Connection.RemoteIpAddress?.ToString(),
            };
        }
        await next(httpContext);
    }

    public static TenantRequestContext Get(HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out object? value) && value is TenantRequestContext tenant
            ? tenant : throw new ControlPlaneException(403, "TENANT_CONTEXT_INVALID", "Tenant context was invalid.");

    private static readonly HashSet<string> DeviceSelfServiceSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "heartbeat", "credential-challenges", "credentials", "keys", "pending-session-requests",
        "unattended-enrollment-confirmations",
    };

    private static bool RequiresTenantContext(PathString path)
    {
        if (path.StartsWithSegments("/v1/tenant", StringComparison.OrdinalIgnoreCase)) return true;
        if (path.StartsWithSegments("/v1/device-enrollment-tokens", StringComparison.OrdinalIgnoreCase)) return true;
        if (!path.StartsWithSegments("/v1/devices", StringComparison.OrdinalIgnoreCase)) return false;
        if (path.StartsWithSegments("/v1/devices/enrollments", StringComparison.OrdinalIgnoreCase)) return false;
        // Device-authenticated (DPoP) or anonymous device self-service calls do not carry an operator
        // tenant context; only operator-facing device inventory/session-creation calls require it.
        string[] segments = (path.Value ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length < 4 || !DeviceSelfServiceSegments.Contains(segments[3]);
    }
}
