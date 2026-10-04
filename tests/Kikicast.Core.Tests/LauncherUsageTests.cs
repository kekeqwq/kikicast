using Kikicast.Core;

namespace Kikicast.Core.Tests;

public class LauncherUsageTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(200);
    [Fact]
    public void EachVisitAdds100AndDecaysBeforeNextVisit()
    {
        var first = LauncherUsage.Visit("x", null, "x", Now);
        Assert.Equal(101, LauncherUsage.Score(first, Now), 6);
        Assert.Equal(50.5, LauncherUsage.Score(first, Now.AddDays(10)), 6);
        var second = LauncherUsage.Visit("x", first, null, Now.AddDays(10));
        Assert.Equal(150.5, LauncherUsage.Score(second, Now.AddDays(10)), 6);
        Assert.Equal(1, LauncherUsage.Score(first, Now.AddDays(1000)));
        Assert.Equal(1, LauncherUsage.Score(null, Now));
    }
    [Fact]
    public void TermsAreNormalizedDistinctBoundedAndGatedAt17Days()
    {
        var visit = LauncherUsage.Visit("x", null, "ＡＢ", Now);
        visit = LauncherUsage.Visit("x", visit, "ab", Now);
        visit = LauncherUsage.Visit("x", visit, "cd", Now);
        visit = LauncherUsage.Visit("x", visit, "ef", Now);
        visit = LauncherUsage.Visit("x", visit, "gh", Now);
        Assert.Equal(new[] { "cd", "ef", "gh" }, visit.SearchTerms);
        Assert.Equal(3, LauncherUsage.Terms(visit, Now.AddDays(17).AddTicks(-1)).Count);
        Assert.Empty(LauncherUsage.Terms(visit, Now.AddDays(17)));
        Assert.Equal(visit.SearchTerms, LauncherUsage.Visit("x", visit, new string('a', 65), Now).SearchTerms);
    }
    [Fact]
    public void VisitsDoNotMutatePreviousTermsAndCategoryVisitsDoNotLearn()
    {
        var old = LauncherUsage.Visit("x", null, "alpha", Now);
        var next = LauncherUsage.Visit("x", old, "beta", Now);
        Assert.Equal(new[] { "alpha" }, old.SearchTerms);
        Assert.Equal(new[] { "alpha", "beta" }, next.SearchTerms);
        Assert.Equal(old.SearchTerms, LauncherUsage.Visit("x", old, null, Now).SearchTerms);
    }
    [Fact]
    public void LegacyDataHasApproximateWeightAndCounterCannotOverflow()
    {
        var old = new LaunchVisit("x", 2, Now);
        Assert.Equal(100, LauncherUsage.Score(old, Now.AddDays(10)), 6);
        Assert.Equal(300, LauncherUsage.Score(LauncherUsage.Visit("x", old, null, Now), Now), 6);
        Assert.Equal(100000, LauncherUsage.Visit("x", old with { Count = int.MaxValue }, null, Now).Count);
        Assert.Equal(DateTimeOffset.MaxValue, LauncherUsage.Visit("x", null, null, DateTimeOffset.MaxValue).Anchor);
    }
}
