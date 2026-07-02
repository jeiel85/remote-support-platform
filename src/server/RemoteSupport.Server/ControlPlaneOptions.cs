using System.Security.Cryptography;
using System.Text;

namespace RemoteSupport.Server;

public sealed class ControlPlaneOptions
{
    public const string SectionName = "ControlPlane";

    public string? LookupKeyBase64 { get; set; }
    public string? TokenSigningKeyBase64 { get; set; }
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan HostBootstrapLifetime { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan OperatorBootstrapLifetime { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan PeerTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan ChallengeLifetime { get; set; } = TimeSpan.FromMinutes(2);
    public int MaximumCodeAttempts { get; set; } = 5;
    public int ResolveRequestsPerMinute { get; set; } = 20;
    public string? SignalingPublicUrl { get; set; }
    public TimeSpan SignalingTicketLifetime { get; set; } = TimeSpan.FromSeconds(60);
    public TimeSpan DpopProofLifetime { get; set; } = TimeSpan.FromMinutes(2);
    public string? TurnSharedSecretBase64 { get; set; }
    public string? TurnMeteringKeyBase64 { get; set; }
    public string TurnRegion { get; set; } = "local";
    public string[] TurnUrls { get; set; } =
    [
        "turn:turn.invalid:3478?transport=udp",
        "turn:turn.invalid:3478?transport=tcp",
        "turns:turn.invalid:5349?transport=tcp",
    ];
    public TimeSpan TurnCredentialLifetime { get; set; } = TimeSpan.FromMinutes(10);
    public bool UseInMemoryStore { get; set; }

    public byte[] GetLookupKey() => DecodeKey(LookupKeyBase64, nameof(LookupKeyBase64));

    public byte[] GetTokenSigningKey() => DecodeKey(TokenSigningKeyBase64, nameof(TokenSigningKeyBase64));

    public byte[] GetTurnSharedSecret() => Encoding.ASCII.GetBytes(ValidateEncodedSecret(
        TurnSharedSecretBase64, nameof(TurnSharedSecretBase64)));

    public byte[] GetTurnMeteringKey() => Convert.FromBase64String(ValidateEncodedSecret(
        TurnMeteringKeyBase64, nameof(TurnMeteringKeyBase64)));

    public void Validate(bool allowEphemeral)
    {
        if (SessionLifetime <= TimeSpan.Zero || SessionLifetime > TimeSpan.FromMinutes(15) ||
            HostBootstrapLifetime <= TimeSpan.Zero || HostBootstrapLifetime > TimeSpan.FromMinutes(10) ||
            OperatorBootstrapLifetime <= TimeSpan.Zero || OperatorBootstrapLifetime > TimeSpan.FromMinutes(5) ||
            PeerTokenLifetime <= TimeSpan.Zero || PeerTokenLifetime > TimeSpan.FromMinutes(15) ||
            ChallengeLifetime <= TimeSpan.Zero || ChallengeLifetime > TimeSpan.FromMinutes(2) ||
            SignalingTicketLifetime <= TimeSpan.Zero || SignalingTicketLifetime > TimeSpan.FromSeconds(60) ||
            DpopProofLifetime <= TimeSpan.Zero || DpopProofLifetime > TimeSpan.FromMinutes(2) ||
            TurnCredentialLifetime <= TimeSpan.Zero || TurnCredentialLifetime > TimeSpan.FromMinutes(10) ||
            MaximumCodeAttempts is < 1 or > 20 || ResolveRequestsPerMinute is < 1 or > 10_000)
        {
            throw new InvalidOperationException("Control-plane security lifetime or limit configuration is invalid.");
        }

        if (allowEphemeral)
        {
            LookupKeyBase64 ??= Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            TokenSigningKeyBase64 ??= Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            TurnSharedSecretBase64 ??= Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            TurnMeteringKeyBase64 ??= Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        }

        _ = GetLookupKey();
        _ = GetTokenSigningKey();
        _ = GetTurnSharedSecret();
        _ = GetTurnMeteringKey();
        byte[][] separatedKeys =
        [
            GetLookupKey(), GetTokenSigningKey(), Convert.FromBase64String(TurnSharedSecretBase64!), GetTurnMeteringKey(),
        ];
        for (int left = 0; left < separatedKeys.Length; left++)
        for (int right = left + 1; right < separatedKeys.Length; right++)
            if (CryptographicOperations.FixedTimeEquals(separatedKeys[left], separatedKeys[right]))
                throw new InvalidOperationException("Control-plane lookup, token, TURN, and metering keys must be distinct.");
        if (!allowEphemeral && (!Uri.TryCreate(SignalingPublicUrl, UriKind.Absolute, out Uri? signaling) ||
            signaling.Scheme != "wss" || !string.IsNullOrEmpty(signaling.Query) || !string.IsNullOrEmpty(signaling.Fragment)))
            throw new InvalidOperationException("SignalingPublicUrl must be an absolute wss URL without query or fragment.");
        if (string.IsNullOrWhiteSpace(TurnRegion) || TurnRegion.Length > 64 || TurnUrls.Length is < 3 or > 16 ||
            TurnUrls.Any(url => string.IsNullOrWhiteSpace(url) || url.Length > 512 || url.Contains('@') ||
                url.Any(character => character is <= ' ' or > '~')) ||
            TurnUrls.Distinct(StringComparer.OrdinalIgnoreCase).Count() != TurnUrls.Length ||
            !TurnUrls.Any(url => url.StartsWith("turn:", StringComparison.OrdinalIgnoreCase) && url.Contains("transport=udp", StringComparison.OrdinalIgnoreCase)) ||
            !TurnUrls.Any(url => url.StartsWith("turn:", StringComparison.OrdinalIgnoreCase) && url.Contains("transport=tcp", StringComparison.OrdinalIgnoreCase)) ||
            !TurnUrls.Any(url => url.StartsWith("turns:", StringComparison.OrdinalIgnoreCase) && url.Contains("transport=tcp", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("TURN region and UDP/TCP/TLS route configuration are required.");
        if (!allowEphemeral && (TurnRegion == "local" || TurnUrls.Any(url => url.Contains(".invalid", StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("Production TURN region and public URLs must be explicitly configured.");
    }

    private static string ValidateEncodedSecret(string? encoded, string name)
    {
        _ = DecodeKey(encoded, name);
        if (encoded!.Any(character => character > 0x7f || char.IsWhiteSpace(character)))
            throw new InvalidOperationException($"{name} must be single-line ASCII base64.");
        return encoded!;
    }

    private static byte[] DecodeKey(string? encoded, string name)
    {
        if (string.IsNullOrWhiteSpace(encoded))
        {
            throw new InvalidOperationException($"{name} is required.");
        }

        byte[] value;
        try
        {
            value = Convert.FromBase64String(encoded);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException($"{name} must be base64.", exception);
        }

        return value.Length >= 32 ? value : throw new InvalidOperationException($"{name} must contain at least 256 bits.");
    }
}

public interface ISystemClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : ISystemClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
