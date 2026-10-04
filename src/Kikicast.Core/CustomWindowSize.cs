using System.Globalization;
using System.Text.Json.Serialization;

namespace Kikicast.Core;

public enum WindowSizeUnit { Dip, Percent }
// Append only: serialized values form the 3×3 anchor grid.
public enum WindowSizeAnchor { TopLeft, Top, TopRight, Left, Center, Right, BottomLeft, Bottom, BottomRight }
public sealed record WindowSizeDimension(int Value = 60, WindowSizeUnit Unit = WindowSizeUnit.Percent)
{
    [JsonIgnore] public int Maximum => Unit == WindowSizeUnit.Percent ? 100 : 16000;
    public WindowSizeDimension Clamped() => this with { Value = Math.Clamp(Value, 1, Maximum) };
    public double Length(double available, double scale) => Math.Min(available, Math.Max(1,
        Unit == WindowSizeUnit.Percent ? available * Value / 100d : Value * scale));
    public WindowSizeDimension Converted(WindowSizeUnit unit, double availableDip)
    {
        if (!Enum.IsDefined(unit) || !double.IsFinite(availableDip) || availableDip <= 0) throw new ArgumentException("Invalid dimension conversion.");
        var requested = unit == Unit ? Value : unit == WindowSizeUnit.Percent ? Value / availableDip * 100 : Value * availableDip / 100;
        return new WindowSizeDimension((int)Math.Clamp(Math.Round(requested), 1, unit == WindowSizeUnit.Percent ? 100 : 16000), unit);
    }
    [JsonIgnore] public string Label => Value.ToString(CultureInfo.InvariantCulture) + (Unit == WindowSizeUnit.Percent ? "%" : " DIP");
}

public sealed record CustomWindowSize(Guid Id, string Name)
{
    public const int MaximumSizes = 128;
    public WindowSizeDimension Width { get; init; } = new();
    public WindowSizeDimension Height { get; init; } = new();
    public WindowSizeAnchor Anchor { get; init; } = WindowSizeAnchor.Center;
    public int OffsetX { get; init; }
    public int OffsetY { get; init; }
    public bool Enabled { get; init; } = true;
    [JsonIgnore] public string EntryId => "window-size:" + Id.ToString("D");
    [JsonIgnore] public string Summary => $"{Width.Label} × {Height.Label} · {AnchorLabel(Anchor)} · Offset {OffsetX}, {OffsetY} DIP";
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CustomWindowSize, LauncherSearchProfile> searchCache = new();
    [JsonIgnore] public LauncherSearchProfile SearchFields => searchCache.GetValue(this, x => LauncherSearchProfile.Create(x.Name, x.Summary, keywords: ["custom window size"]));
    public static string AnchorLabel(WindowSizeAnchor anchor) => anchor switch
    { WindowSizeAnchor.TopLeft => "Top left", WindowSizeAnchor.TopRight => "Top right", WindowSizeAnchor.BottomLeft => "Bottom left", WindowSizeAnchor.BottomRight => "Bottom right", _ => anchor.ToString() };
    public static bool TryId(string? entryId, out Guid id)
    {
        id = Guid.Empty;
        return entryId?.StartsWith("window-size:", StringComparison.OrdinalIgnoreCase) == true
            && Guid.TryParseExact(entryId[12..], "D", out id) && id != Guid.Empty;
    }
    public static CustomWindowSize? Runnable(AppPreferences p, Guid id) => p.WindowManagementEnabled
        ? p.CustomWindowSizes.FirstOrDefault(x => x.Id == id && x.Enabled) : null;
    public string? Validate()
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Length > 120 || Name.Any(char.IsControl)) return "Enter a custom size name of 1–120 characters without controls.";
        if (Width == null || Height == null || !Enum.IsDefined(Width.Unit) || !Enum.IsDefined(Height.Unit)
            || Width.Value < 1 || Width.Value > Width.Maximum || Height.Value < 1 || Height.Value > Height.Maximum)
            return "Size dimensions must be 1–16000 DIP or 1–100%.";
        return !Enum.IsDefined(Anchor) || OffsetX is < -4000 or > 4000 || OffsetY is < -4000 or > 4000
            ? "Choose a nine-grid anchor and offsets within ±4000 DIP." : null;
    }
    public static string? ValidateList(List<CustomWindowSize>? sizes)
    {
        if (sizes == null || sizes.Count > MaximumSizes || sizes.Any(x => x == null)) return "Choose at most 128 custom window sizes.";
        foreach (var size in sizes) if (size.Validate() is { } error) return error;
        return sizes.Select(x => x.Id).Distinct().Count() != sizes.Count
            || sizes.Select(x => x.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != sizes.Count
            ? "Custom sizes require distinct IDs and names." : null;
    }
    // Settings item edits are immediate; the overall size/shortcut draft must not
    // overwrite those edits. Explicit deletion alone removes a size's references.
    public static AppPreferences MergeReferences(AppPreferences current, AppPreferences draft)
    {
        var merged = draft with { FavoriteKeys = current.FavoriteKeys, LauncherAliases = current.LauncherAliases, HiddenEntryKeys = current.HiddenEntryKeys };
        var ids = draft.CustomWindowSizes.Select(x => x.Id).ToHashSet();
        foreach (var removed in current.CustomWindowSizes.Where(x => !ids.Contains(x.Id)))
            merged = LauncherCustomization.RemoveReferences(merged, removed.EntryId);
        return merged;
    }
    public Rect? Frame(DisplayArea host, double gapDip = 0)
    {
        if (Validate() != null || !host.WorkArea.IsValid) return null;
        var scale = Scale(host);
        var canvas = WindowGeometry.Canvas(host.WorkArea, gapDip * scale);
        return WindowGeometry.Round(AnchorFrame(canvas, Width.Length(canvas.Width, scale), Height.Length(canvas.Height, scale), scale));
    }
    // Also used for fixed/minimum-size windows: preserve actual size and anchor it,
    // pinning the leading edge if the application cannot fit the work area.
    public Rect Reanchor(DisplayArea host, double gapDip, double width, double height) => WindowGeometry.Round(
        AnchorFrame(WindowGeometry.Canvas(host.WorkArea, gapDip * Scale(host)), width, height, Scale(host)));
    private Rect AnchorFrame(Rect box, double width, double height, double scale)
    {
        var column = (int)Anchor % 3; var row = (int)Anchor / 3;
        var x = box.X + (box.Width - width) * column / 2 + OffsetX * scale;
        var y = box.Y + (box.Height - height) * row / 2 + OffsetY * scale;
        return new(Math.Clamp(x, box.X, Math.Max(box.X, box.Right - width)),
            Math.Clamp(y, box.Y, Math.Max(box.Y, box.Bottom - height)), width, height);
    }
    private static double Scale(DisplayArea host) => double.IsFinite(host.Scale) && host.Scale > 0 ? host.Scale : 1;
}
