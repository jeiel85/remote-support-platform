namespace RemoteSupport.Domain;

public enum CapabilityScope
{
    ViewScreen = 0,
    ControlPointer = 1,
    ControlKeyboard = 2,
    SyncClipboardTextHostToOperator = 3,
    SyncClipboardTextOperatorToHost = 4,
    TransferFileHostToOperator = 5,
    TransferFileOperatorToHost = 6,
    Chat = 7,
    SwitchMonitor = 8,
    RequestReboot = 9,
    ReconnectAfterReboot = 10,
    UnattendedSession = 11,
}

public readonly record struct ScopeSet
{
    private readonly ulong bits;

    private ScopeSet(ulong bits) => this.bits = bits;

    public static ScopeSet Empty => default;

    public static ScopeSet From(params ReadOnlySpan<CapabilityScope> scopes)
    {
        ulong value = 0;
        foreach (CapabilityScope scope in scopes)
        {
            Validate(scope);
            value |= 1UL << (int)scope;
        }
        return new ScopeSet(value);
    }

    public bool Contains(CapabilityScope scope)
    {
        Validate(scope);
        return (bits & (1UL << (int)scope)) != 0;
    }

    public bool IsSubsetOf(ScopeSet other) => (bits & ~other.bits) == 0;

    public ScopeSet GrantSubset(ScopeSet requested)
    {
        if (!IsSubsetOf(requested))
        {
            throw new InvalidOperationException("Granted scopes must be a subset of requested scopes.");
        }
        return this;
    }

    public IReadOnlyList<CapabilityScope> Values => Enum.GetValues<CapabilityScope>().Where(Contains).ToArray();

    private static void Validate(CapabilityScope scope)
    {
        if (!Enum.IsDefined(scope))
        {
            throw new ArgumentOutOfRangeException(nameof(scope));
        }
    }
}

