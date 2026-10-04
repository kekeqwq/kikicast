using Kikicast.Core;

namespace Kikicast.Core.Tests;

public class LauncherOrderConsistencyTests
{
    private sealed record Item(string Id, LauncherSearchProfile Profile, LauncherSearchSignals Signals);
    private static Item Entry(string id, string title, double usage = 1, bool boost = false, string? alias = null)
        => new(id, LauncherSearchProfile.Create(title), new(title, usage, Alias: alias == null ? null : LauncherSearchText.Create(alias),
            BoostedTerms: boost ? new HashSet<string> { "term" } : null));
    private static Item[] Rank(string query, params Item[] items)
        => LauncherOrder.Ranked(items, query, SearchSensitivity.Low, items.Length, x => x.Profile, x => x.Signals).ToArray();
    private static IEnumerable<Item[]> Permutations(Item[] triple)
    {
        for (var a = 0; a < 3; a++) for (var b = 0; b < 3; b++)
            if (a != b) yield return [triple[a], triple[b], triple[3 - a - b]];
    }
    [Fact]
    public void HigherRealUsageRevokesBoostForThePassInsteadOfCreatingAThreeWayCycle()
    {
        // Old pairwise rule: boosted beats exact; exact beats rival; rival beats boosted.
        var boosted = Entry("boosted", "t_e_r_m", usage: 5, boost: true);
        var rival = Entry("rival", "terminal", usage: 10);
        var exact = Entry("exact", "term");
        foreach (var order in Permutations([boosted, rival, exact]))
            Assert.Equal(new[] { "exact", "rival", "boosted" }, Rank("term", order).Select(x => x.Id));
    }
    [Fact]
    public void IntentionalExactAliasDoesNotRevokeBoostInTheOtherBucket()
    {
        var alias = Entry("alias", "Other", usage: 1000, alias: "term");
        var boosted = Entry("boosted", "t_e_r_m", boost: true);
        var exact = Entry("exact", "term");
        foreach (var order in Permutations([alias, boosted, exact]))
            Assert.Equal(new[] { "alias", "boosted", "exact" }, Rank("term", order).Select(x => x.Id));
    }
    [Theory]
    [InlineData("ter")]
    [InlineData("term")]
    [InlineData("terminal")]
    public void UnboostedComparatorIsAntisymmetricAndTransitiveAcrossFieldAndLearningBuckets(string query)
    {
        var random = new Random(42117);
        var titles = new[] { query, "Terminal tool", "t_e_r_m", "Other", "Résumé", "微信", "Ｔｅｒｍ", "t e r m" };
        var terms = new[] { Array.Empty<string>(), new[] { query }, new[] { query + "inal" }, new[] { query[..1] }, new[] { query[..^1] } };
        var entries = Enumerable.Range(0, 64).Select(i =>
        {
            var title = titles[random.Next(titles.Length)];
            var profile = LauncherSearchProfile.Create(title, i % 5 == 0 ? query : null, query);
            return new Item(i.ToString(), profile, new(title, i % 7 + 1, terms[random.Next(terms.Length)],
                i % 3 == 0 ? LauncherSearchText.Create(i % 2 == 0 ? query : query + " alias") : null, Priority: i));
        }).ToArray();
        var before = new bool[entries.Length, entries.Length];
        for (var a = 0; a < entries.Length; a++) for (var b = a + 1; b < entries.Length; b++)
        {
            var forward = Rank(query, entries[a], entries[b]); var backward = Rank(query, entries[b], entries[a]);
            Assert.Equal(2, forward.Length); Assert.Equal(forward, backward);
            before[a, b] = ReferenceEquals(forward[0], entries[a]); before[b, a] = !before[a, b];
        }
        for (var a = 0; a < entries.Length; a++) for (var b = 0; b < entries.Length; b++) for (var c = 0; c < entries.Length; c++)
            if (before[a, b] && before[b, c]) Assert.True(before[a, c], $"Cycle for '{query}': {a} < {b} < {c}, but not {a} < {c}");
        var ranked = Rank(query, entries);
        Assert.Equal(entries.Length, ranked.Length);
        Assert.Equal(ranked, Rank(query, entries.Reverse().ToArray()));
    }
    [Fact]
    public void MixedBoostCatalogIsPermutationInvariantAndLimitKeepsTheSamePrefix()
    {
        var random = new Random(20261004);
        var entries = Enumerable.Range(0, 80).Select(i =>
        {
            var title = i % 2 == 0 ? "Terminal " + i : "Other " + i;
            return new Item(i.ToString(), LauncherSearchProfile.Create(title, i % 5 == 0 ? "term" : null, "term"),
                new(title, i % 11 + 1, i % 3 == 0 ? new[] { "term" } : null,
                    i % 7 == 0 ? LauncherSearchText.Create("term") : null, Priority: i,
                    BoostedTerms: i % 4 == 0 ? new HashSet<string> { "term" } : null));
        }).ToArray();
        var expected = Rank("term", entries);
        for (var iteration = 0; iteration < 40; iteration++)
        {
            var shuffled = entries.OrderBy(_ => random.Next()).ToArray();
            Assert.Equal(expected, Rank("term", shuffled));
            Assert.Equal(expected.Take(7), LauncherOrder.Ranked(shuffled, "term", SearchSensitivity.Low, 7, x => x.Profile, x => x.Signals));
        }
    }
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-1)]
    [InlineData(0)]
    public void NonFiniteOrBelowBaselineUsageCannotDistortBoostEligibility(double invalid)
    {
        var boosted = Entry("boosted", "t_e_r_m", boost: true);
        var rival = Entry("rival", "term", usage: invalid);
        Assert.Equal(new[] { "boosted", "rival" }, Rank("term", rival, boosted).Select(x => x.Id));
    }
    [Fact]
    public void WarmedRankAvoidsCandidateClosuresAndPerComparisonArrays()
    {
        var entries = Enumerable.Range(0, 1000).Select(i => Entry(i.ToString(), "Terminal " + i)).ToArray();
        for (var i = 0; i < 5; i++) { _ = Rank("te", entries); _ = Rank("zzzz-no-match", entries); }
        var start = GC.GetAllocatedBytesForCurrentThread(); _ = Rank("zzzz-no-match", entries);
        var missBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.True(missBytes < 16384, $"Misses allocated a closure/array for each candidate: {missBytes} bytes.");
        start = GC.GetAllocatedBytesForCurrentThread(); _ = Rank("te", entries);
        var hitBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.True(hitBytes < 768000, $"Ranking allocated repeated collation folds or per-comparison params arrays: {hitBytes} bytes.");
    }
    [Fact]
    public void FoldedNaturalCollationRemainsTransitiveAcrossUnicodeAndNumericRuns()
    {
        var values = new[] { "Item 0", "Item 000", "item 2", "Ítem 02", "ITEM 10", "Item 100000000000000000000", "Item 9a", "Item 9B", "Ｐｏｗｅｒ１", "Power2", "微信", "微信2", "\u0000", "\ud800" };
        foreach (var a in values) foreach (var b in values)
        {
            Assert.Equal(-Math.Sign(LauncherOrder.NaturalCompare(a, b)), Math.Sign(LauncherOrder.NaturalCompare(b, a)));
            foreach (var c in values)
                if (LauncherOrder.NaturalCompare(a, b) <= 0 && LauncherOrder.NaturalCompare(b, c) <= 0)
                    Assert.True(LauncherOrder.NaturalCompare(a, c) <= 0);
        }
    }
}
