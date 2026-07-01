namespace RemoteSupport.Domain;

public readonly record struct ErrorCode
{
    public ErrorCode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > 96 || value.Any(character => !(character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_')))
        {
            throw new ArgumentException("Error codes use 1-96 uppercase ASCII letters, digits, and underscores.", nameof(value));
        }
        Value = value;
    }

    public string Value { get; }
    public override string ToString() => Value;
}

