using System;
using System.Collections.Generic;

namespace ClaudeVisualChat;

/// <summary>
/// Where the word <c>ultrathink</c> stands in the reply box's text, as the extension finds it to paint it: in any case, a whole word, not
/// in a command, a path, a flag or a quoted or bracketed span, and not in the long pastes the box remembers.
/// </summary>
internal static class UltrathinkKeyword
{
    internal const string Word = "ultrathink";

    /// <summary>A text over this many characters (trimmed) is not searched.</summary>
    internal const int MaxLength = 50_000;

    private static readonly IReadOnlyList<(int Start, int Length)> None = Array.Empty<(int, int)>();

    /// <summary>
    /// Every occurrence of the word in <paramref name="text"/>, in order. The ranges in <paramref name="masked"/> (the long pastes) are
    /// letters for the search (an <c>x</c> each, as the extension does): a word glued to a paste has no boundary there, and the paste's
    /// last character does not count as one before or after the word. None in a text that, trimmed, starts with <c>/</c> or is over <see cref="MaxLength"/> characters. An
    /// occurrence counts when it has a word boundary on both sides; is outside <c>`…`</c>, <c>"…"</c>, <c>{…}</c>, <c>[…]</c>,
    /// <c>(…)</c>, <c>'…'</c> (a <c>'</c> opens only after a character that is not a letter, a digit or <c>_</c>, and closes only before
    /// one) and <c>&lt;…&gt;</c> (a <c>&lt;</c> opens only before a letter or <c>/</c>), where an opener with no closer makes no span; has
    /// no <c>/</c>, <c>\</c> or <c>-</c> just before it; and has no <c>/</c>, <c>\</c>, <c>-</c> or <c>?</c> just after it, nor a
    /// <c>.</c> followed by a letter or a digit (<c>ultrathink.md</c>).
    /// </summary>
    internal static IReadOnlyList<(int Start, int Length)> Find(string text, IReadOnlyList<(int Start, int Length)> masked)
    {
        if (string.IsNullOrEmpty(text)) return None;
        var trimmed = text.Trim();
        if (trimmed.StartsWith("/", StringComparison.Ordinal) || trimmed.Length > MaxLength) return None;
        if (text.IndexOf(Word, StringComparison.OrdinalIgnoreCase) < 0) return None;

        var chars = text.ToCharArray();
        foreach (var (start, length) in masked)
        {
            for (var i = Math.Max(0, start); i < Math.Min(chars.Length, start + length); i++) chars[i] = 'x';
        }
        var inSpan = Spans(chars);

        var found = new List<(int, int)>();
        var at = 0;
        var search = new string(chars);
        while ((at = search.IndexOf(Word, at, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            if (Counts(chars, at, inSpan)) found.Add((at, Word.Length));
            at += Word.Length;
        }
        return found;
    }

    private static bool Counts(char[] chars, int at, bool[] inSpan)
    {
        var end = at + Word.Length;
        for (var i = at; i < end; i++)
        {
            if (inSpan[i]) return false;
        }
        var before = at > 0 ? chars[at - 1] : '\0';
        var after = end < chars.Length ? chars[end] : '\0';
        if (IsWordChar(before) || IsWordChar(after)) return false;
        if (before == '/' || before == '\\' || before == '-') return false;
        if (after == '/' || after == '\\' || after == '-' || after == '?') return false;
        if (after == '.' && end + 1 < chars.Length && char.IsLetterOrDigit(chars[end + 1])) return false;
        return true;
    }

    /// <summary>The characters of a regular expression's <c>\w</c>: what a word boundary is between.</summary>
    private static bool IsWordChar(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_';

    /// <summary>A letter, a digit or <c>_</c>: what a single quote must not touch on its outer side.</summary>
    private static bool IsQuoteNeighbour(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>Which characters are inside a quoted or bracketed span (its marks included), read from left to right.</summary>
    private static bool[] Spans(char[] chars)
    {
        var inSpan = new bool[chars.Length];
        // the first closer of each kind found from the last search on (-1: none): the searches only go forward, so a text full of
        // openers with no closer is read once, not once per opener
        var closers = new Dictionary<char, int>();
        var i = 0;
        while (i < chars.Length)
        {
            var close = Closer(chars, i, closers);
            if (close < 0)
            {
                i++;
                continue;
            }
            for (var k = i; k <= close; k++) inSpan[k] = true;
            i = close + 1;
        }
        return inSpan;
    }

    /// <summary>Where the span that the character at <paramref name="at"/> opens ends; -1 when it opens none.</summary>
    private static int Closer(char[] chars, int at, Dictionary<char, int> closers)
    {
        char closing;
        switch (chars[at])
        {
            case '`':
            case '"':
                closing = chars[at];
                break;
            case '{':
                closing = '}';
                break;
            case '[':
                closing = ']';
                break;
            case '(':
                closing = ')';
                break;
            case '<':
                if (at + 1 >= chars.Length || !(char.IsLetter(chars[at + 1]) || chars[at + 1] == '/')) return -1;
                closing = '>';
                break;
            case '\'':
                if (at > 0 && IsQuoteNeighbour(chars[at - 1])) return -1;
                closing = '\'';
                break;
            default:
                return -1;
        }
        var from = at + 1;
        if (closers.TryGetValue(closing, out var known) && (known < 0 || known >= from)) return known;
        var found = -1;
        for (var k = from; k < chars.Length; k++)
        {
            // a single quote closes only where no letter, digit or _ follows it
            if (chars[k] == closing && (closing != '\'' || k + 1 >= chars.Length || !IsQuoteNeighbour(chars[k + 1])))
            {
                found = k;
                break;
            }
        }
        closers[closing] = found;
        return found;
    }
}
