using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace RemoteSupport.AdminPortal;

internal sealed class OriginValidationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPatch(context.Request.Method) ||
            HttpMethods.IsDelete(context.Request.Method) || HttpMethods.IsPut(context.Request.Method))
        {
            string origin = context.Request.Headers.Origin.ToString();
            if (!string.IsNullOrEmpty(origin) && (!Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri) ||
                !string.Equals(uri.Authority, context.Request.Host.Value, StringComparison.OrdinalIgnoreCase)))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
        }
        await next(context);
    }
}

internal sealed class PortalTestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        Claim[] claims = [new("sub", "portal-test-user"), new("name", "Portal Test User")];
        ClaimsPrincipal principal = new(new ClaimsIdentity(claims, Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}
