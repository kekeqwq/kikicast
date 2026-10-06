using System.Runtime.Versioning;
using Kikicast.Core;
using Microsoft.Win32;

namespace Kikicast.Windows;

public sealed record LogonStartupValue(string? Command, RegistryValueKind Kind = RegistryValueKind.String, bool Exists = true)
{
    public static LogonStartupValue Missing { get; } = new(null, Exists: false);
}
public interface ILogonStartupRegistry
{
    LogonStartupValue Read();
    void Write(LogonStartupValue value);
}
[SupportedOSPlatform("windows")]
public sealed class UserRunRegistry(string valueName, string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Run") : ILogonStartupRegistry
{
    public LogonStartupValue Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: false);
        var value = key?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return value == null ? LogonStartupValue.Missing : new(value as string, key!.GetValueKind(valueName));
    }
    public void Write(LogonStartupValue value)
    {
        if (!value.Exists) { using var existing = Registry.CurrentUser.OpenSubKey(keyPath, writable: true); existing?.DeleteValue(valueName, throwOnMissingValue: false); }
        else { using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true); key.SetValue(valueName, value.Command!, value.Kind); }
        if (Read() != value) throw new IOException("Windows startup registration did not match its readback.");
    }
}

[SupportedOSPlatform("windows")]
public sealed class LogonStartupRegistration(ILogonStartupRegistry registry)
{
    public LogonStartupValue Current => registry.Read();
    public string Describe(AppPreferences preferences, string executable)
    {
        var value = registry.Read();
        if (!value.Exists) return preferences.StartAtLogon ? "Saved on, but Windows registration is missing. Save changes to register this location." : "Off. No login startup registration.";
        var owned = IsOwned(value, preferences, executable);
        if (!owned) return "Another entry owns this startup name. Kikicast will not replace or delete it.";
        return "Current-user startup entry exists. Windows Startup apps/policy may disable or delay it; Kikicast never overrides those controls.";
    }
    private static bool IsOwned(LogonStartupValue value, AppPreferences saved, string executable)
        => value.Kind is RegistryValueKind.String or RegistryValueKind.ExpandString && value.Command != null
        && (LogonStartup.ValidExecutable(executable) && value.Command.Equals(LogonStartup.Command(executable), StringComparison.OrdinalIgnoreCase)
            || saved.StartupExecutablePath != null && value.Command.Equals(LogonStartup.Command(saved.StartupExecutablePath), StringComparison.OrdinalIgnoreCase));
    public Change Apply(AppPreferences saved, bool enabled, string executable)
    {
        var before = registry.Read();
        var desired = enabled ? new LogonStartupValue(LogonStartup.Command(executable)) : LogonStartupValue.Missing;
        if (before == desired) return new(registry, before, desired, changed: false);
        if (before.Exists && !IsOwned(before, saved, executable))
        {
            if (!enabled) return new(registry, before, before, changed: false);
            throw new InvalidOperationException("Another entry owns the Kikicast startup name; not replaced. Resolve it in Windows Startup apps before enabling.");
        }
        if (registry.Read() != before) throw new InvalidOperationException("Windows startup registration changed concurrently; not overwritten.");
        try { registry.Write(desired); }
        catch
        {
            if (registry.Read() == desired) registry.Write(before);
            throw;
        }
        return new(registry, before, desired, changed: true);
    }
    public sealed class Change(ILogonStartupRegistry registry, LogonStartupValue before, LogonStartupValue expected, bool changed)
    {
        public void Rollback()
        {
            if (!changed) return;
            if (registry.Read() != expected) throw new InvalidOperationException("Startup rollback refused: Windows/another tool changed the entry; external state was not overwritten.");
            registry.Write(before);
        }
    }
}
