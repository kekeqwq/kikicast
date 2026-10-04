using Kikicast.Core;

namespace Kikicast.Windows;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class ApplicationIndex
{
    public static string? ReadExecutableTarget(string shortcut) => ShortcutTarget.Read(shortcut);
    public static IReadOnlyList<LauncherEntry> Scan() => Scan([]);
    public static IReadOnlyList<LauncherEntry> Scan(IReadOnlyList<string> applicationFolders, int folderDepth = 1, bool includeWindowsAppPaths = true,
        Func<IReadOnlyList<LauncherEntry>, IReadOnlyList<LauncherEntry>>? registeredSource = null,
        bool includePackagedApplications = true, Func<IReadOnlyList<LauncherEntry>, IReadOnlyList<LauncherEntry>>? packagedSource = null)
    {
        ValidateDepth(folderDepth);
        var entries = new Dictionary<string, LauncherEntry>(StringComparer.OrdinalIgnoreCase);
        var budget = 10000;
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms) })
            Walk(root, entries, 0, ref budget);
        foreach (var entry in ScanFolders(applicationFolders, entries.Values.ToArray(), folderDepth)) entries.TryAdd(entry.Path, entry);
        if (includeWindowsAppPaths)
            foreach (var entry in (registeredSource ?? RegisteredApplicationIndex.Scan)(entries.Values.ToArray())) entries.TryAdd(entry.Path, entry);
        if (includePackagedApplications)
            foreach (var entry in (packagedSource ?? PackagedApplicationIndex.Scan)(entries.Values.ToArray())) entries.TryAdd(entry.Path, entry);
        var result = entries.Values.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        foreach (var entry in result) _ = entry.SearchFields; // Fold/transliterate on the indexing worker, not per keystroke.
        return result;
    }

    private static void ValidateDepth(int depth)
    { if (depth is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(depth), "Choose 0–3 child levels."); }
    // Default one child level, configurable 0–3; max 32 scopes / 20,000 inspected items.
    public static IReadOnlyList<LauncherEntry> ScanFolders(IReadOnlyList<string> folders, IReadOnlyList<LauncherEntry> knownApplications, int folderDepth = 1)
    {
        ValidateDepth(folderDepth);
        var entries = new Dictionary<string, LauncherEntry>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var budget = 20000;
        foreach (var scope in folders.Take(32))
        {
            try
            {
                var path = ApplicationFolders.Expand(scope);
                if (visited.Add(path)) WalkScope(path, entries, 0, folderDepth, ref budget);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException) { }
        }
        var covered = knownApplications.Concat(entries.Values.Where(x => x.Path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)))
            .Where(x => x.ExecutablePath != null).Select(x => x.ExecutablePath!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return entries.Values.Where(x => !x.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !covered.Contains(x.ExecutablePath!)).ToArray();
    }
    private static void WalkScope(string directory, Dictionary<string, LauncherEntry> entries, int depth, int maximumDepth, ref int budget)
    {
        if (budget <= 0 || !LocalPathSafety.TryInspect(directory, out var rootAttributes) || (rootAttributes & FileAttributes.Directory) == 0) return;
        try
        {
            if ((File.GetAttributes(directory) & (FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System)) != 0) return;
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                if (--budget < 0) break;
                var attributes = File.GetAttributes(path);
                if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                if ((attributes & FileAttributes.Directory) != 0)
                { if (depth < maximumDepth) WalkScope(path, entries, depth + 1, maximumDepth, ref budget); continue; }
                if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) AddShortcut(path, entries);
                else if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && ExecutablePath.Canonical(path) is { } executable)
                    entries.TryAdd(executable, new(Path.GetFileNameWithoutExtension(path), executable, executable));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
    }
    private static void AddShortcut(string path, Dictionary<string, LauncherEntry> entries)
    {
        var entry = ReadShortcut(path);
        if (entry != null) entries.TryAdd(path, entry);
    }
    public static LauncherEntry? ReadShortcut(string path)
    {
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || !LocalPathSafety.IsFile(path)) return null;
        var details = ShortcutTarget.ReadDetails(path); var target = details?.Target;
        if (target != null && !LocalPathSafety.IsFile(target)) return null;
        return new LauncherEntry(Path.GetFileNameWithoutExtension(path), path, target, details?.IconPath, details?.IconIndex ?? 0, ShortcutAppUserModelId: details?.AppUserModelId);
    }
    private static void Walk(string directory, Dictionary<string, LauncherEntry> entries, int depth, ref int budget)
    {
        if (depth > 12 || budget <= 0 || !LocalPathSafety.TryInspect(directory, out var rootAttributes) || (rootAttributes & FileAttributes.Directory) == 0) return;
        try
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return;
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                if (--budget < 0) break;
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                if ((attributes & FileAttributes.Directory) != 0) Walk(path, entries, depth + 1, ref budget);
                else if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) AddShortcut(path, entries);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException) { }
    }
}
