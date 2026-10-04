using Kikicast.Core;

namespace Kikicast.Core.Tests;

public class WindowGeometryTests
{
    private static readonly DisplayArea Primary = new("primary", new(0, 0, 1441, 900));
    private static readonly Rect Window = new(100, 150, 600, 400);

    [Fact]
    public void ThirdsShareRoundedEdges()
    {
        var a = Place(WindowAction.FirstThird); var b = Place(WindowAction.CenterThird); var c = Place(WindowAction.LastThird);
        Assert.Equal(a.Right, b.X); Assert.Equal(b.Right, c.X);
        Assert.Equal(1441, c.Right); Assert.Equal(1441, a.Width + b.Width + c.Width);
    }
    [Fact]
    public void GapsCompose()
    {
        var left = Place(WindowAction.LeftHalf, 16); var right = Place(WindowAction.RightHalf, 16);
        Assert.Equal(16, left.X); Assert.Equal(16, right.X - left.Right); Assert.Equal(1425, right.Right);
        Assert.Equal(16, left.Y); Assert.Equal(884, left.Bottom);
    }
    [Fact]
    public void SizeRoundTripAndCenterIdempotence()
    {
        var larger = WindowGeometry.Place(WindowAction.Larger, Window, Primary, [Primary])!.Value;
        var smaller = WindowGeometry.Place(WindowAction.Smaller, larger, Primary, [Primary])!.Value;
        Assert.Equal(Window, smaller);
        var center = Place(WindowAction.Center);
        Assert.Equal(center, WindowGeometry.Place(WindowAction.Center, center, Primary, [Primary]));
    }
    [Fact]
    public void NegativeOriginAndDisplayWrap()
    {
        var secondary = new DisplayArea("secondary", new(-1920, -200, 1920, 1080), 1.5);
        var next = WindowGeometry.Place(WindowAction.NextDisplay, Window, Primary, [Primary, secondary])!.Value;
        Assert.True(next.X >= secondary.WorkArea.X && next.Right <= secondary.WorkArea.Right);
        var back = WindowGeometry.Place(WindowAction.PreviousDisplay, next, secondary, [Primary, secondary])!.Value;
        Assert.InRange(Math.Abs(back.Width - Window.Width), 0, 1);
        Assert.Equal(Window, WindowGeometry.Place(WindowAction.NextDisplay, Window, Primary, [Primary]));
    }
    [Fact]
    public void InvalidInputsAndGapAreSafe()
    {
        Assert.Null(WindowGeometry.Place(WindowAction.LeftHalf, new(0, 0, double.NaN, 10), Primary, [Primary]));
        Assert.Null(WindowGeometry.Place(WindowAction.Restore, Window, Primary, [Primary]));
        Assert.Equal(Place(WindowAction.LeftHalf), Place(WindowAction.LeftHalf, double.NaN));
        Assert.Equal(Place(WindowAction.LeftHalf), Place(WindowAction.LeftHalf, -100));
        Assert.NotNull((new AppPreferences { WindowGap = double.PositiveInfinity }).Validate());
    }
    [Fact]
    public void AllFramesStayFiniteAndInsideScreen()
    {
        foreach (var action in Enum.GetValues<WindowAction>().Where(x => x != WindowAction.Restore))
        foreach (var gap in new[] { 0d, 16, 100, 1e6 })
        foreach (var area in new[] { new Rect(0, 0, 1441, 900), new Rect(-2000, -400, 800, 600) })
        {
            var screen = new DisplayArea("screen", area);
            var result = WindowGeometry.Place(action, new(area.X + 20, area.Y + 20, 300, 200), screen, [screen], gap)!.Value;
            Assert.True(result.IsValid);
            Assert.InRange(result.X, area.X, area.Right);
            Assert.InRange(result.Y, area.Y, area.Bottom);
            Assert.True(result.Right <= area.Right + 1 && result.Bottom <= area.Bottom + 1);
        }
    }
    private static Rect Place(WindowAction action, double gap = 0) => WindowGeometry.Place(action, Window, Primary, [Primary], gap)!.Value;
}
