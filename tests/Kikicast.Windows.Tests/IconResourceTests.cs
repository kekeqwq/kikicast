using System.Runtime.InteropServices;
using Kikicast.Windows;

namespace Kikicast.Windows.Tests;

public sealed class IconResourceTests
{
    [Fact]
    public void OwnedResourceExtractsRequestedSizeAndPreservesFile()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(AppContext.BaseDirectory, "icon-fixture.ico");
        var before = File.ReadAllBytes(path);
        using var icon = IconResource.Read(path, size: 64);
        Assert.NotNull(icon); Assert.False(icon.IsInvalid);
        Assert.True(GetIconInfo(icon.DangerousGetHandle(), out var info));
        try
        {
            Assert.NotEqual(0, GetObjectW(info.Color, Marshal.SizeOf<Bitmap>(), out var bitmap));
            Assert.Equal(64, bitmap.Width); Assert.Equal(64, bitmap.Height);
        }
        finally { if (info.Mask != 0) DeleteObject(info.Mask); if (info.Color != 0) DeleteObject(info.Color); }
        Assert.Equal(before, File.ReadAllBytes(path));
        icon.Dispose(); Assert.True(icon.IsClosed);
    }
    [Fact]
    public void RejectsNetworkRelativeMissingAndUnsupportedResources()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Null(IconResource.Read("relative.exe"));
        Assert.Null(IconResource.Read("\\\\server\\share\\app.exe"));
        Assert.Null(IconResource.Read("//server/share/app.exe"));
        Assert.Null(IconResource.Read(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe")));
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        try { File.WriteAllText(path, "not an executable"); Assert.Null(IconResource.Read(path)); Assert.Equal("not an executable", File.ReadAllText(path)); }
        finally { File.Delete(path); }
    }
    [Fact]
    public void CorruptIconFallsBackWithoutChangingFile()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ico");
        try { File.WriteAllBytes(path, [0, 1, 2, 3]); Assert.Null(IconResource.Read(path)); Assert.Equal(new byte[] { 0, 1, 2, 3 }, File.ReadAllBytes(path)); }
        finally { File.Delete(path); }
    }
    [Fact]
    public void LocalJunctionIsNotTraversedForIconExtraction()
    {
        if (!OperatingSystem.IsWindows()) return;
        var folder = Path.Combine(Path.GetTempPath(), "KikicastIconJunction-" + Guid.NewGuid());
        var target = Path.Combine(folder, "target"); var link = Path.Combine(folder, "link");
        Directory.CreateDirectory(Path.Combine(target, "nested")); File.Copy(Path.Combine(AppContext.BaseDirectory, "icon-fixture.ico"), Path.Combine(target, "owned.ico"));
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
            { Arguments = $"/d /c mklink /J \"{link}\" \"{target}\"", UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
            Assert.True(process.WaitForExit(5000)); Assert.Equal(0, process.ExitCode);
            Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0);
            Assert.Null(IconResource.Read(Path.Combine(link, "owned.ico")));
            Assert.False(LocalPathSafety.TryInspect(Path.Combine(link, "nested"), out _));
            Assert.Empty(ApplicationIndex.ScanFolders([Path.Combine(link, "nested")], [], 3));
        }
        finally { if (Directory.Exists(link)) Directory.Delete(link); Directory.Delete(folder, true); }
    }
    [Theory]
    [InlineData(0)] [InlineData(129)]
    public void SizeIsBounded(int size) => Assert.Throws<ArgumentOutOfRangeException>(() => IconResource.Read("unused", size: size));

    [StructLayout(LayoutKind.Sequential)] private struct IconInfo { public int IsIcon, X, Y; public nint Mask, Color; }
    [StructLayout(LayoutKind.Sequential)] private struct Bitmap { public int Type, Width, Height, WidthBytes; public ushort Planes, BitsPixel; public nint Bits; }
    [DllImport("user32.dll")] private static extern bool GetIconInfo(nint icon, out IconInfo info);
    [DllImport("gdi32.dll", ExactSpelling = true)] private static extern int GetObjectW(nint handle, int size, out Bitmap bitmap);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint handle);
}
