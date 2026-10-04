using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using Kikicast.Core;
using Kikicast.Windows;
using Rect = Kikicast.Core.Rect;

namespace Kikicast.App;

public partial class MainWindow
{
    internal async Task VerifyLayoutModelsAsync()
    {
        var original = store.Preferences; var oldEntries = entries; var history = store.History;
        var display = new LayoutDisplay(@"monitor:\\?\DISPLAY#OwnedMissingPanel", "Owned missing panel");
        var layout = new WindowLayout(Guid.NewGuid(), "Owned arrangement") { Entries = [new(Guid.NewGuid(), @"app:C:\KikicastMissingLayoutFixture.exe", display)] };
        try
        {
            entries = [];
            var p = LauncherBindings.Set(original with { WindowManagementEnabled = true, ApplicationsEnabled = true, WindowLayouts = [layout], ShowWindowLayouts = true,
                EntryBindings = [], FavoriteKeys = [layout.EntryId], HiddenEntryKeys = [], LauncherAliases = new() { [layout.EntryId] = "owned-layout-alias" } }, layout.EntryId, new(BindingKind.Combo, 132, 3));
            await store.SavePreferencesAsync(p); Query.Text = "owned-layout-alias"; Refresh();
            if (Results.Items.Cast<Row>().First().Layout?.Id != layout.Id || !SettingsItems.Any(x => x.Id == layout.EntryId && x.Kind == LauncherKind.WindowLayout))
                throw new InvalidOperationException("Layout alias/current settings identity was lost.");
            Query.Clear(); Refresh();
            if (!Results.Items.Cast<Row>().Any(x => x.Layout?.Id == layout.Id && x.Section == "Favorites" && x.GlobalChord != null)) throw new InvalidOperationException("Layout favorites/chords were lost.");
            await store.UpdatePreferencesAsync(x => x with { ShowWindowLayouts = false, HiddenEntryKeys = [layout.EntryId] }); Refresh();
            if (Results.Items.Cast<Row>().Any(x => x.Layout != null) || ResolveBindingRow(layout.EntryId) == null) throw new InvalidOperationException("Layout visibility disabled its execution identity.");
            var binding = BindingCatalog.Build(store.Preferences).Single(x => x.EntryId == layout.EntryId);
            foreach (var gated in new[] { store.Preferences with { WindowManagementEnabled = false }, store.Preferences with { ApplicationsEnabled = false },
                store.Preferences with { WindowLayouts = [layout with { Enabled = false }] }, store.Preferences with { WindowLayouts = [] } })
            {
                await store.SavePreferencesAsync(gated); await RunEntryBindingAsync(binding); await ExecuteRowAsync(new(layout.Name, layout.Summary, Layout: layout));
                if (!ReferenceEquals(history, store.History) || LauncherBindings.Get(store.Preferences, layout.EntryId) == null || ResolveBindingRow(layout.EntryId) != null)
                    throw new InvalidOperationException("A disabled/missing/master-gated layout executed or erased its binding.");
            }
            if (!SettingsItems.Any(x => x.Id == layout.EntryId && x.Unavailable)) throw new InvalidOperationException("Missing layout cannot be recovered in settings.");
        }
        finally { entries = oldEntries; await store.SavePreferencesAsync(original); await store.UpdateHistoryAsync(_ => history); Query.Clear(); Refresh(); if (IsVisible) Query.Focus(); }
    }
    internal async Task VerifyRegisteredLayoutAsync(Window fixture, ForegroundTarget owned, GlobalHotKeys hotkeys, string? evidenceDirectory)
    {
        if (!Environment.GetCommandLineArgs().Contains("--smoke-test", StringComparer.Ordinal)) throw new InvalidOperationException("Owned layout probe requires exact smoke flag.");
        var preferences = store.Preferences; var history = store.History; var oldEntries = entries;
        var initial = WindowManager.ReadFrame(owned.Handle)!.Value;
        var second = new Window { Title = "Kikicast owned layout fixture", Width = 360, Height = 220, ShowInTaskbar = false, ShowActivated = false, Left = fixture.Left + 50, Top = fixture.Top + 50, Content = "Owned layout geometry only" };
        var screens = LayoutDisplays.Read(); if (screens.Count == 0) throw new InvalidOperationException("Native stable display metadata is unavailable; no display is fabricated.");
        var originalScreen = screens.First(x => x.Area.Id == WindowManager.DisplayForWindow(owned.Handle)?.Id);
        try
        {
            second.Show(); await Task.Delay(100);
            using var process = Process.GetCurrentProcess();
            var secondTarget = new ForegroundTarget(new WindowInteropHelper(second).Handle, (uint)process.Id, process.StartTime);
            ownedLayoutTargets = [owned, secondTarget]; entries = [new("Owned layout application", Environment.ProcessPath!)];
            var p = preferences with { WindowManagementEnabled = true, ApplicationsEnabled = true, ShowWindowLayouts = false, EntryBindings = [] };
            var apps = LayoutWindowInventory.Applications(entries, p); if (apps.Count != 1) throw new InvalidOperationException("Owned layout source was not safely resolved.");
            var snapshot = LayoutWindowInventory.ReadOwned(ownedLayoutTargets, apps[0].MatchKey);
            var captured = LayoutWindowInventory.Capture(snapshot, screens, apps);
            if (captured.Entries.Count != 2 || captured.UsesPreferredGap || captured.Entries.Any(x => !snapshot.Windows.Any(w => WindowLayoutGeometry.Resolve(x, screens.First(s => s.Display.Identity == x.Display.Identity).Area) == w.Frame)))
                throw new InvalidOperationException("Owned native layout capture failed exact gapless round-trip.");
            var destinations = new[] { originalScreen }.Concat(screens.Where(s => s.Area.Id != originalScreen.Area.Id)).ToArray();
            var layout = captured with { Name = "Owned registered layout", Entries = captured.Entries.Select((x, i) => x with { Display = destinations[i % destinations.Length].Display, WidthFraction = i == 0 ? .5 : .28, HeightFraction = .55,
                Anchor = i == 0 ? WindowSizeAnchor.Left : WindowSizeAnchor.BottomRight, OffsetX = 0, OffsetY = 0 }).ToList(), FrontmostEntryId = null };
            p = LauncherBindings.Set(p with { WindowLayouts = [layout], HiddenEntryKeys = [layout.EntryId] }, layout.EntryId, new(BindingKind.Combo, 132, 3));
            await store.SavePreferencesAsync(p); if (await hotkeys.ConfigureAsync(p) is { } error) throw new InvalidOperationException(error);
            if (await windowManager.ExecuteAsync(WindowAction.LeftHalf, owned) is { } prime) throw new InvalidOperationException(prime);
            snapshot = LayoutWindowInventory.ReadOwned(ownedLayoutTargets, apps[0].MatchKey);
            var plan = WindowLayoutPlan.Make(layout, screens, apps, snapshot.Windows, p.WindowGap);
            await OwnedInputAutomation.ChordAsync(fixture, 132, 17, 18);
            var deadline = DateTimeOffset.UtcNow.AddSeconds(4);
            while ((executing || !store.History.Launches.Any(x => x.Path == layout.EntryId)) && DateTimeOffset.UtcNow < deadline) await Task.Delay(25);
            if (executing || !store.History.Launches.Any(x => x.Path == layout.EntryId)) throw new InvalidOperationException("Registered layout did not finish through the shared native route: " + Status.Text);
            foreach (var place in plan.Placements)
            {
                Check(WindowManager.ReadFrame(new nint(place.Handle))!.Value, place.Frame, "registered layout");
                if (WindowManager.DisplayForWindow(new nint(place.Handle))?.Id != screens.First(s => s.Display.Identity == place.DisplayIdentity).Area.Id)
                    throw new InvalidOperationException("Registered layout landed on a different native display.");
            }
            await windowManager.ExecuteAsync(WindowAction.Restore, owned); Check(WindowManager.ReadFrame(owned.Handle)!.Value, initial, "layout preserved prior command Restore");
            var changed = layout with { Entries = layout.Entries.Select(x => x with { WidthFraction = .42, HeightFraction = .62, Anchor = WindowSizeAnchor.TopRight }).ToList() };
            await store.UpdatePreferencesAsync(x => x with { WindowLayouts = [changed] });
            snapshot = LayoutWindowInventory.ReadOwned(ownedLayoutTargets, apps[0].MatchKey); plan = WindowLayoutPlan.Make(changed, screens, apps, snapshot.Windows, p.WindowGap);
            await ExecuteRowAsync(new(layout.Name, layout.Summary, Layout: layout), global: true);
            foreach (var place in plan.Placements) Check(WindowManager.ReadFrame(new nint(place.Handle))!.Value, place.Frame, "stale layout current-definition funnel");
            second.MinWidth = 420; second.MinHeight = 300; await Task.Delay(100);
            var host = screens.First(s => s.Area.Id == WindowManager.DisplayForWindow(secondTarget.Handle)?.Id);
            var small = changed.Entries[0] with { WidthFraction = .1, HeightFraction = .12, Anchor = WindowSizeAnchor.BottomRight, OffsetX = -8, OffsetY = -9 };
            var slot = WindowLayoutGeometry.Resolve(small, host.Area)!.Value;
            var one = new WindowLayoutPlan([new(small.Id, secondTarget.Handle.ToInt64(), slot, host.Area.WorkArea, small.Anchor, host.Display.Identity)], [], null);
            var identities = new Dictionary<long, ForegroundTarget> { [secondTarget.Handle.ToInt64()] = secondTarget };
            var minimum = await windowManager.ApplyLayoutAsync(one, screens, identities);
            if (!minimum.Single().Placed) throw new InvalidOperationException("Native layout minimum-size placement failed: " + minimum[0].Error);
            var actualMinimum = WindowManager.ReadFrame(secondTarget.Handle)!.Value;
            Check(actualMinimum, WindowLayoutGeometry.Reanchor(slot, actualMinimum.Width, actualMinimum.Height, small.Anchor, host.Area.WorkArea), "layout minimum reanchor");
            second.ResizeMode = ResizeMode.NoResize; await Task.Delay(100);
            var fixedBefore = WindowManager.ReadFrame(secondTarget.Handle)!.Value;
            var fixedResult = await windowManager.ApplyLayoutAsync(one, screens, identities);
            if (!fixedResult.Single().Placed) throw new InvalidOperationException("Native fixed-size layout placement failed: " + fixedResult[0].Error);
            Check(WindowManager.ReadFrame(secondTarget.Handle)!.Value, WindowLayoutGeometry.Reanchor(slot, fixedBefore.Width, fixedBefore.Height, small.Anchor, host.Area.WorkArea), "layout fixed-size reanchor");
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            var cancelBefore = ownedLayoutTargets.Select(x => WindowManager.ReadFrame(x.Handle)!.Value).ToArray();
            var cancelled = await windowManager.ApplyLayoutAsync(plan, screens, snapshot.Targets, cancel.Token);
            if (cancelled.Any(x => x.Placed) || !cancelBefore.SequenceEqual(ownedLayoutTargets.Select(x => WindowManager.ReadFrame(x.Handle)!.Value))) throw new InvalidOperationException("Cancelled layout wrote a window.");
            if (evidenceDirectory != null)
            {
                OwnedInputAutomation.Screenshot(fixture, System.IO.Path.Combine(evidenceDirectory, "layout-owned-window.png"));
                await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(evidenceDirectory, "layout-evidence.json"), System.Text.Json.JsonSerializer.Serialize(new
                { nativeDisplayCount = WindowManager.Displays().Count, nativeIdentifiedDisplayCount = screens.Count, twoOwnedWindows = true, exactGaplessCapture = true, registeredCtrlAltF21 = true, hiddenAndDisplayOff = true,
                    currentDefinition = true, priorCommandRestorePreserved = true, nativeMinimumReanchor = true, nativeFixedSizeReanchor = true, cancelledWithoutWrites = true, applicationsLaunched = false, generalApplicationMatchingAcceptance = false,
                    nativeCrossDisplayLayout = screens.Count > 1, nativeDpi = screens.Select(x => x.Area.Scale * 96).ToArray(), nativeMixedDpi = screens.Select(x => x.Area.Scale).Distinct().Count() > 1,
                    scope = "Owned inventory seam/synthetic chord with real native displays; not physical-input/hotplug/third-party matching acceptance; actual DPI stated separately" }));
            }
        }
        finally
        {
            ownedLayoutTargets = null; entries = oldEntries; second.Close();
            await windowManager.ExecuteAsync(WindowAction.Restore, owned); // if a failed probe left a primed restore point
            var reset = new WindowLayoutPlan([new(Guid.NewGuid(), owned.Handle.ToInt64(), initial, originalScreen.Area.WorkArea, WindowSizeAnchor.Center, originalScreen.Display.Identity)], [], null);
            await windowManager.ApplyLayoutAsync(reset, screens, new Dictionary<long, ForegroundTarget> { [owned.Handle.ToInt64()] = owned });
            await store.SavePreferencesAsync(preferences); await store.UpdateHistoryAsync(_ => history);
            if (await hotkeys.ConfigureAsync(preferences) is { } error) throw new InvalidOperationException(error);
        }
        static void Check(Rect actual, Rect expected, string stage)
        { if (Math.Abs(actual.X - expected.X) > 4 || Math.Abs(actual.Y - expected.Y) > 4 || Math.Abs(actual.Width - expected.Width) > 4 || Math.Abs(actual.Height - expected.Height) > 4) throw new InvalidOperationException(stage + " geometry differs: " + actual + " / " + expected); }
    }
}
