using RemoteSupport.Media;

namespace RemoteSupport.UnitTests;

public sealed class AdaptiveVideoControllerTests
{
    [Fact]
    [Trait("Requirement", "FR-SCR-005")]
    public void SeverePressureDownshiftsImmediatelyAndRecoversWithHysteresis()
    {
        var controller = new AdaptiveVideoController(VideoQualityProfile.Balanced);
        var healthy = Sample(bitrate: 8_000_000, motion: 0.8);
        var initial = controller.Evaluate(healthy);
        var impaired = controller.Evaluate(Sample(
            bitrate: 900_000, loss: 0.15, rtt: 500, queueDepth: 3, latency: 300, cpu: 0.97, motion: 0.8));

        Assert.True(impaired.EmergencyMode);
        Assert.True(impaired.BitrateBps < initial.BitrateBps);
        Assert.True(impaired.FramesPerSecond < initial.FramesPerSecond);
        Assert.True(impaired.Width <= 960);
        Assert.True(impaired.RequestKeyframe);

        var recovery1 = controller.Evaluate(healthy);
        var recovery2 = controller.Evaluate(healthy);
        var recovery3 = controller.Evaluate(healthy);
        Assert.Equal(impaired.Width, recovery1.Width);
        Assert.Equal(impaired.Width, recovery2.Width);
        Assert.True(recovery3.Width > impaired.Width);
        Assert.True(recovery3.RequestKeyframe);
    }

    [Fact]
    [Trait("Requirement", "FR-SCR-006")]
    public void ProfilesProduceDistinctTargetsAtEqualBandwidth()
    {
        var sample = Sample(bitrate: 5_000_000, motion: 0.9);
        var text = new AdaptiveVideoController(VideoQualityProfile.Text).Evaluate(sample);
        var balanced = new AdaptiveVideoController(VideoQualityProfile.Balanced).Evaluate(sample);
        var motion = new AdaptiveVideoController(VideoQualityProfile.Motion).Evaluate(sample);

        Assert.True(text.FramesPerSecond < balanced.FramesPerSecond);
        Assert.True(balanced.FramesPerSecond < motion.FramesPerSecond);
        Assert.True(text.Width >= balanced.Width);
        Assert.InRange(text.FramesPerSecond, 8, 24);
        Assert.InRange(motion.FramesPerSecond, 18, 60);
    }

    [Fact]
    [Trait("Requirement", "FR-SCR-005")]
    public void LatestFrameQueueRemainsBoundedAndDropsOldestFrames()
    {
        var queue = new BoundedLatestFrameBuffer<int>(3);
        for (var frame = 1; frame <= 7; frame++)
        {
            queue.Enqueue(frame);
        }

        Assert.Equal(3, queue.Count);
        Assert.Equal(4, queue.DroppedFrames);
        Assert.True(queue.TryDequeue(out var first));
        Assert.Equal(5, first);
    }

    [Fact]
    [Trait("Requirement", "FR-SCR-005")]
    public void BandwidthLossAndResourceSweepStaysInsideSafeBounds()
    {
        uint[] bandwidths = [500_000, 1_000_000, 2_000_000, 5_000_000, 10_000_000];
        var widths = bandwidths
            .Select(value => new AdaptiveVideoController(VideoQualityProfile.Balanced).Evaluate(Sample(value, motion: 0.7)).Width)
            .ToArray();

        Assert.True(widths.SequenceEqual(widths.OrderBy(value => value)));
        foreach (var bandwidth in bandwidths)
        {
            var target = new AdaptiveVideoController(VideoQualityProfile.Balanced).Evaluate(Sample(bandwidth, motion: 0.7));
            Assert.InRange(target.BitrateBps, 150_000u, 12_000_000u);
            Assert.InRange(target.FramesPerSecond, 12, 45);
            Assert.InRange(target.Width, 960, 1920);
            Assert.Equal(0, target.Width % 2);
            Assert.Equal(0, target.Height % 2);
        }

        var clean = new AdaptiveVideoController(VideoQualityProfile.Balanced).Evaluate(Sample(4_000_000, motion: 0.7));
        var lossy = new AdaptiveVideoController(VideoQualityProfile.Balanced).Evaluate(
            Sample(4_000_000, loss: 0.08, rtt: 280, queueDepth: 2, latency: 150, cpu: 0.9, motion: 0.7));
        Assert.True(lossy.BitrateBps < clean.BitrateBps);
        Assert.True(lossy.FramesPerSecond < clean.FramesPerSecond);
        Assert.True(lossy.Width <= clean.Width);
    }

    private static AdaptationSample Sample(
        uint bitrate,
        double loss = 0,
        double rtt = 35,
        int queueDepth = 0,
        double latency = 25,
        double cpu = 0.3,
        double motion = 0.5) =>
        new(bitrate, loss, rtt, 3, queueDepth, 3, latency, cpu, 0.25, motion, motion, 1920, 1080);
}
