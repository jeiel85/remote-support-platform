using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RemoteSupport.Server;

namespace RemoteSupport.Server.IntegrationTests;

public sealed class UpdateAndObservabilityApiTests
{
    [Fact]
    [Trait("Requirement", "AT-NFR-SEC-009")]
    [Trait("Requirement", "AT-NFR-REL-001")]
    public async Task UpdatePublicationIsSequentialBoundAndMetricsRequireScrapeCredential()
    {
        using PublicationRoot publication = new();
        await using PublicationFactory factory = new(publication.Path);
        using HttpClient client = factory.CreateClient();
        Assert.Equal(publication.Path, factory.Services.GetRequiredService<UpdatePublicationOptions>().Directory);
        Assert.NotNull(factory.Services.GetRequiredService<UpdatePublicationStore>().GetNextRoot(0));

        using HttpResponseMessage root = await client.GetAsync("/updates/root?currentRootVersion=0");
        Assert.Equal(HttpStatusCode.OK, root.StatusCode);
        Assert.NotNull(root.Headers.ETag);
        using HttpResponseMessage noSkippedRoot = await client.GetAsync("/updates/root?currentRootVersion=1");
        Assert.Equal(HttpStatusCode.NotModified, noSkippedRoot.StatusCode);

        using HttpResponseMessage manifest = await client.GetAsync(
            "/updates/manifest?product=OPERATOR_CONSOLE&channel=canary&architecture=x64&currentSequence=40");
        Assert.Equal(HttpStatusCode.OK, manifest.StatusCode);
        Assert.Equal("no-store", manifest.Headers.CacheControl?.ToString());
        Assert.True(manifest.Headers.Contains("X-Correlation-Id"));
        using HttpResponseMessage current = await client.GetAsync(
            "/updates/manifest?product=OPERATOR_CONSOLE&channel=canary&architecture=x64&currentSequence=41");
        Assert.Equal(HttpStatusCode.NotModified, current.StatusCode);

        using HttpResponseMessage hidden = await client.GetAsync("/internal/metrics");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using HttpRequestMessage scrape = new(HttpMethod.Get, "/internal/metrics");
        scrape.Headers.Authorization = new AuthenticationHeaderValue("Bearer", PublicationFactory.MetricsToken);
        using HttpResponseMessage metrics = await client.SendAsync(scrape);
        metrics.EnsureSuccessStatusCode();
        string text = await metrics.Content.ReadAsStringAsync();
        Assert.Contains("rsp_control_api_requests_total", text, StringComparison.Ordinal);
        Assert.Contains("route=\"/updates/manifest\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("OPERATOR_CONSOLE", text, StringComparison.Ordinal);
    }

    private sealed class PublicationFactory(string directory) : WebApplicationFactory<Program>
    {
        public const string MetricsToken = "goal11-test-metrics-token-00000001";
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ControlPlane:LookupKeyBase64"] = Convert.ToBase64String(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray()),
                    ["ControlPlane:TokenSigningKeyBase64"] = Convert.ToBase64String(Enumerable.Range(33, 32).Select(value => (byte)value).ToArray()),
                    ["ControlPlane:UseInMemoryStore"] = "true",
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ObservabilityOptions>();
                services.AddSingleton(new ObservabilityOptions { MetricsBearerToken = MetricsToken });
                services.RemoveAll<UpdatePublicationOptions>();
                services.RemoveAll<UpdatePublicationStore>();
                services.AddSingleton(new UpdatePublicationOptions { Directory = directory });
                services.AddSingleton<UpdatePublicationStore>();
            });
        }
    }

    private sealed class PublicationRoot : IDisposable
    {
        public PublicationRoot()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rsp-publication-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(System.IO.Path.Combine(Path, "roots"));
            Directory.CreateDirectory(System.IO.Path.Combine(Path, "manifests"));
            File.WriteAllBytes(System.IO.Path.Combine(Path, "roots", "root-1.json"), JsonSerializer.SerializeToUtf8Bytes(new
            {
                signed = new { rootVersion = 1 },
                signatures = new[] { new { keyId = "test", algorithm = "ed25519", signature = new string('a', 64) } },
            }));
            File.WriteAllBytes(System.IO.Path.Combine(Path, "manifests", "OPERATOR_CONSOLE.canary.x64.json"),
                JsonSerializer.SerializeToUtf8Bytes(new
                {
                    signed = new
                    {
                        product = "OPERATOR_CONSOLE", channel = "canary", releaseSequence = 41,
                        artifacts = new[] { new { architecture = "x64" } },
                    },
                    signatures = new[] { new { keyId = "test", algorithm = "ed25519", signature = new string('a', 64) } },
                }));
        }
        public string Path { get; }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
