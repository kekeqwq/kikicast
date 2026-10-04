using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Kikicast.Core;
using Microsoft.Win32.SafeHandles;

namespace Kikicast.Windows;

[SupportedOSPlatform("windows")]
public static class LayoutWindowInventory
{
    public const int MaximumInspectedWindows = 2048, MaximumProcesses = 256, MaximumWindows = 512, MaximumApplications = 4096;
    public sealed record Snapshot(IReadOnlyList<LayoutWindow> Windows, IReadOnlyDictionary<long, ForegroundTarget> Targets,
        IReadOnlyDictionary<long, string> Displays, bool Limited);
    public static IReadOnlyList<LayoutApplication> Applications(IReadOnlyList<LauncherEntry> catalog, AppPreferences preferences)
    {
        var result = new List<LayoutApplication>();
        foreach (var entry in catalog.Take(MaximumApplications))
        {
            if (!entry.IsEnabled(preferences)) continue;
            // Read a current local link, without Resolve or argument rewriting;
            // saved target metadata must not match/launch a changed shortcut.
            var current = entry.AppUserModelId == null && entry.Path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ApplicationIndex.ReadShortcut(entry.Path) : entry;
            if (current == null) continue;
            var package = current.AppUserModelId ?? current.ShortcutAppUserModelId;
            if (package != null)
            {
                if (PackagedApplicationId.IsValid(package) && PackageRegistration.IsRegistered(package)
                    && (entry.AppUserModelId != null || LocalPathSafety.IsFile(entry.Path)))
                    result.Add(new(entry.Id, "package:" + package.ToLowerInvariant()));
                continue;
            }
            var target = current.ExecutablePath ?? current.Path;
            if (!target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !LocalPathSafety.IsFile(entry.Path) || !LocalPathSafety.IsFile(target)) continue;
            result.Add(new(entry.Id, "exe:" + ExecutablePath.Canonical(target).ToUpperInvariant()));
        }
        return result;
    }
    // One gesture-scoped visible top-level sweep, never process enumeration or a
    // timer. Process metadata is read once per visible PID; no titles/contents/argv.
    public static Snapshot Read(IReadOnlyList<LayoutApplication> applications)
    {
        if (applications.Count == 0) return new([], new Dictionary<long, ForegroundTarget>(), new Dictionary<long, string>(), false);
        if (applications.Count > MaximumApplications) return new([], new Dictionary<long, ForegroundTarget>(), new Dictionary<long, string>(), true);
        var allowed = applications.Select(x => x.MatchKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var processes = new Dictionary<uint, (string Key, DateTime Started)?>();
        var windows = new List<LayoutWindow>(); var targets = new Dictionary<long, ForegroundTarget>(); var displays = new Dictionary<long, string>();
        var inspected = 0; var limited = false;
        EnumCallback callback = (hwnd, _) =>
        {
            if (++inspected > MaximumInspectedWindows || windows.Count >= MaximumWindows) { limited = true; return false; }
            if (!Eligible(hwnd)) return true;
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == Environment.ProcessId || pid == 0) return true;
            if (!processes.TryGetValue(pid, out var identity))
            {
                if (processes.Count >= MaximumProcesses) { limited = true; return false; }
                identity = ProcessIdentity(pid); processes[pid] = identity;
            }
            if (identity is not { } app || !allowed.Contains(app.Key) || WindowManager.ReadFrame(hwnd) is not { } frame) return true;
            var target = new ForegroundTarget(hwnd, pid, app.Started);
            if (!target.IsValid()) return true;
            var handle = hwnd.ToInt64(); windows.Add(new(handle, app.Key, frame)); targets[handle] = target; displays[handle] = MonitorFromWindow(hwnd, 2).ToString();
            return true;
        };
        var complete = EnumWindows(callback, 0); GC.KeepAlive(callback);
        return new(windows, targets, displays, limited || !complete);
    }
    // Explicit owned fixture seam, never enabled by normal inventory/capture.
    public static Snapshot ReadOwned(IReadOnlyList<ForegroundTarget> fixtures, string matchKey)
    {
        if (fixtures.Count > MaximumWindows || fixtures.Any(x => x.ProcessId != Environment.ProcessId || !x.IsValid())) throw new ArgumentException("Only current-process owned fixture targets are accepted.");
        var windows = new List<LayoutWindow>(); var targets = new Dictionary<long, ForegroundTarget>(); var displays = new Dictionary<long, string>();
        foreach (var target in fixtures)
        {
            if (!IsWindowVisible(target.Handle) || IsIconic(target.Handle) || WindowManager.ReadFrame(target.Handle) is not { } frame) continue;
            var handle = target.Handle.ToInt64(); windows.Add(new(handle, matchKey, frame)); targets[handle] = target; displays[handle] = MonitorFromWindow(target.Handle, 2).ToString();
        }
        return new(windows, targets, displays, false);
    }
    public static WindowLayout Capture(Snapshot snapshot, IReadOnlyList<LayoutScreen> screens, IReadOnlyList<LayoutApplication> applications, long? focused = null)
    {
        var entries = new List<WindowLayoutEntry>(); Guid? front = null;
        var apps = applications.OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase).GroupBy(x => x.MatchKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().Id, StringComparer.OrdinalIgnoreCase);
        foreach (var window in snapshot.Windows.OrderBy(x => x.Frame.Y).ThenBy(x => x.Frame.X).ThenBy(x => x.Handle))
        {
            if (entries.Count >= WindowLayout.MaximumEntries) break;
            if (!apps.TryGetValue(window.MatchKey, out var app) || !snapshot.Displays.TryGetValue(window.Handle, out var displayId)) continue;
            var screen = screens.FirstOrDefault(x => x.Area.Id == displayId);
            if (screen == null) continue; // never primary/name/ordinal fallback
            var entry = WindowLayoutGeometry.CaptureEntry(app, screen, window.Frame);
            if (entry == null || entry.Validate() != null) continue;
            entries.Add(entry); if (window.Handle == focused) front = entry.Id;
        }
        return new(Guid.NewGuid(), "Captured layout") { UsesPreferredGap = false, Entries = entries, FrontmostEntryId = front };
    }
    private static bool Eligible(nint hwnd) => IsWindowVisible(hwnd) && !IsIconic(hwnd) && GetWindow(hwnd, 4) == 0
        && (GetWindowLongW(hwnd, -16) & 0x40000000) == 0 && (GetWindowLongW(hwnd, -20) & (0x80 | 0x08000000)) == 0
        && (DwmGetWindowAttribute(hwnd, 14, out var cloaked, 4) != 0 || cloaked == 0);
    private static (string, DateTime)? ProcessIdentity(uint pid)
    {
        using var process = OpenProcess(0x1000, false, pid);
        if (process.IsInvalid || !GetProcessTimes(process, out var created, out _, out _, out _)) return null;
        uint packageLength = 0;
        var packageStatus = GetPackageFullName(process, ref packageLength, 0);
        if (packageStatus != 15700) // only NO_PACKAGE can enter desktop matching
        {
            uint length = 131; var id = new StringBuilder(131);
            if (GetApplicationUserModelId(process, ref length, id) != 0 || !PackagedApplicationId.IsValid(id.ToString())) return null;
            return ("package:" + id.ToString().ToLowerInvariant(), DateTime.FromFileTime(created));
        }
        var path = new StringBuilder(4096); uint count = 4096;
        if (!QueryFullProcessImageNameW(process, 0, path, ref count) || !LocalPathSafety.IsFile(path.ToString())) return null;
        return ("exe:" + ExecutablePath.Canonical(path.ToString()).ToUpperInvariant(), DateTime.FromFileTime(created));
    }
    private delegate bool EnumCallback(nint hwnd, nint state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback, nint state);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern int GetWindowLongW(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out uint value, int size);
    [DllImport("kernel32.dll")] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool GetProcessTimes(SafeProcessHandle process, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll")] private static extern int GetPackageFullName(SafeProcessHandle process, ref uint length, nint name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetApplicationUserModelId(SafeProcessHandle process, ref uint length, StringBuilder name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, StringBuilder path, ref uint length);
}
