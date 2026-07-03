using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace RemoteSupport.Observability;

public sealed record SupportBundleSnapshot(
    string Product,
    string Version,
    string CorrelationId,
    string SessionState,
    string RouteClass,
    uint RttMilliseconds,
    uint PacketLossPermyriad,
    string? StableErrorCode,
    DateTimeOffset CapturedAt);

public static class SupportBundleBuilder
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static async Task<string> CreateAsync(string destinationDirectory, SupportBundleSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Validate(snapshot);
        string root = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, $"remote-support-diagnostics-{snapshot.CapturedAt:yyyyMMdd-HHmmss}.zip");
        await using FileStream output = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using ZipArchive archive = new(output, ZipArchiveMode.Create, leaveOpen: true);
        await WriteJsonAsync(archive, "product.json", new
        {
            schemaVersion = 1,
            snapshot.Product,
            snapshot.Version,
            os = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            framework = RuntimeInformation.FrameworkDescription,
            capturedAt = snapshot.CapturedAt,
        }, cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(archive, "session.json", new
        {
            schemaVersion = 1,
            snapshot.CorrelationId,
            snapshot.SessionState,
            snapshot.RouteClass,
            snapshot.RttMilliseconds,
            snapshot.PacketLossPermyriad,
            snapshot.StableErrorCode,
        }, cancellationToken).ConfigureAwait(false);
        return path;
    }

    private static async Task WriteJsonAsync(ZipArchive archive, string name, object value, CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await using Stream stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, value, Json, cancellationToken).ConfigureAwait(false);
    }

    private static void Validate(SupportBundleSnapshot snapshot)
    {
        if (snapshot.Product.Length is < 1 or > 64 || snapshot.Version.Length is < 1 or > 64 ||
            snapshot.CorrelationId.Length is < 1 or > 128 || snapshot.SessionState.Length is < 1 or > 64 ||
            snapshot.RouteClass.Length is < 1 or > 64 || (snapshot.StableErrorCode?.Length ?? 0) > 96 ||
            snapshot.PacketLossPermyriad > 10_000)
            throw new ArgumentException("Diagnostic snapshot is outside the allowlisted schema.", nameof(snapshot));
    }
}
