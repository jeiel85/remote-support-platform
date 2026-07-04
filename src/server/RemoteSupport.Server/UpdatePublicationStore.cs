using System.Security.Cryptography;
using System.Text.Json;

namespace RemoteSupport.Server;

public sealed class UpdatePublicationOptions
{
    public const string SectionName = "UpdatePublication";
    public string? Directory { get; set; }

    public void Validate(bool allowMissing)
    {
        if (allowMissing && string.IsNullOrWhiteSpace(Directory)) return;
        if (string.IsNullOrWhiteSpace(Directory) || !Path.IsPathFullyQualified(Directory))
            throw new InvalidOperationException("UpdatePublication:Directory must be an absolute protected publication directory.");
    }
}

public sealed record PublishedUpdate(byte[] Content, string ETag);

public sealed class UpdatePublicationStore(UpdatePublicationOptions options)
{
    public PublishedUpdate? GetNextRoot(int currentVersion)
    {
        if (currentVersion < 0 || currentVersion == int.MaxValue)
            throw new ControlPlaneException(400, "UPDATE_ROOT_SEQUENCE_REQUIRED", "Current root version is invalid.");
        if (string.IsNullOrWhiteSpace(options.Directory)) return null;
        string path = UnderRoot(Path.Combine("roots", $"root-{checked(currentVersion + 1)}.json"));
        return Read(path, signed => signed.GetProperty("rootVersion").GetInt32() == currentVersion + 1);
    }

    public PublishedUpdate? GetManifest(string product, string channel, string architecture, long currentSequence)
    {
        if (!Safe(product, 64) || channel is not ("internal" or "canary" or "stable") ||
            architecture is not ("x64" or "arm64") || currentSequence < 0)
            throw new ControlPlaneException(400, "UPDATE_REQUEST_INVALID", "Update request binding is invalid.");
        if (string.IsNullOrWhiteSpace(options.Directory)) return null;
        string path = UnderRoot(Path.Combine("manifests", $"{product}.{channel}.{architecture}.json"));
        PublishedUpdate? publication = Read(path, signed => signed.GetProperty("product").GetString() == product &&
            signed.GetProperty("channel").GetString() == channel &&
            signed.GetProperty("artifacts").EnumerateArray().Any(item => item.GetProperty("architecture").GetString() == architecture));
        if (publication is null) return null;
        using JsonDocument document = JsonDocument.Parse(publication.Content);
        return document.RootElement.GetProperty("signed").GetProperty("releaseSequence").GetInt64() > currentSequence
            ? publication : null;
    }

    private static PublishedUpdate? Read(string path, Func<JsonElement, bool> binding)
    {
        if (!File.Exists(path)) return null;
        FileInfo file = new(path);
        if (file.Length is < 64 or > 1024 * 1024) throw new InvalidOperationException("Published update metadata size is invalid.");
        byte[] content = File.ReadAllBytes(path);
        if (content.LongLength != file.Length || content.Length > 1024 * 1024)
            throw new InvalidOperationException("Published update metadata changed during the read.");
        using JsonDocument document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 32 });
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("signed", out JsonElement signed) ||
            !root.TryGetProperty("signatures", out JsonElement signatures) || signatures.GetArrayLength() < 1 || !binding(signed))
            throw new InvalidOperationException("Published update metadata binding is invalid.");
        return new PublishedUpdate(content, '"' + Convert.ToHexStringLower(SHA256.HashData(content)) + '"');
    }

    private string UnderRoot(string relative)
    {
        string root = Path.GetFullPath(options.Directory!).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Update publication path escaped its root.");
        return path;
    }

    private static bool Safe(string value, int maximum) => value.Length is > 0 && value.Length <= maximum &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}
