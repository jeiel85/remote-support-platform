using RemoteSupport.Application;

namespace RemoteSupport.UnitTests;

public sealed class ConnectionDiagnosticsTests
{
    [Theory]
    [InlineData(TransportRoute.DirectUdp, "Direct · UDP", false)]
    [InlineData(TransportRoute.TurnUdp, "Relay · UDP", true)]
    [InlineData(TransportRoute.TurnTcp, "Relay · TCP", true)]
    [InlineData(TransportRoute.TurnTls, "Relay · TLS", true)]
    public void PresentsPrivacySafeRouteWithoutCandidateDetails(TransportRoute route, string label, bool relayed)
    {
        ConnectionDiagnosticView view = ConnectionDiagnostics.Present(new ConnectionDiagnosticSample(
            ConnectionState.Connected, route, 42, 3, 12_500, 100, 200));

        Assert.Equal(label, view.Route);
        Assert.Equal(relayed, view.IsRelayed);
        Assert.Equal("RTT 42 ms · jitter 3 ms · loss 1.25%", view.Quality);
        Assert.DoesNotContain("candidate", view.Route, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsImpossibleLossValue()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ConnectionDiagnostics.Present(
            new ConnectionDiagnosticSample(ConnectionState.Connected, TransportRoute.DirectUdp,
                1, 1, 1_000_001, 0, 0)));
    }
}
