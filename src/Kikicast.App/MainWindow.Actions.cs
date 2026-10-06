using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Kikicast.Core;
using Kikicast.Windows;

namespace Kikicast.App;

public partial class MainWindow
{
    private Row? actionRow;
    private IReadOnlyList<LauncherAction> actionSnapshot = [];
    private Dictionary<string, string>? aliasRevision;
    private Dictionary<string, (string Text, LauncherSearchText Profile)> aliasCache = new(StringComparer.OrdinalIgnoreCase);
    public event Action? LauncherSettingsRequested;

    private Dictionary<string, (string Text, LauncherSearchText Profile)> AliasProfiles()
    {
        if (!ReferenceEquals(aliasRevision, store.Preferences.LauncherAliases))
        {
            aliasRevision = store.Preferences.LauncherAliases;
            aliasCache = aliasRevision.ToDictionary(x => x.Key, x => (x.Value, LauncherSearchText.Create(x.Value)), StringComparer.OrdinalIgnoreCase);
        }
        return aliasCache;
    }
    public IReadOnlyList<LauncherSettingsItem> SettingsItems
    {
        get
        {
            var list = entries.Select(x => new LauncherSettingsItem(x.Id, x.Name, x.UsageKey, LauncherKind.Application, x.LocalPath)).ToList();
            list.AddRange(WindowGeometry.Commands.Select(x => new LauncherSettingsItem(LauncherSections.WindowId(x.Action), x.Name, LauncherSections.WindowId(x.Action), LauncherKind.WindowCommand)));
            list.AddRange(store.Preferences.CustomWindowSizes.Select(x => new LauncherSettingsItem(x.EntryId, x.Name, x.EntryId, LauncherKind.WindowCommand)));
            list.AddRange(store.Preferences.WindowLayouts.Select(x => new LauncherSettingsItem(x.EntryId, x.Name, x.EntryId, LauncherKind.WindowLayout)));
            list.AddRange(extensions.Commands(includeDisabled: true).Select(x => new LauncherSettingsItem(x.EntryId, x.Title, x.EntryId, LauncherKind.Extension)));
            list.AddRange(commands.Current.Commands.Select(x => new LauncherSettingsItem(x.EntryId, x.Name, x.EntryId, LauncherKind.CustomCommand)));
            foreach (var (id, name) in new[] { ("history", "Calculation history"), ("settings", "Kikicast settings"), ("shell", "Run Shell Command"), (RecycleBinCommand.OpenId, "Open Recycle Bin"), (RecycleBinCommand.EmptyId, "Empty Recycle Bin") })
                list.Add(new("cmd:" + id, name, "cmd:" + id, LauncherKind.Command));
            var known = list.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var id in store.Preferences.LauncherAliases.Keys.Concat(store.Preferences.HiddenEntryKeys).Concat(store.Preferences.FavoriteKeys).Concat(store.Preferences.EntryBindings.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
                if (known.Add(id)) list.Add(new(id, "Unavailable · " + id, id, LauncherKind.Command, Unavailable: true));
            return list.OrderBy(x => x.Kind).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }
    private static bool CanHide(Row? row) => row?.Id != null && row.Command != "settings";
    private void OpenActions()
    {
        if (!store.Preferences.ActionsPanelEnabled || executing || updatingPreferences || Results.SelectedItem is not Row row) return;
        actionRow = row;
        var favoriteRows = Results.Items.Cast<Row>().Where(x => x.Section == "Favorites").ToList();
        var favoriteIndex = favoriteRows.FindIndex(x => x.Id == row.Id);
        actionSnapshot = LauncherActions.For(new(row.Id != null, row.IsFavorite,
            favoriteIndex > 0, favoriteIndex >= 0 && favoriteIndex < favoriteRows.Count - 1,
            row.Entry?.LocalPath, row.Answer, CanHide(row), store.History.Launches.Any(x => x.Path.Equals(row.UsageKey, StringComparison.OrdinalIgnoreCase)), row.HistoryItem != null, row.Entry?.AppUserModelId));
        ActionTitle.Text = row.Title;
        ActionFilter.Clear(); ActionPanel.Visibility = Visibility.Visible; FilterActions();
        InputMethod.SetIsInputMethodEnabled(ActionFilter, false); ActionFilter.Focus();
    }
    private void FilterActions()
    {
        if (ActionList == null) return;
        ActionList.ItemsSource = actionSnapshot.Where(x => x.Matches(ActionFilter.Text)).ToArray();
        ActionList.SelectedIndex = ActionList.Items.Count > 0 ? 0 : -1;
    }
    private void ActionFilterChanged(object sender, TextChangedEventArgs e) => FilterActions();
    private void CloseActions(bool focus = true)
    {
        if (ActionPanel == null) return;
        ActionPanel.Visibility = Visibility.Collapsed; actionRow = null; actionSnapshot = [];
        if (focus && IsVisible) Query.Focus();
    }
    private bool HandleActionKey(System.Windows.Input.KeyEventArgs e, ModifierKeys modifiers, bool repeat)
    {
        if (ActionPanel.Visibility != Visibility.Visible) return false;
        if (e.Key == Key.Escape || modifiers == ModifierKeys.Control && e.Key == Key.K)
        { e.Handled = true; CloseActions(); return true; }
        LauncherActionKind? shortcut = modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.F ? LauncherActionKind.ToggleFavorite
            : modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.H ? LauncherActionKind.Hide
            : modifiers == ModifierKeys.Control && e.Key == Key.Enter ? LauncherActionKind.Reveal
            : modifiers == (ModifierKeys.Control | ModifierKeys.Alt) && e.Key == Key.Up ? LauncherActionKind.MoveFavoriteUp
            : modifiers == (ModifierKeys.Control | ModifierKeys.Alt) && e.Key == Key.Down ? LauncherActionKind.MoveFavoriteDown : null;
        if (shortcut != null)
        {
            e.Handled = true;
            var action = actionSnapshot.FirstOrDefault(x => x.Kind == shortcut);
            if (action != null && !repeat) { ActionFilter.Clear(); ActionList.SelectedItem = action; RunSelectedAction(); }
            return true;
        }
        if (modifiers == ModifierKeys.Control && e.Key == Key.OemComma)
        { e.Handled = true; if (!e.IsRepeat) { CloseActions(); SettingsRequested?.Invoke(); } return true; }
        if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown || modifiers == ModifierKeys.Control && e.Key is Key.N or Key.P)
        {
            e.Handled = true;
            var direction = e.Key is Key.Down or Key.PageDown or Key.N ? 1 : -1;
            ActionList.SelectedIndex = Math.Clamp(ActionList.SelectedIndex + direction * (e.Key is Key.PageUp or Key.PageDown ? 6 : 1), 0, Math.Max(0, ActionList.Items.Count - 1));
            if (ActionList.SelectedItem != null) ActionList.ScrollIntoView(ActionList.SelectedItem);
            return true;
        }
        if (e.Key == Key.Enter) { e.Handled = true; if (!repeat) RunSelectedAction(); return true; }
        // Other launcher chords must not act on the obscured selection while the actions pane is up.
        if (modifiers.HasFlag(ModifierKeys.Control) && e.Key is Key.F or Key.H or Key.OemComma
            || modifiers == ModifierKeys.Control && KeyInterop.VirtualKeyFromKey(e.Key) is >= 48 and <= 57)
        { e.Handled = true; return true; }
        return false;
    }
    private void ActionDoubleClick(object sender, MouseButtonEventArgs e) => RunSelectedAction();
    private async void RunSelectedAction()
    {
        if (updatingPreferences || executing || actionRow is not { } row || ActionList.SelectedItem is not LauncherAction action) return;
        CloseActions();
        var current = Results.Items.Cast<Row>().FirstOrDefault(x => row.Id != null ? x.Id == row.Id : x == row);
        if (current == null) { SetStatus("This result is no longer available."); return; }
        Results.SelectedItem = current;
        switch (action.Kind)
        {
            case LauncherActionKind.Activate: Execute(); break;
            case LauncherActionKind.ToggleFavorite: ChangeFavorite(); break;
            case LauncherActionKind.MoveFavoriteUp: ChangeFavorite(-1); break;
            case LauncherActionKind.MoveFavoriteDown: ChangeFavorite(1); break;
            case LauncherActionKind.Hide: await HideSelectedAsync(); break;
            case LauncherActionKind.Reveal: Reveal(current); break;
            case LauncherActionKind.Settings: LauncherSettingsRequested?.Invoke(); break;
            case LauncherActionKind.CopyLocation: CopyAction(current.Entry?.AppUserModelId ?? current.Entry?.Path); break;
            case LauncherActionKind.CopyValue: CopyAction(current.Answer); break;
            case LauncherActionKind.ResetLearning:
                await EditHistory(h => h.ResetLearning(current.UsageKey)); break;
            case LauncherActionKind.DeleteHistory:
                if (current.HistoryItem is { } visit) await EditHistory(h => h.RemoveCalculation(visit)); break;
        }
    }
    private async Task EditHistory(Func<LocalHistory, LocalHistory> update)
    {
        updatingPreferences = true;
        try { await store.UpdateHistoryAsync(update); RefreshKeepingSelection(); }
        catch (Exception ex) when (UserStore.IsStorageError(ex)) { SetStatus("History edit failed: " + ex.Message); }
        finally { updatingPreferences = false; }
    }
    private async Task HideSelectedAsync()
    {
        if (updatingPreferences || executing || Results.SelectedItem is not Row row || !CanHide(row)) return;
        updatingPreferences = true; var index = Results.SelectedIndex;
        try
        {
            await store.UpdatePreferencesAsync(p => LauncherCustomization.Visibility(p, row.Id!, false));
            Refresh(); Results.SelectedIndex = Results.Items.Count > 0 ? Math.Clamp(index, 0, Results.Items.Count - 1) : -1;
            Query.Focus();
        }
        catch (Exception ex) when (UserStore.IsStorageError(ex)) { SetStatus("Visibility was not saved: " + ex.Message); }
        finally { updatingPreferences = false; }
    }
    public void RefreshKeepingSelection()
    {
        var row = Results.SelectedItem as Row; var index = Results.SelectedIndex;
        Refresh();
        var restored = row?.Id == null ? index : Results.Items.Cast<Row>().ToList().FindIndex(x => x.Id == row.Id);
        if (Results.Items.Count > 0) Results.SelectedIndex = Math.Clamp(restored, 0, Results.Items.Count - 1);
    }
    private void CopyAction(string? text)
    {
        if (text == null) return;
        try { System.Windows.Clipboard.SetText(text); Status.Text = "Copied."; }
        catch (System.Runtime.InteropServices.ExternalException ex) { SetStatus("Clipboard unavailable: " + ex.Message); }
    }
    internal async Task VerifySimulatedKeysAsync(string? evidenceDirectory, bool verifyRegisteredActivation = true)
    {
        Query.Clear(); Query.Focus(); UpdateLayout(); await Task.Delay(250);
        if (evidenceDirectory != null) SavePaletteEvidence(System.IO.Path.Combine(evidenceDirectory, "application-icons.png"));
        await OwnedInputAutomation.BurstLettersAsync(this, "pwsh");
        if (!Query.Text.Equals("pwsh", StringComparison.OrdinalIgnoreCase) || !HasQueryFocus) throw new InvalidOperationException("Rapid virtual-key letters were dropped or lost focus.");
        Query.Clear(); await OwnedInputAutomation.TextAsync(this, "pwsh");
        if (Query.Text != "pwsh" || !HasQueryFocus || !IsEnglishInputActive) throw new InvalidOperationException($"Owned injected typing lost English query focus: query='{Query.Text}', focus={HasQueryFocus}, english={IsEnglishInputActive}, keyboard={(Keyboard.FocusedElement as FrameworkElement)?.Name}.");
        await OwnedInputAutomation.ChordAsync(this, 75, 17); // Ctrl+K through WPF's real key pipeline.
        if (ActionPanel.Visibility != Visibility.Visible || !ActionFilter.IsKeyboardFocused) throw new InvalidOperationException("Injected Ctrl+K did not open/focus Actions.");
        await OwnedInputAutomation.TextAsync(this, "settings");
        if (ActionList.Items.Count != 1) throw new InvalidOperationException("Injected action filter did not match one row.");
        if (evidenceDirectory != null) SavePaletteEvidence(System.IO.Path.Combine(evidenceDirectory, "actions.png"));
        await OwnedInputAutomation.ChordAsync(this, 27);
        if (ActionPanel.Visibility != Visibility.Collapsed || !HasQueryFocus) throw new InvalidOperationException("Injected Escape failed layered return to query.");
        await OwnedInputAutomation.ChordAsync(this, 70, 17, 16); // Ctrl+Shift+F, same call as Actions.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (!store.Preferences.FavoriteKeys.Contains("cmd:shell") && DateTimeOffset.UtcNow < deadline) await Task.Delay(30);
        if (!store.Preferences.FavoriteKeys.Contains("cmd:shell")) throw new InvalidOperationException("Injected favorite chord did not persist.");
        await OwnedInputAutomation.ChordAsync(this, 65, 17); await OwnedInputAutomation.ChordAsync(this, 8);
        if (Query.Text.Length != 0 || !HasQueryFocus) throw new InvalidOperationException("Injected Ctrl+A/Backspace failed in query.");
        if (evidenceDirectory != null)
        {
            SavePaletteEvidence(System.IO.Path.Combine(evidenceDirectory, "favorites.png"));
            await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(evidenceDirectory, "input-evidence.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                Timestamp = DateTimeOffset.UtcNow, OS = Environment.OSVersion.ToString(), Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                Checks = new[] { verifyRegisteredActivation ? "owned Unicode source typing / registered Ctrl+Alt+F23 activation" : "owned click / bounded palette activation", "rapid virtual-key letters / English Unicode query typing", "Ctrl+K / filter / Esc", "Ctrl+Shift+F persistence", "Ctrl+A / Backspace" },
                Scope = "Injected owned-window input, not a physical double-Ctrl/IME/TSF acceptance. PNGs render only our visual; desktop backdrop is masked."
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
    }
    private void SavePaletteEvidence(string path)
    {
        var snapshot = FrostImage.Source; FrostImage.Source = null;
        try { OwnedInputAutomation.Screenshot(this, path); }
        finally { FrostImage.Source = snapshot; }
    }

    internal async Task VerifyLauncherActionsAsync(bool verifyFocus = true)
    {
        var preferences = store.Preferences;
        try
        {
            await store.UpdatePreferencesAsync(p => LauncherCustomization.Alias(p with { ShellCommandsEnabled = true, ActionsPanelEnabled = true }, "cmd:shell", "Owned alias"));
            Query.Text = "owned alias"; Refresh();
            if (Results.SelectedItem is not Row { Command: "shell", Alias: "Owned alias" }) throw new InvalidOperationException("Exact alias did not lead search results");
            OpenActions();
            if (ActionPanel.Visibility != Visibility.Visible || verifyFocus && (!IsVisible || !ActionFilter.IsKeyboardFocused || !inputSession.IsEnglish))
                throw new InvalidOperationException("Actions pane dismissed palette, lost focus or changed input session");
            ActionFilter.Text = "settings";
            if (ActionList.Items.Count != 1 || ActionList.SelectedItem is not LauncherAction { Kind: LauncherActionKind.Settings })
                throw new InvalidOperationException("Actions filter disagrees with selectable actions");
            CloseActions();
            if (verifyFocus && !HasQueryFocus) throw new InvalidOperationException("Closing actions did not return to query without reactivation");
            store.RecordLaunch("cmd:shell", "owned alias");
            await HideSelectedAsync();
            if (Results.Items.Cast<Row>().Any(x => x.Command == "shell") || !store.Preferences.LauncherAliases.ContainsKey("cmd:shell"))
                throw new InvalidOperationException("Hide did not remove result or discarded its alias");
            if (!SettingsItems.Any(x => x.Id == "cmd:shell")) throw new InvalidOperationException("Hidden entry cannot be recovered in settings");
            await store.UpdatePreferencesAsync(p => LauncherCustomization.Visibility(p, "cmd:shell", true)); Refresh();
            if (Results.SelectedItem is not Row { Command: "shell" }) throw new InvalidOperationException("Restored alias did not restore the result");
            await EditHistory(h => h.ResetLearning("cmd:shell"));
            if (store.History.Launches.Any(x => x.Path == "cmd:shell")) throw new InvalidOperationException("Per-item learning reset failed");
            await store.UpdatePreferencesAsync(p => p with { ApplicationsEnabled = false, ActionsPanelEnabled = false });
            Query.Clear(); Refresh(); OpenActions();
            if (Results.Items.Cast<Row>().Any(x => x.Entry != null) || ActionPanel.Visibility == Visibility.Visible)
                throw new InvalidOperationException("Applications or actions-panel feature gate ignored");
        }
        finally { CloseActions(); await store.SavePreferencesAsync(preferences); Query.Clear(); Refresh(); }
    }

    private void Reveal(Row row)
    {
        if (row.Entry == null || row.Entry.LocalPath == null || !row.Entry.IsEnabled(store.Preferences) || !LocalPathSafety.IsFile(row.Entry.Path)) return;
        try { Dismiss(restore: false); ShellLocation.Reveal(row.Entry.Path); }
        catch (Exception ex) when (UserStore.IsStorageError(ex) || ex is System.Runtime.InteropServices.ExternalException or System.ComponentModel.Win32Exception)
        { SetStatus("Location could not be revealed: " + ex.Message); ActionFailed?.Invoke(Status.Text); }
    }
}
