using System;

namespace ClaudeVisualChat;

/// <summary>How the user answered a plan (<see cref="PlanReview.Outcome"/>).</summary>
internal enum PlanOutcome
{
    /// <summary>One of the plan card's "Yes": the claude leaves plan mode and starts on it.</summary>
    Approved,

    /// <summary>"No, keep planning", with or without feedback: the claude stays in plan mode.</summary>
    Rejected,
}

/// <summary>
/// The plan of an <c>ExitPlanMode</c> request while it waits for the user (<see cref="ChatConversation.CurrentPlan"/>). It stays
/// with whoever keeps it after the request leaves the queue, and tells how it ended.
/// </summary>
internal sealed class PlanReview : ChatObservableObject
{
    private PlanOutcome? _outcome;

    internal PlanReview(PendingPermission request)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
    }

    /// <summary>The <c>ExitPlanMode</c> request.</summary>
    internal PendingPermission Request { get; }

    /// <summary>
    /// Null while the request waits, and when it left the queue unanswered (cancelled, or the turn ended); then
    /// <see cref="PlanOutcome.Approved"/> or <see cref="PlanOutcome.Rejected"/>, as the user answered it.
    /// </summary>
    internal PlanOutcome? Outcome
    {
        get => _outcome;
        set => SetField(ref _outcome, value);
    }
}
