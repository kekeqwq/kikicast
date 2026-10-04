using System.Diagnostics;
using System.Runtime.Versioning;
using Kikicast.Core;

namespace Kikicast.Windows;

[SupportedOSPlatform("windows")]
public static class ApplicationLauncher
{
    // The same launch primitive for normal Enter/global app execution and layout
    // opens. Original .lnk path/arguments are never replaced by its target EXE.
    public static void Launch(LauncherEntry entry, AppPreferences preferences, string? expectedMatchKey = null,
        Action<ProcessStartInfo>? desktopLaunch = null, Func<string, bool>? registered = null, Action<string>? packageLaunch = null, WindowLayoutInput? input = null, Action<string, PreparedLayoutInput>? packageInputLaunch = null)
    {
        if (!entry.IsEnabled(preferences)) throw new InvalidOperationException("This application source or master gate is disabled.");
        var registration = registered ?? PackageRegistration.IsRegistered;
        string key;
        if (entry.AppUserModelId is { } id)
        {
            if (!registration(id)) throw new InvalidOperationException("This packaged application was removed or is unavailable.");
            key = "package:" + id.ToLowerInvariant();
        }
        else
        {
            if (!LocalPathSafety.IsFile(entry.Path)) throw new InvalidOperationException("This application was deleted, linked or is unavailable.");
            var current = entry.Path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ApplicationIndex.ReadShortcut(entry.Path) : entry;
            if (current?.ShortcutAppUserModelId is { } shortcutId)
            {
                if (!registration(shortcutId)) throw new InvalidOperationException("This shortcut's packaged application is unavailable.");
                key = "package:" + shortcutId.ToLowerInvariant();
            }
            else
            {
                var target = current?.ExecutablePath ?? (entry.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? entry.Path : null);
                if (target == null || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !LocalPathSafety.IsFile(target))
                    throw new InvalidOperationException("This application's current local executable target is unavailable.");
                key = "exe:" + ExecutablePath.Canonical(target).ToUpperInvariant();
            }
        }
        if (expectedMatchKey != null && !key.Equals(expectedMatchKey, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Application target changed since the layout snapshot; not launched.");
        var preparedInput = input == null ? null : LayoutInputPolicy.Prepare(entry, input);
        if (entry.AppUserModelId != null)
        {
            if (preparedInput == null) PackagedApplication.Activate(entry, preferences, registration, packageLaunch);
            else PackagedApplication.OpenInput(entry, preferences, preparedInput, registration, packageInputLaunch);
        }
        else
        {
            var info = new ProcessStartInfo(entry.Path) { UseShellExecute = true };
            if (entry.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) info.WorkingDirectory = Path.GetDirectoryName(entry.Path);
            if (preparedInput != null)
            {
                if (preparedInput.Kind == WindowLayoutInputKind.Arguments) foreach (var value in preparedInput.Arguments) info.ArgumentList.Add(value);
                else info.ArgumentList.Add(preparedInput.Value!); // one literal value; no shell wrapper, templating or file association fallback
            }
            if (desktopLaunch != null) desktopLaunch(info);
            else { using var process = Process.Start(info); }
        }
    }
}
