using System.Text.Json;

namespace RemoteSupport.Infrastructure;

public sealed record ClientUpdateConfiguration(Uri ManifestUrl, string Channel, int TrustedRootVersion, bool AutomaticCheck);
public sealed record ClientLimits(int MaxControlMessageBytes, int MaxFileChunkBytes, int MaxClipboardUtf8Bytes, ulong MaxFileBytes);
public sealed record OperatorOidcConfiguration(Uri Authority, string ClientId, IReadOnlyList<string> Scopes);
public sealed record ClientConfiguration(
    int SchemaVersion,
    Uri ApiBaseUrl,
    Uri SignalingUrl,
    string Environment,
    ClientUpdateConfiguration Update,
    ClientLimits Limits,
    OperatorOidcConfiguration? OperatorOidc)
{
    public static async Task<ClientConfiguration> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 16,
        }, cancellationToken).ConfigureAwait(false);
        JsonElement root = document.RootElement;
        RequireProperties(root, ["schemaVersion", "apiBaseUrl", "signalingUrl", "environment", "update", "limits"],
            ["operatorOidc", "diagnostics"]);
        if (root.GetProperty("schemaVersion").GetInt32() != 1) throw Invalid("Unsupported client configuration version.");
        Uri api = RequireSecureUri(root.GetProperty("apiBaseUrl").GetString(), "https");
        Uri signaling = RequireSecureUri(root.GetProperty("signalingUrl").GetString(), "wss");
        string environment = root.GetProperty("environment").GetString() ?? string.Empty;
        if (environment is not ("development" or "staging" or "production")) throw Invalid("Environment is invalid.");

        JsonElement update = root.GetProperty("update");
        RequireProperties(update, ["manifestUrl", "channel", "trustedRootVersion"], ["automaticCheck"]);
        string channel = update.GetProperty("channel").GetString() ?? string.Empty;
        if (channel is not ("internal" or "canary" or "stable")) throw Invalid("Update channel is invalid.");
        int rootVersion = update.GetProperty("trustedRootVersion").GetInt32();
        if (rootVersion < 1) throw Invalid("Trusted update root version is invalid.");
        ClientUpdateConfiguration updateConfiguration = new(RequireSecureUri(update.GetProperty("manifestUrl").GetString(), "https"),
            channel, rootVersion, update.TryGetProperty("automaticCheck", out JsonElement auto) && auto.GetBoolean());

        JsonElement limits = root.GetProperty("limits");
        RequireProperties(limits, ["maxControlMessageBytes", "maxFileChunkBytes", "maxClipboardUtf8Bytes", "maxFileBytes"], []);
        ClientLimits clientLimits = new(limits.GetProperty("maxControlMessageBytes").GetInt32(),
            limits.GetProperty("maxFileChunkBytes").GetInt32(), limits.GetProperty("maxClipboardUtf8Bytes").GetInt32(),
            limits.GetProperty("maxFileBytes").GetUInt64());
        if (clientLimits.MaxControlMessageBytes is < 4096 or > 1024 * 1024 ||
            clientLimits.MaxFileChunkBytes is < 16 * 1024 or > 1024 * 1024 ||
            clientLimits.MaxClipboardUtf8Bytes is < 1024 or > 1024 * 1024 || clientLimits.MaxFileBytes is 0 or > 1UL << 40)
            throw Invalid("Client limit is outside product hard bounds.");

        OperatorOidcConfiguration? oidc = null;
        if (root.TryGetProperty("operatorOidc", out JsonElement oidcElement))
        {
            RequireProperties(oidcElement, ["authority", "clientId", "scopes"], []);
            string clientId = oidcElement.GetProperty("clientId").GetString() ?? string.Empty;
            string[] scopes = oidcElement.GetProperty("scopes").EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray();
            if (clientId.Length is < 1 or > 200 || scopes.Length is < 1 or > 32 || scopes.Any(scope => scope.Length is < 1 or > 128))
                throw Invalid("OIDC client configuration is invalid.");
            oidc = new OperatorOidcConfiguration(RequireSecureUri(oidcElement.GetProperty("authority").GetString(), "https"), clientId, scopes);
        }
        return new ClientConfiguration(1, api, signaling, environment, updateConfiguration, clientLimits, oidc);
    }

    private static void RequireProperties(JsonElement element, IReadOnlyCollection<string> required, IReadOnlyCollection<string> optional)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Invalid("Configuration object expected.");
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name) || (!required.Contains(property.Name) && !optional.Contains(property.Name)))
                throw Invalid("Configuration contains duplicate or unknown properties.");
        }
        if (required.Any(name => !seen.Contains(name))) throw Invalid("Configuration is missing a required property.");
    }

    private static Uri RequireSecureUri(string? value, string scheme)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || !string.Equals(uri.Scheme, scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) || uri.IsLoopback && scheme == "https" && uri.Port == 0)
            throw Invalid("Configuration URL is invalid or insecure.");
        return uri;
    }

    private static InvalidDataException Invalid(string message) => new(message);
}
