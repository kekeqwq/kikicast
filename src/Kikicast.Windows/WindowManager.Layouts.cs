using System.Diagnostics;
using System.Runtime.InteropServices;
using Kikicast.Core;

namespace Kikicast.Windows;

public sealed partial class WindowManager
{
    public sealed record LayoutResult(Guid EntryId, bool Placed, string? Error);
    public async Task<IReadOnlyList<LayoutResult>> ApplyLayoutAsync(WindowLayoutPlan plan, IReadOnlyList<LayoutScreen> expectedScreens,
        IReadOnlyDictionary<long, ForegroundTarget> targets, CancellationToken cancellationToken = default)
    {
        var placements = plan.Placements.ToArray(); var screens = expectedScreens.ToArray();
        if (placements.Length > WindowLayout.MaximumEntries || screens.Length > WindowCycle.MaximumDisplays || targets.Count > LayoutWindowInventory.MaximumWindows
            || placements.Any(x => !x.Frame.IsValid || !x.Canvas.IsValid || !Enum.IsDefined(x.Anchor) || !LayoutDisplay.ValidIdentity(x.DisplayIdentity))
            || screens.Any(x => x.Display.Validate() != null || !x.Area.WorkArea.IsValid)
            || screens.Select(x => x.Display.Identity).Distinct(StringComparer.OrdinalIgnoreCase).Count() != screens.Length
            || placements.Any(x => !screens.Any(s => s.Display.Identity.Equals(x.DisplayIdentity, StringComparison.OrdinalIgnoreCase)
                && x.Canvas.X >= s.Area.WorkArea.X - 1 && x.Canvas.Y >= s.Area.WorkArea.Y - 1
                && x.Canvas.Right <= s.Area.WorkArea.Right + 1 && x.Canvas.Bottom <= s.Area.WorkArea.Bottom + 1)
                || x.Frame.X < x.Canvas.X - 1 || x.Frame.Y < x.Canvas.Y - 1 || x.Frame.Right > x.Canvas.Right + 1 || x.Frame.Bottom > x.Canvas.Bottom + 1)
            || placements.Select(x => x.Handle).Distinct().Count() != placements.Length)
            return placements.Select(x => new LayoutResult(x.EntryId, false, "Invalid layout placement snapshot.")).ToArray();
        var identities = targets.ToDictionary();
        await gate.WaitAsync(CancellationToken.None);
        try
        {
            return await Task.Run<IReadOnlyList<LayoutResult>>(() =>
            {
                if (!OperatingSystem.IsWindows()) return placements.Select(x => new LayoutResult(x.EntryId, false, "Window layouts require Windows.")).ToArray();
                var outcomes = new List<LayoutResult>(); var elapsed = Stopwatch.StartNew();
                foreach (var placement in placements)
                {
                    if (cancellationToken.IsCancellationRequested || elapsed.Elapsed > TimeSpan.FromSeconds(10))
                    { outcomes.Add(new(placement.EntryId, false, "Cancelled or layout's 10-second placement budget exhausted.")); continue; }
                    // Revalidate stable identity/topology before each write; never
                    // put a disconnected target onto an arbitrary fallback display.
                    var current = LayoutDisplays.Read();
                    if (current.Count != screens.Length || !screens.All(x => current.Any(y => y.Display.Identity.Equals(x.Display.Identity, StringComparison.OrdinalIgnoreCase) && y.Area == x.Area)))
                    { outcomes.Add(new(placement.EntryId, false, "Display topology changed; recapture or refresh before applying.")); continue; }
                    if (!identities.TryGetValue(placement.Handle, out var target) || target.Handle.ToInt64() != placement.Handle || cancellationToken.IsCancellationRequested)
                    { outcomes.Add(new(placement.EntryId, false, "Target unavailable or operation cancelled.")); continue; }
                    var error = PlaceLayoutWindow(target, placement);
                    outcomes.Add(new(placement.EntryId, error == null, error));
                }
                return outcomes;
            });
        }
        finally { gate.Release(); }
    }
    // Deliberately does not read/write memories, usage, tile or cycle state.
    private static string? PlaceLayoutWindow(ForegroundTarget target, LayoutPlacement placement)
    {
        if (!target.IsValid() || !IsWindowVisible(target.Handle) || IsIconic(target.Handle)) return "Window closed, hidden, minimized or inaccessible.";
        var hwnd = target.Handle; var style = GetWindowLong(hwnd, -16);
        if ((style & 0x40000000) != 0) return "Child windows are not supported.";
        var original = new Placement { Length = (uint)Marshal.SizeOf<Placement>() };
        if (!GetWindowPlacement(hwnd, ref original)) return "Window state unavailable.";
        string Fail(string message)
        { if (target.IsValid()) SetWindowPlacement(hwnd, ref original); return message; }
        if (IsZoomed(hwnd))
        {
            if (!target.IsValid() || !ShowWindowAsync(hwnd, 9)) return Fail("Window could not leave maximized state.");
            for (var i = 0; i < 10 && IsZoomed(hwnd); i++) Thread.Sleep(20);
            if (IsZoomed(hwnd)) return Fail("Window did not leave maximized state.");
        }
        var before = ReadFrame(hwnd);
        if (before == null) return Fail("Window frame unavailable.");
        var resizable = (style & 0x00040000) != 0;
        var frame = resizable ? placement.Frame : WindowLayoutGeometry.Reanchor(placement.Frame, before.Value.Width, before.Value.Height, placement.Anchor, placement.Canvas);
        if (!target.IsValid() || !WriteFrame(hwnd, frame, resizable)) return Fail("Window refused placement, possibly due to application permissions.");
        var observed = Observe(hwnd, frame);
        if (observed is { } actual && resizable && Drift(actual, frame))
        {
            var corrected = WindowLayoutGeometry.Reanchor(placement.Frame, Math.Max(frame.Width, actual.Width), Math.Max(frame.Height, actual.Height), placement.Anchor, placement.Canvas);
            if (target.IsValid()) WriteFrame(hwnd, corrected, true);
            observed = Observe(hwnd, corrected); frame = corrected;
        }
        if (!target.IsValid() || observed == null) return "Window closed or its applied frame could not be read.";
        return Drift(observed.Value, frame) ? Fail("Window did not apply the requested position/size.") : null;
    }
}
