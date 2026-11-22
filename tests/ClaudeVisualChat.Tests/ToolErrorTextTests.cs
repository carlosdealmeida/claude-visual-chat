using Xunit;

namespace ClaudeVisualChat.Tests;

public class ToolErrorTextTests
{
    private const string Sentence = "The user doesn't want to proceed with this tool use. The tool use was rejected (eg. if it was a file edit, the new_string was NOT written to the file). STOP what you are doing and wait for the user to tell you how to proceed.";
    private const string Lead = "The user provided the following reason for the rejection: ";
    private const string Note = "Note: The user's next message may contain a correction or preference. Pay close attention.";

    [Fact]
    public void The_correction_note_at_the_end_goes()
    {
        Assert.Equal("Exit code 1\nboom", ToolErrorText.Clean("Bash", "Exit code 1\nboom\n\n" + Note));
    }

    [Fact]
    public void A_standard_refusal_sentence_alone_is_null()
    {
        Assert.Null(ToolErrorText.Clean("Read", Sentence));
        Assert.Null(ToolErrorText.Clean("Read", Sentence + "\n\n" + Note));
        Assert.Null(ToolErrorText.Clean("Read", Sentence + " " + Lead)); // the lead with nothing after it
    }

    [Fact]
    public void The_refusal_prefix_before_the_typed_reason_goes()
    {
        Assert.Equal("Use rg instead.\nNot grep.", ToolErrorText.Clean("Bash", Sentence + " " + Lead + "Use rg instead.\nNot grep.\n\n" + Note));
    }

    [Fact]
    public void A_wrapper_around_the_whole_text_goes()
    {
        Assert.Equal("String to replace not found in file.\nString: x", ToolErrorText.Clean("Edit", "<tool_use_error>String to replace not found in file.\nString: x</tool_use_error>"));
    }

    [Fact]
    public void A_wrapper_with_a_tag_inside_stays()
    {
        const string text = "<tool_use_error>see <file>a.txt</file> for it</tool_use_error>";
        Assert.Equal(text, ToolErrorText.Clean("Edit", text));
    }

    [Fact]
    public void The_retry_guidance_at_the_end_goes()
    {
        Assert.Equal("Permission denied.", ToolErrorText.Clean("Bash", "Permission denied.\nIMPORTANT: You *may* attempt to accomplish this action using other tools."));
    }

    [Fact]
    public void A_command_loses_its_ansi_codes_and_every_tool_shows_control_and_format_characters()
    {
        Assert.Equal("red\ttab\nnext\\u{200B}\u2407", ToolErrorText.Clean("Bash", "\u001b[31mred\u001b[0m\ttab\r\nnext\u200B\u0007  "));
        Assert.Equal("a\nb", ToolErrorText.Clean("PowerShell", "a\r\nb"));
        Assert.Equal("a\\u{202E}b", ToolErrorText.Clean("Read", "a\u202Eb"));
    }

    [Fact]
    public void Ansi_is_kept_for_a_tool_that_is_not_a_command()
    {
        // the ESC shows as its symbol; the rest of the code stays as text
        Assert.Equal("\u241B[31mred\u241B[0m", ToolErrorText.Clean("Read", "\u001b[31mred\u001b[0m"));
    }

    [Fact]
    public void An_error_shows_hidden_characters_as_symbols()
    {
        Assert.Equal("a\\u{200B}b\u2407", ToolErrorText.Clean("Read", "a\u200Bb\u0007"));
    }

    [Fact]
    public void An_error_keeps_an_emoji_family_whole()
    {
        const string family = "x \U0001F468\u200D\U0001F469\u200D\U0001F467 y";
        Assert.Equal(family, ToolErrorText.Clean("Read", family));
    }

    [Fact]
    public void A_command_error_loses_its_plain_colors_and_another_tools_shows_the_escape()
    {
        const string colored = "\u001b[31mboom\u001b[0m";
        Assert.Equal("boom", ToolErrorText.Clean("Bash", colored));
        Assert.Equal("boom", ToolErrorText.Clean("TaskOutput", colored));
        Assert.Equal("\u241B[31mboom\u241B[0m", ToolErrorText.Clean("Read", colored));
    }

    [Fact]
    public void A_lone_carriage_return_is_a_line_break()
    {
        Assert.Equal("a\nb\nc", ToolErrorText.Clean("Bash", "a\rb\r\nc"));
    }

    [Fact]
    public void A_text_over_two_thousand_units_is_cut_with_an_ellipsis()
    {
        var cut = ToolErrorText.Clean("Read", new string('x', 2500))!;
        Assert.Equal(2001, cut.Length);
        Assert.EndsWith("…", cut);
        Assert.Equal(new string('x', 2000), ToolErrorText.Clean("Read", new string('x', 2000)));
    }
}
