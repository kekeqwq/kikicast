using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Kikicast.Windows;

public sealed class OwnedIconHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal OwnedIconHandle(nint handle) : base(true) => SetHandle(handle);
    protected override bool ReleaseHandle() => DestroyIcon(handle);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
}

// Documented resource extraction only: no Shell icon extension, Resolve, launch or DLL execution.
public static class IconResource
{
    public static OwnedIconHandle? Read(string path, int index = 0, int size = 64)
    {
        if (size is < 16 or > 128) throw new ArgumentOutOfRangeException(nameof(size));
        if (index is < -65535 or > 65535) return null;
        try
        {
            if (!LocalPathSafety.IsFile(path)) return null;
            if (new FileInfo(path).Length > 256L * 1024 * 1024) return null;
            var extension = Path.GetExtension(path);
            if (extension is not null && !new[] { ".exe", ".dll", ".ico" }.Contains(extension, StringComparer.OrdinalIgnoreCase)) return null;
            var result = SHDefExtractIconW(path, index, 0, out var large, out var small, (uint)(size | size << 16));
            if (small != 0 && small != large) DestroyIcon(small);
            if (result != 0 || large == 0) { if (large != 0) DestroyIcon(large); return null; }
            return new(large);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException) { return null; }
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHDefExtractIconW(string path, int index, uint flags, out nint large, out nint small, uint size);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
}
