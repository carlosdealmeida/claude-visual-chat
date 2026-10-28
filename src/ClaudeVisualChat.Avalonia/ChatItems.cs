using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace ClaudeVisualChat;

/// <summary>One entry of the conversation list.</summary>
public abstract class ChatItem : ChatObservableObject
{
}

/// <summary>A message the user sent.</summary>
public sealed class UserMessageItem : ChatItem
{
    private DateTimeOffset? _timestamp;
    private bool _isTextExpanded;

    public UserMessageItem(string text, IReadOnlyList<ChatImage>? images = null)
        : this(text, images, null)
    {
    }

    public UserMessageItem(string text, IReadOnlyList<ChatImage>? images, IReadOnlyList<ChatFile>? files)
    {
        Text = text ?? "";
        Images = images ?? Array.Empty<ChatImage>();
        Files = files ?? Array.Empty<ChatFile>();
    }

    /// <summary>What the user wrote, without the lines that name the attached files.</summary>
    public string Text { get; }

    /// <summary>The images sent with the message; empty when there are none.</summary>
    public IReadOnlyList<ChatImage> Images { get; }

    /// <summary>
    /// The files sent with the message; empty when there are none. A message read back from the claude or from a
    /// session file has the files its marker lines name, with no size.
    /// </summary>
    public IReadOnlyList<ChatFile> Files { get; }

    /// <summary>When it was sent; null when unknown. The view shows it above the message.</summary>
    public DateTimeOffset? Timestamp
    {
        get => _timestamp;
        set => SetField(ref _timestamp, value);
    }

    /// <summary>
    /// Whether the text shows whole instead of folded at 60 px behind "Show more". Only a text taller than that folds; "Show more"
    /// and "Show less" turn it on and off. It lives here because the list recycles its rows.
    /// </summary>
    public bool IsTextExpanded
    {
        get => _isTextExpanded;
        set => SetField(ref _isTextExpanded, value);
    }

    /// <summary>
    /// The line under the message while it is held in <see cref="ChatConversation.QueuedMessages"/> (it ran at once, while Claude
    /// worked); a <see cref="MetaLineItem"/> under it once it goes into the list. Null for none. The conversation changes it by
    /// putting the message back in its place in the queue, which is what the view follows.
    /// </summary>
    internal string? QueuedNotice { get; set; }
}

/// <summary>Text from Claude; grows while <see cref="IsStreaming"/>.</summary>
public sealed class AssistantTextItem : ChatItem
{
    private string _text = "";
    private bool _isStreaming;
    private DateTimeOffset? _timestamp;
    private bool _isBookmarked;
    private string? _uuid;
    private bool? _rating;

    public string Text
    {
        get => _text;
        set => SetField(ref _text, value ?? "");
    }

    /// <summary>
    /// When Claude wrote it, from its assistant line; null while it is still arriving or when the line has none.
    /// </summary>
    public DateTimeOffset? Timestamp
    {
        get => _timestamp;
        set => SetField(ref _timestamp, value);
    }

    public bool IsStreaming
    {
        get => _isStreaming;
        set => SetField(ref _isStreaming, value);
    }

    /// <summary>
    /// Whether the user bookmarked it, with the bookmark button under the turn it ends (the view marks the last text of a turn). It is
    /// kept here, in memory, because the list recycles its rows.
    /// </summary>
    public bool IsBookmarked
    {
        get => _isBookmarked;
        set => SetField(ref _isBookmarked, value);
    }

    /// <summary>
    /// The <c>uuid</c> of the claude's line that brought it (what a rating names it by); null for an item built by hand, or while it
    /// is still arriving.
    /// </summary>
    public string? Uuid
    {
        get => _uuid;
        internal set => SetField(ref _uuid, value);
    }

    /// <summary>The user's rating of the turn this text ends: true for good, false for bad, null for none. Kept for the row that shows it.</summary>
    internal bool? Rating
    {
        get => _rating;
        set => SetField(ref _rating, value);
    }
}

/// <summary>Claude's thinking; collapsed unless <see cref="IsExpanded"/>.</summary>
public sealed class ThinkingItem : ChatItem
{
    private string _text = "";
    private bool _isStreaming;
    private bool _isExpanded;
    private long? _durationMs;
    private long? _estimatedTokens;
    private readonly TokenCountUp _countUp = new TokenCountUp();

    public string Text
    {
        get => _text;
        set => SetField(ref _text, value ?? "");
    }

    public bool IsStreaming
    {
        get => _isStreaming;
        set => SetField(ref _isStreaming, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    /// <summary>
    /// How long the thought took, once it is whole: from the session file, or from the conversation's clock between
    /// the start and the end of the thought as it arrived. Null while it arrives, or when nothing tells it.
    /// </summary>
    public long? DurationMs
    {
        get => _durationMs;
        set => SetField(ref _durationMs, value);
    }

    /// <summary>
    /// The tokens the claude has used thinking so far, while the thought arrives (<c>system/thinking_tokens</c>, or the sum of its
    /// deltas); null when nothing tells, and again once the turn ends. Only the main agent's thoughts have it. The line counts up to
    /// it over a few seconds (<see cref="DisplayedTokens"/>), so what is shown is not this value at once.
    /// </summary>
    public long? EstimatedTokens
    {
        get => _estimatedTokens;
        internal set
        {
            if (_estimatedTokens == value) return;
            _countUp.Set(value, TokenCountUp.NowMs()); // before the event: the line that follows it reads the count
            SetField(ref _estimatedTokens, value);
        }
    }

    /// <summary>The count the line shows at <paramref name="nowMs"/> (the clock of <see cref="TokenCountUp.NowMs"/>): the estimate on its way up.</summary>
    internal long DisplayedTokens(long nowMs) => _countUp.Display(nowMs);

    /// <summary>True while the count shown is still on its way to <see cref="EstimatedTokens"/>.</summary>
    internal bool IsCountingUp(long nowMs) => _countUp.IsMoving(nowMs);
}

/// <summary>State of a tool call.</summary>
public enum ToolStatus
{
    Running,
    Succeeded,
    Failed,
    Rejected,
}

/// <summary>A tool call, its result and, for a subagent, the items it produced (<see cref="Children"/>).</summary>
public sealed class ToolCallItem : ChatItem
{
    private string _toolUseId = "";
    private string _name = "";
    private string _summary = "";
    private JsonElement _input;
    private ToolStatus _status;
    private string? _resultText;
    private string? _fullResultText;
    private string? _rejectionReason;
    private IReadOnlyList<ChatImage>? _resultImages;
    private int _hiddenResultLines;
    private IReadOnlyList<DiffLine>? _diff;
    private IReadOnlyList<TodoEntry>? _todos;
    private bool _isExpanded;
    private bool _isOutputExpanded;
    private bool _isInputExpanded;
    private AgentInfo? _agent;

    public string ToolUseId
    {
        get => _toolUseId;
        set => SetField(ref _toolUseId, value ?? "");
    }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value ?? "");
    }

    /// <summary>One line about the call (a path, a command…); empty until the full input arrives.</summary>
    public string Summary
    {
        get => _summary;
        set => SetField(ref _summary, value ?? "");
    }

    /// <summary>The tool input; <c>{}</c> until the full input arrives.</summary>
    public JsonElement Input
    {
        get => _input;
        set => SetField(ref _input, value);
    }

    public ToolStatus Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    /// <summary>The first <see cref="ToolPresentation.OutputLines"/> lines of the result.</summary>
    public string? ResultText
    {
        get => _resultText;
        set => SetField(ref _resultText, value);
    }

    /// <summary>
    /// The whole result, up to <see cref="ToolPresentation.MaxKeptResultChars"/> characters; for Bash, PowerShell and
    /// TaskOutput without its ANSI escape sequences. Null until the result arrives.
    /// </summary>
    public string? FullResultText
    {
        get => _fullResultText;
        internal set => SetField(ref _fullResultText, value);
    }

    /// <summary>The images the result carried (a screenshot of Claude in Chrome); null when it came with none or has not arrived.</summary>
    public IReadOnlyList<ChatImage>? ResultImages
    {
        get => _resultImages;
        internal set => SetField(ref _resultImages, value);
    }

    /// <summary>
    /// What the user typed when they refused the call: the result's text when the claude marked it as refused by the user
    /// and the text is not its standard sentence. Null for a refusal without a reason, and for a call that was not refused.
    /// </summary>
    public string? RejectionReason
    {
        get => _rejectionReason;
        internal set => SetField(ref _rejectionReason, value);
    }

    /// <summary>Result lines left out of <see cref="ResultText"/>.</summary>
    public int HiddenResultLines
    {
        get => _hiddenResultLines;
        set => SetField(ref _hiddenResultLines, value);
    }

    public IReadOnlyList<DiffLine>? Diff
    {
        get => _diff;
        set => SetField(ref _diff, value);
    }

    public IReadOnlyList<TodoEntry>? Todos
    {
        get => _todos;
        set => SetField(ref _todos, value);
    }

    /// <summary>Items of the subagent this call started.</summary>
    public ObservableCollection<ChatItem> Children { get; } = new ObservableCollection<ChatItem>();

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    /// <summary>
    /// Whether the output (OUT) shows whole instead of cut at 60 px. A click on a long output turns it on and another turns it off.
    /// It lives here because the list recycles its rows.
    /// </summary>
    public bool IsOutputExpanded
    {
        get => _isOutputExpanded;
        set => SetField(ref _isOutputExpanded, value);
    }

    /// <summary>The same for what went in: the command (IN) or the file written.</summary>
    public bool IsInputExpanded
    {
        get => _isInputExpanded;
        set => SetField(ref _isInputExpanded, value);
    }

    /// <summary>
    /// For an Agent (or Task) call, the agent it started, which the conversation links when it knows it: its row reads the prompt
    /// and, for a background agent, its state there. Null for any other call, and for an item built by hand.
    /// </summary>
    internal AgentInfo? Agent
    {
        get => _agent;
        set => SetField(ref _agent, value);
    }
}

/// <summary>Duration, tokens and cost of a finished turn.</summary>
public sealed class TurnFooterItem : ChatItem
{
    public TurnFooterItem(long? durationMs, long? inputTokens, long? outputTokens, double? costUsd, bool isError)
    {
        DurationMs = durationMs;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        CostUsd = costUsd;
        IsError = isError;
    }

    public long? DurationMs { get; }

    public long? InputTokens { get; }

    public long? OutputTokens { get; }

    public double? CostUsd { get; }

    public bool IsError { get; }
}

/// <summary>
/// The token count of a thought on its way up, like the extension's: the first value takes 1,200 ms to reach; each next one takes the
/// time since the one before, but no less than 300 ms and no more than 3,000 ms; a value below the count shown is shown at once. The
/// state is the item's, so a row of the list that is reused for another item shows that item's count.
/// </summary>
internal sealed class TokenCountUp
{
    /// <summary>The time the first value takes to reach.</summary>
    internal const long FirstMs = 1200;

    internal const long MinMs = 300;

    internal const long MaxMs = 3000;

    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

    private bool _hasValue;
    private long _from;
    private long _to;
    private long _startMs;
    private long _durationMs;
    private long _lastMs;

    /// <summary>Test seam: the milliseconds <see cref="NowMs"/> gives, in place of the real clock.</summary>
    internal static Func<long>? ClockOverride { get; set; }

    /// <summary>Milliseconds on a clock that only goes forward.</summary>
    internal static long NowMs() => ClockOverride?.Invoke() ?? Clock.ElapsedMilliseconds;

    /// <summary>A new target at <paramref name="nowMs"/>; null forgets the count.</summary>
    internal void Set(long? value, long nowMs)
    {
        if (value == null)
        {
            _hasValue = false;
            _from = _to = 0;
            return;
        }
        var shown = Display(nowMs);
        if (!_hasValue)
        {
            _from = 0;
            _durationMs = FirstMs;
        }
        else if (value.Value < shown)
        {
            _from = value.Value; // a lower value jumps straight to it
            _durationMs = 0;
        }
        else
        {
            _from = shown;
            _durationMs = Math.Max(MinMs, Math.Min(MaxMs, nowMs - _lastMs));
        }
        _hasValue = true;
        _to = value.Value;
        _startMs = nowMs;
        _lastMs = nowMs;
    }

    /// <summary>The count shown at <paramref name="nowMs"/>.</summary>
    internal long Display(long nowMs)
    {
        if (!_hasValue) return 0;
        if (_durationMs <= 0 || nowMs >= _startMs + _durationMs) return _to;
        if (nowMs <= _startMs) return _from;
        return _from + (long)((_to - _from) * ((nowMs - _startMs) / (double)_durationMs));
    }

    internal bool IsMoving(long nowMs) => _hasValue && _durationMs > 0 && nowMs < _startMs + _durationMs;
}

/// <summary>What a <see cref="NoticeItem"/> tells.</summary>
public enum NoticeKind
{
    /// <summary>The user stopped the claude.</summary>
    Interrupted,
    Compacted,
    ApiRetry,
    TurnFailed,
    /// <summary>The user stopped the claude while it used a tool.</summary>
    ToolInterrupted,
}

/// <summary>A one-line notice; the view picks the text for <see cref="Kind"/>.</summary>
public sealed class NoticeItem : ChatItem
{
    public NoticeItem(NoticeKind kind)
    {
        Kind = kind;
    }

    public NoticeKind Kind { get; }
}

/// <summary>
/// The card that marks a compaction of the conversation: "Compacted chat · manual · 36k tokens freed", closed until it is opened to
/// the summary the claude wrote. It replaces the notice (<see cref="NoticeKind.Compacted"/>) that the boundary and the summary used to be.
/// </summary>
public sealed class CompactionItem : ChatItem
{
    private string? _summary;
    private bool _isExpanded;

    /// <summary>What started it: <c>manual</c> (the user ran <c>/compact</c>) or <c>auto</c> (the context was full); empty when the line does not tell.</summary>
    public string Trigger { get; init; } = "";

    /// <summary>The tokens the conversation held before; null when not told.</summary>
    public long? TokensBefore { get; init; }

    /// <summary>The summary the claude wrote of the conversation, in Markdown; null until it arrives, or when it never does.</summary>
    public string? Summary
    {
        get => _summary;
        set => SetField(ref _summary, value);
    }

    /// <summary>Whether the card is open to the summary.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }
}

/// <summary>What a slash command the claude ran locally printed (<c>/compact</c>, <c>/context</c>…).</summary>
public sealed class CommandOutputItem : ChatItem
{
    public CommandOutputItem(string text)
    {
        Text = text ?? "";
    }

    public string Text { get; }
}

/// <summary>
/// A line the conversation says to the user about a command or an event of its own (what <c>/copy</c> did): italic, in the
/// secondary color, selectable. It has no part in the timeline.
/// </summary>
public sealed class MetaLineItem : ChatItem
{
    public MetaLineItem(string text)
    {
        Text = text ?? "";
    }

    public string Text { get; }
}
