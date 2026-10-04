using System.Runtime.InteropServices;
using Kikicast.Core;

namespace Kikicast.Windows;

// Work runs off the UI thread. No steady-state sweep of foreign windows.
public sealed partial class WindowManager
{
    private sealed record Memory(ForegroundTarget Identity, Placement Original, Rect Applied, WindowAction? Tile,
        int TileStep, WindowCycleState? Cycle);
    private readonly Dictionary<nint, Memory> memories = [];
    private readonly List<nint> usage = [];
    private readonly SemaphoreSlim gate = new(1, 1);
    public Task<string?> ExecuteAsync(WindowAction action, ForegroundTarget? target, double gapDip = 0, bool cycleHalfSizes = false)
        => ExecuteAsync(action, target, gapDip, cycleHalfSizes ? WindowCycleMode.Sizes : WindowCycleMode.Off);

    public async Task<string?> ExecuteAsync(WindowAction action, ForegroundTarget? target, double gapDip, WindowCycleMode cycleMode)
    {
        if (!Enum.IsDefined(cycleMode)) return "Unknown half-cycle mode.";
        await gate.WaitAsync();
        try { return await Task.Run(() => Execute(action, null, target, gapDip, cycleMode)); }
        finally { gate.Release(); }
    }

    public async Task<string?> ExecuteAsync(CustomWindowSize size, ForegroundTarget? target, double gapDip = 0)
    {
        if (size.Validate() is { } error) return error;
        if (!size.Enabled) return "This custom window size is disabled.";
        await gate.WaitAsync();
        try { return await Task.Run(() => Execute(null, size, target, gapDip, WindowCycleMode.Off)); }
        finally { gate.Release(); }
    }

    private string? Execute(WindowAction? action, CustomWindowSize? size, ForegroundTarget? target, double gapDip, WindowCycleMode cycleMode)
    {
        if (target == null || !target.IsValid()) return "The original window is closed or inaccessible.";
        var hwnd = target.Handle;
        if (!IsWindowVisible(hwnd) || IsIconic(hwnd)) return "The target window is hidden or minimized.";
        var style = GetWindowLong(hwnd, -16);
        if ((style & 0x40000000) != 0) return "Child windows are not supported.";
        var before = ReadFrame(hwnd);
        if (before == null) return "The window size could not be read.";
        var original = new Placement { Length = (uint)Marshal.SizeOf<Placement>() };
        if (!GetWindowPlacement(hwnd, ref original)) return "The window state could not be read.";
        if (memories.TryGetValue(hwnd, out var memory) && (memory.Identity != target || action != WindowAction.Restore && Drift(before.Value, memory.Applied)))
        { memories.Remove(hwnd); usage.Remove(hwnd); memory = null; }
        if (action == WindowAction.Restore)
        {
            if (memory == null) return "This window has no restore point.";
            var saved = memory.Original;
            if (!target.IsValid() || !SetWindowPlacement(hwnd, ref saved)) return "The window could not be restored.";
            memories.Remove(hwnd); usage.Remove(hwnd);
            return null;
        }

        var screens = Displays();
        if (WindowCycle.Ordered(screens) == null) return "The display snapshot is unavailable or invalid.";
        var hostHandle = MonitorFromWindow(hwnd, 2);
        var host = screens.FirstOrDefault(x => x.Id == hostHandle.ToString());
        if (host == null) return "The target display could not be identified.";
        if (action is WindowAction.NextDisplay or WindowAction.PreviousDisplay && screens.Count < 2) return "Only one display is available.";
        // Restore maximized state first, but retain the pre-command placement for Restore.
        if (IsZoomed(hwnd))
        {
            ShowWindowAsync(hwnd, 9);
            for (var i = 0; i < 10 && IsZoomed(hwnd); i++) Thread.Sleep(20);
            if (IsZoomed(hwnd)) return "The window did not leave its maximized state.";
        }
        var current = ReadFrame(hwnd);
        if (current == null) return "The restored window could not be read.";
        var decision = action is { } command ? WindowCycle.Decide(cycleMode, command, memory?.Cycle, host, screens, memory != null) : null;
        var plan = action is { } geometryAction ? WindowGeometry.Resolve(geometryAction, current.Value, host, screens, gapDip, decision, memory?.Tile, memory?.TileStep ?? 0) : null;
        var desired = size != null ? size.Frame(host, gapDip) : plan?.Frame;
        if (desired == null || !desired.Value.IsValid) return "The window position could not be calculated.";
        var frame = desired.Value;
        var anchorAction = plan?.AnchorAction;
        var resizable = (style & 0x00040000) != 0 && (action == null || !WindowGeometry.IsNudge(action.Value));
        if (!resizable)
            frame = size != null ? size.Reanchor(host, gapDip, current.Value.Width, current.Value.Height)
                : WindowGeometry.AnchorSize(anchorAction!.Value, frame, current.Value.Width, current.Value.Height);
        if (!target.IsValid() || !WriteFrame(hwnd, frame, resizable))
        {
            if (target.IsValid()) SetWindowPlacement(hwnd, ref original);
            return "The window refused to move. It may be restricted by permissions or the application.";
        }
        // A cross-DPI move may change non-client margins. Correct at most once.
        var observed = Observe(hwnd, frame);
        if (observed is { } actual && resizable && Drift(actual, frame))
        {
            var corrected = size != null ? size.Reanchor(host, gapDip, Math.Max(frame.Width, actual.Width), Math.Max(frame.Height, actual.Height))
                : WindowGeometry.AnchorSize(anchorAction!.Value, frame, Math.Max(frame.Width, actual.Width), Math.Max(frame.Height, actual.Height));
            if (target.IsValid()) WriteFrame(hwnd, corrected, true);
            observed = Observe(hwnd, corrected);
        }
        if (!target.IsValid() || observed == null) return "The window closed or its state could not be read.";
        if (!Drift(observed.Value, current.Value) && Drift(frame, current.Value))
        {
            if (target.IsValid()) SetWindowPlacement(hwnd, ref original);
            return "The window did not apply the requested position or size.";
        }
        if (memories.Count >= 64 && !memories.ContainsKey(hwnd))
        { memories.Remove(usage[0]); usage.RemoveAt(0); }
        var screenId = MonitorFromWindow(hwnd, 2).ToString();
        var cycle = action is { } halfAction && WindowCycle.IsHalf(halfAction) && decision != null
            ? WindowCycle.Remember(halfAction, decision, screenId, screens) : null;
        usage.Remove(hwnd); usage.Add(hwnd);
        memories[hwnd] = new(target, memory?.Original ?? original, observed.Value, plan?.Tile, plan?.TileStep ?? 0, cycle);
        return null;
    }

    private static Rect? Observe(nint hwnd, Rect requested)
    {
        Rect? result = null;
        for (var i = 0; i < 10; i++)
        {
            Thread.Sleep(20);
            result = ReadFrame(hwnd);
            if (result == null || !Drift(result.Value, requested)) break;
        }
        return result;
    }

    private static bool Drift(Rect a, Rect b) => Math.Abs(a.X - b.X) > 2 || Math.Abs(a.Y - b.Y) > 2
        || Math.Abs(a.Width - b.Width) > 2 || Math.Abs(a.Height - b.Height) > 2;

    public static Rect? ReadFrame(nint hwnd)
    {
        if (!GetWindowRect(hwnd, out var outer)) return null;
        var visible = DwmGetWindowAttribute(hwnd, 9, out NativeRect bounds, Marshal.SizeOf<NativeRect>()) == 0 ? bounds : outer;
        var frame = new Rect(visible.Left, visible.Top, visible.Right - visible.Left, visible.Bottom - visible.Top);
        return frame.IsValid ? frame : null;
    }
    private static bool WriteFrame(nint hwnd, Rect frame, bool resize)
    {
        if (!GetWindowRect(hwnd, out var outer) || ReadFrame(hwnd) is not { } visible) return false;
        var leftMargin = visible.X - outer.Left; var topMargin = visible.Y - outer.Top;
        var widthMargin = outer.Right - outer.Left - visible.Width;
        var heightMargin = outer.Bottom - outer.Top - visible.Height;
        return SetWindowPos(hwnd, 0, (int)Math.Round(frame.X - leftMargin), (int)Math.Round(frame.Y - topMargin),
            (int)Math.Round(frame.Width + widthMargin), (int)Math.Round(frame.Height + heightMargin),
            0x0010 | 0x0004 | 0x4000 | (resize ? 0u : 0x0001)); // no activate / no z-order / async
    }

    public static DisplayArea? DisplayForWindow(nint hwnd)
    { var id = MonitorFromWindow(hwnd, 2).ToString(); return Displays().FirstOrDefault(x => x.Id == id); }

    public static IReadOnlyList<DisplayArea> Displays()
    {
        var result = new List<DisplayArea>();
        var invalid = false;
        MonitorCallback callback = (nint monitor, nint hdc, ref NativeRect bounds, nint data) =>
        {
            if (result.Count >= WindowCycle.MaximumDisplays) { invalid = true; return false; }
            var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info)) { invalid = true; return false; }
            var r = info.Work;
            var scale = GetDpiForMonitor(monitor, 0, out var x, out _) == 0 ? x / 96d : 1;
            var b = info.Bounds;
            result.Add(new(monitor.ToString(), new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top), scale,
                new(b.Left, b.Top, b.Right - b.Left, b.Bottom - b.Top)));
            return true;
        };
        var enumerated = EnumDisplayMonitors(0, 0, callback, 0);
        GC.KeepAlive(callback);
        return enumerated && !invalid ? result : [];
    }

    private delegate bool MonitorCallback(nint monitor, nint hdc, ref NativeRect bounds, nint data);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Placement
    { public uint Length, Flags, ShowCommand; public Point Min, Max; public NativeRect Normal; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public uint Size; public NativeRect Bounds, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out NativeRect rect, int size);
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(nint hwnd, ref Placement placement);
    [DllImport("user32.dll")] private static extern bool SetWindowPlacement(nint hwnd, ref Placement placement);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(nint hwnd, int command);
    [DllImport("user32.dll")] private static extern bool IsZoomed(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorCallback callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);
}
