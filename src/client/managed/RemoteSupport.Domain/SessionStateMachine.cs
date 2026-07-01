namespace RemoteSupport.Domain;

public enum SessionState
{
    Created,
    WaitingForOperator,
    ConsentPending,
    HostPending,
    Authorized,
    Negotiating,
    Connected,
    Reconnecting,
    Expired,
    Rejected,
    Failed,
    Cancelled,
    Ended,
}

public static class SessionStateMachine
{
    private static readonly Dictionary<SessionState, ScopeSetForStates> DirectTransitions =
        new Dictionary<SessionState, ScopeSetForStates>
        {
            [SessionState.Created] = ScopeSetForStates.Of(SessionState.WaitingForOperator, SessionState.HostPending),
            [SessionState.WaitingForOperator] = ScopeSetForStates.Of(SessionState.ConsentPending, SessionState.Expired),
            [SessionState.ConsentPending] = ScopeSetForStates.Of(SessionState.Authorized, SessionState.Rejected, SessionState.Expired),
            [SessionState.HostPending] = ScopeSetForStates.Of(SessionState.ConsentPending, SessionState.Authorized, SessionState.Expired),
            [SessionState.Authorized] = ScopeSetForStates.Of(SessionState.Negotiating),
            [SessionState.Negotiating] = ScopeSetForStates.Of(SessionState.Connected),
            [SessionState.Connected] = ScopeSetForStates.Of(SessionState.Reconnecting, SessionState.Ended),
            [SessionState.Reconnecting] = ScopeSetForStates.Of(SessionState.Negotiating, SessionState.Ended),
        };

    public static bool IsTerminal(SessionState state) => state is
        SessionState.Expired or SessionState.Rejected or SessionState.Failed or SessionState.Cancelled or SessionState.Ended;

    public static bool CanTransition(SessionState current, SessionState next)
    {
        if (!Enum.IsDefined(current) || !Enum.IsDefined(next) || IsTerminal(current) || current == next)
        {
            return false;
        }
        if (next is SessionState.Failed or SessionState.Cancelled)
        {
            return true;
        }
        return DirectTransitions.TryGetValue(current, out ScopeSetForStates allowed) && allowed.Contains(next);
    }

    public static void RequireTransition(SessionState current, SessionState next)
    {
        if (!CanTransition(current, next))
        {
            throw new InvalidOperationException($"Session transition {current} -> {next} is not allowed.");
        }
    }

    private readonly record struct ScopeSetForStates(ulong Bits)
    {
        public static ScopeSetForStates Of(params ReadOnlySpan<SessionState> states)
        {
            ulong bits = 0;
            foreach (SessionState state in states)
            {
                bits |= 1UL << (int)state;
            }
            return new ScopeSetForStates(bits);
        }

        public bool Contains(SessionState state) => (Bits & (1UL << (int)state)) != 0;
    }
}
