using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Kikicast.Core;
using Kikicast.Windows;

namespace Kikicast.App;

public partial class SettingsWindow : Window
{
    public sealed class BindingRow(string name, string description, string group, WindowAction? action, HotKeyBinding? initial, string? entryId = null) : INotifyPropertyChanged
    {
        private HotKeyBinding? binding = initial;
        private bool recording;
        public string Name { get; } = name;
        public string Description { get; } = description;
        public string Group { get; } = group;
        public WindowAction? Action { get; } = action;
        public string? EntryId { get; } = entryId;
        public HotKeyBinding? Binding { get => binding; set { binding = value; Changed(); } }
        public bool IsRecording { get => recording; set { recording = value; Changed(); } }
        public bool HasBinding => Binding != null;
        public string Label => IsRecording ? "Press keys…" : Binding?.Display ?? "Unbound";
        public string AccessibleName => Name + "：" + Label;
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Changed()
        {
            foreach (var key in new[] { nameof(Binding), nameof(IsRecording), nameof(HasBinding), nameof(Label), nameof(AccessibleName) })
                PropertyChanged?.Invoke(this, new(key));
        }
    }

    private readonly Func<AppPreferences, Task<string?>> apply;
    private readonly Func<string>? startupStatus;
    private readonly AppPreferences original;
    private readonly RunningApplicationDiscovery discovery;
    private readonly Func<Task> refreshStartMenu;
    private readonly SavedCommandStore commands;
    private readonly UserStore userStore;
    private Guid commandId = Guid.NewGuid();
    private bool commandsBusy;
    private bool commandsSaving
    {
        get => commandsBusy;
        set { commandsBusy = value; if (CommandsPage != null) CommandsPage.IsEnabled = !value; }
    }
    private readonly BindingRow paletteRow;
    private readonly BindingRow[] windowRows;
    private readonly Dictionary<string, BindingRow> itemBindingRows = new(StringComparer.OrdinalIgnoreCase);
    private BindingRow? recordingRow;
    private bool saving, ready;
    private readonly DoubleTapRecognizer detector = new();
    private readonly HashSet<int> held = [];
    public int VisibleBindingCount => 1 + windowRows.Length;
    public int CategoryCount => Categories.Items.Count;

    public SettingsWindow(AppPreferences preferences, Func<AppPreferences, Task<string?>> apply, RunningApplicationDiscovery discovery, Func<Task> refreshStartMenu, SavedCommandStore commands, UserStore userStore, Func<IReadOnlyList<LauncherSettingsItem>> launcherCatalog, Action refreshLauncher, Func<IReadOnlyList<LauncherEntry>>? layoutApplications = null, Func<string>? startupStatus = null, ExtensionStore? extensions = null)
    {
        InitializeComponent();
        this.apply = apply; this.startupStatus = startupStatus;
        this.discovery = discovery; this.refreshStartMenu = refreshStartMenu;
        this.commands = commands; this.userStore = userStore;
        this.launcherCatalog = launcherCatalog; this.refreshLauncher = refreshLauncher;
        this.layoutApplications = layoutApplications ?? (() => []);
        this.extensions = extensions;
        EnableExtensions.IsChecked = preferences.ExtensionsEnabled; ShowExtensionCommands.IsChecked = preferences.ShowExtensions;
        InputMethod.SetIsInputMethodEnabled(InstalledExtensions, false); InputMethod.SetIsInputMethodEnabled(ExtensionFolders, false); InputMethod.SetIsInputMethodEnabled(ExtensionCatalogItems, false);
        RefreshExtensions();
        StartAtLogon.IsChecked = preferences.StartAtLogon;
        StartupStatus.Text = startupStatus?.Invoke() ?? "Startup registration status is unavailable in this model-only view.";
        EnableApplications.IsChecked = preferences.ApplicationsEnabled;
        ShowApplicationIcons.IsChecked = preferences.ShowApplicationIcons;
        FolderDepth.SelectedIndex = preferences.ApplicationFolderDepth;
        IncludeAppPaths.IsChecked = preferences.IncludeWindowsAppPaths;
        IncludePackagedApps.IsChecked = preferences.IncludePackagedApplications;
        EnableActionsPanel.IsChecked = preferences.ActionsPanelEnabled;
        SensitivityLow.IsChecked = preferences.MatchSensitivity == SearchSensitivity.Low;
        SensitivityMedium.IsChecked = preferences.MatchSensitivity == SearchSensitivity.Medium;
        SensitivityHigh.IsChecked = preferences.MatchSensitivity == SearchSensitivity.High;
        RefreshLauncherItems();
        EnableCalculator.IsChecked = preferences.CalculatorEnabled;
        EnableCurrency.IsChecked = preferences.CurrencyConversionEnabled;
        EnableCalcHistory.IsChecked = preferences.CalculationHistoryEnabled;
        EnableShell.IsChecked = preferences.ShellCommandsEnabled;
        EnableShellFallback.IsChecked = preferences.ShellFallbackEnabled;
        EnableSystem.IsChecked = preferences.SystemCommandsEnabled;
        EnableSavedCommands.IsChecked = preferences.SavedCommandsEnabled;
        ShowSavedCommands.IsChecked = preferences.ShowSavedCommands;
        RefreshCommands(); NewCommand(this, new RoutedEventArgs());
        if (commands.Warning != null) CommandStatus.Text = commands.Warning;
        discovery.Changed += DiscoveryChanged;
        Closed += (_, _) => discovery.Changed -= DiscoveryChanged;
        DiscoverApps.IsChecked = preferences.DiscoverRunningApplications;
        applicationFolders.AddRange(preferences.ApplicationFolders);
        FolderScopes.ItemsSource = applicationFolders.ToArray();
        RefreshDiscoveredApps();
        original = preferences;
        foreach (var pair in preferences.EntryBindings) itemBindingRows[pair.Key] = new(pair.Key, "Global item shortcut", "Launcher", null, pair.Value, pair.Key);
        paletteRow = new("Show palette", "Default: double Ctrl · click to replace", "General", null, preferences.PaletteBinding);
        windowRows = WindowGeometry.Commands.Select(x => new BindingRow(x.Name, x.Keywords,
            WindowGeometry.Group(x.Action), x.Action, preferences.WindowBindings.GetValueOrDefault(x.Action))).ToArray();
        GlobalBindings.ItemsSource = new[] { paletteRow };
        var view = CollectionViewSource.GetDefaultView(windowRows);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(BindingRow.Group)));
        WindowBindings.ItemsSource = view;
        Fallback.IsChecked = preferences.AltSpaceFallback;
        Fallback.Visibility = preferences.AltSpaceFallback ? Visibility.Visible : Visibility.Collapsed;
        WindowManagement.IsChecked = preferences.WindowManagementEnabled;
        ShowWindows.IsChecked = preferences.ShowWindowCommands;
        customSizes.AddRange(preferences.CustomWindowSizes);
        layouts.AddRange(preferences.WindowLayouts); ShowLayouts.IsChecked = preferences.ShowWindowLayouts; RefreshLayouts();
        SizeAnchor.ItemsSource = Enum.GetValues<WindowSizeAnchor>().Select(CustomWindowSize.AnchorLabel).ToArray();
        RefreshCustomSizes(); NewCustomSize(this, new RoutedEventArgs());
        HalfCycleSelector.SelectedIndex = (int)WindowCycle.Resolve(preferences);
        Suggestions.IsChecked = preferences.ShowSuggestions;
        WindowGap.Text = preferences.WindowGap.ToString(System.Globalization.CultureInfo.CurrentCulture);
        ready = true;
    }
    private async void ImportCommands(object sender, RoutedEventArgs e)
    {
        if (commandsSaving) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Import Kikicast PowerShell commands", Filter = "Commands JSON (*.json)|*.json", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        if (System.Windows.MessageBox.Show(this, "These commands can execute arbitrary PowerShell code. Import only trusted files. Imported commands will be disabled; review each script before enabling. Existing matching IDs are replaced. Continue?", "Import commands", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        commandsSaving = true; var imported = false;
        try { await commands.ImportAsync(dialog.FileName); imported = true; RefreshCommands(); CommandStatus.Text = "Imported. All imported commands are disabled until you review and enable them."; }
        catch (Exception ex) when (UserStore.IsStorageError(ex) || ex is ArgumentException) { CommandStatus.Text = "Import failed: " + ex.Message; }
        finally { commandsSaving = false; }
        if (imported) NewCommand(sender, e);
    }
    private async void ExportCommands(object sender, RoutedEventArgs e)
    {
        if (commandsSaving) return;
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = "Export commands (scripts are plain text)", Filter = "Commands JSON (*.json)|*.json", FileName = "kikicast-commands.json", DefaultExt = ".json", AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return;
        commandsSaving = true;
        try { await commands.ExportAsync(dialog.FileName); CommandStatus.Text = "Exported. Scripts are plain text; keep this file private."; }
        catch (Exception ex) when (UserStore.IsStorageError(ex)) { CommandStatus.Text = "Export failed: " + ex.Message; }
        finally { commandsSaving = false; }
    }

    private void RefreshCommands(Guid? selected = null)
    {
        CommandList.ItemsSource = commands.Current.Commands;
        if (selected != null) CommandList.SelectedItem = commands.Current.Commands.FirstOrDefault(x => x.Id == selected);
    }
    private void NewCommand(object sender, RoutedEventArgs e)
    {
        if (commandsSaving) return;
        CommandList.SelectedIndex = -1; commandId = Guid.NewGuid();
        CommandName.Clear(); CommandScript.Clear(); CommandFolder.Clear();
        CommandEnabled.IsChecked = true; CommandProfile.IsChecked = false;
        CommandArg1.Clear(); CommandArg2.Clear(); CommandArg3.Clear();
        CommandArgOptional1.IsChecked = CommandArgOptional2.IsChecked = CommandArgOptional3.IsChecked = false;
    }
    private void CommandSelected(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (CommandList.SelectedItem is not SavedCommand command) return;
        commandId = command.Id; CommandName.Text = command.Name; CommandScript.Text = command.Script;
        CommandFolder.Text = command.WorkingDirectory ?? ""; CommandProfile.IsChecked = command.LoadProfile; CommandEnabled.IsChecked = command.Enabled;
        var names = new[] { CommandArg1, CommandArg2, CommandArg3 };
        var optional = new[] { CommandArgOptional1, CommandArgOptional2, CommandArgOptional3 };
        for (var i = 0; i < 3; i++) { names[i].Text = command.Arguments.ElementAtOrDefault(i)?.Name ?? ""; optional[i].IsChecked = command.Arguments.ElementAtOrDefault(i)?.Optional == true; }
    }
    private async void SaveCommand(object sender, RoutedEventArgs e)
    {
        if (commandsSaving) return;
        var command = new SavedCommand(commandId, CommandName.Text.Trim(), CommandScript.Text,
            string.IsNullOrWhiteSpace(CommandFolder.Text) ? null : CommandFolder.Text.Trim(), CommandProfile.IsChecked == true, CommandEnabled.IsChecked == true)
        {
            Arguments = new[] { (CommandArg1.Text, CommandArgOptional1.IsChecked), (CommandArg2.Text, CommandArgOptional2.IsChecked), (CommandArg3.Text, CommandArgOptional3.IsChecked) }
                .Where(x => !string.IsNullOrWhiteSpace(x.Text)).Select(x => new SavedCommandArgument(x.Text.Trim(), x.IsChecked == true)).ToList()
        };
        commandsSaving = true;
        try
        {
            await commands.UpdateAsync(library => library.Upsert(command));
            RefreshCommands(command.Id); CommandStatus.Text = !userStore.Preferences.SavedCommandsEnabled
                ? "Command saved. Enable saved commands and Save changes to make it available." : !command.Enabled
                ? "Command saved and disabled." : "Command saved. It runs only when explicitly activated.";
        }
        catch (Exception ex) when (UserStore.IsStorageError(ex) || ex is ArgumentException) { CommandStatus.Text = ex.Message; }
        finally { commandsSaving = false; }
    }
    private async void DeleteCommand(object sender, RoutedEventArgs e)
    {
        if (commandsSaving || !commands.Current.Commands.Any(x => x.Id == commandId)) return;
        var id = commandId; var entryId = commands.Current.Commands.First(x => x.Id == id).EntryId;
        commandsSaving = true;
        try
        {
            await commands.UpdateAsync(library => library.Remove(id));
            await userStore.UpdatePreferencesAsync(p => LauncherCustomization.RemoveReferences(p, entryId));
            itemBindingRows.Remove(entryId); RefreshLauncherItems();
            RefreshCommands(); CommandStatus.Text = "Command deleted.";
        }
        catch (Exception ex) when (UserStore.IsStorageError(ex)) { CommandStatus.Text = ex.Message; }
        finally { commandsSaving = false; }
        if (!commands.Current.Commands.Any(x => x.Id == id)) NewCommand(sender, e);
    }

    private void DiscoveryChanged()
    { if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(RefreshDiscoveredApps); }
    private void RefreshDiscoveredApps()
    {
        DiscoveredApps.ItemsSource = discovery.Applications;
        AppStatus.Text = discovery.Warning ?? $"{discovery.Applications.Count} saved EXEs · Add/Forget apply immediately; the discovery switch applies on Save.";
    }
    private async Task ManageApps(Func<Task> operation)
    {
        try { await operation(); RefreshDiscoveredApps(); }
        catch (Exception ex) when (UserStore.IsStorageError(ex) || ex is InvalidOperationException or ArgumentException)
        { AppStatus.Text = ex.Message; }
    }
    private async void ScanApps(object sender, RoutedEventArgs e)
    { await ManageApps(async () => { await refreshStartMenu(); await discovery.ScanRunningAsync(); await discovery.RevalidateAsync(); }); }
    private async void RefreshStartMenu(object sender, RoutedEventArgs e) => await ManageApps(refreshStartMenu);
    private async void AddExe(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Add application", Filter = "Applications (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) await ManageApps(() => discovery.AddAsync(dialog.FileName));
    }
    private async void ForgetExe(object sender, RoutedEventArgs e)
    { if (sender is System.Windows.Controls.Button { Tag: string path }) await ManageApps(() => discovery.ForgetAsync(path)); }

    private void CategoryChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ready && ReferenceEquals(e.Source, Categories)) StopRecording();
    }

    private void StartRecording(object sender, RoutedEventArgs e)
    {
        if (saving || sender is not System.Windows.Controls.Button { Tag: BindingRow row } button) return;
        StopRecording();
        recordingRow = row; row.IsRecording = true;
        held.Clear(); detector.Cancel(); SaveButton.IsEnabled = false;
        RecorderOptions.Visibility = Visibility.Visible;
        Feedback.Text = "Recording \"" + row.Name + "\": press a shortcut or double-tap Ctrl, Alt, or Shift. Esc cancels.";
        button.Focus();
    }
    private void CancelRecording(object sender, RoutedEventArgs e)
    { StopRecording(); Feedback.Text = "Recording canceled. The current binding is unchanged."; }
    private void StopRecording()
    {
        if (recordingRow != null) recordingRow.IsRecording = false;
        recordingRow = null; held.Clear(); detector.Cancel(); SaveButton.IsEnabled = !saving;
        RecorderOptions.Visibility = Visibility.Collapsed;
    }
    private AppPreferences Draft(BindingRow? row = null, HotKeyBinding? candidate = null)
    {
        var map = windowRows.Where(x => x.Binding != null).ToDictionary(x => x.Action!.Value, x => x.Binding!);
        var palette = paletteRow.Binding;
        var items = itemBindingRows.Where(x => x.Value.Binding != null).ToDictionary(x => x.Key, x => x.Value.Binding!, StringComparer.OrdinalIgnoreCase);
        if (row != null)
        {
            if (row.Action is { } action) { if (candidate == null) map.Remove(action); else map[action] = candidate; }
            else if (row.EntryId is { } id) { if (candidate == null) items.Remove(id); else items[id] = candidate; }
            else palette = candidate;
        }
        return original with { PaletteBinding = palette, WindowBindings = map, EntryBindings = items, CustomWindowSizes = customSizes.ToList(), WindowLayouts = layouts.ToList(), AltSpaceFallback = Fallback.IsChecked == true };
    }
    private void Pick(HotKeyBinding picked)
    {
        if (saving || recordingRow == null) return;
        if (Draft(recordingRow, picked).Validate() is { } error) { Feedback.Text = error; return; }
        var name = recordingRow.Name;
        recordingRow.Binding = picked;
        if (recordingRow.EntryId is { } id) itemBindingRows[id] = recordingRow;
        StopRecording();
        Feedback.Text = "\"" + name + "\" recorded. Saving checks Windows registration before it takes effect.";
    }
    private void ResetBindings(object sender, RoutedEventArgs e)
    {
        if (saving) return;
        StopRecording(); paletteRow.Binding = new();
        foreach (var row in windowRows.Concat(itemBindingRows.Values)) row.Binding = null;
        Fallback.IsChecked = false; Fallback.Visibility = Visibility.Collapsed;
        Feedback.Text = "Defaults restored in this draft: only double Ctrl remains. Save to apply.";
    }
    private void ClearBinding(object sender, RoutedEventArgs e)
    {
        if (saving || sender is not System.Windows.Controls.Button { Tag: BindingRow row }) return;
        row.Binding = null;
        if (row.EntryId is { } id) itemBindingRows[id] = row;
        StopRecording();
        Feedback.Text = "\"" + row.Name + "\" cleared. Save to apply.";
    }

    private static int VirtualKey(System.Windows.Input.KeyEventArgs e) => KeyInterop.VirtualKeyFromKey(e.Key == Key.System ? e.SystemKey : e.Key);
    private static bool IsModifier(int key) => key is >= 160 and <= 165 or 91 or 92;
    private static int GeneralKey(int key) => key switch { 160 or 161 => 16, 162 or 163 => 17, 164 or 165 => 18, _ => key };
    private static double Now => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
    private void RecordDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (recordingRow == null || e.Key == Key.ImeProcessed) return;
        e.Handled = true;
        if (e.Key == Key.Escape) { StopRecording(); Feedback.Text = "Recording canceled."; return; }
        if (e.IsRepeat) return;
        var key = VirtualKey(e);
        if (IsModifier(key))
        {
            if (held.Add(key) && held.Count == 1 && key is >= 160 and <= 165) detector.Press(key, Now);
            else detector.Cancel();
            return;
        }
        detector.Cancel();
        var modifiers = Keyboard.Modifiers;
        uint mask = (modifiers.HasFlag(ModifierKeys.Alt) ? 1u : 0) | (modifiers.HasFlag(ModifierKeys.Control) ? 2u : 0)
            | (modifiers.HasFlag(ModifierKeys.Shift) ? 4u : 0) | (modifiers.HasFlag(ModifierKeys.Windows) ? 8u : 0);
        var candidate = new HotKeyBinding(BindingKind.Combo, key, mask);
        if (candidate.Validate() is { } error) Feedback.Text = error;
        else Pick(candidate);
    }
    private void RecordUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (recordingRow == null) return;
        e.Handled = true;
        var key = VirtualKey(e);
        if (!IsModifier(key)) return;
        if (held.Remove(key) && held.Count == 0 && detector.Release(key, Now))
            Pick(new(BindingKind.DoubleTap, GeneralKey(key), 0, DistinguishSide.IsChecked == true
                ? key % 2 == 0 ? KeySide.Left : KeySide.Right : KeySide.Any));
    }
    private void MousePressed(object sender, MouseButtonEventArgs e) { if (recordingRow != null) detector.Cancel(); }
    private async void Save(object sender, RoutedEventArgs e)
    {
        if (saving || commandsSaving || launcherSaving || layoutBusy || extensionBusy || recordingRow != null) return;
        if (!double.TryParse(WindowGap.Text, System.Globalization.CultureInfo.CurrentCulture, out var gap))
        { Feedback.Text = "Enter a valid window gap."; return; }
        var preferences = Draft() with { ExtensionsEnabled = EnableExtensions.IsChecked == true, ShowExtensions = ShowExtensionCommands.IsChecked == true, StartAtLogon = StartAtLogon.IsChecked == true, WindowManagementEnabled = WindowManagement.IsChecked == true,
            ShowWindowCommands = ShowWindows.IsChecked == true, ShowWindowLayouts = ShowLayouts.IsChecked == true, WindowGap = gap, HalfCycleMode = (WindowCycleMode)HalfCycleSelector.SelectedIndex, CycleHalfSizes = HalfCycleSelector.SelectedIndex == (int)WindowCycleMode.Sizes, ShowSuggestions = Suggestions.IsChecked == true, DiscoverRunningApplications = DiscoverApps.IsChecked == true,
            CalculatorEnabled = EnableCalculator.IsChecked == true, CurrencyConversionEnabled = EnableCurrency.IsChecked == true,
            CalculationHistoryEnabled = EnableCalcHistory.IsChecked == true, ShellCommandsEnabled = EnableShell.IsChecked == true,
            ShellFallbackEnabled = EnableShellFallback.IsChecked == true, SystemCommandsEnabled = EnableSystem.IsChecked == true,
            SavedCommandsEnabled = EnableSavedCommands.IsChecked == true, ShowSavedCommands = ShowSavedCommands.IsChecked == true, ApplicationsEnabled = EnableApplications.IsChecked == true,
            ActionsPanelEnabled = EnableActionsPanel.IsChecked == true, ApplicationFolders = applicationFolders.ToList(),
            ShowApplicationIcons = ShowApplicationIcons.IsChecked == true, ApplicationFolderDepth = FolderDepth.SelectedIndex, IncludeWindowsAppPaths = IncludeAppPaths.IsChecked == true, IncludePackagedApplications = IncludePackagedApps.IsChecked == true, MatchSensitivity = SensitivityHigh.IsChecked == true ? SearchSensitivity.High : SensitivityLow.IsChecked == true ? SearchSensitivity.Low : SearchSensitivity.Medium };
        if (preferences.Validate() is { } invalid) { Feedback.Text = invalid; return; }
        saving = true; SaveButton.IsEnabled = Categories.IsEnabled = false;
        try { Feedback.Text = await apply(preferences) ?? "Saved. These settings return on the next launch."; StartupStatus.Text = startupStatus?.Invoke(); }
        catch (Exception ex) { Feedback.Text = "Save failed: " + ex.Message; }
        finally { saving = false; SaveButton.IsEnabled = Categories.IsEnabled = true; }
    }
    private void WindowClosing(object? sender, CancelEventArgs e) { if (saving || commandsSaving || launcherSaving || extensionBusy) e.Cancel = true; else StopRecording(); }
}
