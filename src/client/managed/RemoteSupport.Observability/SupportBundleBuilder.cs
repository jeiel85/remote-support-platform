using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
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

public sealed record SupportBundlePreview(string PreviewId, IReadOnlyList<string> IncludedFiles,
    IReadOnlyList<string> ExcludedSensitiveCategories);
public sealed record SupportBundleApproval(string PreviewId, bool Approved);

public static class SupportBundleBuilder
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static SupportBundlePreview Preview(SupportBundleSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Validate(snapshot);
        string input = JsonSerializer.Serialize(snapshot);
        string id = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("RSP-SUPPORT-PREVIEW-V1\0" + input)));
        return new SupportBundlePreview(id, ["product.json", "session.json", "privacy.json"],
            ["access tokens and credentials", "screen and keystroke content", "clipboard, chat and transferred-file content",
                "raw SDP and IP addresses", "user email and device name"]);
    }

    public static async Task<string> CreateAsync(string destinationDirectory, SupportBundleSnapshot snapshot,
        SupportBundleApproval approval,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(approval);
        Validate(snapshot);
        SupportBundlePreview preview = Preview(snapshot);
        if (!approval.Approved || !CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(preview.PreviewId), Encoding.ASCII.GetBytes(approval.PreviewId)))
            throw new InvalidOperationException("Support bundle preview must be explicitly approved without modification.");
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
        await WriteJsonAsync(archive, "privacy.json", new
        {
            schemaVersion = 1,
            preview.PreviewId,
            includedFiles = preview.IncludedFiles,
            excludedSensitiveCategories = preview.ExcludedSensitiveCategories,
            uploadPerformed = false,
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
        if (!Bounded(snapshot.Product, 64) || !Version(snapshot.Version) ||
            !Bounded(snapshot.CorrelationId, 128) || !Bounded(snapshot.SessionState, 64) ||
            !Bounded(snapshot.RouteClass, 64) || (snapshot.StableErrorCode is not null && !Bounded(snapshot.StableErrorCode, 96)) ||
            snapshot.PacketLossPermyriad > 10_000)
            throw new ArgumentException("Diagnostic snapshot is outside the allowlisted schema.", nameof(snapshot));
    }

    private static bool Bounded(string value, int maximum) => value.Length is > 0 && value.Length <= maximum &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
    private static bool Version(string value) => value.Length is > 0 and <= 64 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '.' or '+');
}
