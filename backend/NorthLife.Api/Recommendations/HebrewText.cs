using System.Globalization;
using System.Text;

namespace NorthLife.Api.Recommendations;

/// <summary>
/// Light Hebrew text processing for event descriptions.
/// <list type="number">
/// <item>Normalise: drop niqqud and cantillation marks, write final letters in their regular form
/// (ך→כ, ם→מ, ן→נ, ף→פ, ץ→צ) so "שלום" and "שלומות" share a stem, remove geresh, gershayim and
/// punctuation, and lower-case Latin letters.</item>
/// <item>Tokenise on anything that is not a letter or digit, and drop stop words.</item>
/// <item>Strip attached prefixes (ו, ה, ב, ל, מ, ש, כ; up to two of them) only when the rest also appears on
/// its own somewhere in the corpus. A fixed rule would break words that merely start with those
/// letters: "מוזיקה" is not מ + "וזיקה", and since "וזיקה" never occurs alone it is left intact, while
/// "והופעה" becomes "הופעה".</item>
/// </list>
/// </summary>
public static class HebrewText
{
    public const string Prefixes = "והבלמשכ";
    public const int MinimumStemLength = 2;

    private static readonly HashSet<string> StopWords = new(
        [
            "של", "את", "על", "עם", "או", "גם", "כל", "זה", "זו", "זאת", "הוא", "היא", "הם", "הן", "אני", "אתה", "את",
            "אנחנו", "אתם", "יש", "אינ", "לא", "כנ", "מה", "מי", "איכ", "כמו", "אחרי", "לפני", "בינ", "עד", "רק",
            "אבל", "כי", "אמ", "אז", "יותר", "מאוד", "שמ", "פה", "כאנ", "אל", "אשר", "היה", "היו", "להיות", "אותו",
            "אותה", "שלו", "שלה", "שלהמ", "לכל", "בכל", "כדי", "תוכ", "ללא", "בלי", "עוד", "כבר",
            // Boilerplate of event descriptions: present almost everywhere, so they carry no signal.
            "אירוע", "האירוע", "מתקיימ", "מומלצ", "להגיע", "דקות", "תחילת", "מחיר", "כרטיס", "שח", "הכניסה", "חופשית",
        ],
        StringComparer.Ordinal);

    public static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text.Normalize(NormalizationForm.FormD))
        {
            // Niqqud and cantillation (U+0591-U+05C7) and other combining marks.
            if (character is >= '֑' and <= 'ׇ' && character is not '־') continue;
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(character switch
            {
                'ך' => 'כ',
                'ם' => 'מ',
                'ן' => 'נ',
                'ף' => 'פ',
                'ץ' => 'צ',
                '־' => ' ', // maqaf joins words; split them
                '׳' or '״' or '\'' or '"' => '\0',
                _ => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ',
            });
        }

        return builder.Replace("\0", string.Empty).ToString();
    }

    /// <summary>Normalised tokens of at least two characters, without stop words.</summary>
    public static IEnumerable<string> Tokens(string text) =>
        Normalize(text)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= 2 && !StopWords.Contains(token));

    /// <summary>
    /// Strips up to two attached prefixes when what remains is a word of the corpus vocabulary,
    /// preferring the longest remainder that qualifies (the least stripping).
    /// </summary>
    public static string Stem(string token, IReadOnlySet<string> vocabulary)
    {
        for (var strip = 1; strip <= 2 && token.Length - strip >= MinimumStemLength; strip++)
        {
            if (!Prefixes.Contains(token[strip - 1])) return token;
            var rest = token[strip..];
            if (vocabulary.Contains(rest)) return rest;
        }

        return token;
    }
}
