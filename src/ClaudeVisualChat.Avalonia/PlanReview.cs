using System;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;

namespace ClaudeVisualChat;

/// <summary>
/// A comment of the user on a passage of a plan: the passage (<paramref name="Quote"/>, trimmed), the nearest heading above it
/// (<paramref name="Heading"/>, if any) and what the user wrote about it (<paramref name="Text"/>).
/// </summary>
internal sealed record PlanComment(string Quote, string? Heading, string Text);

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

    /// <summary>What a refusal tells the claude when nothing is written (the extension's sentence); the feedback's first line then.</summary>
    internal const string StayInPlanMode = "User chose to stay in plan mode and continue planning";

    /// <summary>The line over the comments in the feedback.</summary>
    internal const string CommentsHeader = "Comments on the plan:";

    /// <summary>The most a refusal carries (the host's contract), the feedback with its comments too.</summary>
    internal const int MaxFeedbackLength = 4000;

    private const string Ellipsis = "…";

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

    /// <summary>
    /// The user's comments on passages of the plan, in the order they were added; empty for a plan only to read
    /// (<see cref="IsReadOnly"/>). The plan card lists them, and its feedback carries them (<see cref="Feedback"/>).
    /// </summary>
    internal ObservableCollection<PlanComment> Comments { get; } = new ObservableCollection<PlanComment>();

    /// <summary>
    /// Adds a comment on <paramref name="quote"/> (the passage, trimmed), under <paramref name="heading"/> (the nearest heading
    /// above it, if any). A comment with no text, and any comment on a plan only to read, is ignored.
    /// </summary>
    internal void AddComment(string quote, string? heading, string text)
    {
        if (IsReadOnly || string.IsNullOrWhiteSpace(text)) return;
        Comments.Add(new PlanComment((quote ?? "").Trim(), heading, text.Trim()));
    }

    /// <summary>Takes <paramref name="comment"/> away: this very one, not another with the same words.</summary>
    internal void RemoveComment(PlanComment comment)
    {
        for (var i = 0; i < Comments.Count; i++)
        {
            if (!ReferenceEquals(Comments[i], comment)) continue;
            Comments.RemoveAt(i);
            return;
        }
    }

    /// <summary>
    /// The refusal that sends the comments: the typed text (or <see cref="StayInPlanMode"/> when nothing is typed), a blank line,
    /// "Comments on the plan:" and a line per comment, <c>[Re: "passage"] comment</c>. Never longer than <paramref name="maxLength"/>
    /// (the host carries 4,000 characters of a refusal at most): the passages are shortened first, all to the same length and each
    /// with "…"; then, if that is not enough, the comments; then the typed text; and, in the last resort, the message itself. No cut
    /// parts a surrogate pair. (The extension sends each passage whole.)
    /// </summary>
    internal string Feedback(string? typed, int maxLength = MaxFeedbackLength)
    {
        var text = string.IsNullOrWhiteSpace(typed) ? StayInPlanMode : typed!.Trim();
        if (Comments.Count == 0) return Shorten(text, maxLength);
        var longestQuote = 1;
        var longestComment = 1;
        foreach (var comment in Comments)
        {
            longestQuote = Math.Max(longestQuote, comment.Quote.Length);
            longestComment = Math.Max(longestComment, comment.Text.Length);
        }

        var whole = Compose(text, int.MaxValue, int.MaxValue, int.MaxValue);
        if (whole.Length <= maxLength) return whole;
        if (Compose(text, 1, int.MaxValue, int.MaxValue).Length <= maxLength)
            return Compose(text, Longest(longestQuote, cap => Compose(text, cap, int.MaxValue, int.MaxValue).Length <= maxLength), int.MaxValue, int.MaxValue);
        if (Compose(text, 1, 1, int.MaxValue).Length <= maxLength)
            return Compose(text, 1, Longest(longestComment, cap => Compose(text, 1, cap, int.MaxValue).Length <= maxLength), int.MaxValue);
        if (Compose(text, 1, 1, 1).Length <= maxLength)
            return Compose(text, 1, 1, Longest(text.Length, cap => Compose(text, 1, 1, cap).Length <= maxLength));
        return Shorten(Compose(text, 1, 1, 1), maxLength);
    }

    /// <summary>The longest cap from 1 to <paramref name="most"/> with which the message fits (it fits with 1).</summary>
    private static int Longest(int most, Func<int, bool> fits)
    {
        int low = 1, high = most;
        while (low < high)
        {
            var middle = low + (high - low + 1) / 2;
            if (fits(middle)) low = middle;
            else high = middle - 1;
        }
        return low;
    }

    private string Compose(string text, int quoteCap, int commentCap, int textCap)
    {
        var message = new StringBuilder(Shorten(text, textCap)).Append("\n\n").Append(CommentsHeader);
        foreach (var comment in Comments)
            message.Append("\n[Re: \"").Append(Shorten(comment.Quote, quoteCap)).Append("\"] ").Append(Shorten(comment.Text, commentCap));
        return message.ToString();
    }

    /// <summary>
    /// <paramref name="text"/> when it has at most <paramref name="cap"/> characters; else its start and "…", <paramref name="cap"/>
    /// characters in all, never ending on half of a surrogate pair.
    /// </summary>
    internal static string Shorten(string text, int cap)
    {
        if (text.Length <= cap) return text;
        if (cap <= 1) return Ellipsis;
        var take = cap - 1;
        if (char.IsHighSurrogate(text[take - 1])) take--;
        return text.Substring(0, take) + Ellipsis;
    }

    /// <summary>
    /// <paramref name="text"/> when it has at most <paramref name="length"/> characters; else its first <paramref name="length"/>
    /// (one less rather than half of a surrogate pair) and "…": how the comment box and the card show a passage.
    /// </summary>
    internal static string Clip(string text, int length)
    {
        if (text.Length <= length) return text;
        var take = length;
        if (take > 0 && char.IsHighSurrogate(text[take - 1])) take--;
        return text.Substring(0, take) + Ellipsis;
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
