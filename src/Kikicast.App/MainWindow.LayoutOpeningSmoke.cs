using System.Diagnostics;
using System.Windows;
using Kikicast.Core;
using Kikicast.Windows;

namespace Kikicast.App;

public partial class MainWindow
{
    internal async Task VerifyMissingLayoutLaunchAsync(Window fixture, GlobalHotKeys hotkeys, string fixturePath, string? evidenceDirectory)
    {
        if (!Environment.GetCommandLineArgs().Contains("--smoke-test", StringComparer.Ordinal)) throw new InvalidOperationException("Missing-app probe requires exact smoke flag.");
        var original = store.Preferences; var history = store.History; var oldEntries = entries;
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KikicastLayoutOpening-" + Guid.NewGuid()); System.IO.Directory.CreateDirectory(root);
        var exe = System.IO.Path.Combine(root, "OwnedLayoutFixture-" + Guid.NewGuid().ToString("N") + ".exe");
        Process? child = null; var children = new List<Process>(); var completed = false;
        try
        {
            System.IO.File.Copy(fixturePath, exe);
            var folder = System.IO.Path.GetDirectoryName(fixturePath)!;
            foreach (var name in new[] { "PortableFixture.dll", "PortableFixture.runtimeconfig.json", "PortableFixture.deps.json" }) System.IO.File.Copy(System.IO.Path.Combine(folder, name), System.IO.Path.Combine(root, name));
            entries = [new("Owned layout opening application", exe)];
            var screen = LayoutDisplays.Read().First(); var entry = new WindowLayoutEntry(Guid.NewGuid(), entries[0].Id, screen.Display)
                { WidthFraction = .46, HeightFraction = .58, Anchor = WindowSizeAnchor.BottomRight };
            var layout = new WindowLayout(Guid.NewGuid(), "Owned missing-app layout") { Entries = [entry], UsesPreferredGap = false };
            var preferences = LauncherBindings.Set(original with { ApplicationsEnabled = true, WindowManagementEnabled = true, WindowLayouts = [layout],
                ShowWindowLayouts = false, HiddenEntryKeys = [layout.EntryId], EntryBindings = [] }, layout.EntryId, new(BindingKind.Combo, 131, 3)); // Ctrl+Alt+F20
            await store.SavePreferencesAsync(preferences);
            var apps = LayoutWindowInventory.Applications(entries, preferences); if (apps.Count != 1) throw new InvalidOperationException("Owned opening EXE source unavailable.");
            await RunLayoutAsync(layout.Id, global: true); // opt-out refuses to launch
            if (LayoutWindowInventory.Read(apps).Windows.Count != 0 || !ReferenceEquals(history, store.History)) throw new InvalidOperationException("Launch-off layout implicitly opened or recorded a visit.");
            layout = layout with { LaunchMissingApplications = true };
            await store.UpdatePreferencesAsync(p => p with { WindowLayouts = [layout] });
            if (await hotkeys.ConfigureAsync(store.Preferences) is { } error) throw new InvalidOperationException(error);
            await OwnedInputAutomation.ClickToActivateAsync(fixture);
            await OwnedInputAutomation.ChordAsync(fixture, 131, 17, 18);
            var end = DateTimeOffset.UtcNow.AddSeconds(8);
            while ((executing || !store.History.Launches.Any(x => x.Path == layout.EntryId)) && DateTimeOffset.UtcNow < end) await Task.Delay(25);
            if (executing || !store.History.Launches.Any(x => x.Path == layout.EntryId)) throw new InvalidOperationException("Registered missing-app launch did not finish: " + Status.Text);
            var native = LayoutWindowInventory.Read(apps); var window = native.Windows.Single();
            var target = native.Targets[window.Handle]; child = Process.GetProcessById((int)target.ProcessId);
            if (child.StartTime != target.Started) throw new InvalidOperationException("Owned launched process cookie changed.");
            var expected = WindowLayoutGeometry.Resolve(entry, screen.Area)!.Value; var actual = WindowManager.ReadFrame(target.Handle)!.Value;
            if (Math.Abs(actual.X - expected.X) > 4 || Math.Abs(actual.Y - expected.Y) > 4 || Math.Abs(actual.Width - expected.Width) > 4 || Math.Abs(actual.Height - expected.Height) > 4)
                throw new InvalidOperationException("Production inventory/new-window geometry differs: " + actual + " / " + expected);
            if (!Status.Text.Contains("1 placed, 1 launched", StringComparison.Ordinal)) throw new InvalidOperationException("Missing-app layout result was not exactly one launch/placement: " + Status.Text);
            var restoreError = await windowManager.ExecuteAsync(WindowAction.Restore, target);
            if (restoreError != "This window has no restore point.") throw new InvalidOperationException("Opening layout unexpectedly created a command Restore ledger: " + restoreError);
            var ownedFile = System.IO.Path.Combine(root, "owned input file.txt"); await System.IO.File.WriteAllTextAsync(ownedFile, "Owned input fixture only");
            var inputs = new[] { new WindowLayoutInput(WindowLayoutInputKind.File) { Value = ownedFile }, new(WindowLayoutInputKind.Uri) { Value = "https://example.invalid/owned?a=b%20c" },
                new(WindowLayoutInputKind.Arguments) { Arguments = ["--owned", "a b", "a\"b\\", "", "; $(not-run) &"] } };
            foreach (var input in inputs)
            {
                var baseline = LayoutWindowInventory.Read(apps); var oldFrames = baseline.Windows.ToDictionary(x => x.Handle, x => x.Frame);
                var changed = layout with { Entries = [entry with { Input = input }] }; await store.UpdatePreferencesAsync(p => p with { WindowLayouts = [changed] });
                await ExecuteRowAsync(new(layout.Name, layout.Summary, Layout: layout), global: true); // stale plain row must execute current typed input
                var after = LayoutWindowInventory.Read(apps); var fresh = after.Windows.Where(x => !oldFrames.ContainsKey(x.Handle)).ToArray();
                if (fresh.Length != 1 || !Status.Text.Contains("1 placed, 1 launched", StringComparison.Ordinal)) throw new InvalidOperationException("Saved input did not create/place exactly one fresh owned window: " + Status.Text);
                foreach (var old in oldFrames) if (WindowManager.ReadFrame(new nint(old.Key)) != old.Value) throw new InvalidOperationException("Saved input stole or moved a baseline window.");
                var opened = after.Targets[fresh[0].Handle]; var instance = Process.GetProcessById((int)opened.ProcessId);
                if (instance.StartTime != opened.Started) { instance.Dispose(); throw new InvalidOperationException("Saved-input process cookie changed."); }
                children.Add(instance);
                var received = System.Text.Json.JsonSerializer.Deserialize<string[]>(await System.IO.File.ReadAllTextAsync(System.IO.Path.Combine(root, "owned-input-received.json")))!;
                var sent = input.Kind == WindowLayoutInputKind.Arguments ? input.Arguments.ToArray() : new[] { input.Value! };
                if (!received.SequenceEqual(sent)) throw new InvalidOperationException("Owned literal input boundaries changed.");
                var placed = WindowManager.ReadFrame(opened.Handle)!.Value;
                if (Math.Abs(placed.X - expected.X) > 4 || Math.Abs(placed.Y - expected.Y) > 4 || Math.Abs(placed.Width - expected.Width) > 4 || Math.Abs(placed.Height - expected.Height) > 4) throw new InvalidOperationException("Saved input window geometry differs.");
            }
            if (evidenceDirectory != null)
                await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(evidenceDirectory, "layout-opening-evidence.json"), System.Text.Json.JsonSerializer.Serialize(new
                { nativeDisplayCount = WindowManager.Displays().Count, registeredCtrlAltF20 = true, optOutNoLaunch = true, oneOwnedExeLaunch = true,
                    originalPathPreserved = true, productionInventory = true, ownedInventorySeam = false, eventDrivenArrival = true, nativePlacement = true,
                    realOwnedFileUriAndArgv = true, exactLiteralValues = true, baselineWindowsUntouched = true, staleRowTypedCurrentDefinition = true, totalOwnedLaunches = 4,
                    noCommandRestoreRecord = true, hiddenAndDisplayOff = true, noUserApplicationsLaunched = true, noProcessTerminated = true,
                    scope = "UUID-named copied owned apphost only; public production desktop metadata/new-window arrival/placement, not arbitrary third-party/package/mixed-DPI/hotplug acceptance" }));
            completed = true;
        }
        finally
        {
            // Fixture shuts itself down after10s. No Kill/Stop/termination or same-name enumeration.
            if (child != null) { try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(12)); } finally { child.Dispose(); } }
            else if (System.IO.File.Exists(exe)) await Task.Delay(11000); // failure cleanup: allow the self-exiting owned fixture to finish
            foreach (var instance in children) { try { await instance.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(12)); } finally { instance.Dispose(); } }
            if (!completed && System.IO.File.Exists(exe)) await Task.Delay(11000); // even an unobserved started fixture self-exits; never terminate it
            await discovery.ForgetAsync(exe); entries = oldEntries;
            await store.SavePreferencesAsync(original); await store.UpdateHistoryAsync(_ => history);
            if (await hotkeys.ConfigureAsync(original) is { } error) throw new InvalidOperationException(error);
            System.IO.Directory.Delete(root, true);
            if (completed) await OwnedInputAutomation.ClickToActivateAsync(fixture);
        }
    }
}
