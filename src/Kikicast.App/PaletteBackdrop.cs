using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Kikicast.App;

// A bounded, in-memory snapshot of only the hidden palette's client footprint.
// No polling, files, background recording, private composition APIs, or content logging.
internal static class PaletteBackdrop
{
    public static BitmapSource? Capture(nint hwnd, double insetDip)
    {
        if (hwnd == 0 || IsWindowVisible(hwnd) || !GetWindowRect(hwnd, out var rect)) return null;
        var inset = (int)Math.Round(insetDip * GetDpiForWindow(hwnd) / 96);
        var width = rect.Right - rect.Left - inset * 2;
        var height = rect.Bottom - rect.Top - inset * 2;
        if (width <= 0 || height <= 0 || (long)width * height > 16_000_000) return null;
        nint bitmapHandle = 0;
        try
        {
            using var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(rect.Left + inset, rect.Top + inset, 0, 0,
                    new System.Drawing.Size(width, height), System.Drawing.CopyPixelOperation.SourceCopy);
            bitmapHandle = bitmap.GetHbitmap();
            var image = Imaging.CreateBitmapSourceFromHBitmap(bitmapHandle, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is ExternalException or System.ComponentModel.Win32Exception or ArgumentException)
        { return null; } // Protected/unavailable desktop: use the tinted fallback, never retry/grab focus.
        finally { if (bitmapHandle != 0) DeleteObject(bitmapHandle); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Bounds { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out Bounds bounds);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint handle);
}
