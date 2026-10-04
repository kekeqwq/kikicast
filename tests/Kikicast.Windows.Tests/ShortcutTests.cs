using System.Runtime.InteropServices;
using Kikicast.Windows;
using Kikicast.Core;

namespace Kikicast.Windows.Tests;

public class ShortcutTests
{
    [Fact]
    public void PackageShortcutMetadataDeduplicatesStoreRowAndKeepsItsOriginalLaunchPathAndArguments()
    {
        if (!OperatingSystem.IsWindows()) return;
        var folder = Path.Combine(Path.GetTempPath(), "KikicastPackageLink-" + Guid.NewGuid()); Directory.CreateDirectory(folder);
        object? shell = null, shortcut = null, instance = null; var value = new Variant();
        const string id = "Kikicast.Fixture_abcde12345678!App";
        try
        {
            var executable = Path.Combine(folder, "GenericHost.exe"); File.WriteAllBytes(executable, []);
            var link = Path.Combine(folder, "Owned packaged link.lnk"); shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            shortcut = ((dynamic)shell!).CreateShortcut(link); ((dynamic)shortcut).TargetPath = executable; ((dynamic)shortcut).Arguments = "--keep-original"; ((dynamic)shortcut).Save();
            instance = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"))!);
            var file = (System.Runtime.InteropServices.ComTypes.IPersistFile)instance!; file.Load(link, 2); // read/write only for the owned fixture's metadata setup
            var key = new Key { Format = new("9f4c2855-9f79-4b39-a8d0-e1d42de1d5f3"), Id = 5 };
            value.Type = 31; value.Text = Marshal.StringToCoTaskMemUni(id); var properties = (Properties)instance!;
            properties.SetValue(ref key, ref value); properties.Commit(); file.Save(link, true);
            var before = File.ReadAllBytes(link); var entry = ApplicationIndex.ReadShortcut(link)!;
            Assert.Equal(id, entry.ShortcutAppUserModelId); Assert.Null(entry.ExecutablePath); Assert.Null(entry.AppUserModelId); Assert.Equal(link, entry.Path); Assert.Equal(LauncherSections.ApplicationId(link), entry.Id);
            Assert.Empty(PackagedApplicationIndex.ScanRecords([new("Fixture", id)], [entry], _ => { throw new InvalidOperationException("Covered package must not look up or activate anything."); }));
            Assert.Equal(before, File.ReadAllBytes(link)); Assert.Equal("--keep-original", (string)((dynamic)shortcut).Arguments);
            Assert.True(entry.IsEnabled(new() { IncludePackagedApplications = false })); // independent shortcut source
        }
        finally
        {
            if (value.Text != 0) Marshal.FreeCoTaskMem(value.Text);
            foreach (var item in new[] { instance, shortcut, shell }) if (item != null) Marshal.FinalReleaseComObject(item);
            Directory.Delete(folder, true);
        }
    }
    [Fact]
    public void LayoutOpeningSharesOriginalLinkLaunchAndRefusesChangedTargetWithoutTouchingBytes()
    {
        if (!OperatingSystem.IsWindows()) return;
        var folder = Path.Combine(Path.GetTempPath(), "KikicastOpeningLink-" + Guid.NewGuid()); Directory.CreateDirectory(folder);
        object? shell = null, shortcut = null;
        try
        {
            var exe = Path.Combine(folder, "Owned.exe"); var other = Path.Combine(folder, "Other.exe");
            File.WriteAllBytes(exe, []); File.WriteAllBytes(other, []);
            var link = Path.Combine(folder, "Owned original link.lnk"); shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            shortcut = ((dynamic)shell!).CreateShortcut(link); ((dynamic)shortcut).TargetPath = exe;
            ((dynamic)shortcut).Arguments = "--literal-original \"a b\""; ((dynamic)shortcut).WorkingDirectory = folder; ((dynamic)shortcut).Save();
            var entry = ApplicationIndex.ReadShortcut(link)!; var before = File.ReadAllBytes(link); var count = 0;
            ApplicationLauncher.Launch(entry, new(), "exe:" + exe.ToUpperInvariant(), info =>
            { count++; Assert.Equal(link, info.FileName); Assert.True(info.UseShellExecute); Assert.Empty(info.Arguments); Assert.Empty(info.ArgumentList); });
            foreach (var input in new[] { new WindowLayoutInput(WindowLayoutInputKind.File) { Value = exe }, new(WindowLayoutInputKind.Uri) { Value = "https://example.invalid/owned" }, new(WindowLayoutInputKind.Arguments) { Arguments = ["--never-append"] } })
                Assert.Throws<InvalidOperationException>(() => { if (OperatingSystem.IsWindows()) ApplicationLauncher.Launch(entry, new(), desktopLaunch: _ => count++, input: input); });
            Assert.Equal(1, count); Assert.Equal(before, File.ReadAllBytes(link)); Assert.Equal("--literal-original \"a b\"", (string)((dynamic)shortcut).Arguments);
            ((dynamic)shortcut).TargetPath = other; ((dynamic)shortcut).Save(); before = File.ReadAllBytes(link);
            Assert.Throws<InvalidOperationException>(() => { if (OperatingSystem.IsWindows()) ApplicationLauncher.Launch(entry, new(), "exe:" + exe.ToUpperInvariant(), _ => count++); });
            var descriptor = Assert.Single(LayoutWindowInventory.Applications([entry], new())); Assert.Equal("exe:" + other.ToUpperInvariant(), descriptor.MatchKey);
            Assert.Equal(1, count); Assert.Equal(before, File.ReadAllBytes(link));
        }
        finally
        {
            if (shortcut != null) Marshal.FinalReleaseComObject(shortcut);
            if (shell != null) Marshal.FinalReleaseComObject(shell);
            Directory.Delete(folder, true);
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Key { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct Variant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public nint Text; }
    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface Properties
    {
        void GetCount(out uint count); void GetAt(uint index, out Key key); void GetValue(ref Key key, out Variant value);
        void SetValue(ref Key key, ref Variant value); void Commit();
    }
    [Fact]
    public void ReadsActualLinkTargetWithoutLaunchingOrChangingArguments()
    {
        if (!OperatingSystem.IsWindows()) return;
        var folder = Path.Combine(Path.GetTempPath(), "KikicastLink-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        object? shell = null, shortcut = null;
        try
        {
            var executable = Path.Combine(folder, "Demo.exe"); File.WriteAllBytes(executable, []);
            var link = Path.Combine(folder, "Different display name.lnk");
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            shortcut = ((dynamic)shell!).CreateShortcut(link);
            ((dynamic)shortcut).TargetPath = executable;
            ((dynamic)shortcut).Arguments = "--preserve-this-argument";
            var icon = Path.Combine(AppContext.BaseDirectory, "icon-fixture.ico");
            ((dynamic)shortcut).IconLocation = icon + ",0";
            ((dynamic)shortcut).Save();
            var before = File.ReadAllBytes(link);
            Assert.Equal(executable, ApplicationIndex.ReadExecutableTarget(link), StringComparer.OrdinalIgnoreCase);
            var entry = ApplicationIndex.ReadShortcut(link);
            Assert.NotNull(entry); Assert.Equal(icon, entry.IconPath, StringComparer.OrdinalIgnoreCase); Assert.Equal(0, entry.IconIndex);
            Assert.Equal(executable, entry.ExecutablePath, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(before, File.ReadAllBytes(link));
            Assert.Equal("--preserve-this-argument", (string)((dynamic)shortcut).Arguments);
        }
        finally
        {
            if (shortcut != null) Marshal.FinalReleaseComObject(shortcut);
            if (shell != null) Marshal.FinalReleaseComObject(shell);
            Directory.Delete(folder, true);
        }
    }
}
