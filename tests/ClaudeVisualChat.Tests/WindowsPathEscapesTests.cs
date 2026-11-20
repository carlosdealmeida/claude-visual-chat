using ClaudeVisualChat.Controls;
using Xunit;

namespace ClaudeVisualChat.Tests;

public class WindowsPathEscapesTests
{
    [Fact]
    public void Text_without_a_path_comes_back_as_it_is()
    {
        var text = @"snake\_case and **bold** and C:";
        Assert.Same(text, WindowsPathEscapes.Keep(text));
    }

    [Fact]
    public void A_dotted_folder_is_left_alone()
    {
        var text = @"C:\work\.claude";
        Assert.Same(text, WindowsPathEscapes.Keep(text));
    }

    [Fact]
    public void A_fence_is_left_alone()
    {
        var text = "```\nC:\\a\\_b\n```\n\n`C:\\a\\_b`";
        Assert.Same(text, WindowsPathEscapes.Keep(text));
    }

    [Fact]
    public void A_backslash_before_an_underscore_or_an_asterisk_in_a_path_is_doubled()
    {
        Assert.Equal(@"see C:\a\\\_b and \\\\srv\s\\\*x", WindowsPathEscapes.Keep(@"see C:\a\_b and \\srv\s\*x"));
        Assert.Equal(@"snake\_case C:\a\\\_b", WindowsPathEscapes.Keep(@"snake\_case C:\a\_b"));
    }

    [Fact]
    public void The_delimiters_closing_an_emphasis_before_the_path_stay()
    {
        Assert.Equal(@"**C:\a\\\_b**", WindowsPathEscapes.Keep(@"**C:\a\_b**"));
    }

    [Theory]
    [InlineData(@"**C:\a\_b**:", @"**C:\a\\\_b**:")]
    [InlineData(@"(**C:\a\_b**)", @"(**C:\a\\\_b**)")]
    [InlineData(@"**C:\a\_b**.", @"**C:\a\\\_b**.")]
    public void Punctuation_after_the_closing_delimiters_leaves_them_alone(string markdown, string kept)
    {
        Assert.Equal(kept, WindowsPathEscapes.Keep(markdown));
    }

    [Fact]
    public void Text_after_a_closed_fence_is_still_looked_at()
    {
        Assert.Equal("```\nC:\\a\\_b\n```\nC:\\a\\\\\\_b", WindowsPathEscapes.Keep("```\nC:\\a\\_b\n```\nC:\\a\\_b"));
    }
}
