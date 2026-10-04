using System.Globalization;

namespace Kikicast.Core;

public static class Calculator
{
    public static string? Evaluate(string text)
    {
        if (text.Length is 0 or > 256 || !text.Any(c => "+-*/^()×÷".Contains(c))) return null;
        try
        {
            var parser = new Parser(text);
            var value = parser.Expression();
            parser.Space();
            return parser.End && double.IsFinite(value)
                ? value.ToString("G15", CultureInfo.InvariantCulture) : null;
        }
        catch (FormatException) { return null; }
    }

    private sealed class Parser(string text)
    {
        private int at;
        public bool End => at == text.Length;
        public void Space() { while (!End && char.IsWhiteSpace(text[at])) at++; }
        private bool Take(char c) { Space(); if (End || text[at] != c) return false; at++; return true; }
        public double Expression()
        {
            var result = Product();
            while (true)
            {
                if (Take('+')) result += Product();
                else if (Take('-')) result -= Product();
                else return result;
            }
        }
        private double Product()
        {
            var result = Unary();
            while (true)
            {
                if (Take('*') || Take('×')) result *= Unary();
                else if (Take('/') || Take('÷')) result /= Unary();
                else return result;
            }
        }
        private double Unary()
        {
            if (Take('+')) return Unary();
            if (Take('-')) return -Unary();
            var value = Atom();
            if (Take('^')) value = Math.Pow(value, Unary());
            return value;
        }
        private double Atom()
        {
            if (Take('('))
            {
                var result = Expression();
                if (!Take(')')) throw new FormatException();
                return result;
            }
            Space();
            var start = at;
            while (!End && (char.IsAsciiDigit(text[at]) || text[at] == '.')) at++;
            if (!End && (text[at] == 'e' || text[at] == 'E'))
            {
                at++;
                if (!End && (text[at] == '+' || text[at] == '-')) at++;
                while (!End && char.IsAsciiDigit(text[at])) at++;
            }
            if (start == at || !double.TryParse(text[start..at], NumberStyles.Float,
                CultureInfo.InvariantCulture, out var number)) throw new FormatException();
            return number;
        }
    }
}
