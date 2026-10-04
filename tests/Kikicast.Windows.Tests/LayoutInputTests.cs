using Kikicast.Core;
using Kikicast.Windows;

namespace Kikicast.Windows.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class LayoutInputTests
{
    [Fact]
    public void ExeFileUriAndLiteralArgumentsArePassedAsDataWithNoDefaultHandlerOrShellWrapper()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "KikicastLayoutInput-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            var exe = Path.Combine(root, "Owned.exe"); File.WriteAllBytes(exe, []); var file = Path.Combine(root, "owned file.txt"); File.WriteAllBytes(file, []);
            var inputs = new[] { new WindowLayoutInput(WindowLayoutInputKind.File) { Value = file }, new(WindowLayoutInputKind.Uri) { Value = "https://example.invalid/a%20b" },
                new(WindowLayoutInputKind.Arguments) { Arguments = ["a b", "a\"b\\", "", "; $(not-run) &"] } };
            foreach (var input in inputs)
                ApplicationLauncher.Launch(new("Owned", exe), new(), input: input, desktopLaunch: info =>
                {
                    Assert.Equal(exe, info.FileName); Assert.True(info.UseShellExecute); Assert.Empty(info.Arguments); Assert.Empty(info.Verb);
                    Assert.Equal(input.Kind == WindowLayoutInputKind.Arguments ? input.Arguments : new[] { input.Value! }, info.ArgumentList);
                });
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void UnsafeMissingAndLinkedLocalInputNeverInvokesLaunchAndSourceOffRetainsInputs()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "KikicastLayoutInput-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            var exe = Path.Combine(root, "Owned.exe"); File.WriteAllBytes(exe, []); var count = 0; var entry = new LauncherEntry("Owned", exe);
            foreach (var path in new[] { Path.Combine(root, "missing.txt"), @"\\server\owned.txt", @"C:\Owned\..\file.txt", @"\\?\C:\Owned.txt" })
                Assert.NotNull(Record.Exception(() => ApplicationLauncher.Launch(entry, new(), input: new(WindowLayoutInputKind.File) { Value = path }, desktopLaunch: _ => count++)));
            var uri = new WindowLayoutInput(WindowLayoutInputKind.Uri) { Value = "https://example.invalid/" };
            Assert.Throws<InvalidOperationException>(() => ApplicationLauncher.Launch(entry, new() { ApplicationsEnabled = false }, input: uri, desktopLaunch: _ => count++));
            Assert.Throws<InvalidOperationException>(() => ApplicationLauncher.Launch(entry with { RegistrationNames = ["owned.exe"] }, new() { IncludeWindowsAppPaths = false }, input: uri, desktopLaunch: _ => count++));
            Assert.Equal(0, count); Assert.Equal("https://example.invalid/", uri.Value);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void PackageFileAndProtocolUseOnlyExplicitVerifiedAumidContractAndPropagateRefusal()
    {
        if (!OperatingSystem.IsWindows()) return;
        const string id = "Kikicast.Owned_abcdefghijklm!App"; var entry = new LauncherEntry("Owned package", "shell:AppsFolder\\" + id, AppUserModelId: id); var count = 0;
        var root = Path.Combine(Path.GetTempPath(), "KikicastPackageInput-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "owned.txt"); File.WriteAllBytes(file, []);
            foreach (var input in new[] { new WindowLayoutInput(WindowLayoutInputKind.File) { Value = file }, new(WindowLayoutInputKind.Uri) { Value = "https://example.invalid/" } })
            {
                ApplicationLauncher.Launch(entry, new(), registered: _ => true, input: input, packageInputLaunch: (actual, prepared) =>
                { Assert.Equal(id, actual); Assert.Equal(input.Kind, prepared.Kind); Assert.Equal(input.Value, prepared.Value); count++; });
                Assert.Throws<InvalidOperationException>(() => ApplicationLauncher.Launch(entry, new() { IncludePackagedApplications = false }, registered: _ => true, input: input, packageInputLaunch: (_, _) => count++));
                Assert.Throws<InvalidOperationException>(() => ApplicationLauncher.Launch(entry, new(), registered: _ => false, input: input, packageInputLaunch: (_, _) => count++));
                Assert.Throws<InvalidOperationException>(() => ApplicationLauncher.Launch(entry, new(), registered: _ => true, input: input, packageInputLaunch: (_, _) => throw new InvalidOperationException("controlled unsupported contract")));
            }
            Assert.Equal(2, count); // no installed package, URI handler or network access
            Assert.Throws<InvalidOperationException>(() => ApplicationLauncher.Launch(entry, new(), registered: _ => true, input: new(WindowLayoutInputKind.File) { Value = root }, packageInputLaunch: (_, _) => count++));
            Assert.Throws<InvalidOperationException>(() => ApplicationLauncher.Launch(entry, new(), registered: _ => true, input: new(WindowLayoutInputKind.Arguments) { Arguments = ["one"] }, packageInputLaunch: (_, _) => count++));
            Assert.Equal(2, count);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void LinkedInputAncestorsAreRejectedAndNativeOwnedFileShellArrayIsExactlyOneItem()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "KikicastInputLinks-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        var target = Path.Combine(root, "target"); Directory.CreateDirectory(target); var link = Path.Combine(root, "link");
        try
        {
            var file = Path.Combine(target, "owned.txt"); File.WriteAllBytes(file, []);
            using var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
            { Arguments = $"/d /c mklink /J \"{link}\" \"{target}\"", UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
            Assert.True(mklink.WaitForExit(5000)); Assert.Equal(0, mklink.ExitCode);
            Assert.Throws<InvalidOperationException>(() => LayoutInputPolicy.Prepare(new("Owned", Path.Combine(root, "Owned.exe")), new(WindowLayoutInputKind.File) { Value = Path.Combine(link, "owned.txt") }));
            var type = typeof(PackagedApplication).Assembly.GetType("Kikicast.Windows.PackageInputItems")!;
            using var items = (IDisposable)Activator.CreateInstance(type, file)!;
            var handle = (nint)type.GetProperty("Handle")!.GetValue(items)!;
            var table = System.Runtime.InteropServices.Marshal.ReadIntPtr(handle);
            var count = System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer<GetCount>(System.Runtime.InteropServices.Marshal.ReadIntPtr(table, 7 * nint.Size));
            Assert.Equal(0, count(handle, out var actual)); Assert.Equal(1u, actual);
            items.Dispose(); Assert.Equal(nint.Zero, (nint)type.GetProperty("Handle")!.GetValue(items)!); // owned local item only; no app activation/URI parse
        }
        finally { if (Directory.Exists(link)) Directory.Delete(link); Directory.Delete(root, true); }
    }
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
    private delegate int GetCount(nint self, out uint count);
    [Fact]
    public void AuthoredInputAtomicSaveReloadAndLegacyOrDamagedOriginalPreservation()
    {
        var root = Path.Combine(Path.GetTempPath(), "KikicastInputSave-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            var display = new LayoutDisplay(@"monitor:\\?\DISPLAY#InputStoragePanel", "Owned panel");
            var input = new WindowLayoutInput(WindowLayoutInputKind.Arguments) { Arguments = ["a b", "", "\"; &"] };
            var layout = new WindowLayout(Guid.NewGuid(), "Owned") { Entries = [new(Guid.NewGuid(), @"app:C:\Owned.exe", display) { Input = input }], LaunchMissingApplications = true };
            var path = Path.Combine(root, "settings.json"); var preferences = new AppPreferences { WindowLayouts = [layout] }; JsonFile.Save(path, preferences); var bytes = File.ReadAllBytes(path);
            var loaded = JsonFile.Load(path, () => new AppPreferences()); Assert.Null(loaded.Validate()); Assert.Equal(input.Arguments, loaded.WindowLayouts[0].Entries[0].Input!.Arguments);
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.True(Record.Exception(() => JsonFile.Save(path, preferences with { WindowLayouts = [] })) is IOException or UnauthorizedAccessException);
            Assert.Equal(bytes, File.ReadAllBytes(path));
            File.WriteAllText(path, "{\"SchemaVersion\":1}"); bytes = File.ReadAllBytes(path); Assert.Empty(JsonFile.Load(path, () => new AppPreferences()).WindowLayouts); Assert.Equal(bytes, File.ReadAllBytes(path));
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(preferences).Replace("\"Kind\":2", "\"Kind\":99", StringComparison.Ordinal));
            bytes = File.ReadAllBytes(path); Assert.NotNull(JsonFile.Load(path, () => new AppPreferences()).Validate()); Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        finally { Directory.Delete(root, true); }
    }
}
