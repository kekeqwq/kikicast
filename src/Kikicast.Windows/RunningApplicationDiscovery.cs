using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Kikicast.Core;

namespace Kikicast.Windows;

[SupportedOSPlatform("windows")]
public sealed class RunningApplicationDiscovery : IDisposable
{
    private sealed record Work(nint Window = 0, string? Add = null, string? Forget = null, bool Scan = false,
        bool Prune = false, TaskCompletionSource? Completion = null, HashSet<string>? Known = null);
    private readonly string file;
    private readonly uint? processFilter;
    private readonly Channel<Work> queue = Channel.CreateBounded<Work>(new BoundedChannelOptions(256) { SingleReader = true });
    private readonly CancellationTokenSource stopping = new();
    private readonly Task worker;
    private readonly WinEvent callback;
    private readonly Dictionary<string, FileSystemWatcher> watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<(uint Pid, long Created)> seen = [];
    private readonly System.Threading.Timer maintenance;
    private nint foregroundHook, showHook;
    private DiscoveredApplications data = new();
    private IReadOnlyList<DiscoveredApplication> published = Array.Empty<DiscoveredApplication>();
    private HashSet<string> knownExecutables = new(StringComparer.OrdinalIgnoreCase);
    private bool knownReady, desiredEnabled;
    private bool writable = true, disposed;
    public event Action? Changed;
    public event Action? StartMenuInvalidated;
    public event Action<string>? Failed;
    public IReadOnlyList<DiscoveredApplication> Applications => Volatile.Read(ref published);
    public string? Warning { get; private set; }
    public bool IsEnabled => foregroundHook != 0 && showHook != 0;

    public RunningApplicationDiscovery(string file, uint? processFilter = null)
    {
        this.file = file; this.processFilter = processFilter;
        try
        {
            var saved = JsonFile.Load(file, () => new DiscoveredApplications());
            if (!saved.IsValid) throw new InvalidDataException("Invalid discovered applications file.");
            data = saved;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or NotSupportedException)
        { writable = false; Warning = "Discovered apps could not be read. Writing is paused; the original file was kept."; }
        published = data.Applications.ToArray();
        callback = (_, _, hwnd, objectId, childId, _, _) =>
        {
            if (hwnd != 0 && objectId == 0 && childId == 0) queue.Writer.TryWrite(new(Window: hwnd));
        };
        worker = Task.Run(Consume);
        queue.Writer.TryWrite(new(Prune: true));
        // Only stat the bounded saved paths, never poll the process table or walk directories.
        maintenance = new(_ => queue.Writer.TryWrite(new(Prune: true)), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }
    // Must be called on the message-loop thread (WPF Dispatcher). Native callbacks do no filesystem IO.
    public void SetEnabled(bool enabled)
    {
        if (disposed) return;
        desiredEnabled = enabled;
        if (foregroundHook != 0) UnhookWinEvent(foregroundHook);
        if (showHook != 0) UnhookWinEvent(showHook);
        foregroundHook = showHook = 0;
        if (!enabled || !knownReady) return;
        foregroundHook = SetWinEventHook(3, 3, 0, callback, processFilter ?? 0, 0, 2); // foreground, OUTOFCONTEXT + SKIPOWNPROCESS
        showHook = SetWinEventHook(0x8002, 0x8002, 0, callback, processFilter ?? 0, 0, 2); // top-level show only, not all WinEvents
        if (!IsEnabled)
        {
            if (foregroundHook != 0) UnhookWinEvent(foregroundHook);
            if (showHook != 0) UnhookWinEvent(showHook);
            foregroundHook = showHook = 0;
            Warning = "Windows did not enable application discovery. Use Scan running apps or Add EXE.";
        }
        else queue.Writer.TryWrite(new(Scan: true));
    }
    public async Task SetKnownApplicationsAsync(IEnumerable<LauncherEntry> entries)
    {
        var known = entries.Where(x => x.ExecutablePath != null).Select(x => x.ExecutablePath!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        await Submit(new(Known: known));
        knownReady = true;
        SetEnabled(desiredEnabled);
    }
    public Task ScanRunningAsync() => Submit(new(Scan: true));
    public Task RevalidateAsync() => Submit(new(Prune: true));
    public Task AddAsync(string path) => Submit(new(Add: path));
    public Task ForgetAsync(string path) => Submit(new(Forget: path));
    private async Task Submit(Work work)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await queue.Writer.WriteAsync(work with { Completion = completion }, stopping.Token);
        if (await Task.WhenAny(completion.Task, worker) == worker)
        {
            await worker;
            throw new InvalidOperationException("Application discovery has stopped.");
        }
        await completion.Task;
    }
    private async Task Consume()
    {
        try
        {
            while (await queue.Reader.WaitToReadAsync(stopping.Token))
            {
                var before = data;
                var completions = new List<TaskCompletionSource>();
                var batchCount = 0;
                while (batchCount++ < 64 && queue.Reader.TryRead(out var work))
                {
                    try
                    {
                        if (work.Known != null)
                        {
                            knownExecutables = work.Known; seen.Clear();
                            data = data.Prune(path => !knownExecutables.Contains(path) && LocalPathSafety.IsFile(path));
                        }
                        else if (work.Add != null) Remember(Path.GetFullPath(work.Add), true);
                        else if (work.Forget != null) data = data.Forget(work.Forget);
                        else if (work.Scan) EnumWindows((hwnd, _) => { Observe(hwnd); return true; }, 0);
                        else if (work.Prune)
                        {
                            data = data.Prune(LocalPathSafety.IsFile);
                            if (knownExecutables.Any(path => !LocalPathSafety.IsFile(path))) StartMenuInvalidated?.Invoke();
                        }
                        else Observe(work.Window);
                        if (work.Completion != null) completions.Add(work.Completion);
                    }
                    catch (Exception ex)
                    {
                        if (work.Completion != null) work.Completion.TrySetException(ex);
                        else Failed?.Invoke("Application discovery skipped an entry: " + ex.Message);
                    }
                }
                // Immutable list snapshots, batch writes, at most 64 directory notification handles.
                UpdateWatchers();
                if (!before.Applications.SequenceEqual(data.Applications) || !before.IgnoredPaths.SequenceEqual(data.IgnoredPaths))
                {
                    Volatile.Write(ref published, data.Applications.ToArray());
                    if (writable)
                    {
                        try { JsonFile.Save(file, data); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Warning = "Discovered apps could not be saved: " + ex.Message; Failed?.Invoke(Warning); }
                    }
                    Changed?.Invoke();
                }
                foreach (var completion in completions) completion.TrySetResult();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Warning = "Application discovery stopped: " + ex.Message; }
        finally
        {
            foreach (var watcher in watchers.Values) watcher.Dispose();
            while (queue.Reader.TryRead(out var pending)) pending.Completion?.TrySetCanceled();
        }
    }
    private void Observe(nint hwnd)
    {
        if (!IsWindowVisible(hwnd) || GetAncestor(hwnd, 2) != hwnd) return;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0 || pid == Environment.ProcessId || processFilter is { } filter && pid != filter) return;
        var process = OpenProcess(0x1000, false, pid);
        if (process == 0) return;
        try
        {
            if (!GetProcessTimes(process, out var created, out _, out _, out _)) return;
            var identity = (pid, created);
            if (seen.Contains(identity)) return;
            uint packageLength = 0;
            // Package identities belong to AppsFolder/AUMID, not a versioned host EXE.
            // Query only this event's already opened process; no process polling or data collection.
            if (GetPackageFullName(process, ref packageLength, 0) != 15700) // APPMODEL_ERROR_NO_PACKAGE
            { if (seen.Count >= 512) seen.Clear(); seen.Add(identity); return; }
            var path = new StringBuilder(32768); var length = path.Capacity;
            if (!QueryFullProcessImageNameW(process, 0, path, ref length)) return;
            if (seen.Count >= 512) seen.Clear();
            seen.Add(identity);
            Remember(path.ToString(), false);
        }
        finally { CloseHandle(process); }
    }
    private void Remember(string path, bool manual)
    {
        if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !Path.IsPathFullyQualified(path) || !LocalPathSafety.IsFile(path))
        { if (manual) throw new InvalidDataException("Choose an existing local EXE file without linked-folder traversal."); return; }
        path = ExecutablePath.Canonical(path);
        if (knownExecutables.Contains(path))
        { if (manual) throw new InvalidDataException("This EXE is already searchable through the Start menu."); return; }
        if (!manual)
        {
            var name = Path.GetFileName(path);
            if (path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || new[] { "Kikicast.App.exe", "pwsh.exe", "powershell.exe", "cmd.exe", "conhost.exe", "OpenConsole.exe", "WindowsTerminal.exe", "msedgewebview2.exe" }.Contains(name, StringComparer.OrdinalIgnoreCase)) return;
        }
        var title = Path.GetFileNameWithoutExtension(path);
        try
        {
            var description = FileVersionInfo.GetVersionInfo(path).FileDescription;
            if (!string.IsNullOrWhiteSpace(description) && description.Length <= 120) title = description.Trim();
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException) { }
        data = data.Remember(new(path, title, DateTimeOffset.UtcNow), manual);
    }
    private void UpdateWatchers()
    {
        var directories = data.Applications.Select(x => Path.GetDirectoryName(x.Path)!).Concat(knownExecutables.Select(x => Path.GetDirectoryName(x)!)).Distinct(StringComparer.OrdinalIgnoreCase).Take(64).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var removed in watchers.Keys.Where(x => !directories.Contains(x)).ToList()) { watchers[removed].Dispose(); watchers.Remove(removed); }
        foreach (var directory in directories.Where(x => !watchers.ContainsKey(x) && LocalPathSafety.TryInspect(x, out _)))
        {
            try
            {
                var watcher = new FileSystemWatcher(directory, "*.exe") { NotifyFilter = NotifyFilters.FileName, IncludeSubdirectories = false };
                watcher.Deleted += (_, _) => queue.Writer.TryWrite(new(Prune: true));
                watcher.Renamed += (_, _) => queue.Writer.TryWrite(new(Prune: true));
                watcher.Error += (_, _) => queue.Writer.TryWrite(new(Prune: true));
                watcher.EnableRaisingEvents = true; watchers.Add(directory, watcher);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
    }
    public void Dispose()
    {
        if (disposed) return;
        SetEnabled(false); disposed = true;
        maintenance.Dispose(); queue.Writer.TryComplete();
        // Drain pending writes before cancellation; never wait for event callbacks to do IO.
        if (!worker.Wait(TimeSpan.FromSeconds(2))) stopping.Cancel();
    }
    private delegate void WinEvent(nint hook, uint eventId, nint hwnd, int objectId, int childId, uint thread, uint time);
    private delegate bool EnumCallback(nint hwnd, nint state);
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEvent callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback, nint state);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("kernel32.dll")] private static extern nint OpenProcess(uint access, bool inherit, uint process);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern bool QueryFullProcessImageNameW(nint process, uint flags, StringBuilder path, ref int length);
    [DllImport("kernel32.dll")] private static extern int GetPackageFullName(nint process, ref uint length, nint name);
    [DllImport("kernel32.dll")] private static extern bool GetProcessTimes(nint process, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
}
