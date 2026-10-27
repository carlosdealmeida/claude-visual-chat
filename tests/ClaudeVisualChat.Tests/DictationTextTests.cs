using ClaudeVisualChat.Controls;
using Xunit;

namespace ClaudeVisualChat.Tests;

/// <summary>Where dictated words go in the reply box's text.</summary>
public class DictationTextTests
{
    [Theory]
    [InlineData("Run the tests", 13, "and tell me", "Run the tests and tell me", 25)]
    [InlineData("", 0, "hello", "hello", 5)]
    [InlineData("abc", 0, "x", "x abc", 1)]
    [InlineData("abc ", 4, "x", "abc x", 5)]
    [InlineData("abc", 3, "  ", "abc", 3)]
    [InlineData("abc", 3, null, "abc", 3)]
    public void Insert_puts_the_words_at_the_caret_with_one_space_around(string text, int caret, string? spoken, string expected, int expectedCaret)
        => Assert.Equal((expected, expectedCaret), DictationText.Insert(text, caret, spoken));

    [Theory]
    [InlineData("abc\n", 4, "x", "abc\nx", 5)]            // a line break is a space already
    [InlineData("ab cd", 3, "x", "ab x cd", 4)]           // between two words: the space before is there, the one after is not
    [InlineData("abc", 1, "x", "a x bc", 3)]              // inside a word: a space on each side
    [InlineData("abc  def", 3, "x", "abc x  def", 5)]     // the space after is there already
    [InlineData("abc", 3, "  hello  ", "abc hello", 9)]   // the spoken words are trimmed
    [InlineData("abc", 99, "x", "abc x", 5)]              // a caret past the end is the end
    [InlineData("abc", -2, "x", "x abc", 1)]              // and one before the start is the start
    public void Insert_reads_the_neighbours_of_the_caret_and_clamps_it(string text, int caret, string? spoken, string expected, int expectedCaret)
        => Assert.Equal((expected, expectedCaret), DictationText.Insert(text, caret, spoken));
}
