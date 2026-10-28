using System;

namespace ClaudeVisualChat;

/// <summary>Where a subagent is.</summary>
public enum AgentStatus
{
    Running,
    Completed,
    Failed,
    Stopped,
}

/// <summary>
/// One subagent of the conversation (see <see cref="ChatConversation.Agents"/>), from its Agent call, the claude's
/// <c>system/task_*</c> lines and the call's result. The conversation fills it; the host only reads it.
/// </summary>
public sealed class AgentInfo : ChatObservableObject
{
    private string? _taskId;
    private string _description = "";
    private string? _agentType;
    private string? _model;
    private bool _isBackground;
    private int _depth = 1;
    private string? _parentId;
    private AgentStatus _status;
    private DateTimeOffset _startedAt;
    private long? _durationMs;
    private long? _tokens;
    private int? _toolUses;
    private string? _activity;
    private string? _result;
    private string? _prompt;
    private bool _isWaitingForPermission;
    private AgentNesting _nested;

    internal AgentInfo(string id)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
    }

    /// <summary>The <c>tool_use_id</c> of the Agent call that started it.</summary>
    public string Id { get; }

    /// <summary>The claude's id for the task (<c>task_id</c>); null until a task line or the notification tells it.</summary>
    public string? TaskId
    {
        get => _taskId;
        internal set => SetField(ref _taskId, value);
    }

    /// <summary>The short description the call gave it.</summary>
    public string Description
    {
        get => _description;
        internal set => SetField(ref _description, value ?? "");
    }

    /// <summary>The kind of agent (<c>general-purpose</c>, <c>Explore</c>…); null when not told.</summary>
    public string? AgentType
    {
        get => _agentType;
        internal set => SetField(ref _agentType, value);
    }

    /// <summary>The model it ran with (the result's <c>resolvedModel</c>); null until the result tells.</summary>
    public string? Model
    {
        get => _model;
        internal set => SetField(ref _model, value);
    }

    /// <summary>True for an agent that runs in the background while the conversation goes on.</summary>
    public bool IsBackground
    {
        get => _isBackground;
        internal set => SetField(ref _isBackground, value);
    }

    /// <summary>1 for an agent the main agent called, 2 for one a subagent called, and so on.</summary>
    public int Depth
    {
        get => _depth;
        internal set => SetField(ref _depth, value);
    }

    /// <summary>
    /// The <see cref="Id"/> of the agent that called this one; null when the main agent did. It may also be null for
    /// a nested agent whose caller's call never reached the conversation (only its task line did): <see cref="Depth"/>
    /// still tells the nesting.
    /// </summary>
    public string? ParentId
    {
        get => _parentId;
        internal set => SetField(ref _parentId, value);
    }

    /// <summary>
    /// <see cref="AgentStatus.Running"/> until its end arrives (its notification or its call's result). An agent a
    /// session file leaves without an end is <see cref="AgentStatus.Stopped"/>, and comes back to running when the
    /// live claude shows it still runs; an ended agent that is resumed runs again too.
    /// </summary>
    public AgentStatus Status
    {
        get => _status;
        internal set => SetField(ref _status, value);
    }

    /// <summary>When the call arrived (its line's time, else the conversation's clock).</summary>
    public DateTimeOffset StartedAt
    {
        get => _startedAt;
        internal set => SetField(ref _startedAt, value);
    }

    /// <summary>How long it has run, as the claude last reported; null until it does.</summary>
    public long? DurationMs
    {
        get => _durationMs;
        internal set => SetField(ref _durationMs, value);
    }

    /// <summary>The tokens it has used, as the claude last reported; null until it does.</summary>
    public long? Tokens
    {
        get => _tokens;
        internal set => SetField(ref _tokens, value);
    }

    /// <summary>The tools it has called, as the claude last reported; null until it does.</summary>
    public int? ToolUses
    {
        get => _toolUses;
        internal set => SetField(ref _toolUses, value);
    }

    /// <summary>What it is doing, from the last progress line ("Reading a.txt"); null before the first.</summary>
    public string? Activity
    {
        get => _activity;
        internal set => SetField(ref _activity, value);
    }

    /// <summary>What it answered, in markdown; null until it finishes.</summary>
    public string? Result
    {
        get => _result;
        internal set => SetField(ref _result, value);
    }

    /// <summary>
    /// What it was asked to do: the <c>prompt</c> of its Agent call, or of its <c>task_started</c> line when the call did not
    /// carry it; null when neither told.
    /// </summary>
    public string? Prompt
    {
        get => _prompt;
        internal set => SetField(ref _prompt, value);
    }

    /// <summary>
    /// True while it runs and one of the conversation's pending requests (<see cref="ChatConversation.PendingPermissions"/>) is
    /// its own: the request's <see cref="PendingPermission.AgentId"/> is its <see cref="TaskId"/>. A request that names no agent
    /// belongs to the agent whose step asked, else to the main agent when its own step asked, else to the agent that started
    /// running last.
    /// </summary>
    public bool IsWaitingForPermission
    {
        get => _isWaitingForPermission;
        internal set => SetField(ref _isWaitingForPermission, value);
    }

    /// <summary>What the agents it started (and the ones those started) are doing, for the line under a background agent's row.</summary>
    internal AgentNesting Nested
    {
        get => _nested;
        set => SetField(ref _nested, value);
    }
}

/// <summary>What the agents an agent started, at any depth, are doing: any of them waits, works, failed or was stopped.</summary>
[Flags]
internal enum AgentNesting
{
    None = 0,
    Waiting = 1,
    Working = 2,
    Failed = 4,
    Stopped = 8,
}
