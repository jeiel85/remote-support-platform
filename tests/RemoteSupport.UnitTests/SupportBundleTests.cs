using System.IO.Compression;
using RemoteSupport.Application;
using RemoteSupport.Infrastructure;
using RemoteSupport.Observability;

namespace RemoteSupport.UnitTests;

public sealed class SupportBundleTests
{
    [Fact]
    [Trait("Requirement", "NFR-PRV-006")]
    public async Task BundleContainsOnlyAllowlistedDiagnostics()
    {
        string directory = Path.Combine(Path.GetTempPath(), "rsp-bundle-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            SupportBundleSnapshot snapshot = new("OPERATOR_CONSOLE", "0.9.0", "correlation-123", "CONNECTED",
                "TURN_TLS", 42, 17, "TRANSPORT_ICE_FAILED", DateTimeOffset.UnixEpoch.AddDays(20_000));
            string bundle = await SupportBundleBuilder.CreateAsync(directory, snapshot);
            using ZipArchive archive = ZipFile.OpenRead(bundle);
            Assert.Equal(["product.json", "session.json"], archive.Entries.Select(entry => entry.FullName).Order().ToArray());
            string content = string.Join('\n', archive.Entries.Select(Read));
            Assert.DoesNotContain("token", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("clipboard", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("chat", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("file content", content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("correlation-123", content, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    [Trait("Requirement", "AT-FR-DAT-007")]
    public async Task LocalDataAuditHashesFilenameAndNeverStoresContent()
    {
        string directory = Path.Combine(Path.GetTempPath(), "rsp-audit-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using LocalDataFeatureAuditSink audit = new("agent", directory);
            const string canaryName = "customer-secret-payroll.txt";
            const string canaryContent = "content-canary-never-log";
            await audit.WriteAsync(new DataFeatureAuditRecord("FILE_TRANSFER", Guid.NewGuid(), Guid.NewGuid(),
                "OPERATOR_TO_HOST", canaryName, 123, "FILE_TRANSFER_ACCEPTED", DateTimeOffset.UtcNow),
                CancellationToken.None);
            string persisted = await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(directory)));
            Assert.DoesNotContain(canaryName, persisted, StringComparison.Ordinal);
            Assert.DoesNotContain(canaryContent, persisted, StringComparison.Ordinal);
            Assert.Contains("normalizedNameSha256", persisted, StringComparison.Ordinal);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static string Read(ZipArchiveEntry entry)
    {
        using StreamReader reader = new(entry.Open());
        return reader.ReadToEnd();
    }
}
