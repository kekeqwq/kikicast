using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Kikicast.Core;

namespace Kikicast.Windows;

[SupportedOSPlatform("windows")]
public static class PackagedApplicationIndex
{
    public const int MaximumInspectedItems = 4096, MaximumEntries = 512, MaximumFamilies = 512;
    public sealed record Record(string Name, string AppUserModelId);
    public static IReadOnlyList<LauncherEntry> Scan(IReadOnlyList<LauncherEntry> known)
    {
        try { return ScanRecords(ShellRecords(), known, PackageRegistration.ReadFamily); }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or System.Security.SecurityException) { return []; }
    }
    // Controlled records/resolver seam: no package install, registry writes or application execution in tests.
    public static IReadOnlyList<LauncherEntry> ScanRecords(IEnumerable<Record> source, IReadOnlyList<LauncherEntry> known,
        Func<string, IReadOnlyList<PackageRegistration.Application>> registrations)
    {
        var covered = known.Select(x => x.AppUserModelId ?? x.ShortcutAppUserModelId).Where(x => x != null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var families = new Dictionary<string, IReadOnlyList<PackageRegistration.Application>>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var result = new List<LauncherEntry>();
        foreach (var row in source.Take(MaximumInspectedItems))
        {
            if (string.IsNullOrWhiteSpace(row.Name) || row.Name.Length > 512 || row.Name.Any(char.IsControl)
                || !PackagedApplicationId.TrySplit(row.AppUserModelId, out var family, out _) || !seen.Add(row.AppUserModelId) || covered.Contains(row.AppUserModelId)) continue;
            if (!families.TryGetValue(family, out var apps))
            {
                if (families.Count == MaximumFamilies) continue;
                families[family] = apps = registrations(family);
            }
            var app = apps.Take(PackageRegistration.MaximumPackagesPerFamily * PackageRegistration.MaximumApplicationsPerPackage)
                .FirstOrDefault(x => row.AppUserModelId.Equals(x.Id, StringComparison.OrdinalIgnoreCase));
            if (app == null) continue; // AppsFolder also contains classic Win32 AUMIDs; never activate them as packages.
            var entry = new LauncherEntry(row.Name.Trim(), PackagedApplicationId.ShellPath(app.Id), AppUserModelId: app.Id, PackageInstallPath: app.InstallPath);
            _ = entry.SearchFields; result.Add(entry);
            if (result.Count == MaximumEntries) break;
        }
        return result;
    }
    private static IEnumerable<Record> ShellRecords()
    {
        IShellItem? root = null; object? enumeration = null;
        try
        {
            var folder = new Guid("1e87508d-89c2-42f0-8a7e-645a0f50ca58"); var itemId = typeof(IShellItem).GUID;
            if (SHGetKnownFolderItem(ref folder, 0, 0, ref itemId, out root) < 0 || root == null) yield break;
            var handler = new Guid("94f60519-2850-4924-aa5a-d15e84868039"); var enumId = typeof(IEnumShellItems).GUID;
            if (root.BindToHandler(0, ref handler, ref enumId, out enumeration) < 0 || enumeration is not IEnumShellItems items) yield break;
            for (var i = 0; i < MaximumInspectedItems; i++)
            {
                IShellItem? item = null;
                try
                {
                    if (items.Next(1, out item, out var fetched) != 0 || fetched != 1 || item == null) yield break;
                    Record? row = Read(item);
                    if (row != null) yield return row;
                }
                finally { Release(item); }
            }
        }
        finally { Release(enumeration); Release(root); }
    }
    private static Record? Read(IShellItem item)
    {
        nint name = 0, id = 0;
        try
        {
            var key = AppIdKey;
            if (item is not IShellItem2 properties || properties.GetString(ref key, out id) < 0 || item.GetDisplayName(0, out name) < 0) return null;
            var aumid = ReadString(id, PackagedApplicationId.MaximumLength);
            var title = ReadString(name, 512);
            return aumid != null && title != null ? new(title, aumid) : null;
        }
        catch (COMException) { return null; }
        finally { if (id != 0) Marshal.FreeCoTaskMem(id); if (name != 0) Marshal.FreeCoTaskMem(name); }
    }
    internal static string? ReadString(nint text, int maximum)
    {
        if (text == 0) return null;
        for (var i = 0; i <= maximum; i++) if (Marshal.ReadInt16(text, i * 2) == 0) return Marshal.PtrToStringUni(text, i);
        return null;
    }
    internal static void Release(object? value) { if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
    [StructLayout(LayoutKind.Sequential)] internal struct PropertyKey { public Guid Format; public uint Id; }
    internal static PropertyKey AppIdKey => new() { Format = new("9f4c2855-9f79-4b39-a8d0-e1d42de1d5f3"), Id = 5 };
    [DllImport("shell32.dll")] private static extern int SHGetKnownFolderItem(ref Guid folder, uint flags, nint token, ref Guid id, [MarshalAs(UnmanagedType.Interface)] out IShellItem? item);
    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig] int BindToHandler(nint context, ref Guid handler, ref Guid id, [MarshalAs(UnmanagedType.Interface)] out object? value);
        void GetParent(out IShellItem parent);
        [PreserveSig] int GetDisplayName(uint kind, out nint name);
        void GetAttributes(uint mask, out uint attributes); void Compare(IShellItem item, uint hint, out int order);
    }
    [ComImport, Guid("7e9fb0d3-919f-4307-ab2e-9b1860310c93"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem2
    {
        void BindToHandler(nint context, ref Guid handler, ref Guid id, out nint value);
        void GetParent(out nint parent); void GetDisplayName(uint kind, out nint name); void GetAttributes(uint mask, out uint attributes); void Compare(nint item, uint hint, out int order);
        void GetPropertyStore(uint flags, ref Guid id, out nint store); void GetPropertyStoreWithCreateObject(uint flags, nint create, ref Guid id, out nint store);
        void GetPropertyStoreForKeys(nint keys, uint count, uint flags, ref Guid id, out nint store); void GetPropertyDescriptionList(ref PropertyKey key, ref Guid id, out nint list);
        void Update(nint context); void GetProperty(ref PropertyKey key, nint value); void GetCLSID(ref PropertyKey key, out Guid value); void GetFileTime(ref PropertyKey key, out long value); void GetInt32(ref PropertyKey key, out int value);
        [PreserveSig] int GetString(ref PropertyKey key, out nint value);
    }
    [ComImport, Guid("70629033-e363-4a28-a567-0db78006e6d7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumShellItems
    {
        [PreserveSig] int Next(uint count, [MarshalAs(UnmanagedType.Interface)] out IShellItem? item, out uint fetched);
        void Skip(uint count); void Reset(); void Clone(out IEnumShellItems clone);
    }
}
