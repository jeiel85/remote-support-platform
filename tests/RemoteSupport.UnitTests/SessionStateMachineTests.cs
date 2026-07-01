using RemoteSupport.Domain;

namespace RemoteSupport.UnitTests;

public sealed class SessionStateMachineTests
{
    [Theory]
    [InlineData(SessionState.Created, SessionState.WaitingForOperator)]
    [InlineData(SessionState.Created, SessionState.HostPending)]
    [InlineData(SessionState.ConsentPending, SessionState.Authorized)]
    [InlineData(SessionState.HostPending, SessionState.Authorized)]
    [InlineData(SessionState.Connected, SessionState.Reconnecting)]
    [InlineData(SessionState.Reconnecting, SessionState.Negotiating)]
    public void DocumentedTransitionsAreAllowed(SessionState current, SessionState next)
    {
        Assert.True(SessionStateMachine.CanTransition(current, next));
    }

    [Theory]
    [InlineData(SessionState.Ended, SessionState.Connected)]
    [InlineData(SessionState.Rejected, SessionState.Authorized)]
    [InlineData(SessionState.Created, SessionState.Connected)]
    [InlineData(SessionState.Connected, SessionState.Connected)]
    public void InvalidOrTerminalTransitionsAreRejected(SessionState current, SessionState next)
    {
        Assert.False(SessionStateMachine.CanTransition(current, next));
    }

    [Fact]
    public void EveryNonterminalStateCanFailOrCancel()
    {
        foreach (SessionState state in Enum.GetValues<SessionState>().Where(state => !SessionStateMachine.IsTerminal(state)))
        {
            Assert.True(SessionStateMachine.CanTransition(state, SessionState.Failed));
            Assert.True(SessionStateMachine.CanTransition(state, SessionState.Cancelled));
        }
    }
}
