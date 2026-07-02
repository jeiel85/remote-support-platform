using System.Security.Claims;

namespace RemoteSupport.Server;

internal sealed class ResolveAbuseGuard(ControlPlaneOptions options, ISystemClock clock)
{
    private readonly object gate = new();
    private readonly Dictionary<string, Counter> counters = new(StringComparer.Ordinal);

    public bool TryAcquire(HttpContext context, string? supportCode)
    {
        string subject = context.User.FindFirstValue("sub") ?? "anonymous";
        string tenant = context.User.FindFirstValue("tenant_id") ?? "none";
        string edge = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        string canonical = (supportCode ?? string.Empty).Trim().ToUpperInvariant().Replace("-", string.Empty, StringComparison.Ordinal);
        string prefix = canonical.Length >= 2 ? canonical[..2] : "invalid";
        (string Key, int Limit)[] dimensions =
        [
            ($"subject:{subject}", options.ResolveRequestsPerMinute),
            ($"tenant:{tenant}", checked(options.ResolveRequestsPerMinute * 10)),
            ($"edge:{edge}", checked(options.ResolveRequestsPerMinute * 5)),
            ($"prefix:{prefix}", checked(options.ResolveRequestsPerMinute * 20)),
            ("global", checked(options.ResolveRequestsPerMinute * 100)),
        ];
        DateTimeOffset now = clock.UtcNow;
        lock (gate)
        {
            foreach ((string key, int limit) in dimensions)
            {
                if (counters.TryGetValue(key, out Counter? counter) && counter.WindowEndsAt > now && counter.Count >= limit)
                    return false;
            }
            foreach ((string key, _) in dimensions)
            {
                if (!counters.TryGetValue(key, out Counter? counter) || counter.WindowEndsAt <= now)
                    counters[key] = new Counter(now + TimeSpan.FromMinutes(1), 1);
                else
                    counter.Count++;
            }
            if (counters.Count > 50_000)
            {
                foreach (string key in counters.Where(pair => pair.Value.WindowEndsAt <= now).Select(pair => pair.Key).ToArray())
                    counters.Remove(key);
            }
            return true;
        }
    }

    private sealed class Counter(DateTimeOffset windowEndsAt, int count)
    {
        public DateTimeOffset WindowEndsAt { get; } = windowEndsAt;
        public int Count { get; set; } = count;
    }
}
