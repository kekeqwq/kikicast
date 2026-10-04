namespace Kikicast.Windows;

// Inspect from the drive outward. Never inspect a child before rejecting a linked ancestor.
// This is a privacy boundary, not a defense against concurrent privileged filesystem mutation.
public static class LocalPathSafety
{
    public static bool TryInspect(string path, out FileAttributes attributes)
    {
        attributes = default;
        try
        {
            if (!Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal)) return false;
            var full = Path.GetFullPath(path); var root = Path.GetPathRoot(full)!;
            if (new DriveInfo(root).DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.CDRom or DriveType.Ram)) return false;
            var current = root;
            attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0) return false;
            var components = full[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
            if (components.Length > 128) return false;
            foreach (var component in components)
            {
                current = Path.Combine(current, component); attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0) return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException) { return false; }
    }
    public static bool IsFile(string path) => TryInspect(path, out var attributes) && (attributes & FileAttributes.Directory) == 0;
}
