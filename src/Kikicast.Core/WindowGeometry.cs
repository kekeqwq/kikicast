namespace Kikicast.Core;

public readonly record struct Rect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public bool IsValid => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Width) && double.IsFinite(Height)
        && Width > 0 && Height > 0;
}
public sealed record DisplayArea(string Id, Rect WorkArea, double Scale = 1, Rect? Bounds = null);
public sealed record WindowPlacementPlan(Rect Frame, DisplayArea Display, WindowAction AnchorAction,
    WindowAction? Tile, int TileStep);
public enum WindowAction
{
    LeftHalf, RightHalf, TopHalf, BottomHalf,
    TopLeft, TopRight, BottomLeft, BottomRight,
    FirstThird, CenterThird, LastThird, FirstTwoThirds, LastTwoThirds,
    Maximize, AlmostMaximize, ReasonableSize, Center, Larger, Smaller,
    NextDisplay, PreviousDisplay, Restore,
    // Append only: existing JSON numeric action IDs must not change.
    FirstThreeFourths, LastThreeFourths, MaximizeHeight, MaximizeWidth,
    CenterHalf, CenterTwoThirds, MoveLeft, MoveRight, MoveUp, MoveDown
}
public sealed record WindowCommand(WindowAction Action, string Name, string Keywords)
{
    private readonly SearchProfile nameProfile = SearchProfile.Create(Name);
    private readonly SearchProfile keywordProfile = SearchProfile.Create(Keywords);
    public int Score(string query) => Math.Max(nameProfile.Score(query), keywordProfile.Score(query));
}

public static class WindowGeometry
{
    public static IReadOnlyList<WindowCommand> Commands { get; } = new WindowCommand[]
    {
        new(WindowAction.LeftHalf, "Left half", "zuobanping left half"), new(WindowAction.RightHalf, "Right half", "youbanping right half"),
        new(WindowAction.TopHalf, "Top half", "shangbanping top half"), new(WindowAction.BottomHalf, "Bottom half", "xiabanping bottom half"),
        new(WindowAction.TopLeft, "Top left", "zuoshang quarter"), new(WindowAction.TopRight, "Top right", "youshang quarter"),
        new(WindowAction.BottomLeft, "Bottom left", "zuoxia quarter"), new(WindowAction.BottomRight, "Bottom right", "youxia quarter"),
        new(WindowAction.FirstThird, "Left third", "zuoce first third"), new(WindowAction.CenterThird, "Center third", "zhongjian center third"),
        new(WindowAction.LastThird, "Right third", "youce last third"), new(WindowAction.FirstTwoThirds, "Left two thirds", "zuoce two thirds"),
        new(WindowAction.LastTwoThirds, "Right two thirds", "youce two thirds"),
        new(WindowAction.Maximize, "Fill work area", "tianman maximize"), new(WindowAction.AlmostMaximize, "Almost maximize", "jiejin almost maximize"),
        new(WindowAction.ReasonableSize, "Reasonable size", "heli reasonable size"), new(WindowAction.Center, "Center window", "juzhong chuangkoujuzhong center window"),
        new(WindowAction.Larger, "Make larger", "fangda make larger"), new(WindowAction.Smaller, "Make smaller", "suoxiao make smaller"),
        new(WindowAction.NextDisplay, "Next display", "xiayi next display monitor"), new(WindowAction.PreviousDisplay, "Previous display", "shangyi previous display monitor"),
        new(WindowAction.Restore, "Restore window", "huifu restore window"),
        new(WindowAction.FirstThreeFourths, "Left three fourths", "zuoce three quarters width"),
        new(WindowAction.LastThreeFourths, "Right three fourths", "youce three quarters width"),
        new(WindowAction.MaximizeHeight, "Maximize height", "chuizhi maximize height"),
        new(WindowAction.MaximizeWidth, "Maximize width", "shuiping maximize width"),
        new(WindowAction.CenterHalf, "Center half", "juzhong center half"),
        new(WindowAction.CenterTwoThirds, "Center two thirds", "juzhong center two thirds"),
        new(WindowAction.MoveLeft, "Move left", "zuoyi nudge left"), new(WindowAction.MoveRight, "Move right", "youyi nudge right"),
        new(WindowAction.MoveUp, "Move up", "shangyi nudge up"), new(WindowAction.MoveDown, "Move down", "xiayi nudge down")
    };

    public static bool IsNudge(WindowAction action) => action is WindowAction.MoveLeft or WindowAction.MoveRight or WindowAction.MoveUp or WindowAction.MoveDown;
    public static bool IsTile(WindowAction action) => (int)action is >= 0 and <= (int)WindowAction.LastTwoThirds
        || action is WindowAction.FirstThreeFourths or WindowAction.LastThreeFourths or WindowAction.CenterHalf or WindowAction.CenterTwoThirds;
    public static string Group(WindowAction action) => action is WindowAction.NextDisplay or WindowAction.PreviousDisplay ? "Displays"
        : IsNudge(action) ? "Moving" : IsTile(action) ? "Tiling" : "Size and restore";

    public static Rect? Place(WindowAction action, Rect window, DisplayArea host, IReadOnlyList<DisplayArea> screens, double gap = 0, int step = 0)
    {
        if (!window.IsValid || !host.WorkArea.IsValid || action == WindowAction.Restore) return null;
        var box = Canvas(host.WorkArea, gap);
        var scale = double.IsFinite(host.Scale) && host.Scale > 0 ? host.Scale : 1;
        var fraction = WindowCycle.Fraction(step);
        (double l, double t, double r, double b)? tile = action switch
        {
            WindowAction.LeftHalf => (0, 0, fraction, 1), WindowAction.RightHalf => (1 - fraction, 0, 1, 1),
            WindowAction.TopHalf => (0, 0, 1, fraction), WindowAction.BottomHalf => (0, 1 - fraction, 1, 1),
            WindowAction.TopLeft => (0, 0, .5, .5), WindowAction.TopRight => (.5, 0, 1, .5),
            WindowAction.BottomLeft => (0, .5, .5, 1), WindowAction.BottomRight => (.5, .5, 1, 1),
            WindowAction.FirstThird => (0, 0, 1d / 3, 1), WindowAction.CenterThird => (1d / 3, 0, 2d / 3, 1),
            WindowAction.LastThird => (2d / 3, 0, 1, 1), WindowAction.FirstTwoThirds => (0, 0, 2d / 3, 1),
            WindowAction.LastTwoThirds => (1d / 3, 0, 1, 1),
            WindowAction.FirstThreeFourths => (0, 0, .75, 1), WindowAction.LastThreeFourths => (.25, 0, 1, 1),
            WindowAction.CenterHalf => (.25, 0, .75, 1), WindowAction.CenterTwoThirds => (1d / 6, 0, 5d / 6, 1), _ => null
        };
        if (tile is { } f)
        {
            var g = SafeGap(host.WorkArea, gap);
            var area = host.WorkArea;
            var l = area.X + area.Width * f.l + (f.l == 0 ? g : g / 2);
            var t = area.Y + area.Height * f.t + (f.t == 0 ? g : g / 2);
            var r = area.X + area.Width * f.r - (f.r == 1 ? g : g / 2);
            var b = area.Y + area.Height * f.b - (f.b == 1 ? g : g / 2);
            return Round(new(l, t, Math.Max(1, r - l), Math.Max(1, b - t)));
        }
        if (action is WindowAction.NextDisplay or WindowAction.PreviousDisplay)
        {
            return Resolve(action, window, host, screens, gap / scale)?.Frame ?? window;
        }
        Rect result;
        switch (action)
        {
            case WindowAction.Maximize: result = box; break;
            case WindowAction.AlmostMaximize: result = Center(box.Width * .9, box.Height * .9, box); break;
            case WindowAction.ReasonableSize: result = Center(Math.Min(box.Width * .6, 1025 * scale), Math.Min(box.Height * .6, 900 * scale), box); break;
            case WindowAction.Center: result = Center(window.Width, window.Height, box); break;
            case WindowAction.MaximizeHeight: result = new(window.X, box.Y, window.Width, box.Height); break;
            case WindowAction.MaximizeWidth: result = new(box.X, window.Y, box.Width, window.Height); break;
            case WindowAction.MoveLeft: result = window with { X = window.X - Math.Round(box.Width * .05) }; break;
            case WindowAction.MoveRight: result = window with { X = window.X + Math.Round(box.Width * .05) }; break;
            case WindowAction.MoveUp: result = window with { Y = window.Y - Math.Round(box.Height * .05) }; break;
            case WindowAction.MoveDown: result = window with { Y = window.Y + Math.Round(box.Height * .05) }; break;
            case WindowAction.Larger:
            case WindowAction.Smaller:
                var direction = action == WindowAction.Larger ? 1 : -1;
                var dw = Math.Max(2, Math.Round(box.Width * .05 / 2) * 2) * direction;
                var dh = Math.Max(2, Math.Round(box.Height * .05 / 2) * 2) * direction;
                var width = Math.Clamp(window.Width + dw, Math.Min(box.Width, Math.Max(200 * scale, box.Width * .15)), box.Width);
                var height = Math.Clamp(window.Height + dh, Math.Min(box.Height, Math.Max(150 * scale, box.Height * .15)), box.Height);
                result = new(window.X - (width - window.Width) / 2, window.Y - (height - window.Height) / 2, width, height);
                break;
            default: return null;
        }
        return Round(Clamp(result, box));
    }

    // Resolve display identity and actual tile edge explicitly; never infer them
    // from the requested frame's centre (work areas can be offset/overlap).
    public static WindowPlacementPlan? Resolve(WindowAction action, Rect window, DisplayArea host,
        IReadOnlyList<DisplayArea> screens, double gapDip = 0, WindowCycleDecision? cycle = null,
        WindowAction? lastTile = null, int lastTileStep = 0)
    {
        var ordered = WindowCycle.Ordered(screens);
        if (!window.IsValid || ordered == null || ordered.FirstOrDefault(x => x.Id == host.Id) is not { } current
            || !Enum.IsDefined(action) || action == WindowAction.Restore || cycle != null && !Enum.IsDefined(cycle.Mode)) return null;
        var destination = current;
        var edge = action; var tileStep = 0;
        if (WindowCycle.IsHalf(action) && cycle?.Mode == WindowCycleMode.Displays)
        {
            var slot = WindowCycle.HalfSlot(action, current, ordered, cycle.Step, cycle.OriginDisplay);
            if (slot == null) return null;
            destination = slot.Display; edge = slot.Edge;
        }
        else if (WindowCycle.IsHalf(action) && cycle?.Mode == WindowCycleMode.Sizes) tileStep = WindowCycle.Wrap(cycle.Step, 3);
        if (action is WindowAction.NextDisplay or WindowAction.PreviousDisplay)
        {
            if (ordered.Count < 2) return null;
            var index = ordered.ToList().FindIndex(x => x.Id == current.Id);
            destination = ordered[WindowCycle.Wrap(index + (action == WindowAction.NextDisplay ? 1 : -1), ordered.Count)];
            if (lastTile is { } tile && IsTile(tile))
            {
                edge = tile; tileStep = WindowCycle.IsHalf(tile) ? WindowCycle.Wrap(lastTileStep, 3) : 0;
                var tileFrame = Place(tile, window, destination, ordered, gapDip * Scale(destination), tileStep);
                return tileFrame is { IsValid: true } placedTile ? new(placedTile, destination, edge, tile, tileStep) : null;
            }
            var box = Canvas(current.WorkArea, gapDip * Scale(current));
            var target = Canvas(destination.WorkArea, gapDip * Scale(destination));
            var scaled = Round(Clamp(new(target.X + (window.X - box.X) / box.Width * target.Width,
                target.Y + (window.Y - box.Y) / box.Height * target.Height,
                window.Width / box.Width * target.Width, window.Height / box.Height * target.Height), target));
            return scaled.IsValid ? new(scaled, destination, WindowAction.Center, null, 0) : null;
        }
        var frame = Place(edge, window, destination, ordered, gapDip * Scale(destination), tileStep);
        return frame is { IsValid: true } placed ? new(placed, destination, edge, IsTile(edge) ? edge : null, tileStep) : null;
    }
    private static double Scale(DisplayArea screen) => double.IsFinite(screen.Scale) && screen.Scale > 0 ? screen.Scale : 1;

    // Preserve the tile's anchor when a fixed/minimum-sized window cannot fit the requested slot.
    public static Rect AnchorSize(WindowAction action, Rect slot, double width, double height)
    {
        var centerBoth = action is WindowAction.Center or WindowAction.ReasonableSize or WindowAction.AlmostMaximize
            or WindowAction.Larger or WindowAction.Smaller;
        var right = action is WindowAction.RightHalf or WindowAction.TopRight or WindowAction.BottomRight
            or WindowAction.LastThird or WindowAction.LastTwoThirds or WindowAction.LastThreeFourths;
        var bottom = action is WindowAction.BottomHalf or WindowAction.BottomLeft or WindowAction.BottomRight;
        var centerX = centerBoth || action is WindowAction.CenterThird or WindowAction.CenterHalf or WindowAction.CenterTwoThirds;
        return new(right ? slot.Right - width : centerX ? slot.X + (slot.Width - width) / 2 : slot.X,
            bottom ? slot.Bottom - height : centerBoth ? slot.Y + (slot.Height - height) / 2 : slot.Y, width, height);
    }

    public static double SafeGap(Rect box, double gap) => !double.IsFinite(gap) ? 0
        : Math.Clamp(gap, 0, Math.Max(0, Math.Min(box.Width, box.Height) / 2 - 1));
    public static Rect Canvas(Rect area, double gap)
    { var g = SafeGap(area, gap); return new(area.X + g, area.Y + g, area.Width - g * 2, area.Height - g * 2); }
    public static Rect Clamp(Rect frame, Rect box)
    {
        var w = Math.Min(frame.Width, box.Width); var h = Math.Min(frame.Height, box.Height);
        return new(Math.Clamp(frame.X, box.X, box.Right - w), Math.Clamp(frame.Y, box.Y, box.Bottom - h), w, h);
    }
    private static Rect Center(double width, double height, Rect box) => new(box.X + (box.Width - width) / 2, box.Y + (box.Height - height) / 2, width, height);
    public static Rect Round(Rect frame)
    {
        var l = Math.Round(frame.X); var t = Math.Round(frame.Y);
        return new(l, t, Math.Max(1, Math.Round(frame.Right) - l), Math.Max(1, Math.Round(frame.Bottom) - t));
    }
}
