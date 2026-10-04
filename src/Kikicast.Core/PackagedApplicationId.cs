namespace Kikicast.Core;

public static class PackagedApplicationId
{
    // Package family + relative application ID, never a generic host executable.
    public const int MaximumLength = 130;
    public static bool TrySplit(string? value, out string family, out string application)
    {
        family = application = "";
        if (string.IsNullOrEmpty(value) || value.Length > MaximumLength) return false;
        var separator = value.IndexOf('!');
        if (separator <= 0 || separator != value.LastIndexOf('!')) return false;
        var f = value.AsSpan(0, separator); var a = value.AsSpan(separator + 1);
        var underscore = f.LastIndexOf('_');
        if (underscore is < 3 or > 50 || f.Length - underscore - 1 != 13 || a.Length is < 1 or > 64) return false;
        foreach (var c in f[..underscore]) if (!char.IsAsciiLetterOrDigit(c) && c is not '.' and not '-') return false;
        foreach (var c in f[(underscore + 1)..]) if (!char.IsAsciiLetterOrDigit(c)) return false;
        foreach (var c in a) if (!char.IsAsciiLetterOrDigit(c) && c is not '.' and not '-' and not '_') return false;
        family = f.ToString(); application = a.ToString(); return true;
    }
    public static bool IsValid(string? value) => TrySplit(value, out _, out _);
    public static string EntryId(string value) => "packaged:" + value.ToUpperInvariant();
    public static string ShellPath(string value) => "shell:AppsFolder\\" + value;
}
