namespace Kikicast.Core;

// Append only: explicit modes are additive schema-1 settings. Legacy bool-only
// preferences still resolve to Off/Sizes without rewriting the original file.
public enum WindowCycleMode { Off, Sizes, Displays }
public sealed record WindowCycleState(WindowAction Action, WindowCycleMode Mode, int Step, string OriginDisplay,
    string AppliedDisplay, IReadOnlyList<DisplayArea> Screens);
public sealed record WindowCycleDecision(WindowCycleMode Mode, int Step, string OriginDisplay);
public sealed record WindowHalfSlot(DisplayArea Display, WindowAction Edge);

// Adapted from Tinycast WindowCycle/WindowActionMemory/WindowPlacementEngine;
// see NOTICE.md. Only observed, unchanged windows can continue a chain.
public static class WindowCycle
{
    public const int MaximumDisplays = 64;
    public static bool IsHalf(WindowAction action) => action is WindowAction.LeftHalf or WindowAction.RightHalf
        or WindowAction.TopHalf or WindowAction.BottomHalf;
    public static WindowCycleMode Resolve(AppPreferences p) => p.HalfCycleMode ?? (p.CycleHalfSizes ? WindowCycleMode.Sizes : WindowCycleMode.Off);
    public static int Length(WindowCycleMode mode, WindowAction action, int displayCount) => !IsHalf(action) ? 1
        : mode == WindowCycleMode.Sizes ? 3 : mode == WindowCycleMode.Displays && displayCount is > 1 and <= MaximumDisplays ? displayCount * 2 : 1;

    // One shared ordering, using physical monitor bounds when available so a
    // side taskbar cannot reverse display order. IDs are session-only tie-breaks.
    public static IReadOnlyList<DisplayArea>? Ordered(IReadOnlyList<DisplayArea> screens)
    {
        if (screens.Count is < 1 or > MaximumDisplays || screens.Any(x => x == null || string.IsNullOrEmpty(x.Id)
            || !x.WorkArea.IsValid || x.Bounds is { IsValid: false })
            || screens.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != screens.Count) return null;
        return screens.OrderBy(x => (x.Bounds ?? x.WorkArea).X).ThenBy(x => (x.Bounds ?? x.WorkArea).Y)
            .ThenBy(x => x.Id, StringComparer.Ordinal).ToArray();
    }
    public static WindowCycleDecision Decide(WindowCycleMode mode, WindowAction action, WindowCycleState? previous,
        DisplayArea host, IReadOnlyList<DisplayArea> screens, bool frameUnchanged)
    {
        var ordered = Ordered(screens);
        var length = ordered == null ? 1 : Length(mode, action, ordered.Count);
        var continues = length > 1 && frameUnchanged && previous != null && ordered != null && mode == previous.Mode && action == previous.Action
            && host.Id == previous.AppliedDisplay && ordered.Any(x => x.Id == previous.OriginDisplay)
            && ordered.SequenceEqual(previous.Screens);
        return new(mode, continues ? (Wrap(previous!.Step, length) + 1) % length : 0,
            continues ? previous!.OriginDisplay : host.Id);
    }
    public static WindowCycleState Remember(WindowAction action, WindowCycleDecision decision, string appliedDisplay, IReadOnlyList<DisplayArea> screens)
    {
        var ordered = Ordered(screens) ?? throw new ArgumentException("Invalid display snapshot.");
        return new(action, decision.Mode, decision.Step, decision.OriginDisplay, appliedDisplay, Array.AsReadOnly(ordered.ToArray()));
    }
    public static WindowHalfSlot? HalfSlot(WindowAction action, DisplayArea host, IReadOnlyList<DisplayArea> screens, int step, string? originDisplay)
    {
        if (!IsHalf(action)) return null;
        var strip = Ordered(screens);
        if (strip == null || !strip.Any(x => x.Id == host.Id)) return null;
        if (strip.Count == 1) return new(host, action); // no in-place opposite-edge flip
        var originIndex = -1; var hostIndex = -1;
        for (var i = 0; i < strip.Count; i++) { if (strip[i].Id == originDisplay) originIndex = i; if (strip[i].Id == host.Id) hostIndex = i; }
        if (originIndex < 0) originIndex = hostIndex;
        var leading = action is WindowAction.LeftHalf or WindowAction.TopHalf;
        var slot = Wrap(originIndex * 2L + (leading ? 0 : 1) + (leading ? -(long)step : step), strip.Count * 2);
        var edge = action is WindowAction.LeftHalf or WindowAction.RightHalf
            ? slot % 2 == 0 ? WindowAction.LeftHalf : WindowAction.RightHalf
            : slot % 2 == 0 ? WindowAction.TopHalf : WindowAction.BottomHalf;
        return new(strip[slot / 2], edge);
    }
    public static int Wrap(long step, int length) => length <= 1 ? 0 : (int)((step % length + length) % length);

    // Compatibility helper for existing size-cycle callers/tests. Production uses
    // Decide, which additionally resets when mode or display topology changes.
    public static int NextStep(bool enabled, WindowAction action, WindowAction? previousAction,
        int previousStep, string display, string? previousDisplay, bool frameUnchanged)
        => enabled && IsHalf(action) && action == previousAction && display == previousDisplay && frameUnchanged
            ? (Math.Max(0, previousStep) % 3 + 1) % 3 : 0;
    public static double Fraction(int step) => (((step % 3) + 3) % 3) switch { 1 => 1d / 3, 2 => 2d / 3, _ => .5 };
}
