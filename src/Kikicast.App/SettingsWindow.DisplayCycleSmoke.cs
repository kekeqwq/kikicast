using System.IO;
using System.Windows;
using Kikicast.Core;

namespace Kikicast.App;

public partial class SettingsWindow
{
    private async Task VerifyDisplayCycleInputAsync(string evidenceDirectory)
    {
        var originalMode = WindowCycle.Resolve(userStore.Preferences);
        var bindings = userStore.Preferences.WindowBindings;
        var items = userStore.Preferences.EntryBindings;
        SelectCategory("Window management"); UpdateLayout(); await Task.Delay(60);
        HalfCycleSelector.BringIntoView();
        if (!HalfCycleSelector.Focus()) throw new InvalidOperationException("Half-cycle selector could not receive owned focus.");
        await OwnedInputAutomation.ChordAsync(this, 115);
        if (!HalfCycleSelector.IsDropDownOpen) throw new InvalidOperationException("Native F4 did not open the half-cycle selector.");
        await OwnedInputAutomation.ChordAsync(this, 27);
        async Task Choose(WindowCycleMode mode)
        {
            HalfCycleSelector.BringIntoView(); HalfCycleSelector.Focus();
            await OwnedInputAutomation.ChordAsync(this, 36);
            for (var i = 0; i < (int)mode; i++) await OwnedInputAutomation.ChordAsync(this, 40);
            if (HalfCycleSelector.SelectedIndex != (int)mode) throw new InvalidOperationException("Native arrows did not select half-cycle mode.");
        }
        async Task SaveNative(bool success = true)
        {
            SaveButton.Focus(); await OwnedInputAutomation.ChordAsync(this, 32);
            var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
            while (saving && DateTimeOffset.UtcNow < deadline) await Task.Delay(25);
            if (saving || success && !Feedback.Text.StartsWith("Saved.", StringComparison.Ordinal)) throw new InvalidOperationException("Half-cycle Save failed: " + Feedback.Text);
        }
        await Choose(WindowCycleMode.Displays);
        var settingsPath = Path.Combine(userStore.DirectoryPath, "settings.json"); var before = File.ReadAllBytes(settingsPath);
        using (var locked = new FileStream(settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await SaveNative(false);
            if (WindowCycle.Resolve(userStore.Preferences) != originalMode || !Feedback.Text.StartsWith("Settings were not saved:", StringComparison.Ordinal))
                throw new InvalidOperationException("Failed half-cycle Save published a mode draft.");
        }
        if (!before.SequenceEqual(File.ReadAllBytes(settingsPath))) throw new InvalidOperationException("Half-cycle failed write changed original bytes.");
        await SaveNative();
        var loaded = new UserStore(userStore.DirectoryPath).Preferences;
        if (loaded.HalfCycleMode != WindowCycleMode.Displays || loaded.CycleHalfSizes) throw new InvalidOperationException("Display mode did not persist exclusively across reload.");
        HalfCycleSelector.BringIntoView(); OwnedInputAutomation.Screenshot(this, Path.Combine(evidenceDirectory, "settings-display-cycle.png"));
        await Choose(WindowCycleMode.Sizes); await SaveNative();
        loaded = new UserStore(userStore.DirectoryPath).Preferences;
        if (loaded.HalfCycleMode != WindowCycleMode.Sizes || !loaded.CycleHalfSizes) throw new InvalidOperationException("Size mode lost legacy boolean compatibility.");
        await Choose(WindowCycleMode.Off); await SaveNative();
        if (WindowCycle.Resolve(userStore.Preferences) != WindowCycleMode.Off || userStore.Preferences.CycleHalfSizes) throw new InvalidOperationException("Off mode did not persist.");
        if (originalMode != WindowCycleMode.Off) { await Choose(originalMode); await SaveNative(); }
        if (!bindings.OrderBy(x => x.Key).SequenceEqual(userStore.Preferences.WindowBindings.OrderBy(x => x.Key))
            || !items.OrderBy(x => x.Key).SequenceEqual(userStore.Preferences.EntryBindings.OrderBy(x => x.Key)))
            throw new InvalidOperationException("Mode settings changed existing binding identities.");
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "settings-display-cycle-evidence.json"),
            "{\"nativeF4EscArrows\":true,\"offSizesDisplays\":true,\"reload\":true,\"legacySizeBoolean\":true,\"lockedWriteRollback\":true,\"bindingsUnchanged\":true,\"scope\":\"Owned synthetic settings input; not physical/DPI acceptance\"}");
    }
}
