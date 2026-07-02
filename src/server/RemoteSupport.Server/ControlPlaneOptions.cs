using System.Security.Cryptography;

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
    public bool UseInMemoryStore { get; set; }

    public byte[] GetLookupKey() => DecodeKey(LookupKeyBase64, nameof(LookupKeyBase64));

    public byte[] GetTokenSigningKey() => DecodeKey(TokenSigningKeyBase64, nameof(TokenSigningKeyBase64));

    public void Validate(bool allowEphemeral)
    {
        if (SessionLifetime <= TimeSpan.Zero || SessionLifetime > TimeSpan.FromMinutes(15) ||
            HostBootstrapLifetime <= TimeSpan.Zero || HostBootstrapLifetime > TimeSpan.FromMinutes(10) ||
            OperatorBootstrapLifetime <= TimeSpan.Zero || OperatorBootstrapLifetime > TimeSpan.FromMinutes(5) ||
            PeerTokenLifetime <= TimeSpan.Zero || PeerTokenLifetime > TimeSpan.FromMinutes(15) ||
            ChallengeLifetime <= TimeSpan.Zero || ChallengeLifetime > TimeSpan.FromMinutes(2) ||
            MaximumCodeAttempts is < 1 or > 20 || ResolveRequestsPerMinute is < 1 or > 10_000)
        {
            throw new InvalidOperationException("Control-plane security lifetime or limit configuration is invalid.");
        }

        if (allowEphemeral)
        {
            LookupKeyBase64 ??= Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            TokenSigningKeyBase64 ??= Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        }

        _ = GetLookupKey();
        _ = GetTokenSigningKey();
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
