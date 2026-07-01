namespace RemoteSupport.Media;

public enum ViewportMode
{
    Fit,
    ActualSize,
    Stretch,
}

public readonly record struct PhysicalPoint(double X, double Y);

public readonly record struct DesktopPoint(int X, int Y);

public readonly record struct ViewportTransform(
    ViewportMode Mode,
    double Zoom = 1.0,
    double PanSourceX = 0.0,
    double PanSourceY = 0.0);

public static class ViewportMapper
{
    public static PhysicalPoint LogicalToPhysical(double logicalX, double logicalY, double dpiScaleX, double dpiScaleY)
    {
        if (!double.IsFinite(logicalX) || !double.IsFinite(logicalY) ||
            !double.IsFinite(dpiScaleX) || !double.IsFinite(dpiScaleY) || dpiScaleX <= 0 || dpiScaleY <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpiScaleX));
        }

        return new PhysicalPoint(logicalX * dpiScaleX, logicalY * dpiScaleY);
    }

    public static bool TryMapToDesktop(
        PhysicalPoint viewportPoint,
        int viewportWidth,
        int viewportHeight,
        int frameWidth,
        int frameHeight,
        int desktopOriginX,
        int desktopOriginY,
        ViewportTransform transform,
        out DesktopPoint desktopPoint)
    {
        desktopPoint = default;
        if (viewportWidth <= 0 || viewportHeight <= 0 || frameWidth <= 0 || frameHeight <= 0 ||
            !double.IsFinite(viewportPoint.X) || !double.IsFinite(viewportPoint.Y) ||
            !double.IsFinite(transform.Zoom) || !double.IsFinite(transform.PanSourceX) ||
            !double.IsFinite(transform.PanSourceY) || transform.Zoom < 0.25 || transform.Zoom > 8.0)
        {
            return false;
        }

        double scaleX = (double)viewportWidth / frameWidth;
        double scaleY = (double)viewportHeight / frameHeight;
        if (transform.Mode == ViewportMode.Fit)
        {
            scaleX = scaleY = Math.Min(scaleX, scaleY);
        }
        else if (transform.Mode == ViewportMode.ActualSize)
        {
            scaleX = scaleY = 1.0;
        }

        scaleX *= transform.Zoom;
        scaleY *= transform.Zoom;
        double renderedWidth = frameWidth * scaleX;
        double renderedHeight = frameHeight * scaleY;
        double left = (viewportWidth - renderedWidth) * 0.5 - transform.PanSourceX * scaleX;
        double top = (viewportHeight - renderedHeight) * 0.5 - transform.PanSourceY * scaleY;
        double sourceX = (viewportPoint.X - left) / scaleX;
        double sourceY = (viewportPoint.Y - top) / scaleY;
        if (sourceX < 0 || sourceY < 0 || sourceX >= frameWidth || sourceY >= frameHeight)
        {
            return false;
        }

        long desktopX = (long)desktopOriginX + Math.Min(frameWidth - 1, (int)Math.Floor(sourceX));
        long desktopY = (long)desktopOriginY + Math.Min(frameHeight - 1, (int)Math.Floor(sourceY));
        if (desktopX is < int.MinValue or > int.MaxValue || desktopY is < int.MinValue or > int.MaxValue)
        {
            return false;
        }

        desktopPoint = new DesktopPoint((int)desktopX, (int)desktopY);
        return true;
    }
}
