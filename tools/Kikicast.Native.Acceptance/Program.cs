using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Kikicast.Core;
using Kikicast.Windows;
using Microsoft.Win32.SafeHandles;

namespace Kikicast.Native.Acceptance;

// Explicit opt-in native acceptance, never a benchmark or production startup route.
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        NativeAcceptanceOptions options;
        try { options = NativeAcceptanceOptions.Parse(args); }
        catch (ArgumentException ex) { Console.Error.WriteLine(ex.Message); return 2; }
        if (options.Exe == null) { Console.WriteLine(JsonSerializer.Serialize(DisplayReport(), JsonOptions)); return 0; }
        if (!LocalPathSafety.IsFile(options.Exe)) { Console.Error.WriteLine("Refusing missing/remote/linked executable."); return 2; }
        var root = Path.GetFullPath(options.Evidence!);
        if (!SafeEvidenceRoot(root)) { Console.Error.WriteLine("Refusing non-local, linked or nonempty evidence directory."); return 2; }
        Directory.CreateDirectory(root);
        var exitCode = 1;
        using var context = new ApplicationContext();
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        using var kick = new System.Windows.Forms.Timer { Interval = 10 };
        kick.Tick += async (_, _) =>
        {
            kick.Stop();
            try { exitCode = await Discover(options, root); }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { context.ExitThread(); }
        };
        kick.Start(); System.Windows.Forms.Application.Run(context); return exitCode;
    }
    private static bool SafeEvidenceRoot(string root)
    {
        if (!Path.IsPathFullyQualified(root) || root.StartsWith("\\\\", StringComparison.Ordinal) || root.Equals(Path.GetPathRoot(root), StringComparison.OrdinalIgnoreCase)) return false;
        if (LocalPathSafety.TryInspect(root, out var attributes)) return (attributes & FileAttributes.Directory) != 0 && !Directory.EnumerateFileSystemEntries(root).Any();
        var parent = Path.GetDirectoryName(root);
        // Only one new child under a verified existing safe parent; do not walk
        // past a rejected linked ancestor and later create through it.
        if (parent == null || !LocalPathSafety.TryInspect(parent, out attributes) || (attributes & FileAttributes.Directory) == 0) return false;
        try { _ = File.GetAttributes(root); return false; }
        catch (FileNotFoundException) { return true; }
        catch (DirectoryNotFoundException) { return true; }
    }
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static object DisplayReport()
    {
        var monitors = WindowManager.Displays(); var stable = LayoutDisplays.Read();
        return new { schemaVersion = 1, capturedUtc = DateTimeOffset.UtcNow, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            nativeDisplayCount = monitors.Count, identifiedDisplayCount = stable.Count, mixedDpi = monitors.Select(x => x.Scale).Distinct().Count() > 1,
            monitors = monitors.Select(x => new { x.Id, x.Bounds, x.WorkArea, dpi = x.Scale * 96, x.Scale, identified = stable.Any(s => s.Area.Id == x.Id) }).ToArray(),
            displayConfigurationWritten = false, scope = "Public native monitor/work-area/DPI/device-path metadata; no desktop contents collected" };
    }
    private static async Task<int> Discover(NativeAcceptanceOptions options, string root)
    {
        var exe = options.Exe!; var report = new Dictionary<string, object?> { ["schemaVersion"] = 1, ["exe"] = exe, ["startedUtc"] = DateTimeOffset.UtcNow,
            ["displays"] = DisplayReport(), ["scope"] = "Explicitly authorized one EXE launch, event discovery, isolated store and created-job-only termination; no user profile edits/titles/contents/argv collection", ["knownIndexIsolatedEmpty"] = true };
        var errors = new List<string>(); var existing = 0;
        // One preflight of this executable's basename only, not process polling.
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
        {
            using (process)
            {
                existing++; // conservatively refuse same-name inaccessible/different-path instances too
            }
        }
        report["preexistingSameNameCount"] = existing;
        if (existing != 0) { report["blockedPreexisting"] = true; WriteReport(); return 1; }
        CreatedProcess? created = null;
        try
        {
            created = CreatedProcess.Suspended(exe); report["createdPid"] = created.Pid;
            var discoveredFile = Path.Combine(root, "discovered-apps.json");
            using var observer = new RunningApplicationDiscovery(discoveredFile, created.Pid);
            var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            observer.Failed += text => { lock (errors) errors.Add(text); };
            observer.Changed += () => { if (observer.Applications.Any(x => x.Path.Equals(exe, StringComparison.OrdinalIgnoreCase))) changed.TrySetResult(); };
            await observer.SetKnownApplicationsAsync([]); observer.SetEnabled(true);
            if (!observer.IsEnabled) throw new InvalidOperationException("Discovery hooks not enabled before resume: " + observer.Warning);
            report["hooksEnabledBeforeResume"] = true; created.Resume(); report["launchCount"] = 1;
            try { await changed.Task.WaitAsync(TimeSpan.FromSeconds(25)); }
            catch (TimeoutException) { errors.Add("No matching discovery record within 25 seconds."); }
            var matches = observer.Applications.Where(x => x.Path.Equals(exe, StringComparison.OrdinalIgnoreCase)).ToArray();
            report["discoveredExactlyOnce"] = matches.Length == 1; report["discoveredName"] = matches.FirstOrDefault()?.Name;
            report["manualAddUsed"] = false; report["scanAfterResumeUsed"] = false;
            if (matches.Length == 1)
            {
                await observer.RevalidateAsync();
                var row = new LauncherEntry(matches[0].Name, matches[0].Path, matches[0].Path);
                report["nameSearchFindsJeppview"] = LauncherMatch.Match(LauncherSearchText.Create("jeppview"), row.SearchFields.Title) != null;
                if (options.Exercise)
                {
                    await Task.Delay(1000); // bounded UI-startup settle, not process polling
                    try { report["createdWindowExercise"] = await ExerciseCreatedWindow(exe, created.Pid); }
                    catch (Exception ex) { report["createdWindowExerciseError"] = ex.ToString(); }
                }
            }
            observer.SetEnabled(false); created.Stop(); report["createdJobTerminated"] = true; report["createdProcessExited"] = created.Exited;
            await observer.RevalidateAsync();
            report["recordRetainedAfterExit"] = observer.Applications.Count(x => x.Path.Equals(exe, StringComparison.OrdinalIgnoreCase)) == 1;
            using var reloaded = new RunningApplicationDiscovery(discoveredFile, created.Pid);
            report["recordReloadedAfterExit"] = reloaded.Applications.Count(x => x.Path.Equals(exe, StringComparison.OrdinalIgnoreCase)) == 1;
            report["fileStillExists"] = LocalPathSafety.IsFile(exe);
            report["passedDiscoveryCriterion"] = matches.Length == 1 && created.Exited && (bool)report["recordReloadedAfterExit"]!;
            report["observerWarning"] = observer.Warning;
        }
        catch (Exception ex) { errors.Add(ex.ToString()); }
        finally
        {
            if (created != null)
            {
                try { created.Stop(); report["cleanupCreatedProcessExited"] = created.Exited; }
                catch (Exception ex) { errors.Add("Created-job cleanup: " + ex.Message); }
                finally { created.Dispose(); }
            }
            WriteReport();
        }
        var passed = report.GetValueOrDefault("passedDiscoveryCriterion") is true;
        Console.WriteLine(passed ? "PASS: authorized EXE discovered and persisted; created job terminated." : "FAIL: see native-discovery-evidence.json.");
        return passed ? 0 : 1;
        void WriteReport()
        {
            lock (errors) report["errors"] = errors.ToArray(); report["endedUtc"] = DateTimeOffset.UtcNow;
            using var stream = new FileStream(Path.Combine(root, "native-discovery-evidence.json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            JsonSerializer.Serialize(stream, report, JsonOptions);
        }
    }
    private static async Task<object> ExerciseCreatedWindow(string exe, uint pid)
    {
        var screens = LayoutDisplays.Read();
        var apps = LayoutWindowInventory.Applications([new("Authorized acceptance app", exe, exe)], new() { WindowManagementEnabled = true });
        var snapshot = LayoutWindowInventory.Read(apps);
        var own = snapshot.Targets.Values.Where(x => x.ProcessId == pid && x.IsValid()).ToArray();
        if (own.Length == 0) return new { productionInventoryMatched = false, placed = false, reason = "No eligible unowned top-level window yet; discovery does not imply layout compatibility." };
        var captured = LayoutWindowInventory.Capture(snapshot with
        { Windows = snapshot.Windows.Where(x => own.Any(y => y.Handle.ToInt64() == x.Handle)).ToArray() }, screens, apps);
        var target = own.OrderByDescending(x => { var frame = WindowManager.ReadFrame(x.Handle)!.Value; return frame.Width * frame.Height; }).First();
        var manager = new WindowManager(); var original = WindowManager.ReadFrame(target.Handle)!.Value;
        var nativeScreens = WindowManager.Displays(); var samples = new List<object>(); var tourActions = new List<string>();
        static bool Near(Kikicast.Core.Rect a, Kikicast.Core.Rect b) => Math.Abs(a.X - b.X) <= 4 && Math.Abs(a.Y - b.Y) <= 4 && Math.Abs(a.Width - b.Width) <= 4 && Math.Abs(a.Height - b.Height) <= 4;
        async Task Restore()
        {
            if (await manager.ExecuteAsync(WindowAction.Restore, target) is { } error) throw new InvalidOperationException("Authorized app Restore: " + error);
            await Task.Delay(100);
            if (!Near(WindowManager.ReadFrame(target.Handle)!.Value, original)) throw new InvalidOperationException("Authorized app Restore geometry differs from its initial frame.");
        }
        try
        {
            if (nativeScreens.Count is > 1 and <= 4)
            {
                foreach (var action in new[] { WindowAction.LeftHalf, WindowAction.RightHalf, WindowAction.TopHalf, WindowAction.BottomHalf })
                {
                    WindowCycleState? previous = null;
                    for (var step = 0; step <= WindowCycle.Length(WindowCycleMode.Displays, action, nativeScreens.Count); step++)
                    {
                        var host = WindowManager.DisplayForWindow(target.Handle)!; var before = WindowManager.ReadFrame(target.Handle)!.Value;
                        var decision = WindowCycle.Decide(WindowCycleMode.Displays, action, previous, host, nativeScreens, previous != null);
                        var plan = WindowGeometry.Resolve(action, before, host, nativeScreens, 8, decision)!;
                        if (await manager.ExecuteAsync(action, target, 8, WindowCycleMode.Displays) is { } error) throw new InvalidOperationException("Authorized app half-cycle: " + error);
                        var actual = WindowManager.ReadFrame(target.Handle)!.Value; var landing = WindowManager.DisplayForWindow(target.Handle)!;
                        var expected = WindowGeometry.AnchorSize(plan.AnchorAction, plan.Frame, Math.Max(plan.Frame.Width, actual.Width), Math.Max(plan.Frame.Height, actual.Height));
                        samples.Add(new { action = action.ToString(), step, plannedDisplay = plan.Display.Id, landingDisplay = landing.Id, requested = plan.Frame, actual });
                        if (landing.Id != plan.Display.Id || !Near(actual, expected)) throw new InvalidOperationException("Authorized app half-cycle destination/geometry mismatch.");
                        previous = WindowCycle.Remember(action, decision, landing.Id, nativeScreens);
                    }
                    await Restore(); tourActions.Add(action.ToString());
                }
            }
            var size = new CustomWindowSize(Guid.NewGuid(), "Authorized native acceptance size") { Width = new(560, WindowSizeUnit.Dip), Height = new(60), Anchor = WindowSizeAnchor.BottomRight, OffsetX = -10, OffsetY = -10 };
            if (await manager.ExecuteAsync(size, target, 8) is { } sizeError) throw new InvalidOperationException(sizeError);
            var sizeFrame = WindowManager.ReadFrame(target.Handle)!.Value; var sizeHost = WindowManager.DisplayForWindow(target.Handle)!;
            if (!Near(sizeFrame, size.Reanchor(sizeHost, 8, sizeFrame.Width, sizeFrame.Height))) throw new InvalidOperationException("Authorized app custom-size anchor mismatch.");
            var movedDisplay = false;
            if (nativeScreens.Count > 1)
            {
                var move = WindowGeometry.Resolve(WindowAction.NextDisplay, sizeFrame, sizeHost, nativeScreens, 8)!;
                if (await manager.ExecuteAsync(WindowAction.NextDisplay, target, 8) is { } moveError) throw new InvalidOperationException(moveError);
                movedDisplay = WindowManager.DisplayForWindow(target.Handle)!.Id == move.Display.Id;
                if (!movedDisplay) throw new InvalidOperationException("Authorized app Next Display did not land on its explicit destination.");
            }
            await Restore();
            var hostBeforeLayout = WindowManager.DisplayForWindow(target.Handle)!;
            var destination = screens.FirstOrDefault(x => x.Area.Id != hostBeforeLayout.Id) ?? screens.First(x => x.Area.Id == hostBeforeLayout.Id);
            var entry = new WindowLayoutEntry(Guid.NewGuid(), apps[0].Id, destination.Display) { WidthFraction = .65, HeightFraction = .7, Anchor = WindowSizeAnchor.Center };
            var layout = new WindowLayout(Guid.NewGuid(), "Authorized native acceptance layout") { Entries = [entry], FrontmostEntryId = null };
            snapshot = LayoutWindowInventory.Read(apps);
            snapshot = snapshot with { Windows = snapshot.Windows.Where(x => x.Handle == target.Handle.ToInt64()).ToArray() };
            var planLayout = WindowLayoutPlan.Make(layout, screens, apps, snapshot.Windows, 8);
            if (planLayout.Placements.Count != 1) throw new InvalidOperationException("Authorized app was not resolved by the production layout planner.");
            if (await manager.ExecuteAsync(WindowAction.Center, target) is { } primeError) throw new InvalidOperationException(primeError);
            var outcomes = await manager.ApplyLayoutAsync(planLayout, screens, snapshot.Targets);
            if (!outcomes.Single().Placed || WindowManager.DisplayForWindow(target.Handle)!.Id != destination.Area.Id) throw new InvalidOperationException("Authorized app cross-display layout was refused: " + outcomes[0].Error);
            await Restore();
            return new { productionInventoryMatched = true, eligibleCreatedWindowCount = own.Length, capturedEntryCount = captured.Entries.Count,
                nativeDisplayCount = nativeScreens.Count, completeHalfTourActions = tourActions, samples, customDipPercentSize = true, nextDisplay = movedDisplay,
                crossDisplayLayout = nativeScreens.Count > 1, priorCommandRestorePreserved = true, placed = true, mixedDpiAccepted = false, hotplugAccepted = false,
                scope = "Authorized newly created app process; production inventory/placement, no input/title/content collection; no display configuration writes" };
        }
        finally { if (target.IsValid()) await manager.ExecuteAsync(WindowAction.Restore, target); }
    }
    private sealed class KernelHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public KernelHandle(nint handle) : base(true) => SetHandle(handle);
        protected override bool ReleaseHandle() => CloseHandle(handle);
    }
    private sealed class CreatedProcess : IDisposable
    {
        private readonly KernelHandle job, process, thread;
        private bool stopped;
        public uint Pid { get; }
        public bool Exited => WaitForSingleObject(process, 0) == 0;
        private CreatedProcess(KernelHandle job, KernelHandle process, KernelHandle thread, uint pid)
        { this.job = job; this.process = process; this.thread = thread; Pid = pid; }
        public static CreatedProcess Suspended(string exe)
        {
            var job = new KernelHandle(CreateJobObjectW(0, null));
            if (job.IsInvalid) { job.Dispose(); throw new System.ComponentModel.Win32Exception(); }
            var limits = new JobLimits { Basic = new() { Flags = 0x2000 } }; // kill only new job members on close
            if (!SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<JobLimits>())) { job.Dispose(); throw new System.ComponentModel.Win32Exception(); }
            var startup = new StartupInfo { Size = (uint)Marshal.SizeOf<StartupInfo>() };
            if (!CreateProcessW(exe, null, 0, 0, false, 4, 0, Path.GetDirectoryName(exe), ref startup, out var info)) { job.Dispose(); throw new System.ComponentModel.Win32Exception(); }
            var process = new KernelHandle(info.Process); var thread = new KernelHandle(info.Thread);
            if (!AssignProcessToJobObject(job, process))
            { TerminateProcess(process, 1); thread.Dispose(); process.Dispose(); job.Dispose(); throw new System.ComponentModel.Win32Exception(); }
            return new(job, process, thread, info.Pid);
        }
        public void Resume() { if (ResumeThread(thread) == uint.MaxValue) throw new System.ComponentModel.Win32Exception(); }
        public void Stop()
        {
            if (stopped) return;
            if (!TerminateJobObject(job, 0) || WaitForSingleObject(process, 5000) != 0) throw new InvalidOperationException("Created process did not exit after created-job termination.");
            stopped = true;
        }
        public void Dispose() { job.Dispose(); thread.Dispose(); process.Dispose(); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits
    { public long ProcessTime, JobTime; public uint Flags; public nuint MinWorkingSet, MaxWorkingSet; public uint ActiveProcessLimit; public nuint Affinity; public uint Priority, Scheduling; }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct JobLimits
    { public BasicLimits Basic; public IoCounters Io; public nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfo
    { public uint Size; public nint Reserved, Desktop, Title; public uint X, Y, Width, Height, XCount, YCount, Fill, Flags; public ushort Show, ReservedBytes; public nint ReservedData, Input, Output, Error; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public nint Process, Thread; public uint Pid, Tid; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)] private static extern nint CreateJobObjectW(nint security, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(KernelHandle job, int type, ref JobLimits limits, uint length);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)] private static extern bool CreateProcessW(string application, string? command, nint processSecurity, nint threadSecurity, bool inherit, uint flags, nint environment, string? directory, ref StartupInfo startup, out ProcessInfo info);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(KernelHandle job, KernelHandle process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(KernelHandle thread);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateJobObject(KernelHandle job, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateProcess(KernelHandle process, uint exitCode);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(KernelHandle handle, uint milliseconds);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
}
