using System;
using System.Globalization;
using System.Text;

namespace ClaudeVisualChat;

/// <summary>
/// What a shell command printed, made ready to show: the plain color codes go, a text printed in UTF-16 loses the NUL after each of
/// its characters, and what a terminal would not show (the control and formatting characters, the default ignorable ones and the
/// escape sequences that are not plain colors) is shown as a symbol or as its code, so that nothing hides in the output.
/// </summary>
/// <remarks>Every function is one scan from left to right, and gives back the same instance when it has nothing to change.</remarks>
internal static class CommandOutputText
{
    private const char Escape = '\u001b';
    private const char Nul = '\0';
    private const char ReplacementCharacter = '\uFFFD';
    private const int ZeroWidthJoiner = 0x200D;
    private const int CombiningKeycap = 0x20E3;
    private const int TextPresentation = 0xFE0E;
    private const int EmojiPresentation = 0xFE0F;
    private const int WavingBlackFlag = 0x1F3F4;
    private const int CancelTag = 0xE007F;

    /// <summary>How many runs of UTF-16 lines lose their NUL; the ones after them stay as they are.</summary>
    private const int Utf16Runs = 4;

    /// <summary>The flags made of the waving black flag and tags: England, Scotland and Wales.</summary>
    private static readonly string[] TagFlags = { "gbeng", "gbsct", "gbwls" };

    /// <summary>
    /// The output of a command that ended without an error: <see cref="WithoutSimpleColors"/>, then each run of carriage returns
    /// right before a line feed (CR LF, CR CR LF...) as that one line feed, then <see cref="Symbols"/> keeping the other carriage returns.
    /// </summary>
    internal static string Result(string text) =>
        Symbols(WithoutCarriageReturnsBeforeLineFeeds(WithoutSimpleColors(text)), keepCarriageReturn: true);

    /// <summary>The text without the runs of carriage returns that come right before a line feed; a carriage return elsewhere stays.</summary>
    private static string WithoutCarriageReturnsBeforeLineFeeds(string text)
    {
        var i = text.IndexOf('\r');
        if (i < 0) return text;
        StringBuilder? result = null;
        var copied = 0;
        while (i < text.Length)
        {
            if (text[i] != '\r')
            {
                i++;
                continue;
            }
            var run = i;
            while (i < text.Length && text[i] == '\r') i++;
            if (i < text.Length && text[i] == '\n')
            {
                result ??= new StringBuilder(text.Length);
                result.Append(text, copied, run - copied);
                copied = i; // the line feed itself is copied with what follows it
            }
        }
        if (result == null) return text;
        result.Append(text, copied, text.Length - copied);
        return result.ToString();
    }

    /// <summary>
    /// The text without its plain color codes, and with the NUL taken out of its first four runs of UTF-16 lines; the text as it was
    /// when nothing but white space would be left of it.
    /// </summary>
    /// <remarks>
    /// <para>A plain color code is <c>ESC [</c>, up to three codes separated by <c>;</c>, and <c>m</c>. A code is 0 to 4 or 7 (with an
    /// optional leading zero), 21 to 29, 30 to 37, 39, 40 to 47, 49 or 90 to 97; and <c>38;5;3</c> is one more, on its own. An
    /// <c>ESC ( B</c> right before it and an erase line (<c>ESC [ K</c>, <c>ESC [ 0K</c>, <c>1K</c> or <c>2K</c>) right after it go with
    /// it. An erase line on its own goes only at the start of a line or at its end.</para>
    /// <para>A run of UTF-16 lines starts at the start of the text or after a line feed (and a NUL, if one follows it), may have two
    /// U+FFFD, and has one or more lines where each character (a tab, printable ASCII or U+FFFD) is followed by a NUL, then an optional
    /// CR NUL and LF NUL. The NUL of the lines go; the start of the run stays.</para>
    /// </remarks>
    internal static string WithoutSimpleColors(string text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var result = WithoutUtf16Nuls(WithoutColors(text));
        return ReferenceEquals(result, text) || !IsWhiteSpace(result) ? result : text;
    }

    /// <summary>
    /// The text with what cannot be seen shown: a control character, a formatting character, a default ignorable one
    /// (<see cref="IsDefaultIgnorable"/>), U+2028 and U+2029. Line feeds and tabs stay, and carriage returns when
    /// <paramref name="keepCarriageReturn"/>; ESC is U+241B, the other C0 controls the picture U+2400 plus their code, DEL U+2421, and
    /// the others their code as <c>\u{HEX}</c>.
    /// </summary>
    /// <remarks>
    /// What an emoji needs stays: the flags of England, Scotland and Wales; the text or emoji presentation selector or a skin tone after
    /// a pictograph, and a zero width joiner after it when another pictograph follows; the emoji selector of a keycap. A surrogate with
    /// no partner stays too.
    /// </remarks>
    internal static string Symbols(string text, bool keepCarriageReturn)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        StringBuilder? result = null;
        var copied = 0;
        var i = 0;
        while (i < text.Length)
        {
            var cp = CodePointAt(text, i);
            if (cp >= 0x20 && cp < 0x7F && !IsKeycapBase(cp))
            {
                i++;
                continue;
            }
            var kept = EmojiLength(text, i, cp);
            if (kept > 0)
            {
                i += kept;
                continue;
            }
            var length = cp > 0xFFFF ? 2 : 1;
            if (IsHidden(text, i, cp, keepCarriageReturn))
            {
                result ??= new StringBuilder(text.Length + 16);
                result.Append(text, copied, i - copied);
                AppendShown(result, cp);
                copied = i + length;
            }
            i += length;
        }
        if (result == null) return text;
        result.Append(text, copied, text.Length - copied);
        return result.ToString();
    }

    /// <summary>The longest symbol <see cref="Symbols"/> writes: <c>\u{10FFFF}</c>.</summary>
    internal const int LongestSymbol = 10;

    /// <summary>
    /// The first <paramref name="maxChars"/> UTF-16 units of a text made by <see cref="Symbols"/> (the text itself when it is no
    /// longer), a little fewer when the cut falls inside a <c>\u{…}</c> (the symbol goes whole) or between the two halves of a
    /// surrogate pair.
    /// </summary>
    internal static string CutOutsideSymbols(string text, int maxChars)
    {
        if (text.Length <= maxChars) return text;
        var cut = maxChars;
        // a symbol that starts in the last characters and closes past the cut goes whole
        for (var start = Math.Max(0, cut - LongestSymbol + 1); start < cut; start++)
        {
            if (string.CompareOrdinal(text, start, "\\u{", 0, 3) != 0) continue;
            var close = text.IndexOf('}', start + 3, Math.Min(LongestSymbol - 3, text.Length - start - 3));
            if (close < cut) continue; // closed before the cut, or no symbol at all
            cut = start;
            break;
        }
        if (cut > 0 && char.IsHighSurrogate(text[cut - 1]) && char.IsLowSurrogate(text[cut])) cut--;
        return text.Substring(0, cut);
    }

    /// <summary>The text with each bidirectional control (U+061C, 200E, 200F, 202A to 202E, 2066 to 2069) written as <c>\uXXXX</c>.</summary>
    internal static string BidiAsText(string text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        StringBuilder? result = null;
        var copied = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (!IsBidiControl(c)) continue;
            result ??= new StringBuilder(text.Length + 16);
            result.Append(text, copied, i - copied);
            result.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            copied = i + 1;
        }
        if (result == null) return text;
        result.Append(text, copied, text.Length - copied);
        return result.ToString();
    }

    /// <summary>
    /// True for the default ignorable code points of this list: U+00AD, 034F, 061C, 115F-1160, 17B4-17B5, 180B-180F, 200B-200F,
    /// 202A-202E, 2060-206F, 3164, FE00-FE0F, FEFF, FFA0, FFF0-FFF8, 1BCA0-1BCA3, 1D173-1D17A and E0000-E0FFF.
    /// </summary>
    internal static bool IsDefaultIgnorable(int codePoint)
    {
        switch (codePoint)
        {
            case 0x00AD: case 0x034F: case 0x061C: case 0x3164: case 0xFEFF: case 0xFFA0:
                return true;
        }
        return (codePoint >= 0x115F && codePoint <= 0x1160)
            || (codePoint >= 0x17B4 && codePoint <= 0x17B5)
            || (codePoint >= 0x180B && codePoint <= 0x180F)
            || (codePoint >= 0x200B && codePoint <= 0x200F)
            || (codePoint >= 0x202A && codePoint <= 0x202E)
            || (codePoint >= 0x2060 && codePoint <= 0x206F)
            || (codePoint >= 0xFE00 && codePoint <= 0xFE0F)
            || (codePoint >= 0xFFF0 && codePoint <= 0xFFF8)
            || (codePoint >= 0x1BCA0 && codePoint <= 0x1BCA3)
            || (codePoint >= 0x1D173 && codePoint <= 0x1D17A)
            || (codePoint >= 0xE0000 && codePoint <= 0xE0FFF);
    }

    // ---- plain colors

    private static string WithoutColors(string text)
    {
        var i = text.IndexOf(Escape);
        if (i < 0) return text;
        StringBuilder? result = null;
        var copied = 0;
        while (i >= 0)
        {
            var end = ColorWithItsNeighboursEnd(text, i);
            if (end < 0)
            {
                var erase = EraseLineEnd(text, i);
                if (erase > 0 && (IsLineEdge(text, i - 1) || IsLineEdge(text, erase))) end = erase;
            }
            if (end < 0)
            {
                i = text.IndexOf(Escape, i + 1);
                continue;
            }
            result ??= new StringBuilder(text.Length);
            result.Append(text, copied, i - copied);
            copied = end;
            i = end < text.Length ? text.IndexOf(Escape, end) : -1;
        }
        if (result == null) return text;
        result.Append(text, copied, text.Length - copied);
        return result.ToString();
    }

    /// <summary>Where a plain color code at <paramref name="at"/> ends, with the <c>ESC ( B</c> before it and the erase line after it; -1 when there is none.</summary>
    private static int ColorWithItsNeighboursEnd(string text, int at)
    {
        var end = -1;
        if (at + 3 < text.Length && text[at + 1] == '(' && text[at + 2] == 'B' && text[at + 3] == Escape)
            end = ColorEnd(text, at + 3);
        if (end < 0) end = ColorEnd(text, at);
        if (end < 0) return -1;
        var erase = EraseLineEnd(text, end);
        return erase > 0 ? erase : end;
    }

    /// <summary>Where the plain color code that starts at <paramref name="at"/> ends; -1 when there is none.</summary>
    private static int ColorEnd(string text, int at)
    {
        if (at + 2 >= text.Length || text[at] != Escape || text[at + 1] != '[') return -1;
        var p = at + 2;
        if (text[p] == 'm') return p + 1;
        if (string.CompareOrdinal(text, p, "38;5;3m", 0, 7) == 0) return p + 7;
        for (var codes = 1; ; codes++)
        {
            var q = p;
            while (q < text.Length && q - p < 3 && text[q] >= '0' && text[q] <= '9') q++;
            if (!IsColorCode(text, p, q - p) || q >= text.Length) return -1;
            if (text[q] == 'm') return q + 1;
            if (text[q] != ';' || codes == 3) return -1;
            p = q + 1;
        }
    }

    /// <summary>0 to 4 or 7 (with an optional leading zero), 21 to 29, 30 to 37, 39, 40 to 47, 49, 90 to 97.</summary>
    private static bool IsColorCode(string text, int at, int length)
    {
        static bool Basic(char c) => (c >= '0' && c <= '4') || c == '7';
        if (length == 1) return Basic(text[at]);
        if (length != 2) return false;
        var tens = text[at];
        var units = text[at + 1];
        switch (tens)
        {
            case '0': return Basic(units);
            case '2': return units >= '1' && units <= '9';
            case '3':
            case '4': return (units >= '0' && units <= '7') || units == '9';
            case '9': return units >= '0' && units <= '7';
            default: return false;
        }
    }

    /// <summary>Where the erase line (<c>ESC [ K</c>, <c>ESC [ 0K</c>, <c>1K</c>, <c>2K</c>) that starts at <paramref name="at"/> ends; -1 when there is none.</summary>
    private static int EraseLineEnd(string text, int at)
    {
        if (at + 2 >= text.Length || text[at] != Escape || text[at + 1] != '[') return -1;
        var p = at + 2;
        if (text[p] >= '0' && text[p] <= '2') p++;
        return p < text.Length && text[p] == 'K' ? p + 1 : -1;
    }

    /// <summary>True when <paramref name="index"/> is outside the text or a carriage return or a line feed.</summary>
    private static bool IsLineEdge(string text, int index) =>
        index < 0 || index >= text.Length || text[index] == '\r' || text[index] == '\n';

    // ---- UTF-16 NUL

    private static string WithoutUtf16Nuls(string text)
    {
        if (text.IndexOf(Nul) < 0) return text;
        StringBuilder? result = null;
        var copied = 0;
        var runs = 0;
        var next = 0; // where to look for the next line feed that may start a run
        var atStart = true;
        while (runs < Utf16Runs)
        {
            int from;
            if (atStart)
            {
                atStart = false;
                from = 0;
            }
            else
            {
                var feed = text.IndexOf('\n', next);
                if (feed < 0) break;
                next = feed + 1;
                from = feed + 1;
                if (from < text.Length && text[from] == Nul) from++;
            }
            // a line cannot start with the two U+FFFD (the second is no NUL), so there is nothing to try without them
            var lines = from + 1 < text.Length && text[from] == ReplacementCharacter && text[from + 1] == ReplacementCharacter ? from + 2 : from;
            var end = Utf16LinesEnd(text, lines);
            if (end < 0) continue;
            runs++;
            result ??= new StringBuilder(text.Length);
            result.Append(text, copied, lines - copied);
            for (var k = lines; k < end; k += 2) result.Append(text[k]);
            copied = end;
            next = end;
        }
        if (result == null) return text;
        result.Append(text, copied, text.Length - copied);
        return result.ToString();
    }

    /// <summary>Where the UTF-16 lines that start at <paramref name="at"/> end (after the LF NUL of the last whole one); -1 when there is not one.</summary>
    private static int Utf16LinesEnd(string text, int at)
    {
        var end = -1;
        var p = at;
        while (true)
        {
            var q = p;
            while (q + 1 < text.Length && IsUtf16LineCharacter(text[q]) && text[q + 1] == Nul) q += 2;
            if (q + 1 < text.Length && text[q] == '\r' && text[q + 1] == Nul) q += 2;
            if (q + 1 >= text.Length || text[q] != '\n' || text[q + 1] != Nul) return end;
            p = q + 2;
            end = p;
        }
    }

    private static bool IsUtf16LineCharacter(char c) => c == '\t' || (c >= ' ' && c <= '~') || c == ReplacementCharacter;

    // ---- symbols

    /// <summary>
    /// The length, in characters, of the emoji sequence at <paramref name="at"/> that stays whole (a tag flag, a pictograph with its
    /// selector or skin tone and a joiner to the next pictograph, a keycap); 0 when there is none.
    /// </summary>
    private static int EmojiLength(string text, int at, int cp)
    {
        if (cp == WavingBlackFlag)
        {
            var flag = TagFlagLength(text, at + 2);
            if (flag > 0) return 2 + flag;
        }
        if (InvisibleCharacters.IsPictographic(cp))
        {
            var p = at + (cp > 0xFFFF ? 2 : 1);
            var next = CodePointAt(text, p);
            if (next == TextPresentation) return p + 1 - at;
            if (next == EmojiPresentation || IsSkinTone(next))
            {
                p += next > 0xFFFF ? 2 : 1;
                next = CodePointAt(text, p);
            }
            if (next == ZeroWidthJoiner && InvisibleCharacters.IsPictographic(CodePointAt(text, p + 1))) p++;
            return p - at;
        }
        if (IsKeycapBase(cp) && CodePointAt(text, at + 1) == EmojiPresentation && CodePointAt(text, at + 2) == CombiningKeycap) return 3;
        return 0;
    }

    /// <summary>
    /// The length of the tags at <paramref name="from"/> when they spell the flag of England, Scotland or Wales and what follows them is
    /// not another tag, a variation selector, a joiner or a combining mark; 0 otherwise.
    /// </summary>
    private static int TagFlagLength(string text, int from)
    {
        foreach (var flag in TagFlags)
        {
            var length = (flag.Length + 1) * 2; // each tag is a surrogate pair
            if (from + length > text.Length) continue;
            var match = true;
            for (var k = 0; k <= flag.Length && match; k++)
                match = CodePointAt(text, from + k * 2) == (k < flag.Length ? 0xE0000 + flag[k] : CancelTag);
            if (!match) continue;
            var after = from + length;
            if (after < text.Length && ExtendsATagFlag(text, after)) return 0;
            return length;
        }
        return 0;
    }

    private static bool ExtendsATagFlag(string text, int at)
    {
        var cp = CodePointAt(text, at);
        if ((cp >= 0xE0000 && cp <= CancelTag) || (cp >= 0xFE00 && cp <= 0xFE0F) || (cp >= 0xE0100 && cp <= 0xE01EF) || cp == ZeroWidthJoiner)
            return true;
        var category = CharUnicodeInfo.GetUnicodeCategory(text, at);
        return category == UnicodeCategory.NonSpacingMark || category == UnicodeCategory.SpacingCombiningMark
            || category == UnicodeCategory.EnclosingMark;
    }

    private static bool IsSkinTone(int cp) => cp >= 0x1F3FB && cp <= 0x1F3FF;

    private static bool IsKeycapBase(int cp) => (cp >= '0' && cp <= '9') || cp == '#' || cp == '*';

    private static bool IsHidden(string text, int at, int cp, bool keepCarriageReturn)
    {
        if (cp == '\n' || cp == '\t' || (cp == '\r' && keepCarriageReturn)) return false;
        if (cp == 0x2028 || cp == 0x2029 || IsDefaultIgnorable(cp)) return true;
        var category = CharUnicodeInfo.GetUnicodeCategory(text, at);
        return category == UnicodeCategory.Control || category == UnicodeCategory.Format;
    }

    /// <summary>ESC as U+241B, another C0 control as its picture, DEL as U+2421, anything else as <c>\u{HEX}</c>.</summary>
    private static void AppendShown(StringBuilder result, int cp)
    {
        if (cp == Escape) result.Append('\u241B');
        else if (cp < 0x20) result.Append((char)(0x2400 + cp));
        else if (cp == 0x7F) result.Append('\u2421');
        else result.Append("\\u{").Append(cp.ToString("X", CultureInfo.InvariantCulture)).Append('}');
    }

    private static bool IsBidiControl(char c) =>
        c == '\u061C' || c == '\u200E' || c == '\u200F' || (c >= '\u202A' && c <= '\u202E') || (c >= '\u2066' && c <= '\u2069');

    private static bool IsWhiteSpace(string text)
    {
        foreach (var c in text)
        {
            if (!char.IsWhiteSpace(c)) return false;
        }
        return true;
    }

    /// <summary>The code point at <paramref name="index"/> (a surrogate with no partner is itself); -1 past the end.</summary>
    private static int CodePointAt(string text, int index)
    {
        if (index >= text.Length) return -1;
        var c = text[index];
        if (char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            return char.ConvertToUtf32(c, text[index + 1]);
        return c;
    }
}
