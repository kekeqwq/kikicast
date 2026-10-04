using Kikicast.Core;

namespace Kikicast.Core.Tests;

public class WindowBindingTests
{
    [Fact]
    public void ConflictIncludesPaletteFallbackAndDisabledWindowCommands()
    {
        var settings = new AppPreferences
        { WindowBindings = new() { [WindowAction.LeftHalf] = new() } };
        Assert.Contains("conflict", settings.Validate(), StringComparison.OrdinalIgnoreCase);
        settings = settings with { AltSpaceFallback = true, WindowBindings = new() { [WindowAction.LeftHalf] = new(BindingKind.Combo, 32, 1) } };
        Assert.NotNull(settings.Validate());
        settings = settings with { AltSpaceFallback = false };
        Assert.Null(settings.Validate());
        Assert.Single(BindingCatalog.Build(settings));
        Assert.Equal(2, BindingCatalog.Build(settings with { WindowManagementEnabled = true }).Count);
    }

    [Fact]
    public void TwoWindowCommandsCannotShareCombo()
    {
        var combo = new HotKeyBinding(BindingKind.Combo, 37, 3);
        var settings = new AppPreferences { WindowBindings = new()
        { [WindowAction.LeftHalf] = combo, [WindowAction.RightHalf] = combo } };
        Assert.NotNull(settings.Validate());
        settings.WindowBindings[WindowAction.RightHalf] = combo with { Key = 39 };
        Assert.Null(settings.Validate());
    }

    [Theory]
    [InlineData(KeySide.Any, KeySide.Left, true)]
    [InlineData(KeySide.Any, KeySide.Right, true)]
    [InlineData(KeySide.Left, KeySide.Right, false)]
    [InlineData(KeySide.Left, KeySide.Left, true)]
    public void DoubleTapSidesOverlap(KeySide a, KeySide b, bool expected) => Assert.Equal(expected,
        BindingCatalog.Overlaps(new(BindingKind.DoubleTap, 18, Side: a), new(BindingKind.DoubleTap, 18, Side: b)));

    [Fact]
    public void UnknownWindowActionRejectedBeforeCatalogBuild()
    {
        var settings = new AppPreferences { WindowBindings = new() { [(WindowAction)999] = new(BindingKind.Combo, 75, 2) } };
        Assert.NotNull(settings.Validate());
    }

    [Theory]
    [InlineData(WindowAction.LeftHalf)] [InlineData(WindowAction.RightHalf)]
    [InlineData(WindowAction.TopHalf)] [InlineData(WindowAction.BottomHalf)]
    public void SizeCycleWrapsAndResets(WindowAction action)
    {
        Assert.Equal(1, WindowCycle.NextStep(true, action, action, 0, "a", "a", true));
        Assert.Equal(2, WindowCycle.NextStep(true, action, action, 1, "a", "a", true));
        Assert.Equal(0, WindowCycle.NextStep(true, action, action, 2, "a", "a", true));
        Assert.Equal(0, WindowCycle.NextStep(false, action, action, 1, "a", "a", true));
        Assert.Equal(0, WindowCycle.NextStep(true, action, action, 1, "b", "a", true));
        Assert.Equal(0, WindowCycle.NextStep(true, action, action, 1, "a", "a", false));
        Assert.Equal(0, WindowCycle.NextStep(true, action, WindowAction.Center, 1, "a", "a", true));
    }

    [Theory]
    [InlineData(WindowAction.RightHalf, 200, 0)]
    [InlineData(WindowAction.BottomRight, 200, 100)]
    [InlineData(WindowAction.Center, 100, 50)]
    public void RefusedSizeKeepsCorrectAnchor(WindowAction action, double expectedX, double expectedY)
    {
        var slot = new Rect(0, 0, 500, 300);
        var placed = WindowGeometry.AnchorSize(action, slot, 300, 200);
        Assert.Equal(new Rect(expectedX, expectedY, 300, 200), placed);
    }

    [Theory]
    [InlineData(WindowAction.LeftHalf, 1, 100, 200, 300, 600)]
    [InlineData(WindowAction.RightHalf, 1, 700, 200, 300, 600)]
    [InlineData(WindowAction.TopHalf, 1, 100, 200, 900, 200)]
    [InlineData(WindowAction.BottomHalf, 1, 100, 600, 900, 200)]
    [InlineData(WindowAction.LeftHalf, 2, 100, 200, 600, 600)]
    [InlineData(WindowAction.RightHalf, 2, 400, 200, 600, 600)]
    [InlineData(WindowAction.TopHalf, 2, 100, 200, 900, 400)]
    [InlineData(WindowAction.BottomHalf, 2, 100, 400, 900, 400)]
    public void CycledGeometry(WindowAction action, int step, double x, double y, double w, double h)
    {
        var screen = new DisplayArea("screen", new(100, 200, 900, 600));
        Assert.Equal(new Rect(x, y, w, h), WindowGeometry.Place(action, new(150, 250, 300, 200), screen, [screen], step: step));
    }
}
