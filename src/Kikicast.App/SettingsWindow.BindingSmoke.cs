using System.IO;
using System.Windows;
using Kikicast.Core;

namespace Kikicast.App;

public partial class SettingsWindow
{
    private async Task VerifyItemBindingInputAsync(string evidenceDirectory)
    {
        SelectCategory("Launcher"); LauncherFilter.Text = "Calculation history"; UpdateLayout(); await Task.Delay(60);
        LauncherItems.SelectedItem = LauncherItems.Items.Cast<LauncherSettingsItem>().Single(x => x.Id == "cmd:history");
        UpdateLayout();
        var row = LauncherBindingEditor.DataContext as BindingRow ?? throw new InvalidOperationException("Item recorder is not attached to the selected stable identity.");
        var originalBinding = row.Binding;
        ItemBindingButton.BringIntoView(); ItemBindingButton.Focus(); await OwnedInputAutomation.ChordAsync(this, 32);
        if (recordingRow != row) throw new InvalidOperationException("Native Space did not start the item recorder.");
        await OwnedInputAutomation.ChordAsync(this, 135, 17, 18); // Ctrl+Alt+F24, never executes while settings is open
        if (recordingRow != null || row.Binding != new HotKeyBinding(BindingKind.Combo, 135, 3))
            throw new InvalidOperationException("Native item recorder did not capture Ctrl+Alt+F24: " + Feedback.Text);
        async Task SaveNative()
        {
            SaveButton.Focus(); await OwnedInputAutomation.ChordAsync(this, 32);
            var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
            while (saving && DateTimeOffset.UtcNow < deadline) await Task.Delay(25);
            if (saving) throw new InvalidOperationException("Item shortcut Save exceeded its bounded deadline.");
        }
        var settingsPath = Path.Combine(userStore.DirectoryPath, "settings.json");
        var before = File.ReadAllBytes(settingsPath);
        using (var locked = new FileStream(settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await SaveNative();
            if (LauncherBindings.Get(userStore.Preferences, "cmd:history") != originalBinding || !Feedback.Text.StartsWith("Settings were not saved:", StringComparison.Ordinal))
                throw new InvalidOperationException("Item shortcut storage failure published its draft or lost rollback feedback: " + Feedback.Text);
        }
        if (!before.SequenceEqual(File.ReadAllBytes(settingsPath))) throw new InvalidOperationException("Failed shortcut Save modified the prior settings file.");
        await SaveNative();
        if (LauncherBindings.Get(userStore.Preferences, "cmd:history") != row.Binding) throw new InvalidOperationException("Native shortcut Save did not persist the item binding.");
        var reloaded = new UserStore(userStore.DirectoryPath);
        if (LauncherBindings.Get(reloaded.Preferences, "cmd:history") != row.Binding) throw new InvalidOperationException("Saved shortcut did not survive settings reload.");
        ItemBindingButton.BringIntoView(); OwnedInputAutomation.Screenshot(this, Path.Combine(evidenceDirectory, "settings-item-shortcut-saved.png"));
        // A conflicting default double Ctrl must be refused, never overwrite the palette.
        ItemBindingButton.Focus(); await OwnedInputAutomation.ChordAsync(this, 32);
        await OwnedInputAutomation.ChordAsync(this, 17);
        await OwnedInputAutomation.ChordAsync(this, 17);
        if (recordingRow != row || row.Binding != new HotKeyBinding(BindingKind.Combo, 135, 3) || !Feedback.Text.Contains("conflict", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Item recorder accepted the default palette conflict.");
        StopRecording(); ItemClearBindingButton.BringIntoView(); ItemClearBindingButton.Focus(); await OwnedInputAutomation.ChordAsync(this, 32);
        LauncherItems.SelectedIndex = -1; LauncherItems.SelectedItem = LauncherItems.Items.Cast<LauncherSettingsItem>().Single(x => x.Id == "cmd:history");
        if ((LauncherBindingEditor.DataContext as BindingRow)?.Binding != null || Draft().EntryBindings.ContainsKey("cmd:history"))
            throw new InvalidOperationException("Cleared shortcut draft resurrected when reselecting the item.");
        row.Binding = originalBinding; itemBindingRows["cmd:history"] = row;
        await SaveNative();
        if (LauncherBindings.Get(userStore.Preferences, "cmd:history") != originalBinding) throw new InvalidOperationException("Item shortcut did not restore its original persistence state.");
        LauncherFilter.Clear(); RefreshLauncherItems();
    }
}
