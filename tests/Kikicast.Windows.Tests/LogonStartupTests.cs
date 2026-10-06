using Kikicast.Core;
using Kikicast.Windows;
using Microsoft.Win32;

namespace Kikicast.Windows.Tests;
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class LogonStartupTests
{
    private const string Exe = "C:\\Owned\\Kikicast.App.exe";
    private class Fake : ILogonStartupRegistry
    {
        public LogonStartupValue Value = LogonStartupValue.Missing;
        public bool Fail;
        public LogonStartupValue Read() => Value;
        public void Write(LogonStartupValue value) { if (Fail) throw new IOException("owned refusal"); Value = value; }
    }
    [Fact] public void DefaultOffEnableDisableAndSaveRollback()
    {
        if (!OperatingSystem.IsWindows()) return;
        var fake = new Fake(); var registration = new LogonStartupRegistration(fake); var enabled = registration.Apply(new(), true, Exe);
        Assert.Equal(LogonStartup.Command(Exe), fake.Value.Command); enabled.Rollback(); Assert.False(fake.Value.Exists);
        registration.Apply(new(), true, Exe); var disabled = registration.Apply(new() { StartAtLogon = true, StartupExecutablePath = Exe }, false, Exe); Assert.False(fake.Value.Exists); disabled.Rollback(); Assert.True(fake.Value.Exists);
    }
    [Fact] public void ForeignValuesAndConcurrentChangesAreNeverOverwritten()
    {
        if (!OperatingSystem.IsWindows()) return;
        var fake = new Fake { Value = new("foreign", RegistryValueKind.ExpandString) }; var registration = new LogonStartupRegistration(fake);
        Assert.Throws<InvalidOperationException>(() => registration.Apply(new(), true, Exe)); registration.Apply(new(), false, Exe); Assert.Equal("foreign", fake.Value.Command);
        fake.Value = LogonStartupValue.Missing; var change = registration.Apply(new(), true, Exe); fake.Value = new("external-change"); Assert.Throws<InvalidOperationException>(change.Rollback); Assert.Equal("external-change", fake.Value.Command);
    }
    [Fact] public void RegistrationFailureDoesNotPublishAndRelocationUsesSavedOwnership()
    {
        if (!OperatingSystem.IsWindows()) return;
        var fake = new Fake { Fail = true }; var registration = new LogonStartupRegistration(fake); Assert.Throws<IOException>(() => registration.Apply(new(), true, Exe)); Assert.False(fake.Value.Exists);
        fake.Fail = false; fake.Value = new(LogonStartup.Command(Exe)); registration.Apply(new() { StartupExecutablePath = Exe }, true, "C:\\New\\Kikicast.App.exe"); Assert.Equal("\"C:\\New\\Kikicast.App.exe\"", fake.Value.Command);
    }
    [Fact] public void PrivateRegistryReadbackNeverRegistersRealStartup()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = @"Software\Kikicast.Tests\Startup\" + Guid.NewGuid().ToString("N");
        try
        { var registry = new UserRunRegistry("owned", root); Assert.False(registry.Read().Exists); var registration = new LogonStartupRegistration(registry); registration.Apply(new(), true, Exe); Assert.Equal(LogonStartup.Command(Exe), registry.Read().Command); registration.Apply(new(), false, Exe); Assert.False(registry.Read().Exists); }
        finally { Registry.CurrentUser.DeleteSubKeyTree(root, false); }
    }
}
