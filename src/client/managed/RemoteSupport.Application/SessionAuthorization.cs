using RemoteSupport.Domain;

namespace RemoteSupport.Application;

public static class SessionAuthorization
{
    public static ScopeSet Approve(ScopeSet requested, ScopeSet granted)
    {
        granted.GrantSubset(requested);
        return granted;
    }
}

