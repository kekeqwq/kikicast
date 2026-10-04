namespace Kikicast.Windows;

// User-selected local scopes only, never whole-drive recursion or network traversal.
public static class ApplicationFolders
{
    public static string Expand(string path)
    {
        path = path.Trim();
        if (path == "~") path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);
        if (!Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
            throw new ArgumentException("Application folders must be absolute local paths or ~/ paths; network/device paths are not scanned.");
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var root = Path.GetPathRoot(full)!;
        if (full.TrimEnd('\\', '/') == root.TrimEnd('\\', '/') || new DriveInfo(root).DriveType == DriveType.Network)
            throw new ArgumentException("Choose an application folder, not a drive root or network drive.");
        return full;
    }
    public static string Abbreviate(string path)
    {
        var full = Expand(path);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\', '/');
        return full.Equals(home, StringComparison.OrdinalIgnoreCase) ? "~" : full.StartsWith(home + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? "~" + full[home.Length..] : full;
    }
}
