using Kikicast.Core;
namespace Kikicast.Core.Tests;

public class LauncherCustomizationTests
{
    [Fact]
    public void AliasAndVisibilityAreImmutableCaseInsensitiveAndKeepFavoriteReferences()
    {
        var original = new AppPreferences { FavoriteKeys = ["entry"] };
        var aliased = LauncherCustomization.Alias(original, "entry", "  工具  ");
        Assert.Empty(original.LauncherAliases); Assert.Equal("工具", aliased.LauncherAliases["entry"]);
        var hidden = LauncherCustomization.Visibility(aliased, "ENTRY", false);
        Assert.Single(hidden.HiddenEntryKeys); Assert.Single(LauncherCustomization.Visibility(hidden, "entry", false).HiddenEntryKeys);
        Assert.Equal(aliased.FavoriteKeys, hidden.FavoriteKeys); Assert.Equal(aliased.LauncherAliases, hidden.LauncherAliases);
        Assert.Empty(LauncherCustomization.Visibility(hidden, "entry", true).HiddenEntryKeys);
        Assert.Empty(LauncherCustomization.Alias(hidden, "ENTRY", " ").LauncherAliases);
    }
    [Theory]
    [InlineData(" terminal ", "ＴＥＲＭＩＮＡＬ", 2)]
    [InlineData("terminal", "term", 1)]
    [InlineData("iterm", "term", 0)]
    [InlineData("a", "", 0)]
    public void AliasPriorityIsOnlyExactOrPrefix(string alias, string query, int tier) => Assert.Equal(tier, LauncherCustomization.AliasTier(alias, query));
    [Fact]
    public void InvalidAliasAndHiddenMetadataAreRejected()
    {
        Assert.NotNull((new AppPreferences { LauncherAliases = new() { ["a"] = "bad\0value" } }).Validate());
        Assert.NotNull((new AppPreferences { LauncherAliases = new() { ["a"] = new string('a', 121) } }).Validate());
        Assert.NotNull((new AppPreferences { LauncherAliases = new() { ["a"] = "one", ["A"] = "two" } }).Validate());
        Assert.NotNull((new AppPreferences { HiddenEntryKeys = ["a", "A"] }).Validate());
        Assert.Null(new AppPreferences().Validate());
    }
    [Fact]
    public void DeletionClearsOnlyItsEntryReferences()
    {
        var preferences = new AppPreferences { FavoriteKeys = ["a", "b"], HiddenEntryKeys = ["a", "b"], LauncherAliases = new() { ["a"] = "x", ["b"] = "y" } };
        var next = LauncherCustomization.RemoveReferences(preferences, "A");
        Assert.Equal(new[] { "b" }, next.FavoriteKeys); Assert.Equal(new[] { "b" }, next.HiddenEntryKeys); Assert.Single(next.LauncherAliases);
        Assert.Equal(2, preferences.FavoriteKeys.Count);
    }
    [Fact]
    public void FallbackAndHistoryMenusNeverAdvertiseFavoriteOrHide()
    {
        var fallback = LauncherActions.For(new(false));
        Assert.DoesNotContain(fallback, x => x.Kind is LauncherActionKind.ToggleFavorite or LauncherActionKind.Hide or LauncherActionKind.Reveal);
        var history = LauncherActions.For(new(false, Value: "42", History: true));
        Assert.Contains(history, x => x.Kind == LauncherActionKind.DeleteHistory);
        Assert.DoesNotContain(history, x => x.Kind == LauncherActionKind.ToggleFavorite);
    }
    [Fact]
    public void ActionCapabilitiesAndFavoriteDirectionMatchExactly()
    {
        var actions = LauncherActions.For(new(true, Favorite: true, MoveDown: true, Path: "owned", CanHide: true, HasLearning: true));
        Assert.DoesNotContain(actions, x => x.Kind == LauncherActionKind.MoveFavoriteUp);
        Assert.Contains(actions, x => x.Kind == LauncherActionKind.MoveFavoriteDown);
        Assert.Contains(actions, x => x.Kind == LauncherActionKind.Reveal && x.Hint == "Ctrl+Enter");
        Assert.Contains(actions, x => x.Kind == LauncherActionKind.ResetLearning);
        Assert.Equal(actions.Count, actions.Select(x => x.Kind).Distinct().Count());
    }
    [Fact]
    public void HistoryUsesConsecutiveDedupAndResetDoesNotDeleteCalculations()
    {
        var now = DateTimeOffset.UtcNow;
        var history = new LocalHistory().RecordCalculation("1+1", "2", now).RecordCalculation("2+2", "4", now).RecordCalculation("1+1", "2", now);
        Assert.Equal(3, history.Calculations.Count);
        history = history.RecordLaunch("app", now, "a").RecordLaunch("other", now);
        Assert.Single(history.ResetLearning("APP").Launches); Assert.Equal(3, history.ResetLearning().Calculations.Count);
        Assert.Empty(history.ResetLearning().Launches);
        Assert.Equal(2, history.RemoveCalculation(history.Calculations[0]).Calculations.Count);
    }
}
