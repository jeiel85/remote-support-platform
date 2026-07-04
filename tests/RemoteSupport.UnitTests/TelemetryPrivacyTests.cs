using RemoteSupport.Observability;

namespace RemoteSupport.UnitTests;

public sealed class TelemetryPrivacyTests
{
    [Fact]
    [Trait("Requirement", "AT-NFR-SEC-005")]
    public void StructuredTelemetryAndSupportSnapshotsRejectContentLikeDimensions()
    {
        string log = RemoteSupportTelemetry.StructuredLog("updater", 4100, "failure",
            "UPDATE_SIGNATURE_INVALID", "correlation-123");
        Assert.DoesNotContain("bearer-secret-canary", log, StringComparison.Ordinal);
        Assert.DoesNotContain("clipboard-secret-canary", log, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => RemoteSupportTelemetry.StructuredLog("updater", 4100, "failure",
            "bearer secret canary", "correlation-123"));

        SupportBundleSnapshot unsafeSnapshot = new("OPERATOR_CONSOLE", "0.9.0", "correlation-123", "CONNECTED",
            "TURN_TLS", 20, 0, "clipboard secret canary", DateTimeOffset.UnixEpoch);
        Assert.Throws<ArgumentException>(() => SupportBundleBuilder.Preview(unsafeSnapshot));
    }
}
