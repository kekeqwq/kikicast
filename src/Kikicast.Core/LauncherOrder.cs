namespace Kikicast.Core;

// Field-aware comparator adapted from Tinycast LauncherOrder.swift, fa1c2bb (AGPL-3.0).
public sealed record LauncherSearchSignals(string Title, double Frecency = 1, IReadOnlyList<string>? Terms = null,
    LauncherSearchText? Alias = null, int Priority = 0, IReadOnlySet<string>? BoostedTerms = null);
public static class LauncherOrder
{
    private sealed record Query(LauncherSearchText Latin, LauncherSearchText Typed, string Term, string LatinTerm);
    private enum TermKind { None, Overbounds, Prefix, Exact }
    private readonly record struct Term(TermKind Kind = TermKind.None, int Length = 0)
    { public bool Long => Kind == TermKind.Overbounds && Length >= 3; }
    private readonly record struct Facts(int Alias, bool Boosted, bool TitleExact, int Title, bool TitlePrefix, bool SubtitleExact, int Subtitle, Term Term);
    private sealed class Candidate<T>(T item, LauncherSearchSignals signals, Facts facts, int position)
    {
        public T Item { get; } = item;
        public LauncherSearchSignals Signals { get; } = signals;
        public Facts Facts { get; } = facts;
        public int Position { get; } = position;
        public bool BoostEligible { get; set; } = facts.Boosted;
        private string? foldedTitle;
        public string SortTitle => foldedTitle ??= LauncherSearchText.Fold(Signals.Title);
    }
    public static IReadOnlyList<T> Ranked<T>(IEnumerable<T> items, string query, SearchSensitivity sensitivity, int limit,
        Func<T, LauncherSearchProfile> profile, Func<T, LauncherSearchSignals> signals)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 128 || limit <= 0) return [];
        var trimmed = query.Trim(); var latin = LauncherSearchText.Create(trimmed, true);
        var q = new Query(latin, LauncherSearchText.Create(trimmed), LauncherSearchText.Fold(trimmed), latin.Text);
        if (q.Typed.Text.Length == 0 || q.Latin.Text.Length == 0) return [];
        var scored = new List<Candidate<T>>(); var position = 0; var hasBoost = false;
        foreach (var item in items)
        {
            var signal = signals(item);
            if (!double.IsFinite(signal.Frecency) || signal.Frecency < 1) signal = signal with { Frecency = 1 };
            var facts = Describe(profile(item), signal, q, sensitivity);
            if (facts is { } value) { scored.Add(new(item, signal, value, position)); hasBoost |= value.Boosted; }
            position++;
        }
        if (hasBoost) ResolveBoosts(scored);
        scored.Sort((a, b) => { var result = Compare(a, b, q.Latin.Text.Length); return result != 0 ? result : a.Position.CompareTo(b.Position); });
        return scored.Take(limit).Select(x => x.Item).ToArray();
    }
    private static Facts? Describe(LauncherSearchProfile p, LauncherSearchSignals s, Query q, SearchSensitivity sensitivity)
    {
        var alias = s.Alias == null ? 0 : s.Alias.Text == q.Typed.Text ? 2 : s.Alias.Text.StartsWith(q.Typed.Text, StringComparison.Ordinal) ? 1 : 0;
        var title = LauncherMatch.Match(q.Latin, p.Title);
        var strength = title?.Value ?? int.MinValue; var exact = title?.Exact == true;
        var admitted = alias != 0 || title?.Accepted(sensitivity, q.Latin.Text.Length) == true;
        // Do not allocate LINQ closures/arrays for every indexed candidate, including misses.
        for (var i = 0; i < p.Alternates.Count + (alias == 0 && s.Alias != null ? 1 : 0); i++)
        {
            var alternate = i < p.Alternates.Count ? p.Alternates[i] : s.Alias!;
            var match = LauncherMatch.Match(q.Typed, alternate);
            strength = Math.Max(strength, match?.Value ?? int.MinValue); exact |= match?.Exact == true;
            admitted |= match?.Accepted(sensitivity, q.Typed.Text.Length) == true;
        }
        var subtitle = p.Subtitle == null ? null : LauncherMatch.Match(q.Latin, p.Subtitle);
        admitted |= subtitle?.Accepted(sensitivity, q.Latin.Text.Length) == true;
        if (!admitted)
            for (var i = 0; i < p.Keywords.Count; i++)
                if (LauncherMatch.Match(q.Latin, p.Keywords[i])?.Accepted(sensitivity, q.Latin.Text.Length) == true) { admitted = true; break; }
        if (!admitted) return null;
        var prefix = p.Title.Text.StartsWith(q.Latin.Text, StringComparison.Ordinal);
        if (!prefix)
        {
            for (var i = 0; i < p.Alternates.Count; i++)
                if (p.Alternates[i].Text.StartsWith(q.Typed.Text, StringComparison.Ordinal)) { prefix = true; break; }
            if (alias == 0 && s.Alias != null) prefix |= s.Alias.Text.StartsWith(q.Typed.Text, StringComparison.Ordinal);
        }
        return new(alias, s.BoostedTerms?.Contains(q.Term) == true, exact, strength, prefix,
            subtitle?.Exact == true, subtitle?.Value ?? int.MinValue, TermHit(s.Terms ?? [], q.LatinTerm));
    }
    private static Term TermHit(IReadOnlyList<string> terms, string query)
    {
        var best = new Term();
        for (var i = terms.Count - 1; i >= 0; i--)
        {
            var stored = terms[i];
            if (stored.Length == 0) continue;
            if (stored.Length > query.Length)
            { if (stored.StartsWith(query, StringComparison.Ordinal) && best.Kind != TermKind.Prefix) best = new(TermKind.Prefix, stored.Length); }
            else if (stored.Length == query.Length)
            { if (stored == query) return new(TermKind.Exact, stored.Length); }
            else if (query.StartsWith(stored, StringComparison.Ordinal) && query.Length - stored.Length <= 3
                && (best.Kind == TermKind.None || best.Kind == TermKind.Overbounds && stored.Length > best.Length)) best = new(TermKind.Overbounds, stored.Length);
        }
        return best;
    }
    private static void ResolveBoosts<T>(IReadOnlyList<Candidate<T>> candidates)
    {
        // Pairwise boost exceptions can cycle. Resolve revocation once for the matching
        // pass, independently within exact-alias/ordinary buckets (aliases always win).
        double ordinaryRival = 1, aliasRival = 1;
        foreach (var candidate in candidates)
            if (!candidate.Facts.Boosted)
            {
                if (candidate.Facts.Alias == 2) aliasRival = Math.Max(aliasRival, candidate.Signals.Frecency);
                else ordinaryRival = Math.Max(ordinaryRival, candidate.Signals.Frecency);
            }
        foreach (var candidate in candidates)
            candidate.BoostEligible = candidate.Facts.Boosted && candidate.Signals.Frecency >= (candidate.Facts.Alias == 2 ? aliasRival : ordinaryRival);
    }
    private static int Desc<T>(T a, T b) where T : IComparable<T> => b.CompareTo(a);
    private static int First(params ReadOnlySpan<int> values)
    { foreach (var value in values) if (value != 0) return value; return 0; }
    private static int Usage<T>(Candidate<T> a, Candidate<T> b) => Desc(a.Signals.Frecency, b.Signals.Frecency);
    private static int Collate<T>(Candidate<T> a, Candidate<T> b) => NaturalCompareFolded(a.SortTitle, b.SortTitle);
    private static int Tie<T>(Candidate<T> a, Candidate<T> b)
    {
        var order = First(Usage(a, b), Desc(a.Signals.Alias != null, b.Signals.Alias != null), Desc(a.Signals.Priority, b.Signals.Priority));
        return order != 0 ? order : Collate(a, b);
    }
    private static int Compare<T>(Candidate<T> a, Candidate<T> b, int length)
    {
        var x = a.Facts; var y = b.Facts;
        if (x.Alias != y.Alias && (x.Alias == 2 || y.Alias == 2)) return x.Alias == 2 ? -1 : 1;
        if (a.BoostEligible != b.BoostEligible) return a.BoostEligible ? -1 : 1;
        if (length > 3 && (x.TitleExact || y.TitleExact))
        {
            if (x.TitleExact != y.TitleExact) return x.TitleExact ? -1 : 1;
            var order = First(Desc((int)x.Term.Kind, (int)y.Term.Kind), x.Term.Kind == TermKind.Overbounds && y.Term.Kind == TermKind.Overbounds ? Desc(x.Term.Length, y.Term.Length) : 0, Usage(a, b));
            return order != 0 ? order : Tie(a, b);
        }
        if (x.Term.Kind == TermKind.Exact || y.Term.Kind == TermKind.Exact)
            return x.Term.Kind != y.Term.Kind ? x.Term.Kind == TermKind.Exact ? -1 : 1 : Tie(a, b);
        if (x.SubtitleExact || y.SubtitleExact)
        {
            if (x.SubtitleExact != y.SubtitleExact) return x.SubtitleExact ? -1 : 1;
            var order = First(Usage(a, b), Desc(x.Title, y.Title)); return order != 0 ? order : Tie(a, b);
        }
        if ((x.Alias == 1) != (y.Alias == 1)) return x.Alias == 1 ? -1 : 1;
        if ((x.Term.Kind == TermKind.Prefix) != (y.Term.Kind == TermKind.Prefix)) return x.Term.Kind == TermKind.Prefix ? -1 : 1;
        if (x.Term.Kind != TermKind.None && y.Term.Kind != TermKind.None)
        {
            if (x.Term.Kind == TermKind.Prefix && y.Term.Kind == TermKind.Prefix && Usage(a, b) != 0) return Usage(a, b);
            if (x.Term.Kind == TermKind.Overbounds && y.Term.Kind == TermKind.Overbounds && x.Term.Length != y.Term.Length && (x.Term.Length >= 3 || y.Term.Length >= 3)) return Desc(x.Term.Length, y.Term.Length);
            if (x.Term.Long != y.Term.Long) return x.Term.Long ? -1 : 1;
        }
        if (x.Term.Long && y.Term.Kind == TermKind.None) return -1;
        if (y.Term.Long && x.Term.Kind == TermKind.None) return 1;
        var fallback = First(Desc(Math.Max(x.Title, x.Subtitle), Math.Max(y.Title, y.Subtitle)), Usage(a, b), Desc(x.Title, y.Title),
            Desc(x.TitlePrefix, y.TitlePrefix), Desc(a.Signals.Priority, b.Signals.Priority));
        return fallback != 0 ? fallback : Collate(a, b);
    }
    public static int NaturalCompare(string left, string right) => NaturalCompareFolded(LauncherSearchText.Fold(left), LauncherSearchText.Fold(right));
    private static int NaturalCompareFolded(string a, string b)
    {
        var ai = 0; var bi = 0;
        while (ai < a.Length && bi < b.Length)
        {
            if (char.IsAsciiDigit(a[ai]) && char.IsAsciiDigit(b[bi]))
            {
                var ae = ai; var be = bi; while (ae < a.Length && char.IsAsciiDigit(a[ae])) ae++; while (be < b.Length && char.IsAsciiDigit(b[be])) be++;
                var an = a.AsSpan(ai, ae - ai).TrimStart('0'); var bn = b.AsSpan(bi, be - bi).TrimStart('0');
                var result = an.Length.CompareTo(bn.Length); if (result == 0) result = an.SequenceCompareTo(bn);
                if (result != 0) return result; ai = ae; bi = be;
            }
            else { var result = a[ai++].CompareTo(b[bi++]); if (result != 0) return result; }
        }
        return (a.Length - ai).CompareTo(b.Length - bi);
    }
}
