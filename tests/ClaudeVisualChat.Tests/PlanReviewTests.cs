using System.Linq;
using System.Text.Json;
using Xunit;
using PermissionCancelled = ClaudeVisualChat.Protocol.PermissionCancelled;
using PermissionRequested = ClaudeVisualChat.Protocol.PermissionRequested;
using AssistantBlock = ClaudeVisualChat.Protocol.AssistantBlock;
using ToolUseBlock = ClaudeVisualChat.Protocol.ToolUseBlock;

namespace ClaudeVisualChat.Tests;

/// <summary>
/// The plan of an <c>ExitPlanMode</c> request as the document shows it (<see cref="PlanReview"/>): its Markdown, the name of its file
/// and its title, taken as the extension takes it; and the conversation's plan under review, which follows the request.
/// </summary>
public class PlanReviewTests
{
    private static JsonElement J(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static string Input(string plan, string? path = null) =>
        "{\"plan\":" + JsonSerializer.Serialize(plan) + (path == null ? "" : ",\"planFilePath\":" + JsonSerializer.Serialize(path)) + "}";

    private static PlanReview Review(string plan, string? path = null) =>
        new PlanReview(new PendingPermission("r1", "ExitPlanMode", "ExitPlanMode", null, "t1", "", J(Input(plan, path)), null));

    private static string Title(string plan) => Review(plan).Title;

    [Fact]
    public void The_title_is_the_first_level_one_heading()
    {
        Assert.Equal("Rewrite the parser", Title("\n# Rewrite the parser ##\n\n## Steps\n1. Read it"));
        Assert.Equal("Rewrite the parser", Title("  \n\t\n# Rewrite the parser\r\n"));   // blank lines first, Windows ends
        Assert.Equal("Rewrite the parser", Review("\n# Rewrite the parser ##\n…").Heading);
    }

    [Fact]
    public void Without_a_heading_the_title_is_claudes_plan_with_a_curly_apostrophe()
    {
        var t = Title("Rewrite the parser\n\n# Later heading");                          // the first line is no heading
        Assert.Equal("Claude\u2019s Plan", t);
        Assert.Equal("Claude\u2019s Plan", Title("## Steps\n1. Read it"));                // a level-two heading is not the title
        Assert.Equal("Claude\u2019s Plan", Title("#Rewrite"));                           // no space: no heading
        Assert.Equal("Claude\u2019s Plan", Title("# ##"));                               // nothing left of it
        Assert.Equal("Claude\u2019s Plan", Title(""));
        Assert.Equal("Claude\u2019s Plan", Title("# " + new string('a', 201)));          // too long for a title
        Assert.Equal(new string('a', 200), Title("# " + new string('a', 200)));
        Assert.Null(Review("Rewrite the parser").Heading);
    }

    [Fact]
    public void The_markdown_and_the_file_name_come_from_the_input()
    {
        var review = Review("# Plan\n\n1. Do it", "C:\\Users\\demo\\.claude\\plans\\plan-a-small-change-delegated-dove.md");
        Assert.Equal("# Plan\n\n1. Do it", review.Markdown);
        Assert.Equal("plan-a-small-change-delegated-dove.md", review.FileName);
        Assert.False(review.IsReadOnly);

        Assert.Equal("p.md", Review("x", "/home/demo/.claude/plans/p.md").FileName);  // the other slash
        Assert.Null(Review("x").FileName);                                            // only the plan
        Assert.Null(Review("x", "").FileName);
    }

    [Fact]
    public void A_plan_from_the_history_is_read_only_and_needs_its_text()
    {
        var review = PlanReview.ReadOnly(J(Input("# Plan\n\n1. Do it", "C:\\plans\\p.md")));
        Assert.NotNull(review);
        Assert.True(review!.IsReadOnly);
        Assert.Null(review.Request);
        Assert.Equal("Plan", review.Title);
        Assert.Equal("p.md", review.FileName);

        Assert.Null(PlanReview.ReadOnly(J("{}")));                                     // the call came without its plan
        Assert.Null(PlanReview.ReadOnly(default));
    }

    [Fact]
    public void The_current_plan_follows_the_exit_plan_mode_request()
    {
        var c = new ChatConversation();
        c.Apply(new PermissionRequested("r1", "ExitPlanMode", "ExitPlanMode", null, "t1",
            J(Input("# Rewrite the parser\n\n1. Read it", "C:\\plans\\rewrite.md")), null));

        var plan = c.CurrentPlan;
        Assert.NotNull(plan);
        Assert.Same(c.CurrentPermission, plan!.Request);
        Assert.Equal("Rewrite the parser", plan.Title);
        Assert.Equal("rewrite.md", plan.FileName);

        c.Apply(new PermissionCancelled("r1"));
        Assert.Null(c.CurrentPlan);
    }

    [Fact]
    public void The_call_takes_the_plan_of_the_request_when_it_came_without_it()
    {
        // the recording plan-auto: the call's input is {}, and only the request carries the plan
        var c = new ChatConversation();
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("t1", "ExitPlanMode", J("{}"))));
        var call = c.Items.OfType<ToolCallItem>().Single();
        Assert.Equal("", call.Summary);

        c.Apply(new PermissionRequested("r1", "ExitPlanMode", "ExitPlanMode", null, "t1",
            J(Input("# Plan\n\n1. Do it", "C:\\plans\\p.md")), null));

        Assert.Equal("# Plan\n\n1. Do it", call.Input.GetProperty("plan").GetString());
        Assert.Equal("p.md", call.Summary);                                            // the file's name, never the plan's text
    }

    [Fact]
    public void The_summary_of_exit_plan_mode_is_the_file_name()
    {
        Assert.Equal("p.md", ToolPresentation.Summary("ExitPlanMode", J(Input("# Plan", "C:\\plans\\p.md")), null));
        Assert.Equal("", ToolPresentation.Summary("ExitPlanMode", J(Input("# Plan")), null)); // not the plan's text
    }

    // ---- the comments on the plan (Task 8) ----

    private const string Stay = "User chose to stay in plan mode and continue planning";

    [Fact]
    public void Feedback_lists_each_comment_under_comments_on_the_plan()
    {
        var plan = Review("# Plan\n\n## Steps\n\nUse a table");
        plan.AddComment("Use a table", "Steps", "Prefer a list");
        Assert.Equal(Stay + "\n\nComments on the plan:\n[Re: \"Use a table\"] Prefer a list", plan.Feedback(null));

        plan.AddComment("  Steps  ", null, "  Name them  ");                              // trimmed
        Assert.Equal("Shorter, please\n\nComments on the plan:\n[Re: \"Use a table\"] Prefer a list\n[Re: \"Steps\"] Name them",
            plan.Feedback("  Shorter, please \n"));
        Assert.Equal(Stay + "\n\nComments on the plan:\n[Re: \"Use a table\"] Prefer a list\n[Re: \"Steps\"] Name them", plan.Feedback("   "));
        Assert.Equal(new PlanComment("Steps", null, "Name them"), plan.Comments[1]);
        Assert.Equal("Steps", plan.Comments[0].Heading);
    }

    [Fact]
    public void Long_quotes_are_shortened_so_the_feedback_fits_four_thousand_characters()
    {
        var plan = Review("# Plan");
        foreach (var letter in "abc") plan.AddComment(new string(letter, 3000), null, "Comment " + letter);

        var feedback = plan.Feedback(null);
        Assert.True(feedback.Length <= 4000, "the feedback has " + feedback.Length);
        Assert.True(feedback.Length >= 3990, "the quotes were cut more than needed: " + feedback.Length);
        var lines = feedback.Split('\n');
        Assert.Equal(Stay, lines[0]);
        Assert.Equal("Comments on the plan:", lines[2]);
        var quotes = lines.Skip(3).Select(l => l.Substring(6, l.IndexOf("\"] ", System.StringComparison.Ordinal) - 6)).ToList();
        Assert.Equal(3, quotes.Count);
        Assert.All(quotes, q => Assert.EndsWith("…", q));                                  // every quote is cut…
        Assert.Single(quotes.Select(q => q.Length).Distinct());                            // …to the same length
        for (var i = 0; i < 3; i++) Assert.EndsWith("\"] Comment " + "abc"[i], lines[3 + i]); // the comments whole

        // short quotes stay whole; only the long one is cut
        plan = Review("# Plan");
        plan.AddComment("short", null, "ok");
        plan.AddComment(new string('x', 5000), null, "too long");
        feedback = plan.Feedback(null);
        Assert.True(feedback.Length <= 4000);
        Assert.Contains("[Re: \"short\"] ok", feedback);
        Assert.Contains("…\"] too long", feedback);
        Assert.Equal(4000, plan.Feedback(null, 4000).Length);
    }

    [Fact]
    public void Long_comments_and_a_long_typed_text_are_cut_after_the_quotes()
    {
        // the comments are too long even with every quote cut to "…": they are cut next, to the same length
        var plan = Review("# Plan");
        plan.AddComment(new string('q', 100), null, new string('a', 3000));
        plan.AddComment(new string('r', 100), null, new string('b', 3000));
        var feedback = plan.Feedback("keep it short");
        Assert.True(feedback.Length <= 4000, "the feedback has " + feedback.Length);
        Assert.StartsWith("keep it short\n\nComments on the plan:\n[Re: \"…\"] aaa", feedback);  // the quotes first, to "…"
        Assert.Contains("\n[Re: \"…\"] bbb", feedback);
        Assert.EndsWith("…", feedback);
        var comments = feedback.Split('\n').Skip(3).Select(l => l.Substring("[Re: \"…\"] ".Length)).ToList();
        Assert.Single(comments.Select(c => c.Length).Distinct());

        // a typed text over the ceiling: the quotes and comments go down to "…", then the text is cut
        plan = Review("# Plan");
        plan.AddComment("Use a table", null, "Prefer a list");
        feedback = plan.Feedback(new string('t', 5000));
        Assert.Equal(4000, feedback.Length);
        Assert.EndsWith("\n\nComments on the plan:\n[Re: \"…\"] …", feedback);
        Assert.StartsWith("ttt", feedback);

        // so many comments that even all of them at "…" do not fit: the message itself is cut, at 4,000
        plan = Review("# Plan");
        for (var i = 0; i < 400; i++) plan.AddComment("q" + i, null, "c" + i);
        Assert.True(plan.Feedback(null).Length <= 4000);
    }

    [Fact]
    public void No_cut_parts_a_surrogate_pair()
    {
        var emoji = "\U0001F600";                                                           // two chars
        var plan = Review("# Plan");
        plan.AddComment(string.Concat(Enumerable.Repeat(emoji, 3000)), null, string.Concat(Enumerable.Repeat(emoji, 10)));
        foreach (var max in new[] { 200, 201, 202, 203 })
        {
            var feedback = plan.Feedback(string.Concat(Enumerable.Repeat(emoji, 3000)), max);
            Assert.True(feedback.Length <= max);
            for (var i = 0; i < feedback.Length; i++)
            {
                if (char.IsHighSurrogate(feedback[i])) Assert.True(i + 1 < feedback.Length && char.IsLowSurrogate(feedback[i + 1]), "a high surrogate alone at " + i + " (max " + max + ")");
                if (char.IsLowSurrogate(feedback[i])) Assert.True(i > 0 && char.IsHighSurrogate(feedback[i - 1]), "a low surrogate alone at " + i + " (max " + max + ")");
            }
        }
        Assert.Equal("a…", PlanReview.Shorten("a" + emoji + "b", 3));                      // never half of the pair
        Assert.Equal("a" + emoji + "…", PlanReview.Shorten("a" + emoji + "bc", 4));
    }

    [Fact]
    public void An_empty_comment_is_ignored()
    {
        var plan = Review("# Plan");
        plan.AddComment("Use a table", "Plan", "");
        plan.AddComment("Use a table", "Plan", "   \n ");
        Assert.Empty(plan.Comments);
        Assert.Equal(Stay, plan.Feedback(null));                                           // no comments: only the first line
        Assert.Equal("Do it again", plan.Feedback(" Do it again "));

        // a plan only to read takes no comments
        var history = PlanReview.ReadOnly(J(Input("# Plan")))!;
        history.AddComment("Plan", null, "Nice");
        Assert.Empty(history.Comments);
    }

    [Fact]
    public void Remove_comment_takes_that_very_comment()
    {
        var plan = Review("# Plan");
        plan.AddComment("Plan", null, "Same");
        plan.AddComment("Plan", null, "Same");
        var second = plan.Comments[1];
        Assert.Equal(plan.Comments[0], second);                                            // equal words…
        plan.RemoveComment(second);
        Assert.Single(plan.Comments);
        Assert.NotSame(second, plan.Comments[0]);                                          // …but the one asked for goes
        plan.RemoveComment(second);                                                         // gone already: nothing
        Assert.Single(plan.Comments);
    }
}
