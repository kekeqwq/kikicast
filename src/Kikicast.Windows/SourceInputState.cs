using System.Runtime.InteropServices;

namespace Kikicast.Windows;

// Captured BEFORE activating the palette. Restore values, never toggle Chinese/English.
// IMM metadata is optional: TSF-only/privileged apps may not expose a context.
public sealed record SourceInputState(nint Root, nint Focus, nint Layout, bool? Open, uint? Conversion, uint? Sentence)
{
    public static SourceInputState? Capture(nint root)
    {
        if (root == 0) return null;
        var thread = GetWindowThreadProcessId(root, out _);
        var info = new GuiInfo { Size = (uint)Marshal.SizeOf<GuiInfo>() };
        var focus = GetGUIThreadInfo(thread, ref info) && info.Focus != 0 ? info.Focus : root;
        bool? open = null; uint? conversion = null, sentence = null;
        var context = ImmGetContext(focus);
        if (context != 0)
        {
            try
            {
                open = ImmGetOpenStatus(context);
                if (ImmGetConversionStatus(context, out var c, out var s)) { conversion = c; sentence = s; }
            }
            finally { ImmReleaseContext(focus, context); }
        }
        return new(root, focus, GetKeyboardLayout(thread), open, conversion, sentence);
    }

    public void Restore(ForegroundTarget target)
    {
        if (Root != target.Handle || !target.IsValid() || !WindowActivation.IsForeground(Root)) return;
        var thread = GetWindowThreadProcessId(Root, out _);
        if (Layout != 0 && GetKeyboardLayout(thread) != Layout)
            SendMessageTimeout(Root, 0x0050, 0, Layout, 0x0002, 100, out _);
        // Do not touch a different/recreated child or a window the user has since left.
        if (!WindowActivation.IsForeground(Root) || !IsWindow(Focus) || GetAncestor(Focus, 2) != Root) return;
        var context = ImmGetContext(Focus);
        if (context == 0) return;
        try
        {
            if (Conversion is { } c && Sentence is { } s && ImmGetConversionStatus(context, out var currentC, out var currentS)
                && (c != currentC || s != currentS)) ImmSetConversionStatus(context, c, s);
            if (Open is { } open && ImmGetOpenStatus(context) != open) ImmSetOpenStatus(context, open);
        }
        finally { ImmReleaseContext(Focus, context); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct GuiInfo
    {
        public uint Size, Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public int Left, Top, Right, Bottom;
    }
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiInfo info);
    [DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint SendMessageTimeout(nint hwnd, uint message, nint wParam, nint lParam, uint flags, uint timeout, out nint result);
    [DllImport("imm32.dll")] private static extern nint ImmGetContext(nint hwnd);
    [DllImport("imm32.dll")] private static extern bool ImmReleaseContext(nint hwnd, nint context);
    [DllImport("imm32.dll")] private static extern bool ImmGetOpenStatus(nint context);
    [DllImport("imm32.dll")] private static extern bool ImmSetOpenStatus(nint context, bool open);
    [DllImport("imm32.dll")] private static extern bool ImmGetConversionStatus(nint context, out uint conversion, out uint sentence);
    [DllImport("imm32.dll")] private static extern bool ImmSetConversionStatus(nint context, uint conversion, uint sentence);
}
