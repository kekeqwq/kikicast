using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kikicast.Core;

// User-authored launch data, never collected process argv, search text or usage history.
public enum WindowLayoutInputKind { File, Uri, Arguments }
public sealed record WindowLayoutInput(WindowLayoutInputKind Kind)
{
    public string? Value { get; init; }
    public List<string> Arguments { get; init; } = [];
    public string? Validate()
    {
        if (!Enum.IsDefined(Kind) || Arguments == null) return "Invalid saved layout input kind/list.";
        if (Kind == WindowLayoutInputKind.Arguments)
            return Value != null || Arguments.Count is < 1 or > 8 || Arguments.Any(x => !SafeText(x, 2048)) || Arguments.Sum(x => x.Length) > 8192
                ? "Use 1–8 literal arguments, at most 2048 characters each and 8192 total, without controls or malformed Unicode." : null;
        if (Arguments.Count != 0 || !SafeText(Value, 4096) || string.IsNullOrWhiteSpace(Value) || Value != Value.Trim()) return "Enter one nonempty saved path or URI without controls or surrounding spaces.";
        if (Kind == WindowLayoutInputKind.File) return LocalPath(Value!) ? null : "Use an absolute local drive path or ~/ path; no remote/device, traversal, wildcard or alternate-stream paths.";
        if (!System.Uri.TryCreate(Value, UriKind.Absolute, out var uri) || Value!.Any(char.IsWhiteSpace) || string.IsNullOrEmpty(uri.Scheme)
            || uri.Scheme.Length is < 2 or > 32 || uri.IsFile || (uri.UserInfo.Length != 0 && uri.Scheme != "mailto") || DangerousSchemes.Contains(uri.Scheme)
            || (uri.Scheme is "http" or "https") && string.IsNullOrEmpty(uri.Host)) return "Use a valid explicit URI without credentials; file/shell/script/system-command URI schemes are not supported.";
        return null;
    }
    private static readonly HashSet<string> DangerousSchemes = new(StringComparer.OrdinalIgnoreCase)
        { "file", "shell", "javascript", "vbscript", "data", "cmd", "powershell", "ms-msdt", "search-ms", "ms-settings", "mshta" };
    private static bool LocalPath(string value)
    {
        var home = value.StartsWith("~/", StringComparison.Ordinal) || value.StartsWith("~\\", StringComparison.Ordinal);
        var rooted = value.Length > 3 && char.IsAsciiLetter(value[0]) && value[1] == ':' && value[2] is '\\' or '/';
        if (!home && !rooted) return false;
        var rest = value[(home ? 2 : 3)..];
        if (rest.Length == 0 || rest.IndexOfAny([':', '*', '?', '"', '<', '>', '|']) >= 0) return false;
        return rest.Replace('/', '\\').Split('\\').All(x => x.Length != 0 && x is not "." and not ".." && !x.EndsWith(' ') && !x.EndsWith('.'));
    }
    private static bool SafeText(string? text, int maximum)
    {
        if (text == null || text.Length > maximum || text.Any(char.IsControl)) return false;
        var span = text.AsSpan();
        while (!span.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(span, out _, out var consumed) != System.Buffers.OperationStatus.Done) return false;
            span = span[consumed..];
        }
        return true;
    }
    public string? ValidateApplication(string applicationId)
    {
        if (applicationId.StartsWith("app:", StringComparison.OrdinalIgnoreCase) && !applicationId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return "Saved inputs need a direct EXE or registered packaged application; original shortcut paths/arguments are never replaced or appended.";
        return applicationId.StartsWith("packaged:", StringComparison.OrdinalIgnoreCase) && Kind == WindowLayoutInputKind.Arguments
            ? "Literal argv lists require a direct EXE. Package launch arguments are app-specific and not emulated; use a supported file/URI contract." : null;
    }
    [JsonIgnore] public string Description => Kind switch { WindowLayoutInputKind.File => "Saved file/folder", WindowLayoutInputKind.Uri => "Saved URI", _ => "Saved literal arguments" };
    [JsonIgnore] public string TargetKey => ((int)Kind).ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + (Kind switch
    {
        WindowLayoutInputKind.File => Value!.Replace('/', '\\').ToUpperInvariant(),
        WindowLayoutInputKind.Uri => new Uri(Value!, UriKind.Absolute).AbsoluteUri,
        _ => JsonSerializer.Serialize(Arguments)
    });
    public WindowLayoutInput Copy() => this with { Arguments = Arguments.ToList() };
}
