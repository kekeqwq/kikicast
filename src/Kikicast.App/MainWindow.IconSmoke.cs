using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Kikicast.Core;
using Image = System.Windows.Controls.Image;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Size = System.Windows.Size;
using Point = System.Windows.Point;

namespace Kikicast.App;
public partial class MainWindow
{
    internal async Task VerifyCategoryIconsAsync(string? evidenceDirectory)
    {
        var rows = new[]
        {
            new Row("Owned application fallback", "Application", Entry: new("Owned application", "fixture:category-icon")),
            new Row("Owned window management", "Window command", WindowAction: Core.WindowAction.LeftHalf),
            new Row("Owned window layout", "Display-only fixture; never applied", Layout: new(Guid.NewGuid(), "Owned layout")),
            new Row("Owned extension", "Static metadata only; never executed", Extension: new("extension:owned:icon", "owned", "icon", "Owned extension", null, false)),
            new Row("Owned custom command", "Name only; never executed", Custom: new(Guid.NewGuid(), "Owned custom command", "")),
            new Row("Owned shell command", "Never launched", Command: "shell"),
            new Row("3", "Owned calculation · 1+2", Answer: "3"),
            new Row("Owned calculation history", "History", Command: "history"),
            new Row("Owned settings", "Settings", Command: "settings"),
            new Row("Owned Recycle Bin", "Never opened or emptied", Command: RecycleBinCommand.OpenId),
            new Row("Owned generic command", "Fallback", Command: "owned-icon-only")
        };
        if (rows.Select(x => x.IconKind).Distinct().Count() != Enum.GetValues<LauncherIconKind>().Length)
            throw new InvalidOperationException("Category artwork fixtures do not cover every icon kind.");
        var previous = IconsEnabled;
        var gallery = new StackPanel { Width = 580, Background = Brushes.Transparent, IsHitTestVisible = false };
        var visuals = new List<FrameworkElement>();
        try
        {
            // Attach real row templates to our owned window so ancestor/element bindings are exercised.
            PaletteSurface.Children.Add(gallery);
            foreach (var row in rows)
            {
                var visual = (FrameworkElement)Results.ItemTemplate.LoadContent(); visual.DataContext = row; visual.Margin = new Thickness(12, 5, 12, 5);
                gallery.Children.Add(visual); visuals.Add(visual);
            }
            foreach (var (dark, contrast, label) in new[] { (false, false, "light"), (true, false, "dark"), (false, true, "high-contrast-resource-probe") })
            {
                LauncherCategoryIcons.Populate(gallery.Resources, dark, contrast);
                var background = contrast ? System.Windows.SystemColors.WindowColor : dark ? Color.FromRgb(24, 28, 37) : Color.FromRgb(246, 248, 253);
                var foreground = contrast ? System.Windows.SystemColors.WindowTextColor : dark ? Color.FromRgb(240, 243, 250) : Color.FromRgb(29, 35, 47);
                gallery.Background = new SolidColorBrush(background);
                gallery.Resources["ForegroundBrush"] = new SolidColorBrush(foreground);
                gallery.Resources["MutedBrush"] = new SolidColorBrush(foreground);
                foreach (var visual in visuals) visual.SetResourceReference(ForegroundProperty, "ForegroundBrush");
                IconsEnabled = true; applicationIcons.SetEnabled(true);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind);
                gallery.Measure(new Size(580, double.PositiveInfinity)); gallery.Arrange(new System.Windows.Rect(gallery.DesiredSize)); gallery.UpdateLayout();
                for (var i = 0; i < visuals.Count; i++)
                {
                    var visual = visuals[i]; var icon = (Image)visual.FindName("CategoryIcon"); var slot = (Grid)visual.FindName("RowIcon");
                    var normal = (Grid)visual.FindName("NormalRow"); var title = (TextBlock)visual.FindName("RowTitle"); var subtitle = (TextBlock)visual.FindName("RowSubtitle");
                    if (icon.Source is not DrawingImage { IsFrozen: true } image || !ReferenceEquals(image, gallery.Resources[LauncherCategoryIcons.Key(rows[i].IconKind)])
                        || image.Drawing.Bounds.Width != 28 || image.Drawing.Bounds.Height != 28 || icon.Visibility != Visibility.Visible || slot.ActualWidth != 28 || normal.ColumnDefinitions[0].ActualWidth != 38
                        || Math.Abs(title.TranslatePoint(new Point(), normal).X - 38) > .01 || Grid.GetColumn(subtitle) != 1 || subtitle.Visibility == Visibility.Visible && Math.Abs(subtitle.TranslatePoint(new Point(), normal).X - 38) > .01)
                        throw new InvalidOperationException($"Category icon/layout failed: {rows[i].IconKind}/{label}, source={icon.Source?.GetType().Name}, frozen={icon.Source?.IsFrozen}, expected={ReferenceEquals(icon.Source, gallery.Resources[LauncherCategoryIcons.Key(rows[i].IconKind)])}, bounds={(icon.Source as DrawingImage)?.Drawing.Bounds}, visible={icon.Visibility}, slot={slot.ActualWidth}, column={normal.ColumnDefinitions[0].ActualWidth}, titleX={title.TranslatePoint(new Point(), normal).X}, subtitle={subtitle.Visibility}/{subtitle.TranslatePoint(new Point(), normal).X}");
                }
                if (evidenceDirectory != null)
                {
                    var dpi = VisualTreeHelper.GetDpi(this);
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(580 * dpi.DpiScaleX), (int)Math.Ceiling(rows.Length * 50 * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                    // Resource atlas, not a native-window screenshot. Avoid the real palette's parent clip/offset.
                    // Actual template/ancestor/alignment assertions are performed separately above.
                    var drawing = new DrawingVisual();
                    using (var context = drawing.RenderOpen())
                    {
                        context.DrawRectangle(gallery.Background, null, new System.Windows.Rect(0, 0, 580, rows.Length * 50));
                        for (var i = 0; i < rows.Length; i++)
                        {
                            context.DrawImage((DrawingImage)gallery.Resources[LauncherCategoryIcons.Key(rows[i].IconKind)], new System.Windows.Rect(12, i * 50 + 11, 28, 28));
                            var text = new FormattedText(rows[i].Title, System.Globalization.CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight, new Typeface("Segoe UI"), 17, new SolidColorBrush(foreground), dpi.PixelsPerDip);
                            context.DrawText(text, new Point(50, i * 50 + 4));
                            if (rows[i].ShowSubtitle)
                            {
                                var detail = new FormattedText(rows[i].Subtitle, System.Globalization.CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, new SolidColorBrush(foreground), dpi.PixelsPerDip);
                                context.DrawText(detail, new Point(50, i * 50 + 28));
                            }
                        }
                    }
                    bitmap.Render(drawing); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(evidenceDirectory, "category-icons-" + label + ".png")); encoder.Save(stream);
                }
            }
            // Disabling application artwork must not erase command icons or change column alignment.
            IconsEnabled = false; applicationIcons.SetEnabled(false);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind);
            if (((Image)visuals[0].FindName("CategoryIcon")).Visibility != Visibility.Collapsed || visuals.Skip(1).Any(x => ((Image)x.FindName("CategoryIcon")).Visibility != Visibility.Visible))
                throw new InvalidOperationException("Application icon gate affected category command artwork.");
            var before = applicationIcons.CachedCount;
            var converter = (ApplicationIconConverter)Resources["ApplicationIconConverter"];
            if (converter.Convert([rows[0].Entry!, false], typeof(object), null!, System.Globalization.CultureInfo.InvariantCulture) != null || applicationIcons.CachedCount != before)
                throw new InvalidOperationException("Disabled icon gate started resource IO.");
            IconsEnabled = true; applicationIcons.SetEnabled(true);
            var appLayer = (Grid)visuals[0].FindName("ApplicationIconLayer"); var state = new ApplicationIconState(); appLayer.DataContext = state;
            var original = BitmapSource.Create(16, 16, 96, 96, PixelFormats.Bgra32, null, new byte[16 * 16 * 4], 16 * 4); original.Freeze(); state.Publish(original);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind);
            if (!ReferenceEquals(((Image)visuals[0].FindName("ApplicationIcon")).Source, original) || ((Image)visuals[0].FindName("CategoryIcon")).Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Original asynchronous application artwork did not take precedence over the category fallback.");
            if (evidenceDirectory != null) await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "category-icons-evidence.json"), JsonSerializer.Serialize(new
            { categories = rows.Length, frozenVector28 = true, sharedTitleSubtitleColumn38 = true, themeResources = true, highContrastResourceProbe = true, physicalHighContrastAccepted = false, applicationOriginalPrecedence = true, applicationIconIoGate = true, commandIconsIndependent = true, commandsExecuted = false, wallpaperChanged = false, sourceRecycled = false }));
        }
        finally { PaletteSurface.Children.Remove(gallery); IconsEnabled = previous; applicationIcons.SetEnabled(previous); }
    }
}
