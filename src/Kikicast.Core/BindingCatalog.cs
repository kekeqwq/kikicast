namespace Kikicast.Core;

public sealed record ActionBinding(int Id, string Name, HotKeyBinding Binding, WindowAction? WindowAction = null, string? EntryId = null);

public static class BindingCatalog
{
    public static IReadOnlyList<ActionBinding> Build(AppPreferences preferences, bool activeOnly = true)
    {
        var result = new List<ActionBinding>();
        if (preferences.PaletteBinding is { } palette) result.Add(new(1, "Show palette", palette));
        if (preferences.AltSpaceFallback) result.Add(new(2, "Legacy palette shortcut", new(BindingKind.Combo, 32, 1)));
        if (!activeOnly || preferences.WindowManagementEnabled)
            foreach (var pair in preferences.WindowBindings.OrderBy(x => x.Key))
                result.Add(new(1000 + (int)pair.Key, WindowGeometry.Commands.First(x => x.Action == pair.Key).Name, pair.Value, pair.Key));
        var index = 0;
        foreach (var pair in preferences.EntryBindings.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!activeOnly || LauncherBindings.FeatureEnabled(pair.Key, preferences))
                result.Add(new(6000 + index, pair.Key, pair.Value, EntryId: pair.Key));
            index++;
        }
        return result;
    }

    public static bool Overlaps(HotKeyBinding left, HotKeyBinding right)
    {
        if (left.Kind != right.Kind || left.Key != right.Key) return false;
        return left.Kind == BindingKind.Combo ? left.Modifiers == right.Modifiers
            : left.Side == KeySide.Any || right.Side == KeySide.Any || left.Side == right.Side;
    }

    public static string? FindConflict(AppPreferences preferences)
    {
        var all = Build(preferences, false);
        for (var i = 0; i < all.Count; i++)
        for (var j = i + 1; j < all.Count; j++)
            if (Overlaps(all[i].Binding, all[j].Binding))
                return $"Shortcut conflict: {all[i].Name} and {all[j].Name} ({all[i].Binding.Display}).";
        return null;
    }
}
