using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Kikicast.Core;
using Kikicast.Windows;

namespace Kikicast.App;

public partial class SettingsWindow
{
    private readonly List<CustomWindowSize> customSizes = [];
    private Guid sizeId;
    private bool fillingSize;
    private WindowSizeUnit widthUnit = WindowSizeUnit.Percent, heightUnit = WindowSizeUnit.Percent;
    private void RefreshCustomSizes(Guid? selected = null)
    {
        CustomSizeList.ItemsSource = customSizes.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        if (selected != null) CustomSizeList.SelectedItem = customSizes.FirstOrDefault(x => x.Id == selected);
        RefreshLauncherItems();
    }
    private void FillCustomSize(CustomWindowSize size)
    {
        fillingSize = true;
        try
        {
            sizeId = size.Id; SizeName.Text = size.Name;
            SizeWidth.Text = size.Width.Value.ToString(CultureInfo.CurrentCulture); SizeHeight.Text = size.Height.Value.ToString(CultureInfo.CurrentCulture);
            widthUnit = size.Width.Unit; heightUnit = size.Height.Unit;
            SizeWidthUnit.SelectedIndex = (int)widthUnit; SizeHeightUnit.SelectedIndex = (int)heightUnit;
            SizeAnchor.SelectedIndex = (int)size.Anchor;
            SizeOffsetX.Text = size.OffsetX.ToString(CultureInfo.CurrentCulture); SizeOffsetY.Text = size.OffsetY.ToString(CultureInfo.CurrentCulture);
            SizeEnabled.IsChecked = size.Enabled;
        }
        finally { fillingSize = false; }
    }
    private void NewCustomSize(object sender, RoutedEventArgs e)
    {
        if (saving) return;
        CustomSizeList.SelectedIndex = -1; FillCustomSize(new(Guid.NewGuid(), ""));
        SizeStatus.Text = "New draft · 60% × 60%, centered. Keep the draft before Save changes.";
    }
    private void CustomSizeSelected(object sender, SelectionChangedEventArgs e)
    { if (!saving && CustomSizeList.SelectedItem is CustomWindowSize size) FillCustomSize(size); }
    private void KeepCustomSize(object sender, RoutedEventArgs e)
    {
        if (saving) return;
        if (!int.TryParse(SizeWidth.Text, out var width) || !int.TryParse(SizeHeight.Text, out var height)
            || !int.TryParse(SizeOffsetX.Text, out var x) || !int.TryParse(SizeOffsetY.Text, out var y))
        { SizeStatus.Text = "Enter whole-number dimensions and offsets."; return; }
        var size = new CustomWindowSize(sizeId, SizeName.Text.Trim())
        {
            Width = new WindowSizeDimension(width, widthUnit).Clamped(), Height = new WindowSizeDimension(height, heightUnit).Clamped(),
            Anchor = (WindowSizeAnchor)SizeAnchor.SelectedIndex, OffsetX = Math.Clamp(x, -4000, 4000), OffsetY = Math.Clamp(y, -4000, 4000), Enabled = SizeEnabled.IsChecked == true
        };
        var next = customSizes.Where(s => s.Id != size.Id).Append(size).ToList();
        if (CustomWindowSize.ValidateList(next) is { } error) { SizeStatus.Text = error; return; }
        customSizes.Clear(); customSizes.AddRange(next); RefreshCustomSizes(size.Id);
        SizeStatus.Text = "Size draft kept: " + size.Summary + ". Save changes to persist. Set its shortcut in Launcher items.";
    }
    private void DeleteCustomSize(object sender, RoutedEventArgs e)
    {
        if (saving || CustomSizeList.SelectedItem is not CustomWindowSize size) return;
        customSizes.RemoveAll(x => x.Id == size.Id); itemBindingRows.Remove(size.EntryId);
        StopRecording(); RefreshCustomSizes(); NewCustomSize(sender, e);
        SizeStatus.Text = "Deletion staged. Save changes removes this size and its favorites, alias, hidden flag and shortcut; closing discards it.";
    }
    private void SizeUnitChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || fillingSize || sender is not System.Windows.Controls.ComboBox combo || combo.SelectedIndex < 0) return;
        var isWidth = ReferenceEquals(combo, SizeWidthUnit);
        var text = isWidth ? SizeWidth : SizeHeight;
        var oldUnit = isWidth ? widthUnit : heightUnit;
        var unit = (WindowSizeUnit)combo.SelectedIndex;
        // Documented screen work area/DPI only, never inspect another window's contents.
        var screen = WindowManager.DisplayForWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        if (!int.TryParse(text.Text, out var value) || screen == null)
        {
            fillingSize = true; combo.SelectedIndex = (int)oldUnit; fillingSize = false;
            SizeStatus.Text = "Enter a whole number before switching units."; return;
        }
        var scale = double.IsFinite(screen.Scale) && screen.Scale > 0 ? screen.Scale : 1;
        var gap = double.TryParse(WindowGap.Text, out var typedGap) ? typedGap : 0;
        var canvas = WindowGeometry.Canvas(screen.WorkArea, gap * scale);
        var available = (isWidth ? canvas.Width : canvas.Height) / scale;
        text.Text = new WindowSizeDimension(value, oldUnit).Clamped().Converted(unit, available).Value.ToString(CultureInfo.CurrentCulture);
        if (isWidth) widthUnit = unit; else heightUnit = unit;
        SizeStatus.Text = "Unit converted against the settings display's work area minus the gap. Runtime uses the target window's display.";
    }
}
