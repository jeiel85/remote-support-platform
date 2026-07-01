using RemoteSupport.Media;

namespace RemoteSupport.UnitTests;

public sealed class ViewportMapperTests
{
    [Fact]
    public void FitMapsNegativeOriginCornersAndRejectsLetterbox()
    {
        ViewportTransform transform = new(ViewportMode.Fit);
        Assert.True(ViewportMapper.TryMapToDesktop(new PhysicalPoint(0, 100), 1920, 1280,
            1920, 1080, -1920, -200, transform, out DesktopPoint topLeft));
        Assert.Equal(new DesktopPoint(-1920, -200), topLeft);

        Assert.True(ViewportMapper.TryMapToDesktop(new PhysicalPoint(1919.999, 1179.999), 1920, 1280,
            1920, 1080, -1920, -200, transform, out DesktopPoint bottomRight));
        Assert.Equal(new DesktopPoint(-1, 879), bottomRight);
        Assert.False(ViewportMapper.TryMapToDesktop(new PhysicalPoint(960, 99.999), 1920, 1280,
            1920, 1080, -1920, -200, transform, out _));
    }

    [Fact]
    public void PortraitRotationUsesNormalizedPhysicalFrameDimensions()
    {
        Assert.True(ViewportMapper.TryMapToDesktop(new PhysicalPoint(540, 960), 1080, 1920,
            1080, 1920, 1920, -420, new ViewportTransform(ViewportMode.Fit), out DesktopPoint center));
        Assert.Equal(new DesktopPoint(2460, 540), center);
    }

    [Fact]
    public void StretchZoomAndPanExactlyInvertRendererGeometry()
    {
        ViewportTransform transform = new(ViewportMode.Stretch, Zoom: 2, PanSourceX: 10, PanSourceY: -5);
        Assert.True(ViewportMapper.TryMapToDesktop(new PhysicalPoint(480, 270), 960, 540,
            1920, 1080, 100, 200, transform, out DesktopPoint point));
        Assert.Equal(new DesktopPoint(1070, 735), point);
    }

    [Fact]
    public void LogicalCoordinatesRequireExplicitDpiConversion()
    {
        PhysicalPoint physical = ViewportMapper.LogicalToPhysical(400, 300, 1.5, 2.0);
        Assert.Equal(new PhysicalPoint(600, 600), physical);
        Assert.Throws<ArgumentOutOfRangeException>(() => ViewportMapper.LogicalToPhysical(1, 1, 0, 1));
    }
}
