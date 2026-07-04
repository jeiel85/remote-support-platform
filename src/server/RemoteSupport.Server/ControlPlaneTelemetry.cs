using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text;
using RemoteSupport.Observability;

namespace RemoteSupport.Server;

public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";
    public string? MetricsBearerToken { get; set; }

    public void Validate(bool allowEphemeral)
    {
        if (allowEphemeral && string.IsNullOrWhiteSpace(MetricsBearerToken))
            MetricsBearerToken = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        if (string.IsNullOrWhiteSpace(MetricsBearerToken) || MetricsBearerToken.Length is < 32 or > 512 ||
            MetricsBearerToken.Any(character => character is '\r' or '\n'))
            throw new InvalidOperationException("Observability:MetricsBearerToken must be a protected high-entropy secret.");
    }
}

public sealed class ControlPlaneTelemetry
{
    private readonly ConcurrentDictionary<RequestKey, long> requests = new();
    private readonly ConcurrentDictionary<RequestKey, long> failures = new();
    private readonly ConcurrentDictionary<string, long> securityEvents = new(StringComparer.Ordinal);
    private long auditBacklogSeconds;
    private static readonly Meter Meter = new(RemoteSupportTelemetry.SourceName + ".Server", "1.0.0");
    private static readonly Counter<long> RequestCounter = Meter.CreateCounter<long>("rsp.server.requests", "{request}");
    private static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>("rsp.server.request.duration", "ms");

    public IDisposable? StartRequest(HttpContext context)
    {
        Activity? activity = RemoteSupportTelemetry.Activities.StartActivity("control-api.request", ActivityKind.Server);
        activity?.SetTag("http.request.method", context.Request.Method);
        return activity;
    }

    public void RecordRequest(HttpContext context, TimeSpan elapsed)
    {
        string route = context.GetEndpoint() is RouteEndpoint endpoint ? endpoint.RoutePattern.RawText ?? "unmatched" : "unmatched";
        string statusClass = $"{context.Response.StatusCode / 100}xx";
        RequestKey key = new(context.Request.Method, route, statusClass);
        requests.AddOrUpdate(key, 1, static (_, value) => value + 1);
        if (context.Response.StatusCode >= 500) failures.AddOrUpdate(key, 1, static (_, value) => value + 1);
        TagList tags = new() { { "http.request.method", key.Method }, { "http.route", key.Route }, { "http.response.status_class", key.StatusClass } };
        RequestCounter.Add(1, tags);
        RequestDuration.Record(elapsed.TotalMilliseconds, tags);
        Activity.Current?.SetTag("http.route", route);
        Activity.Current?.SetTag("http.response.status_code", context.Response.StatusCode);
    }

    public void RecordSecurityEvent(string eventCode)
    {
        if (eventCode.Length is < 1 or > 96 || eventCode.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
            throw new ArgumentException("Security event code is not a bounded dimension.", nameof(eventCode));
        securityEvents.AddOrUpdate(eventCode, 1, static (_, value) => value + 1);
    }

    public void SetAuditBacklog(TimeSpan oldestAge) => Interlocked.Exchange(ref auditBacklogSeconds,
        Math.Max(0, checked((long)oldestAge.TotalSeconds)));

    public string RenderPrometheus()
    {
        StringBuilder output = new();
        output.AppendLine("# HELP rsp_control_api_requests_total Bounded control API request count.");
        output.AppendLine("# TYPE rsp_control_api_requests_total counter");
        foreach ((RequestKey key, long value) in requests.OrderBy(pair => pair.Key.Route, StringComparer.Ordinal)
                     .ThenBy(pair => pair.Key.Method, StringComparer.Ordinal).ThenBy(pair => pair.Key.StatusClass, StringComparer.Ordinal))
            output.Append("rsp_control_api_requests_total{method=\"").Append(Escape(key.Method)).Append("\",route=\"")
                .Append(Escape(key.Route)).Append("\",status_class=\"").Append(key.StatusClass).Append("\"} ")
                .AppendLine(value.ToString(CultureInfo.InvariantCulture));
        output.AppendLine("# HELP rsp_control_api_failures_total Control API 5xx request count.");
        output.AppendLine("# TYPE rsp_control_api_failures_total counter");
        foreach ((RequestKey key, long value) in failures.OrderBy(pair => pair.Key.Route, StringComparer.Ordinal))
            output.Append("rsp_control_api_failures_total{method=\"").Append(Escape(key.Method)).Append("\",route=\"")
                .Append(Escape(key.Route)).Append("\"} ").AppendLine(value.ToString(CultureInfo.InvariantCulture));
        output.AppendLine("# HELP rsp_audit_backlog_oldest_seconds Age of the oldest durable unprocessed audit event.");
        output.AppendLine("# TYPE rsp_audit_backlog_oldest_seconds gauge");
        output.Append("rsp_audit_backlog_oldest_seconds ").AppendLine(Interlocked.Read(ref auditBacklogSeconds).ToString(CultureInfo.InvariantCulture));
        output.AppendLine("# HELP rsp_security_events_total Bounded stable security event count.");
        output.AppendLine("# TYPE rsp_security_events_total counter");
        foreach ((string code, long value) in securityEvents.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            output.Append("rsp_security_events_total{event_code=\"").Append(code).Append("\"} ")
                .AppendLine(value.ToString(CultureInfo.InvariantCulture));
        return output.ToString();
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
    private sealed record RequestKey(string Method, string Route, string StatusClass);
}
