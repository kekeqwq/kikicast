using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Kikicast.Core;
using Kikicast.Windows;
using Microsoft.Win32;
using Rect = Kikicast.Core.Rect;
using Brush = System.Windows.Media.Brush;

namespace Kikicast.App;

public partial class WindowLayoutEditor : Window
{
    private sealed record AppChoice(string Id, string Name) { public override string ToString() => Name; }
    private sealed record ScreenChoice(LayoutDisplay Display, string Name) { public override string ToString() => Name; }
    private sealed record EntryItem(WindowLayoutEntry Entry, string Label) { public override string ToString() => Label; }
    private readonly WindowLayout original;
    private readonly List<WindowLayoutEntry> entries;
    private readonly AppChoice[] apps;
    private readonly double gap;
    private readonly Func<WindowLayout, string?>? validate;
    private IReadOnlyList<LayoutScreen> screens = [];
    private Guid entryId;
    private Guid? front;
    private bool ready, filling, closed, refreshing, refreshAgain;
    public WindowLayout? Result { get; private set; }
    public WindowLayoutEditor(WindowLayout layout, IReadOnlyList<LauncherEntry> catalog, AppPreferences preferences, double draftGap, Func<WindowLayout, string?>? validate = null)
    {
        original = layout; entries = layout.Entries.ToList(); front = layout.FrontmostEntryId; gap = draftGap; this.validate = validate;
        apps = catalog.Where(x => x.IsEnabled(preferences)).Take(LayoutWindowInventory.MaximumApplications).Select(x => new AppChoice(x.Id, x.Name)).ToArray();
        InitializeComponent(); LayoutName.Text = layout.Name; LayoutEnabled.IsChecked = layout.Enabled; UseGap.IsChecked = layout.UsesPreferredGap; LaunchMissing.IsChecked = layout.LaunchMissingApplications;
        AnchorChoice.ItemsSource = Enum.GetValues<WindowSizeAnchor>().Select(CustomWindowSize.AnchorLabel).ToArray();
        InputKindChoice.ItemsSource = new[] { "None — use existing or plain missing app", "Local file/folder (EXE; package files only)", "URI (selected EXE or registered package)", "Literal argv list (direct EXE only)" };
        ready = true; RefreshEntries(entries.FirstOrDefault()?.Id);
        if (entries.Count > 0) Fill(entries[0]); else NewEntry(this, new RoutedEventArgs());
        Loaded += async (_, _) => { SystemEvents.DisplaySettingsChanged += DisplaysChanged; await ReadDisplaysAsync(); };
        Closed += (_, _) => { closed = true; SystemEvents.DisplaySettingsChanged -= DisplaysChanged; };
    }
    private async Task ReadDisplaysAsync()
    {
        if (refreshing) { refreshAgain = true; return; }
        refreshing = true; RefreshDisplaysButton.IsEnabled = false;
        try
        {
            do
            {
                refreshAgain = false;
                var next = await Task.Run(LayoutDisplays.Read);
                if (closed) return;
                var selected = (DisplayChoice.SelectedItem as ScreenChoice)?.Display;
                screens = next; RefreshScreenChoices(selected); Draw();
            } while (refreshAgain && !closed);
        }
        finally { refreshing = false; if (!closed) RefreshDisplaysButton.IsEnabled = true; }
    }
    private void DisplaysChanged(object? sender, EventArgs e)
    {
        if (closed || Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.BeginInvoke(new Action(() => { if (!closed) _ = ReadDisplaysAsync(); }));
    }
    private async void RefreshDisplays(object sender, RoutedEventArgs e) => await ReadDisplaysAsync();
    private void RefreshScreenChoices(LayoutDisplay? selected)
    {
        var known = screens.Select(x => x.Display).Concat(entries.Select(x => x.Display)).Concat(selected == null ? [] : new[] { selected })
            .GroupBy(x => x.Identity, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToArray();
        var choices = known.Select(x =>
        {
            var current = screens.FirstOrDefault(s => s.Display.Identity.Equals(x.Identity, StringComparison.OrdinalIgnoreCase));
            var bounds = current?.Area.Bounds ?? current?.Area.WorkArea;
            return new ScreenChoice(x, x.Name + (bounds is { } b ? $" · {b.Width:0}×{b.Height:0} @ ({b.X:0},{b.Y:0})" : " · missing/ambiguous (skipped)"));
        }).ToArray();
        filling = true; DisplayChoice.ItemsSource = choices;
        DisplayChoice.SelectedItem = choices.FirstOrDefault(x => x.Display.Identity.Equals(selected?.Identity, StringComparison.OrdinalIgnoreCase));
        filling = false;
    }
    private void RefreshEntries(Guid? selected = null)
    {
        filling = true;
        EntryList.ItemsSource = entries.Select(x => new EntryItem(x, ApplicationName(x.ApplicationId) + " · " + x.Display.Name + (x.Input == null ? "" : " · " + x.Input.Description) + (x.Id == front ? " · Bring to front" : ""))).ToArray();
        EntryList.SelectedItem = EntryList.Items.Cast<EntryItem>().FirstOrDefault(x => x.Entry.Id == selected);
        filling = false;
    }
    private string ApplicationName(string id) => apps.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Name ?? "Unavailable · " + id;
    private void Fill(WindowLayoutEntry entry)
    {
        filling = true; entryId = entry.Id;
        var choices = apps.Any(x => x.Id.Equals(entry.ApplicationId, StringComparison.OrdinalIgnoreCase)) ? apps : apps.Append(new(entry.ApplicationId, ApplicationName(entry.ApplicationId))).ToArray();
        ApplicationChoice.ItemsSource = choices; ApplicationChoice.SelectedItem = choices.FirstOrDefault(x => x.Id.Equals(entry.ApplicationId, StringComparison.OrdinalIgnoreCase));
        WidthPercent.Text = Format(entry.WidthFraction * 100); HeightPercent.Text = Format(entry.HeightFraction * 100);
        OffsetX.Text = Format(entry.OffsetX); OffsetY.Text = Format(entry.OffsetY); AnchorChoice.SelectedIndex = (int)entry.Anchor; BringToFront.IsChecked = entry.Id == front;
        FillInput(entry.Input);
        filling = false; UpdateInputHint(); RefreshScreenChoices(entry.Display); Draw();
    }
    private static string Format(double value) => value.ToString("0.##############", CultureInfo.CurrentCulture);
    private void SelectEntry(object sender, SelectionChangedEventArgs e)
    { if (ready && !filling && EntryList.SelectedItem is EntryItem item) Fill(item.Entry); }
    private void NewEntry(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        filling = true; EntryList.SelectedIndex = -1; entryId = Guid.NewGuid();
        ApplicationChoice.ItemsSource = apps; ApplicationChoice.SelectedIndex = -1;
        WidthPercent.Text = "50"; HeightPercent.Text = "100"; OffsetX.Text = "0"; OffsetY.Text = "0"; AnchorChoice.SelectedIndex = (int)WindowSizeAnchor.Center; BringToFront.IsChecked = false;
        FillInput(null);
        filling = false; UpdateInputHint(); RefreshScreenChoices(null); Draw();
    }
    private WindowLayoutEntry? ReadEntry()
    {
        if (ApplicationChoice.SelectedItem is not AppChoice app || DisplayChoice.SelectedItem is not ScreenChoice screen
            || !Number(WidthPercent.Text, out var width) || !Number(HeightPercent.Text, out var height)
            || !Number(OffsetX.Text, out var x) || !Number(OffsetY.Text, out var y) || AnchorChoice.SelectedIndex < 0 || !ReadInput(out var input)) return null;
        return new(entryId, app.Id, screen.Display) { WidthFraction = Math.Clamp(width, 0, 100) / 100, HeightFraction = Math.Clamp(height, 0, 100) / 100,
            Anchor = (WindowSizeAnchor)AnchorChoice.SelectedIndex, OffsetX = Math.Clamp(x, -10000, 10000), OffsetY = Math.Clamp(y, -10000, 10000), Input = input };
    }
    private static bool Number(string value, out double number) => double.TryParse(value, out number) && double.IsFinite(number);
    private void KeepEntry(object sender, RoutedEventArgs e)
    {
        var entry = ReadEntry();
        if (entry == null || entry.Validate() is { }) { Feedback.Text = entry?.Validate() ?? "Choose app/display/finite geometry and a valid saved input (literal argv uses a JSON string array)."; return; }
        var index = entries.FindIndex(x => x.Id == entryId);
        if (index < 0 && entries.Count >= WindowLayout.MaximumEntries) { Feedback.Text = "At most 32 entries per layout."; return; }
        if (index < 0) entries.Add(entry); else entries[index] = entry;
        front = BringToFront.IsChecked == true ? entry.Id : front == entry.Id ? null : front;
        RefreshEntries(entryId); Draw(); Feedback.Text = "Entry draft kept. Keep layout draft, then Save changes in Settings.";
    }
    private void RemoveEntry(object sender, RoutedEventArgs e)
    {
        if (EntryList.SelectedItem is not EntryItem item) return;
        entries.RemoveAll(x => x.Id == item.Entry.Id); if (front == item.Entry.Id) front = null;
        RefreshEntries(); NewEntry(sender, e);
    }
    private void Move(int delta)
    {
        if (EntryList.SelectedItem is not EntryItem item) return;
        var index = entries.FindIndex(x => x.Id == item.Entry.Id); var next = index + delta;
        if (next < 0 || next >= entries.Count) return;
        (entries[index], entries[next]) = (entries[next], entries[index]); RefreshEntries(item.Entry.Id);
    }
    private void MoveUp(object sender, RoutedEventArgs e) => Move(-1);
    private void MoveDown(object sender, RoutedEventArgs e) => Move(1);
    private void KeepLayout(object sender, RoutedEventArgs e)
    {
        var result = original with { Name = LayoutName.Text.Trim(), Enabled = LayoutEnabled.IsChecked == true,
            UsesPreferredGap = UseGap.IsChecked == true, LaunchMissingApplications = LaunchMissing.IsChecked == true, Entries = entries.ToList(), FrontmostEntryId = front };
        if ((result.Validate() ?? validate?.Invoke(result)) is { } error) { Feedback.Text = error; return; }
        Result = result; DialogResult = true;
    }
    private void GeometryChanged(object sender, RoutedEventArgs e) { if (ready && !filling) Draw(); }
    private void PreviewSizeChanged(object sender, SizeChangedEventArgs e) { if (ready && !filling) Draw(); }
    private void Draw()
    {
        Preview.Children.Clear();
        if (screens.Count == 0) { PreviewNote.Text = "No unambiguous display device identity available. Refresh displays; no preview fallback is fabricated."; return; }
        var bounds = screens.Select(x => x.Area.Bounds ?? x.Area.WorkArea).ToArray();
        var union = new Rect(bounds.Min(x => x.X), bounds.Min(x => x.Y), bounds.Max(x => x.Right) - bounds.Min(x => x.X), bounds.Max(x => x.Bottom) - bounds.Min(x => x.Y));
        var factor = Math.Min(Math.Max(1, Preview.ActualWidth - 20) / union.Width, 155 / union.Height);
        var left = (Preview.ActualWidth - union.Width * factor) / 2; var top = (175 - union.Height * factor) / 2;
        void Rectangle(Rect rect, Brush fill, Brush stroke, string tooltip, double opacity = 1)
        {
            var visual = new System.Windows.Shapes.Rectangle { Width = Math.Max(1, rect.Width * factor), Height = Math.Max(1, rect.Height * factor), RadiusX = 4, RadiusY = 4,
                Fill = fill, Stroke = stroke, StrokeThickness = 1, Opacity = opacity, ToolTip = tooltip };
            Canvas.SetLeft(visual, left + (rect.X - union.X) * factor); Canvas.SetTop(visual, top + (rect.Y - union.Y) * factor); Preview.Children.Add(visual);
        }
        var surface = (Brush)FindResource("WindowBrush"); var border = (Brush)FindResource("BorderBrush");
        var accent = (Brush)FindResource("AccentBrush"); var soft = (Brush)FindResource("AccentSoftBrush");
        foreach (var screen in screens) Rectangle(screen.Area.Bounds ?? screen.Area.WorkArea, surface, border, screen.Display.Name);
        var current = ReadEntry(); var previewEntries = entries.Where(x => current == null || x.Id != current.Id).Concat(current == null ? [] : new[] { current });
        foreach (var entry in previewEntries)
        {
            var screen = screens.FirstOrDefault(x => x.Display.Identity.Equals(entry.Display.Identity, StringComparison.OrdinalIgnoreCase));
            if (screen == null || WindowLayoutGeometry.Resolve(entry, screen.Area, UseGap.IsChecked == true ? gap : 0) is not { } frame) continue;
            Rectangle(frame, soft, accent, ApplicationName(entry.ApplicationId) + $" · {frame.Width:0} × {frame.Height:0} physical px", entry.Id == entryId ? 1 : .55);
        }
        PreviewNote.Text = $"{screens.Count} uniquely identified native display{(screens.Count == 1 ? "" : "s")} · requested physical frames. Preferred gap: {gap:0.##} DIP. Missing displays are not drawn or reassigned.";
    }
}
