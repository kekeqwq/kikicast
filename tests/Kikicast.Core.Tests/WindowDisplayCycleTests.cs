using System.Text.Json;
using Kikicast.Core;

namespace Kikicast.Core.Tests;

public sealed class WindowDisplayCycleTests
{
    private static readonly DisplayArea a = new("a", new(-1200, 40, 1200, 860), 1, new(-1200, 0, 1200, 900));
    private static readonly DisplayArea b = new("b", new(0, -700, 2000, 1400), 2, new(0, -740, 2000, 1440));
    private static readonly Rect window = new(-1000, 120, 600, 400);
    [Fact]
    public void DefaultsAndLegacyBooleanRemainReadableButExplicitModeWins()
    {
        foreach (var legacy in new[] { "{}", "{\"CycleHalfSizes\":false}", "{\"CycleHalfSizes\":true}" })
        {
            var p = JsonSerializer.Deserialize<AppPreferences>(legacy)!; Assert.Null(p.Validate()); Assert.Null(p.HalfCycleMode);
            Assert.Equal(p.CycleHalfSizes ? WindowCycleMode.Sizes : WindowCycleMode.Off, WindowCycle.Resolve(p));
        }
        foreach (var mode in Enum.GetValues<WindowCycleMode>())
        {
            var p = new AppPreferences { CycleHalfSizes = true, HalfCycleMode = mode };
            Assert.Equal(mode, WindowCycle.Resolve(p)); Assert.Null(p.Validate());
            var copy = JsonSerializer.Deserialize<AppPreferences>(JsonSerializer.Serialize(p))!; Assert.Equal(mode, WindowCycle.Resolve(copy)); Assert.Equal(p.WindowBindings, copy.WindowBindings);
        }
        Assert.NotNull((new AppPreferences { HalfCycleMode = (WindowCycleMode)77 }).Validate());
    }
    [Theory]
    [InlineData(WindowAction.LeftHalf, WindowAction.RightHalf, WindowAction.LeftHalf, WindowAction.RightHalf)]
    [InlineData(WindowAction.RightHalf, WindowAction.LeftHalf, WindowAction.RightHalf, WindowAction.LeftHalf)]
    [InlineData(WindowAction.TopHalf, WindowAction.BottomHalf, WindowAction.TopHalf, WindowAction.BottomHalf)]
    [InlineData(WindowAction.BottomHalf, WindowAction.TopHalf, WindowAction.BottomHalf, WindowAction.TopHalf)]
    public void TwoDisplayTourUsesTheOriginNotTheNewHostAndRetainsActualTileEdge(WindowAction action, WindowAction step1, WindowAction step2, WindowAction step3)
    {
        var edges = new[] { action, step1, step2, step3, action };
        var displayIds = new[] { "a", "b", "b", "a", "a" };
        WindowCycleState? previous = null; var host = a; var frame = window;
        for (var i = 0; i < edges.Length; i++)
        {
            var decision = WindowCycle.Decide(WindowCycleMode.Displays, action, previous, host, [b, a], true);
            Assert.Equal(i % 4, decision.Step); Assert.Equal("a", decision.OriginDisplay);
            var plan = WindowGeometry.Resolve(action, frame, host, [b, a], 10, decision)!;
            Assert.Equal(displayIds[i], plan.Display.Id); Assert.Equal(edges[i], plan.AnchorAction); Assert.Equal(edges[i], plan.Tile); Assert.Equal(0, plan.TileStep);
            Assert.Equal(WindowGeometry.Place(edges[i], frame, plan.Display, [b, a], 10 * plan.Display.Scale), plan.Frame);
            previous = WindowCycle.Remember(action, decision, plan.Display.Id, [b, a]); host = plan.Display; frame = plan.Frame;
        }
    }
    [Theory]
    [InlineData(WindowAction.LeftHalf)] [InlineData(WindowAction.RightHalf)]
    [InlineData(WindowAction.TopHalf)] [InlineData(WindowAction.BottomHalf)]
    public void ThreeDisplayStripVisitsEverySlotExactlyOnceWithPermutationInvariantDpiGaps(WindowAction action)
    {
        var c = new DisplayArea("c", new(2000, 300, 1441, 1050), 1.5, new(2000, 300, 1441, 1080));
        DisplayArea[][] permutations = [[a, b, c], [c, a, b], [b, c, a]];
        List<(string, WindowAction, Rect)>? baseline = null;
        foreach (var screens in permutations)
        {
            var seen = new HashSet<(string, WindowAction)>(); var placements = new List<(string, WindowAction, Rect)>();
            WindowCycleState? previous = null; var host = b;
            for (var i = 0; i < 6; i++)
            {
                var decision = WindowCycle.Decide(WindowCycleMode.Displays, action, previous, host, screens, true);
                var plan = WindowGeometry.Resolve(action, window, host, screens, 13, decision)!;
                Assert.True(seen.Add((plan.Display.Id, plan.AnchorAction))); Assert.Equal("b", decision.OriginDisplay);
                placements.Add((plan.Display.Id, plan.AnchorAction, plan.Frame));
                previous = WindowCycle.Remember(action, decision, plan.Display.Id, screens); host = plan.Display;
            }
            if (baseline == null) baseline = placements; else Assert.Equal(baseline, placements);
            Assert.Equal(0, WindowCycle.Decide(WindowCycleMode.Displays, action, previous, host, screens, true).Step);
        }
    }
    [Theory]
    [InlineData(WindowAction.LeftHalf)] [InlineData(WindowAction.RightHalf)]
    [InlineData(WindowAction.TopHalf)] [InlineData(WindowAction.BottomHalf)]
    public void SingleDisplayNeverFlipsEdgesAndModeOffIgnoresStaleSteps(WindowAction action)
    {
        foreach (var mode in new[] { WindowCycleMode.Off, WindowCycleMode.Displays })
        {
            var decision = new WindowCycleDecision(mode, 71, "missing");
            var plan = WindowGeometry.Resolve(action, window, a, [a], 8, decision)!;
            Assert.Equal(a, plan.Display); Assert.Equal(action, plan.AnchorAction);
            Assert.Equal(WindowGeometry.Place(action, window, a, [a], 8), plan.Frame);
            Assert.Equal(1, WindowCycle.Length(mode, action, 1));
        }
        Assert.Null(WindowGeometry.Resolve(WindowAction.NextDisplay, window, a, [a]));
    }
    [Fact]
    public void ModeCommandDriftHostAndTopologyChangesResetTheChainWithoutGuessingOldOrigin()
    {
        var decision = new WindowCycleDecision(WindowCycleMode.Displays, 1, "a");
        var previous = WindowCycle.Remember(WindowAction.LeftHalf, decision, "b", [a, b]);
        Assert.Equal(2, WindowCycle.Decide(WindowCycleMode.Displays, WindowAction.LeftHalf, previous, b, [b, a], true).Step);
        foreach (var result in new[]
        {
            WindowCycle.Decide(WindowCycleMode.Sizes, WindowAction.LeftHalf, previous, b, [a, b], true),
            WindowCycle.Decide(WindowCycleMode.Displays, WindowAction.RightHalf, previous, b, [a, b], true),
            WindowCycle.Decide(WindowCycleMode.Displays, WindowAction.LeftHalf, previous, b, [a, b], false),
            WindowCycle.Decide(WindowCycleMode.Displays, WindowAction.LeftHalf, previous, b, [b], true),
            WindowCycle.Decide(WindowCycleMode.Displays, WindowAction.LeftHalf, previous, b, [a with { WorkArea = a.WorkArea with { Width = 1190 } }, b], true),
            WindowCycle.Decide(WindowCycleMode.Displays, WindowAction.LeftHalf, previous, b, [a with { Scale = 1.5 }, b], true),
            WindowCycle.Decide(WindowCycleMode.Displays, WindowAction.LeftHalf, previous, b, [a with { Bounds = new(-1200, -20, 1200, 920) }, b], true)
        }) { Assert.Equal(0, result.Step); Assert.Equal("b", result.OriginDisplay); }
        var moved = WindowCycle.Decide(WindowCycleMode.Displays, WindowAction.LeftHalf, previous, a, [a, b], true);
        Assert.Equal(0, moved.Step); Assert.Equal("a", moved.OriginDisplay);
        var added = WindowCycle.Decide(WindowCycleMode.Displays, WindowAction.LeftHalf, previous, b, [a, b, new("c", new(2000, 0, 900, 800))], true);
        Assert.Equal(0, added.Step);
    }
    [Fact]
    public void PhysicalBoundsOrderIsNotChangedBySideTaskbarsAndMalformedSnapshotsRefusePlacement()
    {
        var left = new DisplayArea("left", new(100, 0, 900, 800), 1, new(0, 0, 1000, 800));
        var right = new DisplayArea("right", new(50, 800, 1000, 800), 1, new(50, 800, 1000, 800));
        Assert.Equal(new[] { "left", "right" }, WindowCycle.Ordered([right, left])!.Select(x => x.Id));
        foreach (var bad in new IReadOnlyList<DisplayArea>[] { [], [a, a], [a, b with { WorkArea = new(0, 0, 0, 80) }], [a, b with { Bounds = new(0, 0, double.NaN, 80) }],
            Enumerable.Range(0, 65).Select(i => new DisplayArea(i.ToString(), new(i * 100, 0, 100, 100))).ToArray() })
        { Assert.Null(WindowCycle.Ordered(bad)); Assert.Null(WindowGeometry.Resolve(WindowAction.LeftHalf, window, a, bad)); }
        Assert.Null(WindowGeometry.Resolve(WindowAction.LeftHalf, window, a with { Id = "missing" }, [a, b]));
        Assert.Null(WindowGeometry.Resolve(WindowAction.LeftHalf, window, a, [a, b], cycle: new((WindowCycleMode)44, 0, "a")));
    }
    [Fact]
    public void WrappedStepsAndMissingOriginsAreBoundedAndRememberTakesAnImmutableSnapshot()
    {
        var source = new List<DisplayArea> { b, a };
        var state = WindowCycle.Remember(WindowAction.LeftHalf, new(WindowCycleMode.Displays, 3, "a"), "a", source);
        source.Clear(); Assert.Equal(2, state.Screens.Count); Assert.Equal("a", state.Screens[0].Id);
        foreach (var step in new[] { int.MinValue, -1, 0, 1, 4, int.MaxValue })
        {
            var slot = WindowCycle.HalfSlot(WindowAction.RightHalf, a, [b, a], step, "missing")!;
            var normalized = WindowCycle.HalfSlot(WindowAction.RightHalf, a, [a, b], WindowCycle.Wrap(step, 4), "a")!;
            Assert.Equal(normalized, slot);
        }
        Assert.Null(WindowCycle.HalfSlot(WindowAction.Center, a, [a, b], 1, "a"));
    }
    [Fact]
    public void DisplayMovesKeepTheActualOppositeEdgeOrThirdAndFloatingMovesStayIndependent()
    {
        var cycled = WindowGeometry.Resolve(WindowAction.LeftHalf, window, a, [a, b], 10, new(WindowCycleMode.Displays, 1, "a"))!;
        Assert.Equal(WindowAction.RightHalf, cycled.Tile);
        var next = WindowGeometry.Resolve(WindowAction.PreviousDisplay, cycled.Frame, b, [a, b], 10, lastTile: cycled.Tile, lastTileStep: cycled.TileStep)!;
        Assert.Equal(a, next.Display); Assert.Equal(WindowAction.RightHalf, next.AnchorAction);
        Assert.Equal(WindowGeometry.Place(WindowAction.RightHalf, window, a, [a, b], 10), next.Frame);
        var third = WindowGeometry.Resolve(WindowAction.NextDisplay, window, a, [a, b], 10, lastTile: WindowAction.LeftHalf, lastTileStep: 1)!;
        Assert.Equal(WindowGeometry.Place(WindowAction.LeftHalf, window, b, [a, b], 20, 1), third.Frame); Assert.Equal(1, third.TileStep);
        var floating = WindowGeometry.Resolve(WindowAction.NextDisplay, window, a, [a, b], 10)!;
        Assert.Null(floating.Tile); Assert.Equal(WindowAction.Center, floating.AnchorAction);
        Assert.Equal(WindowGeometry.Place(WindowAction.NextDisplay, window, a, [a, b], 10), floating.Frame);
        var fixedFrame = WindowGeometry.AnchorSize(cycled.AnchorAction, cycled.Frame, cycled.Frame.Width + 100, 600);
        Assert.Equal(cycled.Frame.Right, fixedFrame.Right); // invoking Left must not anchor an actual Right slot as Left
    }
    [Theory]
    [InlineData(WindowAction.Center)] [InlineData(WindowAction.Maximize)] [InlineData(WindowAction.TopRight)] [InlineData(WindowAction.NextDisplay)]
    public void NonHalfCommandsCannotBorrowOrAdvanceACycle(WindowAction action)
    {
        Assert.Equal(1, WindowCycle.Length(WindowCycleMode.Displays, action, 4));
        var state = WindowCycle.Remember(WindowAction.LeftHalf, new(WindowCycleMode.Sizes, 2, "a"), "a", [a, b]);
        Assert.Equal(0, WindowCycle.Decide(WindowCycleMode.Sizes, action, state, a, [a, b], true).Step);
    }
}
