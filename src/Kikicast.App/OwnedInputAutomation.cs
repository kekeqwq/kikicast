using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kikicast.App;

// Opt-in smoke helper only. No production activation route calls SendInput.
internal static class OwnedInputAutomation
{
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Mouse { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Keyboard { public ushort Key, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Explicit)] private struct Payload { [FieldOffset(0)] public Mouse Mouse; [FieldOffset(0)] public Keyboard Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public Payload Data; }
    private static readonly nuint Marker = 0x4B494B49;
    private static void Send(params Input[] input)
    {
        var accepted = SendInput((uint)input.Length, input, Marshal.SizeOf<Input>());
        if (accepted == input.Length) return;
        var releases = input.Take((int)accepted).Where(x => x.Type == 1 && (x.Data.Keyboard.Flags & 2) == 0 || x.Type == 0 && (x.Data.Mouse.Flags & 2) != 0)
            .Select(x =>
            {
                if (x.Type == 1) x.Data.Keyboard.Flags |= 2;
                else x.Data.Mouse.Flags = 4;
                return x;
            }).ToArray();
        if (releases.Length > 0) SendInput((uint)releases.Length, releases, Marshal.SizeOf<Input>());
        throw new InvalidOperationException("Windows refused the opted-in owned-window input probe; accepted downs were released.");
    }
    private static Input Key(ushort key, bool up = false) => new() { Type = 1, Data = new() { Keyboard = new()
    {
        Key = key, Flags = (up ? 2u : 0) | (key is >= 33 and <= 40 or >= 44 and <= 46 or 91 or 92 or 93 or 111 or 144 or 163 or 165 ? 1u : 0), Extra = Marker
    } } }; // E0 navigation keys must not be interpreted as Alt+numpad input.
    private static void RequireOwnedForeground(nint hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid != Environment.ProcessId || GetForegroundWindow() != hwnd)
            throw new InvalidOperationException("Input probe aborted: target is not our owned foreground window.");
    }
    public static async Task ClickToActivateAsync(Window window, string? evidencePath = null)
    {
        if (new ushort[] { 1, 2, 4, 5, 6, 16, 17, 18, 91, 92 }.Any(x => (GetAsyncKeyState(x) & 0x8000) != 0))
            throw new InvalidOperationException("Click probe aborted: a real pointer button or modifier is held.");
        window.UpdateLayout();
        await Task.Delay(180); // Let the owned surface finish its initial native show/layout.
        var hwnd = new WindowInteropHelper(window).Handle;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid != Environment.ProcessId || hwnd == 0) throw new InvalidOperationException("Click probe must target an owned window.");
        if (!SetWindowPos(hwnd, -1, 0, 0, 0, 0, 0x53)) throw new InvalidOperationException("Owned test surface could not be shown topmost.");
        await Task.Delay(120);
        if (!GetClientRect(hwnd, out var box)) throw new InvalidOperationException("Owned client bounds unavailable.");
        var point = new Point { X = (box.Right - box.Left) / 2, Y = Math.Min(16, (box.Bottom - box.Top) / 2) }; // Avoid lower-screen terminal/touch-keyboard overlays.
        if (!ClientToScreen(hwnd, ref point)) throw new InvalidOperationException("Owned client position unavailable.");
        if (!GetCursorPos(out var previous)) throw new InvalidOperationException("Original pointer position unavailable.");
        if (!SetCursorPos(point.X, point.Y)) throw new InvalidOperationException("Pointer move refused.");
        try
        {
            var rawHit = WindowFromPoint(point);
            var physicalHit = WindowFromPhysicalPoint(point);
            var hit = GetAncestor(rawHit, 2);
            if (hit != hwnd)
            {
                GetWindowThreadProcessId(hit, out var hitPid); DwmGetWindowAttribute(hwnd, 14, out var cloaked, sizeof(uint));
                var desktops = (IVirtualDesktopManager)new VirtualDesktopManager();
                desktops.IsWindowOnCurrentVirtualDesktop(hwnd, out var onCurrentDesktop);
                desktops.GetWindowDesktopId(hwnd, out var desktopId);
                Marshal.FinalReleaseComObject(desktops);
                if (evidencePath != null && GetWindowRect(hwnd, out var expectedBounds))
                {
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(evidencePath)!);
                    using var image = new System.Drawing.Bitmap(expectedBounds.Right - expectedBounds.Left, expectedBounds.Bottom - expectedBounds.Top);
                    using (var graphics = System.Drawing.Graphics.FromImage(image)) graphics.CopyFromScreen(expectedBounds.Left, expectedBounds.Top, 0, 0, image.Size);
                    image.Save(evidencePath, System.Drawing.Imaging.ImageFormat.Png);
                    Screenshot(window, evidencePath + ".owned-render.png");
                }
                throw new InvalidOperationException($"Click probe aborted: owned point ({point.X},{point.Y}) covered; target={hwnd}, hit={hit}, raw={rawHit}, physical={physicalHit}, hitPid={hitPid}, cloaked={cloaked}, visible={IsWindowVisible(hwnd)}, style={GetWindowLong(hwnd, -16):X}, extended={GetWindowLong(hwnd, -20):X}, currentDesktop={onCurrentDesktop}, desktopId={desktopId}.");
            }
            Send(new() { Data = new() { Mouse = new() { Flags = 2, Extra = Marker } } }, new() { Data = new() { Mouse = new() { Flags = 4, Extra = Marker } } });
            await Task.Delay(120);
            RequireOwnedForeground(hwnd);
        }
        finally
        {
            if (GetCursorPos(out var current) && current.X == point.X && current.Y == point.Y) SetCursorPos(previous.X, previous.Y);
        }
    }
    public static async Task ChordAsync(Window window, ushort key, params ushort[] modifiers)
    {
        var hwnd = new WindowInteropHelper(window).Handle; RequireOwnedForeground(hwnd);
        if (new ushort[] { 16, 17, 18, 91, 92, key }.Any(x => (GetAsyncKeyState(x) & 0x8000) != 0)) throw new InvalidOperationException("Input probe aborted: a real modifier or probe key is already held.");
        try
        {
            foreach (var modifier in modifiers) Send(Key(modifier));
            if (modifiers.Length > 0)
            {
                // Let WPF observe modifier state before a system-key message; releasing Alt
                // immediately can turn Alt+Down into an unmodified Down in queued dispatch.
                await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                await Task.Delay(35); RequireOwnedForeground(hwnd);
            }
            Send(Key(key), Key(key, up: true));
            if (modifiers.Length > 0)
            {
                await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                await Task.Delay(35);
            }
        }
        finally { foreach (var modifier in modifiers.Reverse()) Send(Key(modifier, up: true)); }
        await Task.Delay(160);
    }
    public static async Task BurstLettersAsync(Window window, string text)
    {
        RequireOwnedForeground(new WindowInteropHelper(window).Handle);
        if (text.Length is < 1 or > 64 || !text.All(char.IsAsciiLetter)) throw new ArgumentException("Burst probe accepts bounded ASCII letters only.");
        if (new ushort[] { 16, 17, 18, 91, 92 }.Any(x => (GetAsyncKeyState(x) & 0x8000) != 0)
            || text.Any(x => (GetAsyncKeyState(char.ToUpperInvariant(x)) & 0x8000) != 0)) throw new InvalidOperationException("Burst probe aborted: real key held.");
        Send(text.SelectMany(c => new[] { Key(char.ToUpperInvariant(c)), Key(char.ToUpperInvariant(c), up: true) }).ToArray());
        await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        await Task.Delay(220);
    }

    public static async Task TextAsync(Window window, string text)
    {
        var hwnd = new WindowInteropHelper(window).Handle; RequireOwnedForeground(hwnd);
        if (new ushort[] { 16, 17, 18, 91, 92 }.Any(x => (GetAsyncKeyState(x) & 0x8000) != 0)) throw new InvalidOperationException("Input probe aborted: modifier held.");
        foreach (var character in text)
        {
            RequireOwnedForeground(hwnd);
            Send(new() { Type = 1, Data = new() { Keyboard = new() { Scan = character, Flags = 4, Extra = Marker } } },
                new() { Type = 1, Data = new() { Keyboard = new() { Scan = character, Flags = 6, Extra = Marker } } });
            await Task.Delay(45); // Give the real WPF text/composition pipeline a message-pump turn per character.
        }
        await Task.Delay(120);
    }
    public static void Screenshot(Window window, string path)
    {
        window.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        using var stream = System.IO.File.Create(path); encoder.Save(stream);
    }
    [ComImport, Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A")] private class VirtualDesktopManager { }
    [ComImport, Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(nint hwnd, [MarshalAs(UnmanagedType.Bool)] out bool current);
        [PreserveSig] int GetWindowDesktopId(nint hwnd, out Guid desktop);
        [PreserveSig] int MoveWindowToDesktop(nint hwnd, in Guid desktop);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Bounds { public int Left, Top, Right, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, uint attribute, out uint value, int size);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out Bounds bounds);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out Bounds bounds);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint hwnd, ref Point point);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern nint WindowFromPhysicalPoint(Point point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flag);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] input, int size);
}
