using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using RemoteSupport.AdminPortal;
using RemoteSupport.AdminPortal.Components;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
bool testing = builder.Environment.IsEnvironment("Testing");
builder.Services.AddRazorComponents();
builder.Services.AddHttpContextAccessor();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.Name = "__Host-rsp-admin";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.IdleTimeout = TimeSpan.FromMinutes(15);
});
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "__Host-rsp-csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
});
builder.Services.AddHttpClient<ControlPlaneBffClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ControlPlane:BaseUrl"] ?? "https://localhost:7043/");
    client.Timeout = TimeSpan.FromSeconds(15);
});

if (testing)
{
    builder.Services.AddAuthentication("PortalTest").AddScheme<AuthenticationSchemeOptions,
        PortalTestAuthenticationHandler>("PortalTest", _ => { });
}
else
{
    string authority = builder.Configuration["Oidc:Authority"] ??
        throw new InvalidOperationException("Oidc:Authority is required.");
    string clientId = builder.Configuration["Oidc:ClientId"] ??
        throw new InvalidOperationException("Oidc:ClientId is required.");
    string clientSecret = builder.Configuration["Oidc:ClientSecret"] ??
        throw new InvalidOperationException("Oidc:ClientSecret is required.");
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    }).AddCookie(options =>
    {
        options.Cookie.Name = "__Host-rsp-admin-auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.SlidingExpiration = false;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    }).AddOpenIdConnect(options =>
    {
        options.Authority = authority;
        options.ClientId = clientId;
        options.ClientSecret = clientSecret;
        options.ResponseType = "code";
        options.UsePkce = true;
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = false;
        options.MapInboundClaims = false;
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.Scope.Add("remote-support-api");
    });
}
builder.Services.AddAuthorization(options => options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
    .RequireAuthenticatedUser().Build());

WebApplication app = builder.Build();
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});
if (!testing) app.UseHsts();
app.Use(async (context, next) =>
{
    string nonce = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(18));
    context.Items["CspNonce"] = nonce;
    context.Response.Headers.ContentSecurityPolicy =
        $"default-src 'none'; style-src 'self'; img-src 'self'; font-src 'self'; form-action 'self'; " +
        $"base-uri 'none'; frame-ancestors 'none'; object-src 'none'; script-src 'nonce-{nonce}'";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    await next();
});
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseSession();
app.UseAuthorization();
app.UseMiddleware<OriginValidationMiddleware>();
app.UseAntiforgery();

app.MapGet("/signin", () => Results.Challenge(new AuthenticationProperties { RedirectUri = "/overview" },
    [OpenIdConnectDefaults.AuthenticationScheme])).AllowAnonymous();
app.MapPost("/signout", async (HttpContext context) =>
{
    context.Session.Clear();
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/");
});
app.MapPost("/actions/tenant/select", async (HttpContext context, ControlPlaneBffClient api) =>
{
    IFormCollection form = await context.Request.ReadFormAsync();
    if (!Guid.TryParse(form["tenantId"], out Guid tenantId) || !await api.CanSelectTenant(tenantId))
        return Results.Redirect("/overview?error=tenant");
    context.Session.SetString(ControlPlaneBffClient.TenantSessionKey, tenantId.ToString("D"));
    return Results.Redirect("/overview");
});
app.MapPost("/actions/invitations", async (HttpContext context, ControlPlaneBffClient api) =>
{
    IFormCollection form = await context.Request.ReadFormAsync();
    InvitationView invitation = await api.CreateInvitation(form["email"].ToString(), form["role"].ToString());
    if (!string.IsNullOrEmpty(invitation.AcceptanceToken))
    {
        context.Session.SetString("invitation-token", invitation.AcceptanceToken);
        context.Session.SetString("invitation-email", invitation.Email);
    }
    return Results.Redirect("/members");
});
app.MapPost("/actions/settings", async (HttpContext context, ControlPlaneBffClient api) =>
{
    IFormCollection form = await context.Request.ReadFormAsync();
    if (!int.TryParse(form["retentionDays"], NumberStyles.None, CultureInfo.InvariantCulture, out int retention) ||
        !long.TryParse(form["fileSizeLimitBytes"], NumberStyles.None, CultureInfo.InvariantCulture, out long fileLimit) ||
        !long.TryParse(form["settingsVersion"], NumberStyles.None, CultureInfo.InvariantCulture, out long version))
        return Results.Redirect("/settings?error=input");
    await api.UpdateSettings(retention, fileLimit, version);
    return Results.Redirect("/settings?saved=true");
});
app.MapPost("/actions/policies", async (HttpContext context, ControlPlaneBffClient api) =>
{
    IFormCollection form = await context.Request.ReadFormAsync();
    await api.CreatePolicy(form["name"].ToString(), form["document"].ToString());
    return Results.Redirect("/policies");
});
app.MapPost("/actions/exports", async (ControlPlaneBffClient api) =>
{
    await api.RequestExport();
    return Results.Redirect("/privacy/export?requested=true");
});
app.MapPost("/actions/closure", async (HttpContext context, ControlPlaneBffClient api) =>
{
    IFormCollection form = await context.Request.ReadFormAsync();
    await api.RequestClosure(form["confirmationPhrase"].ToString(), form["reason"].ToString());
    return Results.Redirect("/tenant/close?requested=true");
});

app.MapRazorComponents<App>();
app.Run();

public partial class Program { }
