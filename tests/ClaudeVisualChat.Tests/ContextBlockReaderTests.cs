using ClaudeVisualChat.Protocol;
using Xunit;

namespace ClaudeVisualChat.Tests;

public class ContextBlockReaderTests
{
    private const string Tail = "\n\nThis may or may not be related to the current task.";

    [Fact]
    public void A_real_selection_sample_reads_the_file_name_the_lines_and_the_text_after_the_path_line()
    {
        var b = ContextBlockReader.Read(
            "<ide_selection>The user selected the lines 94 to 99 from c:\\@work\\proj\\s1\\README.md:\r\nlinha um\r\nlinha dois" + Tail + "</ide_selection>");
        Assert.NotNull(b);
        Assert.Equal(ContextBlockReader.Selection, b!.Kind);
        Assert.Equal("README.md#94-99", b.Subject);
        Assert.Equal("linha um\r\nlinha dois" + Tail, b.Content);
    }

    [Fact]
    public void A_single_line_selection_keeps_the_repeated_form()
    {
        var b = ContextBlockReader.Read(
            "<ide_selection>The user selected the lines 5 to 5 from c:\\@work\\proj\\Settings.cs:\ntexto da linha cinco" + Tail + "</ide_selection>");
        Assert.Equal("Settings.cs#5-5", b!.Subject);
        Assert.StartsWith("texto da linha cinco", b.Content);
        Assert.DoesNotContain("The user selected", b.Content);
    }

    [Fact]
    public void A_selection_without_the_lines_is_the_bare_file_name_and_may_have_no_text()
    {
        Assert.Equal("b.py", ContextBlockReader.Read("<ide_selection>The user selected from /home/a/b.py:\nx</ide_selection>")!.Subject);
        var none = ContextBlockReader.Read("<ide_selection>The user selected from /home/a/b.py:</ide_selection>");
        Assert.Equal("b.py", none!.Subject);
        Assert.Equal("", none.Content);
    }

    [Fact]
    public void A_network_path_is_kept_whole_and_a_drive_path_is_split_at_either_slash()
    {
        Assert.Equal("\\\\servidor\\pasta\\a.cs",
            ContextBlockReader.Read("<ide_selection>The user selected from \\\\servidor\\pasta\\a.cs:\nx</ide_selection>")!.Subject);
        Assert.Equal("a.cs",
            ContextBlockReader.Read("<ide_selection>The user selected from C:/p\\a.cs:\nx</ide_selection>")!.Subject);
    }

    [Theory]
    [InlineData("<ide_selection>The user selected the lines a to 9 from /x.cs:\nx</ide_selection>")]
    [InlineData("<ide_selection>The user selected from /x.cs\nx</ide_selection>")]
    [InlineData("<ide_selection>The user selected from :\nx</ide_selection>")]
    [InlineData("<ide_selection>The user selected from /x.cs:\nx")]
    public void A_malformed_selection_is_no_block(string text) => Assert.Null(ContextBlockReader.Read(text));

    [Fact]
    public void The_browser_instructions_sample_keeps_everything_between_the_tags()
    {
        var body = "# Claude in Chrome browser automation\n\nYou have access ...\n tabs_context_mcp ";
        var b = ContextBlockReader.Read("<browser_instruction>" + body + "</browser_instruction>\n");
        Assert.Equal(ContextBlockReader.BrowserInstructions, b!.Kind);
        Assert.Equal("", b.Subject);
        Assert.Equal(body, b.Content);
        Assert.Equal("", ContextBlockReader.Read("<browser_instruction></browser_instruction>")!.Content);
        Assert.Null(ContextBlockReader.Read("<browser_instruction>never closed"));
    }

    [Fact]
    public void A_terminal_trims_one_line_feed_each_side_and_may_be_empty_or_have_blank_lines()
    {
        Assert.Equal("", ContextBlockReader.Read("<terminal name=\"t\"></terminal>")!.Content);
        Assert.Equal("", ContextBlockReader.Read("<terminal name=\"t\">\n\n</terminal>")!.Content);
        var b = ContextBlockReader.Read("<terminal name=\"build\">\na\n\nb <c>\n</terminal>  ");
        Assert.Equal("build", b!.Subject);
        Assert.Equal("a\n\nb <c>", b.Content);
        Assert.Equal("raw", ContextBlockReader.Read("<terminal name=\"t\">raw</terminal>")!.Content);
    }

    [Fact]
    public void A_tab_shows_host_and_path_only()
    {
        var withPath = ContextBlockReader.Read("<browser tabGroupId=\"g1\" tabId=\"42\">https://example.com/docs/page?q=1#f</browser>");
        Assert.Equal("example.com/docs/page", withPath!.Subject);
        Assert.Equal("https://example.com/docs/page?q=1#f", withPath.Content);
        Assert.Equal("example.com", ContextBlockReader.Read("<browser tabGroupId=\"g\" tabId=\"1\">https://example.com/</browser>")!.Subject);
        var none = ContextBlockReader.Read("<browser tabGroupId=\"g\" tabId=\"1\"></browser>");
        Assert.Equal("", none!.Subject);
        Assert.Equal("", none.Content);
    }

    [Fact]
    public void A_tab_whose_address_is_not_http_or_https_shows_the_bare_label()
    {
        var b = ContextBlockReader.Read("<browser tabGroupId=\"g\" tabId=\"1\">javascript:alert(1)</browser>");
        Assert.Equal("", b!.Subject);
        Assert.Equal("javascript:alert(1)", b.Content);
    }

    [Theory]
    [InlineData("<browser tabId=\"1\" tabGroupId=\"g\">x</browser>")]
    [InlineData("<browser tabGroupId=\"a:b\" tabId=\"1\">x</browser>")]
    [InlineData("<browser tabGroupId=\"g\" tabId=\"x\">x</browser>")]
    [InlineData("<browser tabGroupId=\"g\" tabId=\"12345678901234567\">x</browser>")]
    [InlineData("<browser tabGroupId=\"g\" tabId=\"1\">a b</browser>")]
    [InlineData("<browser tabGroupId=\"g\" tabId=\"1\">https://a.com")]
    public void A_malformed_tab_is_no_block(string text) => Assert.Null(ContextBlockReader.Read(text));

    [Fact]
    public void A_name_with_a_control_character_or_over_the_limit_is_no_block()
    {
        Assert.Null(ContextBlockReader.Read("<terminal name=\"a\u0007b\">\nx\n</terminal>"));
        Assert.Null(ContextBlockReader.Read("<terminal name=\"a\u202Eb\">\nx\n</terminal>"));
        Assert.Null(ContextBlockReader.Read("<terminal name=\"" + new string('n', 4097) + "\">\nx\n</terminal>"));
        Assert.NotNull(ContextBlockReader.Read("<terminal name=\"" + new string('n', 4096) + "\">\nx\n</terminal>"));
        Assert.Null(ContextBlockReader.Read("<ide_selection>The user selected from /" + new string('p', 4096) + ":\nx</ide_selection>"));
        Assert.Null(ContextBlockReader.Read("<browser tabGroupId=\"g\" tabId=\"1\">http://a.com/" + new string('p', 2048) + "</browser>"));
        Assert.Null(ContextBlockReader.Read("<terminal name=\"a\uD800b\">\nx\n</terminal>"));
        Assert.Null(ContextBlockReader.Read("<terminal name=\"a\uDB40\uDC41b\">\nx\n</terminal>"));
        Assert.NotNull(ContextBlockReader.Read("<terminal name=\"a\u200Eb\">\nx\n</terminal>"));
    }

    [Fact]
    public void A_tab_label_drops_the_port()
    {
        Assert.Equal("localhost/docs", ContextBlockReader.Read("<browser tabGroupId=\"g\" tabId=\"1\">http://localhost:3000/docs</browser>")!.Subject);
    }

    [Fact]
    public void Only_the_invisible_tag_characters_are_refused_not_the_variation_selectors_or_the_flags()
    {
        Assert.NotNull(ContextBlockReader.Read("<terminal name=\"a\U000E0100b\">\nx\n</terminal>"));
        Assert.NotNull(ContextBlockReader.Read("<terminal name=\"a\U0001F3F4\U000E0067\U000E0062\U000E0065\U000E006E\U000E0067\U000E007F\">\nx\n</terminal>"));
        Assert.Null(ContextBlockReader.Read("<terminal name=\"a\U000E0067b\">\nx\n</terminal>"));
    }

    [Fact]
    public void An_opening_without_its_closing_is_no_block()
    {
        Assert.Null(ContextBlockReader.Read("<terminal name=\"t\">\nx\n"));
        Assert.Null(ContextBlockReader.Read("<terminal name=\"t"));
        Assert.Null(ContextBlockReader.Read("<ide_selection>The user selected "));
        Assert.Null(ContextBlockReader.Read(null));
        Assert.Null(ContextBlockReader.Read("just a note"));
    }
}
