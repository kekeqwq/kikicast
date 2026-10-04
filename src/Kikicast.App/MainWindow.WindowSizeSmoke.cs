using System.Windows;
using System.Windows.Interop;
using Kikicast.Core;
using Kikicast.Windows;

namespace Kikicast.App;

public partial class MainWindow
{
    internal async Task VerifyCustomWindowSizeModelsAsync(string? evidenceDirectory = null)
    {
        var preferences = store.Preferences; var oldEntries = entries;
        var size = new CustomWindowSize(Guid.NewGuid(), "Owned Reading Size");
        try
        {
            entries = [];
            await store.SavePreferencesAsync(LauncherBindings.Set(preferences with
            {
                WindowManagementEnabled = true, ShowWindowCommands = true, CustomWindowSizes = [size], EntryBindings = [],
                HiddenEntryKeys = [], FavoriteKeys = [size.EntryId], LauncherAliases = new() { [size.EntryId] = "read-box" }
            }, size.EntryId, new(BindingKind.Combo, 135, 3)));
            Query.Text = "read-box"; Refresh();
            if (Results.Items.Cast<Row>().First().WindowSize?.Id != size.Id || !SettingsItems.Any(x => x.Id == size.EntryId && x.Kind == LauncherKind.WindowCommand))
                throw new InvalidOperationException("Custom size lost search alias/settings identity.");
            if (IsVisible && evidenceDirectory != null) SavePaletteEvidence(System.IO.Path.Combine(evidenceDirectory, "custom-size-search.png"));
            Query.Clear(); Refresh();
            if (!Results.Items.Cast<Row>().Any(x => x.WindowSize?.Id == size.Id && x.Section == "Favorites" && x.GlobalChord != null))
                throw new InvalidOperationException("Custom size favorite/chord did not share the window section.");
            var binding = BindingCatalog.Build(store.Preferences).Single(x => x.EntryId == size.EntryId);
            await store.UpdatePreferencesAsync(p => p with { ShowWindowCommands = false }); Refresh();
            if (Results.Items.Cast<Row>().Any(x => Kind(x) == LauncherKind.WindowCommand) || ResolveBindingRow(size.EntryId) == null)
                throw new InvalidOperationException("Window display switch disabled execution or left window rows visible.");
            await store.UpdatePreferencesAsync(p => p with { ShowWindowCommands = true, HiddenEntryKeys = [size.EntryId] }); Refresh();
            if (Results.Items.Cast<Row>().Any(x => x.WindowSize != null) || ResolveBindingRow(size.EntryId) == null)
                throw new InvalidOperationException("Hidden custom size lost binding availability.");
            var history = store.History;
            await store.UpdatePreferencesAsync(p => p with { CustomWindowSizes = [size with { Enabled = false }] });
            await RunEntryBindingAsync(binding);
            await ExecuteRowAsync(new(size.Name, size.Summary, WindowSize: size));
            if (ResolveBindingRow(size.EntryId) != null || !ReferenceEquals(history, store.History) || LauncherBindings.Get(store.Preferences, size.EntryId) == null)
                throw new InvalidOperationException("Disabled size executed or erased its dormant binding.");
            await store.UpdatePreferencesAsync(p => p with { WindowManagementEnabled = false, CustomWindowSizes = [size] });
            await RunEntryBindingAsync(binding); Refresh();
            if (!ReferenceEquals(history, store.History) || Results.Items.Cast<Row>().Any(x => x.WindowSize != null))
                throw new InvalidOperationException("Window master gate failed for custom sizes.");
            await store.UpdatePreferencesAsync(p => p with { WindowManagementEnabled = true, CustomWindowSizes = [] });
            await RunEntryBindingAsync(binding);
            if (!ReferenceEquals(history, store.History) || !SettingsItems.Any(x => x.Id == size.EntryId && x.Unavailable))
                throw new InvalidOperationException("Missing size binding failed recovery/refusal.");
        }
        finally { entries = oldEntries; await store.SavePreferencesAsync(preferences); Query.Clear(); Refresh(); if (IsVisible) Query.Focus(); }
    }

    internal async Task VerifyRegisteredCustomSizeAsync(Window fixture, ForegroundTarget owned, GlobalHotKeys hotkeys, string? evidenceDirectory)
    {
        var preferences = store.Preferences;
        var size = new CustomWindowSize(Guid.NewGuid(), "Owned registered size") { Width = new(62), Height = new(58), Anchor = WindowSizeAnchor.BottomRight };
        var chord = new HotKeyBinding(BindingKind.Combo, 135, 3);
        try
        {
            var configured = LauncherBindings.Set(preferences with { WindowManagementEnabled = true, ShowWindowCommands = false, CustomWindowSizes = [size], EntryBindings = [], HiddenEntryKeys = [size.EntryId] }, size.EntryId, chord);
            await store.SavePreferencesAsync(configured);
            if (await hotkeys.ConfigureAsync(configured) is { } error) throw new InvalidOperationException(error);
            var host = WindowManager.DisplayForWindow(owned.Handle)!;
            var before = WindowManager.ReadFrame(owned.Handle)!.Value;
            await OwnedInputAutomation.ChordAsync(fixture, 135, 17, 18);
            var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
            while ((executing || !store.History.Launches.Any(x => x.Path == size.EntryId)) && DateTimeOffset.UtcNow < deadline) await Task.Delay(25);
            var expected = size.Frame(host, configured.WindowGap)!.Value;
            var actual = WindowManager.ReadFrame(owned.Handle)!.Value;
            if (executing || Math.Abs(actual.X - expected.X) > 4 || Math.Abs(actual.Y - expected.Y) > 4
                || Math.Abs(actual.Width - expected.Width) > 4 || Math.Abs(actual.Height - expected.Height) > 4)
                throw new InvalidOperationException("Registered hidden custom-size chord failed its owned native route.");
            await RunWindowCommandAsync(WindowAction.Restore, null, true, owned);
            actual = WindowManager.ReadFrame(owned.Handle)!.Value;
            if (Math.Abs(actual.X - before.X) > 4 || Math.Abs(actual.Y - before.Y) > 4 || Math.Abs(actual.Width - before.Width) > 4 || Math.Abs(actual.Height - before.Height) > 4)
                throw new InvalidOperationException("Registered custom-size route lost its shared Restore point.");
            if (evidenceDirectory != null) await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(evidenceDirectory, "custom-size-registered-evidence.json"),
                "{\"registeredCtrlAltF24\":true,\"hiddenAndDisplayOff\":true,\"sharedRestore\":true,\"scope\":\"Owned synthetic chord/native window; not physical acceptance\"}");
        }
        finally
        {
            await store.SavePreferencesAsync(preferences);
            if (await hotkeys.ConfigureAsync(preferences) is { } restoreError) throw new InvalidOperationException(restoreError);
        }
    }

    internal async Task VerifyCustomWindowSizeNativeAsync(Window fixture, ForegroundTarget owned, string? evidenceDirectory)
    {
        var manager = new WindowManager();
        var hwnd = new WindowInteropHelper(fixture).Handle;
        var host = WindowManager.DisplayForWindow(hwnd) ?? throw new InvalidOperationException("Custom-size fixture display unavailable.");
        var original = WindowManager.ReadFrame(hwnd)!.Value;
        static void EqualFrame(Kikicast.Core.Rect actual, Kikicast.Core.Rect expected, string context)
        {
            if (Math.Abs(actual.X - expected.X) > 4 || Math.Abs(actual.Y - expected.Y) > 4
                || Math.Abs(actual.Width - expected.Width) > 4 || Math.Abs(actual.Height - expected.Height) > 4)
                throw new InvalidOperationException(context + ": observed=" + actual + ", requested=" + expected);
        }
        async Task Apply(CustomWindowSize size, double gap = 10)
        {
            if (await manager.ExecuteAsync(size, owned, gap) is { } error) throw new InvalidOperationException(error);
            EqualFrame(WindowManager.ReadFrame(hwnd)!.Value, size.Frame(host, gap)!.Value, "Custom-size native placement mismatch");
        }
        var size = new CustomWindowSize(Guid.NewGuid(), "Owned Native Size") { Width = new(55), Height = new(260, WindowSizeUnit.Dip), OffsetX = 12, OffsetY = -8 };
        foreach (var anchor in Enum.GetValues<WindowSizeAnchor>()) await Apply(size with { Anchor = anchor });
        await Apply(size); await Apply(size); // idempotent; restore retains the first original
        if (await manager.ExecuteAsync(WindowAction.Restore, owned) is { } restoreError) throw new InvalidOperationException(restoreError);
        EqualFrame(WindowManager.ReadFrame(hwnd)!.Value, original, "Custom-size Restore mismatch");
        fixture.WindowState = WindowState.Maximized; await Task.Delay(150);
        await Apply(size);
        if (await manager.ExecuteAsync(WindowAction.Restore, owned) is { } maxError) throw new InvalidOperationException(maxError);
        await Task.Delay(150);
        if (fixture.WindowState != WindowState.Maximized) throw new InvalidOperationException("Custom size did not restore the original maximized state.");
        fixture.WindowState = WindowState.Normal; await Task.Delay(150);
        fixture.MinWidth = 420; fixture.MinHeight = 300;
        var minimum = size with { Width = new(100, WindowSizeUnit.Dip), Height = new(100, WindowSizeUnit.Dip), Anchor = WindowSizeAnchor.BottomRight, OffsetX = 0, OffsetY = 0 };
        if (await manager.ExecuteAsync(minimum, owned, 10) is { } minError) throw new InvalidOperationException(minError);
        var actual = WindowManager.ReadFrame(hwnd)!.Value;
        EqualFrame(actual, minimum.Reanchor(host, 10, actual.Width, actual.Height), "Minimum-size anchor mismatch");
        if (await manager.ExecuteAsync(WindowAction.Restore, owned) is { } minRestore) throw new InvalidOperationException(minRestore);
        fixture.MinWidth = fixture.MinHeight = 0; fixture.ResizeMode = ResizeMode.NoResize; await Task.Delay(100);
        var fixedBefore = WindowManager.ReadFrame(hwnd)!.Value;
        if (await manager.ExecuteAsync(minimum, owned, 10) is { } fixedError) throw new InvalidOperationException(fixedError);
        EqualFrame(WindowManager.ReadFrame(hwnd)!.Value, minimum.Reanchor(host, 10, fixedBefore.Width, fixedBefore.Height), "Fixed-size anchor mismatch");
        if (await manager.ExecuteAsync(WindowAction.Restore, owned) is { } fixedRestore) throw new InvalidOperationException(fixedRestore);
        fixture.ResizeMode = ResizeMode.CanResize; await Task.Delay(100);
        await Apply(size);
        if (await manager.ExecuteAsync(WindowAction.LeftHalf, owned, cycleHalfSizes: true) is { } cycleError) throw new InvalidOperationException(cycleError);
        EqualFrame(WindowManager.ReadFrame(hwnd)!.Value, WindowGeometry.Place(WindowAction.LeftHalf, original, host, WindowManager.Displays())!.Value, "Custom size incorrectly advanced half cycling");
        if (await manager.ExecuteAsync(WindowAction.Restore, owned) is { } cycleRestore) throw new InvalidOperationException(cycleRestore);
        var beforeFailure = WindowManager.ReadFrame(hwnd)!.Value;
        if (await manager.ExecuteAsync(size, null) == null || await manager.ExecuteAsync(size with { Enabled = false }, owned) == null
            || await manager.ExecuteAsync(size with { Width = null! }, owned) == null) throw new InvalidOperationException("Invalid/disabled custom placement was accepted.");
        EqualFrame(WindowManager.ReadFrame(hwnd)!.Value, beforeFailure, "Failed custom action moved a window");
        // The production shared row funnel resolves the current definition, not old row dimensions.
        var preferences = store.Preferences;
        try
        {
            var current = size with { Width = new(65), Anchor = WindowSizeAnchor.Right };
            await store.SavePreferencesAsync(LauncherBindings.Set(preferences with { WindowManagementEnabled = true, ShowWindowCommands = false, CustomWindowSizes = [current], EntryBindings = [], HiddenEntryKeys = [current.EntryId] }, current.EntryId, new(BindingKind.Combo, 135, 3)));
            var binding = BindingCatalog.Build(store.Preferences).Single(x => x.EntryId == current.EntryId);
            await RunEntryBindingAsync(binding, owned);
            EqualFrame(WindowManager.ReadFrame(hwnd)!.Value, current.Frame(host, store.Preferences.WindowGap)!.Value, "Hidden globally bound size did not use shared execution");
            var visit = store.History.Launches.Single(x => x.Path == current.EntryId);
            if (visit.SearchTerms is { Count: > 0 }) throw new InvalidOperationException("Global custom size learned a transient palette query.");
            if (evidenceDirectory != null) OwnedInputAutomation.Screenshot(fixture, System.IO.Path.Combine(evidenceDirectory, "custom-size-owned-window.png"));
            if (await windowManager.ExecuteAsync(WindowAction.Restore, owned) is { } funnelRestore) throw new InvalidOperationException(funnelRestore);
            await ExecuteRowAsync(new(size.Name, size.Summary, WindowSize: size), global: true, capturedWindow: owned);
            EqualFrame(WindowManager.ReadFrame(hwnd)!.Value, current.Frame(host, store.Preferences.WindowGap)!.Value, "Stale size row did not re-resolve current dimensions");
            if (await windowManager.ExecuteAsync(WindowAction.Restore, owned) is { } rowRestore) throw new InvalidOperationException(rowRestore);
        }
        finally { await store.SavePreferencesAsync(preferences); }
        if (evidenceDirectory != null) await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(evidenceDirectory, "custom-size-evidence.json"),
            "{\"nineAnchors\":true,\"mixedDipPercent\":true,\"idempotent\":true,\"restore\":true,\"maximizedRestore\":true,\"minimumSizeReanchor\":true,\"fixedSizeReanchor\":true,\"halfCycleReset\":true,\"hiddenGlobalFunnel\":true,\"staleDefinitionResolved\":true,\"scope\":\"Owned native windows; not mixed-DPI/physical acceptance\"}");
    }
}
