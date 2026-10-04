namespace Kikicast.Core;

public sealed record DiscoveredApplication(string Path, string Name, DateTimeOffset FirstSeen);
public sealed record DiscoveredApplications
{
    public int Version { get; init; } = 1;
    public List<DiscoveredApplication> Applications { get; init; } = [];
    public List<string> IgnoredPaths { get; init; } = [];
    public bool IsValid => Version == 1 && Applications != null && IgnoredPaths != null && Applications.Count <= 1000
        && IgnoredPaths.Count <= 1000 && Applications.All(x => x != null && !string.IsNullOrWhiteSpace(x.Path) && !string.IsNullOrWhiteSpace(x.Name))
        && IgnoredPaths.All(x => !string.IsNullOrWhiteSpace(x));
    public DiscoveredApplications Remember(DiscoveredApplication application, bool manual = false)
    {
        if (!manual && IgnoredPaths.Contains(application.Path, StringComparer.OrdinalIgnoreCase)) return this;
        if (Applications.Any(x => StringComparer.OrdinalIgnoreCase.Equals(x.Path, application.Path))) return this;
        return this with
        {
            Applications = Applications.Append(application).TakeLast(1000).ToList(),
            IgnoredPaths = manual ? IgnoredPaths.Where(x => !StringComparer.OrdinalIgnoreCase.Equals(x, application.Path)).ToList() : IgnoredPaths
        };
    }
    public DiscoveredApplications Forget(string path) => this with
    {
        Applications = Applications.Where(x => !StringComparer.OrdinalIgnoreCase.Equals(x.Path, path)).ToList(),
        IgnoredPaths = IgnoredPaths.Where(x => !StringComparer.OrdinalIgnoreCase.Equals(x, path)).Append(path).TakeLast(1000).ToList()
    };
    // Deletion hides the entry but does not permanently suppress a later reinstall/run.
    public DiscoveredApplications Prune(Func<string, bool> exists) => this with { Applications = Applications.Where(x => exists(x.Path)).ToList() };
}
