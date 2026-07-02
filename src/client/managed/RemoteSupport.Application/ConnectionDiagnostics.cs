namespace RemoteSupport.Application;

public enum ConnectionState
{
    Connecting,
    Connected,
    Reconnecting,
    Disconnected,
}

public enum TransportRoute
{
    Unknown,
    DirectUdp,
    DirectTcp,
    TurnUdp,
    TurnTcp,
    TurnTls,
}

public sealed record ConnectionDiagnosticSample(ConnectionState State, TransportRoute Route,
    uint RoundTripMilliseconds, uint JitterMilliseconds, uint LossPartsPerMillion,
    ulong BytesSent, ulong BytesReceived);

public sealed record ConnectionDiagnosticView(string State, string Route, string Quality,
    bool IsRelayed);

public static class ConnectionDiagnostics
{
    public static ConnectionDiagnosticView Present(ConnectionDiagnosticSample sample)
    {
        if (!Enum.IsDefined(sample.State) || !Enum.IsDefined(sample.Route) ||
            sample.RoundTripMilliseconds > 120_000 || sample.JitterMilliseconds > 120_000 ||
            sample.LossPartsPerMillion > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(sample));
        string route = sample.Route switch
        {
            TransportRoute.DirectUdp => "Direct · UDP",
            TransportRoute.DirectTcp => "Direct · TCP",
            TransportRoute.TurnUdp => "Relay · UDP",
            TransportRoute.TurnTcp => "Relay · TCP",
            TransportRoute.TurnTls => "Relay · TLS",
            _ => "Route pending",
        };
        string quality = sample.State == ConnectionState.Connected
            ? $"RTT {sample.RoundTripMilliseconds} ms · jitter {sample.JitterMilliseconds} ms · loss {sample.LossPartsPerMillion / 10_000d:0.##}%"
            : "Quality pending";
        return new ConnectionDiagnosticView(sample.State.ToString(), route, quality,
            sample.Route is TransportRoute.TurnUdp or TransportRoute.TurnTcp or TransportRoute.TurnTls);
    }
}
