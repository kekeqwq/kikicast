using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Kikicast.Windows;

// Owns one public IShellItemArray interface pointer; no Shell-extension icon,
// folder bind/enumeration, Resolve, default association or automatic activation.
[SupportedOSPlatform("windows")]
internal sealed class PackageInputItems : IDisposable
{
    public nint Handle { get; private set; }
    public PackageInputItems(string value)
    {
        nint item = 0, array = 0;
        try
        {
            var itemId = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(value, 0, ref itemId, out item));
            var arrayId = new Guid("b63ea76d-1f85-456f-a19c-48159efa858b");
            Marshal.ThrowExceptionForHR(SHCreateShellItemArrayFromShellItem(item, ref arrayId, out array));
            Handle = array; array = 0;
        }
        finally { if (array != 0) Marshal.Release(array); if (item != 0) Marshal.Release(item); }
    }
    public void Dispose() { var array = Handle; Handle = 0; if (array != 0) Marshal.Release(array); }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHCreateItemFromParsingName(string path, nint context, ref Guid iid, out nint item);
    [DllImport("shell32.dll")] private static extern int SHCreateShellItemArrayFromShellItem(nint item, ref Guid iid, out nint array);
}
