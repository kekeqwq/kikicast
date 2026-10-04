using Kikicast.Core;
using Kikicast.Windows;

namespace Kikicast.Windows.Tests;

public sealed class WindowLayoutTests
{
    private static readonly LayoutDisplay Display = new(@"monitor:\\?\DISPLAY#OwnedPanel#stable", "Owned panel");
    private static readonly DisplayArea Area = new("session", new(0, 40, 1000, 960), 1.5, new(0, 0, 1000, 1000));
    [Fact]
    public void NativeReadOnlyDisplayContractsAreBoundedAndDoNotRequireAFabricatedDisplay()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.True(LayoutDisplays.NativeContractsValid);
        var displays = LayoutDisplays.Read(); Assert.InRange(displays.Count, 0, WindowCycle.MaximumDisplays);
        Assert.Equal(displays.Count, displays.Select(x => x.Display.Identity).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var display in displays) { Assert.Null(display.Display.Validate()); Assert.True(display.Area.WorkArea.IsValid); Assert.NotNull(display.Area.Bounds); }
    }
    [Fact]
    public void ExactMonitorPathAndCurrentBoundsAreRequiredNamesAndClonePathsNeverSupplyFallback()
    {
        if (!OperatingSystem.IsWindows()) return;
        LayoutDisplays.MonitorRecord[] monitors = [new("source", Area)];
        var path = new LayoutDisplays.PathRecord("SOURCE", @"\\?\DISPLAY#OwnedPanel#stable", "Owned panel", Area.Bounds!.Value);
        var matched = Assert.Single(LayoutDisplays.Bind(monitors, [path])).Display;
        Assert.Equal(Display.Identity, matched.Identity, ignoreCase: true); Assert.Equal(Display.Name, matched.Name);
        Assert.Empty(LayoutDisplays.Bind(monitors, [path, path with { DevicePath = @"\\?\DISPLAY#ClonedPanel" }]));
        Assert.Empty(LayoutDisplays.Bind(monitors, [path with { SourceName = "other" }]));
        Assert.Empty(LayoutDisplays.Bind(monitors, [path with { SourceBounds = new(0, 0, 900, 900) }]));
        Assert.Empty(LayoutDisplays.Bind(monitors, [path with { DevicePath = "DISPLAY1" }]));
        Assert.Empty(LayoutDisplays.Bind([monitors[0], monitors[0]], [path]));
        Assert.Empty(LayoutDisplays.Bind(Enumerable.Repeat(monitors[0], 65).ToArray(), [path]));
        Assert.Empty(LayoutDisplays.Bind(monitors, Enumerable.Repeat(path, 129).ToArray()));
    }
    [Fact]
    public void CurrentCatalogSourceAndLocalSafetyGatesAreCheckedWithoutLaunchingAnything()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "KikicastLayoutSources-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            var exe = Path.Combine(root, "owned.exe"); File.Copy(Environment.ProcessPath!, exe);
            LauncherEntry[] catalog = [new("Owned", exe), new("Registration", exe, RegistrationNames: ["owned.exe"]),
                new("Remote", @"\\server\app.exe"), new("Missing", Path.Combine(root, "missing.exe")),
                new("Packaged", "shell:AppsFolder\\Kikicast.LayoutOwned_xxxxxxxxxxxxx!App", AppUserModelId: "Kikicast.LayoutOwned_xxxxxxxxxxxxx!App")];
            Assert.Equal(2, LayoutWindowInventory.Applications(catalog, new()).Count);
            Assert.Single(LayoutWindowInventory.Applications(catalog, new() { IncludeWindowsAppPaths = false }));
            Assert.Empty(LayoutWindowInventory.Applications(catalog, new() { ApplicationsEnabled = false }));
            Assert.Empty(LayoutWindowInventory.Read([]).Windows);
            Assert.Throws<ArgumentException>(() => { if (OperatingSystem.IsWindows()) LayoutWindowInventory.ReadOwned([new(0, 0, default)], "owned"); });
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void CaptureUsesNativeHostIdentityWithoutNameFallbackAndCapsEntries()
    {
        if (!OperatingSystem.IsWindows()) return;
        var frame = new Rect(10, 50, 400, 500);
        var windows = Enumerable.Range(1, 40).Select(i => new LayoutWindow(i, "owned", frame)).ToArray();
        var displays = windows.ToDictionary(x => x.Handle, _ => Area.Id);
        var snapshot = new LayoutWindowInventory.Snapshot(windows, new Dictionary<long, ForegroundTarget>(), displays, false);
        LayoutApplication[] apps = [new(@"app:C:\Owned.exe", "owned")];
        var layout = LayoutWindowInventory.Capture(snapshot, [new(Display, Area)], apps, 2);
        Assert.Equal(32, layout.Entries.Count); Assert.False(layout.UsesPreferredGap); Assert.Equal(layout.Entries[1].Id, layout.FrontmostEntryId);
        Assert.All(layout.Entries, x => Assert.Equal(frame, WindowLayoutGeometry.Resolve(x, Area)));
        Assert.Empty(LayoutWindowInventory.Capture(snapshot, [new(Display, Area with { Id = "different-session" })], apps).Entries);
        Assert.Empty(LayoutWindowInventory.Capture(snapshot, [], apps).Entries);
    }
    [Fact]
    public async Task CancelInvalidAndMissingIdentityPlansCannotWriteWindows()
    {
        if (!OperatingSystem.IsWindows()) return;
        var entry = Guid.NewGuid(); var placement = new LayoutPlacement(entry, 0, new(10, 50, 400, 500), Area.WorkArea, WindowSizeAnchor.Center, Display.Identity);
        var plan = new WindowLayoutPlan([placement], [], null); var manager = new WindowManager();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var cancelled = await manager.ApplyLayoutAsync(plan, [new(Display, Area)], new Dictionary<long, ForegroundTarget>(), cancellation.Token);
        Assert.False(Assert.Single(cancelled).Placed);
        var invalid = await manager.ApplyLayoutAsync(plan with { Placements = [placement with { Frame = new(-500, -500, 400, 500) }] }, [new(Display, Area)], new Dictionary<long, ForegroundTarget>());
        Assert.Equal("Invalid layout placement snapshot.", Assert.Single(invalid).Error);
        var missing = await manager.ApplyLayoutAsync(plan, [new(Display, Area)], new Dictionary<long, ForegroundTarget>());
        Assert.False(Assert.Single(missing).Placed); Assert.NotNull(missing[0].Error);
    }
    [Fact]
    public void DefinitionReferencesAndBindingSaveTogetherLockedWritesPreserveBytes()
    {
        var root = Path.Combine(Path.GetTempPath(), "KikicastLayouts-" + Guid.NewGuid()); Directory.CreateDirectory(root); var path = Path.Combine(root, "settings.json");
        try
        {
            var entry = new WindowLayoutEntry(Guid.NewGuid(), @"app:C:\Owned.exe", Display);
            var layout = new WindowLayout(Guid.NewGuid(), "Owned") { Entries = [entry], FrontmostEntryId = entry.Id, UsesPreferredGap = false };
            var preferences = LauncherBindings.Set(new() { WindowManagementEnabled = true, WindowLayouts = [layout], ShowWindowLayouts = false,
                FavoriteKeys = [layout.EntryId], LauncherAliases = new() { [layout.EntryId] = "owned" }, HiddenEntryKeys = [layout.EntryId] }, layout.EntryId, new(BindingKind.Combo, 132, 3));
            JsonFile.Save(path, preferences); var bytes = File.ReadAllBytes(path);
            var loaded = JsonFile.Load(path, () => new AppPreferences()); Assert.Null(loaded.Validate());
            var persisted = Assert.Single(loaded.WindowLayouts); Assert.Equal(layout.Id, persisted.Id); Assert.Equal(entry, Assert.Single(persisted.Entries)); Assert.Equal(entry.Id, persisted.FrontmostEntryId);
            Assert.Equal(preferences.EntryBindings, loaded.EntryBindings); Assert.False(loaded.ShowWindowLayouts);
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.True(Record.Exception(() => JsonFile.Save(path, WindowLayout.MergeReferences(preferences, preferences with { WindowLayouts = [] }))) is IOException or UnauthorizedAccessException);
            Assert.Equal(bytes, File.ReadAllBytes(path)); Assert.Single(Directory.GetFiles(root));
            JsonFile.Save(path, WindowLayout.MergeReferences(preferences, preferences with { WindowLayouts = [] }));
            loaded = JsonFile.Load(path, () => new AppPreferences()); Assert.Empty(loaded.WindowLayouts); Assert.Empty(loaded.EntryBindings); Assert.Empty(loaded.FavoriteKeys); Assert.Empty(loaded.HiddenEntryKeys); Assert.Empty(loaded.LauncherAliases);
        }
        finally { Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData("{\"WindowLayouts\":null}")]
    [InlineData("{\"WindowLayouts\":[null]}")]
    [InlineData("{\"WindowLayouts\":[{\"Id\":\"d156b8e7-9f76-408d-a416-91351351daa8\",\"Name\":\"Owned\",\"Entries\":null}]}")]
    public void DamagedAndLegacyFilesAreNeverRewrittenOnLoad(string json)
    {
        var root = Path.Combine(Path.GetTempPath(), "KikicastLayoutsDamaged-" + Guid.NewGuid()); Directory.CreateDirectory(root); var path = Path.Combine(root, "settings.json");
        try
        {
            File.WriteAllText(path, json); var bytes = File.ReadAllBytes(path); var loaded = JsonFile.Load(path, () => new AppPreferences());
            Assert.NotNull(loaded.Validate()); Assert.Equal(bytes, File.ReadAllBytes(path)); Assert.Single(Directory.GetFiles(root));
            File.WriteAllText(path, "{\"SchemaVersion\":1}"); bytes = File.ReadAllBytes(path); loaded = JsonFile.Load(path, () => new AppPreferences());
            Assert.Null(loaded.Validate()); Assert.Empty(loaded.WindowLayouts); Assert.True(loaded.ShowWindowLayouts); Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        finally { Directory.Delete(root, true); }
    }
}
