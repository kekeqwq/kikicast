using System.Text.Json;
using Kikicast.Core;

namespace Kikicast.Core.Tests;

public sealed class WindowLayoutTests
{
    private static readonly LayoutDisplay Display = new(@"monitor:\\?\DISPLAY#OwnedPanel#stable", "Owned panel");
    private static readonly LayoutScreen Screen = new(Display, new("session", new(-1500, 35, 1500, 865), 1.5, new(-1500, 0, 1500, 900)));
    private const string App = @"app:C:\OwnedFixture.exe";
    private static WindowLayoutEntry Entry() => new(Guid.NewGuid(), App, Display);
    private static WindowLayout Layout() => new(Guid.NewGuid(), "Owned layout") { Entries = [Entry()] };
    [Fact]
    public void DefaultsStableIdsAndSearchSerializationDoNotInventBindingsOrNativeState()
    {
        var p = new AppPreferences(); Assert.Empty(p.WindowLayouts); Assert.True(p.ShowWindowLayouts);
        var layout = Layout(); Assert.Null(layout.Validate()); Assert.True(WindowLayout.TryId(layout.EntryId.ToUpperInvariant(), out var id)); Assert.Equal(layout.Id, id);
        Assert.True(LauncherBindings.CanBind(layout.EntryId)); Assert.False(WindowLayout.TryId("window-layout:" + Guid.Empty, out _));
        Assert.False(WindowLayout.TryId("window-layout:" + Guid.NewGuid().ToString("N"), out _));
        var json = JsonSerializer.Serialize(layout); Assert.DoesNotContain("SearchFields", json); Assert.DoesNotContain("Summary", json); Assert.DoesNotContain("session", json); Assert.DoesNotContain("Handle", json);
        var renamed = layout with { Name = "Renamed arrangement" }; Assert.NotSame(layout.SearchFields, renamed.SearchFields);
        Assert.NotNull(LauncherMatch.Match(LauncherSearchText.Create("renamed"), renamed.SearchFields.Title));
        Assert.Equal(3, (int)LauncherKind.CustomCommand); Assert.Equal(4, (int)LauncherKind.WindowLayout);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)]
    public void NineAnchorsUseDestinationDpiAndTheSameWorkCanvas(int anchor)
    {
        var entry = Entry() with { WidthFraction = .5, HeightFraction = .4, Anchor = (WindowSizeAnchor)anchor, OffsetX = -10, OffsetY = 10 };
        var box = WindowLayoutGeometry.Box(Screen.Area, 8);
        var expected = new Rect(box.X + box.Width * .5 * (anchor % 3) / 2 - 15, box.Y + box.Height * .6 * (anchor / 3) / 2 + 15, box.Width * .5, box.Height * .4);
        Assert.Equal(WindowGeometry.Round(WindowGeometry.Clamp(expected, box)), WindowLayoutGeometry.Resolve(entry, Screen.Area, 8));
    }
    [Fact]
    public void DescribeAndResolveRoundTripIntegerContainedFramesAtSeveralDpisAndNegativeOrigins()
    {
        foreach (var scale in new[] { 1d, 1.25, 1.5, 2d, 2.5 })
        foreach (var gap in new[] { 0d, 7d, 10.5 })
        {
            var screen = Screen.Area with { Scale = scale }; var box = WindowLayoutGeometry.Box(screen, gap);
            for (var i = 0; i < 200; i++)
            {
                var width = 50 + i % 900; var height = 50 + i % 700;
                var x = Math.Ceiling(box.X) + i % (int)(box.Width - width - 1); var y = Math.Ceiling(box.Y) + i % (int)(box.Height - height - 1);
                var frame = new Rect(x, y, width, height); var saved = WindowLayoutGeometry.Describe(frame, screen, gap)!;
                var entry = Entry() with { WidthFraction = saved.WidthFraction, HeightFraction = saved.HeightFraction, Anchor = saved.Anchor, OffsetX = saved.OffsetX, OffsetY = saved.OffsetY };
                Assert.Equal(frame, WindowLayoutGeometry.Resolve(entry, screen, gap));
            }
        }
    }
    [Fact]
    public void ZeroFractionsClampToOnePixelAndOversizedCaptureNormalizesIdempotently()
    {
        Assert.Equal(1, WindowLayoutGeometry.Resolve(Entry() with { WidthFraction = 0, HeightFraction = 0 }, Screen.Area)!.Value.Width);
        var saved = WindowLayoutGeometry.Describe(new(-2000, -100, 2000, 1200), Screen.Area)!;
        var entry = Entry() with { WidthFraction = saved.WidthFraction, HeightFraction = saved.HeightFraction, Anchor = saved.Anchor, OffsetX = saved.OffsetX, OffsetY = saved.OffsetY };
        var normalized = WindowLayoutGeometry.Resolve(entry, Screen.Area)!.Value;
        var described = WindowLayoutGeometry.Describe(normalized, Screen.Area)!;
        Assert.Equal(normalized, WindowLayoutGeometry.Resolve(entry with { WidthFraction = described.WidthFraction, HeightFraction = described.HeightFraction, Anchor = described.Anchor, OffsetX = described.OffsetX, OffsetY = described.OffsetY }, Screen.Area));
        Assert.Equal(Screen.Area.WorkArea, WindowLayoutGeometry.Reanchor(new(-1000, 200, 200, 200), 1500, 865, WindowSizeAnchor.BottomRight, Screen.Area.WorkArea));
        var tooBig = WindowLayoutGeometry.Reanchor(new(-1000, 200, 200, 200), 2000, 1200, WindowSizeAnchor.Center, Screen.Area.WorkArea);
        Assert.Equal(Screen.Area.WorkArea.X, tooBig.X); Assert.Equal(Screen.Area.WorkArea.Y, tooBig.Y); Assert.Equal(2000, tooBig.Width);
    }
    [Fact]
    public void CaptureIsGaplessAndNeverPersistsRuntimeSessionIdentity()
    {
        var frame = new Rect(-1400, 50, 700, 500); var entry = WindowLayoutGeometry.CaptureEntry(App, Screen, frame)!;
        Assert.Equal(frame, WindowLayoutGeometry.Resolve(entry, Screen.Area)); Assert.Equal(Display, entry.Display);
    }
    [Theory]
    [InlineData("window:LeftHalf")] [InlineData("cmd:shell")] [InlineData("app:\\\\server\\file.exe")] [InlineData("app:C:\\file.txt")] [InlineData("")]
    public void OnlyApplicationIdentitiesAreValidLayoutTargets(string id) => Assert.NotNull((Entry() with { ApplicationId = id }).Validate());
    [Fact]
    public void AllMalformedSchemaNumbersListsIdentitiesAndFrontMarksAreRefused()
    {
        var entry = Entry(); var layout = Layout();
        foreach (var bad in new[] { entry with { WidthFraction = double.NaN }, entry with { HeightFraction = 1.1 }, entry with { OffsetX = double.PositiveInfinity },
            entry with { OffsetY = 10001 }, entry with { Anchor = (WindowSizeAnchor)99 }, entry with { Display = new("monitor:DISPLAY1", "Same name") }, entry with { Id = Guid.Empty } }) Assert.NotNull(bad.Validate());
        Assert.NotNull((layout with { Entries = [] }).Validate()); Assert.NotNull((layout with { Entries = [entry, entry] }).Validate());
        Assert.NotNull((layout with { FrontmostEntryId = Guid.NewGuid() }).Validate()); Assert.NotNull((layout with { Entries = null! }).Validate());
        Assert.NotNull(WindowLayout.ValidateList([layout, layout with { Id = Guid.NewGuid() }])); Assert.NotNull(WindowLayout.ValidateList(null));
        Assert.NotNull(WindowLayout.ValidateList(Enumerable.Range(0, 65).Select(i => layout with { Id = Guid.NewGuid(), Name = "Owned " + i }).ToList()));
        var full = Enumerable.Range(0, 32).Select(_ => Entry()).ToList();
        Assert.NotNull(WindowLayout.ValidateList(Enumerable.Range(0, 17).Select(i => layout with { Id = Guid.NewGuid(), Name = "Owned " + i, Entries = full }).ToList()));
        Assert.NotNull((layout with { Entries = full.Append(Entry()).ToList() }).Validate());
    }
    [Fact]
    public void PlanningUsesExactStableDisplayAndDeterministicNearestUnclaimedWindows()
    {
        var first = Entry() with { Anchor = WindowSizeAnchor.Left }; var second = Entry() with { Anchor = WindowSizeAnchor.Right };
        var layout = Layout() with { Entries = [first, second, Entry()], FrontmostEntryId = second.Id };
        LayoutWindow[] windows = [new(10, "owned", new(-1450, 50, 200, 500)), new(20, "OWNED", new(-400, 50, 200, 500))];
        var plan = WindowLayoutPlan.Make(layout, [Screen], [new(App, "owned")], windows, 0);
        Assert.Equal(new long[] { 10, 20 }, plan.Placements.Select(x => x.Handle)); Assert.Equal(second.Id, plan.FrontmostEntryId); Assert.Single(plan.Skipped);
        Assert.Equal(plan.Placements, WindowLayoutPlan.Make(layout, [Screen], [new(App, "owned")], windows.Reverse().ToArray(), 0).Placements);
        var missing = Screen with { Display = new(@"monitor:\\?\DISPLAY#OtherPanel", Display.Name) };
        Assert.Empty(WindowLayoutPlan.Make(layout, [missing], [new(App, "owned")], windows, 0).Placements);
        Assert.Empty(WindowLayoutPlan.Make(layout, [Screen, Screen], [new(App, "owned")], windows, 0).Placements);
        Assert.Empty(WindowLayoutPlan.Make(layout, [Screen], [], windows, 0).Placements);
        Assert.Empty(WindowLayoutPlan.Make(layout, [Screen], [new(App, "owned")], [windows[0], windows[0]], 0).Placements);
    }
    [Fact]
    public void IndependentVisibilityEnablementMasterGatesAndDuplicateIdsRetainDormantOwnership()
    {
        var layout = Layout(); var p = LauncherBindings.Set(new() { WindowManagementEnabled = true, WindowLayouts = [layout], ShowWindowLayouts = false, HiddenEntryKeys = [layout.EntryId] }, layout.EntryId, new(BindingKind.Combo, 132, 3));
        Assert.Null(p.Validate());
        Assert.NotNull(WindowLayout.Runnable(p, layout.Id)); Assert.Single(BindingCatalog.Build(p), x => x.EntryId == layout.EntryId);
        Assert.Null(WindowLayout.Runnable(p with { ApplicationsEnabled = false }, layout.Id)); Assert.Null(WindowLayout.Runnable(p with { WindowManagementEnabled = false }, layout.Id));
        Assert.Null(WindowLayout.Runnable(p with { WindowLayouts = [layout with { Enabled = false }] }, layout.Id));
        Assert.NotNull(LauncherBindings.Get(p with { WindowLayouts = [] }, layout.EntryId));
        var duplicate = layout with { FrontmostEntryId = layout.Entries[0].Id }; var copy = duplicate.Duplicate("Owned copy");
        Assert.NotEqual(layout.Id, copy.Id); Assert.NotEqual(layout.Entries[0].Id, copy.Entries[0].Id); Assert.Equal(copy.Entries[0].Id, copy.FrontmostEntryId);
    }
    [Fact]
    public void SameNamedDisplaysResolveTheirOwnCanvasDpiAndIdentityWithoutOrderDependence()
    {
        var other = new LayoutScreen(new(@"monitor:\\?\DISPLAY#SecondOwnedPanel", Display.Name), new("other-session", new(0, 0, 1920, 1040), 2, new(0, 0, 1920, 1080)));
        var first = Entry() with { OffsetX = 10, OffsetY = 10 }; var second = Entry() with { Display = other.Display, OffsetX = 10, OffsetY = 10 };
        var layout = Layout() with { Entries = [first, second] };
        LayoutWindow[] windows = [new(1, "owned", Screen.Area.WorkArea), new(2, "owned", other.Area.WorkArea)];
        var plan = WindowLayoutPlan.Make(layout, [Screen, other], [new(App, "owned")], windows, 10);
        Assert.Equal(2, plan.Placements.Count);
        Assert.Equal(WindowLayoutGeometry.Resolve(first, Screen.Area, 10), plan.Placements[0].Frame);
        Assert.Equal(WindowLayoutGeometry.Resolve(second, other.Area, 10), plan.Placements[1].Frame);
        Assert.Equal(plan.Placements, WindowLayoutPlan.Make(layout, [other, Screen], [new(App, "owned")], windows.Reverse().ToArray(), 10).Placements);
    }
    [Fact]
    public void DisabledAndOversizedPlansHaveNoPlacementOrFrontmostSideEffects()
    {
        var entry = Entry(); var layout = Layout() with { Entries = [entry], FrontmostEntryId = entry.Id };
        LayoutWindow[] windows = [new(1, "owned", Screen.Area.WorkArea)];
        Assert.Empty(WindowLayoutPlan.Make(layout with { Enabled = false }, [Screen], [new(App, "owned")], windows, 0).Placements);
        var invalid = WindowLayoutPlan.Make(layout, [Screen], [new(App, "owned")], Enumerable.Repeat(windows[0], 513).ToArray(), 0);
        Assert.Empty(invalid.Placements); Assert.Single(invalid.Skipped); Assert.Null(invalid.FrontmostEntryId);
        Assert.Empty(WindowLayoutPlan.Make(layout, Enumerable.Repeat(Screen, 65).ToArray(), [new(App, "owned")], windows, 0).Placements);
    }
    [Fact]
    public void OnlyExplicitDeletionCleansReferencesAndImmediateCustomizationIsMerged()
    {
        var layout = Layout(); var size = new CustomWindowSize(Guid.NewGuid(), "Size");
        var p = LauncherBindings.Set(new() { WindowLayouts = [layout], CustomWindowSizes = [size], FavoriteKeys = [layout.EntryId, size.EntryId], HiddenEntryKeys = [layout.EntryId], LauncherAliases = new() { [layout.EntryId] = "live" } }, layout.EntryId, new(BindingKind.Combo, 132, 3));
        var renamed = WindowLayout.MergeReferences(p, p with { WindowLayouts = [layout with { Name = "Renamed", Enabled = false }], FavoriteKeys = [], LauncherAliases = [] });
        Assert.Equal(p.FavoriteKeys, renamed.FavoriteKeys); Assert.Equal("live", renamed.LauncherAliases[layout.EntryId]); Assert.NotNull(LauncherBindings.Get(renamed, layout.EntryId));
        var deleted = WindowLayout.MergeReferences(p, p with { WindowLayouts = [] }); Assert.Equal(new[] { size.EntryId }, deleted.FavoriteKeys); Assert.Empty(deleted.HiddenEntryKeys); Assert.Empty(deleted.LauncherAliases); Assert.Empty(deleted.EntryBindings);
    }
}
