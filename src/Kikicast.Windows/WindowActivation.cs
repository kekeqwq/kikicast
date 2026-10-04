using System.Runtime.InteropServices;

namespace Kikicast.Windows;

public static class WindowActivation
{
    public static bool IsForeground(nint hwnd) => GetForegroundWindow() == hwnd;

    // One bounded activation, never a focus polling loop or synthetic Alt key.
    // Attach only while transferring activation; detach even if a native call fails.
    public static bool Once(nint hwnd, bool focusHost = false)
    {
        if (!IsWindow(hwnd)) return false;
        var current = GetCurrentThreadId();
        var foreground = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var destination = GetWindowThreadProcessId(hwnd, out _);
        var attachedForeground = foreground != 0 && foreground != current && AttachThreadInput(current, foreground, true);
        var attachedDestination = destination != 0 && destination != current && destination != foreground
            && AttachThreadInput(current, destination, true);
        try
        {
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
            if (focusHost && IsForeground(hwnd)) { SetActiveWindow(hwnd); SetFocus(hwnd); }
            return IsForeground(hwnd);
        }
        finally
        {
            if (attachedDestination) AttachThreadInput(current, destination, false);
            if (attachedForeground) AttachThreadInput(current, foreground, false);
        }
    }
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint first, uint second, bool attach);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(nint hwnd);
    [DllImport("user32.dll")] private static extern nint SetActiveWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint SetFocus(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
}
