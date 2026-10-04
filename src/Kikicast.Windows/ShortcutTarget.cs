using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using System.Text;
using Kikicast.Core;

namespace Kikicast.Windows;

[SupportedOSPlatform("windows")]
internal static class ShortcutTarget
{
    internal sealed record Details(string? Target, string? IconPath, int IconIndex, string? AppUserModelId);
    public static string? Read(string path) => ReadDetails(path)?.Target;
    public static Details? ReadDetails(string path)
    {
        object? instance = null;
        try
        {
            instance = new ShellLink();
            ((IPersistFile)instance).Load(path, 0);
            var target = new StringBuilder(32768);
            ((IShellLinkW)instance).GetPath(target, target.Capacity, 0, 4); // raw path; never Resolve/execute the link
            var file = Environment.ExpandEnvironmentVariables(target.ToString());
            var executable = Path.IsPathFullyQualified(file) && file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? ExecutablePath.Canonical(file) : null;
            string? iconPath = null; var iconIndex = 0;
            try
            {
                var icon = new StringBuilder(32768);
                ((IShellLinkW)instance).GetIconLocation(icon, icon.Capacity, out iconIndex);
                var resource = Environment.ExpandEnvironmentVariables(icon.ToString());
                if (Path.IsPathFullyQualified(resource)) iconPath = ExecutablePath.Canonical(resource);
            }
            catch (Exception ex) when (ex is COMException or IOException or ArgumentException) { }
            var appId = ReadAppId(instance);
            return new(appId == null ? executable : null, iconPath, iconIndex, appId);
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException) { return null; }
        finally { if (instance != null && Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance); }
    }
    private static string? ReadAppId(object instance)
    {
        var value = new PropVariant();
        try
        {
            var key = PackagedApplicationIndex.AppIdKey;
            if (instance is not IPropertyStore store || store.GetValue(ref key, out value) < 0) return null;
            var id = value.Type == 31 ? PackagedApplicationIndex.ReadString(value.Text, PackagedApplicationId.MaximumLength)
                : value.Type == 8 && value.Text != 0 && SysStringLen(value.Text) <= PackagedApplicationId.MaximumLength ? Marshal.PtrToStringBSTR(value.Text) : null;
            return PackagedApplicationId.IsValid(id) ? id : null;
        }
        catch (COMException) { return null; }
        finally { PropVariantClear(ref value); }
    }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public nint Text; }
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
    [DllImport("oleaut32.dll")] private static extern uint SysStringLen(nint value);
    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count); void GetAt(uint index, out PackagedApplicationIndex.PropertyKey key);
        [PreserveSig] int GetValue(ref PackagedApplicationIndex.PropertyKey key, out PropVariant value);
    }
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")] private class ShellLink { }
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int size, nint findData, uint flags);
        void GetIDList(out nint list); void SetIDList(nint list);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder value, int size);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string value);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder value, int size);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string value);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder value, int size);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string value);
        void GetHotkey(out short key); void SetHotkey(short key);
        void GetShowCmd(out int command); void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int size, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(nint hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
