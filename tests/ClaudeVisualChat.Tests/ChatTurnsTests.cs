using Xunit;

namespace ClaudeVisualChat.Tests;

/// <summary>What the copy button under a turn takes: the turn's answers, joined.</summary>
public class ChatTurnsTests
{
    private static TurnFooterItem Footer() => new TurnFooterItem(null, null, null, null, false);

    [Fact]
    public void AnswerText_joins_the_turn_answers_with_a_blank_line()
    {
        var items = new ChatItem[] { new UserMessageItem("q"), new AssistantTextItem { Text = "one" },
            new ToolCallItem { Name = "Bash" }, new AssistantTextItem { Text = "two" }, Footer() };
        Assert.Equal("one\n\ntwo", ChatTurns.AnswerText(items, 4));
    }

    [Fact]
    public void AnswerText_stops_at_the_previous_turn()
    {
        var items = new ChatItem[] { new UserMessageItem("a"), new AssistantTextItem { Text = "old" }, Footer(),
            new UserMessageItem("b"), new AssistantTextItem { Text = "new" }, Footer() };
        Assert.Equal("new", ChatTurns.AnswerText(items, 5));
    }

    [Fact]
    public void AnswerText_without_answers_is_empty()
        => Assert.Equal("", ChatTurns.AnswerText(new ChatItem[] { new UserMessageItem("a"), Footer() }, 1));

    [Fact]
    public void AnswerText_takes_only_the_answers_not_thoughts_notices_or_command_output()
    {
        var items = new ChatItem[] { new UserMessageItem("q"), new ThinkingItem { Text = "hmm" },
            new AssistantTextItem { Text = "" }, new AssistantTextItem { Text = "one" }, new CommandOutputItem("out"),
            new NoticeItem(NoticeKind.Interrupted), Footer() };
        Assert.Equal("one", ChatTurns.AnswerText(items, 6));
    }

    [Fact]
    public void AnswerText_of_a_turn_that_follows_a_footer_without_a_message_starts_after_that_footer()
    {
        // text that arrives with no message before it (a background agent finishing) is a turn of its own
        var items = new ChatItem[] { new UserMessageItem("q"), new AssistantTextItem { Text = "first" }, Footer(),
            new AssistantTextItem { Text = "later" }, Footer() };
        Assert.Equal("later", ChatTurns.AnswerText(items, 4));
    }

    [Fact]
    public void LastAnswer_is_the_last_text_of_the_turn_with_something_written()
    {
        var two = new AssistantTextItem { Text = "two" };
        var items = new ChatItem[] { new UserMessageItem("a"), new AssistantTextItem { Text = "old" }, Footer(),
            new UserMessageItem("q"), new AssistantTextItem { Text = "one" }, two, new AssistantTextItem { Text = "" },
            new ToolCallItem { Name = "Bash" }, Footer() };
        Assert.Same(two, ChatTurns.LastAnswer(items, 8));
        Assert.Null(ChatTurns.LastAnswer(new ChatItem[] { new UserMessageItem("a"), new ThinkingItem { Text = "hmm" }, Footer() }, 2));
    }
}
