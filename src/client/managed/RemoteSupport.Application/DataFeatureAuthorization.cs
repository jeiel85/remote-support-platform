using RemoteSupport.Domain;

namespace RemoteSupport.Application;

public enum SessionPeerRole
{
    Host,
    Operator,
}

public sealed class DataFeatureException(string code, string message) : Exception(message)
{
    public ErrorCode Code { get; } = new(code);
}

public sealed record PermissionSnapshot(ulong Revision, ScopeSet ActiveScopes);

public sealed class SessionPermissionGate
{
    private PermissionSnapshot current;

    public SessionPermissionGate(ulong revision, ScopeSet activeScopes)
    {
        ArgumentOutOfRangeException.ThrowIfZero(revision);
        current = new PermissionSnapshot(revision, activeScopes);
    }

    public PermissionSnapshot Current => Volatile.Read(ref current);

    public void Replace(ulong revision, ScopeSet activeScopes)
    {
        PermissionSnapshot prior = Current;
        if (revision <= prior.Revision)
        {
            throw new ArgumentOutOfRangeException(nameof(revision), "Permission revision must increase.");
        }
        Interlocked.Exchange(ref current, new PermissionSnapshot(revision, activeScopes));
    }

    public void RestrictCurrent(ScopeSet activeScopes)
    {
        PermissionSnapshot prior = Current;
        if (activeScopes.Values.Except(prior.ActiveScopes.Values).Any())
        {
            throw new ArgumentException("A local restriction cannot add session scopes.", nameof(activeScopes));
        }
        Interlocked.Exchange(ref current, new PermissionSnapshot(prior.Revision, activeScopes));
    }

    public void Demand(CapabilityScope scope, ulong messageRevision, string deniedCode)
    {
        PermissionSnapshot snapshot = Current;
        if (messageRevision != snapshot.Revision)
        {
            throw new DataFeatureException("SESSION_PERMISSION_REVISION_STALE", "Permission revision is stale.");
        }
        if (!snapshot.ActiveScopes.Contains(scope))
        {
            throw new DataFeatureException(deniedCode, "The active session scope does not permit this operation.");
        }
    }

    public static CapabilityScope ClipboardScope(SessionPeerRole source) => source == SessionPeerRole.Host
        ? CapabilityScope.SyncClipboardTextHostToOperator
        : CapabilityScope.SyncClipboardTextOperatorToHost;

    public static CapabilityScope FileScope(SessionPeerRole source) => source == SessionPeerRole.Host
        ? CapabilityScope.TransferFileHostToOperator
        : CapabilityScope.TransferFileOperatorToHost;
}
