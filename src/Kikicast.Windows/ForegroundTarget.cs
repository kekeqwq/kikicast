using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Kikicast.Windows;

public sealed record ForegroundTarget(nint Handle, uint ProcessId, DateTime Started)
{
    public static ForegroundTarget? Capture()
    {
        var hwnd = GetForegroundWindow();
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == Environment.ProcessId) return null;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return new(hwnd, pid, process.StartTime);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { return null; }
    }

    public bool IsValid()
    {
        if (!IsWindow(Handle)) return false;
        GetWindowThreadProcessId(Handle, out var pid);
        if (pid != ProcessId) return false;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.StartTime == Started;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }

    public void Restore() { if (IsValid()) WindowActivation.Once(Handle); }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
}
