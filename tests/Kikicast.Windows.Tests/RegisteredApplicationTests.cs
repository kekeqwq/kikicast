using System.Runtime.Versioning;
using Kikicast.Core;
using Kikicast.Windows;
using Microsoft.Win32;

namespace Kikicast.Windows.Tests;

public class RegisteredApplicationTests
{
    [SupportedOSPlatform("windows")]
    private sealed class Fixture : IDisposable
    {
        public readonly string Folder = Path.Combine(Path.GetTempPath(), "KikicastAppPaths-" + Guid.NewGuid());
        private readonly string keyPath = @"Software\KikicastTests\" + Guid.NewGuid();
        public RegistryKey Root { get; }
        public Fixture() { Directory.CreateDirectory(Folder); Root = Registry.CurrentUser.CreateSubKey(keyPath); }
        public string Exe(string name)
        { var path = Path.GetFullPath(Path.Combine(Folder, name)); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, []); return path; }
        public void Register(string name, object value, RegistryValueKind kind = RegistryValueKind.String)
        { using var key = Root.CreateSubKey(name); key.SetValue("", value, kind); }
        public void Dispose() { Root.Dispose(); Registry.CurrentUser.DeleteSubKeyTree(keyPath, false); Directory.Delete(Folder, true); }
    }

    [Fact]
    public void ReadsQuotedTargetWithoutLaunchingOrChangingRegistryAndFiles()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture(); var exe = fixture.Exe("Actual.exe");
        var value = "\"" + exe + "\""; fixture.Register("Alias.exe", value);
        using (var key = fixture.Root.OpenSubKey("Alias.exe", true)!) key.SetValue("Path", "NeverCollectThisPathOrCommandLine");
        var rows = RegisteredApplicationIndex.ScanRoots([fixture.Root], []);
        var row = Assert.Single(rows); Assert.Equal("Actual", row.Name); Assert.Equal(exe, row.Path); Assert.Equal(exe, row.ExecutablePath);
        Assert.Equal("Alias", Assert.Single(row.RegistrationNames!)); Assert.Equal("alias", Assert.Single(row.SearchFields.Keywords).Text);
        using var restored = fixture.Root.OpenSubKey("Alias.exe")!;
        Assert.Equal(value, restored.GetValue("")); Assert.Equal("NeverCollectThisPathOrCommandLine", restored.GetValue("Path"));
        Assert.Empty(File.ReadAllBytes(exe));
    }
    [Fact]
    public void DeduplicatesActualTargetsNotFilenamesAndPreservesShortcutEntry()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture(); var first = fixture.Exe("one/Same.exe"); var second = fixture.Exe("two/Same.exe");
        fixture.Register("First.exe", first); fixture.Register("Duplicate.exe", first); fixture.Register("Second.exe", second);
        var link = new LauncherEntry("Argument-bearing shortcut", "owned.lnk", first.Replace('\\', '/'));
        var row = Assert.Single(RegisteredApplicationIndex.ScanRoots([fixture.Root], [link])); Assert.Equal(second, row.Path);
        Assert.Equal("owned.lnk", link.Path);
        var all = RegisteredApplicationIndex.ScanRoots([fixture.Root], []);
        Assert.Equal(2, all.Count);
        Assert.Equal(new[] { "Duplicate", "First" }, all.Single(x => x.Path == first).RegistrationNames!.Order(StringComparer.Ordinal));
    }
    [Fact]
    public void UserAndViewPrecedenceIsDeterministicCaseInsensitive()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var user = new Fixture(); using var machine = new Fixture();
        var userExe = user.Exe("User.exe"); var machineExe = machine.Exe("Machine.exe");
        user.Register("Alias.exe", userExe); machine.Register("ALIAS.EXE", machineExe);
        Assert.Equal(userExe, Assert.Single(RegisteredApplicationIndex.ScanRoots([user.Root, machine.Root], [])).Path);
        user.Register("Alias.exe", "//server/share/app.exe");
        Assert.Empty(RegisteredApplicationIndex.ScanRoots([user.Root, machine.Root], []));
    }
    [Fact]
    public void RejectsRemoteRelativeMissingArgumentsControlsAndNonStringValues()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture(); var exe = fixture.Exe("Valid.exe");
        var bad = new[] { "relative.exe", "//server/share/app.exe", @"\\server\share\app.exe", @"\\?\C:\app.exe", exe + " --argument", exe + "\0", "\n" + exe, Path.Combine(fixture.Folder, "missing.exe"), fixture.Folder };
        for (var i = 0; i < bad.Length; i++) fixture.Register($"Bad{i}.exe", bad[i]);
        fixture.Register("Number.exe", 42, RegistryValueKind.DWord);
        fixture.Register("Binary.exe", new byte[] { 0, 1, 2 }, RegistryValueKind.Binary);
        fixture.Register("List.exe", new[] { exe }, RegistryValueKind.MultiString);
        fixture.Register("NotAnApp.txt", exe); fixture.Register("Good.exe", exe);
        Assert.Equal(exe, Assert.Single(RegisteredApplicationIndex.ScanRoots([fixture.Root], [])).Path);
    }
    [Fact]
    public void ExpandsOnlyExpandStringsAndBoundsRawAndExpandedValues()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture(); var exe = fixture.Exe("Expanded.exe");
        var variable = "KIKICAST_TEST_" + Guid.NewGuid().ToString("N");
        try
        {
            Environment.SetEnvironmentVariable(variable, fixture.Folder);
            fixture.Register("Expanded.exe", $"%{variable}%\\Expanded.exe", RegistryValueKind.ExpandString);
            fixture.Register("Literal.exe", $"%{variable}%\\Expanded.exe");
            fixture.Register("Huge.exe", new string('x', 10000) + ".exe");
            Assert.Equal(exe, Assert.Single(RegisteredApplicationIndex.ScanRoots([fixture.Root], [])).Path);
            Environment.SetEnvironmentVariable(variable, new string('x', 5000));
            Assert.Empty(RegisteredApplicationIndex.ScanRoots([fixture.Root], []));
        }
        finally { Environment.SetEnvironmentVariable(variable, null); }
    }
    [Fact]
    public void KeyEnumerationAndRootCountAreBoundedWithoutGetSubKeyNamesAllocation()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture();
        for (var i = 0; i < RegisteredApplicationIndex.MaximumInspectedKeys + 20; i++) fixture.Register("NotAnExe" + i, "unused");
        var report = RegisteredApplicationIndex.InspectRoots([fixture.Root], []);
        Assert.Equal(RegisteredApplicationIndex.MaximumInspectedKeys, report.InspectedKeys); Assert.Empty(report.Entries);
        using var empty = new Fixture(); var reached = 0;
        IEnumerable<RegistryKey> Roots()
        { if (OperatingSystem.IsWindows()) for (var i = 0; i < 5; i++) { reached++; yield return empty.Root; } }
        Assert.Empty(RegisteredApplicationIndex.ScanRoots(Roots(), [])); Assert.Equal(4, reached);
    }
    [Fact]
    public void ApplicationIndexGateSkipsTheSourceAndScopeIdentityDeduplicatesIt()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture(); var exe = fixture.Exe("KikicastRegisteredGate" + Guid.NewGuid().ToString("N") + ".exe");
        fixture.Register("Gate.exe", exe); var calls = 0;
        IReadOnlyList<LauncherEntry> Source(IReadOnlyList<LauncherEntry> known)
        {
            calls++; return OperatingSystem.IsWindows() ? RegisteredApplicationIndex.ScanRoots([fixture.Root], known) : [];
        }
        Assert.DoesNotContain(ApplicationIndex.Scan([], includeWindowsAppPaths: false, registeredSource: Source), x => x.Path == exe);
        Assert.Equal(0, calls);
        Assert.Single(ApplicationIndex.Scan([], registeredSource: Source), x => x.Path == exe); Assert.Equal(1, calls);
        Assert.Single(ApplicationIndex.Scan([fixture.Folder], folderDepth: 0, registeredSource: Source), x => x.Path == exe); Assert.Equal(2, calls);
    }
    [Fact]
    public void DisposedRootIsSkippedAndCallerOwnedHandlesStayUsable()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var disposed = new Fixture(); disposed.Root.Dispose();
        using var valid = new Fixture(); var exe = valid.Exe("Valid.exe"); valid.Register("Valid.exe", exe);
        Assert.Equal(exe, Assert.Single(RegisteredApplicationIndex.ScanRoots([disposed.Root, valid.Root], [])).Path);
        valid.Register("StillWritable.exe", exe);
        Assert.Single(RegisteredApplicationIndex.ScanRoots([valid.Root], []));
    }
    [Fact]
    public void RegistrationAliasesDoNotCreateDuplicateRowsOrUnboundedProfiles()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture(); var exe = fixture.Exe("Actual.exe");
        for (var i = 0; i < 40; i++) fixture.Register($"Alias{i}.exe", exe);
        var row = Assert.Single(RegisteredApplicationIndex.ScanRoots([fixture.Root], []));
        Assert.Equal(RegisteredApplicationIndex.MaximumAliasesPerEntry, row.RegistrationNames!.Count);
        Assert.Equal(RegisteredApplicationIndex.MaximumAliasesPerEntry, row.SearchFields.Keywords.Count);
    }
    [Fact]
    public void AcceptedEntryCountIsBounded()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture();
        for (var i = 0; i < RegisteredApplicationIndex.MaximumEntries + 10; i++) fixture.Register($"App{i}.exe", fixture.Exe($"App{i}.exe"));
        var report = RegisteredApplicationIndex.InspectRoots([fixture.Root], []);
        Assert.Equal(RegisteredApplicationIndex.MaximumEntries, report.Entries.Count);
        Assert.Equal(RegisteredApplicationIndex.MaximumEntries, report.InspectedKeys);
    }
}
