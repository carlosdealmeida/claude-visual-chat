using ClaudeVisualChat.Protocol;
using Xunit;

namespace ClaudeVisualChat.Tests;

public class AnsiTextTests
{
    [Fact]
    public void Strip_removes_csi_osc_and_two_character_escapes()
    {
        Assert.Equal("ok done", AnsiText.Strip("\u001b[32mok\u001b[0m \u001b]0;title\u0007done"));
    }

    [Fact]
    public void Strip_removes_every_kind_of_sequence()
    {
        Assert.Equal("ab", AnsiText.Strip("a\u001b[1;31;4mb"));                 // CSI with parameters
        Assert.Equal("ab", AnsiText.Strip("a\u001b[?25lb"));                     // private-mode CSI
        Assert.Equal("ab", AnsiText.Strip("a\u001b[2Kb"));                       // erase line
        Assert.Equal("ab", AnsiText.Strip("a\u009b32mb"));                       // the 8-bit form of CSI
        Assert.Equal("ab", AnsiText.Strip("a\u001b]8;;https://x.test\u001b\\b")); // OSC ended by ESC \
        Assert.Equal("ab", AnsiText.Strip("a\u001b]0;title\u0007b"));            // OSC ended by BEL
        Assert.Equal("ab", AnsiText.Strip("a\u001b(Bb"));                        // character set selection
        Assert.Equal("ab", AnsiText.Strip("a\u001b)0b"));
        Assert.Equal("ab", AnsiText.Strip("a\u001b=b"));                         // two-character sequences
        Assert.Equal("ab", AnsiText.Strip("a\u001b7b"));
        Assert.Equal("ab", AnsiText.Strip("a\u001bMb"));
        Assert.Equal("ab", AnsiText.Strip("a\u001bcb"));
    }

    [Fact]
    public void Strip_keeps_text_without_escapes_as_it_is()
    {
        Assert.Equal("", AnsiText.Strip(""));
        Assert.Equal("[32m not an escape\r\n\ttab", AnsiText.Strip("[32m not an escape\r\n\ttab"));
        Assert.Equal("ünïcode ✓ line\nline", AnsiText.Strip("ünïcode ✓ line\nline"));
    }

    [Fact]
    public void Strip_works_on_a_whole_colored_output()
    {
        var output = "\u001b[1m\u001b[34mbuild\u001b[0m: \u001b[32mok\u001b[0m\n\u001b[2K\u001b[1Gdone\n";
        Assert.Equal("build: ok\ndone\n", AnsiText.Strip(output));
    }
}
