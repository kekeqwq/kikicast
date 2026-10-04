namespace Kikicast.Core;

// Tinycast LauncherRankingStore policy; timestamps are supplied, never read here.
public static class LauncherUsage
{
    private static readonly double Decay = Math.Log(2) / TimeSpan.FromDays(10).TotalSeconds;
    private sealed record TermSnapshot(IReadOnlyList<string> Values);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<LaunchVisit, TermSnapshot> termCache = new();
    public static string NormalizeTerm(string query) => LauncherSearchText.Create(query.Trim(), transliterated: true).Text;
    private static IReadOnlyList<string> NormalizedTerms(LaunchVisit visit) => termCache.GetValue(visit, static value => new(
        (value.SearchTerms ?? []).Select(NormalizeTerm).Where(x => x.Length is > 0 and <= 64).Distinct(StringComparer.Ordinal).TakeLast(3).ToArray())).Values;
    public static double Score(LaunchVisit? visit, DateTimeOffset now)
    {
        if (visit == null) return 1;
        if (visit.Anchor is { } anchor) return Math.Max(1, Math.Exp(Math.Min(709.78, (anchor - now).TotalSeconds * Decay)));
        // Legacy count/last-use data cannot recover individual visit times. Keep its approximate weight.
        return Math.Max(1, visit.Count * 100.0 * Math.Pow(.5, Math.Max(0, (now - visit.LastUsed).TotalDays) / 10));
    }
    public static LaunchVisit Visit(string key, LaunchVisit? previous, string? query, DateTimeOffset now)
    {
        var seconds = Math.Log(Score(previous, now) + 100) / Decay;
        var anchor = seconds >= (DateTimeOffset.MaxValue - now).TotalSeconds ? DateTimeOffset.MaxValue : now.AddSeconds(seconds);
        var terms = previous == null ? [] : NormalizedTerms(previous).ToList();
        if (query != null)
        {
            var term = NormalizeTerm(query);
            if (term.Length is > 0 and <= 64)
            { terms.RemoveAll(x => x == term); terms.Add(term); }
        }
        return new(key, Math.Min(99999, Math.Max(0, previous?.Count ?? 0)) + 1, now, anchor, terms.TakeLast(3).ToList());
    }
    public static IReadOnlyList<string> Terms(LaunchVisit? visit, DateTimeOffset now) => visit != null
        && Score(visit, now) > 1 && (now - visit.LastUsed).TotalHours < 408 ? NormalizedTerms(visit) : [];
}
