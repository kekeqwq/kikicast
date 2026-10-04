using Kikicast.Core;
namespace Kikicast.Core.Tests;

public class AdditionalWindowActionsTests
{
    private static readonly DisplayArea Host = new("owned", new(-1000, -500, 1200, 800));
    private static readonly Rect Window = new(-700, -300, 300, 200);
    [Fact]
    public void ThirtyTwoUniqueCatalogActionsKeepExistingNumericBindings()
    {
        Assert.Equal(32, WindowGeometry.Commands.Count);
        Assert.Equal(32, WindowGeometry.Commands.Select(x => x.Action).Distinct().Count());
        Assert.Equal(0, (int)WindowAction.LeftHalf); Assert.Equal(21, (int)WindowAction.Restore);
        Assert.All(Enum.GetValues<WindowAction>(), action => Assert.Contains(WindowGeometry.Commands, x => x.Action == action));
    }
    [Theory]
    [InlineData(WindowAction.FirstThreeFourths, -1000, 900)]
    [InlineData(WindowAction.LastThreeFourths, -700, 900)]
    [InlineData(WindowAction.CenterHalf, -700, 600)]
    [InlineData(WindowAction.CenterTwoThirds, -800, 800)]
    public void AdditionalTilesUseFractionalEdges(WindowAction action, double x, double width)
    {
        var result = WindowGeometry.Place(action, Window, Host, [Host])!.Value;
        Assert.Equal(new Rect(x, -500, width, 800), result); Assert.True(WindowGeometry.IsTile(action));
    }
    [Theory]
    [InlineData(WindowAction.MoveLeft, -760, -300)]
    [InlineData(WindowAction.MoveRight, -640, -300)]
    [InlineData(WindowAction.MoveUp, -700, -340)]
    [InlineData(WindowAction.MoveDown, -700, -260)]
    public void NudgeIsFivePercentOfCanvasAndDoesNotResize(WindowAction action, double x, double y)
    {
        Assert.Equal(new Rect(x, y, 300, 200), WindowGeometry.Place(action, Window, Host, [Host])!.Value);
        Assert.True(WindowGeometry.IsNudge(action)); Assert.False(WindowGeometry.IsTile(action));
        Assert.Equal("Moving", WindowGeometry.Group(action));
    }
    [Fact]
    public void MaximizeOneAxisPreservesOtherAxisAndClampsOffscreen()
    {
        Assert.Equal(new Rect(-700, -500, 300, 800), WindowGeometry.Place(WindowAction.MaximizeHeight, Window, Host, [Host])!.Value);
        Assert.Equal(new Rect(-1000, -300, 1200, 200), WindowGeometry.Place(WindowAction.MaximizeWidth, Window, Host, [Host])!.Value);
        var offscreen = Window with { X = 10000, Y = -10000 };
        var height = WindowGeometry.Place(WindowAction.MaximizeHeight, offscreen, Host, [Host])!.Value;
        Assert.Equal(Host.WorkArea.Right - 300, height.X); Assert.Equal(Host.WorkArea.Y, height.Y);
    }
    [Fact]
    public void AdditionalTilesUseSharedGapAndMinimumSizeAnchors()
    {
        var first = WindowGeometry.Place(WindowAction.FirstThreeFourths, Window, Host, [Host], 12)!.Value;
        Assert.Equal(-988, first.X); Assert.Equal(-488, first.Y); Assert.Equal(882, first.Width); Assert.Equal(776, first.Height);
        var centered = WindowGeometry.Place(WindowAction.CenterHalf, Window, Host, [Host])!.Value;
        Assert.Equal(new Rect(-800, -500, 800, 900), WindowGeometry.AnchorSize(WindowAction.CenterHalf, centered, 800, 900));
        var right = WindowGeometry.Place(WindowAction.LastThreeFourths, Window, Host, [Host])!.Value;
        Assert.Equal(right.Right, WindowGeometry.AnchorSize(WindowAction.LastThreeFourths, right, 1100, 800).Right);
    }
}
