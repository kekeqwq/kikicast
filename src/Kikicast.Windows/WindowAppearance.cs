using System.Runtime.InteropServices;

namespace Kikicast.Windows;

public static class WindowAppearance
{
    public static uint AccentColor()
        => DwmGetColorizationColor(out var color, out _) == 0 ? color | 0xff000000 : 0xff678bfa;

    // Public Windows 11 DWM backdrop API. No undocumented composition policies.
    public static bool Apply(nint hwnd, bool dark, bool translucent)
    {
        var mode = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20, ref mode, sizeof(int));
        var corners = 2;
        DwmSetWindowAttribute(hwnd, 33, ref corners, sizeof(int));
        if (translucent)
        {
            var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);
            var border = -2; // DWMWA_COLOR_NONE: no square native border under our rounded surface
            DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int));
        }
        var backdrop = translucent ? 3 : 1; // transient/acrylic, or none
        var enabled = DwmSetWindowAttribute(hwnd, 38, ref backdrop, sizeof(int)) == 0 && translucent;
        return enabled;
    }
    public static void ClipRounded(nint hwnd, double radiusDip)
    {
        if (!GetWindowRect(hwnd, out var rect)) return;
        var diameter = (int)Math.Round(radiusDip * 2 * GetDpiForWindow(hwnd) / 96);
        var region = CreateRoundRectRgn(0, 0, rect.Right - rect.Left + 1, rect.Bottom - rect.Top + 1, diameter, diameter);
        if (region == 0) return;
        var existing = CreateRectRgn(0, 0, 0, 0);
        if (existing != 0)
        {
            var same = GetWindowRgn(hwnd, existing) != 0 && EqualRgn(existing, region);
            DeleteObject(existing);
            if (same) { DeleteObject(region); return; }
        }
        if (SetWindowRgn(hwnd, region, true) == 0) DeleteObject(region);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Margins { public int Left, Right, Top, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Bounds { public int Left, Top, Right, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref Margins margins);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out Bounds bounds);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("gdi32.dll")] private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint handle);
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern bool EqualRgn(nint first, nint second);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(nint hwnd, nint region);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint hwnd, nint region, bool redraw);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetColorizationColor(out uint color, out bool opaque);
}
