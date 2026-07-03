using System.Security.Claims;
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
builder.Services.AddSingleton(controlPlane);
builder.Services.AddSingleton<RemoteSupport.Server.ISystemClock, RemoteSupport.Server.SystemClock>();
builder.Services.AddSingleton<ControlPlaneCrypto>();

string? postgres = builder.Configuration.GetConnectionString("ControlPlane");
if (controlPlane.UseInMemoryStore || testing || (development && string.IsNullOrWhiteSpace(postgres)))
{
    if (!allowNonProductionAdapters) throw new InvalidOperationException("In-memory control-plane persistence is forbidden in Release and outside Development/Testing.");
    builder.Services.AddSingleton<IAttendedSessionStore, InMemoryAttendedSessionStore>();
}
else
{
    if (string.IsNullOrWhiteSpace(postgres)) throw new InvalidOperationException("ConnectionStrings:ControlPlane is required.");
    builder.Services.AddSingleton(NpgsqlDataSource.Create(postgres));
    builder.Services.AddSingleton<IAttendedSessionStore, PostgresAttendedSessionStore>();
    builder.Services.AddHostedService<PostgresMigrationRunner>();
}
builder.Services.AddSingleton<AttendedSessionService>();
builder.Services.AddSingleton<ResolveAbuseGuard>();
builder.Services.AddSingleton<PeerAccessService>();
builder.Services.AddSingleton<SignalingTicketService>();
builder.Services.AddSingleton<TurnCredentialService>();
builder.Services.AddSingleton<SignalingProtocolValidator>();
builder.Services.AddSingleton<SignalingHub>();

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
builder.Services.AddAuthorization(options => options.AddPolicy("Operator", policy => policy
    .RequireAuthenticatedUser().RequireClaim("sub").RequireClaim("tenant_id").RequireClaim("name").RequireClaim("tenant_name")));
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
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(20),
});
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    try { await next(); }
    catch (ControlPlaneException exception)
    {
        if (context.Response.HasStarted) throw;
        context.Response.StatusCode = exception.StatusCode;
        await context.Response.WriteAsJsonAsync(new ProblemContract(exception.Code,
            exception.StatusCode == 404 ? "The session was not found." : exception.Message,
            Guid.NewGuid(), exception.StatusCode >= 500));
    }
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/v1/attended-sessions", (CreateAttendedSessionRequest request, HttpRequest http,
    AttendedSessionService service) => Results.Created($"/v1/attended-sessions", service.Create(request,
        http.Headers["Idempotency-Key"].ToString(),
        $"{http.Scheme}://{http.Host}"))).AllowAnonymous();

app.MapPost("/v1/attended-sessions/resolve", (ResolveAttendedSessionRequest request, ClaimsPrincipal principal,
    HttpContext context, AttendedSessionService service, ResolveAbuseGuard abuse) => abuse.TryAcquire(context, request.SupportCode)
        ? Results.Ok(service.Resolve(request, OperatorFrom(principal)))
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

app.MapGet("/v1/sessions/{sessionId:guid}", (Guid sessionId, AttendedSessionService service) =>
    Results.Ok(service.Get(sessionId))).RequireAuthorization("Operator");
app.MapGet("/health/live", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.Run();

static string BootstrapToken(HttpRequest request)
{
    string header = request.Headers.Authorization.ToString();
    if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || header.Length <= 7)
        throw new ControlPlaneException(401, "AUTHENTICATION_REQUIRED", "Authentication required.");
    return header[7..];
}

static long IfMatch(HttpRequest request)
{
    string value = request.Headers.IfMatch.ToString().Trim().Trim('"');
    return long.TryParse(value, out long version) && version > 0 ? version :
        throw new ControlPlaneException(400, "IF_MATCH_REQUIRED", "A valid If-Match state version is required.");
}

static OperatorIdentity OperatorFrom(ClaimsPrincipal principal)
{
    string? subject = principal.FindFirstValue("sub");
    string? name = principal.FindFirstValue("name");
    string? tenantName = principal.FindFirstValue("tenant_name");
    if (!Guid.TryParse(principal.FindFirstValue("tenant_id"), out Guid tenantId) ||
        string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(tenantName) ||
        name.Length > 200 || tenantName.Length > 200)
        throw new ControlPlaneException(403, "TENANT_CONTEXT_INVALID", "Tenant context was invalid.");
    return new OperatorIdentity(tenantId, subject, name, tenantName,
        string.Equals(principal.FindFirstValue("tenant_verified"), "true", StringComparison.OrdinalIgnoreCase));
}

public partial class Program { }

internal sealed class TestOidcHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-Operator-Subject", out Microsoft.Extensions.Primitives.StringValues subject))
            return Task.FromResult(AuthenticateResult.NoResult());
        Claim[] claims =
        [
            new("sub", subject.ToString()),
            new("name", Request.Headers["X-Test-Operator-Name"].ToString()),
            new("tenant_id", Request.Headers["X-Test-Tenant-Id"].ToString()),
            new("tenant_name", Request.Headers["X-Test-Tenant-Name"].ToString()),
            new("tenant_verified", Request.Headers["X-Test-Tenant-Verified"].ToString()),
        ];
        ClaimsPrincipal principal = new(new ClaimsIdentity(claims, Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}
