using Kikicast.Core;

namespace Kikicast.Core.Tests;

public class CurrencyTests
{
    private static readonly CurrencySnapshot Rates = new("USD", new() { ["USD"] = 1, ["CNY"] = 7, ["EUR"] = .9 }, new(2026, 10, 2), DateTimeOffset.UnixEpoch);
    [Theory]
    [InlineData("1 usd to cny", "7.00")]
    [InlineData("2 USD in CNY", "14.00")]
    [InlineData("usd -> cny", "7.00")]
    [InlineData("(2+3) usd to cny", "35.00")]
    [InlineData("-2 usd → cny", "-14.00")]
    [InlineData("1e2 usd to cny", "700.00")]
    [InlineData("0 cny to usd", "0.00")]
    public void ConvertsNumbersExpressionsAndImplicitOne(string query, string expected) => Assert.Equal(expected, CurrencyConversion.Parse(query)?.Answer(Rates));
    [Theory]
    [InlineData("abc to usd")][InlineData("1,5 usd to cny")][InlineData("1/0 usd to cny")]
    [InlineData("1 usd to kg")][InlineData("1e999 usd to cny")][InlineData("1 usd to cny ; script")]
    public void InvalidInputDoesNotMakeAnAnswer(string query) => Assert.Null(CurrencyConversion.Parse(query));
    [Fact]
    public void MissingRatesStayUnavailableAndSameCurrencyNeedsNoNetwork()
    {
        Assert.Null(CurrencyConversion.Parse("usd to cny")!.Answer(null));
        Assert.Null(CurrencyConversion.Parse("usd to jpy")!.Answer(Rates));
        Assert.Equal("1.00", CurrencyConversion.Parse("usd to usd")!.Answer(null));
        Assert.Equal(7 / .9, Rates.Convert(1, "EUR", "CNY")!.Value, 10);
        Assert.False((Rates with { Rates = new() { ["USD"] = 1, ["CNY"] = double.NaN } }).IsValid);
        Assert.False((Rates with { Rates = null! }).IsValid);
        Assert.Equal("6.70", CurrencyQuery.FormatMoney(6.7046));
        Assert.Equal("0.001234", CurrencyQuery.FormatMoney(.001234));
        Assert.Equal("0.00", CurrencyQuery.FormatMoney(-1e-12));
    }
}
