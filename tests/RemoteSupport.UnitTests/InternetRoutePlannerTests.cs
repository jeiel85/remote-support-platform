using RemoteSupport.Application;

namespace RemoteSupport.UnitTests;

public sealed class InternetRoutePlannerTests
{
    [Fact]
    public void OrdersDirectThenUdpTcpAndTlsWithOneEndpointPerAttempt()
    {
        IReadOnlyList<InternetRouteAttempt> attempts = InternetRoutePlanner.Plan(
        [
            new("turns:turn.example:5349?transport=tcp", "user", "credential-credential"),
            new("turn:turn.example:3478?transport=udp", "user", "credential-credential"),
            new("turn:turn.example:3478?transport=tcp", "user", "credential-credential"),
        ]);

        Assert.Equal([InternetRouteKind.DirectOrTurnUdp,
            InternetRouteKind.TurnTcp, InternetRouteKind.TurnTls], attempts.Select(attempt => attempt.Kind));
        Assert.False(attempts[0].RelayOnly);
        Assert.All(attempts.Skip(1), attempt => Assert.True(attempt.RelayOnly));
        Assert.All(attempts.Skip(1), attempt => Assert.NotNull(attempt.Turn));
    }

    [Fact]
    public void RejectsCredentialInUrlAndMissingFallback()
    {
        Assert.Throws<ArgumentException>(() => InternetRoutePlanner.Plan(
        [
            new("turn:user:password@turn.example:3478?transport=udp", "user", "credential-credential"),
            new("turn:turn.example:3478?transport=tcp", "user", "credential-credential"),
            new("turns:turn.example:5349?transport=tcp", "user", "credential-credential"),
        ]));
    }
}
