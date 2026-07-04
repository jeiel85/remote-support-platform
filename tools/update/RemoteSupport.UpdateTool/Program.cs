using System.Security.Cryptography;
using System.Text.Json;
using RemoteSupport.Security;

return await UpdateTool.RunAsync(args);

internal static class UpdateTool
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            Dictionary<string, string> options = Parse(args);
            switch (Required(options, "command"))
            {
                case "sign": await SignAsync(options); break;
                case "combine": await CombineAsync(options); break;
                case "verify": await VerifyAsync(options); break;
                case "keygen-development": GenerateDevelopmentKey(options); break;
                default: throw new ArgumentException("Command must be sign, combine, verify, or keygen-development.");
            }
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or CryptographicException or
            UpdateSecurityException or UpdateArtifactException or PlatformNotSupportedException)
        {
            Console.Error.WriteLine($"Release verification failed safely: {exception.Message}");
            return 2;
        }
    }

    private static async Task CombineAsync(IReadOnlyDictionary<string, string> options)
    {
        string[] inputs = Required(options, "inputs").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (inputs.Length is < 2 or > 8) throw new ArgumentException("Combine requires two to eight signed documents.");
        List<JsonDocument> documents = [];
        try
        {
            foreach (string input in inputs) documents.Add(JsonDocument.Parse(await File.ReadAllBytesAsync(input)));
            JsonElement signed = documents[0].RootElement.GetProperty("signed");
            if (documents.Skip(1).Any(document => !JsonElement.DeepEquals(signed, document.RootElement.GetProperty("signed"))))
                throw new ArgumentException("Cannot combine signatures over different signed payloads.");
            JsonElement[] signatures = documents.SelectMany(document => document.RootElement.GetProperty("signatures").EnumerateArray())
                .Select(item => item.Clone()).ToArray();
            string[] keyIds = signatures.Select(item => item.GetProperty("keyId").GetString()!).ToArray();
            if (keyIds.Distinct(StringComparer.Ordinal).Count() != keyIds.Length)
                throw new ArgumentException("Combined signatures contain a duplicate key ID.");
            string output = Path.GetFullPath(Required(options, "output"));
            using FileStream stream = new(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(stream, new { signed, signatures }, Json);
            stream.Flush(flushToDisk: true);
            Console.WriteLine($"Combined {signatures.Length} independent metadata signatures.");
        }
        finally { foreach (JsonDocument document in documents) document.Dispose(); }
    }

    private static async Task SignAsync(IReadOnlyDictionary<string, string> options)
    {
        string encoded = Environment.GetEnvironmentVariable("RS_UPDATE_SIGNING_KEY_BASE64URL")
            ?? throw new ArgumentException("RS_UPDATE_SIGNING_KEY_BASE64URL must come from the protected signing job.");
        Environment.SetEnvironmentVariable("RS_UPDATE_SIGNING_KEY_BASE64URL", null);
        byte[] privateKey = UpdateMetadataSigner.DecodeBase64Url(encoded);
        try
        {
            byte[] document = UpdateMetadataSigner.Sign(await File.ReadAllBytesAsync(Required(options, "input")),
                Required(options, "key-id"), privateKey, Required(options, "type"));
            string output = Path.GetFullPath(Required(options, "output"));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using FileStream stream = new(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await stream.WriteAsync(document);
            stream.Flush(flushToDisk: true);
            Console.WriteLine($"Signed {Required(options, "type")} metadata written to {output}.");
        }
        finally { CryptographicOperations.ZeroMemory(privateKey); }
    }

    private static async Task VerifyAsync(IReadOnlyDictionary<string, string> options)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        UpdateTrustStore trust = UpdateTrustStore.LoadBootstrap(await File.ReadAllBytesAsync(Required(options, "root")), now);
        long installed = long.Parse(options.GetValueOrDefault("installed-sequence", "0"),
            System.Globalization.CultureInfo.InvariantCulture);
        VerifiedUpdateManifest manifest = trust.VerifyManifest(await File.ReadAllBytesAsync(Required(options, "manifest")),
            Required(options, "product"), Required(options, "channel"), Required(options, "architecture"), installed,
            Required(options, "rollout-identity"), now);
        await new UpdateArtifactVerifier().VerifyAsync(manifest, Required(options, "artifact"));
        Console.WriteLine($"Verified release {manifest.Product} {manifest.Version}, sequence {manifest.ReleaseSequence}, root {manifest.RootVersion}.");
    }

    private static void GenerateDevelopmentKey(IReadOnlyDictionary<string, string> options)
    {
        string output = Path.GetFullPath(Required(options, "output"));
        if (File.Exists(output)) throw new IOException("Development key output already exists.");
        UpdateSigningKeyPair pair = UpdateMetadataSigner.GenerateKeyPair();
        try
        {
            File.WriteAllText(output, UpdateMetadataSigner.Base64Url(pair.PrivateKey));
            File.WriteAllText(output + ".pub", UpdateMetadataSigner.Base64Url(pair.PublicKey));
            Console.WriteLine("Development-only metadata key generated. Never use this file for a production role.");
        }
        finally { CryptographicOperations.ZeroMemory(pair.PrivateKey); }
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        if (args.Length == 0) throw new ArgumentException("A command is required.");
        Dictionary<string, string> result = new(StringComparer.Ordinal) { ["command"] = args[0].ToLowerInvariant() };
        for (int index = 1; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("Options use --name value pairs.");
            if (!result.TryAdd(args[index][2..], args[index + 1])) throw new ArgumentException("Duplicate option.");
        }
        return result;
    }

    private static string Required(IReadOnlyDictionary<string, string> options, string name) =>
        options.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value) ? value :
            throw new ArgumentException($"Missing --{name}.");
}
