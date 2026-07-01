namespace RemoteSupport.Media;

public enum VideoQualityProfile
{
    Text,
    Balanced,
    Motion,
}

public sealed record AdaptationSample(
    uint EstimatedAvailableBitrateBps,
    double PacketLossRatio,
    double RoundTripTimeMs,
    double JitterMs,
    int EncoderQueueDepth,
    int EncoderQueueCapacity,
    double CaptureToSendLatencyMs,
    double CpuUtilization,
    double GpuUtilization,
    double DirtyAreaRatio,
    double MotionScore,
    int SourceWidth,
    int SourceHeight);

public sealed record EncoderTarget(
    uint BitrateBps,
    int FramesPerSecond,
    int Width,
    int Height,
    bool RequestKeyframe,
    bool EmergencyMode);

public interface IEncoderAdaptationSink
{
    void Apply(EncoderTarget target);
}

/// <summary>Maps transport, encoder and content pressure to a bounded low-latency encoder target.</summary>
public sealed class AdaptiveVideoController
{
    private const uint MinimumBitrate = 150_000;
    private readonly IEncoderAdaptationSink? sink;
    private EncoderTarget? current;
    private int healthySamples;

    public AdaptiveVideoController(VideoQualityProfile profile, IEncoderAdaptationSink? sink = null)
    {
        Profile = profile;
        this.sink = sink;
    }

    public VideoQualityProfile Profile { get; set; }

    public EncoderTarget Evaluate(AdaptationSample sample)
    {
        Validate(sample);
        var policy = Policy.For(Profile);
        var queueRatio = (double)sample.EncoderQueueDepth / sample.EncoderQueueCapacity;
        var resourcePressure = Math.Max(sample.CpuUtilization, sample.GpuUtilization);
        var emergency = sample.PacketLossRatio >= 0.12 || sample.RoundTripTimeMs >= 450 ||
            queueRatio >= 0.9 || sample.CaptureToSendLatencyMs >= 250 || resourcePressure >= 0.95;
        var constrained = emergency || sample.PacketLossRatio >= 0.05 || sample.RoundTripTimeMs >= 220 ||
            queueRatio >= 0.6 || sample.CaptureToSendLatencyMs >= 120 || resourcePressure >= 0.85;

        var lossFactor = sample.PacketLossRatio switch
        {
            >= 0.12 => 0.50,
            >= 0.05 => 0.68,
            >= 0.02 => 0.82,
            _ => 0.90,
        };
        var pressureFactor = emergency ? 0.62 : constrained ? 0.80 : 1.0;
        var targetBitrate = (uint)Math.Clamp(
            sample.EstimatedAvailableBitrateBps * lossFactor * pressureFactor,
            MinimumBitrate,
            policy.MaximumBitrate);

        var contentMotion = Math.Clamp(Math.Max(sample.MotionScore, sample.DirtyAreaRatio), 0, 1);
        var desiredFps = policy.MinimumFps + (int)Math.Round((policy.MaximumFps - policy.MinimumFps) * contentMotion);
        if (Profile == VideoQualityProfile.Text)
        {
            desiredFps = Math.Min(desiredFps, 24);
        }
        if (constrained)
        {
            desiredFps = Math.Max(policy.MinimumFps, (int)Math.Floor(desiredFps * (emergency ? 0.50 : 0.75)));
        }

        var pixels = (long)sample.SourceWidth * sample.SourceHeight;
        var bitsPerPixelFrame = Profile == VideoQualityProfile.Text ? 0.105 : Profile == VideoQualityProfile.Motion ? 0.072 : 0.085;
        var affordableScale = Math.Sqrt(targetBitrate / Math.Max(1.0, pixels * desiredFps * bitsPerPixelFrame));
        var desiredScale = Math.Clamp(affordableScale, policy.MinimumScale, 1.0);
        if (emergency)
        {
            desiredScale = Math.Min(desiredScale, 0.50);
        }
        else if (constrained)
        {
            desiredScale = Math.Min(desiredScale, 0.75);
        }

        var desired = BuildTarget(sample, targetBitrate, desiredFps, desiredScale, emergency);
        desired = ApplyRecoveryHysteresis(desired, constrained);
        sink?.Apply(desired);
        current = desired;
        return desired;
    }

    private EncoderTarget ApplyRecoveryHysteresis(EncoderTarget desired, bool constrained)
    {
        if (current is null || constrained || IsDownshift(desired, current))
        {
            healthySamples = 0;
            return desired with { RequestKeyframe = current is not null && DimensionsChanged(desired, current) };
        }

        healthySamples++;
        if (healthySamples < 3)
        {
            return current with { BitrateBps = Math.Min(desired.BitrateBps, current.BitrateBps), RequestKeyframe = false };
        }

        healthySamples = 0;
        return desired with { RequestKeyframe = DimensionsChanged(desired, current) };
    }

    private static bool IsDownshift(EncoderTarget desired, EncoderTarget previous) =>
        desired.BitrateBps < previous.BitrateBps || desired.FramesPerSecond < previous.FramesPerSecond ||
        desired.Width < previous.Width || desired.Height < previous.Height;

    private static bool DimensionsChanged(EncoderTarget left, EncoderTarget right) =>
        left.Width != right.Width || left.Height != right.Height;

    private static EncoderTarget BuildTarget(AdaptationSample sample, uint bitrate, int fps, double scale, bool emergency)
    {
        var width = Even((int)Math.Floor(sample.SourceWidth * scale));
        var height = Even((int)Math.Floor(sample.SourceHeight * scale));
        return new EncoderTarget(bitrate, fps, Math.Max(16, width), Math.Max(16, height), false, emergency);
    }

    private static int Even(int value) => value & ~1;

    private static void Validate(AdaptationSample sample)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sample.EstimatedAvailableBitrateBps, MinimumBitrate);
        ArgumentOutOfRangeException.ThrowIfNegative(sample.PacketLossRatio);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sample.PacketLossRatio, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(sample.RoundTripTimeMs);
        ArgumentOutOfRangeException.ThrowIfNegative(sample.JitterMs);
        ArgumentOutOfRangeException.ThrowIfLessThan(sample.EncoderQueueCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(sample.EncoderQueueDepth);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sample.EncoderQueueDepth, sample.EncoderQueueCapacity);
        ArgumentOutOfRangeException.ThrowIfNegative(sample.CaptureToSendLatencyMs);
        ArgumentOutOfRangeException.ThrowIfNegative(sample.CpuUtilization);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sample.CpuUtilization, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(sample.GpuUtilization);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sample.GpuUtilization, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sample.SourceWidth, 16);
        ArgumentOutOfRangeException.ThrowIfLessThan(sample.SourceHeight, 16);
    }

    private sealed record Policy(int MinimumFps, int MaximumFps, double MinimumScale, uint MaximumBitrate)
    {
        public static Policy For(VideoQualityProfile profile) => profile switch
        {
            VideoQualityProfile.Text => new(8, 24, 0.75, 8_000_000),
            VideoQualityProfile.Balanced => new(12, 45, 0.50, 12_000_000),
            VideoQualityProfile.Motion => new(18, 60, 0.50, 16_000_000),
            _ => throw new ArgumentOutOfRangeException(nameof(profile)),
        };
    }
}
