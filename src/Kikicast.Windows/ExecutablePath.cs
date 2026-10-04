using System.Runtime.InteropServices;
using System.Text;

namespace Kikicast.Windows;

internal static class ExecutablePath
{
    public static string Canonical(string path)
    {
        var full = Path.GetFullPath(path);
        if (!LocalPathSafety.TryInspect(full, out _)) return full; // lexical fallback: no network/reparse resolution
        var buffer = new StringBuilder(32768);
        return GetLongPathNameW(full, buffer, (uint)buffer.Capacity) is > 0 and < 32768 ? buffer.ToString() : full;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint GetLongPathNameW(string shortPath, StringBuilder longPath, uint size);
}
