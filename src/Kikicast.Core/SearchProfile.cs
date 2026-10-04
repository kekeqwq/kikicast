using System.Text;
using TinyPinyin;

namespace Kikicast.Core;

// Build once at index time; no transliteration work per keystroke.
public sealed record SearchProfile(string Title, string Pinyin, string Initials)
{
    public static SearchProfile Create(string name)
    {
        var text = Normalize(name);
        if (!text.Any(PinyinHelper.IsChinese)) return new(text, "", "");
        var pinyin = new StringBuilder();
        var initials = new StringBuilder();
        foreach (var c in text)
        {
            var syllable = PinyinHelper.GetPinyin(c).ToLowerInvariant();
            pinyin.Append(syllable);
            initials.Append(PinyinHelper.IsChinese(c) ? syllable[0].ToString() : syllable);
        }
        return new(text, pinyin.ToString(), initials.ToString());
    }

    public static string Normalize(string text)
    {
        try { text = text.Normalize(NormalizationForm.FormKC); }
        catch (ArgumentException) { /* Malformed UTF-16 clipboard/shortcut names must not crash search. */ }
        return string.Concat(text.ToLowerInvariant().Where(c => !char.IsWhiteSpace(c)));
    }

    public int Score(string query)
    {
        query = Normalize(query);
        if (query.Length == 0) return 0;
        var direct = Match(Title, query);
        var romanized = Pinyin.Length == 0 ? -1 : Match(Pinyin, query);
        var initials = Initials.Length == 0 ? -1 : Match(Initials, query);
        return Math.Max(direct, Math.Max(romanized < 0 ? -1 : Math.Max(0, romanized - 100),
            initials < 0 ? -1 : Math.Max(0, initials - 200)));
    }

    private static int Match(string text, string query)
    {
        if (text == query) return 10000;
        if (text.StartsWith(query, StringComparison.Ordinal)) return 5000 - Math.Min(1000, text.Length);
        var index = text.IndexOf(query, StringComparison.Ordinal);
        if (index >= 0) return 2000 - Math.Min(1000, index);
        var cursor = 0; var score = 0;
        foreach (var c in query)
        {
            var next = text.IndexOf(c, cursor);
            if (next < 0) return -1;
            score += next == cursor ? 4 : 1;
            cursor = next + 1;
        }
        return score;
    }
}
