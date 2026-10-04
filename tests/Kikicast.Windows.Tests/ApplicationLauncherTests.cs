using Kikicast.Core;
using Kikicast.Windows;

namespace Kikicast.Windows.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class ApplicationLauncherTests
{
    [Fact]
    public void SourceMissingRemoteAndChangedIdentityRefuseBeforeLaunchCallbacks()
    {
        if (!OperatingSystem.IsWindows()) return;
        var count = 0;
        var root = Path.Combine(Path.GetTempPath(), "KikicastLauncher-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            var exe = Path.Combine(root, "owned.exe"); File.Copy(Environment.ProcessPath!, exe);
            var entry = new LauncherEntry("Owned", exe);
            Assert.Throws<InvalidOperationException>(() => ApplicationLauncher.Launch(entry, new() { ApplicationsEnabled = false }, desktopLaunch: _ => count++));
            Assert.Throws<InvalidOperationException>(() => ApplicationLauncher.Launch(entry with { RegistrationNames = ["owned.exe"] }, new() { IncludeWindowsAppPaths = false }, desktopLaunch: _ => count++));
            Assert.Throws<InvalidOperationException>(() => ApplicationLauncher.Launch(entry, new(), "exe:CHANGED", _ => count++));
            Assert.Throws<InvalidOperationException>(() => ApplicationLauncher.Launch(entry with { Path = @"\\server\owned.exe" }, new(), desktopLaunch: _ => count++));
            File.Delete(exe);
            Assert.Throws<InvalidOperationException>(() => ApplicationLauncher.Launch(entry, new(), desktopLaunch: _ => count++));
            Assert.Equal(0, count);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void PlainExeShellLaunchPreservesPathAndHasNoInjectedArgumentsElevationOrRedirection()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "KikicastLauncher-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            var exe = Path.Combine(root, "owned.exe"); File.Copy(Environment.ProcessPath!, exe); var count = 0;
            ApplicationLauncher.Launch(new("Owned", exe), new(), "exe:" + exe.ToUpperInvariant(), info =>
            {
                count++; Assert.Equal(exe, info.FileName); Assert.True(info.UseShellExecute); Assert.Equal(root, info.WorkingDirectory);
                Assert.Empty(info.ArgumentList); Assert.Empty(info.Arguments); Assert.Empty(info.Verb); Assert.False(info.RedirectStandardInput); Assert.False(info.RedirectStandardOutput);
            });
            Assert.Equal(1, count);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void PackagedSourceAndCurrentRegistrationGatesShareTheActivationPrimitive()
    {
        if (!OperatingSystem.IsWindows()) return;
        const string id = "Kikicast.Owned_abcdefghijklm!App"; var count = 0;
        var entry = new LauncherEntry("Owned package", "shell:AppsFolder\\" + id, AppUserModelId: id);
        Assert.Throws<InvalidOperationException>(() => ApplicationLauncher.Launch(entry, new() { IncludePackagedApplications = false }, registered: _ => true, packageLaunch: _ => count++));
        Assert.Throws<InvalidOperationException>(() => ApplicationLauncher.Launch(entry, new(), registered: _ => false, packageLaunch: _ => count++));
        Assert.Throws<InvalidOperationException>(() => ApplicationLauncher.Launch(entry, new(), "package:other", registered: _ => true, packageLaunch: _ => count++));
        Assert.Equal(0, count);
        ApplicationLauncher.Launch(entry, new(), "package:" + id.ToLowerInvariant(), registered: _ => true, packageLaunch: actual => { Assert.Equal(id, actual); count++; });
        Assert.Equal(1, count); // no installed package launched
    }
    [Fact]
    public async Task GestureHookLifetimeIsExplicitAndWaitingIsCancellableWithoutProcessLaunch()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var events = new LayoutWindowEvents(); // no UI/message pumping or target application acceptance claimed
        events.Dispose(); Assert.False(events.IsEnabled);
        await Assert.ThrowsAsync<InvalidOperationException>(() => events.WaitAsync(CancellationToken.None));
    }
}
