using System.IO;
using System.Windows;
using Kikicast.Core;

namespace Kikicast.App;

public partial class SettingsWindow
{
    private async Task VerifyCustomSizeInputAsync(string evidenceDirectory)
    {
        var originalMaster = WindowManagement.IsChecked; var originalDisplay = ShowWindows.IsChecked;
        SelectCategory("Window management"); UpdateLayout(); await Task.Delay(60);
        NewSizeButton.BringIntoView(); NewSizeButton.Focus(); await OwnedInputAutomation.ChordAsync(this, 32);
        async Task Type(System.Windows.Controls.TextBox box, string value)
        {
            UpdateLayout(); box.BringIntoView(); await Task.Delay(60);
            if (!box.Focus()) throw new InvalidOperationException($"Custom-size text editor could not receive focus: name={box.Name},visible={box.IsVisible},enabled={box.IsEnabled},saving={saving},category={Categories.SelectedIndex}.");
            await OwnedInputAutomation.ChordAsync(this, 65, 17); await OwnedInputAutomation.TextAsync(this, value);
        }
        await Type(SizeName, "Owned input size"); await Type(SizeWidth, "54"); await Type(SizeOffsetX, "12");
        SizeWidthUnit.Focus(); await OwnedInputAutomation.ChordAsync(this, 36);
        if (widthUnit != WindowSizeUnit.Dip || !int.TryParse(SizeWidth.Text, out var converted) || converted <= 100)
            throw new InvalidOperationException("Native custom-size unit change did not convert percentage to DIP.");
        await OwnedInputAutomation.ChordAsync(this, 35);
        if (widthUnit != WindowSizeUnit.Percent || SizeWidth.Text != "54") throw new InvalidOperationException("Custom-size unit round-trip lost the authored fraction.");
        SizeAnchor.Focus(); await OwnedInputAutomation.ChordAsync(this, 36); await OwnedInputAutomation.ChordAsync(this, 40);
        if (SizeAnchor.SelectedIndex != 1) throw new InvalidOperationException("Native custom-size anchor selector did not receive arrows.");
        KeepSizeButton.BringIntoView(); KeepSizeButton.Focus(); await OwnedInputAutomation.ChordAsync(this, 32);
        var size = customSizes.Single(x => x.Id == sizeId);
        if (size.Width.Value != 54 || size.Anchor != WindowSizeAnchor.Top || size.OffsetX != 12 || userStore.Preferences.CustomWindowSizes.Any(x => x.Id == size.Id))
            throw new InvalidOperationException("Native custom editor did not keep a separate unapplied draft.");
        WindowManagement.BringIntoView();
        if (WindowManagement.IsChecked != true) { WindowManagement.Focus(); await OwnedInputAutomation.ChordAsync(this, 32); }
        if (ShowWindows.IsChecked != false) { ShowWindows.Focus(); await OwnedInputAutomation.ChordAsync(this, 32); }
        SelectCategory("Launcher"); LauncherFilter.Text = "Owned input size"; UpdateLayout();
        LauncherItems.SelectedItem = LauncherItems.Items.Cast<LauncherSettingsItem>().Single(x => x.Id == size.EntryId);
        UpdateLayout(); ItemBindingButton.BringIntoView(); ItemBindingButton.Focus(); await OwnedInputAutomation.ChordAsync(this, 32);
        await OwnedInputAutomation.ChordAsync(this, 135, 17, 18);
        var chord = new HotKeyBinding(BindingKind.Combo, 135, 3);
        if (recordingRow != null || Draft().EntryBindings.GetValueOrDefault(size.EntryId) != chord)
            throw new InvalidOperationException("Draft custom size could not use the shared item shortcut editor.");
        async Task SaveNative(bool expectedSuccess = true)
        {
            SaveButton.Focus(); await OwnedInputAutomation.ChordAsync(this, 32);
            var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
            while (saving && DateTimeOffset.UtcNow < deadline) await Task.Delay(25);
            if (saving || expectedSuccess && !Feedback.Text.StartsWith("Saved.", StringComparison.Ordinal))
                throw new InvalidOperationException("Custom-size native Save failed: " + Feedback.Text);
        }
        var settingsPath = Path.Combine(userStore.DirectoryPath, "settings.json"); var before = File.ReadAllBytes(settingsPath);
        using (var locked = new FileStream(settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await SaveNative(false);
            if (userStore.Preferences.CustomWindowSizes.Any(x => x.Id == size.Id) || LauncherBindings.Get(userStore.Preferences, size.EntryId) != null
                || !Feedback.Text.StartsWith("Settings were not saved:", StringComparison.Ordinal))
                throw new InvalidOperationException("Locked custom-size Save published definition or binding.");
        }
        if (!before.SequenceEqual(File.ReadAllBytes(settingsPath))) throw new InvalidOperationException("Failed custom-size Save changed the original settings bytes.");
        await SaveNative();
        var loaded = new UserStore(userStore.DirectoryPath).Preferences;
        if (loaded.CustomWindowSizes.Single(x => x.Id == size.Id) != size || LauncherBindings.Get(loaded, size.EntryId) != chord || loaded.ShowWindowCommands || !loaded.WindowManagementEnabled)
            throw new InvalidOperationException("Custom size, shortcut or display/master gates failed persistence round-trip.");
        SelectCategory("Window management"); CustomSizeList.SelectedItem = size;
        await Type(SizeName, "Owned renamed size"); KeepSizeButton.BringIntoView(); KeepSizeButton.Focus(); await OwnedInputAutomation.ChordAsync(this, 32);
        await SaveNative();
        var renamed = userStore.Preferences.CustomWindowSizes.Single(x => x.Id == size.Id);
        if (renamed.Name != "Owned renamed size" || renamed.EntryId != size.EntryId || LauncherBindings.Get(userStore.Preferences, size.EntryId) != chord)
            throw new InvalidOperationException("Rename changed custom-size identity or lost its binding.");
        SizeEnabled.BringIntoView(); SizeEnabled.Focus(); await OwnedInputAutomation.ChordAsync(this, 32);
        KeepSizeButton.Focus(); await OwnedInputAutomation.ChordAsync(this, 32); await SaveNative();
        loaded = new UserStore(userStore.DirectoryPath).Preferences;
        if (loaded.CustomWindowSizes.Single(x => x.Id == size.Id).Enabled || LauncherBindings.Get(loaded, size.EntryId) != chord)
            throw new InvalidOperationException("Disabled custom size did not retain its binding across reload.");
        SizeName.BringIntoView(); OwnedInputAutomation.Screenshot(this, Path.Combine(evidenceDirectory, "settings-custom-size-saved.png"));
        await userStore.UpdatePreferencesAsync(p => LauncherCustomization.Visibility(LauncherCustomization.Alias(p with { FavoriteKeys = p.FavoriteKeys.Append(size.EntryId).ToList() }, size.EntryId, "owned-alias"), size.EntryId, false));
        DeleteSizeButton.BringIntoView(); DeleteSizeButton.Focus(); await OwnedInputAutomation.ChordAsync(this, 32);
        if (customSizes.Any(x => x.Id == size.Id) || !userStore.Preferences.CustomWindowSizes.Any(x => x.Id == size.Id))
            throw new InvalidOperationException("Custom-size deletion was not a separate draft.");
        WindowManagement.BringIntoView();
        if (WindowManagement.IsChecked != originalMaster) { WindowManagement.Focus(); await OwnedInputAutomation.ChordAsync(this, 32); }
        if (ShowWindows.IsChecked != originalDisplay) { ShowWindows.Focus(); await OwnedInputAutomation.ChordAsync(this, 32); }
        await SaveNative();
        loaded = new UserStore(userStore.DirectoryPath).Preferences;
        if (loaded.CustomWindowSizes.Any(x => x.Id == size.Id) || LauncherBindings.Get(loaded, size.EntryId) != null
            || loaded.FavoriteKeys.Contains(size.EntryId) || loaded.HiddenEntryKeys.Contains(size.EntryId) || loaded.LauncherAliases.ContainsKey(size.EntryId))
            throw new InvalidOperationException("Explicit custom-size deletion left persistent references.");
        LauncherFilter.Clear(); RefreshLauncherItems();
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "settings-custom-size-evidence.json"),
            "{\"nativeTextEditors\":true,\"unitConversion\":true,\"anchorArrows\":true,\"separateDraft\":true,\"shortcutEditor\":true,\"lockedWriteRollback\":true,\"reload\":true,\"renameStableIdentity\":true,\"disabledBindingRetained\":true,\"deleteReferenceCleanup\":true,\"scope\":\"Owned synthetic input; not physical/DPI acceptance\"}");
    }
}
