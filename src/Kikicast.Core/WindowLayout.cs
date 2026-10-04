using System.Text.Json.Serialization;

namespace Kikicast.Core;

public sealed record LayoutDisplay(string Identity, string Name)
{
    public static bool ValidIdentity(string? id) => id is { Length: > 20 and <= 512 }
        && id.StartsWith("monitor:\\\\?\\DISPLAY#", StringComparison.OrdinalIgnoreCase) && !id.Any(char.IsControl);
    public string? Validate() => !ValidIdentity(Identity) || string.IsNullOrWhiteSpace(Name) || Name.Length > 120 || Name.Any(char.IsControl)
        ? "Choose a uniquely identified local display." : null;
}
public sealed record LayoutScreen(LayoutDisplay Display, DisplayArea Area);
public sealed record WindowLayoutEntry(Guid Id, string ApplicationId, LayoutDisplay Display)
{
    public WindowLayoutInput? Input { get; init; }
    public double WidthFraction { get; init; } = .5;
    public double HeightFraction { get; init; } = 1;
    public WindowSizeAnchor Anchor { get; init; } = WindowSizeAnchor.Center;
    public double OffsetX { get; init; }
    public double OffsetY { get; init; }
    public string? Validate()
    {
        if (Id == Guid.Empty || !WindowLayout.ValidApplicationId(ApplicationId)) return "Choose a current desktop or packaged application for each layout entry.";
        if (Display == null || Display.Validate() is { }) return "Invalid layout display identity.";
        if ((Input?.Validate() ?? Input?.ValidateApplication(ApplicationId)) is { } inputError) return inputError;
        return !double.IsFinite(WidthFraction) || WidthFraction is < 0 or > 1 || !double.IsFinite(HeightFraction) || HeightFraction is < 0 or > 1
            || !Enum.IsDefined(Anchor) || !double.IsFinite(OffsetX) || Math.Abs(OffsetX) > 10000 || !double.IsFinite(OffsetY) || Math.Abs(OffsetY) > 10000
            ? "Layout dimensions must be 0–100%, with finite offsets within ±10000 DIP and a nine-grid anchor." : null;
    }
}
public sealed record WindowLayout(Guid Id, string Name)
{
    public const int MaximumLayouts = 64, MaximumEntries = 32, MaximumTotalEntries = 512;
    public bool Enabled { get; init; } = true;
    public bool UsesPreferredGap { get; init; } = true;
    // Additive schema-1 opt-in; legacy definitions never begin launching on load.
    public bool LaunchMissingApplications { get; init; }
    public List<WindowLayoutEntry> Entries { get; init; } = [];
    public Guid? FrontmostEntryId { get; init; }
    [JsonIgnore] public string EntryId => "window-layout:" + Id.ToString("D");
    [JsonIgnore] public string Summary => $"{Entries.Count} windows · {Entries.Select(x => x.Display.Identity).Distinct(StringComparer.OrdinalIgnoreCase).Count()} displays · {(LaunchMissingApplications ? "Application opening allowed" : "Application opening off")}";
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<WindowLayout, LauncherSearchProfile> cache = new();
    [JsonIgnore] public LauncherSearchProfile SearchFields => cache.GetValue(this, x => LauncherSearchProfile.Create(x.Name, x.Summary, keywords: ["window layout arrangement"]));
    public static bool TryId(string? value, out Guid id)
    {
        id = Guid.Empty;
        return value?.StartsWith("window-layout:", StringComparison.OrdinalIgnoreCase) == true && Guid.TryParseExact(value[14..], "D", out id) && id != Guid.Empty;
    }
    public static bool ValidApplicationId(string? id) => id != null && LauncherBindings.CanBind(id)
        && (id.StartsWith("app:", StringComparison.OrdinalIgnoreCase) || id.StartsWith("packaged:", StringComparison.OrdinalIgnoreCase));
    public string? Validate()
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Length > 120 || Name.Any(char.IsControl)) return "Enter a layout name of 1–120 characters without controls.";
        if (Entries == null || Entries.Count is < 1 or > MaximumEntries || Entries.Any(x => x == null)) return "A layout needs 1–32 application entries.";
        foreach (var entry in Entries) if (entry.Validate() is { } error) return error;
        return Entries.Select(x => x.Id).Distinct().Count() != Entries.Count || FrontmostEntryId is { } front && !Entries.Any(x => x.Id == front)
            ? "Layout entry IDs must be distinct, with at most one valid Bring to front mark." : null;
    }
    public static string? ValidateList(List<WindowLayout>? layouts)
    {
        if (layouts == null || layouts.Count > MaximumLayouts || layouts.Any(x => x == null)) return "Choose at most 64 layouts.";
        foreach (var layout in layouts) if (layout.Validate() is { } error) return error;
        return layouts.Sum(x => x.Entries.Count) > MaximumTotalEntries || layouts.Select(x => x.Id).Distinct().Count() != layouts.Count
            || layouts.Select(x => x.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != layouts.Count
            ? "Layouts require distinct IDs/names and at most 512 total entries." : null;
    }
    public static WindowLayout? Runnable(AppPreferences p, Guid id) => p.WindowManagementEnabled && p.ApplicationsEnabled
        ? p.WindowLayouts.FirstOrDefault(x => x.Id == id && x.Enabled) : null;
    public WindowLayout Duplicate(string name)
    {
        var copy = Entries.Select(x => x with { Id = Guid.NewGuid(), Input = x.Input?.Copy() }).ToList();
        var index = FrontmostEntryId == null ? -1 : Entries.FindIndex(x => x.Id == FrontmostEntryId);
        return this with { Id = Guid.NewGuid(), Name = name, Entries = copy, FrontmostEntryId = index < 0 ? null : copy[index].Id };
    }
    public static AppPreferences MergeReferences(AppPreferences current, AppPreferences draft)
    {
        var merged = CustomWindowSize.MergeReferences(current, draft);
        var ids = draft.WindowLayouts.Select(x => x.Id).ToHashSet();
        foreach (var removed in current.WindowLayouts.Where(x => !ids.Contains(x.Id))) merged = LauncherCustomization.RemoveReferences(merged, removed.EntryId);
        return merged;
    }
}

// No title, contents, command line or transient native handle is persisted.
public sealed record LayoutApplication(string Id, string MatchKey);
public sealed record LayoutWindow(long Handle, string MatchKey, Rect Frame);
public sealed record LayoutPlacement(Guid EntryId, long Handle, Rect Frame, Rect Canvas, WindowSizeAnchor Anchor, string DisplayIdentity);
public sealed record LayoutSkip(Guid EntryId, string Reason);
public sealed record LayoutOpen(Guid EntryId, string ApplicationId, string MatchKey, Rect Frame, Rect Canvas, WindowSizeAnchor Anchor, string DisplayIdentity)
{
    public WindowLayoutInput? Input { get; init; }
    [JsonIgnore] public string TargetKey => MatchKey.ToUpperInvariant() + "\0" + (Input?.TargetKey ?? "plain");
    public LayoutPlacement Bind(long handle) => new(EntryId, handle, Frame, Canvas, Anchor, DisplayIdentity);
}
public sealed record WindowLayoutPlan(IReadOnlyList<LayoutPlacement> Placements, IReadOnlyList<LayoutSkip> Skipped, Guid? FrontmostEntryId)
{
    public IReadOnlyList<LayoutOpen> Opens { get; init; } = [];
    public static WindowLayoutPlan Make(WindowLayout layout, IReadOnlyList<LayoutScreen> screens, IReadOnlyList<LayoutApplication> applications,
        IReadOnlyList<LayoutWindow> windows, double preferredGap)
    {
        var placed = new List<LayoutPlacement>(); var skipped = new List<LayoutSkip>(); var opens = new List<LayoutOpen>();
        var launching = new HashSet<string>(StringComparer.Ordinal);
        if (!layout.Enabled || layout.Validate() != null || screens.Count > WindowCycle.MaximumDisplays || windows.Count > 512 || applications.Count > 4096)
            return new([], layout.Entries?.Where(x => x != null).Select(x => new LayoutSkip(x.Id, "Invalid or oversized layout snapshot.")).ToArray() ?? [], null);
        var displays = screens.Where(x => x.Display.Validate() == null && x.Area.WorkArea.IsValid)
            .GroupBy(x => x.Display.Identity, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() == 1)
            .ToDictionary(x => x.Key, x => x.Single(), StringComparer.OrdinalIgnoreCase);
        var apps = applications.GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() == 1)
            .ToDictionary(x => x.Key, x => x.Single(), StringComparer.OrdinalIgnoreCase);
        var available = windows.Where(x => x.Frame.IsValid && x.Handle != 0).GroupBy(x => x.Handle).Where(x => x.Count() == 1).Select(x => x.Single())
            .OrderBy(x => x.Frame.Y).ThenBy(x => x.Frame.X).ThenBy(x => x.Handle).ToList();
        foreach (var entry in layout.Entries)
        {
            if (!displays.TryGetValue(entry.Display.Identity, out var display)) { skipped.Add(new(entry.Id, "Display disconnected or ambiguous; no primary/name fallback.")); continue; }
            if (!apps.TryGetValue(entry.ApplicationId, out var app)) { skipped.Add(new(entry.Id, "Application source is disabled, deleted or unavailable.")); continue; }
            var gap = layout.UsesPreferredGap ? preferredGap : 0;
            var frame = WindowLayoutGeometry.Resolve(entry, display.Area, gap);
            if (frame == null) { skipped.Add(new(entry.Id, "Unresolvable geometry.")); continue; }
            var candidates = available.Where(x => entry.Input == null && x.MatchKey.Equals(app.MatchKey, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => Math.Abs(x.Frame.X + x.Frame.Width / 2 - frame.Value.X - frame.Value.Width / 2)
                    + Math.Abs(x.Frame.Y + x.Frame.Height / 2 - frame.Value.Y - frame.Value.Height / 2)).ToArray();
            if (candidates.Length == 0)
            {
                if (!layout.LaunchMissingApplications) skipped.Add(new(entry.Id, "Application opening is off; missing apps and saved inputs are not opened."));
                else
                {
                    var open = new LayoutOpen(entry.Id, entry.ApplicationId, app.MatchKey, frame.Value, WindowLayoutGeometry.Box(display.Area, gap), entry.Anchor, entry.Display.Identity) { Input = entry.Input?.Copy() };
                    if (!launching.Add(open.TargetKey)) skipped.Add(new(entry.Id, "Duplicate application/input target; each exact authored target opens at most once."));
                    else opens.Add(open);
                }
                continue;
            }
            var target = candidates[0]; available.Remove(target);
            placed.Add(new(entry.Id, target.Handle, frame.Value, WindowLayoutGeometry.Box(display.Area, gap), entry.Anchor, entry.Display.Identity));
        }
        return new(placed, skipped, placed.Any(x => x.EntryId == layout.FrontmostEntryId) || opens.Any(x => x.EntryId == layout.FrontmostEntryId) ? layout.FrontmostEntryId : null) { Opens = opens };
    }
}
