using Kikicast.Core;

namespace Kikicast.Core.Tests;

public class CoreTests
{
    [Theory]
    [InlineData("1+2*3", "7")]
    [InlineData("(1+2)*3", "9")]
    [InlineData("-2^2", "-4")]
    [InlineData("2^3^2", "512")]
    [InlineData("1e2+5", "105")]
    [InlineData("10 / 4", "2.5")]
    [InlineData("2×3", "6")]
    public void Arithmetic(string text, string expected) => Assert.Equal(expected, Calculator.Evaluate(text));

    [Theory]
    [InlineData("chrome")][InlineData("123")][InlineData("(1+2")]
    [InlineData("1/0")][InlineData("1e400+1")][InlineData("1+System.Exit()")]
    [InlineData("1 2+3")][InlineData("2**3")]
    public void InvalidArithmetic(string text) => Assert.Null(Calculator.Evaluate(text));

    [Fact]
    public void DoubleTapFiresOnSecondRelease()
    {
        var detector = new DoubleTapRecognizer();
        Assert.False(detector.Press(162, 0));
        Assert.False(detector.Release(162, .05));
        Assert.False(detector.Press(162, .15));
        Assert.True(detector.Release(162, .20));
        Assert.False(detector.Release(162, .21));
    }

    [Fact]
    public void BoundariesAndTimeout()
    {
        var detector = new DoubleTapRecognizer();
        detector.Press(162, 0); detector.Release(162, .25);
        detector.Press(162, .55); Assert.True(detector.Release(162, .75));
        detector.Press(162, 1); detector.Release(162, 1.251);
        detector.Press(162, 1.3); Assert.False(detector.Release(162, 1.35));
    }

    [Fact]
    public void CancellationAndSides()
    {
        var detector = new DoubleTapRecognizer();
        detector.Press(162, 0); detector.Release(162, .05); detector.Cancel();
        detector.Press(162, .1); Assert.False(detector.Release(162, .15));
        detector.Press(163, .2); Assert.False(detector.Release(163, .25));
        detector.Press(163, .3); Assert.True(detector.Release(163, .35));
    }

    [Fact]
    public void RankingIsDeterministic()
    {
        var exact = new LauncherEntry("Code", "a");
        var prefix = new LauncherEntry("Code Editor", "b");
        var fuzzy = new LauncherEntry("Cool Desktop Environment", "c");
        Assert.True(exact.Score("code") > prefix.Score("code"));
        Assert.True(prefix.Score("code") > fuzzy.Score("code"));
        Assert.Equal(-1, exact.Score("xyz"));
    }
}
