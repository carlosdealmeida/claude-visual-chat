using System;
using System.Globalization;
using System.Text;

namespace ClaudeVisualChat;

/// <summary>What the notice under the reply box says, besides how many characters went (see <see cref="ChatStrings.FormatInvisibleNotice"/>).</summary>
internal enum InvisibleNoticeKind
{
    /// <summary>They were found when the message was sent: the box is filled again with the clean text and nothing was sent.</summary>
    Review,

    /// <summary>They were found when the message was sent, and nothing is left of it.</summary>
    Empty,

    /// <summary>They were found in the text that was pasted.</summary>
    Pasted,
}

/// <summary>
/// Takes the characters that cannot be seen out of the text of a message: the ones that hide instructions from the person who
/// reads it (zero-width characters, the tags block, the bidirectional controls, the variation selectors, the control codes) or
/// that change nothing on the screen. Done on a paste (up to <see cref="PasteLimit"/> characters of it) and again on send.
/// </summary>
/// <remarks>
/// <para>What goes: the C0 controls except Tab and line feed, DEL and the C1 controls; U+00AD, 034F, 061C, 115F, 1160, 3164, FFA0;
/// 17B4-17B5, 180B-180F; 200B-200F, 202A-202E, 2060-206F; FE00-FE0F, FEFF, FFF0-FFFB; 1107F, 13430-1343F, 16FE4, 1BCA0-1BCA3,
/// 1D173-1D17A, E0000-E0FFF; and a surrogate with no partner.</para>
/// <para>What stays, because the text needs it: a zero width (non) joiner between letters of the scripts that use it and a zero width
/// joiner between emoji; a zero width space between letters of Thai, Lao, Khmer or Myanmar; a variation selector 15 or 16 after an
/// emoji; the left-to-right, right-to-left and Arabic letter marks on a line that has right-to-left letters; the tags of the flags of
/// England, Scotland and Wales.</para>
/// <para>Line breaks: a carriage return, vertical tab, form feed, next line, line separator or paragraph separator is a line feed
/// (a carriage return and a line feed together, one). These are not counted as removed.</para>
/// </remarks>
internal static class InvisibleCharacters
{
    /// <summary>How much of a pasted text is looked through, and how long a message may be (the rest is cut off when it is sent).</summary>
    internal const int PasteLimit = 50000;

    /// <summary>The text with the invisible characters out and its line breaks as line feeds, and how many characters went.</summary>
    internal static (string Text, int Removed) Clean(string text)
    {
        if (string.IsNullOrEmpty(text)) return (text ?? "", 0);
        var lines = LineFeeds(text);
        var result = new StringBuilder(lines.Length);
        var removed = 0;
        var previous = -1; // the last character kept, and its category
        var previousCategory = UnicodeCategory.OtherNotAssigned;
        var line = new Line();
        var i = 0;
        while (i < lines.Length)
        {
            var at = i;
            var cp = NextCodePoint(lines, i);
            i += cp > 0xFFFF ? 2 : 1;
            if (cp >= 0xD800 && cp <= 0xDFFF)
            {
                removed++; // a surrogate with no partner
                continue;
            }
            if (cp == '\n')
            {
                line = new Line { Start = i };
            }
            else if (cp < 0x20 && cp != '\t' || cp >= 0x7F && cp <= 0x9F || IsInvisible(cp) && !KeptInContext(lines, at, i, cp, previous, previousCategory, line))
            {
                removed++;
                continue;
            }
            var length = i - at;
            if (cp == 0x1F3F4)
            {
                // the tags of the flags of England, Scotland and Wales go with the waving black flag
                var tags = FlagTagsLength(lines, i);
                length += tags;
                i += tags;
                if (tags > 0) cp = 0xE007F;
            }
            result.Append(lines, at, length);
            previous = cp;
            previousCategory = CharUnicodeInfo.GetUnicodeCategory(lines, at);
        }
        return (removed == 0 ? lines : result.ToString(), removed);
    }

    /// <summary>The line the scan is on: where it starts, and (once asked) whether it has a letter written from right to left.</summary>
    private sealed class Line
    {
        public int Start { get; set; }

        public bool? HasRtl { get; set; }
    }

    /// <summary>The text with its line breaks as line feeds: CR LF is one, and a lone CR, VT, FF, NEL, LS or PS is one.</summary>
    private static string LineFeeds(string text)
    {
        var any = false;
        foreach (var c in text)
        {
            if (c == '\r' || c == '\u000B' || c == '\u000C' || c == '\u0085' || c == '\u2028' || c == '\u2029')
            {
                any = true;
                break;
            }
        }
        if (!any) return text;
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r')
            {
                sb.Append('\n');
                if (i + 1 < text.Length && text[i + 1] == '\n') i++;
            }
            else if (c == '\u000B' || c == '\u000C' || c == '\u0085' || c == '\u2028' || c == '\u2029')
            {
                sb.Append('\n');
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    /// <summary>True for a character of the lists above, the ones that may stay in some context included.</summary>
    private static bool IsInvisible(int cp)
    {
        switch (cp)
        {
            case 0x00AD: case 0x034F: case 0x061C: case 0x115F: case 0x1160: case 0x3164: case 0xFFA0:
            case 0x17B4: case 0x17B5:
            case 0xFEFF: case 0x1107F: case 0x16FE4:
                return true;
        }
        return (cp >= 0x180B && cp <= 0x180F)
            || (cp >= 0x200B && cp <= 0x200F)
            || (cp >= 0x202A && cp <= 0x202E)
            || (cp >= 0x2060 && cp <= 0x206F)
            || (cp >= 0xFE00 && cp <= 0xFE0F)
            || (cp >= 0xFFF0 && cp <= 0xFFFB)
            || (cp >= 0x13430 && cp <= 0x1343F)
            || (cp >= 0x1BCA0 && cp <= 0x1BCA3)
            || (cp >= 0x1D173 && cp <= 0x1D17A)
            || (cp >= 0xE0000 && cp <= 0xE0FFF);
    }

    /// <summary>True when the text needs this character where it is (see the remarks of the class).</summary>
    private static bool KeptInContext(string text, int at, int after, int cp, int previous, UnicodeCategory previousCategory, Line line)
    {
        var next = NextCodePoint(text, after);
        var afterLetter = next >= 0 && IsLetterOrMarkAt(text, after);
        var beforeLetter = previous >= 0 && IsLetterOrMark(previousCategory);
        switch (cp)
        {
            case 0x200B: // zero width space: between letters of the scripts that write no spaces
                return beforeLetter && afterLetter && IsSpacelessScript(previous) && IsSpacelessScript(next);
            case 0x200C: // zero width non-joiner and joiner: inside a word of a script that joins letters
            case 0x200D:
                if (beforeLetter && afterLetter && IsJoiningScript(previous) && IsJoiningScript(next)) return true;
                // and the joiner between the parts of an emoji
                return cp == 0x200D && previous >= 0 && next >= 0 && IsEmojiPart(previous) && IsPictographic(next);
            case 0x200E: // left-to-right mark, right-to-left mark, Arabic letter mark: on a line that has right-to-left letters
            case 0x200F:
            case 0x061C:
                return HasRtl(text, at, line);
            case 0xFE0E: // text and emoji presentation after an emoji
            case 0xFE0F:
                return previous >= 0 && (IsPictographic(previous) || IsKeycapBase(previous) || (previous >= 0x1F1E6 && previous <= 0x1F1FF));
        }
        return false;
    }

    private static int NextCodePoint(string text, int index)
    {
        if (index >= text.Length) return -1;
        if (char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            return char.ConvertToUtf32(text[index], text[index + 1]);
        return text[index];
    }

    private static bool IsLetterOrMark(UnicodeCategory category) =>
        category == UnicodeCategory.UppercaseLetter || category == UnicodeCategory.LowercaseLetter
        || category == UnicodeCategory.TitlecaseLetter || category == UnicodeCategory.ModifierLetter
        || category == UnicodeCategory.OtherLetter || category == UnicodeCategory.NonSpacingMark
        || category == UnicodeCategory.SpacingCombiningMark || category == UnicodeCategory.EnclosingMark;

    private static bool IsLetterOrMarkAt(string text, int index) =>
        index < text.Length && IsLetterOrMark(CharUnicodeInfo.GetUnicodeCategory(text, index));

    /// <summary>Thai, Lao, Khmer, Myanmar: scripts that write words with no spaces between them.</summary>
    private static bool IsSpacelessScript(int cp) =>
        (cp >= 0x0E00 && cp <= 0x0EFF) || (cp >= 0x1000 && cp <= 0x109F) || (cp >= 0x1780 && cp <= 0x17FF)
        || (cp >= 0x19E0 && cp <= 0x19FF) || (cp >= 0xA9E0 && cp <= 0xA9FF) || (cp >= 0xAA60 && cp <= 0xAA7F);

    /// <summary>The scripts whose letters join, or whose consonants take a virama: a joiner changes the shape of the word.</summary>
    private static bool IsJoiningScript(int cp) =>
        (cp >= 0x0600 && cp <= 0x077F) || (cp >= 0x0870 && cp <= 0x08FF)   // Arabic, Syriac
        || (cp >= 0x0780 && cp <= 0x07FF)                                    // Thaana, Nko
        || (cp >= 0x0900 && cp <= 0x0DFF)                                    // Devanagari to Sinhala
        || (cp >= 0x0F00 && cp <= 0x0FFF)                                    // Tibetan
        || (cp >= 0x1000 && cp <= 0x109F) || (cp >= 0x1780 && cp <= 0x17FF)  // Myanmar, Khmer
        || (cp >= 0x1800 && cp <= 0x18AF)                                    // Mongolian
        || (cp >= 0x2D30 && cp <= 0x2D7F)                                    // Tifinagh
        || (cp >= 0xA8E0 && cp <= 0xA8FF)                                    // Devanagari extended
        || (cp >= 0xFB50 && cp <= 0xFDFF) || (cp >= 0xFE70 && cp <= 0xFEFC); // Arabic forms

    /// <summary>A character an emoji joiner may follow: a pictograph, the emoji presentation selector or a skin tone.</summary>
    private static bool IsEmojiPart(int cp) => IsPictographic(cp) || cp == 0xFE0F || (cp >= 0x1F3FB && cp <= 0x1F3FF);

    /// <summary>An approximation of Extended_Pictographic: the blocks of the emoji.</summary>
    private static bool IsPictographic(int cp) =>
        cp == 0x00A9 || cp == 0x00AE || cp == 0x203C || cp == 0x2049 || cp == 0x2122 || cp == 0x2139
        || (cp >= 0x2194 && cp <= 0x21AA) || (cp >= 0x231A && cp <= 0x23FF) || cp == 0x24C2
        || (cp >= 0x25AA && cp <= 0x25FE) || (cp >= 0x2600 && cp <= 0x27BF) || cp == 0x2934 || cp == 0x2935
        || (cp >= 0x2B05 && cp <= 0x2B55) || cp == 0x3030 || cp == 0x303D || cp == 0x3297 || cp == 0x3299
        || (cp >= 0x1F000 && cp <= 0x1FAFF);

    /// <summary>A digit, # or *: with the selector and the keycap, an emoji.</summary>
    private static bool IsKeycapBase(int cp) => (cp >= '0' && cp <= '9') || cp == '#' || cp == '*';

    /// <summary>True when the line has a letter written from right to left (Hebrew, Arabic, Syriac, Thaana, N'Ko...); worked out once a line.</summary>
    private static bool HasRtl(string text, int at, Line line)
    {
        if (line.HasRtl is { } known) return known;
        var end = text.IndexOf('\n', at);
        if (end < 0) end = text.Length;
        var found = false;
        for (var i = line.Start; i < end && !found; i += NextCodePoint(text, i) > 0xFFFF ? 2 : 1)
        {
            found = IsRtlRange(NextCodePoint(text, i)) && IsLetterAt(text, i);
        }
        line.HasRtl = found;
        return found;
    }

    private static bool IsLetterAt(string text, int index)
    {
        var category = CharUnicodeInfo.GetUnicodeCategory(text, index);
        return category == UnicodeCategory.UppercaseLetter || category == UnicodeCategory.LowercaseLetter
            || category == UnicodeCategory.TitlecaseLetter || category == UnicodeCategory.ModifierLetter
            || category == UnicodeCategory.OtherLetter;
    }

    private static bool IsRtlRange(int cp) =>
        (cp >= 0x0590 && cp <= 0x08FF) || (cp >= 0xFB1D && cp <= 0xFDFF) || (cp >= 0xFE70 && cp <= 0xFEFC)
        || (cp >= 0x10800 && cp <= 0x10FFF) || (cp >= 0x1E800 && cp <= 0x1EFFF);

    /// <summary>
    /// The length, in characters, of the tags that follow a waving black flag when they spell the flag of England, Scotland or Wales
    /// (gbeng, gbsct, gbwls and the cancel tag); 0 when they do not.
    /// </summary>
    private static int FlagTagsLength(string text, int from)
    {
        foreach (var flag in new[] { "gbeng", "gbsct", "gbwls" })
        {
            // each tag is a surrogate pair: U+E0000 plus the ASCII letter, then U+E007F
            var length = (flag.Length + 1) * 2;
            if (from + length > text.Length) continue;
            var match = true;
            for (var k = 0; k <= flag.Length && match; k++)
            {
                var cp = k < flag.Length ? 0xE0000 + flag[k] : 0xE007F;
                match = NextCodePoint(text, from + k * 2) == cp;
            }
            if (match) return length;
        }
        return 0;
    }
}
