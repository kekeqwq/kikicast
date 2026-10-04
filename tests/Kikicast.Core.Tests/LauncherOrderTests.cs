using System.Text.Json;
using Kikicast.Core;

namespace Kikicast.Core.Tests;

public class LauncherOrderTests
{
    private sealed record Item(string Id, LauncherSearchProfile Profile, LauncherSearchSignals Signals);
    private static Item Entry(string id, string name, string? alias = null, double usage = 1, string[]? terms = null, string? subtitle = null, string[]? keywords = null, int priority = 0, string[]? boosted = null)
        => new(id, LauncherSearchProfile.Create(name, subtitle, keywords ?? []), new(name, usage, terms, alias == null ? null : LauncherSearchText.Create(alias), priority, boosted?.ToHashSet()));
    private static string[] Rank(string query, params Item[] entries) => LauncherOrder.Ranked(entries, query, SearchSensitivity.Medium, 60, x => x.Profile, x => x.Signals).Select(x => x.Id).ToArray();
    [Fact]
    public void ExactAliasWinsEvenAgainstExactTitleAndHeavyUsage()
    { Assert.Equal(new[] { "alias", "title" }, Rank("terminal", Entry("title", "terminal", usage: 10000), Entry("alias", "Other", alias: "terminal"))); }
    [Fact]
    public void LongTitleExactBeatsLearnedTermButShortQueryAllowsLearning()
    {
        Assert.Equal("title", Rank("terminal", Entry("learned", "terminal tool", usage: 1000, terms: ["terminal"]), Entry("title", "terminal"))[0]);
        Assert.Equal("learned", Rank("app", Entry("title", "app"), Entry("learned", "apple", terms: ["app"]))[0]);
    }
    [Fact]
    public void ExactTermAndSubtitleBeatAliasPrefix()
    {
        Assert.Equal("term", Rank("term", Entry("alias", "Something", alias: "terminal"), Entry("term", "Terminal", terms: ["term"]))[0]);
        Assert.Equal("subtitle", Rank("tool", Entry("alias", "Something", alias: "tools"), Entry("subtitle", "Some other app", subtitle: "tool"))[0]);
    }
    [Fact]
    public void AliasPrefixAndLearnedPrefixBeatUnlearnedAlignment()
    {
        Assert.Equal("alias", Rank("term", Entry("plain", "terminal"), Entry("alias", "Other", alias: "terminal"))[0]);
        Assert.Equal("learned", Rank("term", Entry("plain", "terminal"), Entry("learned", "terminal utility", terms: ["terminal"] ))[0]);
    }
    [Fact]
    public void LongOverboundsIsLimitedToThreeExtraCharacters()
    {
        Assert.Equal("learned", Rank("terminal", Entry("plain", "terminal app"), Entry("learned", "terminal utility", terms: ["termi"]))[0]);
        Assert.Equal("plain", Rank("terminal", Entry("plain", "terminal app"), Entry("learned", "terminal utility", terms: ["term"]))[0]);
    }
    [Fact]
    public void ABoostYieldsOnlyToHigherRealUsage()
    {
        var boost = Entry("boost", "terminal utility", boosted: ["term"]);
        Assert.Equal("boost", Rank("term", Entry("plain", "terminal app"), boost)[0]);
        Assert.Equal("plain", Rank("term", Entry("plain", "terminal app", usage: 2), boost)[0]);
    }
    [Fact]
    public void KeywordOnlyAdmissionCannotPretendToBeAnExactTitle()
    { Assert.Equal(new[] { "title", "keyword" }, Rank("terminal", Entry("keyword", "Other", keywords: ["terminal"]), Entry("title", "terminal"))); }
    [Fact]
    public void KindPriorityNumericCollationAndStableTies()
    {
        Assert.Equal(new[] { "app", "command" }, Rank("item", Entry("command", "Item", priority: 3), Entry("app", "Item", priority: 4)));
        Assert.Equal(new[] { "two", "ten" }, Rank("item", Entry("ten", "Item 10"), Entry("two", "Item 2")));
        Assert.Equal(new[] { "first", "second" }, Rank("item", Entry("first", "Item"), Entry("second", "ITEM")));
    }
    [Theory]
    [InlineData("微信", "weixin")]
    [InlineData("微信", "wx")]
    [InlineData("Résumé", "resume")]
    [InlineData("ＰｏｗｅｒＳｈｅｌｌ", "powershell")]
    [InlineData("PowerShell", "ps")]
    [InlineData("Visual Studio", "visual-studio")]
    public void NamesHaveCachedRomanizationHumpsAndUnicodeFolding(string name, string query)
    { Assert.Equal(new[] { "hit" }, Rank(query, Entry("hit", name))); }
    [Fact]
    public void SensitivityControlsLooseHitsButNeverBlocksExactOrAliasPrefix()
    {
        var loose = Entry("loose", "axbyc");
        Assert.Single(LauncherOrder.Ranked([loose], "abc", SearchSensitivity.Low, 5, x => x.Profile, x => x.Signals));
        Assert.Empty(LauncherOrder.Ranked([loose], "abc", SearchSensitivity.High, 5, x => x.Profile, x => x.Signals));
        var exact = Entry("exact", "abc"); var alias = Entry("alias", "Other", alias: "abcdef");
        Assert.Equal(2, LauncherOrder.Ranked([exact, alias], "abc", SearchSensitivity.High, 5, x => x.Profile, x => x.Signals).Count);
    }
    [Fact]
    public void EmptyMalformedAndLongQueriesAreBoundedAndSensitivityPersists()
    {
        Assert.Empty(Rank("\u200b", Entry("a", "Any", alias: "a")));
        Assert.Empty(Rank(new string('a', 129), Entry("a", new string('a', 200))));
        _ = Rank("\ud800", Entry("a", "\ud800"));
        var preferences = new AppPreferences { MatchSensitivity = SearchSensitivity.High };
        Assert.Equal(SearchSensitivity.High, JsonSerializer.Deserialize<AppPreferences>(JsonSerializer.Serialize(preferences))!.MatchSensitivity);
        Assert.Equal(SearchSensitivity.Medium, JsonSerializer.Deserialize<AppPreferences>("{}")!.MatchSensitivity);
        Assert.NotNull((new AppPreferences { MatchSensitivity = (SearchSensitivity)99 }).Validate());
    }
    [Fact]
    public void RegisteredSourceKeywordsAndGateRemainSeparateFromIntentionalAliases()
    {
        var entry = new LauncherEntry("Actual executable", "Owned.exe", RegistrationNames: ["RegistryAlias"]);
        var preferences = new AppPreferences();
        Assert.True(entry.IsEnabled(preferences));
        Assert.False(entry.IsEnabled(preferences with { IncludeWindowsAppPaths = false }));
        Assert.False(entry.IsEnabled(preferences with { ApplicationsEnabled = false }));
        Assert.True((entry with { RegistrationNames = null }).IsEnabled(preferences with { IncludeWindowsAppPaths = false }));
        Assert.Single(LauncherOrder.Ranked([entry], "RegistryAlias", SearchSensitivity.High, 10, x => x.SearchFields, x => new(x.Name)));
        var renamed = entry with { RegistrationNames = ["Different"] };
        Assert.Empty(LauncherOrder.Ranked([renamed], "RegistryAlias", SearchSensitivity.High, 10, x => x.SearchFields, x => new(x.Name)));
    }
    [Fact]
    public void AlignmentReusesBoundedWorkspaceInsteadOfAllocatingCandidateSizedArrays()
    {
        var query = LauncherSearchText.Create("abc"); var candidate = LauncherSearchText.Create("A" + new string('x', 80) + "B" + new string('y', 80) + "C");
        for (var i = 0; i < 50; i++) _ = LauncherMatch.Match(query, candidate);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) Assert.NotNull(LauncherMatch.Match(query, candidate));
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 256000, "Alignment allocated fresh text-sized arrays per candidate.");
    }
    [Fact]
    public void LearnedTermsUseTheSameLatinAndDiacriticFoldWithoutDestroyingLegacyRows()
    {
        Assert.Equal("wei xin", LauncherUsage.NormalizeTerm(" 微信 "));
        Assert.Equal("cafe", LauncherUsage.NormalizeTerm("Ｃａｆé"));
        Assert.Equal("visual studio", LauncherUsage.NormalizeTerm(" Visual Studio "));
        var now = DateTimeOffset.UtcNow;
        var legacy = new LaunchVisit("old", 1, now, now.AddDays(1), ["微信", "Café"]);
        Assert.Equal(new[] { "wei xin", "cafe" }, LauncherUsage.Terms(legacy, now));
        Assert.Equal(new[] { "微信", "Café" }, legacy.SearchTerms);
        var next = LauncherUsage.Visit("old", legacy, "Visual Studio", now);
        Assert.Equal(new[] { "wei xin", "cafe", "visual studio" }, next.SearchTerms);
        Assert.Equal("learned", Rank("visual studio", Entry("plain", "Visual Studio app"), Entry("learned", "Visual Studio utility", terms: next.SearchTerms!.ToArray()))[0]);
    }
    [Fact]
    public void EmptyOrderingUsesAliasThenKindPriorityAndNaturalNumbers()
    {
        var placements = LauncherSections.Empty(new[] { new LauncherItem("ten", "Item 10", LauncherKind.Application),
            new LauncherItem("two", "Item 2", LauncherKind.Application), new LauncherItem("alias", "Z", LauncherKind.Application, HasAlias: true) },
            [], _ => 1, DateTimeOffset.UtcNow, suggestions: false);
        Assert.Equal(new[] { "alias", "two", "ten" }, placements.Select(x => x.Item.Id));
    }
    [Fact]
    public void RenamedRecordProfilesDoNotReuseStaleNamesOrExposeScripts()
    {
        var entry = new LauncherEntry("Old", "Demo.exe"); _ = entry.SearchFields;
        var renamed = entry with { Name = "New" };
        Assert.Equal("new", renamed.SearchFields.Title.Text); Assert.True(renamed.Score("new") > 0);
        var command = new SavedCommand(Guid.NewGuid(), "Safe name", "secret-token"); _ = command.SearchFields;
        Assert.Null(LauncherMatch.Match(LauncherSearchText.Create("secret-token"), command.SearchFields.Title));
        Assert.DoesNotContain("SearchFields", JsonSerializer.Serialize(command));
        Assert.Equal("new", (command with { Name = "New" }).SearchFields.Title.Text);
    }
}
