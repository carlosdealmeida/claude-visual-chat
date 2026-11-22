using System.Diagnostics;
using System.Linq;
using System.Text;
using Xunit;

namespace ClaudeVisualChat.Tests;

/// <remarks>Every character that is not plain ASCII is written as an escape, so that nothing hidden is in this file.</remarks>
public class CommandOutputTextTests
{
    private const string E = "\u001b";

    private static string Repeat(string text, int times) => new StringBuilder(text.Length * times).Insert(0, text, times).ToString();

    private const string Family = "\U0001F468\u200D\U0001F469\u200D\U0001F467";
    private const string RedHeart = "\u2764\uFE0F";
    private const string ThumbsUpMediumSkin = "\U0001F44D\U0001F3FD";
    private const string BrazilFlag = "\U0001F1E7\U0001F1F7";
    private const string KeycapOne = "1\uFE0F\u20E3";
    private const string ScotlandFlag = "\U0001F3F4\U000E0067\U000E0062\U000E0073\U000E0063\U000E0074\U000E007F";
    private const string SmileAsText = "\u263A\uFE0E";

    /// <summary>A Persian word with a zero width non-joiner between its letters.</summary>
    private const string Persian = "\u0633\u0644\u0627\u200C\u0645";

    [Theory]
    [InlineData("a" + E + "[mb")]
    [InlineData("a" + E + "[0mb")]
    [InlineData("a" + E + "[01;32mb")]
    [InlineData("a" + E + "[1;31;42mb")]
    [InlineData("a" + E + "[22;39;49mb")]
    [InlineData("a" + E + "[97mb")]
    [InlineData("a" + E + "[38;5;3mb")]
    [InlineData("a" + E + "(B" + E + "[mb")]
    [InlineData("a" + E + "[31m" + E + "[Kb")]
    public void Simple_colors_go(string text)
    {
        Assert.Equal("ab", CommandOutputText.WithoutSimpleColors(text));
        Assert.Equal("ab", CommandOutputText.Result(text));
    }

    [Fact]
    public void An_erase_line_goes_only_at_a_line_edge()
    {
        Assert.Equal("ab", CommandOutputText.WithoutSimpleColors(E + "[Kab"));
        Assert.Equal("ab\ncd", CommandOutputText.WithoutSimpleColors("ab" + E + "[K\ncd"));
        Assert.Equal("a" + E + "[Kb", CommandOutputText.WithoutSimpleColors("a" + E + "[Kb"));
    }

    [Theory]
    [InlineData("[38;5;196m")]
    [InlineData("[38;5;31m")]
    [InlineData("[48;5;3m")]
    [InlineData("[38;2;255;0;0m")]
    [InlineData("[5m")]
    [InlineData("[9m")]
    [InlineData("[101m")]
    [InlineData("[1;2;3;4m")]
    [InlineData("[2J")]
    [InlineData("[?25l")]
    [InlineData("]8;;http://x\u0007", "]8;;http://x\u2407")] // the bell that ends a hyperlink shows too
    public void Other_sequences_stay_and_show_their_escape(string sequence, string? shown = null)
    {
        Assert.Equal("a\u241B" + (shown ?? sequence) + "b", CommandOutputText.Result("a" + E + sequence + "b"));
    }

    [Fact]
    public void Text_that_is_only_colors_comes_back_as_it_was()
    {
        var text = E + "[31m" + E + "[0m";
        Assert.Same(text, CommandOutputText.WithoutSimpleColors(text));
    }

    [Fact]
    public void Utf16_nuls_go_from_the_first_four_runs()
    {
        Assert.Equal("\uFFFD\uFFFDab\r\n", CommandOutputText.WithoutSimpleColors("\uFFFD\uFFFDa\0b\0\r\0\n\0"));

        const string run = "a\0\n\0";
        var text = string.Join("x\n", Enumerable.Repeat(run, 5));
        Assert.Equal("a\nx\na\nx\na\nx\na\nx\na\0\n\0", CommandOutputText.WithoutSimpleColors(text));
    }

    [Fact]
    public void Controls_become_pictures()
    {
        Assert.Equal("\u2400", CommandOutputText.Symbols("\0", true));
        Assert.Equal("\u2407", CommandOutputText.Symbols("\u0007", true));
        Assert.Equal("\u2408", CommandOutputText.Symbols("\b", true));
        Assert.Equal("\u240B", CommandOutputText.Symbols("\v", true));
        Assert.Equal("\u240C", CommandOutputText.Symbols("\f", true));
        Assert.Equal("\u2421", CommandOutputText.Symbols("\u007f", true));
        Assert.Equal("\u241B", CommandOutputText.Symbols(E, true));
        Assert.Equal("\\u{85}", CommandOutputText.Symbols("\u0085", true));
    }

    [Fact]
    public void Formatting_characters_become_their_code()
    {
        Assert.Equal("a\\u{200B}b", CommandOutputText.Symbols("a\u200Bb", true));
        Assert.Equal("\\u{202E}abc", CommandOutputText.Symbols("\u202Eabc", true));
        Assert.Equal("a\\u{AD}b", CommandOutputText.Symbols("a\u00ADb", true));
        Assert.Equal("a\\u{FEFF}b", CommandOutputText.Symbols("a\uFEFFb", true));
        Assert.Equal("a\\u{2028}b", CommandOutputText.Symbols("a\u2028b", true));
        Assert.Equal("\\u{E0001}", CommandOutputText.Symbols("\U000E0001", true));
        Assert.Equal("\u0633\u0644\u0627\\u{200C}\u0645", CommandOutputText.Symbols(Persian, true));
        Assert.Equal("a\\u{FE0F}", CommandOutputText.Symbols("a\uFE0F", true));
    }

    [Fact]
    public void Emoji_sequences_stay_whole()
    {
        foreach (var emoji in new[] { Family, RedHeart, ThumbsUpMediumSkin, BrazilFlag, KeycapOne, ScotlandFlag, SmileAsText })
        {
            Assert.Equal(emoji, CommandOutputText.Symbols(emoji, true));
            Assert.Equal("see " + emoji + " there", CommandOutputText.Result("see " + emoji + " there"));
        }
    }

    [Fact]
    public void A_joiner_after_an_emoji_with_no_emoji_after_it_shows()
    {
        Assert.Equal("\U0001F468\\u{200D}x", CommandOutputText.Symbols("\U0001F468\u200Dx", true));
    }

    [Fact]
    public void Line_breaks_and_tabs_stay()
    {
        Assert.Equal("a\tb\nc\rd\n", CommandOutputText.Result("a\tb\r\nc\rd\n"));
        Assert.Equal("a\u240Db", CommandOutputText.Symbols("a\rb", false));
    }

    [Fact]
    public void A_run_of_carriage_returns_before_a_line_break_is_one_line_break()
    {
        Assert.Equal("a\nb", CommandOutputText.Result("a\r\r\nb"));
        Assert.Equal("a\nb", CommandOutputText.Result("a\r\r\r\nb"));
        Assert.Equal("a\r\rb", CommandOutputText.Result("a\r\rb"));
    }

    [Fact]
    public void A_lone_surrogate_stays()
    {
        Assert.Equal("a\uD800b", CommandOutputText.Symbols("a\uD800b", true));
        Assert.Equal("a\uDC00", CommandOutputText.Symbols("a\uDC00", true));
        Assert.Equal("\uD83D", CommandOutputText.Result("\uD83D"));
    }

    [Fact]
    public void Bidi_marks_become_text()
    {
        Assert.Equal("a\\u202Eb\\u2066c\\u061C", CommandOutputText.BidiAsText("a\u202Eb\u2066c\u061C"));
        Assert.Equal("a\u200Bb", CommandOutputText.BidiAsText("a\u200Bb"));
        Assert.Equal("a" + E + "[31mb", CommandOutputText.BidiAsText("a" + E + "[31mb"));
    }

    [Fact]
    public void Clean_text_comes_back_the_same_instance()
    {
        var text = "plain\ttext, with accents: a\u00E7\u00E3o \u2713 " + Family + " " + RedHeart + " " + KeycapOne + " " + ScotlandFlag + "\nline";
        Assert.Same(text, CommandOutputText.Result(text));
        Assert.Same(text, CommandOutputText.WithoutSimpleColors(text));
        Assert.Same(text, CommandOutputText.Symbols(text, false));
        Assert.Same(text, CommandOutputText.BidiAsText(text));
    }

    /// <summary>The inputs of the tests above.</summary>
    private static readonly string[] Cases =
    {
        "a" + E + "[mb", "a" + E + "[0mb", "a" + E + "[01;32mb", "a" + E + "[1;31;42mb", "a" + E + "[22;39;49mb", "a" + E + "[97mb",
        "a" + E + "[38;5;3mb", "a" + E + "(B" + E + "[mb", "a" + E + "[31m" + E + "[Kb",
        E + "[Kab", "ab" + E + "[K\ncd", "a" + E + "[Kb",
        "a" + E + "[38;5;196mb", "a" + E + "[38;5;31mb", "a" + E + "[48;5;3mb", "a" + E + "[38;2;255;0;0mb", "a" + E + "[5mb",
        "a" + E + "[9mb", "a" + E + "[101mb", "a" + E + "[1;2;3;4mb", "a" + E + "[2Jb", "a" + E + "[?25lb", "a" + E + "]8;;http://x\u0007b",
        E + "[31m" + E + "[0m",
        "\uFFFD\uFFFDa\0b\0\r\0\n\0", string.Join("x\n", Enumerable.Repeat("a\0\n\0", 5)),
        "\0", "\u0007", "\b", "\v", "\f", "\u007f", E, "\u0085",
        "a\u200Bb", "\u202Eabc", "a\u00ADb", "a\uFEFFb", "a\u2028b", "\U000E0001", Persian, "a\uFE0F",
        Family, RedHeart, ThumbsUpMediumSkin, BrazilFlag, KeycapOne, ScotlandFlag, SmileAsText,
        "\U0001F468\u200Dx",
        "a\tb\r\nc\rd\n", "a\rb", "a\r\r\nb", "a\r\r\r\nb", "a\r\rb",
        "a\uD800b", "a\uDC00", "\uD83D",
        "a\u202Eb\u2066c\u061C",
    };

    [Fact]
    public void Result_twice_is_result_once()
    {
        foreach (var text in Cases)
        {
            var once = CommandOutputText.Result(text);
            Assert.Equal(once, CommandOutputText.Result(once));
        }
    }

    [Fact]
    public void Half_a_megabyte_of_hidden_characters_takes_under_five_seconds()
    {
        foreach (var (text, expected) in new[]
                 {
                     (Repeat("\u200B", 500_000), Repeat("\\u{200B}", 500_000)),
                     (Repeat(E + "[2J", 500_000), Repeat("\u241B[2J", 500_000)),
                     (Repeat("a\0", 250_000), Repeat("a\u2400", 250_000)),
                 })
        {
            var clock = Stopwatch.StartNew();
            var result = CommandOutputText.Result(text);
            clock.Stop();
            Assert.True(clock.ElapsedMilliseconds < 5000, clock.ElapsedMilliseconds + " ms");
            Assert.Equal(expected, result);
        }
    }
}
