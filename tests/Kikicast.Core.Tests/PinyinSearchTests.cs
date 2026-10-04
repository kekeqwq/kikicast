using Kikicast.Core;

namespace Kikicast.Core.Tests;

public class PinyinSearchTests
{
    [Theory]
    [InlineData("微信", "weixin")]
    [InlineData("微信", "wx")]
    [InlineData("微信", "wei xin")]
    [InlineData("微信", "wexn")]
    [InlineData("记事本", "jisben")]
    [InlineData("记事本", "jsb")]
    [InlineData("计算器", "jisuanqi")]
    [InlineData("计算器", "jsq")]
    [InlineData("微信", "ＷＥＩＸＩＮ")]
    public void MatchesPinyinAndInitials(string name, string query)
        => Assert.True(new LauncherEntry(name, "fixture.lnk").Score(query) >= 0);

    [Fact]
    public void MalformedClipboardUnicodeDoesNotCrashSearch()
    {
        Assert.Equal(-1, new LauncherEntry("微信", "fixture.lnk").Score("\ud800"));
        Assert.True(new LauncherEntry("\ud800微信", "fixture.lnk").Score("wx") >= 0);
    }

    [Fact]
    public void WindowCommandsUseSamePinyinMatcher()
    {
        var command = WindowGeometry.Commands.Single(x => x.Action == WindowAction.Center);
        Assert.True(command.Score("ckjz") >= 0);
        Assert.True(command.Score("chuangkoujuzhong") >= 0);
        Assert.True(command.Score("center") >= 0);
        Assert.Equal(-1, command.Score("xyzxyzxyz"));
    }

    [Fact]
    public void DefaultIsOnlyDoubleCtrlAndEverythingCanBeUnbound()
    {
        var preferences = new AppPreferences();
        var bindings = BindingCatalog.Build(preferences, false);
        Assert.Single(bindings);
        Assert.Equal(new HotKeyBinding(), bindings[0].Binding);
        Assert.Empty(preferences.WindowBindings);
        Assert.False(preferences.AltSpaceFallback);
        var cleared = preferences with { PaletteBinding = null };
        Assert.Null(cleared.Validate());
        Assert.Empty(BindingCatalog.Build(cleared));
    }
}
