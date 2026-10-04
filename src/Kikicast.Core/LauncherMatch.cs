using System.Globalization;
using System.Text;
using TinyPinyin;

namespace Kikicast.Core;

// Adapted from Tinycast LauncherMatch.swift at fa1c2bb (AGPL-3.0); UTF-16 alignment.
public enum SearchSensitivity { Low, Medium, High }
public sealed record LauncherSearchText(string Text, IReadOnlySet<int> Humps)
{
    public static string Fold(string raw)
    {
        try { raw = raw.Normalize(NormalizationForm.FormKC).Normalize(NormalizationForm.FormD); }
        catch (ArgumentException) { /* Malformed UTF-16 is a searchable string, not a crash. */ }
        var text = new StringBuilder(raw.Length);
        foreach (var c in raw)
            if (char.GetUnicodeCategory(c) is not (UnicodeCategory.Format or UnicodeCategory.NonSpacingMark)) text.Append(char.ToLowerInvariant(c));
        try { return text.ToString().Normalize(NormalizationForm.FormC); }
        catch (ArgumentException) { return text.ToString(); }
    }
    public static LauncherSearchText Create(string raw, bool transliterated = false)
    {
        raw = raw.Length <= 512 ? raw : raw[..512];
        var humps = new HashSet<int>();
        if (transliterated && raw.Any(PinyinHelper.IsChinese))
        {
            var latin = new StringBuilder();
            foreach (var c in raw)
                if (PinyinHelper.IsChinese(c)) latin.Append(' ').Append(PinyinHelper.GetPinyin(c)).Append(' ');
                else latin.Append(c);
            return new(Fold(string.Join(' ', latin.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))), humps);
        }
        if (raw.All(char.IsAscii))
            for (var i = 1; i < raw.Length; i++)
            {
                if (char.IsAsciiLetterLower(raw[i - 1]) && char.IsAsciiLetterUpper(raw[i]) || char.IsAsciiDigit(raw[i - 1]) && char.IsAsciiLetter(raw[i])) humps.Add(i);
                else if (i > 1 && char.IsAsciiLetterUpper(raw[i - 2]) && char.IsAsciiLetterUpper(raw[i - 1]) && char.IsAsciiLetterLower(raw[i])) humps.Add(i - 1);
            }
        return new(Fold(raw), humps);
    }
}
public sealed record LauncherSearchProfile(LauncherSearchText Title, IReadOnlyList<LauncherSearchText> Alternates, LauncherSearchText? Subtitle, IReadOnlyList<LauncherSearchText> Keywords)
{
    public static LauncherSearchProfile Create(string title, string? subtitle = null, params string[] keywords)
    {
        var latin = LauncherSearchText.Create(title, true); var typed = LauncherSearchText.Create(title);
        var alternates = new List<LauncherSearchText>();
        if (typed.Text != latin.Text) alternates.Add(typed);
        if (title.Any(PinyinHelper.IsChinese)) alternates.Add(LauncherSearchText.Create(SearchProfile.Create(title).Initials));
        return new(latin, alternates,
            subtitle == null ? null : LauncherSearchText.Create(subtitle, true), keywords.Select(x => LauncherSearchText.Create(x, true)).ToArray());
    }
}
public sealed record LauncherMatchOutcome(int Value, int Skipped = 0, bool Exact = false)
{
    public bool Accepted(SearchSensitivity sensitivity, int queryLength) => Exact || (sensitivity switch
    {
        SearchSensitivity.Low => true,
        SearchSensitivity.Medium => Value >= 1.5 * (queryLength - Skipped - 2) + 4,
        SearchSensitivity.High => Value > 2 * (queryLength - Skipped),
        _ => false
    });
}
public static class LauncherMatch
{
    public static bool IsSeparator(char c) => c is '\t' or '\n' or ' ' or '(' or ')' or '-' or '.' or '/' or '[' or ']';
    public static LauncherMatchOutcome? Match(LauncherSearchText query, LauncherSearchText target)
    {
        var q = query.Text; var t = target.Text;
        if (q.Length == 0 || q.Length > 128) return null;
        if (q == t) return new(int.MaxValue, Exact: true);
        var letters = 0;
        foreach (var character in q) if (!IsSeparator(character)) letters++;
        if (letters > t.Length) return null;
        if (letters > 2)
        {
            var position = 0;
            foreach (var c in q)
            {
                if (IsSeparator(c)) { if (position < t.Length && IsSeparator(t[position])) position++; continue; }
                var found = t.IndexOf(c, position); if (found < 0) return null; position = found + 1;
            }
        }
        var previous = System.Buffers.ArrayPool<int>.Shared.Rent(t.Length);
        var current = System.Buffers.ArrayPool<int>.Shared.Rent(t.Length);
        try
        {
            Array.Fill(previous, int.MinValue, 0, t.Length); Array.Fill(current, int.MinValue, 0, t.Length);
            var anchor = -1; var rowStart = 0; var rowEnd = 0; var matched = 0; var skipped = 0;
            foreach (var unit in q)
            {
                var separator = IsSeparator(unit); var lower = anchor + 1;
                var upper = Math.Min(t.Length, t.Length - (letters - 1 - matched)); var first = -1; var gapBest = int.MinValue;
                for (var column = lower; column < upper; column++)
                {
                    if (anchor >= 0 && column - 2 >= anchor) gapBest = Math.Max(gapBest, previous[column - 2]);
                    var same = t[column] == unit; var bothSeparators = !same && separator && IsSeparator(t[column]);
                    if (!same && !bothSeparators) { current[column] = int.MinValue; continue; }
                    var points = bothSeparators ? 1 : anchor < 0 && column == 0 ? 4
                        : column > 0 && IsSeparator(t[column - 1]) && !IsSeparator(t[column]) || target.Humps.Contains(column) ? 3 : 2;
                    if (anchor < 0) current[column] = points;
                    else
                    {
                        var adjacent = previous[column - 1]; var best = adjacent == int.MinValue ? int.MinValue : adjacent + points;
                        if (gapBest != int.MinValue) best = Math.Max(best, gapBest + points - 1);
                        current[column] = best;
                    }
                    if (first < 0) first = column;
                }
                if (first >= 0) { anchor = first; matched++; rowStart = lower; rowEnd = upper; (previous, current) = (current, previous); }
                else if (separator) skipped++; else return null;
            }
            if (anchor < 0) return null;
            var score = int.MinValue;
            for (var i = rowStart; i < rowEnd; i++) score = Math.Max(score, previous[i]);
            return score == int.MinValue ? null : new(score, skipped);
        }
        finally { System.Buffers.ArrayPool<int>.Shared.Return(previous); System.Buffers.ArrayPool<int>.Shared.Return(current); }
    }
}
