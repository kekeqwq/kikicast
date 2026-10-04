using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using Kikicast.Core;

// Opt-in, generated data only. No application indexing, filesystem scan, registry,
// network, desktop input or user configuration. JSON goes to stdout, not a fixed file.
var count = 5000; var samples = 15; var label = "local";
try
{
    for (var i = 0; i < args.Length; i += 2)
    {
        if (i + 1 >= args.Length) throw new ArgumentException("Every option requires a value.");
        switch (args[i])
        {
            case "--count": count = int.Parse(args[i + 1]); break;
            case "--samples": samples = int.Parse(args[i + 1]); break;
            case "--label": label = args[i + 1]; break;
            default: throw new ArgumentException("Unknown option: " + args[i]);
        }
    }
    if (count is < 1 or > 30000 || samples is < 5 or > 100 || label.Length is < 1 or > 64)
        throw new ArgumentException("count must be 1–30000, samples 5–100, label 1–64 characters.");
}
catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
{ Console.Error.WriteLine(ex.Message); return 2; }

var titles = new[] { "Windows Terminal", "Visual Studio", "PowerShell", "微信", "Résumé café", "Application", "Term Utility", "文件管理", "Ｐｏｗｅｒ Tools", "T_e_r_m" };
var buildStart = Stopwatch.GetTimestamp(); var buildBytes = GC.GetAllocatedBytesForCurrentThread();
var items = Enumerable.Range(0, count).Select(i =>
{
    var title = titles[i % titles.Length] + " " + i;
    var keywords = i % 13 == 0 ? Enumerable.Range(0, 16).Select(j => "RegistryAlias" + j).ToArray() : [];
    return new Item(i, LauncherSearchProfile.Create(title, i % 7 == 0 ? "terminal command" : null, keywords),
        new(title, i % 23 == 0 ? 100 : 1, i % 19 == 0 ? new[] { "terminal", "wei xin" } : null,
            i % 17 == 0 ? LauncherSearchText.Create("terminal alias " + i) : null, i % 5 == 0 ? 3 : 4));
}).ToArray();
var buildMs = Stopwatch.GetElapsedTime(buildStart).TotalMilliseconds;
var profileBytes = GC.GetAllocatedBytesForCurrentThread() - buildBytes;
var reports = new List<object>();
foreach (var sensitivity in new[] { SearchSensitivity.Medium, SearchSensitivity.High })
    foreach (var query in new[] { "t", "te", "term", "terminal", "wei xin", "cafe", "registryalias", "zzzz-no-match" })
    {
        IReadOnlyList<Item> Search() => LauncherOrder.Ranked(items, query, sensitivity, 60, x => x.Profile, x => x.Signals);
        for (var warm = 0; warm < 6; warm++) _ = Search();
        var times = new double[samples]; var bytes = new long[samples]; var ids = Array.Empty<int>();
        for (var iteration = 0; iteration < samples; iteration++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
            var result = Search();
            times[iteration] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            bytes[iteration] = GC.GetAllocatedBytesForCurrentThread() - before;
            var nextIds = result.Select(x => x.Id).ToArray();
            if (iteration > 0 && !nextIds.SequenceEqual(ids)) throw new InvalidOperationException("Same-input results changed between benchmark passes.");
            ids = nextIds;
        }
        Array.Sort(times); Array.Sort(bytes);
        reports.Add(new { query, sensitivity = sensitivity.ToString(), resultCount = ids.Length, medianMs = times[samples / 2],
            p95Ms = times[(int)Math.Ceiling(samples * .95) - 1], medianAllocatedBytes = bytes[samples / 2], topIds = ids.Take(5) });
    }
Console.WriteLine(JsonSerializer.Serialize(new { schemaVersion = 1, label, generatedAtUtc = DateTimeOffset.UtcNow, count, samples,
    framework = RuntimeInformation.FrameworkDescription, osArchitecture = RuntimeInformation.OSArchitecture.ToString(),
    processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(), serverGc = GCSettings.IsServerGC, buildMs, profileBytes,
    scope = "Generated cached Core profiles only; not WPF/indexing/physical input or a release latency SLO", reports }, new JsonSerializerOptions { WriteIndented = true }));
return 0;

sealed record Item(int Id, LauncherSearchProfile Profile, LauncherSearchSignals Signals);
