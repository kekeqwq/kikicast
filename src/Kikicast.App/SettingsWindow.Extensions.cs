using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CheckBox = System.Windows.Controls.CheckBox;
using Kikicast.Core;
using Kikicast.Windows;

namespace Kikicast.App;

public partial class SettingsWindow
{
    private readonly ExtensionStore? extensions;
    private bool extensionBusy;
    private InstalledExtension? editingExtension;
    private Guid extensionFolderId = Guid.NewGuid();
    private readonly Dictionary<string, ExtensionConfiguration> extensionDrafts = [];
    private void RefreshExtensions()
    {
        InstalledExtensions.ItemsSource = extensions?.Current ?? [];
        if (extensions?.Warning != null) ExtensionState.Text = extensions.Warning;
    }
    private ExtensionConfiguration CaptureExtensionDraft()
    {
        var initial = extensionDrafts[editingExtension!.Manifest.Id];
        return initial with { Enabled = ExtensionEnabled.IsChecked == true, Options = ExtensionOptions.Children.OfType<CheckBox>().ToDictionary(x => (string)x.Tag, x => x.IsChecked == true) };
    }
    private void ExtensionSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ExtensionOptions == null) return;
        if (editingExtension != null && extensionDrafts.ContainsKey(editingExtension.Manifest.Id)) extensionDrafts[editingExtension.Manifest.Id] = CaptureExtensionDraft();
        editingExtension = InstalledExtensions.SelectedItem as InstalledExtension;
        ExtensionOptions.Children.Clear(); ExtensionEditor.IsEnabled = editingExtension != null;
        if (editingExtension == null) return;
        var manifest = editingExtension.Manifest; var config = extensionDrafts.GetValueOrDefault(manifest.Id) ?? editingExtension.Configuration; extensionDrafts[manifest.Id] = config;
        ExtensionTitle.Text = manifest.Name + " · " + manifest.Version; ExtensionEnabled.IsChecked = config.Enabled;
        foreach (var option in manifest.Booleans) ExtensionOptions.Children.Add(new CheckBox { Content = option.Label, Tag = option.Key, IsChecked = config.Options[option.Key], Margin = new Thickness(0, 6, 0, 0) });
        ExtensionFoldersPanel.Visibility = manifest.HasFolders ? Visibility.Visible : Visibility.Collapsed;
        ExtensionFolders.ItemsSource = config.Folders; NewExtensionFolder(this, new()); RefreshExtensionState(this, new());
    }
    private void ExtensionFolderSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ExtensionFolderName == null || ExtensionFolders.SelectedItem is not ExtensionFolder folder) return;
        extensionFolderId = folder.Id; ExtensionFolderName.Text = folder.Name; ExtensionFolderPath.Text = folder.Path; ExtensionFolderEnabled.IsChecked = folder.Enabled;
    }
    private void NewExtensionFolder(object sender, RoutedEventArgs e)
    { ExtensionFolders.SelectedIndex = -1; extensionFolderId = Guid.NewGuid(); ExtensionFolderName.Clear(); ExtensionFolderPath.Clear(); ExtensionFolderEnabled.IsChecked = true; }
    private void KeepExtensionFolder(object sender, RoutedEventArgs e)
    {
        if (editingExtension == null) return; var draft = CaptureExtensionDraft();
        var next = draft.Folders.Where(x => x.Id != extensionFolderId).Append(new ExtensionFolder(extensionFolderId, ExtensionFolderName.Text.Trim(), ExtensionFolderPath.Text.Trim(), ExtensionFolderEnabled.IsChecked == true)).ToList();
        draft = draft with { Folders = next }; if (draft.Validate(editingExtension.Manifest) is { } error) { ExtensionState.Text = error; return; }
        extensionDrafts[editingExtension.Manifest.Id] = draft; ExtensionFolders.ItemsSource = next; ExtensionFolders.SelectedItem = next.Last(); ExtensionState.Text = "Folder draft kept; Save extension settings to persist. No wallpaper operation ran.";
    }
    private void RemoveExtensionFolder(object sender, RoutedEventArgs e)
    {
        if (editingExtension == null || ExtensionFolders.SelectedItem is not ExtensionFolder folder) return;
        var draft = CaptureExtensionDraft() with { Folders = extensionDrafts[editingExtension.Manifest.Id].Folders.Where(x => x.Id != folder.Id).ToList() };
        extensionDrafts[editingExtension.Manifest.Id] = draft; ExtensionFolders.ItemsSource = draft.Folders; NewExtensionFolder(sender, e);
    }
    private async Task ExtensionOperation(Func<Task> action)
    {
        if (extensions == null || extensionBusy || saving || launcherSaving || commandsSaving || layoutBusy) return;
        extensionBusy = true; ExtensionsPage.IsEnabled = SaveButton.IsEnabled = false;
        try { await action(); refreshLauncher(); }
        catch (Exception ex) { ExtensionState.Text = "Extension operation failed: " + ex.Message; }
        finally { extensionBusy = false; ExtensionsPage.IsEnabled = SaveButton.IsEnabled = true; }
    }
    private async void SaveExtensionConfiguration(object sender, RoutedEventArgs e)
    {
        if (editingExtension == null) return; var id = editingExtension.Manifest.Id; var draft = CaptureExtensionDraft();
        await ExtensionOperation(async () => { await extensions!.ConfigureAsync(id, draft); extensionDrafts[id] = draft; RefreshExtensions(); InstalledExtensions.SelectedItem = extensions.Current.Single(x => x.Manifest.Id == id); ExtensionState.Text = "Extension settings saved. No command was executed."; });
    }
    private bool ConfirmCode() => System.Windows.MessageBox.Show(this, "Install/update unsigned executable extension code from this package?\n\nIt runs with your Windows user permissions when invoked. Declared capabilities and checksums are not enforced isolation or a trusted signature. No commands run during installation.", "Install Windows/.NET extension", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
    private async void ImportExtension(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFileDialog { Filter = "Kikicast extension|*.kikicast", CheckFileExists = true };
        if (picker.ShowDialog(this) != true || !ConfirmCode()) return;
        await ExtensionOperation(async () => { await extensions!.InstallAsync(picker.FileName); RefreshExtensions(); ExtensionState.Text = "Package installed. Configure/enable it explicitly; installation did not run it."; });
    }
    private async void RefreshExtensionCatalog(object sender, RoutedEventArgs e) => await ExtensionOperation(async () => { ExtensionCatalogItems.ItemsSource = await ExtensionCatalog.LoadAsync(extensions!.Runtime); ExtensionState.Text = "Official repository catalog loaded for this Windows architecture/host compatibility."; });
    private async void InstallCatalogExtension(object sender, RoutedEventArgs e)
    {
        if (ExtensionCatalogItems.SelectedItem is not ExtensionCatalogItem item || !ConfirmCode()) return;
        await ExtensionOperation(async () =>
        {
            var temporary = Path.Combine(Path.GetTempPath(), "KikicastExtension-" + Guid.NewGuid().ToString("N") + ".kikicast");
            try { await ExtensionCatalog.DownloadAsync(item, temporary); await extensions!.InstallAsync(temporary, item.Sha256); RefreshExtensions(); ExtensionState.Text = "Release installed/updated; configuration/state retained. No extension command ran."; }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        });
    }
    private async void UninstallExtension(object sender, RoutedEventArgs e)
    {
        if (editingExtension == null || System.Windows.MessageBox.Show(this, "Remove this extension's package, attached settings/state and launcher references?\n\nYour wallpaper directories and source images are never removed.", "Uninstall extension", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        var id = editingExtension.Manifest.Id;
        await ExtensionOperation(async () =>
        {
            var prefix = "extension:" + id + ":";
            await extensions!.UninstallAsync(id, async () =>
            {
                var before = userStore.Preferences;
                await userStore.UpdatePreferencesAsync(p => p with { EntryBindings = p.EntryBindings.Where(x => !x.Key.StartsWith(prefix, StringComparison.Ordinal)).ToDictionary(), FavoriteKeys = p.FavoriteKeys.Where(x => !x.StartsWith(prefix, StringComparison.Ordinal)).ToList(), HiddenEntryKeys = p.HiddenEntryKeys.Where(x => !x.StartsWith(prefix, StringComparison.Ordinal)).ToList(), LauncherAliases = p.LauncherAliases.Where(x => !x.Key.StartsWith(prefix, StringComparison.Ordinal)).ToDictionary() });
                return async () => await userStore.UpdatePreferencesAsync(p => p with { EntryBindings = before.EntryBindings, FavoriteKeys = before.FavoriteKeys, HiddenEntryKeys = before.HiddenEntryKeys, LauncherAliases = before.LauncherAliases });
            });
            foreach (var key in itemBindingRows.Keys.Where(x => x.StartsWith(prefix, StringComparison.Ordinal)).ToArray()) itemBindingRows.Remove(key);
            extensionDrafts.Remove(id); editingExtension = null; RefreshExtensions(); RefreshLauncherItems(); ExtensionState.Text = "Extension package/configuration/state removed; wallpaper source directories untouched.";
        });
    }
    private async void ResetExtensionState(object sender, RoutedEventArgs e)
    {
        if (editingExtension == null || System.Windows.MessageBox.Show(this, "Reset the recorded shuffle/current-source state?\n\nThe original state is retained as a backup until extension uninstall. No wallpaper is changed, and no source image is deleted. DeleteNow will be unavailable until a new successful manual selection.", "Reset extension state", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await ExtensionOperation(() =>
        {
            var path = Path.Combine(extensions!.DataDirectory(editingExtension), "state.json");
            if (File.Exists(path)) { if (!LocalPathSafety.IsFile(path)) throw new InvalidDataException("Unsafe state path."); File.Move(path, path + ".backup-" + Guid.NewGuid().ToString("N")); }
            RefreshExtensionState(sender, e); return Task.CompletedTask;
        });
    }
    private void RefreshExtensionState(object sender, RoutedEventArgs e)
    {
        if (editingExtension == null || extensions == null) return;
        var path = Path.Combine(extensions.DataDirectory(editingExtension), "state.json");
        ExtensionState.Text = "No recorded operation. Wallpaper changes and deletion await your manual test.";
        if (!File.Exists(path)) return;
        try
        {
            if (!LocalPathSafety.IsFile(path) || new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException();
            using var doc = JsonDocument.Parse(File.ReadAllText(path)); var state = doc.RootElement;
            var current = state.TryGetProperty("current", out var source) && source.ValueKind == JsonValueKind.Object && source.TryGetProperty("path", out var value) ? value.GetString() : "none";
            var result = state.TryGetProperty("lastResult", out var last) ? last.GetString() : "unknown";
            ExtensionState.Text = "Last source selected by this extension (outside changes are not tracked):\n" + current + "\n" + result;
        }
        catch { ExtensionState.Text = "State is damaged/linked/oversized. Original retained; no extension was executed."; }
    }
}
