namespace Kikicast.Core;

public sealed record LauncherSettingsItem(string Id, string Name, string UsageKey, LauncherKind Kind, string? Path = null, bool Unavailable = false);

public static class LauncherCustomization
{
    public static AppPreferences Alias(AppPreferences preferences, string id, string value)
    {
        var aliases = new Dictionary<string, string>(preferences.LauncherAliases, StringComparer.OrdinalIgnoreCase);
        value = value.Trim();
        if (value.Length == 0) aliases.Remove(id); else aliases[id] = value;
        return preferences with { LauncherAliases = aliases };
    }
    public static AppPreferences Visibility(AppPreferences preferences, string id, bool visible) => preferences with
    {
        HiddenEntryKeys = visible ? preferences.HiddenEntryKeys.Where(x => !StringComparer.OrdinalIgnoreCase.Equals(x, id)).ToList()
            : preferences.HiddenEntryKeys.Contains(id, StringComparer.OrdinalIgnoreCase) ? preferences.HiddenEntryKeys.ToList() : preferences.HiddenEntryKeys.Append(id).ToList()
    };
    public static AppPreferences RemoveReferences(AppPreferences preferences, string id)
    {
        var updated = Alias(Visibility(preferences, id, true), id, "");
        if (LauncherBindings.CanBind(id)) updated = LauncherBindings.Set(updated, id, null);
        return updated with { FavoriteKeys = updated.FavoriteKeys.Where(x => !StringComparer.OrdinalIgnoreCase.Equals(x, id)).ToList() };
    }
    // An intentional exact alias beats every vendor field; only a prefix earns the next tier.
    public static int AliasTier(string? alias, string query)
    {
        if (string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(query)) return 0;
        var text = SearchProfile.Normalize(alias); query = SearchProfile.Normalize(query);
        return text == query ? 2 : text.StartsWith(query, StringComparison.Ordinal) ? 1 : 0;
    }
}

public enum LauncherActionKind { Activate, ToggleFavorite, MoveFavoriteUp, MoveFavoriteDown, Reveal, CopyLocation, CopyValue, Hide, ResetLearning, DeleteHistory, Settings }
public sealed record LauncherAction(LauncherActionKind Kind, string Title, string Hint = "")
{
    private readonly SearchProfile profile = SearchProfile.Create(Title);
    public bool Matches(string query) => profile.Score(query) >= 0;
}
public sealed record LauncherActionTarget(bool HasId, bool Favorite = false, bool MoveUp = false, bool MoveDown = false,
    string? Path = null, string? Value = null, bool CanHide = false, bool HasLearning = false, bool History = false, string? ApplicationId = null);
public static class LauncherActions
{
    public static IReadOnlyList<LauncherAction> For(LauncherActionTarget target)
    {
        var result = new List<LauncherAction> { new(LauncherActionKind.Activate, target.Value != null ? "Copy result" : "Open / Run", "Enter") };
        if (target.HasId)
        {
            result.Add(new(LauncherActionKind.ToggleFavorite, target.Favorite ? "Remove from Favorites" : "Add to Favorites", "Ctrl+Shift+F"));
            if (target.MoveUp) result.Add(new(LauncherActionKind.MoveFavoriteUp, "Move Favorite Up", "Ctrl+Alt+Up"));
            if (target.MoveDown) result.Add(new(LauncherActionKind.MoveFavoriteDown, "Move Favorite Down", "Ctrl+Alt+Down"));
        }
        if (target.Path != null)
        {
            result.Add(new(LauncherActionKind.Reveal, "Show in Explorer", "Ctrl+Enter"));
            result.Add(new(LauncherActionKind.CopyLocation, "Copy location"));
        }
        if (target.ApplicationId != null) result.Add(new(LauncherActionKind.CopyLocation, "Copy application ID"));
        if (target.Value != null) result.Add(new(LauncherActionKind.CopyValue, "Copy result without closing"));
        if (target.CanHide) result.Add(new(LauncherActionKind.Hide, "Hide from Search", "Ctrl+Shift+H"));
        if (target.HasLearning) result.Add(new(LauncherActionKind.ResetLearning, "Reset learned ranking"));
        if (target.History) result.Add(new(LauncherActionKind.DeleteHistory, "Delete history item"));
        result.Add(new(LauncherActionKind.Settings, "Launcher settings / aliases"));
        return result;
    }
}
