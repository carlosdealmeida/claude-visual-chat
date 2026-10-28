using System;
using System.Text.Json;

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
/// The plan of an <c>ExitPlanMode</c> request while it waits for the user (<see cref="ChatConversation.CurrentPlan"/>), or one
/// of the timeline opened again to be read (<see cref="ReadOnly"/>). It stays with whoever keeps it after the request leaves the
/// queue, and tells how it ended. What the plan's document shows comes from the call's input: the Markdown (<c>plan</c>), the
/// name of its file (<c>planFilePath</c>) and its title.
/// </summary>
internal sealed class PlanReview : ChatObservableObject
{
    /// <summary>The title of a plan whose first line is no level-one heading, as the extension names it (with U+2019).</summary>
    internal const string DefaultTitle = "Claude’s Plan";

    /// <summary>The longest heading that is the plan's title; a longer one leaves it <see cref="DefaultTitle"/>.</summary>
    internal const int MaxTitleLength = 200;

    private PlanOutcome? _outcome;

    internal PlanReview(PendingPermission request)
        : this(request ?? throw new ArgumentNullException(nameof(request)), request.Input)
    {
    }

    private PlanReview(PendingPermission? request, JsonElement input)
    {
        Request = request;
        Markdown = PlanText(input) ?? "";
        FileName = ToolPresentation.PlanFileName(input);
        Heading = HeadingOf(Markdown);
    }

    /// <summary>
    /// A plan of the timeline to read again (a history, or a plan already answered): no request, so no comments. Null when the
    /// call carries no plan's text.
    /// </summary>
    internal static PlanReview? ReadOnly(JsonElement input) =>
        PlanText(input) == null ? null : new PlanReview(null, input);

    /// <summary>True when the input carries the plan's text (<c>plan</c>), which is what a document needs.</summary>
    internal static bool HasPlan(JsonElement input) => PlanText(input) != null;

    /// <summary>The <c>ExitPlanMode</c> request; null for a plan opened again from the timeline (<see cref="IsReadOnly"/>).</summary>
    internal PendingPermission? Request { get; }

    /// <summary>A plan only to read: opened again from the timeline, with no request behind it.</summary>
    internal bool IsReadOnly => Request == null;

    /// <summary>The plan, in Markdown (the input's <c>plan</c>).</summary>
    internal string Markdown { get; }

    /// <summary>The last part of the input's <c>planFilePath</c>; null when it has none.</summary>
    internal string? FileName { get; }

    /// <summary>
    /// The plan's own title: its first non-empty line when that is a level-one heading ("# Title"), without the #s that close it,
    /// and at most <see cref="MaxTitleLength"/> characters long; else null.
    /// </summary>
    internal string? Heading { get; }

    /// <summary><see cref="Heading"/>, or <see cref="DefaultTitle"/> when the plan has none.</summary>
    internal string Title => Heading ?? DefaultTitle;

    /// <summary>
    /// Null while the request waits, and when it left the queue unanswered (cancelled, or the turn ended); then
    /// <see cref="PlanOutcome.Approved"/> or <see cref="PlanOutcome.Rejected"/>, as the user answered it.
    /// </summary>
    internal PlanOutcome? Outcome
    {
        get => _outcome;
        set => SetField(ref _outcome, value);
    }

    private static string? PlanText(JsonElement input) =>
        input.ValueKind == JsonValueKind.Object
        && input.TryGetProperty("plan", out var plan)
        && plan.ValueKind == JsonValueKind.String
            ? plan.GetString()
            : null;

    private static string? HeadingOf(string markdown)
    {
        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            // the first line with something on it decides: "# " (or "#" and a tab) opens a level-one heading
            if (line.Length < 2 || line[0] != '#' || (line[1] != ' ' && line[1] != '\t')) return null;
            var title = line.Substring(2).TrimEnd().TrimEnd('#').Trim();
            return title.Length == 0 || title.Length > MaxTitleLength ? null : title;
        }
        return null;
    }
}
