using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ClaudeVisualChat.Controls;

/// <summary>What a fold of the Focus view says while one of its steps is under way; the strongest one wins.</summary>
internal enum FocusLive
{
    None,
    /// <summary>A tool runs: "Running Bash…".</summary>
    Running,
    /// <summary>A question of AskUserQuestion waits: "Waiting for your answer…".</summary>
    Answer,
    /// <summary>A permission request of one of its tools, or of one of its agents, waits: "Waiting for permission…".</summary>
    Permission,
}

/// <summary>A live subagent of a fold, as its row reads: "Look around: Reading a.txt", then "1.2k tokens · 3 tools · 1m 5s".</summary>
internal sealed class FocusAgentRow : IEquatable<FocusAgentRow>
{
    internal FocusAgentRow(string line, long? tokens, int? tools, long? durationMs)
    {
        Line = line;
        Tokens = tokens;
        Tools = tools;
        DurationMs = durationMs;
    }

    /// <summary>"{description}: {what it does, or its last tool}", or the description alone.</summary>
    public string Line { get; }

    public long? Tokens { get; }

    public int? Tools { get; }

    public long? DurationMs { get; }

    public bool Equals(FocusAgentRow? other) =>
        other != null && Line == other.Line && Tokens == other.Tokens && Tools == other.Tools && DurationMs == other.DurationMs;

    public override bool Equals(object? obj) => Equals(obj as FocusAgentRow);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Line);
}

/// <summary>
/// What a fold shows, read from its steps at once: replaced whole when any of it changes, so the row's bindings follow one
/// property. Equal states are not replaced.
/// </summary>
internal sealed class FocusFoldState : IEquatable<FocusFoldState>
{
    internal static readonly FocusFoldState Empty = new FocusFoldState();

    /// <summary>The tool calls among the steps, and those that failed.</summary>
    public int ToolCount { get; init; }

    public int FailedCount { get; init; }

    /// <summary>The thoughts among the steps.</summary>
    public int ThinkingCount { get; init; }

    /// <summary>All the steps.</summary>
    public int StepCount { get; init; }

    /// <summary>A thought still arriving.</summary>
    public bool IsThinking { get; init; }

    /// <summary>How long the thoughts took, together; null while any of them does not tell.</summary>
    public long? ThoughtMs { get; init; }

    public FocusLive Live { get; init; }

    /// <summary>The tool that runs, for "Running {0}…" (the one that came to run last, when more do).</summary>
    public string? RunningTool { get; init; }

    /// <summary>The subagents at work, as rows: up to 4, or 3 and <see cref="MoreAgents"/>.</summary>
    public IReadOnlyList<FocusAgentRow> Agents { get; init; } = Array.Empty<FocusAgentRow>();

    /// <summary>The subagents at work that have no row of their own ("+{0} more agents"); 0 for none.</summary>
    public int MoreAgents { get; init; }

    /// <summary>Something in it is under way: a tool, a request or a thought. Its dot blinks.</summary>
    public bool IsLive => Live != FocusLive.None || IsThinking;

    /// <summary>A step failed: its dot is red.</summary>
    public bool HasFailed => FailedCount > 0;

    /// <summary>The label is the live one, and the count follows it.</summary>
    public bool HasLiveLabel => Live != FocusLive.None;

    public bool HasAgents => Agents.Count > 0;

    public bool HasMoreAgents => MoreAgents > 0;

    public bool Equals(FocusFoldState? other)
    {
        if (other == null) return false;
        if (ToolCount != other.ToolCount || FailedCount != other.FailedCount || ThinkingCount != other.ThinkingCount
            || StepCount != other.StepCount || IsThinking != other.IsThinking || ThoughtMs != other.ThoughtMs || Live != other.Live
            || RunningTool != other.RunningTool || MoreAgents != other.MoreAgents || Agents.Count != other.Agents.Count)
            return false;
        for (var i = 0; i < Agents.Count; i++)
        {
            if (!Agents[i].Equals(other.Agents[i])) return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as FocusFoldState);

    public override int GetHashCode() => (ToolCount * 31 + ThinkingCount) * 31 + StepCount;
}

/// <summary>
/// One row of the Focus view in the place of a run of steps of a round (tool calls with their results, thoughts): "3 tool calls · 1
/// failed", "Thought for 4s", "5 hidden steps", or while one is under way "Running Bash…" with the count after it. It opens to the steps
/// themselves (<see cref="Items"/>), and ends then with "Collapse". Its state lives here, as every item's, because the list recycles its
/// rows: <see cref="IsExpanded"/> survives the row going off screen and the steps that come. A step waiting for permission opens it.
/// Under it, while it is closed, the rows of its subagents at work. <see cref="FocusProjection"/> makes it, fills it and tells it
/// when a step changed (<see cref="Update"/>); it reads nothing by itself. It keeps what each step adds to its totals, so a step added
/// or changed costs the same in a run of two steps as in a run of thousands.
/// </summary>
internal sealed class FocusFoldItem : ChatItem
{
    /// <summary>The most agents with a row; with more, one row fewer and "+{0} more agents".</summary>
    internal const int MaxAgentRows = 4;

    /// <summary>What one step adds to the fold's totals, as it was when last read.</summary>
    private struct Part
    {
        public bool Tool;
        public bool Failed;
        public bool Running;
        public FocusLive Waits;
        public bool AtWork;
        public bool Thought;
        public bool Streaming;
        public long? Ms;
    }

    private readonly Func<ToolCallItem, FocusLive> _waiting;
    private readonly Dictionary<ChatItem, Part> _parts = new Dictionary<ChatItem, Part>();

    /// <summary>The calls of its agents at work, in the order they came to work.</summary>
    private readonly List<ToolCallItem> _atWork = new List<ToolCallItem>();

    private int _tools, _failed, _thoughts, _streaming, _unknownMs, _running, _permissions, _answers;
    private long _ms;
    private ToolCallItem? _runningTool;
    private FocusFoldState _state = FocusFoldState.Empty;
    private bool _isExpanded;
    private bool _wasWaiting;

    /// <param name="waiting">What a running tool waits for: a request of its own, or nothing (the projection asks the conversation).</param>
    internal FocusFoldItem(Func<ToolCallItem, FocusLive> waiting)
    {
        _waiting = waiting ?? throw new ArgumentNullException(nameof(waiting));
    }

    /// <summary>The steps it stands for, in the order of the list; what the open fold shows.</summary>
    public ObservableCollection<ChatItem> Items { get; } = new ObservableCollection<ChatItem>();

    /// <summary>Whether it shows its steps. A step that comes to wait for permission opens it; the user closes it again.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    /// <summary>What it shows now.</summary>
    public FocusFoldState State
    {
        get => _state;
        private set
        {
            if (_state.Equals(value)) return;
            _state = value;
            OnPropertyChanged();
        }
    }

    /// <summary>A step at its end (it came at the end of the list).</summary>
    internal void Add(ChatItem item)
    {
        Items.Add(item);
        Count(item, PartOf(item), +1);
        Publish();
    }

    /// <summary>Its steps are these now: the same ones are left as they are, and all are read again.</summary>
    internal void SetItems(IReadOnlyList<ChatItem> items)
    {
        var same = items.Count == Items.Count;
        for (var i = 0; same && i < items.Count; i++) same = ReferenceEquals(items[i], Items[i]);
        if (!same)
        {
            Items.Clear();
            foreach (var item in items) Items.Add(item);
        }
        Refresh();
    }

    /// <summary>One of its steps changed (its state, its agent, a request about it): what it adds is read again.</summary>
    internal void Update(ChatItem item)
    {
        if (!_parts.TryGetValue(item, out var had)) return;
        var now = PartOf(item);
        Count(item, had, -1, now);
        Count(item, now, +1, had);
        if (had.Running && !now.Running && ReferenceEquals(item, _runningTool)) _runningTool = LastRunning();
        Publish();
    }

    /// <summary>Reads all its steps again (a request that may be about any of them went, or its steps changed).</summary>
    internal void Refresh()
    {
        _parts.Clear();
        _atWork.Clear();
        _tools = _failed = _thoughts = _streaming = _unknownMs = _running = _permissions = _answers = 0;
        _ms = 0;
        _runningTool = null;
        foreach (var item in Items) Count(item, PartOf(item), +1);
        Publish();
    }

    /// <summary>What <paramref name="item"/> adds now.</summary>
    private Part PartOf(ChatItem item)
    {
        var part = new Part();
        if (item is ToolCallItem tool)
        {
            part.Tool = true;
            part.Failed = tool.Status == ToolStatus.Failed;
            part.Running = tool.Status == ToolStatus.Running;
            if (part.Running) part.Waits = _waiting(tool);
            if (tool.Agent is { IsWaitingForPermission: true }) part.Waits = FocusLive.Permission;
            part.AtWork = IsAtWork(tool);
        }
        else if (item is ThinkingItem thought)
        {
            part.Thought = true;
            part.Streaming = thought.IsStreaming;
            part.Ms = thought.DurationMs;
        }
        return part;
    }

    /// <summary>
    /// Adds what <paramref name="item"/> adds to the totals (<paramref name="sign"/> +1), or takes it away (-1). <paramref name="other"/>
    /// is what it added before, or adds after, when it only changed: an agent that stays at work keeps its place among the rows, and a
    /// tool that stays running does not take the name from the one named.
    /// </summary>
    private void Count(ChatItem item, Part part, int sign, Part other = default)
    {
        if (sign > 0) _parts[item] = part;
        if (part.Tool) _tools += sign;
        if (part.Failed) _failed += sign;
        if (part.Running)
        {
            _running += sign;
            if (sign > 0 && !other.Running) _runningTool = (ToolCallItem)item; // the one that came to run last is named
        }
        if (part.Waits == FocusLive.Permission) _permissions += sign;
        else if (part.Waits == FocusLive.Answer) _answers += sign;
        if (part.AtWork && !other.AtWork)
        {
            if (sign > 0) _atWork.Add((ToolCallItem)item);
            else _atWork.Remove((ToolCallItem)item);
        }
        if (part.Thought)
        {
            _thoughts += sign;
            if (part.Streaming) _streaming += sign;
            if (part.Ms is long ms) _ms += sign * ms;
            else _unknownMs += sign;
        }
    }

    /// <summary>The last of its steps that runs; only read when the one named stops while others run.</summary>
    private ToolCallItem? LastRunning()
    {
        if (_running == 0) return null;
        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (Items[i] is ToolCallItem { Status: ToolStatus.Running } tool) return tool;
        }
        return null;
    }

    /// <summary>The totals become the state shown; a step that came to wait for permission opens the fold.</summary>
    private void Publish()
    {
        var live = _permissions > 0 ? FocusLive.Permission : _answers > 0 ? FocusLive.Answer : _running > 0 ? FocusLive.Running : FocusLive.None;
        var waiting = live == FocusLive.Permission;
        if (waiting && !_wasWaiting) IsExpanded = true; // the request is for one of its steps: it opens by itself
        _wasWaiting = waiting;
        var rows = _atWork.Count > MaxAgentRows ? MaxAgentRows - 1 : _atWork.Count;
        var agents = rows == 0 ? Array.Empty<FocusAgentRow>() : new FocusAgentRow[rows];
        for (var i = 0; i < rows; i++) agents[i] = RowOf(_atWork[i]);
        State = new FocusFoldState
        {
            ToolCount = _tools,
            FailedCount = _failed,
            ThinkingCount = _thoughts,
            StepCount = Items.Count,
            IsThinking = _streaming > 0,
            ThoughtMs = _thoughts > 0 && _unknownMs == 0 && _streaming == 0 ? _ms : (long?)null,
            Live = live,
            RunningTool = live == FocusLive.None ? null : _runningTool?.Name,
            Agents = agents,
            MoreAgents = _atWork.Count - rows,
        };
    }

    /// <summary>An Agent (or Task) call whose agent works: by its agent's state, or by the call's own while it is linked to none.</summary>
    private static bool IsAtWork(ToolCallItem tool)
    {
        if (tool.Agent is { } agent) return agent.Status == AgentStatus.Running;
        return FocusProjection.IsAgentTool(tool.Name) && tool.Status == ToolStatus.Running;
    }

    /// <summary>The row of a subagent at work: its description, what it does (or the last tool it called), and its numbers.</summary>
    private static FocusAgentRow RowOf(ToolCallItem tool)
    {
        var agent = tool.Agent;
        var description = agent?.Description is { Length: > 0 } told ? told : tool.Summary;
        ToolCallItem? last = null;
        var calls = 0;
        foreach (var child in tool.Children)
        {
            if (child is not ToolCallItem call) continue;
            last = call;
            calls++;
        }
        var doing = agent?.Activity is { Length: > 0 } activity ? activity : last?.Name;
        var line = string.IsNullOrEmpty(doing) ? description : string.IsNullOrEmpty(description) ? doing! : description + ": " + doing;
        return new FocusAgentRow(line, agent?.Tokens, agent?.ToolUses ?? (calls > 0 ? calls : (int?)null), agent?.DurationMs);
    }
}
