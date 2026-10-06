using System.Text.RegularExpressions;

namespace Kikicast.Core;

// Protocol 1: static declarations; listing/editing/installing never runs extension code.
public sealed record ExtensionCommandDefinition(string Id, string Title, bool ForEachFolder = false, bool Destructive = false);
public sealed record ExtensionBoolean(string Key, string Label, bool Default = true);
public sealed record ExtensionManifest
{
    public int Protocol { get; init; } = 1;
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    public string Runtime { get; init; } = "";
    public string MinimumHostVersion { get; init; } = "0.2.0";
    public string Executable { get; init; } = "";
    public List<string> Capabilities { get; init; } = [];
    public List<ExtensionCommandDefinition> Commands { get; init; } = [];
    public List<ExtensionBoolean> Booleans { get; init; } = [];
    public bool HasFolders { get; init; }
    public Dictionary<string, string> Files { get; init; } = [];
    public string? Validate(string runtime)
    {
        if (string.IsNullOrEmpty(Version) || string.IsNullOrEmpty(MinimumHostVersion) || string.IsNullOrEmpty(Executable) || Protocol != 1 || !ExtensionIdentity.Token(Id) || string.IsNullOrWhiteSpace(Name) || Name.Length > 100 || Name.Any(char.IsControl)
            || !Regex.IsMatch(Version, @"^\d{1,4}\.\d{1,4}\.\d{1,4}(?:-[a-zA-Z0-9.-]{1,40})?$") || !System.Version.TryParse(MinimumHostVersion, out var min)
            || min > new System.Version(0, 2, 0) || Runtime != runtime || runtime is not ("win-arm64" or "win-x64")) return "Unsupported extension identity, version, architecture or protocol.";
        if (!ExtensionIdentity.FilePath(Executable) || Executable.Contains('/') || !Executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return "Invalid extension executable.";
        if (Commands is not { Count: > 0 and <= 64 } || Commands.Any(x => x == null || !ExtensionIdentity.Token(x.Id) || string.IsNullOrWhiteSpace(x.Title) || x.Title.Length > 100 || x.Title.Any(char.IsControl) || x.ForEachFolder && !HasFolders)
            || Commands.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Commands.Count) return "Invalid extension command declarations.";
        if (Booleans == null || Booleans.Count > 16 || Booleans.Any(x => x == null || !ExtensionIdentity.Token(x.Key) || string.IsNullOrWhiteSpace(x.Label) || x.Label.Length > 100 || x.Label.Any(char.IsControl))
            || Booleans.Select(x => x.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Booleans.Count || Capabilities == null || Capabilities.Count > 16 || Capabilities.Any(x => !ExtensionIdentity.Token(x))) return "Invalid declarative extension settings or capabilities.";
        if (Files == null || Files.Count is < 1 or > 1024 || !Files.ContainsKey(Executable) || !Files.Keys.Any(x => x.StartsWith("licenses/", StringComparison.Ordinal))
            || Files.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != Files.Count || Files.Any(x => !ExtensionIdentity.FilePath(x.Key) || x.Key.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) || !Regex.IsMatch(x.Value ?? "", "^[a-f0-9]{64}$"))) return "Invalid extension file inventory or licenses.";
        return null;
    }
}
public sealed record ExtensionFolder(Guid Id, string Name, string Path, bool Enabled = true);
public sealed record ExtensionConfiguration
{
    public bool Enabled { get; init; }
    public Dictionary<string, bool> Options { get; init; } = [];
    public List<ExtensionFolder> Folders { get; init; } = [];
    public string? Validate(ExtensionManifest manifest)
    {
        if (Folders == null || Folders.Count > 32 || !manifest.HasFolders && Folders.Count != 0 || Folders.Any(x => x == null || x.Id == Guid.Empty || string.IsNullOrWhiteSpace(x.Name) || x.Name.Length > 100 || x.Name.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(x.Path) || x.Path.Length > 4096 || x.Path.Any(char.IsControl)) || Folders.Select(x => x.Id).Distinct().Count() != Folders.Count
            || manifest.Commands.Sum(x => x.ForEachFolder ? Folders.Count : 1) > 128
            || Options == null || Options.Count != manifest.Booleans.Count || manifest.Booleans.Any(x => !Options.ContainsKey(x.Key))) return "Invalid extension settings: at most 32 named folder identities and declared boolean options.";
        if (System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(this, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)).Length > 48000) return "Extension settings exceed the bounded IPC configuration budget.";
        return null;
    }
    public static ExtensionConfiguration Defaults(ExtensionManifest manifest) => new() { Options = manifest.Booleans.ToDictionary(x => x.Key, x => x.Default) };
}
public sealed record ExtensionCommand(string EntryId, string PluginId, string CommandId, string Title, Guid? FolderId, bool Destructive);
public static class ExtensionIdentity
{
    public static bool Token(string? value) => value is { Length: > 0 and <= 80 } && Regex.IsMatch(value, "^[a-z][a-z0-9.-]*$") && !value.Contains("..");
    public static bool FilePath(string? value) => value is { Length: > 0 and <= 240 } && !value.Contains('\\') && value.Split('/').All(x => x.Length > 0 && x is not "." and not ".." && !x.EndsWith('.') && !x.EndsWith(' ')
        && x.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_') && !Regex.IsMatch(x, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase));
    public static bool IsEntry(string? id)
    {
        var parts = id?.Split(':');
        return parts is { Length: 3 or 4 } && parts[0] == "extension" && Token(parts[1]) && Token(parts[2])
            && (parts.Length == 3 || Guid.TryParseExact(parts[3], "D", out var guid) && guid != Guid.Empty);
    }
    public static IReadOnlyList<ExtensionCommand> Commands(ExtensionManifest manifest, ExtensionConfiguration config, bool includeDisabled = false)
    {
        if (!includeDisabled && !config.Enabled || config.Validate(manifest) != null) return [];
        return manifest.Commands.SelectMany(c => c.ForEachFolder ? config.Folders.Where(x => includeDisabled || x.Enabled).Select(f => new ExtensionCommand($"extension:{manifest.Id}:{c.Id}:{f.Id:D}", manifest.Id, c.Id, manifest.Name + " → " + f.Name, f.Id, c.Destructive))
            : [new ExtensionCommand($"extension:{manifest.Id}:{c.Id}", manifest.Id, c.Id, manifest.Name + " → " + c.Title, null, c.Destructive)]).ToArray();
    }
}
