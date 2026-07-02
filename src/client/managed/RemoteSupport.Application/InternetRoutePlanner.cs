namespace RemoteSupport.Application;

public enum InternetRouteKind
{
    DirectOrTurnUdp,
    TurnTcp,
    TurnTls,
}

public sealed record TurnRouteEndpoint(string Url, string Username, string Credential);
public sealed record InternetRouteAttempt(InternetRouteKind Kind, TurnRouteEndpoint? Turn,
    bool RelayOnly);

public static class InternetRoutePlanner
{
    public static IReadOnlyList<InternetRouteAttempt> Plan(IEnumerable<TurnRouteEndpoint> endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        TurnRouteEndpoint[] values = endpoints.ToArray();
        if (values.Length is < 3 or > 16 || values.Any(endpoint =>
                string.IsNullOrWhiteSpace(endpoint.Username) || endpoint.Username.Length > 256 ||
                endpoint.Username.Any(character => character is < '!' or > '~') ||
                string.IsNullOrWhiteSpace(endpoint.Credential) || endpoint.Credential.Length > 512 ||
                endpoint.Credential.Any(character => character is < '!' or > '~')))
            throw new ArgumentException("Bounded TURN endpoints and credentials are required.", nameof(endpoints));
        (InternetRouteKind Kind, TurnRouteEndpoint Endpoint)[] routes = values
            .Select(endpoint => (Classify(endpoint.Url), endpoint)).ToArray();
        if (routes.Select(route => route.Endpoint.Url).Distinct(StringComparer.OrdinalIgnoreCase).Count() != routes.Length)
            throw new ArgumentException("Duplicate TURN endpoints are forbidden.", nameof(endpoints));
        if (!routes.Any(route => route.Kind == InternetRouteKind.DirectOrTurnUdp) ||
            !routes.Any(route => route.Kind == InternetRouteKind.TurnTcp) ||
            !routes.Any(route => route.Kind == InternetRouteKind.TurnTls))
            throw new ArgumentException("TURN UDP, TCP, and TLS endpoints are required.", nameof(endpoints));
        return routes.OrderBy(route => route.Kind).ThenBy(route => route.Endpoint.Url, StringComparer.OrdinalIgnoreCase)
            .Select(route => new InternetRouteAttempt(route.Kind, route.Endpoint,
                route.Kind != InternetRouteKind.DirectOrTurnUdp)).ToArray();
    }

    private static InternetRouteKind Classify(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 512 || url.Contains('@') ||
            url.Any(character => character is <= ' ' or > '~'))
            throw new ArgumentException("TURN URL was invalid.", nameof(url));
        if (url.StartsWith("turns:", StringComparison.OrdinalIgnoreCase) &&
            url.Contains("transport=tcp", StringComparison.OrdinalIgnoreCase)) return InternetRouteKind.TurnTls;
        if (url.StartsWith("turn:", StringComparison.OrdinalIgnoreCase) &&
            url.Contains("transport=tcp", StringComparison.OrdinalIgnoreCase)) return InternetRouteKind.TurnTcp;
        if (url.StartsWith("turn:", StringComparison.OrdinalIgnoreCase) &&
            url.Contains("transport=udp", StringComparison.OrdinalIgnoreCase)) return InternetRouteKind.DirectOrTurnUdp;
        throw new ArgumentException("Unsupported TURN route URL.", nameof(url));
    }
}
