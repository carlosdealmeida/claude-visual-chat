using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ClaudeVisualChat.Tests;

/// <summary>What counts as the word <c>ultrathink</c> in the reply box (<see cref="UltrathinkKeyword.Find"/>).</summary>
public class UltrathinkKeywordTests
{
    private static readonly IReadOnlyList<(int Start, int Length)> None = Array.Empty<(int, int)>();

    private static List<(int Start, int Length)> Find(string text) => UltrathinkKeyword.Find(text, None).ToList();

    [Fact]
    public void The_word_alone_in_a_sentence_is_found_where_it_is()
    {
        Assert.Equal(new[] { (7, 10) }, Find("please ultrathink about it"));
        Assert.Equal(new[] { (0, 10) }, Find("ultrathink"));
        Assert.Equal(new[] { (6, 10) }, Find("first\nultrathink\nthen"));
    }

    [Fact]
    public void Case_does_not_matter_and_every_occurrence_counts()
    {
        Assert.Equal(new[] { (0, 10), (15, 10) }, Find("ULTRATHINK and Ultrathink"));
        Assert.Equal(new[] { (0, 10), (11, 10), (22, 10) }, Find("ultrathink ultrathink uLtRaThInK"));
    }

    [Fact]
    public void A_text_that_is_a_command_or_too_long_has_none()
    {
        Assert.Empty(Find("/ultrathink"));
        Assert.Empty(Find("  /review ultrathink"));
        Assert.Empty(Find("ultrathink " + new string('a', 50_000)));
        // the limit is on the text trimmed: spaces around do not count
        var atLimit = "ultrathink " + new string('a', 50_000 - 11);
        Assert.Single(Find("   " + atLimit + "   "));
    }

    [Fact]
    public void Word_boundaries_on_both_sides()
    {
        Assert.Empty(Find("ultrathinking"));
        Assert.Empty(Find("ultrathink_x"));
        Assert.Empty(Find("ultrathink2"));
        Assert.Empty(Find("xultrathink"));
        Assert.Empty(Find("_ultrathink"));
        Assert.Empty(Find("2ultrathink"));
        Assert.Single(Find("ultrathink, please"));
        Assert.Single(Find("ok:ultrathink!"));
    }

    [Theory]
    [InlineData("`ultrathink`")]
    [InlineData("run `please ultrathink now` here")]
    [InlineData("\"ultrathink\"")]
    [InlineData("{ultrathink}")]
    [InlineData("[ultrathink]")]
    [InlineData("(ultrathink)")]
    [InlineData("say (do not ultrathink) now")]
    [InlineData("'ultrathink'")]
    [InlineData("a 'quiet ultrathink' b")]
    [InlineData("<ultrathink>")]
    [InlineData("</ultrathink>")]
    public void Inside_quotes_or_brackets_it_does_not_count(string text) => Assert.Empty(Find(text));

    [Fact]
    public void An_opener_without_a_closer_makes_no_span()
    {
        Assert.Single(Find("`ultrathink"));
        Assert.Single(Find("\"ultrathink"));
        Assert.Single(Find("(ultrathink"));
        Assert.Single(Find("[ultrathink"));
        Assert.Single(Find("{ultrathink"));
        // after a span that closed, the word counts again
        Assert.Equal(new[] { (6, 10) }, Find("(a b) ultrathink"));
    }

    [Fact]
    public void A_single_quote_opens_only_after_a_non_word_character_and_closes_only_before_one()
    {
        // the apostrophes of "don't" and "it's" are neither: the word between them counts
        Assert.Single(Find("don't ultrathink, it's fine"));
        // an opener whose only closer is an apostrophe makes no span
        Assert.Single(Find("'ultrathink isn't"));
        // a real pair after an apostrophe
        Assert.Empty(Find("it's 'ultrathink' now"));
    }

    [Fact]
    public void An_angle_bracket_opens_only_before_a_letter_or_a_slash()
    {
        Assert.Single(Find("a < ultrathink > b"));
        Assert.Single(Find("1<2 ultrathink 3>2"));
        Assert.Empty(Find("<x ultrathink>"));
        Assert.Empty(Find("</ ultrathink>"));
        // a tag is a span of its own: the word between two tags counts
        Assert.Single(Find("<b>ultrathink</b> x"));
    }

    [Theory]
    [InlineData("/ultrathink x", 0)]
    [InlineData("go /ultrathink", 0)]
    [InlineData("go \\ultrathink", 0)]
    [InlineData("go -ultrathink", 0)]
    [InlineData("ultrathink/ x", 0)]
    [InlineData("ultrathink\\ x", 0)]
    [InlineData("ultrathink- x", 0)]
    [InlineData("ultrathink? x", 0)]
    [InlineData("ultrathink.md", 0)]
    [InlineData("ultrathink.2", 0)]
    [InlineData("think. ultrathink.", 1)]
    [InlineData("ultrathink. Then", 1)]
    [InlineData("ultrathink.", 1)]
    [InlineData("ultrathink!", 1)]
    [InlineData("ultrathink: go", 1)]
    public void Path_and_flag_neighbours_do_not_count(string text, int count) => Assert.Equal(count, Find(text).Count);

    [Fact]
    public void Masked_ranges_are_blanked_before_the_search()
    {
        var text = "ultrathink PASTE ultrathink tail ultrathink";
        // the second occurrence lies in a long paste: it does not count, and the others keep their places
        var found = UltrathinkKeyword.Find(text, new[] { (11, 21) }).ToList();
        Assert.Equal(new[] { (0, 10), (33, 10) }, found);
        // a paste that leaves a word next to blanks: the blanks are spaces, a boundary
        Assert.Equal(new[] { (5, 10) }, UltrathinkKeyword.Find("PASTEultrathinkPASTE", new[] { (0, 5), (15, 5) }).ToList());
        // ranges out of the text are ignored
        Assert.Single(UltrathinkKeyword.Find("ultrathink", new[] { (20, 5), (-3, 2) }));
    }

    [Fact]
    public void A_masked_bracket_does_not_open_a_span()
    {
        // a "(" inside a paste is not the user's: the word after it counts
        Assert.Single(UltrathinkKeyword.Find("x( ultrathink )", new[] { (1, 1) }));
    }
}
