namespace Kikicast.Native.Acceptance;

// Side-effect-free parse: no native calls, output creation, hooks or process start.
internal sealed record NativeAcceptanceOptions(bool Displays, string? Exe, string? Evidence, bool Terminate, bool Exercise)
{
    public static NativeAcceptanceOptions Parse(string[] args)
    {
        bool displays = false, terminate = false, exercise = false; string? exe = null, evidence = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var name = args[i]; if (!seen.Add(name)) throw new ArgumentException("Duplicate option: " + name);
            switch (name)
            {
                case "--describe-displays": displays = true; break;
                case "--allow-terminate-created": terminate = true; break;
                case "--exercise-created-window": exercise = true; break;
                case "--discovery-exe" when i + 1 < args.Length: exe = args[++i]; break;
                case "--evidence" when i + 1 < args.Length: evidence = args[++i]; break;
                default: throw new ArgumentException("Unsupported/missing option: " + name);
            }
        }
        if (exe == null ? !displays || evidence != null || terminate || exercise : displays || !terminate || string.IsNullOrWhiteSpace(evidence))
            throw new ArgumentException("Use --describe-displays alone, or --discovery-exe <local EXE> --evidence <new local folder> --allow-terminate-created [--exercise-created-window].");
        if (evidence != null && (!Path.IsPathFullyQualified(evidence) || evidence.StartsWith("\\\\", StringComparison.Ordinal))) throw new ArgumentException("Evidence directory must be fully qualified and local.");
        if (exe != null && (!Path.IsPathFullyQualified(exe) || exe.StartsWith("\\\\", StringComparison.Ordinal) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Use a fully qualified local EXE path.");
        return new(displays, exe == null ? null : Path.GetFullPath(exe), evidence, terminate, exercise);
    }
}
