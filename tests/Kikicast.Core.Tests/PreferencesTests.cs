using Kikicast.Core;

namespace Kikicast.Core.Tests;

public class PreferencesTests
{
    [Theory]
    [InlineData(17, KeySide.Any, 162, true)]
    [InlineData(17, KeySide.Left, 163, false)]
    [InlineData(17, KeySide.Right, 163, true)]
    [InlineData(18, KeySide.Any, 164, true)]
    [InlineData(16, KeySide.Any, 161, true)]
    [InlineData(17, KeySide.Any, 160, false)]
    public void SideFiltering(int key, KeySide side, int physical, bool expected) =>
        Assert.Equal(expected, new HotKeyBinding(BindingKind.DoubleTap, key, Side: side).AcceptsModifier(physical));

    [Theory]
    [InlineData(65, 0, false)][InlineData(65, 4, false)][InlineData(65, 2, true)]
    [InlineData(32, 1, true)][InlineData(112, 0, true)][InlineData(162, 2, false)]
    [InlineData(65, 16, false)][InlineData(0, 2, false)]
    public void ComboValidation(int key, uint modifiers, bool valid) =>
        Assert.Equal(valid, new HotKeyBinding(BindingKind.Combo, key, modifiers).Validate() == null);

    [Fact]
    public void RejectDuplicateFallbackAndFutureVersion()
    {
        var preferences = new AppPreferences { PaletteBinding = new(BindingKind.Combo, 32, 1), AltSpaceFallback = true };
        Assert.NotNull(preferences.Validate());
        Assert.Null((preferences with { AltSpaceFallback = false }).Validate());
        Assert.NotNull((new AppPreferences { Version = 2 }).Validate());
    }

    [Fact]
    public void HistoryIsBoundedAndDoesNotMutateOldSnapshot()
    {
        var first = new LocalHistory();
        var time = DateTimeOffset.UnixEpoch;
        var second = first.RecordLaunch("C:\\Apps\\One.lnk", time);
        var third = second.RecordLaunch("c:\\apps\\one.lnk", time);
        Assert.Empty(first.Launches);
        Assert.Single(third.Launches);
        Assert.Equal(2, third.Launches[0].Count);
        Assert.Equal(100.5, third.Frequency("C:\\Apps\\One.lnk", time.AddDays(10)), 6);
        var calculations = first;
        for (var i = 0; i < 220; i++) calculations = calculations.RecordCalculation($"{i}+1", $"{i+1}", time);
        Assert.Equal(200, calculations.Calculations.Count);
        Assert.Equal("219+1", calculations.Calculations[0].Expression);
        Assert.Same(calculations, calculations.RecordCalculation("219+1", "220", time));
    }
}
