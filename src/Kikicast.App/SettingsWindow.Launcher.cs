using System.Windows;
using System.Windows.Controls;
using Kikicast.Core;

namespace Kikicast.App;

public partial class SettingsWindow
{
    private readonly Func<IReadOnlyList<LauncherSettingsItem>> launcherCatalog;
    private readonly Action refreshLauncher;
    private bool launcherSaving;
    private void RefreshLauncherItems()
    {
        if (launcherCatalog == null || LauncherItems == null) return;
        var selected = (LauncherItems.SelectedItem as LauncherSettingsItem)?.Id;
        var query = LauncherFilter.Text.Trim();
        var aliases = userStore.Preferences.LauncherAliases;
        LauncherItems.ItemsSource = launcherCatalog().Where(x => !CustomWindowSize.TryId(x.Id, out var id) || !userStore.Preferences.CustomWindowSizes.Any(s => s.Id == id))
            .Where(x => !WindowLayout.TryId(x.Id, out var layoutId) || !userStore.Preferences.WindowLayouts.Any(s => s.Id == layoutId))
            .Where(x => !layouts.Any(s => s.EntryId.Equals(x.Id, StringComparison.OrdinalIgnoreCase)))
            .Concat(layouts.Select(x => new LauncherSettingsItem(x.EntryId, x.Name, x.EntryId, LauncherKind.WindowLayout)))
            .Where(x => !customSizes.Any(s => s.EntryId.Equals(x.Id, StringComparison.OrdinalIgnoreCase)))
            .Concat(customSizes.Select(x => new LauncherSettingsItem(x.EntryId, x.Name, x.EntryId, LauncherKind.WindowCommand)))
            .Where(x => query.Length == 0 || x.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || x.Id.Contains(query, StringComparison.OrdinalIgnoreCase) || aliases.Any(a => a.Key.Equals(x.Id, StringComparison.OrdinalIgnoreCase) && a.Value.Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (selected != null) LauncherItems.SelectedItem = LauncherItems.Items.Cast<LauncherSettingsItem>().FirstOrDefault(x => x.Id == selected);
    }
    private void FilterLauncherItems(object sender, TextChangedEventArgs e) => RefreshLauncherItems();
    private void LauncherItemSelected(object sender, SelectionChangedEventArgs e)
    {
        if (LauncherItems.SelectedItem is not LauncherSettingsItem item) return;
        LauncherItemTitle.Text = item.Name;
        LauncherAlias.Text = userStore.Preferences.LauncherAliases.FirstOrDefault(x => x.Key.Equals(item.Id, StringComparison.OrdinalIgnoreCase)).Value ?? "";
        LauncherVisible.IsEnabled = item.Id != "cmd:settings";
        LauncherVisible.IsChecked = item.Id == "cmd:settings" || !userStore.Preferences.HiddenEntryKeys.Contains(item.Id, StringComparer.OrdinalIgnoreCase);
        LauncherFavorite.IsChecked = userStore.Preferences.FavoriteKeys.Contains(item.Id, StringComparer.OrdinalIgnoreCase);
        StopRecording();
        BindingRow? row = null;
        if (item.Id.StartsWith("window:", StringComparison.OrdinalIgnoreCase) && Enum.TryParse<WindowAction>(item.Id[7..], out var action)) row = windowRows.FirstOrDefault(x => x.Action == action);
        else if (LauncherBindings.CanBind(item.Id)) row = itemBindingRows.GetValueOrDefault(item.Id) ?? new(item.Name, "Global item shortcut", "Launcher", null, LauncherBindings.Get(userStore.Preferences, item.Id), item.Id);
        LauncherBindingEditor.DataContext = row; LauncherBindingEditor.IsEnabled = row != null;
        LauncherBindingHint.Text = row == null ? "Query-driven Shell/calculation rows are not global actions." : "Shortcut drafts apply with Save changes, not Save item. Hidden rows remain bindable; feature switches and command Enabled still gate execution.";
    }
    private async void SaveLauncherItem(object sender, RoutedEventArgs e)
    {
        if (launcherSaving || saving || LauncherItems.SelectedItem is not LauncherSettingsItem item) return;
        var alias = LauncherAlias.Text; var visible = LauncherVisible.IsChecked == true || item.Id == "cmd:settings";
        var favorite = LauncherFavorite.IsChecked == true;
        launcherSaving = true; Categories.IsEnabled = false;
        try
        {
            await userStore.UpdatePreferencesAsync(p =>
            {
                var next = LauncherCustomization.Alias(LauncherCustomization.Visibility(p, item.Id, visible), item.Id, alias);
                return next.FavoriteKeys.Contains(item.Id, StringComparer.OrdinalIgnoreCase) == favorite ? next
                    : next with { FavoriteKeys = LauncherSections.ToggleFavorite(next.FavoriteKeys, item.Id) };
            });
            refreshLauncher(); RefreshLauncherItems(); LauncherStatus.Text = "Item saved. Hidden entries can be restored here.";
        }
        catch (Exception ex) when (UserStore.IsStorageError(ex)) { LauncherStatus.Text = "Item was not saved: " + ex.Message; }
        finally { launcherSaving = false; Categories.IsEnabled = true; }
    }
    private async void ResetAllLearning(object sender, RoutedEventArgs e)
    {
        if (launcherSaving || saving) return;
        if (System.Windows.MessageBox.Show(this, "Clear all learned launcher visits and search terms? Favorites, aliases and calculation history are kept.", "Clear learned ranking", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await EditLauncherHistory(h => h.ResetLearning());
    }
    private async void ClearCalculationHistory(object sender, RoutedEventArgs e)
    {
        if (launcherSaving || saving) return;
        if (System.Windows.MessageBox.Show(this, "Clear the saved calculation history? This cannot be undone. Learned launcher visits are kept.", "Clear calculation history", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await EditLauncherHistory(h => h with { Calculations = [] });
    }
    private async Task EditLauncherHistory(Func<LocalHistory, LocalHistory> update)
    {
        launcherSaving = true; Categories.IsEnabled = false;
        try { await userStore.UpdateHistoryAsync(update); refreshLauncher(); LauncherStatus.Text = "History updated."; }
        catch (Exception ex) when (UserStore.IsStorageError(ex)) { LauncherStatus.Text = "History was not changed: " + ex.Message; }
        finally { launcherSaving = false; Categories.IsEnabled = true; }
    }
    internal async Task VerifyCategoryInputAsync(string evidenceDirectory)
    {
        SelectCategory("General");
        ((TabItem)Categories.Items[0]).Focus();
        for (var index = 0; index < Categories.Items.Count; index++)
        {
            if (Categories.SelectedIndex != index) throw new InvalidOperationException("Injected Ctrl+Tab failed category navigation.");
            var category = ((TabItem)Categories.Items[index]).Header.ToString()!.Replace(" ", "-");
            OwnedInputAutomation.Screenshot(this, System.IO.Path.Combine(evidenceDirectory, "settings-" + category + ".png"));
            await OwnedInputAutomation.ChordAsync(this, 9, 17);
        }
        if (Categories.SelectedIndex != 0) throw new InvalidOperationException("Injected category cycling did not wrap to General.");
        await OwnedInputAutomation.ChordAsync(this, 9, 17, 16);
        if (Categories.SelectedIndex != Categories.Items.Count - 1) throw new InvalidOperationException("Injected Ctrl+Shift+Tab failed reverse category navigation.");
        var originalDepth = userStore.Preferences.ApplicationFolderDepth;
        var originalAppPaths = userStore.Preferences.IncludeWindowsAppPaths;
        var originalPackaged = userStore.Preferences.IncludePackagedApplications;
        async Task SetDepth(int depth, bool appPaths, bool packaged)
        {
            SelectCategory("Applications"); UpdateLayout(); await Task.Delay(60);
            if (IncludeAppPaths.IsChecked != appPaths) { IncludeAppPaths.Focus(); await OwnedInputAutomation.ChordAsync(this, 32); }
            if (IncludeAppPaths.IsChecked != appPaths) throw new InvalidOperationException("Native registered-application checkbox did not toggle the source gate.");
            if (IncludePackagedApps.IsChecked != packaged) { IncludePackagedApps.Focus(); await OwnedInputAutomation.ChordAsync(this, 32); }
            if (IncludePackagedApps.IsChecked != packaged) throw new InvalidOperationException("Native packaged-application checkbox did not toggle the source gate.");
            if (!FolderDepth.Focus() || !FolderDepth.IsKeyboardFocusWithin) throw new InvalidOperationException("Visible depth selector could not receive keyboard focus.");
            var trace = new List<string> { $"before={FolderDepth.IsDropDownOpen},focus={System.Windows.Input.Keyboard.FocusedElement?.GetType().Name}" };
            void KeyTrace(object sender, System.Windows.Input.KeyEventArgs e) => trace.Add($"key={e.Key}/{e.SystemKey},mods={System.Windows.Input.Keyboard.Modifiers}");
            void OpenTrace(object? sender, EventArgs e) => trace.Add("opened");
            void CloseTrace(object? sender, EventArgs e) => trace.Add("closed");
            FolderDepth.PreviewKeyDown += KeyTrace; FolderDepth.DropDownOpened += OpenTrace; FolderDepth.DropDownClosed += CloseTrace;
            try { await OwnedInputAutomation.ChordAsync(this, 115); }
            finally { FolderDepth.PreviewKeyDown -= KeyTrace; FolderDepth.DropDownOpened -= OpenTrace; FolderDepth.DropDownClosed -= CloseTrace; }
            if (!FolderDepth.IsDropDownOpen) throw new InvalidOperationException("Native F4 did not open the themed depth list: " + string.Join(";", trace));
            await OwnedInputAutomation.ChordAsync(this, 27);
            if (FolderDepth.IsDropDownOpen) throw new InvalidOperationException("Native Esc did not dismiss the depth list.");
            await OwnedInputAutomation.ChordAsync(this, 40, 18);
            if (!FolderDepth.IsDropDownOpen) throw new InvalidOperationException("Native Alt+Down did not open the non-text depth list.");
            await OwnedInputAutomation.ChordAsync(this, 27);
            if (FolderDepth.IsDropDownOpen) throw new InvalidOperationException("Native Esc did not close the Alt+Down depth list.");
            await OwnedInputAutomation.ChordAsync(this, 36);
            for (var step = 0; step < depth; step++) await OwnedInputAutomation.ChordAsync(this, 40);
            if (FolderDepth.SelectedIndex != depth) throw new InvalidOperationException("Injected depth selector did not reflect native arrow keys.");
            SelectCategory("Launcher"); UpdateLayout(); await Task.Delay(60);
        }
        await SetDepth(2, false, false);
        var originalSensitivity = userStore.Preferences.MatchSensitivity;
        var originalIcons = userStore.Preferences.ShowApplicationIcons;
        if (ShowApplicationIcons.IsChecked == true) { ShowApplicationIcons.Focus(); await OwnedInputAutomation.ChordAsync(this, 32); }
        async Task SetAndSave(SearchSensitivity sensitivity)
        {
            var radio = sensitivity == SearchSensitivity.High ? SensitivityHigh : sensitivity == SearchSensitivity.Low ? SensitivityLow : SensitivityMedium;
            if (!radio.Focus() || !radio.IsKeyboardFocused) throw new InvalidOperationException("Visible settings radio could not receive owned keyboard focus.");
            await OwnedInputAutomation.ChordAsync(this, 32);
            if (radio.IsChecked != true) throw new InvalidOperationException("Injected Space did not select the focused sensitivity radio.");
            SaveButton.Focus(); await OwnedInputAutomation.ChordAsync(this, 32);
            var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
            while (saving && DateTimeOffset.UtcNow < deadline) await Task.Delay(25);
            if (saving || userStore.Preferences.MatchSensitivity != sensitivity || !Feedback.Text.StartsWith("Saved.", StringComparison.Ordinal))
                throw new InvalidOperationException($"Injected settings radio/Save did not persist search sensitivity: wanted={sensitivity}, actual={userStore.Preferences.MatchSensitivity}, saving={saving}, feedback={Feedback.Text}");
        }
        await SetAndSave(SearchSensitivity.High);
        if (userStore.Preferences.ShowApplicationIcons) throw new InvalidOperationException("Injected icon checkbox/Save did not persist the execution gate.");
        if (userStore.Preferences.ApplicationFolderDepth != 2) throw new InvalidOperationException("Injected depth selection/Save did not persist scan depth.");
        if (userStore.Preferences.IncludeWindowsAppPaths) throw new InvalidOperationException("Native registered-application checkbox/Save did not persist its gate.");
        if (userStore.Preferences.IncludePackagedApplications) throw new InvalidOperationException("Native packaged-application checkbox/Save did not persist its source gate.");
        await SetDepth(originalDepth, originalAppPaths, originalPackaged);
        if (ShowApplicationIcons.IsChecked != originalIcons) { ShowApplicationIcons.Focus(); await OwnedInputAutomation.ChordAsync(this, 32); }
        await SetAndSave(originalSensitivity);
        if (userStore.Preferences.IncludeWindowsAppPaths != originalAppPaths) throw new InvalidOperationException("Native registered-application source gate failed persistence round-trip.");
        if (userStore.Preferences.IncludePackagedApplications != originalPackaged) throw new InvalidOperationException("Native packaged source failed persistence round-trip.");
        if (userStore.Preferences.ApplicationFolderDepth != originalDepth) throw new InvalidOperationException("Injected depth failed persistence round-trip.");
        if (userStore.Preferences.ShowApplicationIcons != originalIcons) throw new InvalidOperationException("Injected icon preference failed persistence round-trip.");
        OwnedInputAutomation.Screenshot(this, System.IO.Path.Combine(evidenceDirectory, "settings-sensitivity-saved.png"));
        await VerifyItemBindingInputAsync(evidenceDirectory);
        await VerifyCustomSizeInputAsync(evidenceDirectory);
        await VerifyDisplayCycleInputAsync(evidenceDirectory);
        await VerifyLayoutInputAsync(evidenceDirectory);
        SelectCategory("Applications"); UpdateLayout(); await Task.Delay(60);
        if (!ApplicationSettingsScroll.Focus()) throw new InvalidOperationException("Application settings scroll surface could not receive focus.");
        await OwnedInputAutomation.ChordAsync(this, 35, 17); UpdateLayout();
        if (ApplicationSettingsScroll.ScrollableHeight > 0 && ApplicationSettingsScroll.VerticalOffset == 0)
            throw new InvalidOperationException("Native Ctrl+End did not reveal the lower application settings.");
        OwnedInputAutomation.Screenshot(this, System.IO.Path.Combine(evidenceDirectory, "settings-Applications-bottom.png"));
        await OwnedInputAutomation.ChordAsync(this, 36, 17); UpdateLayout();
        OwnedInputAutomation.Screenshot(this, System.IO.Path.Combine(evidenceDirectory, "settings-Applications-themed.png"));
    }

    public void SelectCategory(string name)
    {
        foreach (var tab in Categories.Items.OfType<TabItem>())
            if (tab.Header?.ToString()?.Equals(name, StringComparison.OrdinalIgnoreCase) == true) { Categories.SelectedItem = tab; break; }
    }
}
