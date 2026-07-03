using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace RemoteSupport.AdminPortal.Tests;

public sealed class PortalRouteTests
{
    private static readonly string[] RequiredRoutes =
    [
        "/overview", "/members", "/devices", "/policies", "/audit", "/settings",
        "/privacy/export", "/tenant/close",
    ];
    private static readonly string[] StateChangingRoutes =
        ["/members", "/policies", "/settings", "/privacy/export", "/tenant/close"];

    [Fact]
    [Trait("Requirement", "AT-FR-ADM-007")]
    public async Task RequiredAdminRoutesRenderWithSecurityHeadersAndNoBrowserTokenStorage()
    {
        await using PortalFactory factory = new();
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        foreach (string route in RequiredRoutes)
        {
            using HttpResponseMessage response = await client.GetAsync(route);
            response.EnsureSuccessStatusCode();
            string html = await response.Content.ReadAsStringAsync();
            Assert.Contains("Remote Support", html, StringComparison.Ordinal);
            Assert.DoesNotContain("localStorage", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("bearer", html, StringComparison.OrdinalIgnoreCase);
            Assert.True(response.Headers.TryGetValues("Content-Security-Policy", out IEnumerable<string>? values));
            Assert.Contains("frame-ancestors 'none'", Assert.Single(values), StringComparison.Ordinal);
            Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        }
    }

    [Fact]
    public async Task StateChangingFormsContainAntiforgeryTokens()
    {
        await using PortalFactory factory = new();
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });
        foreach (string route in StateChangingRoutes)
        {
            string html = await client.GetStringAsync(route);
            Assert.Contains("__RequestVerificationToken", html, StringComparison.Ordinal);
        }
    }

    private sealed class PortalFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ControlPlane:BaseUrl"] = "https://api.invalid/" }));
        }
    }
}
