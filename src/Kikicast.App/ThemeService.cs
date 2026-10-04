using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Kikicast.Windows;
using Microsoft.Win32;
using Color = System.Windows.Media.Color;
using SystemColors = System.Windows.SystemColors;

namespace Kikicast.App;

public sealed class ThemeService : IDisposable
{
    private readonly System.Windows.Application app;
    private readonly List<(WeakReference<Window> Window, bool Glass)> windows = [];
    private sealed class NativeState { public int Mode = -1; public bool Available; }
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window, NativeState> nativeStates = new();
    private bool dark, translucent, disposed, refreshQueued;
    private Color surface;
    public ThemeService(System.Windows.Application app)
    {
        this.app = app;
        SystemEvents.UserPreferenceChanged += PreferenceChanged;
        SystemParameters.StaticPropertyChanged += ParameterChanged;
        Refresh();
    }

    private static int Preference(string name, int fallback)
    {
        try { return Convert.ToInt32(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", name, fallback)); }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or FormatException or OverflowException or System.IO.IOException) { return fallback; }
    }
    public void Register(Window window, bool glass)
    {
        windows.Add((new(window), glass));
        window.SourceInitialized += (_, _) =>
        {
            ApplyWindow(window, glass);
            if (HwndSource.FromHwnd(new WindowInteropHelper(window).Handle) is { } source)
                source.AddHook((nint hwnd, int msg, nint wParam, nint lParam, ref bool handled) =>
                {
                    if (msg is 0x0320 or 0x031A or 0x001A || glass && msg is 0x0005 or 0x02E0) QueueRefresh();
                    return 0;
                });
        };
    }
    private void PreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => QueueRefresh();
    private void ParameterChanged(object? sender, PropertyChangedEventArgs e) => QueueRefresh();
    private void QueueRefresh()
    {
        if (disposed || app.Dispatcher.HasShutdownStarted || refreshQueued) return;
        refreshQueued = true;
        app.Dispatcher.BeginInvoke(() => { refreshQueued = false; Refresh(); });
    }
    public void Refresh()
    {
        if (disposed) return;
        var contrast = SystemParameters.HighContrast;
        app.Resources["ScrollThumbOpacity"] = contrast ? 1d : .4d;
        dark = Preference("AppsUseLightTheme", 1) == 0;
        translucent = !contrast && Preference("EnableTransparency", 1) != 0;
        surface = dark ? Color.FromRgb(24, 28, 37) : Color.FromRgb(246, 248, 253);
        var accent = WindowAppearance.AccentColor();
        var accentColor = Color.FromRgb((byte)(accent >> 16), (byte)(accent >> 8), (byte)accent);
        Brush("WindowBrush", contrast ? SystemColors.WindowColor : surface);
        Brush("SurfaceBrush", contrast ? SystemColors.WindowColor : dark ? Color.FromRgb(34, 39, 50) : Colors.White);
        Brush("ForegroundBrush", contrast ? SystemColors.WindowTextColor : dark ? Color.FromRgb(240, 243, 250) : Color.FromRgb(29, 35, 47));
        Brush("MutedBrush", contrast ? SystemColors.WindowTextColor : dark ? Color.FromRgb(164, 176, 196) : Color.FromRgb(100, 112, 134));
        Brush("BorderBrush", contrast ? SystemColors.WindowTextColor : dark ? Color.FromArgb(80, 204, 216, 240) : Color.FromArgb(90, 130, 149, 185));
        Brush("FocusBorderBrush", contrast ? SystemColors.HighlightColor : dark ? Color.FromArgb(128, 164, 176, 196) : Color.FromArgb(160, 100, 112, 134));
        Brush("AccentBrush", contrast ? SystemColors.HighlightColor : accentColor);
        Brush("AccentSoftBrush", contrast ? SystemColors.HighlightColor : Color.FromArgb(dark ? (byte)68 : (byte)38, accentColor.R, accentColor.G, accentColor.B));
        app.Resources["BackdropVisibility"] = translucent ? Visibility.Visible : Visibility.Collapsed;
        Brush("SelectionForegroundBrush", contrast ? SystemColors.HighlightTextColor : dark ? Color.FromRgb(240, 243, 250) : Color.FromRgb(29, 35, 47));
        Brush("PaletteSelectionBrush", contrast ? SystemColors.HighlightColor : dark ? Color.FromArgb(42, 255, 255, 255) : Color.FromArgb(20, 0, 0, 0));
        Brush("GlassHighlightBrush", contrast ? Colors.Transparent : Color.FromArgb(dark ? (byte)12 : (byte)24, 255, 255, 255));
        Brush("GlassEdgeBrush", contrast ? SystemColors.WindowTextColor : Color.FromArgb(dark ? (byte)95 : (byte)195, 255, 255, 255));
        windows.RemoveAll(x => !x.Window.TryGetTarget(out _));
        foreach (var item in windows)
            if (item.Window.TryGetTarget(out var window)) ApplyWindow(window, item.Glass);
    }
    private void Brush(string key, Color color)
    {
        var brush = new SolidColorBrush(color); brush.Freeze(); app.Resources[key] = brush;
    }
    private void ApplyWindow(Window window, bool glass)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0 || glass) {
            // Layered palette: per-pixel alpha. DWM frames and window regions repaint the square backing.
            var glassColor = SystemParameters.HighContrast ? SystemColors.WindowColor : surface;
            if (translucent) glassColor.A = dark ? (byte)190 : (byte)208;
            var glassBrush = new SolidColorBrush(glassColor); glassBrush.Freeze();
            window.Resources["GlassSurfaceBrush"] = glassBrush;
            if (hwnd == 0) return;
        }
        if (glass) return;
        var state = nativeStates.GetValue(window, _ => new NativeState());
        var mode = dark ? 1 : 0;
        if (state.Mode != mode)
        {
            state.Available = WindowAppearance.Apply(hwnd, dark, false);
            state.Mode = mode;
        }
        var color = SystemParameters.HighContrast ? SystemColors.WindowColor : surface;
        var brush = new SolidColorBrush(color); brush.Freeze();
        window.Resources["GlassSurfaceBrush"] = brush;
    }
    public void Dispose()
    {
        disposed = true;
        SystemEvents.UserPreferenceChanged -= PreferenceChanged;
        SystemParameters.StaticPropertyChanged -= ParameterChanged;
    }
}
