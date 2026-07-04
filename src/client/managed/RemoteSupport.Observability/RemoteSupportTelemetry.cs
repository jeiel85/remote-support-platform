using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;

namespace RemoteSupport.Observability;

public static class RemoteSupportTelemetry
{
    public const string SourceName = "RemoteSupport";
    public static readonly ActivitySource Activities = new(SourceName, "1.0.0");
    public static readonly Meter Metrics = new(SourceName, "1.0.0");
    private static readonly Counter<long> Operations = Metrics.CreateCounter<long>("rsp.operations", "{operation}");
    private static readonly Histogram<double> Duration = Metrics.CreateHistogram<double>("rsp.operation.duration", "ms");
    private static readonly Counter<long> Updates = Metrics.CreateCounter<long>("rsp.update", "{installation}");
    private static readonly Counter<long> ClientSessions = Metrics.CreateCounter<long>("rsp.client.session", "{session}");
    private static readonly Counter<long> ClientCrashes = Metrics.CreateCounter<long>("rsp.client.crash", "{crash}");
    private static readonly HashSet<string> AllowedComponents = new(StringComparer.Ordinal)
    { "agent", "operator", "updater", "control-api", "signaling", "turn", "audit" };

    public static Activity? Start(string component, string operation, string? correlationId = null)
    {
        Validate(component, operation, "started", null);
        Activity? activity = Activities.StartActivity(operation, ActivityKind.Internal);
        activity?.SetTag("rsp.component", component);
        if (!string.IsNullOrWhiteSpace(correlationId)) activity?.SetTag("rsp.correlation_id", SafeCorrelation(correlationId));
        return activity;
    }

    public static void Record(string component, string operation, string outcome, TimeSpan elapsed,
        string? stableErrorCode = null)
    {
        Validate(component, operation, outcome, stableErrorCode);
        TagList tags = new()
        {
            { "rsp.component", component },
            { "rsp.operation", operation },
            { "rsp.outcome", outcome },
        };
        if (stableErrorCode is not null) tags.Add("rsp.error_code", stableErrorCode);
        Operations.Add(1, tags);
        Duration.Record(elapsed.TotalMilliseconds, tags);
    }

    public static string StructuredLog(string component, int eventId, string outcome, string? stableErrorCode,
        string? correlationId)
    {
        Validate(component, "structured-log", outcome, stableErrorCode);
        return JsonSerializer.Serialize(new
        {
            timestamp = DateTimeOffset.UtcNow,
            eventId,
            component,
            outcome,
            stableErrorCode,
            correlationId = correlationId is null ? null : SafeCorrelation(correlationId),
        });
    }

    public static void RecordUpdate(string channel, string outcome, string? stableErrorCode = null)
    {
        if (channel is not ("internal" or "canary" or "stable") || !Bounded(outcome, 32) ||
            (stableErrorCode is not null && !Bounded(stableErrorCode, 96)))
            throw new ArgumentException("Update telemetry dimensions are invalid.");
        TagList tags = new() { { "channel", channel }, { "outcome", outcome } };
        if (stableErrorCode is not null) tags.Add("error_code", stableErrorCode);
        Updates.Add(1, tags);
    }

    public static void RecordClientStart(string component, bool recoveredFromCrash)
    {
        if (component is not ("agent" or "operator")) throw new ArgumentException("Client component is invalid.");
        TagList tags = new() { { "component", component } };
        ClientSessions.Add(1, tags);
        if (recoveredFromCrash) ClientCrashes.Add(1, tags);
    }

    private static void Validate(string component, string operation, string outcome, string? stableErrorCode)
    {
        if (!AllowedComponents.Contains(component) || !Bounded(operation, 64) || !Bounded(outcome, 32) ||
            (stableErrorCode is not null && !Bounded(stableErrorCode, 96)))
            throw new ArgumentException("Telemetry dimensions must use the bounded allowlisted schema.");
    }

    private static bool Bounded(string value, int maximum) => value.Length is > 0 && value.Length <= maximum &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private static string SafeCorrelation(string value) => Bounded(value, 128) ? value :
        throw new ArgumentException("Correlation ID is invalid.");
}
