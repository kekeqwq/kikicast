using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Data;
using Kikicast.Core;
using Kikicast.Windows;

namespace Kikicast.App;

public partial class MainWindow : Window
{
    private sealed record Row(string Title, string Subtitle, LauncherEntry? Entry = null, string? Answer = null,
        string? Command = null, WindowAction? WindowAction = null, string Section = "Results", string? FavoriteChord = null, bool IsFavorite = false, CurrencyQuery? Currency = null, string? ShellText = null, SavedCommand? Custom = null, CalculationVisit? HistoryItem = null, string? Alias = null, string? GlobalChord = null, CustomWindowSize? WindowSize = null, WindowLayout? Layout = null, ExtensionCommand? Extension = null)
    {
        public bool IsCurrency => Currency != null;
        public string CurrencyOutput => Title;
        public string? CurrencyInput => Currency?.InputLabel;
        public string? Id => Extension != null ? Extension.EntryId : Layout != null ? Layout.EntryId : WindowSize != null ? WindowSize.EntryId : Custom != null ? Custom.EntryId : Command == "shell-query" ? null : Entry != null ? Entry.Id
            : WindowAction is { } action ? LauncherSections.WindowId(action) : Command != null ? "cmd:" + Command : null;
        public string UsageKey => Entry?.UsageKey ?? Id ?? "";
        public bool ShowSubtitle => Entry == null && WindowAction == null;
        public string RightLabel => FavoriteChord ?? GlobalChord ?? (Extension != null ? "Extension" : Entry != null ? "Application" : Layout != null ? "Window layout" : WindowAction != null || WindowSize != null ? "Window command" : Custom != null ? "Custom command" : Answer != null ? "Calculation" : "Command");
        public string DisplayTitle => (IsFavorite ? "★ " : "") + Title + (string.IsNullOrWhiteSpace(Alias) ? "" : " · " + Alias);
    }
    private bool updatingPreferences, categoryQuery;
    private readonly UserStore store;
    private readonly ApplicationIconCache applicationIcons;
    public static readonly DependencyProperty IconsEnabledProperty = DependencyProperty.Register(nameof(IconsEnabled), typeof(bool), typeof(MainWindow), new PropertyMetadata(true));
    public bool IconsEnabled { get => (bool)GetValue(IconsEnabledProperty); private set => SetValue(IconsEnabledProperty, value); }
    private readonly Task indexTask;
    private readonly CurrencyRateService currencyRates;
    private readonly RunningApplicationDiscovery discovery;
    private readonly SavedCommandStore commands;
    private readonly ExtensionStore extensions;
    private IReadOnlyList<LauncherEntry> startMenuEntries = [];
    private Task? activeIndexRefresh;
    private bool indexRefreshAgain;
    private DateTimeOffset indexedAt;
    private bool fetchingRates;
    private readonly WindowManager windowManager = new();
    private readonly EnglishInputSession inputSession = new();
    public bool IsEnglishInputActive => inputSession.IsActive && inputSession.IsEnglish && !InputMethod.GetIsInputMethodEnabled(Query);
    public bool IsExecuting => executing;
    internal string ActivationDiagnostic => Status.Text + " · " + lastDismissReason;
    private string? lastDismissReason;
    internal bool HasBackdrop => FrostImage.Source != null;
    internal bool WantsBackdrop => TryFindResource("BackdropVisibility") is Visibility.Visible;
    public bool HasQueryFocus => Query.IsKeyboardFocused && WindowActivation.IsForeground(new WindowInteropHelper(this).Handle);
    internal ForegroundTarget? InvocationTarget => target;
    private SourceInputState? sourceInput;
    private bool opening, dismissing, focusArmed;
    private bool historyMode;
    private string shortcutLabel = "Double Ctrl";
    public event Action? SettingsRequested;
    public event Action<string, bool>? ShellRequested;
    public event Action<string>? ActionFailed;
    public event Action<string>? ActionCompleted;
    private IReadOnlyList<LauncherEntry> entries = [];
    private ForegroundTarget? target;
    private bool executing;
    private bool ready;
    private bool composing;
    private string? persistentStatus;

    public MainWindow(UserStore store, string cacheDirectory, RunningApplicationDiscovery discovery, SavedCommandStore commands, ExtensionStore extensions)
    {
        this.store = store;
        this.discovery = discovery; this.commands = commands; this.extensions = extensions;
        extensions.Changed += ExtensionsChanged;
        commands.Changed += DiscoveredAppsChanged;
        discovery.Changed += DiscoveredAppsChanged;
        discovery.StartMenuInvalidated += StartMenuInvalidated;
        currencyRates = new CurrencyRateService(System.IO.Path.Combine(cacheDirectory, "currency-rates.json"));
        applicationIcons = new(Dispatcher);
        InitializeComponent();
        ((ApplicationIconConverter)Resources["ApplicationIconConverter"]).Cache = applicationIcons;
        ready = true;
        TextCompositionManager.AddPreviewTextInputStartHandler(Query, (_, _) => composing = true);
        TextCompositionManager.AddPreviewTextInputHandler(Query, (_, _) => composing = false);
        SourceInitialized += (_, _) =>
        {
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(InputMessages);
        };
        InputLanguageManager.Current.InputLanguageChanging += InputLanguageChanging;
        Closed += (_, _) =>
        {
            extensions.Changed -= ExtensionsChanged;
            applicationIcons.Dispose(); inputSession.End(); currencyRates.Dispose(); discovery.Changed -= DiscoveredAppsChanged; discovery.StartMenuInvalidated -= StartMenuInvalidated; commands.Changed -= DiscoveredAppsChanged;
            InputLanguageManager.Current.InputLanguageChanging -= InputLanguageChanging;
        };
        indexTask = LoadIndex();
    }

    private Task LoadIndex()
    {
        if (activeIndexRefresh is { IsCompleted: false }) { indexRefreshAgain = true; return activeIndexRefresh; }
        return activeIndexRefresh = RefreshIndexCoreAsync();
    }
    private async Task RefreshIndexCoreAsync()
    {
        do
        {
            indexRefreshAgain = false;
            Status.Text = "Indexing applications…";
            var enabled = store.Preferences.ApplicationsEnabled;
            var folders = store.Preferences.ApplicationFolders.ToArray();
            var depth = store.Preferences.ApplicationFolderDepth;
            var appPaths = store.Preferences.IncludeWindowsAppPaths;
            var packaged = store.Preferences.IncludePackagedApplications;
            startMenuEntries = enabled ? await Task.Run(() => ApplicationIndex.Scan(folders, depth, appPaths, includePackagedApplications: packaged)) : [];
            await discovery.SetKnownApplicationsAsync(startMenuEntries);
            applicationIcons.Invalidate();
            indexedAt = DateTimeOffset.UtcNow;
            MergeApplications();
            Status.Text = persistentStatus ?? $"{entries.Count} apps · {shortcutLabel}";
        } while (indexRefreshAgain);
    }
    public Task RefreshApplicationsAsync() => LoadIndex();
    private void StartMenuInvalidated()
    { if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(() => { _ = LoadIndex(); }); }
    private void DiscoveredAppsChanged()
    {
        if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(MergeApplications);
    }
    private void MergeApplications()
    {
        var selectedId = (Results.SelectedItem as Row)?.Id;
        var covered = startMenuEntries.Where(x => x.ExecutablePath != null).Select(x => x.ExecutablePath!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        entries = startMenuEntries.Concat(discovery.Applications.Where(x => !covered.Contains(x.Path))
            .Select(x => new LauncherEntry(x.Name, x.Path, x.Path))).DistinctBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var entry in entries) _ = entry.SearchFields;
        foreach (var command in commands.Current.Commands) _ = command.SearchFields;
        Refresh();
        var index = Results.Items.Cast<Row>().ToList().FindIndex(x => x.Id == selectedId);
        if (index >= 0) Results.SelectedIndex = index;
    }

    public void SetStatus(string text) { persistentStatus = text; Status.Text = text; }
    public void SetShortcutLabel(AppPreferences preferences)
    {
        shortcutLabel = (preferences.PaletteBinding?.Display ?? "No activation shortcut") + (preferences.AltSpaceFallback ? " / Alt+Space" : "");
        if (persistentStatus == null) Status.Text = shortcutLabel;
    }
    public void HideForSettings() { if (IsVisible) Dismiss(true); }
    public void ShowHistory()
    {
        if (!store.Preferences.CalculationHistoryEnabled) return;
        if (!IsVisible) Toggle();
        historyMode = true;
        Query.Clear(); Refresh(); Query.Focus();
    }

    public void ShowExtensions() { if (!IsVisible) Toggle(); if (IsVisible) { Query.Text = "Extensions"; Query.Focus(); } }
    public void Toggle() => ToggleFrom(ForegroundTarget.Capture());

    // The controlled smoke fixture can supply an owned target. Production captures before Show.
    internal void ToggleFrom(ForegroundTarget? capturedTarget)
    {
        if (executing) return;
        if (IsVisible) { Dismiss(true); return; }

        _ = discovery.RevalidateAsync();
        if (DateTimeOffset.UtcNow - indexedAt > TimeSpan.FromSeconds(30)) _ = LoadIndex();
        target = capturedTarget;
        sourceInput = capturedTarget == null ? null : SourceInputState.Capture(capturedTarget.Handle);
        opening = true; focusArmed = false;
        historyMode = false; composing = false;
        Query.Clear(); Refresh();
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + Math.Max(20, area.Height * .18);
        try
        {
            var hwnd = new WindowInteropHelper(this).EnsureHandle();
            FrostImage.Source = TryFindResource("BackdropVisibility") is Visibility.Visible ? PaletteBackdrop.Capture(hwnd, 20) : null;
            Show();
            InputMethod.SetIsInputMethodEnabled(Query, false);
            if (!inputSession.Begin(hwnd)) SetStatus("Add an English keyboard in Windows. IME is disabled here.");
            if (!WindowActivation.Once(hwnd, focusHost: true))
            { Dismiss(true, "activation denied"); ActionFailed?.Invoke("Windows denied activation. Please try the shortcut again."); return; }
            Query.Focus(); Keyboard.Focus(Query);
            inputSession.Enforce();
            focusArmed = HasQueryFocus;
            if (!focusArmed) { Dismiss(true, "query focus failed"); ActionFailed?.Invoke("The search box did not receive focus. Please try again."); }
        }
        finally { opening = false; }
    }

    private void Dismiss(bool restore, string? reason = null)
    {
        if (dismissing) return;
        lastDismissReason = reason ?? "explicit";
        dismissing = true; focusArmed = false;
        var original = target; var originalInput = sourceInput;
        target = null; sourceInput = null;
        try
        {
            CloseActions(focus: false); ClearArguments();
            inputSession.End();
            Hide(); FrostImage.Source = null; composing = false;
            if (restore && original != null) { original.Restore(); originalInput?.Restore(original); }
        }
        finally { dismissing = false; }
    }

    private void SurfaceSizeChanged(object sender, SizeChangedEventArgs e)
    {
        PaletteSurface.Clip = new System.Windows.Media.RectangleGeometry(new System.Windows.Rect(e.NewSize), 22, 22);
    }

    private void QueryLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    { if (focusArmed && !opening && !dismissing && IsVisible
        && (e.NewFocus is not DependencyObject node || Window.GetWindow(node) != this)) Dismiss(true, "external query focus"); }

    private void PaletteMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Result rows remain mouse-selectable without taking keyboard focus from the query.
        var node = e.OriginalSource as DependencyObject;
        while (node != null)
        {
            if (node == ActionPanel) return;
            if (node == ArgumentStrip) { CloseActions(focus: false); return; }
            if (node == Query) { CloseActions(focus: false); return; }
            if (node is System.Windows.Controls.ListBoxItem item)
            {
                if (System.Windows.Controls.ItemsControl.ItemsControlFromItemContainer(item) != ActionList) CloseActions();
                return;
            }
            node = node is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }
        e.Handled = true; Dismiss(true);
    }

    private void QueryChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    { if (ready) Refresh(); }

    private void ExtensionsChanged() { if (ready && !Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(RefreshKeepingSelection); }
    private static LauncherKind Kind(Row row) => row.Extension != null ? LauncherKind.Extension : row.Entry != null ? LauncherKind.Application
        : row.Layout != null ? LauncherKind.WindowLayout : row.WindowAction != null || row.WindowSize != null ? LauncherKind.WindowCommand : row.Custom != null ? LauncherKind.CustomCommand : LauncherKind.Command;
    private void SetRows(List<Row> rows)
    {
        var view = new ListCollectionView(rows);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Row.Section)));
        Results.ItemsSource = view;
        Results.SelectedIndex = rows.Count > 0 ? 0 : -1;
    }
    private void Refresh()
    {
        IconsEnabled = store.Preferences.ApplicationsEnabled && store.Preferences.ShowApplicationIcons;
        applicationIcons.SetEnabled(IconsEnabled);
        applicationIcons.SetPackagedEnabled(store.Preferences.IncludePackagedApplications);
        var query = Query.Text.Trim();
        categoryQuery = false;
        SearchHint.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (historyMode && !store.Preferences.CalculationHistoryEnabled) historyMode = false;
        if (historyMode)
        {
            SetRows(store.History.Calculations.Where(x => x.Expression.Contains(query, StringComparison.OrdinalIgnoreCase)
                || x.Answer.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Select(x => new Row(x.Answer, x.Expression + " · Enter to copy", Answer: x.Answer, Section: "Calculation history", HistoryItem: x)).ToList());
            Status.Text = "Calculation history · Esc to search · Ctrl+, settings";
            return;
        }
        var now = DateTimeOffset.UtcNow;
        var candidates = entries.Where(x => x.IsEnabled(store.Preferences)).Select(x => new Row(x.Name, x.Path, x)).ToList();
        if (store.Preferences.WindowManagementEnabled && store.Preferences.ShowWindowCommands)
        {
            candidates.AddRange(WindowGeometry.Commands.Select(x => new Row(x.Name, "Window management"
                + (store.Preferences.WindowBindings.TryGetValue(x.Action, out var shortcut) ? " · " + shortcut.Display : ""), WindowAction: x.Action)));
            candidates.AddRange(store.Preferences.CustomWindowSizes.Where(x => x.Enabled).Select(x => new Row(x.Name, x.Summary, WindowSize: x)));
        }
        if (store.Preferences.WindowManagementEnabled && store.Preferences.ApplicationsEnabled && store.Preferences.ShowWindowLayouts)
            candidates.AddRange(store.Preferences.WindowLayouts.Where(x => x.Enabled).Select(x => new Row(x.Name, x.Summary, Layout: x)));
        if (store.Preferences.ExtensionsEnabled && store.Preferences.ShowExtensions)
            candidates.AddRange(extensions.Commands().Select(x => new Row(x.Title, "Windows/.NET extension · Explicit Enter to run", Extension: x)));
        candidates.AddRange(BuiltInRows());
        if (store.Preferences.SavedCommandsEnabled)
            candidates.AddRange(commands.Current.Visible(store.Preferences)
                .Select(x => new Row(x.Name, "Custom Command · PowerShell 7 · Enter to run", Custom: x)));
        var aliases = AliasProfiles();
        var hidden = store.Preferences.HiddenEntryKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        candidates = candidates.Where(x => x.Command == "settings" || !hidden.Contains(x.Id!))
            .Select(x => x with { Alias = aliases.TryGetValue(x.Id!, out var alias) ? alias.Text : null,
                GlobalChord = x.WindowAction is { } action ? store.Preferences.WindowBindings.GetValueOrDefault(action)?.Display : LauncherBindings.Get(store.Preferences, x.Id!)?.Display }).ToList();
        var byId = candidates.DistinctBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Id!, StringComparer.OrdinalIgnoreCase);
        var visits = store.History.Launches.GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        LaunchVisit? Visit(Row row) => visits.GetValueOrDefault(row.UsageKey);
        double Usage(string id) => LauncherUsage.Score(Visit(byId[id]), now);
        var favorites = store.Preferences.FavoriteKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        LauncherItem Describe(Row row) => new(row.Id!, row.Title, Kind(row),
            HasHotKey: row.WindowAction is { } action && store.Preferences.WindowBindings.ContainsKey(action) || LauncherBindings.Get(store.Preferences, row.Id!) != null, CanSuggest: row.Command != "settings", HasAlias: !string.IsNullOrWhiteSpace(row.Alias));
        LauncherKind? category = SearchProfile.Normalize(query) switch
        {
            "app" or "application" or "applications" => LauncherKind.Application,
            "window" or "windowcommand" or "windowcommands" or "windowmanagement" => LauncherKind.WindowCommand,
            "extension" or "extensions" => LauncherKind.Extension,
            "customcommand" or "customcommands" => LauncherKind.CustomCommand,
            "layout" or "layouts" or "windowlayout" or "windowlayouts" => LauncherKind.WindowLayout,
            "command" or "commands" => LauncherKind.Command, _ => null
        };
        var rows = new List<Row>();
        if (query.Length == 0 || category != null)
        {
            categoryQuery = category != null;
            var source = byId.Values.Where(x => category == null || Kind(x) == category
                || x.Title.Equals(query, StringComparison.OrdinalIgnoreCase));
            var ordered = LauncherSections.Empty(source.Select(Describe), category == null ? store.Preferences.FavoriteKeys : [],
                Usage, now, category == null && store.Preferences.ShowSuggestions);
            rows.AddRange(ordered.Select(x => byId[x.Item.Id] with { Section = x.Section, FavoriteChord = x.FavoriteChord,
                IsFavorite = favorites.Contains(x.Item.Id) }));
        }
        else
        {
            var currency = store.Preferences.CalculatorEnabled && store.Preferences.CurrencyConversionEnabled ? CurrencyConversion.Parse(query) : null;
            if (currency != null)
            {
                var snapshot = currencyRates.Current;
                var converted = currency.Answer(snapshot);
                var date = currency.From == currency.To ? "Same currency · No exchange rate needed" : snapshot == null ? "No cached rates" : $"Frankfurter · Rates dated {snapshot.RateDate:yyyy-MM-dd}";
                if (converted != null) rows.Add(new(converted + " " + currency.To, date + " · Enter to copy"
                    + (snapshot != null && DateTimeOffset.UtcNow - snapshot.FetchedAt > TimeSpan.FromHours(24) ? " · Cached/stale" : ""),
                    Answer: converted, Section: "Calculator", Currency: currency));
                else rows.Add(new(currencyRates.NeedsRefresh || fetchingRates ? "Loading exchange rates…" : snapshot != null
                    ? "No rate for this currency in the feed" : currencyRates.Error ?? "Exchange rate unavailable",
                    date + " · Daily reference rates, not trading quotes", Section: "Calculator", Currency: currency));
                if (currency.From != currency.To && currencyRates.NeedsRefresh && !fetchingRates) FetchCurrencyRates();
            }
            else
            {
                var answer = store.Preferences.CalculatorEnabled ? Calculator.Evaluate(query) : null;
                if (answer != null) rows.Add(new(answer, "Calculation · Enter to copy", Answer: answer, Section: "Calculator"));
            }
            rows.AddRange(LauncherOrder.Ranked(byId.Values, query, store.Preferences.MatchSensitivity, 60, SearchFields,
                row => new LauncherSearchSignals(row.Title, Usage(row.Id!), LauncherUsage.Terms(Visit(row), now),
                    aliases.TryGetValue(row.Id!, out var alias) ? alias.Profile : null, row.Entry != null ? 4 : 3))
                .Select(row => row with { IsFavorite = favorites.Contains(row.Id!) }));
            if (store.Preferences.ShellCommandsEnabled && store.Preferences.ShellFallbackEnabled)
                rows.Add(new("Run Shell Command", "Enter to run in PowerShell 7 · Interactive system terminal", Command: "shell-query",
                Section: "Use query with…", ShellText: PowerShellCommand.FromQuery(query)));
        }
        SetRows(rows);
        Status.Text = persistentStatus ?? shortcutLabel + " · Ctrl+Shift+F favorite · Ctrl+, settings";
    }

    private async void FetchCurrencyRates()
    {
        fetchingRates = true;
        try
        {
            await currencyRates.RefreshAsync();
            var selectedId = (Results.SelectedItem as Row)?.Id;
            var index = Results.SelectedIndex;
            Refresh();
            var displayed = Results.Items.Cast<Row>().ToList();
            var restored = selectedId == null ? index : displayed.FindIndex(x => x.Id == selectedId);
            if (restored >= 0 && restored < displayed.Count) Results.SelectedIndex = restored;
        }
        finally { fetchingRates = false; }
    }

    private async void ChangeFavorite(int direction = 0)
    {
        if (updatingPreferences || historyMode || Results.SelectedItem is not Row { Id: { } id } row) return;
        var visible = Results.Items.Cast<Row>().Where(x => x.Section == "Favorites").Select(x => x.Id!).ToList();
        if (direction != 0 && (Query.Text.Length != 0 || !visible.Contains(id))) return;
        var oldIndex = Results.SelectedIndex;
        updatingPreferences = true;
        try
        {
            await store.UpdatePreferencesAsync(p => p with { FavoriteKeys = direction == 0
                ? LauncherSections.ToggleFavorite(p.FavoriteKeys, id) : LauncherSections.MoveFavorite(p.FavoriteKeys, visible, id, direction) });
            Refresh();
            var displayed = Results.Items.Cast<Row>().ToList();
            var index = direction != 0 ? displayed.FindIndex(x => x.Id == id)
                : store.Preferences.FavoriteKeys.Contains(id) && Query.Text.Length == 0 ? displayed.FindIndex(x => x.Section == "Favorites")
                : Math.Clamp(oldIndex - 1, 0, Math.Max(0, displayed.Count - 1));
            if (index >= 0 && displayed.Count > 0) { Results.SelectedIndex = index; Results.ScrollIntoView(Results.SelectedItem); }
        }
        catch (Exception ex) when (UserStore.IsStorageError(ex)) { SetStatus("Favorites were not saved: " + ex.Message); ActionFailed?.Invoke(Status.Text); }
        finally { updatingPreferences = false; }
    }
    internal async Task VerifyLauncherSectionsAsync()
    {
        await indexTask;
        var originalEntries = entries; var preferences = store.Preferences;
        try
        {
            entries = new[] { "Alpha", "Beta", "Gamma", "Delta" }.Select(x => new LauncherEntry(x, "fixture:" + x)).ToArray();
            var keys = entries.Select(x => LauncherSections.ApplicationId(x.Path)).ToArray();
            await store.UpdatePreferencesAsync(p => p with { WindowManagementEnabled = true, ShowSuggestions = true,
                FavoriteKeys = [keys[0], "missing-fixture", keys[1]], WindowBindings = [] });
            store.RecordLaunch(entries[2].Path, "gamma");
            Query.Clear(); Refresh();
            var rows = Results.Items.Cast<Row>().ToList();
            if (rows.Count != 4 + WindowGeometry.Commands.Count + 5 || rows[0].Id != keys[0] || rows[1].Id != keys[1]
                || rows[0].FavoriteChord != "Ctrl+1" || rows[1].FavoriteChord != "Ctrl+2"
                || rows[2].Id != keys[2] || rows[2].Section != "Suggestions"
                || rows.Select(x => x.Id).Distinct().Count() != rows.Count
                || ((ListCollectionView)Results.ItemsSource).Groups?.Count != 5)
                throw new InvalidOperationException("Grouped launcher rows, suggestions, or favorite slots disagree");
            SelectRelative(1, section: true);
            if (Results.SelectedItem is not Row { Section: "Suggestions" }) throw new InvalidOperationException("Group navigation selected a header or neighbor");
            Query.Text = "applications";
            if (Results.Items.Count != 4 || Results.Items.Cast<Row>().Any(x => x.Section != "Applications" || x.FavoriteChord != null))
                throw new InvalidOperationException("Category search pinned favorites or lost applications");
            Query.Text = "alpha";
            if (Results.SelectedItem is not Row { IsFavorite: true, FavoriteChord: null }) throw new InvalidOperationException("Query lost the favorite marker");
            Query.Text = "open trash";
            if (!Results.Items.Cast<Row>().Any(x => x.Command == RecycleBinCommand.OpenId)) throw new InvalidOperationException("Open Trash alias was not searchable");
            Query.Text = "empty trash bin";
            if (!Results.Items.Cast<Row>().Any(x => x.Command == RecycleBinCommand.EmptyId)) throw new InvalidOperationException("Empty Trash alias was not searchable");
            Query.Text = "1 usd to usd";
            if (Results.SelectedItem is not Row { Answer: "1.00", IsCurrency: true } || fetchingRates)
                throw new InvalidOperationException("Same-currency card failed or unnecessarily requested network rates");
            Query.Text = "pwsh Get-Date";
            if (!Results.Items.Cast<Row>().Any(x => x.Command == "shell-query" && x.ShellText == "Get-Date" && x.Id == null))
                throw new InvalidOperationException("Shell fallback lost its script or became a saved favorite");
            await store.UpdatePreferencesAsync(p => p with { ShowSuggestions = false });
            Query.Clear(); Refresh();
            if (Results.Items.Cast<Row>().Any(x => x.Section == "Suggestions")) throw new InvalidOperationException("Suggestion switch was ignored");
        }
        finally
        {
            entries = originalEntries;
            await store.SavePreferencesAsync(preferences);
            Query.Clear(); Refresh();
        }
    }

    internal async Task VerifyFeatureSettingsAsync()
    {
        var preferences = store.Preferences; var library = commands.Current;
        var malformed = System.IO.Path.Combine(store.DirectoryPath, "OwnedInvalidSchema");
        System.IO.Directory.CreateDirectory(malformed);
        const string invalidSchema = "{\"Version\":99}";
        System.IO.File.WriteAllText(System.IO.Path.Combine(malformed, "settings.json"), invalidSchema);
        System.IO.File.WriteAllText(System.IO.Path.Combine(malformed, "history.json"), invalidSchema);
        var damaged = new UserStore(malformed);
        if (damaged.LoadWarning == null) throw new InvalidOperationException("Invalid storage schema was not reported");
        damaged.RecordCalculation("owned fixture", "1"); await damaged.FlushAsync();
        if (System.IO.File.ReadAllText(System.IO.Path.Combine(malformed, "history.json")) != invalidSchema
            || System.IO.File.ReadAllText(System.IO.Path.Combine(malformed, "settings.json")) != invalidSchema)
            throw new InvalidOperationException("Invalid storage original was overwritten");
        var saved = new SavedCommand(Guid.NewGuid(), "Owned saved command", "Write-Output 'private-script-marker'", LoadProfile: false);
        try
        {
            await commands.UpdateAsync(x => x.Upsert(saved));
            await store.UpdatePreferencesAsync(p => p with { SavedCommandsEnabled = true, ShowSuggestions = false, FavoriteKeys = [saved.EntryId] });
            Query.Clear(); Refresh();
            if (Results.Items[0] is not Row { Custom: not null, FavoriteChord: "Ctrl+1" }) throw new InvalidOperationException("Saved command lost its favorite slot");
            Query.Text = "private-script-marker";
            if (Results.Items.Cast<Row>().Any(x => x.Custom != null)) throw new InvalidOperationException("Script text was indexed");
            await store.UpdatePreferencesAsync(p => p with { ShowSavedCommands = false, FavoriteKeys = [] });
            Query.Clear(); Refresh();
            if (Results.Items.Cast<Row>().Any(x => x.Custom != null)) throw new InvalidOperationException("Hidden saved commands remain visible");
            await store.UpdatePreferencesAsync(p => p with { FavoriteKeys = [saved.EntryId] }); Refresh();
            if (Results.Items[0] is not Row { Custom: not null }) throw new InvalidOperationException("Launcher visibility hid a saved favorite");
            await commands.UpdateAsync(x => x.Upsert(saved with { Enabled = false })); Refresh();
            if (Results.Items.Cast<Row>().Any(x => x.Custom != null)) throw new InvalidOperationException("Disabled command remained visible");
            await commands.UpdateAsync(x => x.Upsert(saved));
            await store.UpdatePreferencesAsync(p => p with { SavedCommandsEnabled = false, CalculatorEnabled = false,
                CalculationHistoryEnabled = false, ShellCommandsEnabled = false, SystemCommandsEnabled = false });
            Query.Clear(); Refresh();
            if (Results.Items.Cast<Row>().Any(x => x.Custom != null || x.Command is "history" or "shell" || x.Command == RecycleBinCommand.OpenId || x.Command == RecycleBinCommand.EmptyId))
                throw new InvalidOperationException("Disabled features remained in launcher");
            Query.Text = "1+2";
            if (Results.Items.Cast<Row>().Any(x => x.Answer != null || x.Command == "shell-query")) throw new InvalidOperationException("Disabled calculation or shell fallback remained available");
            var before = store.History.Calculations.Count; store.RecordCalculation("owned disabled-history fixture", "3");
            if (store.History.Calculations.Count != before) throw new InvalidOperationException("History disabled but still recorded");
            await store.UpdatePreferencesAsync(p => p with { CalculatorEnabled = true, CurrencyConversionEnabled = false, ShellCommandsEnabled = true, ShellFallbackEnabled = false });
            Query.Text = "1 usd to usd";
            if (Results.Items.Cast<Row>().Any(x => x.Currency != null || x.Command == "shell-query")) throw new InvalidOperationException("Currency or fallback switch ignored");
            Query.Text = "1+2";
            if (Results.Items[0] is not Row { Answer: "3" }) throw new InvalidOperationException("Currency switch disabled ordinary calculator");
        }
        finally
        {
            await commands.UpdateAsync(_ => library); await store.SavePreferencesAsync(preferences);
            Query.Clear(); Refresh();
        }
    }

    private void SelectRelative(int delta, bool section = false)
    {
        if (Results.Items.Count == 0) return;
        var index = Results.SelectedIndex;
        if (section)
        {
            var rows = Results.Items.Cast<Row>().ToList();
            var boundaries = Enumerable.Range(0, rows.Count).Where(i => i == 0 || rows[i].Section != rows[i - 1].Section);
            index = delta > 0 ? boundaries.FirstOrDefault(i => i > index, rows.Count - 1) : boundaries.LastOrDefault(i => i < index, 0);
        }
        else index = Math.Clamp(index + delta, 0, Results.Items.Count - 1);
        Results.SelectedIndex = index; Results.ScrollIntoView(Results.SelectedItem);
    }

    private void HandleKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // IME must finish composition before Enter can activate a row.
        if (composing || e.Key == Key.ImeProcessed) return;
        var modifiers = Keyboard.Modifiers;
        if (HandleActionKey(e, modifiers)) return;
        if (modifiers == ModifierKeys.Control && e.Key == Key.K && store.Preferences.ActionsPanelEnabled)
        { e.Handled = true; if (!e.IsRepeat) OpenActions(); }
        else if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.H && CanHide(Results.SelectedItem as Row))
        { e.Handled = true; if (!e.IsRepeat) _ = HideSelectedAsync(); }
        else if (modifiers == ModifierKeys.Control && e.Key == Key.Enter && Results.SelectedItem is Row { Entry: not null } reveal)
        { e.Handled = true; if (!e.IsRepeat) Reveal(reveal); }
        else if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.F)
        { e.Handled = true; if (!e.IsRepeat) ChangeFavorite(); }
        else if (modifiers == (ModifierKeys.Control | ModifierKeys.Alt) && e.Key is Key.Up or Key.Down)
        { e.Handled = true; ChangeFavorite(e.Key == Key.Down ? 1 : -1); }
        else if (modifiers == ModifierKeys.Control && e.Key is Key.Up or Key.Down)
        { e.Handled = true; SelectRelative(e.Key == Key.Down ? 1 : -1, section: true); }
        else if (modifiers == ModifierKeys.Control && e.Key is Key.N or Key.P)
        { e.Handled = true; SelectRelative(e.Key == Key.N ? 1 : -1); }
        else if (!historyMode && Query.Text.Length == 0 && modifiers == ModifierKeys.Control
            && KeyInterop.VirtualKeyFromKey(e.Key) is >= 48 and <= 57)
        {
            e.Handled = true;
            var slot = (KeyInterop.VirtualKeyFromKey(e.Key) - 49 + 10) % 10;
            var favorite = Results.Items.Cast<Row>().Where(x => x.Section == "Favorites").ElementAtOrDefault(slot);
            if (favorite != null) { Results.SelectedItem = favorite; Execute(); }
        }
        else if (modifiers == ModifierKeys.Control && e.Key == Key.OemComma)
        { e.Handled = true; if (!updatingPreferences) SettingsRequested?.Invoke(); }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.H)
        { e.Handled = true; ShowHistory(); }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (historyMode) { historyMode = false; Query.Clear(); Refresh(); }
            else Dismiss(true);
        }
        else if (e.Key is Key.Up or Key.Down)
        {
            SelectRelative(e.Key == Key.Down ? 1 : -1);
            e.Handled = true;
        }
        else if (e.Key is Key.PageUp or Key.PageDown && modifiers == ModifierKeys.None)
        { e.Handled = true; SelectRelative(e.Key == Key.PageDown ? 8 : -8); }
        else if (e.Key == Key.Enter && !e.IsRepeat) { e.Handled = true; Execute(); }
    }

    private void ActivateSelection(object sender, MouseButtonEventArgs e) => Execute();

    private async void Execute() => await ExecuteRowAsync(Results.SelectedItem as Row);

    private async Task ExecuteRowAsync(Row? row, bool global = false, ForegroundTarget? capturedWindow = null)
    {
        if (executing || updatingPreferences || row == null) return;
        var learnedQuery = global || categoryQuery ? null : Query.Text.Trim();
        if (row.WindowAction is { } action) { await RunWindowActionAsync(action, global, capturedWindow); return; }
        if (row.WindowSize is { } size) { await RunWindowCommandAsync(null, size.Id, global, capturedWindow); return; }
        if (row.Layout is { } layout) { await RunLayoutAsync(layout.Id, global, learnedQuery); return; }
        executing = true;
        try
        {
            if (row.Extension is { } pluginCommand)
            {
                var current = store.Preferences.ExtensionsEnabled ? extensions.Resolve(pluginCommand.EntryId) : null;
                if (current == null) return;
                Dismiss(true);
                var confirmed = !current.Value.Command.Destructive || System.Windows.MessageBox.Show("Replace the current source with a different valid image, then send the verified prior source to the Recycle Bin?\n\nReplacement must succeed for every target still using the prior image. External wallpaper changes/source changes refuse deletion. No permanent-delete fallback.", current.Value.Command.Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
                if (!confirmed) return;
                var result = await ExtensionClient.ExecuteAsync(extensions, pluginCommand.EntryId, () => store.Preferences.ExtensionsEnabled, confirmed);
                if (result.Success) { store.RecordLaunch(pluginCommand.EntryId, learnedQuery); ActionCompleted?.Invoke(result.Summary); }
                else ActionFailed?.Invoke(result.Summary);
                SetStatus(result.Summary);
            }
            else if (row.Custom is { } saved)
            {
                var current = commands.Current.Runnable(saved.Id, store.Preferences);
                if (current == null) return;
                var script = BoundScript(current); if (script == null) return;
                Dismiss(true);
                PowerShellRunner.LaunchInteractive(script, current.WorkingDirectory, current.LoadProfile);
                store.RecordLaunch(current.EntryId, learnedQuery);
            }
            else if (row.Command == "settings")
            { store.RecordLaunch(row.UsageKey, learnedQuery); executing = false; SettingsRequested?.Invoke(); }
            else if (row.Command == "history")
            { if (!store.Preferences.CalculationHistoryEnabled) return; store.RecordLaunch(row.UsageKey, learnedQuery); ShowHistory(); }
            else if (row.Command is "shell" or "shell-query")
            {
                if (!store.Preferences.ShellCommandsEnabled || row.Command == "shell-query" && !store.Preferences.ShellFallbackEnabled) return;
                var immediately = row.Command == "shell-query" || Query.Text.Trim().Equals("pwsh", StringComparison.OrdinalIgnoreCase);
                Dismiss(true); executing = false; ShellRequested?.Invoke(row.ShellText ?? "", immediately);
            }
            else if (row.Command is RecycleBinCommand.OpenId or RecycleBinCommand.EmptyId)
            {
                if (!store.Preferences.SystemCommandsEnabled) return;
                Dismiss(true);
                var bin = new RecycleBinService();
                if (row.Command == RecycleBinCommand.OpenId)
                { bin.Open(); store.RecordLaunch(row.UsageKey, learnedQuery); }
                else
                {
                    var result = await RecycleBinCommand.EmptyAsync(bin, info => Task.FromResult(
                        System.Windows.MessageBox.Show($"Permanently delete all {info.Items:N0} items currently in the Recycle Bin?\n\nThis cannot be undone.",
                            "Empty Recycle Bin", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes));
                    if (result != RecycleBinResult.Cancelled)
                    {
                        store.RecordLaunch(row.UsageKey, learnedQuery);
                        ActionCompleted?.Invoke(result == RecycleBinResult.AlreadyEmpty ? "Recycle Bin is already empty." : "Recycle Bin emptied.");
                    }
                }
            }
            else if (row.Answer != null)
            {
                if (!historyMode && (!store.Preferences.CalculatorEnabled || row.Currency != null && !store.Preferences.CurrencyConversionEnabled)) return;
                System.Windows.Clipboard.SetText(row.Answer);
                if (!historyMode) store.RecordCalculation(Query.Text.Trim(), row.Answer);
                Dismiss(true);
            }
            else if (row.Entry != null)
            {
                if (!row.Entry.IsEnabled(store.Preferences)) return;
                if (row.Entry.AppUserModelId == null && !LocalPathSafety.IsFile(row.Entry.Path)) { await discovery.RevalidateAsync(); throw new InvalidOperationException("This application was deleted, linked or is unavailable."); }
                Dismiss(true);
                ApplicationLauncher.Launch(row.Entry, store.Preferences);
                store.RecordLaunch(row.UsageKey, learnedQuery);
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException
            or System.Runtime.InteropServices.ExternalException)
        { Status.Text = "Action failed: " + ex.Message; ActionFailed?.Invoke(Status.Text); }
        finally { executing = false; }
    }

    // One funnel for launcher results and global bindings; captures target before hiding.
    public Task RunWindowActionAsync(WindowAction action, bool global = false, ForegroundTarget? capturedWindow = null) => RunWindowCommandAsync(action, null, global, capturedWindow);
    private async Task RunWindowCommandAsync(WindowAction? action, Guid? sizeId, bool global, ForegroundTarget? capturedWindow = null)
    {
        if (executing || !store.Preferences.WindowManagementEnabled) return;
        var size = sizeId is { } id ? CustomWindowSize.Runnable(store.Preferences, id) : null;
        if (sizeId != null && size == null) { SetStatus("This custom window size is disabled or unavailable."); ActionFailed?.Invoke(Status.Text); return; }
        executing = true;
        var windowTarget = IsVisible ? target : capturedWindow ?? ForegroundTarget.Capture();
        try
        {
            var throughPalette = IsVisible;
            var learnedQuery = global || categoryQuery ? null : Query.Text.Trim();
            if (throughPalette) Dismiss(true);
            var error = size != null ? await windowManager.ExecuteAsync(size, windowTarget, store.Preferences.WindowGap)
                : await windowManager.ExecuteAsync(action!.Value, windowTarget, store.Preferences.WindowGap, WindowCycle.Resolve(store.Preferences));
            if (error != null) { SetStatus(error); ActionFailed?.Invoke(error); }
            else if (throughPalette || global) store.RecordLaunch(size?.EntryId ?? LauncherSections.WindowId(action!.Value), learnedQuery);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        { SetStatus(ex.Message); ActionFailed?.Invoke(ex.Message); }
        finally { executing = false; }
    }

    private void InputLanguageChanging(object sender, InputLanguageEventArgs e)
    {
        if (inputSession.IsActive && e is System.Windows.Input.InputLanguageChangingEventArgs changing && e.NewLanguage.TwoLetterISOLanguageName != "en")
            changing.Rejected = true;
    }
    private nint InputMessages(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (!inputSession.IsActive) return 0;
        if (message == 0x0050) { handled = true; return 0; } // reject palette language-switch requests
        if (message == 0x0051) Dispatcher.BeginInvoke(() => { if (inputSession.IsActive) inputSession.Enforce(); });
        return 0;
    }
    private void HandleDeactivate(object sender, EventArgs e)
    {
        if (IsVisible && !opening && !dismissing) Dismiss(true, "deactivated");
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!System.Windows.Application.Current.Dispatcher.HasShutdownStarted)
        { e.Cancel = true; Dismiss(true); }
        base.OnClosing(e);
    }
}
