namespace Kikicast.Windows;

public static class UserDataDirectory
{
    public static string PathFor(string home, bool development) => Path.Combine(home, ".config",
        development ? "kikicast-dev" : "kikicast");

    // Copy, never move or delete the old channel. New files always win, including damaged files.
    // Copy raw bytes so future-version/corrupt user data is not silently rewritten.
    public static string Prepare(string home, string localApplicationData, bool development)
    {
        var destination = PathFor(home, development);
        var legacy = Path.Combine(localApplicationData, development ? "Kikicast.Dev" : "Kikicast");
        Directory.CreateDirectory(destination);
        var staged = new List<(string Temporary, string Target)>();
        var published = new List<string>();
        try
        {
            foreach (var name in new[] { "settings.json", "settings.json.bak", "history.json" })
            {
                var source = Path.Combine(legacy, name);
                var target = Path.Combine(destination, name);
                if (!File.Exists(source) || File.Exists(target)) continue;
                var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                staged.Add((temporary, target));
                using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                input.CopyTo(output); output.Flush(true);
            }
            foreach (var file in staged)
            {
                if (File.Exists(file.Target)) continue;
                File.Move(file.Temporary, file.Target, false);
                published.Add(file.Target);
            }
        }
        catch
        {
            // No stale half-migration may mask subsequent changes made in the legacy fallback.
            foreach (var file in published) File.Delete(file);
            throw;
        }
        finally
        {
            foreach (var file in staged) if (File.Exists(file.Temporary)) File.Delete(file.Temporary);
        }
        return destination;
    }
}
