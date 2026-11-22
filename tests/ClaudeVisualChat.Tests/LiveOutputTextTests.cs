using System.Linq;
using Xunit;

namespace ClaudeVisualChat.Tests;

public class LiveOutputTextTests
{
    [Fact]
    public void A_truncated_output_drops_its_first_line()
    {
        Assert.Equal("b\nc", LiveOutputText.Prepare("a\nb\nc", truncated: true));
    }

    [Fact]
    public void A_truncated_output_with_one_line_loses_only_the_broken_start()
    {
        Assert.Equal("abc", LiveOutputText.Prepare("\uFFFD\uFFFDabc", truncated: true));
        Assert.Equal("abc\n", LiveOutputText.Prepare("\uFFFDabc\n", truncated: true)); // its only line break ends it
    }

    [Fact]
    public void An_output_that_is_not_truncated_keeps_its_first_line()
    {
        Assert.Equal("a\nb\nc", LiveOutputText.Prepare("a\nb\nc", truncated: false));
        Assert.Equal("\uFFFDabc", LiveOutputText.Prepare("\uFFFDabc", truncated: false));
    }

    [Fact]
    public void A_line_shows_what_follows_its_last_carriage_return()
    {
        Assert.Equal("100%\ndone", LiveOutputText.Prepare("10%\r50%\r100%\r\ndone", false));
        Assert.Equal("a\nb\n", LiveOutputText.Prepare("a\r\nb\r\n", false)); // Windows line ends
    }

    [Fact]
    public void Ansi_codes_are_taken_out()
    {
        Assert.Equal("red", LiveOutputText.Prepare("\u001b[31mred\u001b[0m", false));
    }

    [Fact]
    public void Bidi_marks_show_as_text_and_nothing_else_changes()
    {
        Assert.Equal("a\\u202Eb\u200Bc", LiveOutputText.Prepare("a\u202Eb\u200Bc", false));
    }

    [Fact]
    public void A_very_long_output_keeps_its_end()
    {
        var lines = Enumerable.Range(0, 3000).Select(_ => new string('x', 99)).ToList();
        lines[lines.Count - 1] = "last" + new string('x', 95);
        var output = string.Join("\n", lines);

        var text = LiveOutputText.Prepare(output, false);

        Assert.True(text.Length <= 100_000, text.Length.ToString());
        Assert.True(text.Length > 99_800, "too much was dropped");
        Assert.EndsWith(lines[lines.Count - 1], text);
        Assert.Equal(0, (output.Length - text.Length) % 100); // it starts at the beginning of a line
        Assert.StartsWith(new string('x', 99) + "\n", text);
    }

    [Fact]
    public void An_empty_output_stays_empty()
    {
        Assert.Equal("", LiveOutputText.Prepare("", false));
        Assert.Equal("", LiveOutputText.Prepare("", true));
    }
}
