using System.Text.RegularExpressions;
using NorthLife.Api.Recommendations;

namespace NorthLife.Api.Moderation;

/// <summary>
/// Text checks for event submissions: banned words and contact details that belong on the event
/// page's own buttons, not in its text.
/// </summary>
public static class ContentScanner
{
    public const string Link = "link";
    public const string Email = "email";
    public const string Phone = "phone";

    // NonBacktracking gives linear-time matching, so no input (descriptions reach 5,000 characters)
    // can trigger catastrophic backtracking. It does not support lookarounds; digit boundaries of
    // phone numbers are therefore checked in code.
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;

    private static readonly Regex EmailPattern = new(@"[a-z0-9._%+-]+@[a-z0-9-]+(\.[a-z0-9-]+)+", Options);

    private static readonly Regex LinkPattern = new(
        @"(https?://|www\.)\S+|\b[a-z0-9-]+(\.[a-z0-9-]+)*\.(com|net|org|il|io|me|ly|app|info|biz|link|site|shop|online|tv)\b",
        Options);

    // Israeli numbers: mobile 05X and 07X (10 digits), landline 02/03/04/08/09 (9 digits), with 0 or
    // +972 in front, and 1-700 / 1-800 service numbers. Up to two spaces, dots, dashes or brackets may
    // separate the digits.
    private static readonly Regex PhonePattern = new(
        @"(\+?972|0)[-. ()]{0,2}(5[0-9]|7[0-9]|[23489])([-. ()]{0,2}[0-9]){7}|1[-. ]{0,2}[78]00([-. ]{0,2}[0-9]){6}",
        Options);

    /// <summary>
    /// The first banned entry found in the texts, or null. Text and entries are compared after
    /// <see cref="HebrewText.Normalize"/> (no niqqud, regular letter forms, lower-case Latin), and a
    /// word also matches with up to two attached prefixes ("והימורים" matches "הימורים"). Prefixes are
    /// stripped only when the rest is itself a banned word, so ordinary words are never split.
    /// An entry of several words matches those words in a row. O(tokens × entries).
    /// </summary>
    public static string? FindBannedWord(IEnumerable<string> texts, IReadOnlyList<string> bannedWords)
    {
        var entries = bannedWords
            .Select(entry => (Entry: entry, Tokens: Words(entry)))
            .Where(entry => entry.Tokens.Length > 0)
            .ToList();
        if (entries.Count == 0) return null;

        var vocabulary = entries.SelectMany(entry => entry.Tokens).ToHashSet(StringComparer.Ordinal);
        foreach (var text in texts)
        {
            var tokens = Words(text);
            for (var index = 0; index < tokens.Length; index++)
            {
                var token = tokens[index];
                var stem = HebrewText.Stem(token, vocabulary);
                foreach (var (entry, words) in entries)
                {
                    if (index + words.Length > tokens.Length) continue;
                    if (words[0] != token && words[0] != stem) continue;

                    var rest = true;
                    for (var offset = 1; offset < words.Length && rest; offset++)
                    {
                        rest = words[offset] == tokens[index + offset];
                    }

                    if (rest) return entry;
                }
            }
        }

        return null;
    }

    /// <summary>The kinds of contact details found ("link", "email", "phone"), in that order.</summary>
    public static IReadOnlyList<string> FindContactDetails(IEnumerable<string> texts)
    {
        bool link = false, email = false, phone = false;
        foreach (var text in texts)
        {
            if (string.IsNullOrEmpty(text)) continue;

            // Blank out email addresses first, so their domain is not reported again as a link.
            var withoutEmails = EmailPattern.Replace(text, match =>
            {
                email = true;
                return new string(' ', match.Length);
            });
            link |= LinkPattern.IsMatch(withoutEmails);
            phone |= PhonePattern.Matches(withoutEmails).Any(match => StandsAlone(withoutEmails, match));
        }

        var kinds = new List<string>(3);
        if (link) kinds.Add(Link);
        if (email) kinds.Add(Email);
        if (phone) kinds.Add(Phone);
        return kinds;
    }

    /// <summary>A phone number must not be part of a longer run of digits, such as a catalogue number.</summary>
    private static bool StandsAlone(string text, Match match)
    {
        var before = match.Index - 1;
        var after = match.Index + match.Length;
        return (before < 0 || !char.IsDigit(text[before])) && (after >= text.Length || !char.IsDigit(text[after]));
    }

    private static string[] Words(string text) =>
        HebrewText.Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
