using RemoteSupport.Application;

namespace RemoteSupport.UnitTests;

public sealed class ConsentPresentationTests
{
    [Fact]
    [Trait("Requirement", "FR-SES-004")]
    public void PresentsVerifiedIdentityAndEveryRequestedScopeWithoutCollapsingThem()
    {
        ConsentViewModel viewModel = new();
        VerifiedConsentRequest request = new(Guid.NewGuid(), Guid.NewGuid(), "Kim Operator", "Example Tenant", true,
            ["VIEW_SCREEN", "CONTROL_POINTER", "CONTROL_KEYBOARD"], DateTimeOffset.UtcNow.AddMinutes(5), 2);
        viewModel.Present(request);
        Assert.Equal("Kim Operator", viewModel.OperatorDisplayName);
        Assert.Equal("Example Tenant", viewModel.TenantDisplayName);
        Assert.Equal("Organization identity verified", viewModel.VerificationText);
        Assert.Equal(["View your screen", "Control the pointer", "Use the keyboard"], viewModel.RequestedScopeLabels);
        Assert.Same(request, viewModel.Request);
    }

    [Fact]
    public void RejectsUnverifiedTenantDuplicateOrUnknownScopes()
    {
        ConsentViewModel viewModel = new();
        DateTimeOffset expiry = DateTimeOffset.UtcNow.AddMinutes(5);
        Assert.Throws<ArgumentException>(() => viewModel.Present(new(Guid.NewGuid(), Guid.NewGuid(), "Operator", "Tenant", false,
            ["VIEW_SCREEN"], expiry, 2)));
        Assert.Throws<ArgumentException>(() => viewModel.Present(new(Guid.NewGuid(), Guid.NewGuid(), "Operator", "Tenant", true,
            ["VIEW_SCREEN", "VIEW_SCREEN"], expiry, 2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => viewModel.Present(new(Guid.NewGuid(), Guid.NewGuid(), "Operator", "Tenant", true,
            ["NOT_A_SCOPE"], expiry, 2)));
    }
}
