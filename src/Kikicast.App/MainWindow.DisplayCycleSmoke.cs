using System.Windows;
using Kikicast.Core;
using Kikicast.Windows;

namespace Kikicast.App;

public partial class MainWindow
{
    internal async Task VerifyRegisteredHalfCycleAsync(Window fixture, ForegroundTarget owned, GlobalHotKeys hotkeys, string? evidenceDirectory)
    {
        // Smoke-only explicit owned target. Never change monitor configuration or
        // fabricate physical displays; single-display evidence stays labelled so.
        var preferences = store.Preferences; var originalHistory = store.History;
        var original = WindowManager.ReadFrame(owned.Handle)!.Value;
        var screens = WindowManager.Displays();
        var nativeCount = screens.Count;
        var action = WindowAction.LeftHalf;
        var id = LauncherSections.WindowId(action);
        var chord = new HotKeyBinding(BindingKind.Combo, 133, 3); // Ctrl+Alt+F22
        WindowCycleState? previous = null;
        static void EqualFrame(Kikicast.Core.Rect actual, Kikicast.Core.Rect expected, string context)
        {
            if (Math.Abs(actual.X - expected.X) > 4 || Math.Abs(actual.Y - expected.Y) > 4
                || Math.Abs(actual.Width - expected.Width) > 4 || Math.Abs(actual.Height - expected.Height) > 4)
                throw new InvalidOperationException(context + ": observed=" + actual + ", expected=" + expected);
        }
        try
        {
            var configured = preferences with { WindowManagementEnabled = true, ShowWindowCommands = false,
                HalfCycleMode = WindowCycleMode.Displays, CycleHalfSizes = false, EntryBindings = [],
                WindowBindings = new() { [action] = chord }, HiddenEntryKeys = [id] };
            await store.SavePreferencesAsync(configured);
            if (await hotkeys.ConfigureAsync(configured) is { } error) throw new InvalidOperationException(error);
            async Task Press(WindowCycleMode mode, bool frameUnchanged = true)
            {
                screens = WindowManager.Displays();
                var host = WindowManager.DisplayForWindow(owned.Handle) ?? throw new InvalidOperationException("Owned cycle window lost its display.");
                var before = WindowManager.ReadFrame(owned.Handle)!.Value;
                var decision = WindowCycle.Decide(mode, action, previous, host, screens, frameUnchanged);
                var plan = WindowGeometry.Resolve(action, before, host, screens, configured.WindowGap, decision) ?? throw new InvalidOperationException("Owned half-cycle plan unavailable.");
                var visits = store.History.Launches.FirstOrDefault(x => x.Path == id)?.Count ?? 0;
                await OwnedInputAutomation.ChordAsync(fixture, 133, 17, 18);
                var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
                while ((executing || (store.History.Launches.FirstOrDefault(x => x.Path == id)?.Count ?? 0) <= visits) && DateTimeOffset.UtcNow < deadline) await Task.Delay(25);
                if (executing || (store.History.Launches.FirstOrDefault(x => x.Path == id)?.Count ?? 0) <= visits)
                    throw new InvalidOperationException("Registered half-cycle chord did not reach the shared window execution funnel.");
                EqualFrame(WindowManager.ReadFrame(owned.Handle)!.Value, plan.Frame, "Registered half-cycle native mismatch");
                var actualDisplay = WindowManager.DisplayForWindow(owned.Handle)!.Id;
                previous = WindowCycle.Remember(action, decision, actualDisplay, screens);
            }
            var completedTours = new List<string>();
            var tours = nativeCount is > 1 and <= 4 ? new[] { WindowAction.LeftHalf, WindowAction.RightHalf, WindowAction.TopHalf, WindowAction.BottomHalf } : new[] { WindowAction.LeftHalf };
            foreach (var tour in tours)
            {
                action = tour; id = LauncherSections.WindowId(action); previous = null;
                configured = configured with { WindowBindings = new() { [action] = chord }, HiddenEntryKeys = [id] };
                await store.SavePreferencesAsync(configured);
                if (await hotkeys.ConfigureAsync(configured) is { } tourError) throw new InvalidOperationException(tourError);
                var length = WindowCycle.Length(WindowCycleMode.Displays, action, nativeCount);
                var probes = nativeCount <= 4 ? length + 1 : 3; // at most nine moves per action
                for (var i = 0; i < probes; i++) await Press(WindowCycleMode.Displays);
                if (nativeCount <= 4) completedTours.Add(action.ToString());
                await RunWindowActionAsync(WindowAction.Restore, global: true, capturedWindow: owned);
                EqualFrame(WindowManager.ReadFrame(owned.Handle)!.Value, original, "Tour Restore lost the original placement");
            }
            action = WindowAction.LeftHalf; id = LauncherSections.WindowId(action); previous = null;
            configured = configured with { WindowBindings = new() { [action] = chord }, HiddenEntryKeys = [id] };
            await store.SavePreferencesAsync(configured);
            if (await hotkeys.ConfigureAsync(configured) is { } resetError) throw new InvalidOperationException(resetError);
            await store.UpdatePreferencesAsync(p => p with { HalfCycleMode = WindowCycleMode.Sizes, CycleHalfSizes = true });
            await Press(WindowCycleMode.Sizes); // mode change resets to ½, not a reused display-step third
            await Press(WindowCycleMode.Sizes); // ⅓
            await store.UpdatePreferencesAsync(p => p with { HalfCycleMode = WindowCycleMode.Off, CycleHalfSizes = false });
            await Press(WindowCycleMode.Off);
            var beforeDisabled = WindowManager.ReadFrame(owned.Handle)!.Value; var history = store.History;
            await store.UpdatePreferencesAsync(p => p with { WindowManagementEnabled = false });
            if (await hotkeys.ConfigureAsync(store.Preferences) is { } offError) throw new InvalidOperationException(offError);
            await OwnedInputAutomation.ChordAsync(fixture, 133, 17, 18); await Task.Delay(100);
            if (!ReferenceEquals(history, store.History) || store.Preferences.WindowBindings.GetValueOrDefault(action) != chord)
                throw new InvalidOperationException("Window master-off shortcut executed or erased its binding.");
            EqualFrame(WindowManager.ReadFrame(owned.Handle)!.Value, beforeDisabled, "Disabled cycle shortcut moved a window");
            await store.UpdatePreferencesAsync(p => p with { WindowManagementEnabled = true });
            if (await hotkeys.ConfigureAsync(store.Preferences) is { } onError) throw new InvalidOperationException(onError);
            await RunWindowActionAsync(WindowAction.Restore, global: true, capturedWindow: owned);
            EqualFrame(WindowManager.ReadFrame(owned.Handle)!.Value, original, "Display-cycle Restore lost the original placement");
            // Owned managed movement stands in for drift, not a physical drag.
            await store.UpdatePreferencesAsync(p => p with { HalfCycleMode = WindowCycleMode.Displays });
            previous = null;
            await Press(WindowCycleMode.Displays);
            fixture.Left += 12; await Task.Delay(100);
            var manuallyMoved = WindowManager.ReadFrame(owned.Handle)!.Value;
            await Press(WindowCycleMode.Displays, frameUnchanged: false);
            await RunWindowActionAsync(WindowAction.Restore, global: true, capturedWindow: owned);
            EqualFrame(WindowManager.ReadFrame(owned.Handle)!.Value, manuallyMoved, "Observed drift did not refresh the cycle restore point");
            var manager = new WindowManager();
            if (await manager.ExecuteAsync(action, owned, 0, (WindowCycleMode)90) == null || await manager.ExecuteAsync(action, null, 0, WindowCycleMode.Displays) == null)
                throw new InvalidOperationException("Invalid mode/missing target accepted a cycle action.");
            EqualFrame(WindowManager.ReadFrame(owned.Handle)!.Value, manuallyMoved, "Invalid cycle action moved a window");
            if (evidenceDirectory != null) await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(evidenceDirectory, "display-cycle-evidence.json"),
                System.Text.Json.JsonSerializer.Serialize(new { nativeDisplayCount = nativeCount, registeredCtrlAltF22 = true,
                    hiddenAndDisplayOff = true, modeChangeReset = true, sizeCycleRetained = true, masterGate = true, bindingRetained = true,
                    restore = true, observedDriftReset = true, refreshedRestorePoint = true, nativeSingleDisplayNoFlip = nativeCount == 1, nativeFullCrossDisplayTour = nativeCount is > 1 and <= 4,
                    nativeCompleteTourActions = completedTours, nativeDpi = screens.Select(x => x.Scale * 96).ToArray(), nativeMixedDpi = screens.Select(x => x.Scale).Distinct().Count() > 1,
                    generatedCoreMultiDisplayTestsSeparate = true, scope = "Owned native windows and real display topology/synthetic chord; not physical-input/hotplug acceptance; DPI scope stated explicitly" }));
        }
        finally
        {
            await store.SavePreferencesAsync(preferences);
            await store.UpdateHistoryAsync(_ => originalHistory);
            if (await hotkeys.ConfigureAsync(preferences) is { } restoreError) throw new InvalidOperationException(restoreError);
        }
    }
}
