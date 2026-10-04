namespace Kikicast.Core;

public sealed record SavedCommand(Guid Id, string Name, string Script, string? WorkingDirectory = null, bool LoadProfile = false, bool Enabled = true)
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SavedCommand, SearchProfile> profiles = new();
    public List<SavedCommandArgument> Arguments { get; init; } = [];
    public string ScriptWithArguments(IReadOnlyList<string> values)
    {
        if (values.Count != Arguments.Count || values.Any(x => x == null || x.Contains('\0') || x.Length > 2048))
            throw new ArgumentException("Supply one value per argument, each at most 2048 characters and without null characters.");
        if (Arguments.Where((x, i) => !x.Optional && values[i].Length == 0).Any()) throw new ArgumentException("Fill every required argument.");
        if (Arguments.Count == 0) return Script;
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Script));
        // Values are single-quoted literals in a data array, never substituted into the trusted script.
        var literals = string.Join(",", values.Select(x => "'" + x.Replace("'", "''") + "'"));
        var bound = "$__kikicast_values = @(" + literals + "); & ([scriptblock]::Create([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + encoded + "')))) @__kikicast_values";
        PowerShellCommand.Encode(bound); // Bound the final launch command, not just the original script.
        return bound;
    }
    [System.Text.Json.Serialization.JsonIgnore]
    public string EntryId => "custom-command:" + Id.ToString("D");
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SavedCommand, LauncherSearchProfile> searchFields = new();
    [System.Text.Json.Serialization.JsonIgnore]
    public LauncherSearchProfile SearchFields => searchFields.GetValue(this, static command => LauncherSearchProfile.Create(command.Name ?? ""));
    public int Score(string query) => profiles.GetValue(this, static command => SearchProfile.Create(command.Name ?? "")).Score(query);
    // Cache is identity-keyed, so renaming via a record copy cannot reuse an old name profile.
    // Only name, never script text or secrets.
    public string? Validate()
    {
        if (Id == Guid.Empty) return "Command ID is missing.";
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 120 || Name.Contains('\0')) return "Enter a command name of at most 120 characters.";
        try { PowerShellCommand.Encode(Script); }
        catch (ArgumentException ex) { return ex.Message; }
        if (Arguments == null || Arguments.Count > 3 || Arguments.Any(x => x == null || string.IsNullOrWhiteSpace(x.Name) || x.Name.Length > 80 || x.Name.Contains('\0')))
            return "Declare at most three named arguments (80 characters per name).";
        if (Arguments.Count > 0)
        {
            try { ScriptWithArguments(Arguments.Select(x => x.Optional ? "" : "x").ToArray()); }
            catch (ArgumentException) { return "Script is too long for parameter binding; shorten the script or invoke a saved script file."; }
        }
        if (WorkingDirectory is { } folder && (folder.Length > 1024 || folder.Contains('\0'))) return "Invalid working directory.";
        return null;
    }
}
public sealed record SavedCommandArgument(string Name, bool Optional = false);
public sealed record SavedCommandLibrary
{
    public int Version { get; init; } = 1;
    public List<SavedCommand> Commands { get; init; } = [];
    public string? Validate()
    {
        if (Version != 1 || Commands == null || Commands.Count > 1000) return "Invalid or unsupported commands library.";
        if (Commands.Any(x => x == null)) return "A command is missing.";
        foreach (var command in Commands) if (command.Validate() is { } error) return error;
        if (Commands.Select(x => x.Id).Distinct().Count() != Commands.Count) return "Duplicate command IDs.";
        if (Commands.Select(x => x.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Commands.Count) return "A command with this name already exists.";
        return null;
    }
    public IReadOnlyList<SavedCommand> Visible(AppPreferences preferences) => !preferences.SavedCommandsEnabled ? []
        : Commands.Where(x => x.Enabled && (preferences.ShowSavedCommands || preferences.FavoriteKeys.Contains(x.EntryId, StringComparer.OrdinalIgnoreCase))).ToList();
    public SavedCommand? Runnable(Guid id, AppPreferences preferences) => !preferences.SavedCommandsEnabled ? null : Commands.FirstOrDefault(x => x.Id == id && x.Enabled);
    public SavedCommandLibrary ImportDisabled(SavedCommandLibrary incoming)
    {
        if (incoming.Validate() is { } error) throw new ArgumentException(error);
        var merged = this;
        foreach (var command in incoming.Commands) merged = merged.Upsert(command with { Enabled = false });
        return merged;
    }
    public SavedCommandLibrary Upsert(SavedCommand command) => this with
    { Commands = Commands.Any(x => x.Id == command.Id) ? Commands.Select(x => x.Id == command.Id ? command : x).ToList() : Commands.Append(command).ToList() };
    public SavedCommandLibrary Remove(Guid id) => this with { Commands = Commands.Where(x => x.Id != id).ToList() };
}
