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
            SupportBundlePreview preview = SupportBundleBuilder.Preview(snapshot);
            Assert.Contains("clipboard, chat and transferred-file content", preview.ExcludedSensitiveCategories);
            await Assert.ThrowsAsync<InvalidOperationException>(() => SupportBundleBuilder.CreateAsync(directory,
                snapshot, new SupportBundleApproval(preview.PreviewId, Approved: false)));
            string bundle = await SupportBundleBuilder.CreateAsync(directory, snapshot,
                new SupportBundleApproval(preview.PreviewId, Approved: true));
            using ZipArchive archive = ZipFile.OpenRead(bundle);
            Assert.Equal(["privacy.json", "product.json", "session.json"], archive.Entries.Select(entry => entry.FullName).Order().ToArray());
            string content = string.Join('\n', archive.Entries.Select(Read));
            string diagnostics = string.Join('\n', archive.Entries.Where(entry => entry.FullName != "privacy.json").Select(Read));
            Assert.DoesNotContain("token", diagnostics, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("clipboard", diagnostics, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("chat", diagnostics, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("file content", diagnostics, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("correlation-123", content, StringComparison.Ordinal);
            Assert.Contains("\"uploadPerformed\": false", content, StringComparison.Ordinal);
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
