using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Kikicast.Core;

namespace Kikicast.Windows;

[SupportedOSPlatform("windows")]
public static class PackageRegistration
{
    public sealed record Application(string Id, string? InstallPath);
    public const int MaximumPackagesPerFamily = 16, MaximumApplicationsPerPackage = 256;
    private const int NameCharacters = 8192, ApplicationBytes = 65536;
    public static IReadOnlyList<Application> ReadFamily(string family)
    {
        if (!PackagedApplicationId.IsValid(family + "!App")) return [];
        var names = Marshal.AllocHGlobal(NameCharacters * 2);
        try
        {
            var pointers = new nint[MaximumPackagesPerFamily]; uint count = MaximumPackagesPerFamily, characters = NameCharacters;
            // Current user's registered main packages only, not staged/all-user/resource packages.
            if (FindPackagesByPackageFamily(family, 0x10, ref count, pointers, ref characters, names, 0) != 0 || count > MaximumPackagesPerFamily || characters > NameCharacters) return [];
            var result = new Dictionary<string, Application>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < count; i++)
            {
                var fullName = ReadBoundedString(pointers[i], names, NameCharacters * 2, 256);
                if (fullName == null || OpenPackageInfoByFullName(fullName, 0, out var info) != 0) continue;
                try
                {
                    var buffer = Marshal.AllocHGlobal(ApplicationBytes);
                    try
                    {
                        uint bytes = ApplicationBytes;
                        if (GetPackageApplicationIds(info, ref bytes, buffer, out var appCount) != 0 || bytes > ApplicationBytes || appCount > MaximumApplicationsPerPackage || appCount * (uint)IntPtr.Size > bytes) continue;
                        var path = new StringBuilder(4096); uint length = 4096;
                        string? installPath = GetPackagePathByFullName(fullName, ref length, path) == 0 && length <= 4096 ? path.ToString() : null;
                        for (var a = 0; a < appCount; a++)
                        {
                            var id = ReadBoundedString(Marshal.ReadIntPtr(buffer, a * IntPtr.Size), buffer, (int)bytes, PackagedApplicationId.MaximumLength + 1);
                            if (PackagedApplicationId.TrySplit(id, out var parsedFamily, out _) && family.Equals(parsedFamily, StringComparison.OrdinalIgnoreCase))
                                result.TryAdd(id!, new(id!, installPath));
                        }
                    }
                    finally { Marshal.FreeHGlobal(buffer); }
                }
                finally { ClosePackageInfo(info); }
            }
            return result.Values.ToArray();
        }
        finally { Marshal.FreeHGlobal(names); }
    }
    public static bool IsRegistered(string id) => PackagedApplicationId.TrySplit(id, out var family, out _)
        && ReadFamily(family).Any(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private static string? ReadBoundedString(nint value, nint buffer, int bytes, int maximumCharacters)
    {
        var delta = (long)value - (long)buffer;
        if (delta < 0 || delta >= bytes || delta % 2 != 0) return null;
        var limit = Math.Min((bytes - (int)delta) / 2, maximumCharacters);
        for (var i = 0; i < limit; i++) if (Marshal.ReadInt16(value, i * 2) == 0) return Marshal.PtrToStringUni(value, i);
        return null;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int FindPackagesByPackageFamily(string family, uint filters, ref uint count, [Out] nint[] names, ref uint bufferLength, nint buffer, nint properties);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int OpenPackageInfoByFullName(string name, uint reserved, out nint info);
    [DllImport("kernel32.dll")] private static extern int ClosePackageInfo(nint info);
    [DllImport("kernel32.dll")] private static extern int GetPackageApplicationIds(nint info, ref uint bytes, nint buffer, out uint count);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetPackagePathByFullName(string name, ref uint length, StringBuilder path);
}
