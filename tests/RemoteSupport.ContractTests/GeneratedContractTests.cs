using RemoteSupport.Contracts.Generated;

namespace RemoteSupport.ContractTests;

public sealed class GeneratedContractTests
{
    [Fact]
    [Trait("Requirement", "NFR-MNT-001")]
    public void OpenApiOperationsAreGeneratedOnce()
    {
        Assert.Equal(49, OpenApiContract.Operations.Count);
        Assert.Equal(49, OpenApiContract.Operations.Select(operation => operation.OperationId).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    [Trait("Requirement", "NFR-MNT-001")]
    public void ErrorCodesAreUniqueAndWellFormed()
    {
        Assert.NotEmpty(ErrorCodes.All);
        Assert.Equal(ErrorCodes.All.Count, ErrorCodes.All.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ErrorCodes.All, code => Assert.Matches("^[A-Z0-9_]{1,96}$", code));
    }
}
