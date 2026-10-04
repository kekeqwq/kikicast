namespace Kikicast.Core;

// Forward and inverse share one physical-coordinate resolver. Authored offsets
// are DIP; fractional dimensions resolve against the selected work canvas.
public static class WindowLayoutGeometry
{
    public sealed record Capture(double WidthFraction, double HeightFraction, WindowSizeAnchor Anchor, double OffsetX, double OffsetY);
    public static double Scale(DisplayArea screen) => double.IsFinite(screen.Scale) && screen.Scale > 0 ? screen.Scale : 1;
    public static Rect Box(DisplayArea screen, double gapDip) => WindowGeometry.Canvas(screen.WorkArea, gapDip * Scale(screen));
    public static Rect? Resolve(WindowLayoutEntry entry, DisplayArea screen, double gapDip = 0)
    {
        if (entry.Validate() != null || !screen.WorkArea.IsValid) return null;
        var box = Box(screen, gapDip);
        var width = Math.Min(box.Width, Math.Max(1, box.Width * entry.WidthFraction));
        var height = Math.Min(box.Height, Math.Max(1, box.Height * entry.HeightFraction));
        var frame = Anchor(entry.Anchor, box, width, height);
        return WindowGeometry.Round(WindowGeometry.Clamp(frame with { X = frame.X + entry.OffsetX * Scale(screen), Y = frame.Y + entry.OffsetY * Scale(screen) }, box));
    }
    public static Capture? Describe(Rect frame, DisplayArea screen, double gapDip = 0)
    {
        if (!frame.IsValid || !screen.WorkArea.IsValid) return null;
        var box = Box(screen, gapDip);
        var width = Math.Min(frame.Width, box.Width); var height = Math.Min(frame.Height, box.Height);
        var column = Nearest(frame.X, box.X, box.Width - width); var row = Nearest(frame.Y, box.Y, box.Height - height);
        var anchor = (WindowSizeAnchor)(row * 3 + column); var origin = Anchor(anchor, box, width, height);
        return new(width / box.Width, height / box.Height, anchor, (frame.X - origin.X) / Scale(screen), (frame.Y - origin.Y) / Scale(screen));
    }
    public static WindowLayoutEntry? CaptureEntry(string applicationId, LayoutScreen screen, Rect frame)
    {
        var capture = Describe(frame, screen.Area); // deliberately gapless
        return capture == null ? null : new(Guid.NewGuid(), applicationId, screen.Display)
        { WidthFraction = capture.WidthFraction, HeightFraction = capture.HeightFraction, Anchor = capture.Anchor, OffsetX = capture.OffsetX, OffsetY = capture.OffsetY };
    }
    public static Rect Reanchor(Rect slot, double width, double height, WindowSizeAnchor anchor, Rect canvas)
    {
        var frame = Anchor(anchor, slot, width, height);
        // Retain application-enforced size: if it cannot fit, pin the leading edge.
        return WindowGeometry.Round(frame with { X = Math.Clamp(frame.X, canvas.X, Math.Max(canvas.X, canvas.Right - width)),
            Y = Math.Clamp(frame.Y, canvas.Y, Math.Max(canvas.Y, canvas.Bottom - height)) });
    }
    private static Rect Anchor(WindowSizeAnchor anchor, Rect box, double width, double height) => new(
        box.X + (box.Width - width) * ((int)anchor % 3) / 2, box.Y + (box.Height - height) * ((int)anchor / 3) / 2, width, height);
    private static int Nearest(double position, double start, double free)
    {
        var best = 1; var distance = Math.Abs(position - start - free / 2);
        foreach (var candidate in new[] { 0, 2 })
        { var next = Math.Abs(position - start - free * candidate / 2); if (next < distance) { distance = next; best = candidate; } }
        return best;
    }
}
