using System.Globalization;
using System.Text.RegularExpressions;

namespace Kikicast.Core;

public sealed record CurrencySnapshot(string Base, Dictionary<string, double> Rates, DateOnly RateDate, DateTimeOffset FetchedAt)
{
    public bool IsValid => Base is { Length: 3 } && Base.All(char.IsAsciiLetterUpper) && Rates is { Count: > 0 and <= 300 }
        && Rates.All(x => x.Key.Length == 3 && x.Key.All(char.IsAsciiLetterUpper) && double.IsFinite(x.Value) && x.Value > 0)
        && Rates.TryGetValue(Base, out var basis) && basis == 1;
    public double? Convert(double amount, string from, string to)
    {
        if (from == to) return amount;
        if (!Rates.TryGetValue(from, out var a) || !Rates.TryGetValue(to, out var b)) return null;
        var result = amount / a * b;
        return double.IsFinite(result) ? result : null;
    }
}
public sealed record CurrencyQuery(double Amount, string From, string To)
{
    public string InputLabel => Amount.ToString("G15", CultureInfo.InvariantCulture) + " " + From;
    public string? Answer(CurrencySnapshot? rates)
    {
        var result = From == To ? Amount : rates?.Convert(Amount, From, To);
        return result is { } value ? FormatMoney(value) : null;
    }
    // Tinycast CalcFormatter.currency: cents normally, wider precision below a cent.
    public static string FormatMoney(double value)
    {
        var magnitude = Math.Abs(value);
        if (magnitude < 1e-9) return "0.00";
        return magnitude >= .01 ? value.ToString("F2", CultureInfo.InvariantCulture)
            : value.ToString("F" + (3 - (int)Math.Floor(Math.Log10(magnitude))), CultureInfo.InvariantCulture).TrimEnd('0');
    }
}

public static partial class CurrencyConversion
{
    private static readonly HashSet<string> Codes = "USD EUR GBP JPY CNY AUD CAD CHF HKD NZD SEK NOK DKK INR KRW SGD BRL MXN ZAR TRY ILS AED SAR THB IDR MYR PHP PLN CZK HUF RON BGN ISK RUB UAH TWD VND ARS CLP COP PKR BDT EGP NPR".Split(' ').ToHashSet(StringComparer.OrdinalIgnoreCase);
    // ISO codes, explicit targets: never guess a user's regional currency or ambiguous symbol.
    [GeneratedRegex(@"^\s*(?<amount>.*?)\s*(?<from>[a-z]{3})\s*(?:to|in|->|→)\s*(?<to>[a-z]{3})\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex ConversionPattern();
    public static CurrencyQuery? Parse(string text)
    {
        if (text.Length is 0 or > 256) return null;
        var match = ConversionPattern().Match(text);
        if (!match.Success) return null;
        if (!Codes.Contains(match.Groups["from"].Value) || !Codes.Contains(match.Groups["to"].Value)) return null;
        var expression = match.Groups["amount"].Value.Trim();
        var numeric = expression.Length == 0 ? "1" : Calculator.Evaluate(expression) ?? expression;
        if (!double.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount) || !double.IsFinite(amount)) return null;
        return new(amount, match.Groups["from"].Value.ToUpperInvariant(), match.Groups["to"].Value.ToUpperInvariant());
    }
}
