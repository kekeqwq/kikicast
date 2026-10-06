using System.Windows;
using System.Windows.Media;
using Kikicast.Core;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using SystemColors = System.Windows.SystemColors;

namespace Kikicast.App;

// Authored vector artwork only. No font glyphs, file/registry/network reads, plugin code or disk cache.
internal static class LauncherCategoryIcons
{
    internal static string Key(LauncherIconKind kind) => "LauncherIcon" + kind;
    internal static void Populate(ResourceDictionary resources, bool dark, bool contrast)
    {
        foreach (var kind in Enum.GetValues<LauncherIconKind>()) resources[Key(kind)] = Create(kind, dark, contrast);
    }
    internal static DrawingImage Create(LauncherIconKind kind, bool dark, bool contrast)
    {
        var (rgb, path) = kind switch
        {
            LauncherIconKind.Application => (0x4278D7u, "M7 7 H12 V12 H7 Z M16 7 H21 V12 H16 Z M7 16 H12 V21 H7 Z M16 16 H21 V21 H16 Z"),
            LauncherIconKind.WindowManagement => (0x7656D8u, "M6 7 H22 V21 H6 Z M6 11 H22 M14 11 V21"),
            LauncherIconKind.WindowLayout => (0x5364CFu, "M6 6 H12 V14 H6 Z M16 6 H22 V10 H16 Z M6 18 H12 V22 H6 Z M16 14 H22 V22 H16 Z"),
            LauncherIconKind.Extension => (0x168C96u, "M6 8 H11 V6 C11 3 17 3 17 6 V8 H22 V13 H20 C17 13 17 19 20 19 H22 V23 H16 V21 C16 18 10 18 10 21 V23 H6 V18 H8 C11 18 11 12 8 12 H6 Z"),
            LauncherIconKind.CustomCommand => (0x36945Fu, "M10 8 L5 14 L10 20 M18 8 L23 14 L18 20 M16 7 L12 21"),
            LauncherIconKind.Shell => (0x3776AEu, "M7 9 L12 14 L7 19 M14 19 H21"),
            LauncherIconKind.Calculator => (0xBD7623u, "M8 5 H20 V23 H8 Z M11 9 H17 M11 14 H12 M16 14 H17 M11 18 H12 M16 18 H17"),
            LauncherIconKind.History => (0x997446u, "M5 12 A9 9 0 1 1 6 20 M5 7 V12 H10 M14 9 V15 L18 17"),
            LauncherIconKind.Settings => (0x6F7D91u, "M12 4 H16 L17 7 H20 L22 10 L20 13 V16 L22 18 L20 21 H17 L16 24 H12 L11 21 H8 L6 18 L8 15 V12 L6 10 L8 7 H11 Z M18 14 A4 4 0 1 1 10 14 A4 4 0 1 1 18 14"),
            LauncherIconKind.RecycleBin => (0xBF6272u, "M7 8 H21 M11 8 V5 H17 V8 M9 8 L10 23 H18 L19 8 M12 12 V19 M16 12 V19"),
            _ => (0xAD7144u, "M16 4 L7 16 H13 L12 24 L21 12 H15 Z")
        };
        var accent = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        // Tinted tile/contrasting colored line in normal themes; opaque system colors in high contrast.
        var foreground = contrast ? SystemColors.WindowTextColor : dark
            ? Color.FromRgb((byte)((accent.R + 255) / 2), (byte)((accent.G + 255) / 2), (byte)((accent.B + 255) / 2)) : accent;
        var background = contrast ? SystemColors.WindowColor : Color.FromArgb(dark ? (byte)64 : (byte)28, accent.R, accent.G, accent.B);
        var pen = new Pen(new SolidColorBrush(foreground), 1.7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(background), null, new RectangleGeometry(new System.Windows.Rect(0, 0, 28, 28), 6, 6)));
        drawing.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse(path)));
        var image = new DrawingImage(drawing); image.Freeze(); return image;
    }
}
