using System.Diagnostics;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Npgsql;
using RemoteSupport.Server;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
ControlPlaneOptions controlPlane = builder.Configuration.GetSection(ControlPlaneOptions.SectionName).Get<ControlPlaneOptions>() ?? new();
GovernanceOptions governance = builder.Configuration.GetSection(GovernanceOptions.SectionName).Get<GovernanceOptions>() ?? new();
ObservabilityOptions observability = builder.Configuration.GetSection(ObservabilityOptions.SectionName).Get<ObservabilityOptions>() ?? new();
UpdatePublicationOptions updatePublication = builder.Configuration.GetSection(UpdatePublicationOptions.SectionName).Get<UpdatePublicationOptions>() ?? new();
bool testing = builder.Environment.IsEnvironment("Testing");
bool development = builder.Environment.IsDevelopment();
bool releaseTestHarness = testing && string.Equals(
    System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name, "testhost", StringComparison.OrdinalIgnoreCase);
#if DEBUG
bool allowNonProductionAdapters = testing || development;
#else
bool allowNonProductionAdapters = releaseTestHarness;
#endif
controlPlane.Validate(allowNonProductionAdapters);
governance.Validate();
observability.Validate(allowNonProductionAdapters);
updatePublication.Validate(allowNonProductionAdapters);
builder.Services.AddSingleton(controlPlane);
builder.Services.AddSingleton(governance);
builder.Services.AddSingleton(observability);
builder.Services.AddSingleton(updatePublication);
builder.Services.AddSingleton<ControlPlaneTelemetry>();
builder.Services.AddSingleton<UpdatePublicationStore>();
builder.Services.AddSingleton<RemoteSupport.Server.ISystemClock, RemoteSupport.Server.SystemClock>();
builder.Services.AddSingleton<ControlPlaneCrypto>();

string? postgres = builder.Configuration.GetConnectionString("ControlPlane");
if (controlPlane.UseInMemoryStore || testing || (development && string.IsNullOrWhiteSpace(postgres)))
{
    if (!allowNonProductionAdapters) throw new InvalidOperationException("In-memory control-plane persistence is forbidden in Release and outside Development/Testing.");
    builder.Services.AddSingleton<IAttendedSessionStore, InMemoryAttendedSessionStore>();
    builder.Services.AddSingleton<IGovernanceStore, InMemoryGovernanceStore>();
}
else
{
    if (string.IsNullOrWhiteSpace(postgres)) throw new InvalidOperationException("ConnectionStrings:ControlPlane is required.");
    builder.Services.AddSingleton(NpgsqlDataSource.Create(postgres));
    builder.Services.AddSingleton<IAttendedSessionStore, PostgresAttendedSessionStore>();
    builder.Services.AddSingleton<IGovernanceStore, PostgresGovernanceStore>();
    builder.Services.AddHostedService<PostgresMigrationRunner>();
}
builder.Services.AddSingleton<AttendedSessionService>();
builder.Services.AddSingleton<ResolveAbuseGuard>();
builder.Services.AddSingleton<PeerAccessService>();
builder.Services.AddSingleton<SignalingTicketService>();
builder.Services.AddSingleton<TurnCredentialService>();
builder.Services.AddSingleton<SignalingProtocolValidator>();
builder.Services.AddSingleton<SignalingHub>();
builder.Services.AddSingleton<GovernanceExportStore>();
builder.Services.AddSingleton<GovernanceService>();
builder.Services.AddSingleton<GovernanceMaintenanceService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<GovernanceMaintenanceService>());

if (testing && allowNonProductionAdapters)
{
    builder.Services.AddAuthentication("TestOidc").AddScheme<AuthenticationSchemeOptions, TestOidcHandler>("TestOidc", _ => { });
}
else
{
    string authority = builder.Configuration["Oidc:Authority"] ?? throw new InvalidOperationException("Oidc:Authority is required.");
    string audience = builder.Configuration["Oidc:Audience"] ?? throw new InvalidOperationException("Oidc:Audience is required.");
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
    {
        options.Authority = authority;
        options.Audience = audience;
        options.RequireHttpsMetadata = !development;
        options.MapInboundClaims = false;
    });
}
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Authenticated", policy => policy.RequireAuthenticatedUser().RequireClaim("sub"));
    options.AddPolicy("Operator", policy => policy.RequireAuthenticatedUser().RequireClaim("sub")
        .RequireClaim("tenant_id").RequireClaim("name").RequireClaim("tenant_name"));
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(new ProblemContract("RATE_LIMITED", "Too many requests.",
            Guid.NewGuid(), true, 60), cancellationToken);
    };
    options.AddPolicy("resolve", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue("sub") ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = controlPlane.ResolveRequestsPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        }));
    options.AddPolicy("peerCredential", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        }));
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, ControlPlaneJsonContext.Default);
    options.SerializerOptions.UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow;
});

WebApplication app = builder.Build();
ControlPlaneTelemetry telemetry = app.Services.GetRequiredService<ControlPlaneTelemetry>();
ILogger observabilityLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("RemoteSupport.Observability");
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(20),
});
app.Use(async (context, next) =>
{
    long started = Stopwatch.GetTimestamp();
    using IDisposable? activity = telemetry.StartRequest(context);
    string correlation = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
    using IDisposable? logScope = observabilityLog.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlation });
    context.Response.Headers["X-Correlation-Id"] = correlation;
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    try { await next(); }
    catch (ControlPlaneException exception)
    {
        telemetry.RecordSecurityEvent(exception.Code);
        if (context.Response.HasStarted) throw;
        context.Response.StatusCode = exception.StatusCode;
        await context.Response.WriteAsJsonAsync(new ProblemContract(exception.Code,
            exception.StatusCode == 404 ? "The session was not found." : exception.Message,
            Guid.NewGuid(), exception.StatusCode >= 500));
    }
    finally
    {
        telemetry.RecordRequest(context, Stopwatch.GetElapsedTime(started));
        string route = context.GetEndpoint() is RouteEndpoint endpoint ? endpoint.RoutePattern.RawText ?? "unmatched" : "unmatched";
        if (observabilityLog.IsEnabled(LogLevel.Information))
            ObservabilityLog.RequestCompleted(observabilityLog, context.Request.Method, route,
                context.Response.StatusCode);
    }
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<TenantContextMiddleware>();
app.UseAuthorization();

app.MapPost("/v1/tenants", (CreateTenantRequest request, HttpRequest http, ClaimsPrincipal principal,
    GovernanceService service) => Results.Created("/v1/tenant", service.CreateTenant(request,
        http.Headers["Idempotency-Key"].ToString(), TenantActor.FromPrincipal(principal))))
    .RequireAuthorization("Authenticated");

app.MapPost("/v1/invitations/{invitationId:guid}/accept", (Guid invitationId,
    InvitationAcceptanceRequest request, ClaimsPrincipal principal, GovernanceService service) =>
    Results.Ok(service.AcceptInvitation(invitationId, request, TenantActor.FromPrincipal(principal))))
    .RequireAuthorization("Authenticated");

app.MapPost("/v1/device-enrollment-tokens", (EnrollmentTokenRequest request, HttpContext context,
    GovernanceService service) => Results.Created("/v1/device-enrollment-tokens", service.CreateEnrollmentToken(
        TenantContextMiddleware.Get(context), request, context.Request.Headers["Idempotency-Key"].ToString())))
    .RequireAuthorization("Authenticated");

app.MapPost("/v1/devices/enrollments", (DeviceEnrollmentRequest request, HttpRequest http,
    GovernanceService service) => Results.Created("/v1/devices", service.EnrollDevice(request,
        http.Headers["Idempotency-Key"].ToString()))).AllowAnonymous();

app.MapGet("/v1/tenant", (HttpContext context, GovernanceService service) =>
    Results.Ok(service.GetTenant(TenantContextMiddleware.Get(context)))).RequireAuthorization("Authenticated");
app.MapGet("/v1/tenant/settings", (HttpContext context, GovernanceService service) =>
    Results.Ok(service.GetSettings(TenantContextMiddleware.Get(context)))).RequireAuthorization("Authenticated");
app.MapPatch("/v1/tenant/settings", (TenantSettingsPatch patch, HttpContext context, GovernanceService service) =>
    Results.Ok(service.UpdateSettings(TenantContextMiddleware.Get(context), patch, IfMatch(context.Request))))
    .RequireAuthorization("Authenticated");
app.MapGet("/v1/tenant/memberships", (HttpContext context, GovernanceService service) =>
    Results.Ok(service.ListMemberships(TenantContextMiddleware.Get(context)))).RequireAuthorization("Authenticated");
app.MapPatch("/v1/tenant/memberships/{userId:guid}", (Guid userId, MembershipPatch patch,
    HttpContext context, GovernanceService service) => Results.Ok(service.UpdateMembership(
        TenantContextMiddleware.Get(context), userId, patch, IfMatch(context.Request))))
    .RequireAuthorization("Authenticated");
app.MapDelete("/v1/tenant/memberships/{userId:guid}", (Guid userId, HttpContext context,
    GovernanceService service) =>
{
    service.RemoveMembership(TenantContextMiddleware.Get(context), userId, IfMatch(context.Request));
    return Results.NoContent();
}).RequireAuthorization("Authenticated");
app.MapGet("/v1/tenant/invitations", (HttpContext context, GovernanceService service) =>
    Results.Ok(service.ListInvitations(TenantContextMiddleware.Get(context)))).RequireAuthorization("Authenticated");
app.MapPost("/v1/tenant/invitations", (InvitationRequest request, HttpContext context,
    GovernanceService service) => Results.Created("/v1/tenant/invitations", service.CreateInvitation(
        TenantContextMiddleware.Get(context), request, context.Request.Headers["Idempotency-Key"].ToString())))
    .RequireAuthorization("Authenticated");
app.MapDelete("/v1/tenant/invitations/{invitationId:guid}", (Guid invitationId, HttpContext context,
    GovernanceService service) =>
{
    service.RevokeInvitation(TenantContextMiddleware.Get(context), invitationId);
    return Results.NoContent();
}).RequireAuthorization("Authenticated");

app.MapGet("/v1/devices", (HttpContext context, GovernanceService service) =>
    Results.Ok(service.ListDevices(TenantContextMiddleware.Get(context)))).RequireAuthorization("Authenticated");
app.MapGet("/v1/devices/{deviceId:guid}", (Guid deviceId, HttpContext context, GovernanceService service) =>
    Results.Ok(service.GetDevice(TenantContextMiddleware.Get(context), deviceId))).RequireAuthorization("Authenticated");
app.MapDelete("/v1/devices/{deviceId:guid}", (Guid deviceId, HttpContext context, GovernanceService service) =>
{
    service.RevokeDevice(TenantContextMiddleware.Get(context), deviceId, IfMatch(context.Request));
    return Results.NoContent();
}).RequireAuthorization("Authenticated");

app.MapGet("/v1/tenant/policies", (HttpContext context, GovernanceService service) =>
    Results.Ok(service.ListPolicies(TenantContextMiddleware.Get(context)))).RequireAuthorization("Authenticated");
app.MapPost("/v1/tenant/policies", (PolicyDocumentRequest request, HttpContext context,
    GovernanceService service) => Results.Created("/v1/tenant/policies", service.CreatePolicy(
        TenantContextMiddleware.Get(context), request, context.Request.Headers["Idempotency-Key"].ToString())))
    .RequireAuthorization("Authenticated");
app.MapPost("/v1/tenant/policies/{policyId:guid}/versions", (Guid policyId, PolicyDocumentRequest request,
    HttpContext context, GovernanceService service) => Results.Created($"/v1/tenant/policies/{policyId:D}",
        service.CreatePolicyVersion(TenantContextMiddleware.Get(context), policyId, request,
            IfMatch(context.Request)))).RequireAuthorization("Authenticated");
app.MapPost("/v1/tenant/policies/{policyId:guid}/activate", (Guid policyId,
    ActivatePolicyVersionRequest request, HttpContext context, GovernanceService service) => Results.Ok(
        service.ActivatePolicy(TenantContextMiddleware.Get(context), policyId, request,
            IfMatch(context.Request)))).RequireAuthorization("Authenticated");
app.MapPost("/v1/tenant/policy-evaluations", (PolicyEvaluationRequest request, HttpContext context,
    GovernanceService service) => Results.Ok(service.EvaluatePolicy(TenantContextMiddleware.Get(context), request)))
    .RequireAuthorization("Authenticated");

app.MapGet("/v1/tenant/audit-events", (HttpContext context, GovernanceService service,
    DateTimeOffset? from, DateTimeOffset? to, string? category) => Results.Ok(service.ListAudit(
        TenantContextMiddleware.Get(context), from, to, category))).RequireAuthorization("Authenticated");
app.MapGet("/v1/tenant/audit-events/verification", (HttpContext context, GovernanceService service) =>
    Results.Ok(service.VerifyAudit(TenantContextMiddleware.Get(context)))).RequireAuthorization("Authenticated");
app.MapGet("/v1/tenant/audit-events/export", (HttpContext context, GovernanceService service) =>
    Results.Text(service.ExportAudit(TenantContextMiddleware.Get(context)), "application/x-ndjson", Encoding.UTF8))
    .RequireAuthorization("Authenticated");

app.MapPost("/v1/tenant/data-exports", (DataExportRequest request, HttpContext context,
    GovernanceService service) => Results.Accepted($"/v1/tenant/data-exports", service.RequestDataExport(
        TenantContextMiddleware.Get(context), request, context.Request.Headers["Idempotency-Key"].ToString(),
        $"{context.Request.Scheme}://{context.Request.Host}"))).RequireAuthorization("Authenticated");
app.MapGet("/v1/tenant/data-exports/{requestId:guid}", (Guid requestId, HttpContext context,
    GovernanceService service) => Results.Ok(service.GetDataExport(TenantContextMiddleware.Get(context), requestId,
        $"{context.Request.Scheme}://{context.Request.Host}"))).RequireAuthorization("Authenticated");
app.MapGet("/v1/tenant/data-exports/{requestId:guid}/download", (Guid requestId, string token,
    HttpContext context, GovernanceService service) =>
{
    ExportDownload export = service.DownloadDataExport(TenantContextMiddleware.Get(context), requestId, token);
    return Results.File(export.Content, export.ContentType, export.FileName);
}).RequireAuthorization("Authenticated");

app.MapPost("/v1/tenant/closure-requests", (TenantClosureRequest request, HttpContext context,
    GovernanceService service) => Results.Accepted("/v1/tenant/closure-requests", service.RequestClosure(
        TenantContextMiddleware.Get(context), request, context.Request.Headers["Idempotency-Key"].ToString())))
    .RequireAuthorization("Authenticated");
app.MapGet("/v1/tenant/closure-requests/{requestId:guid}", (Guid requestId, HttpContext context,
    GovernanceService service) => Results.Ok(service.GetClosure(TenantContextMiddleware.Get(context), requestId)))
    .RequireAuthorization("Authenticated");
app.MapDelete("/v1/tenant/closure-requests/{requestId:guid}", (Guid requestId, HttpContext context,
    GovernanceService service) =>
{
    service.CancelClosure(TenantContextMiddleware.Get(context), requestId, IfMatch(context.Request));
    return Results.NoContent();
}).RequireAuthorization("Authenticated");
app.MapPost("/v1/tenant/support-grants", (SupportGrantRequest request, HttpContext context,
    GovernanceService service) => Results.Created("/v1/tenant/support-grants", service.CreateSupportGrant(
        TenantContextMiddleware.Get(context), request))).RequireAuthorization("Authenticated");
app.MapGet("/internal/v1/support/tenants/{tenantId:guid}", (Guid tenantId, Guid grantId,
    ClaimsPrincipal principal, GovernanceService service) => Results.Ok(service.ReadTenantAsSupport(
        tenantId, grantId, TenantActor.FromPrincipal(principal)))).RequireAuthorization("Authenticated");

app.MapPost("/v1/attended-sessions", (CreateAttendedSessionRequest request, HttpRequest http,
    AttendedSessionService service) => Results.Created($"/v1/attended-sessions", service.Create(request,
        http.Headers["Idempotency-Key"].ToString(),
        $"{http.Scheme}://{http.Host}"))).AllowAnonymous();

app.MapPost("/v1/attended-sessions/resolve", (ResolveAttendedSessionRequest request, ClaimsPrincipal principal,
    HttpContext context, AttendedSessionService service, GovernanceService governanceService,
    ResolveAbuseGuard abuse) => abuse.TryAcquire(context, request.SupportCode)
        ? Results.Ok(service.Resolve(request, OperatorFrom(principal, governanceService, testing)))
        : Results.Json(new ProblemContract("RATE_LIMITED", "Too many requests.", Guid.NewGuid(), true, 60), statusCode: 429))
    .RequireAuthorization("Operator").RequireRateLimiting("resolve");

app.MapGet("/v1/attended-sessions/{sessionId:guid}/pending-consent", (Guid sessionId, HttpRequest request,
    AttendedSessionService service) => service.GetPendingConsent(sessionId, BootstrapToken(request)) is { } pending
        ? Results.Ok(pending) : Results.NoContent()).AllowAnonymous();

app.MapPost("/v1/attended-sessions/{sessionId:guid}/consent", (Guid sessionId, ConsentDecision decision,
    HttpRequest request, AttendedSessionService service) => Results.Ok(service.Decide(sessionId,
        BootstrapToken(request), IfMatch(request), decision))).AllowAnonymous();

app.MapPost("/v1/sessions/{sessionId:guid}/peer-authorization-challenges", (Guid sessionId,
    HttpRequest request, AttendedSessionService service) => Results.Created($"/v1/sessions/{sessionId:D}/peer-authorization-challenges",
        service.CreateChallenge(sessionId, BootstrapToken(request)))).AllowAnonymous();

app.MapPost("/v1/sessions/{sessionId:guid}/peer-authorization", (Guid sessionId,
    PeerAuthorizationRequest body, HttpRequest request, AttendedSessionService service) => Results.Ok(
        service.AuthorizePeer(sessionId, BootstrapToken(request), body))).AllowAnonymous();

app.MapPost("/v1/sessions/{sessionId:guid}/signaling-tickets", (Guid sessionId, HttpRequest request,
    PeerAccessService peers, SignalingTicketService tickets) => Results.Ok(
        tickets.Issue(sessionId, peers.Authenticate(request, sessionId), request)))
    .AllowAnonymous().RequireRateLimiting("peerCredential");

app.MapPost("/v1/sessions/{sessionId:guid}/turn-credentials", (Guid sessionId, HttpRequest request,
    PeerAccessService peers, TurnCredentialService turn) => Results.Ok(
        turn.Issue(sessionId, peers.Authenticate(request, sessionId))))
    .AllowAnonymous().RequireRateLimiting("peerCredential");

app.MapPost("/v1/sessions/{sessionId:guid}/termination", (Guid sessionId, SessionTerminationRequest body,
    HttpRequest request, PeerAccessService peers, AttendedSessionService sessions) => Results.Ok(
        sessions.Terminate(sessionId, peers.Authenticate(request, sessionId), body)))
    .AllowAnonymous().RequireRateLimiting("peerCredential");

app.MapPost("/v1/sessions/{sessionId:guid}/scope-revocations", (Guid sessionId, ScopeRevocationRequest body,
    HttpRequest request, PeerAccessService peers, AttendedSessionService sessions) => Results.Ok(
        sessions.RevokeScopes(sessionId, peers.Authenticate(request, sessionId), IfMatch(request), body)))
    .AllowAnonymous().RequireRateLimiting("peerCredential");

app.MapGet("/v1/signaling", async (HttpContext context, SignalingTicketService tickets,
    SignalingHub hub, CancellationToken cancellationToken) =>
{
    if (!context.WebSockets.IsWebSocketRequest ||
        !context.WebSockets.WebSocketRequestedProtocols.Contains("rsp.signaling.v1", StringComparer.Ordinal) ||
        context.Request.Query["ticket"].Count != 1)
        return Results.BadRequest(new ProblemContract("SIGNAL_UPGRADE_REQUIRED", "A valid signaling WebSocket upgrade is required.",
            Guid.NewGuid(), false));
    SignalingConnectionBinding binding = tickets.Consume(context.Request.Query["ticket"][0]);
    using System.Net.WebSockets.WebSocket socket = await context.WebSockets.AcceptWebSocketAsync("rsp.signaling.v1");
    await hub.RunAsync(socket, binding, cancellationToken);
    return Results.Empty;
}).AllowAnonymous();

app.MapPost("/internal/v1/turn-usage", async (HttpRequest request, TurnCredentialService turn,
    CancellationToken cancellationToken) =>
{
    if (request.ContentLength is null or < 2 or > 16_384 ||
        request.Headers["X-RSP-Turn-Timestamp"].Count != 1 ||
        request.Headers["X-RSP-Turn-Signature"].Count != 1)
        throw new ControlPlaneException(401, "TURN_USAGE_AUTHENTICATION_FAILED", "TURN usage authentication failed.");
    using MemoryStream body = new((int)request.ContentLength.Value);
    await request.Body.CopyToAsync(body, cancellationToken);
    return Results.Ok(turn.AcceptUsage(body.ToArray(), request.Headers["X-RSP-Turn-Timestamp"][0]!,
        request.Headers["X-RSP-Turn-Signature"][0]!));
}).AllowAnonymous();

app.MapGet("/v1/sessions/{sessionId:guid}", (Guid sessionId, ClaimsPrincipal principal,
    AttendedSessionService service, GovernanceService governanceService) =>
    Results.Ok(service.Get(sessionId, OperatorFrom(principal, governanceService, testing)))).RequireAuthorization("Operator");
app.MapGet("/updates/root", (int currentRootVersion, UpdatePublicationStore updates) =>
{
    PublishedUpdate? update = updates.GetNextRoot(currentRootVersion);
    return update is null ? Results.StatusCode(StatusCodes.Status304NotModified) : SignedMetadata(update);
}).AllowAnonymous();
app.MapGet("/updates/manifest", (string product, string channel, string architecture, long currentSequence,
    UpdatePublicationStore updates) =>
{
    PublishedUpdate? update = updates.GetManifest(product, channel, architecture, currentSequence);
    return update is null ? Results.StatusCode(StatusCodes.Status304NotModified) : SignedMetadata(update);
}).AllowAnonymous();
app.MapGet("/internal/metrics", (HttpRequest request, ObservabilityOptions options, ControlPlaneTelemetry metrics) =>
{
    string presented = request.Headers.Authorization.ToString();
    string expected = "Bearer " + options.MetricsBearerToken;
    bool accepted = presented.Length == expected.Length && CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(expected));
    return accepted ? Results.Text(metrics.RenderPrometheus(), "text/plain; version=0.0.4", Encoding.UTF8) : Results.NotFound();
}).AllowAnonymous();
app.MapGet("/health/live", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.Run();

static string BootstrapToken(HttpRequest request)
{
    string header = request.Headers.Authorization.ToString();
    if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || header.Length <= 7)
        throw new ControlPlaneException(401, "AUTHENTICATION_REQUIRED", "Authentication required.");
    return header[7..];
}

static IResult SignedMetadata(PublishedUpdate update)
{
    return Results.Bytes(update.Content, "application/json", lastModified: null, entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue(update.ETag));
}

static long IfMatch(HttpRequest request)
{
    string value = request.Headers.IfMatch.ToString().Trim().Trim('"');
    return long.TryParse(value, out long version) && version > 0 ? version :
        throw new ControlPlaneException(400, "IF_MATCH_REQUIRED", "A valid If-Match state version is required.");
}

static OperatorIdentity OperatorFrom(ClaimsPrincipal principal, GovernanceService governance, bool testing)
{
    string? subject = principal.FindFirstValue("sub");
    string? name = principal.FindFirstValue("name");
    string? tenantName = principal.FindFirstValue("tenant_name");
    if (!Guid.TryParse(principal.FindFirstValue("tenant_id"), out Guid tenantId) ||
        string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(tenantName) ||
        name.Length > 200 || tenantName.Length > 200)
        throw new ControlPlaneException(403, "TENANT_CONTEXT_INVALID", "Tenant context was invalid.");
    if (!testing) return governance.ResolveOperatorIdentity(tenantId, TenantActor.FromPrincipal(principal));
    return new OperatorIdentity(tenantId, subject, name, tenantName,
        string.Equals(principal.FindFirstValue("tenant_verified"), "true", StringComparison.OrdinalIgnoreCase));
}

public partial class Program { }

internal static partial class ObservabilityLog
{
    [LoggerMessage(EventId = 1100, Level = LogLevel.Information,
        Message = "HTTP {Method} {Route} completed with {StatusCode}")]
    public static partial void RequestCompleted(ILogger logger, string method, string route, int statusCode);
}

internal sealed class TestOidcHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-Operator-Subject", out Microsoft.Extensions.Primitives.StringValues subject))
            return Task.FromResult(AuthenticateResult.NoResult());
        List<Claim> claims =
        [
            new("sub", subject.ToString()),
            new("name", Request.Headers["X-Test-Operator-Name"].ToString()),
            new("email", Request.Headers["X-Test-Operator-Email"].ToString()),
            new("iss", "https://testing.remote-support.invalid"),
            new("tenant_id", Request.Headers["X-Test-Tenant-Id"].ToString()),
            new("tenant_name", Request.Headers["X-Test-Tenant-Name"].ToString()),
            new("tenant_verified", Request.Headers["X-Test-Tenant-Verified"].ToString()),
        ];
        if (string.Equals(Request.Headers["X-Test-Mfa"].ToString(), "true", StringComparison.OrdinalIgnoreCase))
        {
            claims.Add(new Claim("amr", "mfa"));
            claims.Add(new Claim("auth_time", Request.Headers["X-Test-Auth-Time"].FirstOrDefault() ??
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        if (Request.Headers.TryGetValue("X-Test-Platform-Role", out Microsoft.Extensions.Primitives.StringValues platformRole))
            claims.Add(new Claim("platform_role", platformRole.ToString()));
        ClaimsPrincipal principal = new(new ClaimsIdentity(claims, Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}
