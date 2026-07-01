namespace RemoteSupport.Observability;

public enum TelemetryEventId
{
    RuntimeStarted = 1000,
    RuntimeStopped = 1001,
    CaptureStarted = 2000,
    CaptureRecovered = 2001,
    EncoderSelected = 3000,
    EncoderFallback = 3001,
}

public sealed record TelemetryEvent(TelemetryEventId EventId, string Component, IReadOnlyDictionary<string, object?> Properties);

