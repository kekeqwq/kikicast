namespace Kikicast.Core;

// Stored entry identities, never display names or native registration IDs. Window
// commands continue using their original enum-keyed WindowBindings (no migration).
public static class LauncherBindings
{
    public const int MaximumBindings = 256;
    public static bool CanBind(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 4096 || id.Any(char.IsControl)) return false;
        if (CustomWindowSize.TryId(id, out _) || WindowLayout.TryId(id, out _)) return true;
        if (id.StartsWith("packaged:", StringComparison.OrdinalIgnoreCase)) return PackagedApplicationId.IsValid(id[9..]);
        if (id.StartsWith("app:", StringComparison.OrdinalIgnoreCase))
        {
            var path = id.AsSpan(4);
            return path.Length > 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/'
                && (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase));
        }
        if (id.StartsWith("custom-command:", StringComparison.OrdinalIgnoreCase))
            return Guid.TryParseExact(id[15..], "D", out var key) && key != Guid.Empty;
        return id.Equals("cmd:settings", StringComparison.OrdinalIgnoreCase) || id.Equals("cmd:history", StringComparison.OrdinalIgnoreCase)
            || id.Equals("cmd:" + RecycleBinCommand.OpenId, StringComparison.OrdinalIgnoreCase)
            || id.Equals("cmd:" + RecycleBinCommand.EmptyId, StringComparison.OrdinalIgnoreCase);
    }
    public static bool FeatureEnabled(string id, AppPreferences p)
    {
        if (!CanBind(id)) return false;
        return WindowLayout.TryId(id, out _) ? p.WindowManagementEnabled && p.ApplicationsEnabled
            : CustomWindowSize.TryId(id, out _) ? p.WindowManagementEnabled
            : id.StartsWith("packaged:", StringComparison.OrdinalIgnoreCase) ? p.ApplicationsEnabled && p.IncludePackagedApplications
            : id.StartsWith("app:", StringComparison.OrdinalIgnoreCase) ? p.ApplicationsEnabled
            : id.StartsWith("custom-command:", StringComparison.OrdinalIgnoreCase) ? p.SavedCommandsEnabled
            : id.Equals("cmd:history", StringComparison.OrdinalIgnoreCase) ? p.CalculationHistoryEnabled
            : id.Equals("cmd:settings", StringComparison.OrdinalIgnoreCase) || p.SystemCommandsEnabled;
    }
    public static HotKeyBinding? Get(AppPreferences p, string id)
    {
        if (p.EntryBindings.TryGetValue(id, out var found)) return found;
        foreach (var pair in p.EntryBindings) if (pair.Key.Equals(id, StringComparison.OrdinalIgnoreCase)) return pair.Value;
        return null;
    }
    public static bool IsCurrent(AppPreferences p, ActionBinding action) => action.EntryId is { } id
        ? FeatureEnabled(id, p) && Get(p, id) == action.Binding
        : action.WindowAction is { } window ? p.WindowManagementEnabled && p.WindowBindings.GetValueOrDefault(window) == action.Binding
        : action.Id == 1 ? p.PaletteBinding == action.Binding : action.Id == 2 && p.AltSpaceFallback;
    public static AppPreferences Set(AppPreferences p, string id, HotKeyBinding? binding)
    {
        if (!CanBind(id)) throw new ArgumentException("This entry is not a bindable global action.");
        var map = new Dictionary<string, HotKeyBinding>(p.EntryBindings, StringComparer.OrdinalIgnoreCase);
        if (binding == null) map.Remove(id); else map[id] = binding;
        return p with { EntryBindings = map };
    }
    public static string? Validate(Dictionary<string, HotKeyBinding>? bindings)
    {
        if (bindings == null || bindings.Count > MaximumBindings) return "Choose at most 256 item shortcuts.";
        if (bindings.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != bindings.Count) return "Duplicate item shortcut identities.";
        foreach (var (id, binding) in bindings)
        {
            if (!CanBind(id) || binding == null) return "Invalid global item shortcut identity or binding; query-driven Shell and window IDs use their existing routes.";
            if (binding.Validate() is { } error) return error;
        }
        return null;
    }
}
