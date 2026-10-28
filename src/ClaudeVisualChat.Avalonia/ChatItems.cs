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
}

/// <summary>Text from Claude; grows while <see cref="IsStreaming"/>.</summary>
public sealed class AssistantTextItem : ChatItem
{
    private string _text = "";
    private bool _isStreaming;
    private DateTimeOffset? _timestamp;

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
}

/// <summary>Claude's thinking; collapsed unless <see cref="IsExpanded"/>.</summary>
public sealed class ThinkingItem : ChatItem
{
    private string _text = "";
    private bool _isStreaming;
    private bool _isExpanded;
    private long? _durationMs;

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
    private int _hiddenResultLines;
    private IReadOnlyList<DiffLine>? _diff;
    private IReadOnlyList<TodoEntry>? _todos;
    private bool _isExpanded;
    private bool _isOutputExpanded;
    private bool _isInputExpanded;

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

/// <summary>What a slash command the claude ran locally printed (<c>/compact</c>, <c>/context</c>…).</summary>
public sealed class CommandOutputItem : ChatItem
{
    public CommandOutputItem(string text)
    {
        Text = text ?? "";
    }

    public string Text { get; }
}
