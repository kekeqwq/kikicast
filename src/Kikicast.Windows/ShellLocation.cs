using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Kikicast.Windows;

[SupportedOSPlatform("windows")]
public static class ShellLocation
{
    // PIDLs avoid explorer.exe command-line quoting ambiguities in paths containing commas.
    public static void Reveal(string path)
    {
        path = Path.GetFullPath(path);
        if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("The item no longer exists.", path);
        Marshal.ThrowExceptionForHR(SHParseDisplayName(path, 0, out var pidl, 0, out _));
        try { Marshal.ThrowExceptionForHR(SHOpenFolderAndSelectItems(pidl, 0, 0, 0)); }
        finally { if (pidl != 0) Marshal.FreeCoTaskMem(pidl); }
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, nint binding, out nint pidl, uint requested, out uint attributes);
    [DllImport("shell32.dll")] private static extern int SHOpenFolderAndSelectItems(nint pidl, uint count, nint children, uint flags);
}
