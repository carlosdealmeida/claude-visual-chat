using System.Linq;
using ClaudeVisualChat.Tests.Ui;
using Xunit;

namespace ClaudeVisualChat.Tests;

/// <summary>How the touch layout's model sheet groups the claude's models: the main ones, and the others.</summary>
public class ModelGroupsTests
{
    private static ChatModel Model(string value, string resolved, string name) =>
        new ChatModel(value, resolved, name, "", new string[0]);

    [Fact]
    public void Split_takes_the_first_of_each_family_and_drops_default()
    {
        var all = new[] { new ChatModel("default", "claude-opus-5-5", "Default (recommended)", "", new string[0]) }
            .Concat(TouchSamples.Models).ToList();
        var (main, others) = ModelGroups.Split(all);
        Assert.Equal(new[] { "Opus 5.5", "Fable 5.1", "Sonnet 5.5", "Haiku 4.5" }, main.Select(m => m.DisplayName));
        Assert.Equal(new[] { "Sonnet 5", "Opus 5", "Fable 5", "Opus 4.8" }, others.Select(m => m.DisplayName));
    }

    [Fact]
    public void Split_puts_a_model_without_family_in_others()
    {
        var (main, others) = ModelGroups.Split(new[] { new ChatModel("x", "mystery-1", "Mystery", "", new string[0]) });
        Assert.Empty(main);
        Assert.Single(others);
    }

    [Fact]
    public void The_family_is_the_second_piece_of_the_resolved_model_and_the_order_is_the_claudes()
    {
        // the first of a family wherever it is in the list, the rest of it after; none of them moved
        var all = new[]
        {
            Model("sonnet", "claude-sonnet-5-5", "Sonnet 5.5"),
            Model("opus-old", "claude-opus-4-8", "Opus 4.8"),
            Model("opus", "claude-opus-5-5", "Opus 5.5"),
            Model("haiku", "claude-haiku-4-5-20251001", "Haiku 4.5"),
            Model("sonnet-old", "claude-sonnet-5", "Sonnet 5"),
        };
        var (main, others) = ModelGroups.Split(all);
        Assert.Equal(new[] { "Sonnet 5.5", "Opus 4.8", "Haiku 4.5" }, main.Select(m => m.DisplayName));
        Assert.Equal(new[] { "Opus 5.5", "Sonnet 5" }, others.Select(m => m.DisplayName));
    }

    [Fact]
    public void Default_is_dropped_wherever_it_is_and_whatever_it_resolves_to()
    {
        var all = new[]
        {
            Model("opus", "claude-opus-5-5", "Opus 5.5"),
            Model("default", "claude-opus-5-5", "Default (recommended)"),
            Model("default", "", "Default"),
        };
        var (main, others) = ModelGroups.Split(all);
        Assert.Equal(new[] { "Opus 5.5" }, main.Select(m => m.DisplayName));
        Assert.Empty(others);
    }

    [Theory]
    [InlineData("")]
    [InlineData("claude")]
    [InlineData("claude-")]
    [InlineData("opus-5-5")]          // the prefix is part of the rule
    [InlineData("Claude-opus-5-5")]
    public void An_id_that_does_not_name_a_family_is_in_others(string resolved)
    {
        var (main, others) = ModelGroups.Split(new[] { Model("x", resolved, "X") });
        Assert.Empty(main);
        Assert.Single(others);
    }

    [Fact]
    public void A_family_with_only_its_name_is_a_family()
    {
        var (main, _) = ModelGroups.Split(new[] { Model("x", "claude-opus", "Opus") });
        Assert.Single(main);
    }

    [Fact]
    public void Nothing_to_split_gives_two_empty_groups()
    {
        var (main, others) = ModelGroups.Split(new ChatModel[0]);
        Assert.Empty(main);
        Assert.Empty(others);
    }
}
