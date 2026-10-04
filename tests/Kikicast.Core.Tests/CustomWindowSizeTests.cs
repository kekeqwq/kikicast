using System.Text.Json;
using Kikicast.Core;

namespace Kikicast.Core.Tests;

public sealed class CustomWindowSizeTests
{
    private static readonly Guid key = Guid.Parse("5a2aa02a-86b1-4a64-a47e-12397dcd1657");
    private static CustomWindowSize Size => new(key, "Reading");
    [Fact]
    public void AdditiveDefaultsAndJsonRetainStableIdentityWithoutComputedSearchData()
    {
        var legacy = JsonSerializer.Deserialize<AppPreferences>("{\"Version\":1}")!;
        Assert.Empty(legacy.CustomWindowSizes); Assert.True(legacy.ShowWindowCommands); Assert.Null(legacy.Validate());
        var p = new AppPreferences { CustomWindowSizes = [Size] };
        var json = JsonSerializer.Serialize(p); var copy = JsonSerializer.Deserialize<AppPreferences>(json)!;
        Assert.Null(copy.Validate()); Assert.Equal(Size.EntryId, copy.CustomWindowSizes[0].EntryId);
        Assert.DoesNotContain("SearchFields", json); Assert.DoesNotContain("Summary", json); Assert.DoesNotContain("Maximum", json);
        Assert.True(CustomWindowSize.TryId(Size.EntryId.ToUpperInvariant(), out var id)); Assert.Equal(key, id);
        Assert.False(CustomWindowSize.TryId("window-size:" + Guid.Empty, out _));
    }
    [Theory]
    [InlineData(0, 0, 120, 80)]
    [InlineData(1, 0, 620, 80)]
    [InlineData(2, 0, 1120, 80)]
    [InlineData(0, 1, 120, 480)]
    [InlineData(1, 1, 620, 480)]
    [InlineData(2, 1, 1120, 480)]
    [InlineData(0, 2, 120, 880)]
    [InlineData(1, 2, 620, 880)]
    [InlineData(2, 2, 1120, 880)]
    public void NineAnchorsUseTheInsetHostCanvasAndDipScaling(int column, int row, double x, double y)
    {
        var size = Size with { Width = new(100, WindowSizeUnit.Dip), Height = new(100, WindowSizeUnit.Dip), Anchor = (WindowSizeAnchor)(row * 3 + column) };
        var host = new DisplayArea("host", new(100, 60, 1240, 1040), 2);
        Assert.Equal(new Rect(x, y, 200, 200), size.Frame(host, 10));
    }
    [Fact]
    public void MixedUnitsPercentOffsetsOversizeAndNegativeDisplaysClampIdempotently()
    {
        var host = new DisplayArea("negative", new(-2000, -500, 1200, 900), 1.5);
        var size = Size with { Width = new(50), Height = new(100, WindowSizeUnit.Dip), Anchor = WindowSizeAnchor.BottomRight, OffsetX = 4000, OffsetY = -4000 };
        var frame = size.Frame(host, 20)!.Value;
        Assert.Equal(new Rect(-1400, -470, 570, 150), frame);
        Assert.Equal(frame, size.Frame(host, 20));
        var full = size with { Width = new(16000, WindowSizeUnit.Dip), Height = new(16000, WindowSizeUnit.Dip) };
        Assert.Equal(WindowGeometry.Round(WindowGeometry.Canvas(host.WorkArea, 30)), full.Frame(host, 20));
        Assert.Equal(new Rect(0, 0, 1, 1), (Size with { Width = new(1), Height = new(1) }).Frame(new("tiny", new(0, 0, 1, 1))));
        Assert.Null(size.Frame(new("bad", new(0, 0, 0, 100))));
    }
    [Fact]
    public void ReanchorsActualMinimumOrFixedSizeRatherThanPretendingItShrank()
    {
        var host = new DisplayArea("host", new(0, 0, 1000, 800));
        var size = Size with { Anchor = WindowSizeAnchor.BottomRight, OffsetX = -10, OffsetY = -20 };
        Assert.Equal(new Rect(680, 570, 300, 200), size.Reanchor(host, 10, 300, 200));
        Assert.Equal(new Rect(10, 10, 1400, 900), size.Reanchor(host, 10, 1400, 900));
    }
    [Fact]
    public void UnitConversionUsesAvailableDipAndClampsWholeNumberEditorValues()
    {
        Assert.Equal(new WindowSizeDimension(500, WindowSizeUnit.Dip), new WindowSizeDimension(50).Converted(WindowSizeUnit.Dip, 1000));
        Assert.Equal(new WindowSizeDimension(50), new WindowSizeDimension(500, WindowSizeUnit.Dip).Converted(WindowSizeUnit.Percent, 1000));
        Assert.Equal(100, new WindowSizeDimension(16000, WindowSizeUnit.Dip).Converted(WindowSizeUnit.Percent, 1000).Value);
        Assert.Equal(1, new WindowSizeDimension(-8).Clamped().Value);
        Assert.Equal(16000, new WindowSizeDimension(int.MaxValue, WindowSizeUnit.Dip).Clamped().Value);
        Assert.Throws<ArgumentException>(() => new WindowSizeDimension().Converted(WindowSizeUnit.Dip, double.NaN));
    }
    [Fact]
    public void CorruptDuplicateNullAndOversizedListsAreRefusedNotSilentlyRewritten()
    {
        foreach (var bad in new[] { Size with { Id = Guid.Empty }, Size with { Name = "\t" }, Size with { Name = "bad\0name" }, Size with { Width = null! },
            Size with { Width = new(0) }, Size with { Height = new(101) }, Size with { Width = new(5, (WindowSizeUnit)9) }, Size with { Anchor = (WindowSizeAnchor)9 }, Size with { OffsetX = -4001 }, Size with { OffsetY = 4001 } })
            Assert.NotNull((new AppPreferences { CustomWindowSizes = [bad] }).Validate());
        foreach (var bad in new List<CustomWindowSize>[] { null!, [null!], [Size, Size], [Size, Size with { Id = Guid.NewGuid(), Name = " reading " }],
            Enumerable.Range(0, 129).Select(i => Size with { Id = Guid.NewGuid(), Name = "Size " + i }).ToList() })
            Assert.NotNull((new AppPreferences { CustomWindowSizes = bad }).Validate());
    }
    [Fact]
    public void SearchCacheIsIdentityKeyedAndRenameCannotKeepOldFields()
    {
        var size = Size; var fields = size.SearchFields; Assert.Same(fields, size.SearchFields);
        var renamed = size with { Name = "New fixture name" };
        Assert.NotSame(fields, renamed.SearchFields);
        Assert.NotNull(LauncherMatch.Match(LauncherSearchText.Create("new fixture"), renamed.SearchFields.Title));
    }
    [Fact]
    public void HiddenAndDisabledSizesRetainDormantConflictOwnershipAndOldWindowIds()
    {
        var p = LauncherBindings.Set(new() { WindowManagementEnabled = true, CustomWindowSizes = [Size], ShowWindowCommands = false, HiddenEntryKeys = [Size.EntryId] }, Size.EntryId, new(BindingKind.Combo, 135, 3));
        Assert.Null(p.Validate()); Assert.Equal(Size, CustomWindowSize.Runnable(p, key));
        var binding = Assert.Single(BindingCatalog.Build(p), x => x.EntryId != null); Assert.True(LauncherBindings.IsCurrent(p, binding));
        var disabled = p with { CustomWindowSizes = [Size with { Enabled = false }] };
        Assert.Null(CustomWindowSize.Runnable(disabled, key)); Assert.Single(disabled.EntryBindings);
        var off = p with { WindowManagementEnabled = false };
        Assert.DoesNotContain(BindingCatalog.Build(off), x => x.EntryId != null); Assert.False(LauncherBindings.IsCurrent(off, binding));
        Assert.NotNull((off with { PaletteBinding = binding.Binding }).Validate());
        var old = new AppPreferences { WindowManagementEnabled = true, WindowBindings = new() { [WindowAction.LeftHalf] = new(BindingKind.Combo, 134, 3) } };
        Assert.Equal(1000, Assert.Single(BindingCatalog.Build(old), x => x.WindowAction != null).Id);
    }
    [Fact]
    public void ExplicitDeletionRemovesOnlyItsReferencesAndMergeKeepsImmediateItemEdits()
    {
        var other = "cmd:settings";
        var current = LauncherBindings.Set(new() { CustomWindowSizes = [Size], FavoriteKeys = [Size.EntryId, other], HiddenEntryKeys = [Size.EntryId], LauncherAliases = new() { [Size.EntryId] = "read", [other] = "config" } }, Size.EntryId, new(BindingKind.Combo, 135, 3));
        var disabled = CustomWindowSize.MergeReferences(current, current with { CustomWindowSizes = [Size with { Enabled = false }], FavoriteKeys = [] });
        Assert.Equal(current.FavoriteKeys, disabled.FavoriteKeys); Assert.Single(disabled.EntryBindings);
        var deleted = CustomWindowSize.MergeReferences(current, current with { CustomWindowSizes = [] });
        Assert.Equal([other], deleted.FavoriteKeys); Assert.Empty(deleted.HiddenEntryKeys); Assert.Empty(deleted.EntryBindings); Assert.Equal("config", deleted.LauncherAliases[other]);
    }
}
