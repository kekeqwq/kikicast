using Kikicast.Core;

namespace Kikicast.Core.Tests;

public class LauncherSectionsTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(200);
    private static LauncherItem App(string id, bool hotKey = false) => new(id, id, LauncherKind.Application, hotKey);

    [Fact]
    public void SuggestionsAreAtMostFiveDistinctRowsAndExcludeFavoritesAndBoundUsedItems()
    {
        var items = Enumerable.Range(0, 10).Select(i => App("app" + i, i == 1)).ToList();
        var rows = LauncherSections.Empty(items, ["app0", "missing"], id => 1000 - int.Parse(id[3..]), Now);
        Assert.Equal("app0", rows[0].Item.Id);
        Assert.Equal("Favorites", rows[0].Section);
        Assert.Equal("Ctrl+1", rows[0].FavoriteChord);
        Assert.Equal(5, rows.Count(x => x.Section == "Suggestions"));
        Assert.DoesNotContain(rows, x => x.Item.Id == "app1" && x.Section == "Suggestions");
        Assert.Equal(10, rows.Count);
        Assert.Equal(10, rows.Select(x => x.Item.Id).Distinct().Count());
    }

    [Fact]
    public void FreshAppsPrecedeUsageButDiscoveryIsNotAnInstallTimestamp()
    {
        var items = new[] { App("used"), App("discovered"), App("fresh-bound", true) with { InstalledAt = Now.AddMinutes(-1) },
            App("old") with { InstalledAt = Now.AddMinutes(-5) }, App("future") with { InstalledAt = Now.AddMinutes(1) } };
        var rows = LauncherSections.Empty(items, [], id => id == "used" ? 101 : 1, Now);
        Assert.Equal(new[] { "fresh-bound", "used" }, rows.Where(x => x.Section == "Suggestions").Select(x => x.Item.Id));
    }

    [Fact]
    public void FreshLimitIsTwoAndFillersUseOnlyAvailableUnboundCommands()
    {
        var items = Enumerable.Range(0, 4).Select(i => App("new" + i) with { InstalledAt = Now }).Concat(new[]
        {
            new LauncherItem("clipboard", "Clipboard", LauncherKind.Command, SuggestionPriority: 80),
            new LauncherItem("files", "Files", LauncherKind.Command, SuggestionPriority: 70),
            new LauncherItem("self", "Settings", LauncherKind.Command, CanSuggest: false, SuggestionPriority: 100),
            new LauncherItem("bound", "Bound", LauncherKind.Command, HasHotKey: true, SuggestionPriority: 100)
        });
        var suggestions = LauncherSections.Empty(items, [], _ => 1, Now).Where(x => x.Section == "Suggestions").ToList();
        Assert.Equal(new[] { "new0", "new1", "clipboard", "files" }, suggestions.Select(x => x.Item.Id));
    }

    [Fact]
    public void SwitchOffKeepsFavoritesAndGroupOrderWithoutDeletingMissingKeys()
    {
        var favorites = new List<string> { "missing", "command" };
        var items = new[] { new LauncherItem("command", "Command", LauncherKind.Command),
            new LauncherItem("window", "Window", LauncherKind.WindowCommand), App("app") };
        var rows = LauncherSections.Empty(items, favorites, _ => 101, Now, suggestions: false);
        Assert.Equal(new[] { "Favorites", "Applications", "Window management" }, rows.Select(x => x.Section));
        Assert.Equal(new[] { "missing", "command" }, favorites);
    }

    [Fact]
    public void SlotsSkipMissingEntriesAndOnlyFirstTenHaveChords()
    {
        var items = Enumerable.Range(0, 12).Select(i => App("a" + i)).ToList();
        var keys = new[] { "missing" }.Concat(items.Select(x => x.Id)).ToList();
        var rows = LauncherSections.Empty(items, keys, _ => 1, Now);
        Assert.Equal("Ctrl+1", rows[0].FavoriteChord);
        Assert.Equal("Ctrl+0", rows[9].FavoriteChord);
        Assert.Null(rows[10].FavoriteChord);
    }

    [Fact]
    public void ReorderExchangesVisiblePositionsAndKeepsInvisibleSlots()
    {
        var keys = new List<string> { "a", "missing", "b", "disabled", "c" };
        Assert.Equal(new[] { "b", "missing", "a", "disabled", "c" }, LauncherSections.MoveFavorite(keys, ["a", "b", "c"], "b", -1));
        Assert.Equal(keys, LauncherSections.MoveFavorite(keys, ["a", "b", "c"], "a", -1));
        Assert.Equal(new[] { "a", "missing", "b", "disabled", "c" }, keys);
    }

    [Fact]
    public void ToggleAndDedupRespectWindowsPathCase()
    {
        Assert.Empty(LauncherSections.ToggleFavorite(["APP:X"], "app:x"));
        Assert.Equal(new[] { "a", "b" }, LauncherSections.ToggleFavorite(["a"], "b"));
        Assert.Single(LauncherSections.Empty([App("x"), App("X")], [], _ => 1, Now));
        Assert.Equal(LauncherSections.ApplicationId("c:\\Apps\\Foo.lnk"), LauncherSections.ApplicationId("C:\\apps\\foo.lnk"));
    }

    [Fact]
    public void InvalidFavoritesAreRejectedWithoutChangingHotkeyDefaults()
    {
        Assert.NotNull(new AppPreferences { FavoriteKeys = ["x", "X"] }.Validate());
        Assert.NotNull(new AppPreferences { FavoriteKeys = [""] }.Validate());
        Assert.Null(new AppPreferences { FavoriteKeys = ["missing-entry"] }.Validate());
        Assert.Single(BindingCatalog.Build(new AppPreferences { FavoriteKeys = ["x"] }));
    }
}
