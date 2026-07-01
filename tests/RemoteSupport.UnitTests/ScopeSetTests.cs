using RemoteSupport.Domain;

namespace RemoteSupport.UnitTests;

public sealed class ScopeSetTests
{
    [Fact]
    public void GrantedScopesMustBeASubsetOfRequestedScopes()
    {
        ScopeSet requested = ScopeSet.From(CapabilityScope.ViewScreen, CapabilityScope.ControlPointer);
        ScopeSet granted = ScopeSet.From(CapabilityScope.ViewScreen);

        Assert.True(granted.IsSubsetOf(requested));
        Assert.Equal(granted, granted.GrantSubset(requested));
    }

    [Fact]
    public void ScopeEscalationIsRejected()
    {
        ScopeSet requested = ScopeSet.From(CapabilityScope.ViewScreen);
        ScopeSet granted = ScopeSet.From(CapabilityScope.ViewScreen, CapabilityScope.ControlKeyboard);

        Assert.Throws<InvalidOperationException>(() => granted.GrantSubset(requested));
    }

    [Fact]
    public void EmptyGrantIsAValidSubset()
    {
        Assert.True(ScopeSet.Empty.IsSubsetOf(ScopeSet.From(CapabilityScope.ViewScreen)));
    }
}
