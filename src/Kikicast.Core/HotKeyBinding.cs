namespace Kikicast.Core;

public enum BindingKind { DoubleTap, Combo }
public enum KeySide { Any, Left, Right }

public sealed record HotKeyBinding(BindingKind Kind = BindingKind.DoubleTap, int Key = 17,
    uint Modifiers = 0, KeySide Side = KeySide.Any)
{
    public string? Validate()
    {
        if (!Enum.IsDefined(Kind) || !Enum.IsDefined(Side)) return "Unknown binding type or key side.";
        if (Kind == BindingKind.DoubleTap)
            return Key is 16 or 17 or 18 && Modifiers == 0 ? null : "Double-tap supports only Ctrl, Alt, or Shift.";
        if (Modifiers > 15 || Key is < 1 or > 254 || Key is 16 or 17 or 18 or 91 or 92 or >= 160 and <= 165)
            return "Invalid shortcut combination.";
        if ((Modifiers & (1 | 2 | 8)) == 0 && Key is not (>= 112 and <= 135))
            return "A normal key needs Ctrl, Alt, or Win. Shift alone is not supported.";
        return null;
    }

    public bool AcceptsModifier(int physicalKey) => Kind == BindingKind.DoubleTap && Key switch
    {
        16 => Matches(physicalKey, 160, 161),
        17 => Matches(physicalKey, 162, 163),
        18 => Matches(physicalKey, 164, 165),
        _ => false
    };
    private bool Matches(int key, int left, int right) => Side switch
    { KeySide.Left => key == left, KeySide.Right => key == right, _ => key == left || key == right };

    public string Display => Kind == BindingKind.DoubleTap
        ? $"Double {(Side == KeySide.Any ? "" : Side == KeySide.Left ? "left " : "right ")}{KeyName(Key)}"
        : string.Join("+", new[] { (Modifiers & 2) != 0 ? "Ctrl" : null, (Modifiers & 1) != 0 ? "Alt" : null,
            (Modifiers & 4) != 0 ? "Shift" : null, (Modifiers & 8) != 0 ? "Win" : null, KeyName(Key) }.OfType<string>());

    private static string KeyName(int key) => key switch
    {
        16 => "Shift", 17 => "Ctrl", 18 => "Alt", 32 => "Space", 13 => "Enter", 27 => "Esc",
        37 => "←", 38 => "↑", 39 => "→", 40 => "↓", 188 => ",", 190 => ".",
        >= 65 and <= 90 or >= 48 and <= 57 => ((char)key).ToString(),
        >= 112 and <= 135 => $"F{key - 111}", _ => $"VK {key}"
    };
}
