using System.Runtime.InteropServices;

namespace Kikicast.Windows;

// Keyboard layout changes are confined to the palette's UI thread, not the target app.
public sealed class EnglishInputSession
{
    private nint previousLayout, englishLayout, previousIme, window;
    public bool IsActive { get; private set; }
    private static bool English(nint layout) => (layout.ToInt64() & 0x3ff) == 0x0009;
    public bool IsEnglish => English(GetKeyboardLayout(0));

    public bool Begin(nint hwnd)
    {
        if (!IsActive)
        {
            previousLayout = GetKeyboardLayout(0);
            // LoadKeyboardLayout changes system-wide state on Windows 8+. Never use it.
            // Select an already installed English layout and activate on this thread only.
            var count = Math.Clamp(GetKeyboardLayoutList(0, null), 0, 256);
            var layouts = new nint[count];
            GetKeyboardLayoutList(count, layouts);
            englishLayout = layouts.FirstOrDefault(x => (x.ToInt64() & 0xffff) == 0x0409);
            if (englishLayout == 0) englishLayout = layouts.FirstOrDefault(English);
            window = hwnd;
            previousIme = ImmAssociateContext(window, 0);
            IsActive = true;
        }
        return Enforce();
    }
    public bool Enforce()
    {
        if (!IsActive) return false;
        if (IsEnglish) return true;
        if (englishLayout == 0) return false;
        ActivateKeyboardLayout(englishLayout, 0);
        return IsEnglish;
    }
    public void End()
    {
        if (!IsActive) return;
        IsActive = false;
        ImmAssociateContext(window, previousIme);
        if (previousLayout != 0) ActivateKeyboardLayout(previousLayout, 0);
        window = previousIme = previousLayout = 0;
    }
    [DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] private static extern int GetKeyboardLayoutList(int count, [Out] nint[]? layouts);
    [DllImport("user32.dll")] private static extern nint ActivateKeyboardLayout(nint layout, uint flags);
    [DllImport("imm32.dll")] private static extern nint ImmAssociateContext(nint hwnd, nint context);
}
