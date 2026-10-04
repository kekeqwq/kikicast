using System.Text.Json;
using Kikicast.Core;

namespace Kikicast.Core.Tests;

public class LauncherBindingTests
{
    private static readonly HotKeyBinding Combo = new(BindingKind.Combo, 135, 3);
    [Fact]
    public void DefaultsRemainOnlyDoubleControlAndLegacyJsonGetsEmptyItems()
    {
        var defaults = JsonSerializer.Deserialize<AppPreferences>("{}")!;
        Assert.Empty(defaults.EntryBindings); Assert.Single(BindingCatalog.Build(defaults)); Assert.Equal(new HotKeyBinding(), defaults.PaletteBinding);
        var saved = LauncherBindings.Set(defaults, "cmd:history", Combo);
        var restored = JsonSerializer.Deserialize<AppPreferences>(JsonSerializer.Serialize(saved))!;
        Assert.Equal(Combo, LauncherBindings.Get(restored, "CMD:HISTORY")); Assert.Null(restored.Validate());
    }
    [Theory]
    [InlineData("cmd:shell")]
    [InlineData("cmd:shell-query")]
    [InlineData("window:LeftHalf")]
    [InlineData("custom-command:00000000-0000-0000-0000-000000000000")]
    [InlineData("custom-command:invalid")]
    [InlineData("app:\\\\server\\share\\app.exe")]
    [InlineData("unknown")]
    public void UnsafeUnsupportedAndQueryDrivenIdentitiesAreRejected(string id)
    {
        Assert.False(LauncherBindings.CanBind(id));
        Assert.NotNull((new AppPreferences { EntryBindings = new() { [id] = Combo } }).Validate());
        Assert.Throws<ArgumentException>(() => LauncherBindings.Set(new(), id, Combo));
    }
    [Fact]
    public void ItemConflictsIncludePaletteWindowFallbackAndOtherItemsEvenWhenDisabled()
    {
        var p = LauncherBindings.Set(new(), "cmd:settings", new()); Assert.NotNull(p.Validate());
        p = LauncherBindings.Set(new() { WindowBindings = new() { [WindowAction.LeftHalf] = Combo } }, "cmd:settings", Combo); Assert.NotNull(p.Validate());
        p = LauncherBindings.Set(new() { AltSpaceFallback = true }, "cmd:settings", new(BindingKind.Combo, 32, 1)); Assert.NotNull(p.Validate());
        p = LauncherBindings.Set(LauncherBindings.Set(new() { SystemCommandsEnabled = false }, "cmd:" + RecycleBinCommand.EmptyId, Combo), "cmd:history", Combo);
        Assert.NotNull(p.Validate());
    }
    [Fact]
    public void ItemDoubleTapSharesTheSameSideConflictPolicy()
    {
        var p = LauncherBindings.Set(new(), "cmd:history", new(BindingKind.DoubleTap, 18, Side: KeySide.Left));
        p = LauncherBindings.Set(p, "cmd:settings", new(BindingKind.DoubleTap, 18, Side: KeySide.Right)); Assert.Null(p.Validate());
        Assert.NotNull(LauncherBindings.Set(p, "cmd:settings", new(BindingKind.DoubleTap, 18)).Validate());
    }
    [Fact]
    public void FeatureGatesDoNotEraseBindingsOrDependOnVisibility()
    {
        var key = "custom-command:" + Guid.NewGuid();
        var p = LauncherBindings.Set(new(), key, Combo);
        Assert.Single(BindingCatalog.Build(p)); Assert.Equal(2, BindingCatalog.Build(p, false).Count);
        p = p with { SavedCommandsEnabled = true, ShowSavedCommands = false, HiddenEntryKeys = [key] };
        Assert.Equal(2, BindingCatalog.Build(p).Count);
        Assert.Equal(Combo, LauncherBindings.Get(p with { SavedCommandsEnabled = false }, key));
        Assert.False(LauncherBindings.FeatureEnabled("cmd:history", p with { CalculationHistoryEnabled = false }));
        Assert.False(LauncherBindings.FeatureEnabled("app:C:\\Owned.exe", p with { ApplicationsEnabled = false }));
        Assert.True(LauncherBindings.FeatureEnabled("cmd:settings", p with { SystemCommandsEnabled = false }));
    }
    [Fact]
    public void CatalogKeepsExistingIdsAndItemIdentityIsIndependentOfItsNativeSlot()
    {
        var p = LauncherBindings.Set(new() { WindowManagementEnabled = true, WindowBindings = new() { [WindowAction.LeftHalf] = new(BindingKind.Combo, 134, 7) } }, "cmd:settings", Combo);
        var first = BindingCatalog.Build(p); Assert.Equal(1, first.Single(x => x.WindowAction == null && x.EntryId == null).Id);
        Assert.Equal(1000 + (int)WindowAction.LeftHalf, first.Single(x => x.WindowAction != null).Id);
        var item = first.Single(x => x.EntryId != null); Assert.InRange(item.Id, 6000, 6255); Assert.Equal("cmd:settings", item.EntryId);
        Assert.True(LauncherBindings.IsCurrent(p, item)); Assert.False(LauncherBindings.IsCurrent(LauncherBindings.Set(p, "cmd:settings", Combo with { Key = 133 }), item));
        Assert.False(LauncherBindings.IsCurrent(LauncherBindings.Set(p, "cmd:settings", null), item));
    }
    [Fact]
    public void DuplicateNullOversizedAndCorruptItemMapsFailValidation()
    {
        Assert.NotNull((new AppPreferences { EntryBindings = null! }).Validate());
        Assert.NotNull((new AppPreferences { EntryBindings = new() { ["cmd:settings"] = null! } }).Validate());
        Assert.NotNull((new AppPreferences { EntryBindings = new() { ["cmd:settings"] = Combo, ["CMD:SETTINGS"] = Combo with { Key = 134 } } }).Validate());
        var rows = Enumerable.Range(0, 257).ToDictionary(i => "app:C:\\Owned" + i + ".exe", _ => Combo);
        Assert.Contains("256", (new AppPreferences { EntryBindings = rows }).Validate());
    }
    [Fact]
    public void ExplicitDeletionRemovesOnlyThatItemsReferencesAndBindings()
    {
        var p = LauncherBindings.Set(LauncherBindings.Set(new() { FavoriteKeys = ["cmd:history", "cmd:settings"] }, "cmd:history", Combo), "cmd:settings", Combo with { Key = 134 });
        p = LauncherCustomization.RemoveReferences(p, "cmd:history");
        Assert.Null(LauncherBindings.Get(p, "cmd:history")); Assert.Equal(Combo with { Key = 134 }, LauncherBindings.Get(p, "cmd:settings")); Assert.Single(p.FavoriteKeys);
    }
}
