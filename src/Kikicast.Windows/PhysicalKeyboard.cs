using System.Runtime.InteropServices;

namespace Kikicast.Windows;

public static class PhysicalKeyboard
{
    // One read when the palette opens; no hook, injection, polling or global reset.
    public static bool IsEnterDown() => (GetAsyncKeyState(13) & 0x8000) != 0;
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);
}
