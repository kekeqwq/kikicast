namespace Kikicast.Core;

public sealed record AppPreferences
{
    public int Version { get; init; } = 1;
    public HotKeyBinding? PaletteBinding { get; init; } = new();
    public bool AltSpaceFallback { get; init; }
    public bool WindowManagementEnabled { get; init; }
    public bool ShowWindowCommands { get; init; } = true;
    public List<CustomWindowSize> CustomWindowSizes { get; init; } = [];
    public List<WindowLayout> WindowLayouts { get; init; } = [];
    public bool ShowWindowLayouts { get; init; } = true;
    public double WindowGap { get; init; }
    public bool CycleHalfSizes { get; init; }
    public WindowCycleMode? HalfCycleMode { get; init; }
    public Dictionary<WindowAction, HotKeyBinding> WindowBindings { get; init; } = [];
    public Dictionary<string, HotKeyBinding> EntryBindings { get; init; } = [];
    public bool ShowSuggestions { get; init; } = true;
    public SearchSensitivity MatchSensitivity { get; init; } = SearchSensitivity.Medium;
    public bool DiscoverRunningApplications { get; init; } = true;
    public List<string> ApplicationFolders { get; init; } = [];
    public int ApplicationFolderDepth { get; init; } = 1;
    public bool IncludeWindowsAppPaths { get; init; } = true;
    public bool IncludePackagedApplications { get; init; } = true;
    public List<string> FavoriteKeys { get; init; } = [];
    public bool CalculatorEnabled { get; init; } = true;
    public bool CurrencyConversionEnabled { get; init; } = true;
    public bool CalculationHistoryEnabled { get; init; } = true;
    public bool ShellCommandsEnabled { get; init; } = true;
    public bool ShellFallbackEnabled { get; init; } = true;
    public bool SystemCommandsEnabled { get; init; } = true;
    public bool SavedCommandsEnabled { get; init; }
    public bool ShowSavedCommands { get; init; } = true;
    public bool ApplicationsEnabled { get; init; } = true;
    public bool ShowApplicationIcons { get; init; } = true;
    public bool ActionsPanelEnabled { get; init; } = true;
    public Dictionary<string, string> LauncherAliases { get; init; } = [];
    public List<string> HiddenEntryKeys { get; init; } = [];
    public string? Validate()
    {
        if (Version != 1) return "Unsupported settings version.";
        if (HalfCycleMode is { } cycleMode && !Enum.IsDefined(cycleMode)) return "Unknown half-cycle mode.";
        if (!Enum.IsDefined(MatchSensitivity)) return "Unknown launcher search sensitivity.";
        if (!double.IsFinite(WindowGap) || WindowGap is < 0 or > 100) return "Window gap must be 0–100 DIP.";
        if (PaletteBinding?.Validate() is { } error) return error;
        if (WindowBindings == null) return "Window bindings are missing.";
        if (CustomWindowSize.ValidateList(CustomWindowSizes) is { } sizeError) return sizeError;
        if (WindowLayout.ValidateList(WindowLayouts) is { } layoutError) return layoutError;
        if (LauncherBindings.Validate(EntryBindings) is { } entryError) return entryError;
        if (ApplicationFolderDepth is < 0 or > 3) return "Application folder depth must be 0–3 child levels.";
        if (ApplicationFolders == null || ApplicationFolders.Count > 32 || ApplicationFolders.Any(x => string.IsNullOrWhiteSpace(x)
            || x.Length > 4096 || x.Any(char.IsControl) || !(x.Trim() == "~" || x.Trim().StartsWith("~/", StringComparison.Ordinal)
                || x.Trim().StartsWith("~" + (char)92, StringComparison.Ordinal) || x.Trim().Length > 3 && char.IsAsciiLetter(x.Trim()[0])
                    && x.Trim()[1] == ':' && (x.Trim()[2] == '/' || x.Trim()[2] == (char)92) && x.Trim().TrimEnd((char)92, '/').Length > 2))
            || ApplicationFolders.Select(x => x.Trim().TrimEnd((char)92, '/').Replace('/', (char)92)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != ApplicationFolders.Count)
            return "Choose at most 32 distinct local application folders; network/device paths are not supported.";
        if (FavoriteKeys == null || FavoriteKeys.Count > 1000 || FavoriteKeys.Any(string.IsNullOrWhiteSpace)
            || FavoriteKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != FavoriteKeys.Count)
            return "Favorites must contain at most 1000 distinct, nonempty entry IDs.";
        if (LauncherAliases == null || LauncherAliases.Count > 3000 || LauncherAliases.Any(x => string.IsNullOrWhiteSpace(x.Key)
            || x.Key.Length > 4096 || x.Value == null || x.Value.Length > 120 || x.Value.Contains('\0'))
            || LauncherAliases.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != LauncherAliases.Count)
            return "Aliases must contain at most 3000 distinct entry IDs and names of at most 120 characters.";
        if (HiddenEntryKeys == null || HiddenEntryKeys.Count > 3000 || HiddenEntryKeys.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 4096)
            || HiddenEntryKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != HiddenEntryKeys.Count)
            return "Hidden entries must contain at most 3000 distinct entry IDs.";
        foreach (var (action, binding) in WindowBindings)
        {
            if (!Enum.IsDefined(action) || binding == null) return "Unknown window action or invalid binding.";
            if (binding.Validate() is { } invalid) return invalid;
        }
        return BindingCatalog.FindConflict(this);
    }
}

public sealed record LaunchVisit(string Path, int Count, DateTimeOffset LastUsed,
    DateTimeOffset? Anchor = null, List<string>? SearchTerms = null);
public sealed record CalculationVisit(string Expression, string Answer, DateTimeOffset At);
public sealed record LocalHistory
{
    public int Version { get; init; } = 1;
    public List<LaunchVisit> Launches { get; init; } = [];
    public List<CalculationVisit> Calculations { get; init; } = [];

    public LocalHistory RecordLaunch(string path, DateTimeOffset now, string? query = null)
    {
        var previous = Launches.FirstOrDefault(x => StringComparer.OrdinalIgnoreCase.Equals(x.Path, path));
        var rows = Launches.Where(x => !StringComparer.OrdinalIgnoreCase.Equals(x.Path, path)).ToList();
        rows.Add(LauncherUsage.Visit(path, previous, query, now));
        return this with { Launches = rows.OrderByDescending(x => x.LastUsed).Take(1000).ToList() };
    }
    public LocalHistory ResetLearning(string? key = null) => this with
    { Launches = key == null ? [] : Launches.Where(x => !StringComparer.OrdinalIgnoreCase.Equals(x.Path, key)).ToList() };
    public LocalHistory RemoveCalculation(CalculationVisit visit)
    {
        var index = Calculations.FindIndex(x => ReferenceEquals(x, visit));
        if (index < 0) index = Calculations.FindIndex(x => x == visit);
        return index < 0 ? this : this with { Calculations = Calculations.Where((_, i) => i != index).ToList() };
    }
    public LocalHistory RecordCalculation(string expression, string answer, DateTimeOffset now) => Calculations.FirstOrDefault() is { } last
        && last.Expression == expression && last.Answer == answer ? this : this with
    { Calculations = new[] { new CalculationVisit(expression, answer, now) }.Concat(Calculations).Take(200).ToList() };

    public double Frequency(string path, DateTimeOffset now)
    {
        var visit = Launches.FirstOrDefault(x => StringComparer.OrdinalIgnoreCase.Equals(x.Path, path));
        return LauncherUsage.Score(visit, now);
    }
}
