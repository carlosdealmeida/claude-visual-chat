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
}
