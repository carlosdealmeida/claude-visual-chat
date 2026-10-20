using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ClaudeVisualChat.Sample;

/// <summary>
/// Plays a recorded <c>claude -p</c> session (one stream-json line each) into a conversation, as if the claude were
/// writing it now. At a line that queues a permission request it stops until that request leaves
/// <see cref="ChatConversation.PendingPermissions"/>, however it leaves: answered in the view, dismissed, or taken out
/// by the conversation itself. Call it on the UI thread, like everything in <see cref="ChatConversation"/>.
/// </summary>
public sealed class FixturePlayer
{
    private readonly IReadOnlyList<string> _lines;
    private readonly ChatConversation _conversation;

    public FixturePlayer(IReadOnlyList<string> lines, ChatConversation conversation)
    {
        _lines = lines ?? throw new ArgumentNullException(nameof(lines));
        _conversation = conversation ?? throw new ArgumentNullException(nameof(conversation));
    }

    /// <summary>
    /// The messages the user sent in the recording. The claude does not repeat them in its output, so the player
    /// submits one before each <c>system/init</c> (the claude reports it at the start of every turn) while any is left.
    /// </summary>
    public IReadOnlyList<string> Prompts { get; set; } = Array.Empty<string>();

    /// <summary>How many lines have been played.</summary>
    public int Position { get; private set; }

    /// <summary>
    /// The time of the recording where the player is: the last <c>timestamp</c> of the lines played, or the
    /// recording's first one before any. Null before playing, or when no line has a time. Give it to
    /// <see cref="ChatConversation.Clock"/> and the times and thought durations are those of the recording.
    /// </summary>
    public DateTimeOffset? RecordingTime { get; private set; }

    /// <summary>
    /// Plays every line, from the first. After a <c>stream_event</c> line (a piece of text arriving) it waits
    /// <paramref name="perStreamEvent"/>, after any other line <paramref name="perOtherLine"/>; zero does not wait.
    /// Cancelling stops it where it is.
    /// </summary>
    public async Task PlayAsync(TimeSpan perStreamEvent, TimeSpan perOtherLine, CancellationToken ct)
    {
        var prompts = new Queue<string>(Prompts);
        Position = 0;
        RecordingTime = null;
        foreach (var line in _lines)
        {
            RecordingTime = Read(line).Time;
            if (RecordingTime != null) break;
        }

        foreach (var line in _lines)
        {
            ct.ThrowIfCancellationRequested();
            var (type, subtype, time) = Read(line);
            if (time != null) RecordingTime = time; // before the line: a block it ends ends at its time
            if (type == "system" && subtype == "init" && prompts.Count > 0) _conversation.Submit(prompts.Dequeue());

            var queued = new List<PendingPermission>(_conversation.PendingPermissions);
            _conversation.AppendLine(line);
            Position++;
            foreach (var request in new List<PendingPermission>(_conversation.PendingPermissions))
            {
                if (!queued.Contains(request)) await UntilGoneAsync(request, ct);
            }

            var pause = type == "stream_event" ? perStreamEvent : perOtherLine;
            if (pause > TimeSpan.Zero) await Task.Delay(pause, ct);
        }
    }

    /// <summary>
    /// Waits for the request to leave the queue. The player goes on in a later dispatcher job, never inside the
    /// queue's change event (where adding the next request would throw).
    /// </summary>
    private async Task UntilGoneAsync(PendingPermission request, CancellationToken ct)
    {
        var queue = _conversation.PendingPermissions;
        if (!queue.Contains(request)) return;
        var gone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (!queue.Contains(request)) gone.TrySetResult(true);
        }

        queue.CollectionChanged += OnChanged;
        try
        {
            using (ct.Register(() => gone.TrySetCanceled(ct)))
            {
                await gone.Task;
            }
        }
        finally
        {
            queue.CollectionChanged -= OnChanged;
        }
    }

    /// <summary>The line's <c>type</c>, <c>subtype</c> and <c>timestamp</c>; nulls for what it does not have.</summary>
    private static (string? Type, string? Subtype, DateTimeOffset? Time) Read(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, null, null);
            DateTimeOffset? time = null;
            if (root.TryGetProperty("timestamp", out var stamp) && stamp.ValueKind == JsonValueKind.String
                && stamp.TryGetDateTimeOffset(out var parsed))
                time = parsed;
            return (Text(root, "type"), Text(root, "subtype"), time);
        }
        catch (JsonException)
        {
            return (null, null, null);
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
