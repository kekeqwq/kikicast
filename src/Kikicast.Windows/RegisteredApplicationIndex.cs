using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Kikicast.Core;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Kikicast.Windows;

// Public Windows App Paths, unnamed EXE target only. No PATH overrides, arguments,
// registry writes, remote registry, file traversal or application execution.
[SupportedOSPlatform("windows")]
public static class RegisteredApplicationIndex
{
    private const string AppPaths = @"Software\Microsoft\Windows\CurrentVersion\App Paths";
    public const int MaximumInspectedKeys = 1024;
    public const int MaximumEntries = 512;
    public const int MaximumAliasesPerEntry = 16;
    private const int MaximumPathCharacters = 4096;

    public static IReadOnlyList<LauncherEntry> Scan(IReadOnlyList<LauncherEntry> knownApplications)
    {
        var roots = new List<RegistryKey>();
        try
        {
            // User registrations precede machine registrations; both documented registry views.
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
                foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    try
                    {
                        using var basis = RegistryKey.OpenBaseKey(hive, view);
                        if (basis.OpenSubKey(AppPaths, writable: false) is { } root) roots.Add(root);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
                }
            return ScanRoots(roots, knownApplications);
        }
        finally { foreach (var root in roots) root.Dispose(); }
    }

    public sealed record ScanReport(IReadOnlyList<LauncherEntry> Entries, int InspectedKeys);
    // Root handles remain owned by the caller. Injectable local roots let tests avoid real App Paths.
    public static IReadOnlyList<LauncherEntry> ScanRoots(IEnumerable<RegistryKey> roots, IReadOnlyList<LauncherEntry> knownApplications) => InspectRoots(roots, knownApplications).Entries;
    public static ScanReport InspectRoots(IEnumerable<RegistryKey> roots, IReadOnlyList<LauncherEntry> knownApplications)
    {
        var covered = knownApplications.Select(KnownTarget).Where(x => x != null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<LauncherEntry>(); var inspected = 0;
        var aliases = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var buffer = new byte[(MaximumPathCharacters + 1) * 2];
        foreach (var root in roots.Take(4))
        {
            try
            {
                var handle = root.Handle;
                for (uint index = 0; inspected < MaximumInspectedKeys && entries.Count < MaximumEntries; index++, inspected++)
                {
                    var name = new StringBuilder(256); uint length = 256;
                    var error = RegEnumKeyExW(handle, index, name, ref length, 0, 0, 0, 0);
                    if (error == 259) break; // ERROR_NO_MORE_ITEMS
                    if (error != 0) { if (error == 234) continue; break; }
                    var registration = name.ToString();
                    if (!registration.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !seenNames.Add(registration)) continue;
                    try
                    {
                        using var key = root.OpenSubKey(registration, writable: false);
                        if (key == null || ReadTarget(key, buffer) is not { } target || !LocalPathSafety.IsFile(target)) continue;
                        target = ExecutablePath.Canonical(target);
                        var alias = Path.GetFileNameWithoutExtension(registration);
                        if (aliases.TryGetValue(target, out var names))
                        { if (names.Count < MaximumAliasesPerEntry) names.Add(alias); continue; }
                        if (!covered.Add(target)) continue;
                        aliases.Add(target, [alias]);
                        entries.Add(new LauncherEntry(Path.GetFileNameWithoutExtension(target), target, target));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException) { }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or ObjectDisposedException or System.Security.SecurityException) { }
            if (inspected >= MaximumInspectedKeys || entries.Count >= MaximumEntries) break;
        }
        var result = entries.Select(x => x with { RegistrationNames = aliases[x.Path].ToArray() }).ToArray();
        foreach (var entry in result) _ = entry.SearchFields;
        return new(result, inspected);
    }
    private static string? KnownTarget(LauncherEntry entry)
    {
        var path = entry.ExecutablePath ?? (entry.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? entry.Path : null);
        try { return path != null && Path.IsPathFullyQualified(path) ? Path.GetFullPath(path) : null; }
        catch (Exception ex) when (ex is ArgumentException or IOException or System.Security.SecurityException) { return null; }
    }
    private static string? ReadTarget(RegistryKey key, byte[] buffer)
    {
        // Do not ask GetValue to allocate an unbounded registry string or expand it implicitly.
        var size = (uint)buffer.Length;
        var error = RegQueryValueExW(key.Handle, null, 0, out var kind, buffer, ref size);
        if (error != 0 || kind is not (1 or 2) || size < 2 || size > buffer.Length || (size & 1) != 0) return null;
        var text = new UnicodeEncoding(false, false, true).GetString(buffer, 0, (int)size);
        if (!text.EndsWith('\0')) return null;
        text = text[..^1];
        if (text.Any(char.IsControl)) return null;
        text = text.Trim();
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"') text = text[1..^1];
        if (kind == 2) { if (ExpandBounded(text) is not { } expanded) return null; text = expanded; }
        if (text.Length is < 1 or > MaximumPathCharacters || text.Any(char.IsControl) || !text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || !Path.IsPathFullyQualified(text)) return null;
        return text;
    }
    private static string? ExpandBounded(string text)
    {
        var result = new StringBuilder(MaximumPathCharacters); var offset = 0;
        while (offset < text.Length)
        {
            var start = text.IndexOf('%', offset); var end = start < 0 ? -1 : text.IndexOf('%', start + 1);
            if (start < 0 || end < 0)
            { if (result.Length + text.Length - offset > MaximumPathCharacters) return null; result.Append(text.AsSpan(offset)); break; }
            if (result.Length + start - offset > MaximumPathCharacters) return null;
            result.Append(text.AsSpan(offset, start - offset));
            var name = text[(start + 1)..end];
            var value = name.Length == 0 ? null : Environment.GetEnvironmentVariable(name);
            value ??= text[start..(end + 1)];
            if (result.Length + value.Length > MaximumPathCharacters) return null;
            result.Append(value); offset = end + 1;
        }
        return result.ToString();
    }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int RegEnumKeyExW(SafeRegistryHandle key, uint index, StringBuilder name, ref uint length, nint reserved, nint className, nint classLength, nint lastWriteTime);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int RegQueryValueExW(SafeRegistryHandle key, string? valueName, nint reserved, out uint type, [Out] byte[] data, ref uint size);
}
