using System.Diagnostics;
using System.Linq;
using System.Text;
using Xunit;

namespace ClaudeVisualChat.Tests;

public class InvisibleCharactersTests
{
    /// <summary>The characters with these code points, so that no invisible character is written into this file.</summary>
    private static string U(params int[] codePoints) => string.Concat(codePoints.Select(char.ConvertFromUtf32));

    private static readonly string ZeroWidthSpace = U(0x200B);
    private static readonly string ZeroWidthNonJoiner = U(0x200C);
    private static readonly string ZeroWidthJoiner = U(0x200D);
    private static readonly string Bom = U(0xFEFF);
    private static readonly string SoftHyphen = U(0x00AD);
    private static readonly string Lrm = U(0x200E);
    private static readonly string Rlm = U(0x200F);

    [Theory]
    [InlineData("a​b", "ab", 1)]
    [InlineData("x﻿y­z", "xyz", 2)]
    [InlineData("a\tb\nc", "a\tb\nc", 0)]
    [InlineData("", "", 0)]
    [InlineData("plain text, with accents: ação é ü", "plain text, with accents: ação é ü", 0)]
    public void Clean_removes_invisible_characters(string input, string expected, int removed)
    {
        var r = InvisibleCharacters.Clean(input);
        Assert.Equal(expected, r.Text);
        Assert.Equal(removed, r.Removed);
    }

    [Fact]
    public void Clean_of_nothing_is_nothing()
    {
        var r = InvisibleCharacters.Clean(null!);
        Assert.Equal("", r.Text);
        Assert.Equal(0, r.Removed);
    }

    [Fact]
    public void Clean_keeps_a_zero_width_joiner_inside_an_emoji()
    {
        var family = U(0x1F468) + ZeroWidthJoiner + U(0x1F469) + ZeroWidthJoiner + U(0x1F467);
        var heartOnFire = U(0x2764, 0xFE0F) + ZeroWidthJoiner + U(0x1F525);
        var rainbowFlag = U(0x1F3F3, 0xFE0F) + ZeroWidthJoiner + U(0x1F308);
        foreach (var emoji in new[] { family, heartOnFire, rainbowFlag })
        {
            var r = InvisibleCharacters.Clean("see " + emoji + " there");
            Assert.Equal("see " + emoji + " there", r.Text);
            Assert.Equal(0, r.Removed);
        }
    }

    [Fact]
    public void Clean_removes_a_joiner_that_is_not_in_an_emoji_or_a_joining_script()
    {
        var r = InvisibleCharacters.Clean("a" + ZeroWidthJoiner + "b " + U(0x1F468) + ZeroWidthJoiner + " " + ZeroWidthJoiner + U(0x1F469));
        Assert.Equal("ab " + U(0x1F468) + " " + U(0x1F469), r.Text);
        Assert.Equal(3, r.Removed);
    }

    [Fact]
    public void Clean_keeps_joiners_inside_words_of_scripts_that_use_them()
    {
        var persian = U(0x0645, 0x06CC) + ZeroWidthNonJoiner + U(0x062E, 0x0648, 0x0627, 0x0647, 0x0645);
        var hindi = U(0x0915, 0x094D) + ZeroWidthJoiner + U(0x0937);
        foreach (var word in new[] { persian, hindi })
        {
            var r = InvisibleCharacters.Clean(word);
            Assert.Equal(word, r.Text);
            Assert.Equal(0, r.Removed);
        }
        // the same joiner next to Latin letters is nothing
        Assert.Equal(("ab", 1), InvisibleCharacters.Clean("a" + ZeroWidthNonJoiner + "b"));
    }

    [Fact]
    public void Clean_keeps_a_zero_width_space_between_letters_of_scripts_without_spaces()
    {
        var thai = U(0x0E2A, 0x0E27, 0x0E31, 0x0E2A, 0x0E14, 0x0E35) + ZeroWidthSpace + U(0x0E04, 0x0E23, 0x0E31, 0x0E1A);
        var khmer = U(0x1780) + ZeroWidthSpace + U(0x1781);
        var myanmar = U(0x1000) + ZeroWidthSpace + U(0x1001);
        foreach (var text in new[] { thai, khmer, myanmar })
        {
            var r = InvisibleCharacters.Clean(text);
            Assert.Equal(text, r.Text);
            Assert.Equal(0, r.Removed);
        }
        Assert.Equal(("ab", 1), InvisibleCharacters.Clean("a" + ZeroWidthSpace + "b"));
        // one side Thai and the other Latin is not enough
        Assert.Equal((U(0x0E2A) + "b", 1), InvisibleCharacters.Clean(U(0x0E2A) + ZeroWidthSpace + "b"));
    }

    [Fact]
    public void Clean_keeps_a_variation_selector_after_an_emoji_and_removes_any_other()
    {
        var heart = U(0x2764, 0xFE0F);
        var text = U(0x2600, 0xFE0E);
        var keycap = "1" + U(0xFE0F, 0x20E3);
        Assert.Equal((heart, 0), InvisibleCharacters.Clean(heart));
        Assert.Equal((text, 0), InvisibleCharacters.Clean(text));
        Assert.Equal((keycap, 0), InvisibleCharacters.Clean(keycap));
        Assert.Equal(("a", 1), InvisibleCharacters.Clean("a" + U(0xFE0F)));
        Assert.Equal(("a", 1), InvisibleCharacters.Clean("a" + U(0xFE00)));
        Assert.Equal(("a", 1), InvisibleCharacters.Clean("a" + U(0xE0100)));    // an ideographic selector
    }

    [Fact]
    public void Clean_keeps_direction_marks_only_on_a_line_with_right_to_left_letters()
    {
        var hebrew = U(0x05E9, 0x05DC, 0x05D5, 0x05DD);
        var arabic = U(0x0645, 0x0631, 0x062D, 0x0628, 0x0627);
        Assert.Equal((hebrew + Rlm, 0), InvisibleCharacters.Clean(hebrew + Rlm));
        Assert.Equal(("abc " + hebrew + Lrm + " def", 0), InvisibleCharacters.Clean("abc " + hebrew + Lrm + " def"));
        Assert.Equal((arabic + U(0x061C), 0), InvisibleCharacters.Clean(arabic + U(0x061C)));
        // no right-to-left letter on the line: they are nothing
        Assert.Equal(("abc", 2), InvisibleCharacters.Clean("abc" + Lrm + Rlm));
        // the line is the one the mark is on, not the text
        var twoLines = hebrew + Rlm + "\nabc" + Rlm + "\n" + hebrew + Lrm;
        var r = InvisibleCharacters.Clean(twoLines);
        Assert.Equal(hebrew + Rlm + "\nabc\n" + hebrew + Lrm, r.Text);
        Assert.Equal(1, r.Removed);
    }

    [Fact]
    public void Clean_keeps_the_tags_of_the_flags_of_england_scotland_and_wales_and_removes_other_tags()
    {
        string Flag(string name) => U(0x1F3F4) + string.Concat(name.Select(c => U(0xE0000 + c))) + U(0xE007F);
        foreach (var flag in new[] { Flag("gbeng"), Flag("gbsct"), Flag("gbwls") })
        {
            Assert.Equal(("go " + flag + " go", 0), InvisibleCharacters.Clean("go " + flag + " go"));
        }
        // tags that spell something else, or hide in plain text, are the ones that smuggle text in
        var hidden = U(0xE0041, 0xE0042, 0xE0043);
        Assert.Equal(("hi", 3), InvisibleCharacters.Clean("hi" + hidden));
        var other = U(0x1F3F4) + string.Concat("gbxyz".Select(c => U(0xE0000 + c))) + U(0xE007F);
        Assert.Equal((U(0x1F3F4), 6), InvisibleCharacters.Clean(other));
    }

    [Fact]
    public void Clean_removes_the_controls_the_marks_and_the_blocks_of_the_list()
    {
        var removable = new[]
        {
            0x0001, 0x0008, 0x001B, 0x007F, 0x0080, 0x0099, 0x009F,                 // C0 (not tab, not line feed), DEL, C1 (not NEL)
            0x00AD, 0x034F, 0x061C, 0x115F, 0x1160, 0x3164, 0xFFA0,
            0x17B4, 0x17B5, 0x180B, 0x180F, 0x200B, 0x200C, 0x200D, 0x200E, 0x200F,
            0x202A, 0x202E, 0x2060, 0x2064, 0x2066, 0x206F, 0xFE00, 0xFE0F, 0xFEFF, 0xFFF0, 0xFFFB,
            0x1107F, 0x13430, 0x1343F, 0x16FE4, 0x1BCA0, 0x1BCA3, 0x1D173, 0x1D17A, 0xE0000, 0xE0001, 0xE0020, 0xE007F, 0xE0100, 0xE0FFF,
        };
        foreach (var cp in removable)
        {
            var r = InvisibleCharacters.Clean("a" + U(cp) + "b");
            Assert.True(r.Text == "ab" && r.Removed == 1, "U+" + cp.ToString("X4"));
        }
        // the neighbors of the lists stay
        foreach (var cp in new[] { 0x00A0, 0x00AC, 0x00AE, 0x2057, 0x2070, 0xFFEF, 0xFFFC, 0xE1000, 0x10FFFD })
        {
            var r = InvisibleCharacters.Clean("a" + U(cp) + "b");
            Assert.True(r.Text == "a" + U(cp) + "b" && r.Removed == 0, "U+" + cp.ToString("X4"));
        }
    }

    [Fact]
    public void Clean_makes_every_kind_of_line_break_a_line_feed_and_counts_none_as_removed()
    {
        var text = "a\r\nb\rc" + U(0x000B) + "d" + U(0x000C) + "e" + U(0x0085) + "f" + U(0x2028) + "g" + U(0x2029) + "h\ni";
        var r = InvisibleCharacters.Clean(text);
        Assert.Equal("a\nb\nc\nd\ne\nf\ng\nh\ni", r.Text);
        Assert.Equal(0, r.Removed);
        // a blank line stays a blank line
        Assert.Equal(("a\n\nb", 0), InvisibleCharacters.Clean("a\r\n\r\nb"));
    }

    [Fact]
    public void Clean_removes_a_surrogate_with_no_partner_and_keeps_a_pair()
    {
        var pair = U(0x1F600);
        Assert.Equal(("ab", 1), InvisibleCharacters.Clean("a" + '\uD800' + "b"));
        Assert.Equal(("ab", 1), InvisibleCharacters.Clean("a" + '\uDC00' + "b"));
        Assert.Equal(("a" + pair + "b", 0), InvisibleCharacters.Clean("a" + pair + "b"));
        Assert.Equal((pair, 1), InvisibleCharacters.Clean(pair + '\uD83D'));          // the pair stays, the last half goes
    }

    [Fact]
    public void Clean_counts_each_character_removed_and_takes_a_byte_order_mark_from_the_start()
    {
        var r = InvisibleCharacters.Clean(Bom + "a" + ZeroWidthSpace + SoftHyphen + ZeroWidthSpace + "b");
        Assert.Equal("ab", r.Text);
        Assert.Equal(4, r.Removed);
    }

    [Fact]
    public void Clean_of_fifty_thousand_characters_is_quick()
    {
        var line = "word " + ZeroWidthSpace + Rlm + "\n";
        var text = string.Concat(Enumerable.Repeat(line, 6000)); // 54,000 characters, 12,000 of them to remove
        var clock = Stopwatch.StartNew();
        var r = InvisibleCharacters.Clean(text);
        clock.Stop();
        Assert.Equal(12000, r.Removed);
        Assert.True(clock.ElapsedMilliseconds < 2000, clock.ElapsedMilliseconds + " ms");
        Assert.Equal(-1, r.Text.IndexOf(ZeroWidthSpace, System.StringComparison.Ordinal));
    }

    [Fact]
    public void Clean_of_text_with_nothing_to_remove_gives_the_text_back()
    {
        var text = new StringBuilder().Insert(0, "line\n", 100).ToString();
        Assert.Same(text, InvisibleCharacters.Clean(text).Text);
    }

    // ---- the long pastes ----

    [Fact]
    public void A_paste_is_long_over_eight_hundred_characters_or_over_two_line_feeds()
    {
        Assert.False(LongPastes.IsLong(new string('a', 800)));
        Assert.True(LongPastes.IsLong(new string('a', 801)));
        Assert.False(LongPastes.IsLong("a\nb\nc"));          // two line feeds
        Assert.True(LongPastes.IsLong("a\nb\nc\nd"));        // three
        Assert.False(LongPastes.IsLong("short"));
    }

    [Fact]
    public void A_long_paste_is_remembered_trimmed_when_at_least_twenty_characters_remain()
    {
        var pastes = new LongPastes();
        Assert.False(pastes.Remember("one\ntwo\nthree\nfour"));                          // long by its lines, but 18 characters
        Assert.False(pastes.Remember("short and sweet"));                                // not long
        Assert.True(pastes.Remember("\n\n  one line here\ntwo lines here\nthree lines\nfour\n  "));
        Assert.Equal(new[] { "one line here\ntwo lines here\nthree lines\nfour" }, pastes.Remembered);
    }

    [Fact]
    public void The_box_remembers_the_last_hundred_long_pastes_and_a_repeat_moves_to_the_end()
    {
        var pastes = new LongPastes();
        for (var i = 0; i < 105; i++) Assert.True(pastes.Remember("paste number " + i + "\nsecond line\nthird line\nfourth line"));
        Assert.Equal(100, pastes.Remembered.Count);
        Assert.StartsWith("paste number 5\n", pastes.Remembered[0]);
        Assert.True(pastes.Remember("paste number 5\nsecond line\nthird line\nfourth line"));
        Assert.Equal(100, pastes.Remembered.Count);
        Assert.StartsWith("paste number 5\n", pastes.Remembered[99]);
        Assert.StartsWith("paste number 6\n", pastes.Remembered[0]);
    }

    [Fact]
    public void The_pastes_in_a_text_are_found_once_each()
    {
        var pastes = new LongPastes();
        var first = "first paste line one\nline two\nline three\nline four";
        var second = new string('x', 900);
        pastes.Remember(first);
        pastes.Remember(second);
        pastes.Remember("a paste that is gone\nsecond\nthird\nfourth line");

        var found = pastes.In("before " + first + " middle " + first + " " + second + " after", 50000);

        Assert.Equal(new[] { first, second }, found);
        Assert.Empty(pastes.In("nothing of them", 50000));
    }

    [Fact]
    public void A_paste_the_limit_cuts_in_two_goes_as_the_part_inside_it()
    {
        var pastes = new LongPastes();
        var paste = string.Concat(Enumerable.Repeat("0123456789", 100));
        pastes.Remember(paste);
        var text = "head " + paste;
        Assert.Equal(new[] { paste }, pastes.In(text, text.Length));
        var cut = pastes.In(text, 5 + 300);
        Assert.Equal(new[] { paste.Substring(0, 300) }, cut);
        Assert.Empty(pastes.In(text, 5 + 10));            // under twenty characters inside the limit: not a paste any more
    }

    [Fact]
    public void Truncate_does_not_cut_a_surrogate_pair_in_two()
    {
        var text = "ab" + U(0x1F600) + "cd";
        Assert.Equal("ab", LongPastes.Truncate(text, 3));
        Assert.Equal("ab" + U(0x1F600), LongPastes.Truncate(text, 4));
        Assert.Same(text, LongPastes.Truncate(text, 50));
    }
}
