namespace Kikicast.Core;

public sealed record LauncherEntry(string Name, string Path, string? ExecutablePath = null, string? IconPath = null, int IconIndex = 0, IReadOnlyList<string>? RegistrationNames = null, string? AppUserModelId = null, string? PackageInstallPath = null, string? ShortcutAppUserModelId = null)
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<LauncherEntry, SearchProfile> profiles = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<LauncherEntry, LauncherSearchProfile> searchFields = new();
    public LauncherSearchProfile SearchFields => searchFields.GetValue(this, static entry => LauncherSearchProfile.Create(entry.Name, null, entry.RegistrationNames?.ToArray() ?? []));
    public string Id => AppUserModelId == null ? LauncherSections.ApplicationId(Path) : PackagedApplicationId.EntryId(AppUserModelId);
    public string UsageKey => AppUserModelId == null ? Path : Id;
    public string? LocalPath => AppUserModelId == null ? Path : null;
    public bool IsEnabled(AppPreferences preferences) => preferences.ApplicationsEnabled && (RegistrationNames == null || preferences.IncludeWindowsAppPaths)
        && (AppUserModelId == null || preferences.IncludePackagedApplications && PackagedApplicationId.IsValid(AppUserModelId));
    public int Score(string query) => profiles.GetValue(this, static entry => SearchProfile.Create(entry.Name)).Score(query);
}
