namespace RemoteSupport.Domain;

public readonly record struct SessionId
{
    public SessionId(Guid value) => Value = value != Guid.Empty ? value : throw new ArgumentException("Session ID cannot be empty.", nameof(value));
    public Guid Value { get; }
    public static SessionId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
}

public readonly record struct TenantId
{
    public TenantId(Guid value) => Value = value != Guid.Empty ? value : throw new ArgumentException("Tenant ID cannot be empty.", nameof(value));
    public Guid Value { get; }
    public override string ToString() => Value.ToString("D");
}

public readonly record struct PeerId
{
    public PeerId(Guid value) => Value = value != Guid.Empty ? value : throw new ArgumentException("Peer ID cannot be empty.", nameof(value));
    public Guid Value { get; }
    public override string ToString() => Value.ToString("D");
}

public readonly record struct DeviceId
{
    public DeviceId(Guid value) => Value = value != Guid.Empty ? value : throw new ArgumentException("Device ID cannot be empty.", nameof(value));
    public Guid Value { get; }
    public override string ToString() => Value.ToString("D");
}

public readonly record struct CorrelationId
{
    public CorrelationId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > 128)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Correlation ID cannot exceed 128 characters.");
        }
        Value = value;
    }

    public string Value { get; }
    public override string ToString() => Value;
}

