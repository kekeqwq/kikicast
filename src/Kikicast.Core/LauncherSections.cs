namespace Kikicast.Core;

public enum LauncherKind { Application, WindowCommand, Command, CustomCommand, WindowLayout }
public sealed record LauncherItem(string Id, string Name, LauncherKind Kind, bool HasHotKey = false,
    bool CanSuggest = true, int? SuggestionPriority = null, DateTimeOffset? InstalledAt = null, bool HasAlias = false);
public sealed record LauncherPlacement(LauncherItem Item, string Section, int? FavoriteIndex = null)
{
    public string? FavoriteChord => FavoriteIndex is >= 0 and < 10 ? "Ctrl+" + ((FavoriteIndex.Value + 1) % 10) : null;
}

public static class LauncherSections
{
    public static string ApplicationId(string path) => "app:" + path.ToUpperInvariant();
    public static string WindowId(WindowAction action) => "window:" + action;
    public static string Section(LauncherKind kind) => kind switch
    { LauncherKind.Application => "Applications", LauncherKind.WindowLayout => "Window layouts", LauncherKind.WindowCommand => "Window management", LauncherKind.CustomCommand => "Custom Commands", _ => "Commands" };

    // Headers are metadata, not actionable rows. All consumers share this flat order.
    public static IReadOnlyList<LauncherPlacement> Empty(IEnumerable<LauncherItem> source, IReadOnlyList<string> favorites,
        Func<string, double> score, DateTimeOffset now, bool suggestions = true)
    {
        var all = source.DistinctBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToList();
        var byId = all.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var pinned = favorites.Distinct(StringComparer.OrdinalIgnoreCase).Where(byId.ContainsKey).Select(x => byId[x]).ToList();
        var taken = pinned.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var remaining = all.Where(x => !taken.Contains(x.Id)).ToList();
        // Read each usage once per pass; stable tie-breakers keep the keyboard's target deterministic.
        var usage = all.ToDictionary(x => x.Id, x => score(x.Id), StringComparer.OrdinalIgnoreCase);
        var ordered = remaining.OrderByDescending(x => usage[x.Id]).ThenByDescending(x => x.HasAlias)
            .ThenByDescending(x => x.Kind == LauncherKind.Application ? 4 : 3)
            .ThenBy(x => x.Name, Comparer<string>.Create(LauncherOrder.NaturalCompare)).ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToList();
        var picked = new List<LauncherItem>();
        if (suggestions)
        {
            var eligible = ordered.Where(x => x.CanSuggest).ToList();
            picked.AddRange(eligible.Where(x => x.Kind == LauncherKind.Application && usage[x.Id] <= 1
                && x.InstalledAt is { } at && now >= at && now - at < TimeSpan.FromMinutes(5)).Take(2));
            var fresh = picked.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            picked.AddRange(eligible.Where(x => !fresh.Contains(x.Id) && usage[x.Id] > 1 && !x.HasHotKey));
            var used = picked.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            picked.AddRange(eligible.Where(x => !used.Contains(x.Id) && !x.HasHotKey && x.SuggestionPriority != null)
                .OrderByDescending(x => x.SuggestionPriority).ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase));
            picked = picked.Take(5).ToList();
        }
        taken.UnionWith(picked.Select(x => x.Id));
        var result = pinned.Select((x, i) => new LauncherPlacement(x, "Favorites", i)).ToList();
        result.AddRange(picked.Select(x => new LauncherPlacement(x, "Suggestions")));
        foreach (var kind in new[] { LauncherKind.Application, LauncherKind.WindowLayout, LauncherKind.WindowCommand, LauncherKind.CustomCommand, LauncherKind.Command })
            result.AddRange(ordered.Where(x => x.Kind == kind && !taken.Contains(x.Id)).Select(x => new LauncherPlacement(x, Section(kind))));
        return result;
    }

    public static List<string> ToggleFavorite(IReadOnlyList<string> keys, string id) => keys.Contains(id, StringComparer.OrdinalIgnoreCase)
        ? keys.Where(x => !StringComparer.OrdinalIgnoreCase.Equals(x, id)).ToList() : keys.Append(id).ToList();

    // Swap visible keys in their stored positions; missing/disabled favorites retain their slots.
    public static List<string> MoveFavorite(IReadOnlyList<string> keys, IReadOnlyList<string> visible, string id, int direction)
    {
        var result = keys.ToList();
        var index = visible.ToList().FindIndex(x => StringComparer.OrdinalIgnoreCase.Equals(x, id));
        var neighbor = index + Math.Sign(direction);
        if (index < 0 || neighbor < 0 || neighbor >= visible.Count) return result;
        var a = result.FindIndex(x => StringComparer.OrdinalIgnoreCase.Equals(x, id));
        var b = result.FindIndex(x => StringComparer.OrdinalIgnoreCase.Equals(x, visible[neighbor]));
        if (a >= 0 && b >= 0) (result[a], result[b]) = (result[b], result[a]);
        return result;
    }
}
