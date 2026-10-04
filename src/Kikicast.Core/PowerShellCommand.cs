using System.Text;

namespace Kikicast.Core;

public static class PowerShellCommand
{
    public static string Encode(string script)
    {
        if (string.IsNullOrWhiteSpace(script) || script.Length > 8192 || script.Contains('\0'))
            throw new ArgumentException("Enter a command of at most 8192 characters.");
        // UTF-16LE is pwsh's documented -EncodedCommand format; no quoting/string interpolation.
        return Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
    }
    public static string FromQuery(string query)
    {
        var text = query.Trim();
        return text.Equals("pwsh", StringComparison.OrdinalIgnoreCase) ? ""
            : text.StartsWith("pwsh ", StringComparison.OrdinalIgnoreCase) ? text[5..].TrimStart() : text;
    }
}

public sealed class BoundedCommandOutput(int limit = 256 * 1024)
{
    private readonly object gate = new();
    private readonly StringBuilder buffer = new();
    private long revision;
    public void Append(string text)
    {
        lock (gate)
        {
            buffer.Append(text);
            if (buffer.Length > limit)
            {
                buffer.Remove(0, buffer.Length - limit);
                if (buffer.Length > 0 && char.IsLowSurrogate(buffer[0])) buffer.Remove(0, 1);
            }
            revision++;
        }
    }
    public (string Text, long Revision) Snapshot() { lock (gate) return (buffer.ToString(), revision); }
}
