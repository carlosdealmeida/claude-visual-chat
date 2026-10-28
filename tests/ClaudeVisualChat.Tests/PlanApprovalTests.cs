using System.Collections.Generic;
using System.Text.Json;
using Xunit;
using ControlAcknowledged = ClaudeVisualChat.Protocol.ControlAcknowledged;
using PermissionCancelled = ClaudeVisualChat.Protocol.PermissionCancelled;
using PermissionRequested = ClaudeVisualChat.Protocol.PermissionRequested;
using StatusChanged = ClaudeVisualChat.Protocol.StatusChanged;
using TurnCompleted = ClaudeVisualChat.Protocol.TurnCompleted;

namespace ClaudeVisualChat.Tests;

/// <summary>
/// The "Yes" of the plan card (<see cref="ChatConversation.ApprovePlan"/>): the mode first, then the allow, at once and in that order,
/// as the claude 2.1.310 applies them in the order it reads them (the recording <c>plan</c>); a refused auto mode asks for
/// acceptEdits; and the plan under review keeps how it ended (<see cref="PlanReview.Outcome"/>).
/// </summary>
public class PlanApprovalTests
{
    private static JsonElement J(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static PermissionRequested ExitPlanMode(string id = "r1") =>
        new PermissionRequested(id, "ExitPlanMode", "ExitPlanMode", null, "tool-" + id,
            J("{\"plan\":\"# Plan\\n\\n1. Do it\",\"planFilePath\":\"C:\\\\Users\\\\demo\\\\.claude\\\\plans\\\\p.md\"}"), null);

    /// <summary>A conversation with an ExitPlanMode request, and what it sends the host, in order ("mode:x", "allow:r1"…).</summary>
    private static (ChatConversation Conversation, List<string> Sent) WithPlan()
    {
        var c = new ChatConversation();
        var sent = new List<string>();
        c.PermissionModeRequested += (_, e) => sent.Add("mode:" + e.Mode);
        c.PermissionAnswered += (_, e) => sent.Add(e.Decision.ToString().ToLowerInvariant() + ":" + e.Request.RequestId
                                                  + (e.Message == null ? "" : ":" + e.Message));
        c.Apply(ExitPlanMode());
        return (c, sent);
    }

    [Fact]
    public void Approve_asks_the_mode_first_and_allows_right_after()
    {
        var (c, sent) = WithPlan();

        c.ApprovePlan(c.CurrentPermission!, "acceptEdits");

        Assert.Equal(new[] { "mode:acceptEdits", "allow:r1" }, sent); // no waiting for the claude's confirmation
        Assert.Null(c.CurrentPermission);
        c.Apply(new StatusChanged(null, "acceptEdits"));
        Assert.Equal("acceptEdits", c.PermissionMode);
        Assert.Equal(2, sent.Count);
    }

    [Fact]
    public void Approve_with_the_default_mode_asks_for_it_too()
    {
        var (c, sent) = WithPlan();

        c.ApprovePlan(c.CurrentPermission!, "default");

        Assert.Equal(new[] { "mode:default", "allow:r1" }, sent);
    }

    [Fact]
    public void A_refused_auto_falls_back_to_accept_edits()
    {
        var (c, sent) = WithPlan();
        c.ApprovePlan(c.CurrentPermission!, "auto");
        Assert.Equal(new[] { "mode:auto", "allow:r1" }, sent);

        c.Apply(new ControlAcknowledged("x", true, null)); // the claude refused the auto mode

        Assert.Equal(new[] { "mode:auto", "allow:r1", "mode:acceptEdits" }, sent);
        c.Apply(new ControlAcknowledged("y", true, null)); // asked once: a second error asks nothing more
        Assert.Equal(3, sent.Count);
    }

    [Fact]
    public void An_accepted_auto_asks_nothing_more_on_a_later_error()
    {
        var (c, sent) = WithPlan();
        c.ApprovePlan(c.CurrentPermission!, "auto");
        c.Apply(new ControlAcknowledged("x", false, J("{\"mode\":\"auto\"}")));
        c.Apply(new StatusChanged(null, "auto"));

        c.Apply(new ControlAcknowledged("y", true, null)); // some other request of the host failed

        Assert.Equal(new[] { "mode:auto", "allow:r1" }, sent);
        Assert.Equal("auto", c.PermissionMode);
    }

    [Fact]
    public void An_error_after_the_turn_ended_asks_nothing()
    {
        var (c, sent) = WithPlan();
        c.ApprovePlan(c.CurrentPermission!, "auto");
        c.Apply(new TurnCompleted("success", false, 10, 0.01, 1, 1));

        c.Apply(new ControlAcknowledged("x", true, null));

        Assert.Equal(new[] { "mode:auto", "allow:r1" }, sent);
    }

    [Fact]
    public void An_error_without_an_auto_request_asks_nothing()
    {
        var (c, sent) = WithPlan();
        c.ApprovePlan(c.CurrentPermission!, "acceptEdits");

        c.Apply(new ControlAcknowledged("x", true, null));

        Assert.Equal(new[] { "mode:acceptEdits", "allow:r1" }, sent);
    }

    [Fact]
    public void A_cancelled_request_is_not_approved()
    {
        var (c, sent) = WithPlan();
        var request = c.CurrentPermission!;
        c.Apply(new PermissionCancelled("r1"));

        c.ApprovePlan(request, "auto");

        Assert.Empty(sent); // the claude no longer waits: neither the mode nor the allow
        c.Apply(new ControlAcknowledged("x", true, null));
        Assert.Empty(sent);
    }

    [Fact]
    public void The_current_plan_follows_the_exit_plan_mode_request()
    {
        var c = new ChatConversation();
        Assert.Null(c.CurrentPlan);
        c.Apply(new PermissionRequested("b1", "Bash", "Bash", null, "tool-b1", J("{\"command\":\"ls\"}"), null));
        Assert.Null(c.CurrentPlan); // not a plan

        c.Apply(ExitPlanMode("r1"));
        Assert.Null(c.CurrentPlan); // a plan, but not on screen yet
        c.AnswerPermission(c.CurrentPermission!, PermissionDecision.Allow);
        var plan = c.CurrentPlan;
        Assert.NotNull(plan);
        Assert.Equal("r1", plan!.Request.RequestId);
        Assert.Null(plan.Outcome); // pending

        c.Apply(new PermissionCancelled("r1"));
        Assert.Null(c.CurrentPlan);
        Assert.Null(plan.Outcome); // cancelled: nobody answered it
    }

    [Fact]
    public void The_plan_keeps_how_it_was_answered()
    {
        var (c, _) = WithPlan();
        var approved = c.CurrentPlan!;
        var changes = new List<string?>();
        approved.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        c.ApprovePlan(c.CurrentPermission!, "acceptEdits");

        Assert.Equal(PlanOutcome.Approved, approved.Outcome);
        Assert.Contains(nameof(PlanReview.Outcome), changes);
        Assert.Null(c.CurrentPlan);

        c.Apply(ExitPlanMode("r2"));
        var refused = c.CurrentPlan!;
        Assert.NotSame(approved, refused);
        c.AnswerPermission(c.CurrentPermission!, PermissionDecision.Deny, "Use a list instead of a table.");
        Assert.Equal(PlanOutcome.Rejected, refused.Outcome);
        Assert.Equal(PlanOutcome.Approved, approved.Outcome);
    }
}
