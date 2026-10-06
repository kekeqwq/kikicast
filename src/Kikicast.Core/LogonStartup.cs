namespace Kikicast.Core;

public static class LogonStartup
{
    // Windows Run/RunOnce documentation bounds command lines to 260 characters.
    public static bool ValidExecutable(string? path) => path is { Length: > 3 and <= 258 } && char.IsAsciiLetter(path[0])
        && path[1] == ':' && path[2] is '/' or '\\' && !path.Any(char.IsControl) && path.IndexOfAny(['"', '*', '?', '<', '>', '|']) < 0
        && path.Replace('/', '\\').Split('\\').Skip(1).All(x => x.Length > 0 && x is not "." and not ".." && !x.Contains(':'))
        && path.Replace('/', '\\').EndsWith("\\Kikicast.App.exe", StringComparison.OrdinalIgnoreCase);
    public static string Command(string path) => ValidExecutable(path) ? "\"" + path + "\"" : throw new ArgumentException("Choose the safe local Kikicast.App.exe apphost; startup command must fit 260 characters.");
}
