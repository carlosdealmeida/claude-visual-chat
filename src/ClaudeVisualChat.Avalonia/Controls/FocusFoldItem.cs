using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ClaudeVisualChat.Controls;

/// <summary>What a fold of the Focus view says while one of its steps is under way; the strongest one wins.</summary>
internal enum FocusLive
{
    None,
    /// <summary>A thought arrives (and no tool runs): "Thinking…".</summary>
    Thinking,
    /// <summary>A tool runs: "Running Bash…".</summary>
    Running,
    /// <summary>A question of AskUserQuestion waits: "Waiting for your answer…".</summary>
    Answer,
    /// <summary>A permission request of one of its tools, or of one of its agents, waits: "Waiting for permission…".</summary>
    Permission,
}

/// <summary>
/// A live subagent of a fold, as its row reads: "Look around: Reading a.txt", then "1.2k tokens · 3 tools · 1m 5s", the time from when it
/// started (it runs: the row's clock redraws it). The row of the agents without one of their own adds them up (<see cref="Combined"/>).
/// </summary>
internal sealed class FocusAgentRow : IEquatable<FocusAgentRow>
{
    internal FocusAgentRow(string line, long? tokens, int? tools, IReadOnlyList<DateTimeOffset> starts, bool combined = false)
    {
        Line = line;
        Tokens = tokens;
        Tools = tools;
        Starts = starts;
        Combined = combined;
    }

    /// <summary>"{description}: {what it does, or its last tool}", or the description alone; empty for the combined row.</summary>
    public string Line { get; }

    public long? Tokens { get; }

    public int? Tools { get; }

    /// <summary>When each agent of the row started (one, or the combined ones'); none when not known.</summary>
    public IReadOnlyList<DateTimeOffset> Starts { get; }

    /// <summary>The row of the agents that have no row of their own: their numbers added, "… combined".</summary>
    public bool Combined { get; }

    public bool Equals(FocusAgentRow? other)
    {
        if (other == null || Line != other.Line || Tokens != other.Tokens || Tools != other.Tools || Combined != other.Combined
            || Starts.Count != other.Starts.Count)
            return false;
        for (var i = 0; i < Starts.Count; i++)
        {
            if (Starts[i] != other.Starts[i]) return false;
        }
        return true;
    }

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

    /// <summary>The tool calls among the steps, and those that failed (an error, a refusal, an interruption, or no result once idle).</summary>
    public int ToolCount { get; init; }

    public int FailedCount { get; init; }

    /// <summary>The thoughts among the steps.</summary>
    public int ThinkingCount { get; init; }

    /// <summary>All the steps.</summary>
    public int StepCount { get; init; }

    /// <summary>A thought arriving while the session works.</summary>
    public bool IsThinking { get; init; }

    /// <summary>How long the thoughts whose length is known took, together; null when none is known.</summary>
    public long? ThoughtMs { get; init; }

    /// <summary>What is under way while the session works; nothing while it is idle.</summary>
    public FocusLive Live { get; init; }

    /// <summary>The tool that runs, for "Running {0}…" (the last one in the list, when more do).</summary>
    public string? RunningTool { get; init; }

    /// <summary>An agent of one of its calls still runs (even idle, a background agent): its dot shows it, without a live label.</summary>
    public bool HasProgress { get; init; }

    /// <summary>The subagents at work, as rows: up to 4, or 3 and <see cref="MoreAgentsRow"/>.</summary>
    public IReadOnlyList<FocusAgentRow> Agents { get; init; } = Array.Empty<FocusAgentRow>();

    /// <summary>The subagents at work that have no row of their own ("+{0} more agents"); 0 for none.</summary>
    public int MoreAgents { get; init; }

    /// <summary>Their numbers added, for the line under "+{0} more agents"; null when there are none.</summary>
    public FocusAgentRow? MoreAgentsRow { get; init; }

    /// <summary>Something in it is under way: its dot blinks.</summary>
    public bool IsLive => Live != FocusLive.None || HasProgress;

    /// <summary>A step failed: its dot is red.</summary>
    public bool HasFailed => FailedCount > 0;

    /// <summary>The label is the live one, and the count follows it (a run of thoughts only reads "Thinking…" alone).</summary>
    public bool HasLiveLabel => Live != FocusLive.None && !(Live == FocusLive.Thinking && ToolCount == 0);

    public bool HasAgents => Agents.Count > 0;

    public bool HasMoreAgents => MoreAgents > 0;

    public bool Equals(FocusFoldState? other)
    {
        if (other == null) return false;
        if (ToolCount != other.ToolCount || FailedCount != other.FailedCount || ThinkingCount != other.ThinkingCount
            || StepCount != other.StepCount || IsThinking != other.IsThinking || ThoughtMs != other.ThoughtMs || Live != other.Live
            || RunningTool != other.RunningTool || HasProgress != other.HasProgress || MoreAgents != other.MoreAgents
            || !Equals(MoreAgentsRow, other.MoreAgentsRow) || Agents.Count != other.Agents.Count)
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
/// failed", "Thought for 4s", or while the session works and one is under way "Running Bash…" with the count after it. It opens to the
/// steps themselves (<see cref="Items"/>), and ends then with "Collapse". Its state lives here, as every item's, because the list recycles
/// its rows: <see cref="IsExpanded"/> survives the row going off screen and the steps that come. While a step waits for permission it is
/// open, whatever the user did. Under its head, open or closed, the rows of its subagents at work. <see cref="FocusProjection"/> makes
/// it, fills it and tells it when a step changed (<see cref="Update"/>) or the session went idle or back to work; it reads nothing by
/// itself. It keeps what each step adds to its totals, so a step added or changed costs the same in a run of two steps as in a run of
/// thousands.
/// </summary>
internal sealed class FocusFoldItem : ChatItem
{
    /// <summary>The most agents with a row; with more, one row fewer and a row for the others.</summary>
    internal const int MaxAgentRows = 4;

    /// <summary>What one step adds to the fold's totals, as it was when last read.</summary>
    private struct Part
    {
        public bool Tool;
        public bool Failed;
        public bool Running;
        public FocusLive Waits;
        public bool AtWork;
        public bool Progress;
        public bool Thought;
        public bool Streaming;
        public long? Ms;
        /// <summary>What it adds changes with the session's work: a call without its result, a thought arriving.</summary>
        public bool Busy;
    }

    private readonly FocusProjection _owner;
    private readonly Dictionary<ChatItem, Part> _parts = new Dictionary<ChatItem, Part>();

    /// <summary>The calls of its agents at work, in the order they came to work.</summary>
    private readonly List<ToolCallItem> _atWork = new List<ToolCallItem>();

    private int _tools, _failed, _thoughts, _streaming, _knownMs, _running, _permissions, _answers, _progress, _busyParts;
    private long _ms;
    private ToolCallItem? _runningTool;
    private FocusFoldState _state = FocusFoldState.Empty;
    private bool _isExpanded;
    private bool _forcedOpen;

    internal FocusFoldItem(FocusProjection owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    /// <summary>The steps it stands for, in the order of the list; what the open fold shows.</summary>
    public ObservableCollection<ChatItem> Items { get; } = new ObservableCollection<ChatItem>();

    /// <summary>
    /// Whether it shows its steps: as the user left it, or open while a step waits for permission (closing it then does nothing; once the
    /// request is answered it is as the user left it).
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded || _forcedOpen;
        set
        {
            _isExpanded = value;
            OnPropertyChanged(); // also when it stays open: the toggle that tried to close it shows it open again
        }
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
        var part = PartOf(item);
        Count(item, part, +1);
        if (part.Running) _runningTool = (ToolCallItem)item; // the last one in the list
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
        if (had.Running != now.Running) _runningTool = LastRunning();
        Publish();
    }

    /// <summary>The session went idle, or back to work: the steps whose part depends on it are read again, and only then.</summary>
    internal void OnBusyChanged()
    {
        if (_busyParts > 0) Refresh();
    }

    /// <summary>Reads all its steps again (a request that may be about any of them went, or its steps changed).</summary>
    internal void Refresh()
    {
        _parts.Clear();
        _atWork.Clear();
        _tools = _failed = _thoughts = _streaming = _knownMs = _running = _permissions = _answers = _progress = _busyParts = 0;
        _ms = 0;
        _owner.CountRead(Items.Count);
        foreach (var item in Items) Count(item, PartOf(item), +1);
        _runningTool = LastRunning();
        Publish();
    }

    /// <summary>What <paramref name="item"/> adds now, with the session at work or idle.</summary>
    private Part PartOf(ChatItem item)
    {
        var busy = _owner.IsBusy;
        var part = new Part();
        if (item is ToolCallItem tool)
        {
            var agent = tool.Agent;
            var agentRuns = agent is { Status: AgentStatus.Running };
            var noResult = tool.Status == ToolStatus.Running;
            part.Tool = true;
            part.Busy = noResult || agent is { IsWaitingForPermission: true };
            part.Running = noResult && busy;
            // a call that ended in an error, was refused or interrupted failed; so did one left without a result once the session is idle,
            // unless it is the call of an agent that still runs
            part.Failed = tool.Status == ToolStatus.Failed || tool.Status == ToolStatus.Rejected || (noResult && !busy && !agentRuns);
            part.Progress = agentRuns;
            if (busy)
            {
                if (part.Running) part.Waits = _owner.WaitingFor(tool);
                if (agent is { IsWaitingForPermission: true }) part.Waits = FocusLive.Permission;
            }
            part.AtWork = agent != null ? agentRuns : busy && noResult && FocusProjection.IsAgentTool(tool.Name);
        }
        else if (item is ThinkingItem thought)
        {
            part.Thought = true;
            part.Busy = thought.IsStreaming;
            part.Streaming = thought.IsStreaming && busy;
            part.Ms = thought.DurationMs;
        }
        return part;
    }

    /// <summary>
    /// Adds what <paramref name="item"/> adds to the totals (<paramref name="sign"/> +1), or takes it away (-1). <paramref name="other"/>
    /// is what it added before, or adds after, when it only changed: an agent that stays at work keeps its place among the rows.
    /// </summary>
    private void Count(ChatItem item, Part part, int sign, Part other = default)
    {
        if (sign > 0) _parts[item] = part;
        if (part.Tool) _tools += sign;
        if (part.Failed) _failed += sign;
        if (part.Running) _running += sign;
        if (part.Progress) _progress += sign;
        if (part.Busy) _busyParts += sign;
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
            if (part.Ms is long ms)
            {
                _knownMs += sign;
                _ms += sign * ms;
            }
        }
    }

    /// <summary>The last of its steps that runs, from the end; read only when which steps run changed.</summary>
    private ToolCallItem? LastRunning()
    {
        if (_running == 0) return null;
        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (Items[i] is ToolCallItem tool && _parts.TryGetValue(tool, out var part) && part.Running) return tool;
        }
        return null;
    }

    /// <summary>The totals become the state shown; while a step waits for permission the fold is open.</summary>
    private void Publish()
    {
        var live = _permissions > 0 ? FocusLive.Permission
            : _answers > 0 ? FocusLive.Answer
            : _running > 0 ? FocusLive.Running
            : _streaming > 0 ? FocusLive.Thinking
            : FocusLive.None;
        var forced = live == FocusLive.Permission;
        if (forced != _forcedOpen)
        {
            var was = IsExpanded;
            _forcedOpen = forced;
            if (IsExpanded != was) OnPropertyChanged(nameof(IsExpanded));
        }
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
            ThoughtMs = _knownMs > 0 ? _ms : (long?)null,
            Live = live,
            RunningTool = live == FocusLive.Running || live == FocusLive.Permission || live == FocusLive.Answer ? _runningTool?.Name : null,
            HasProgress = _progress > 0,
            Agents = agents,
            MoreAgents = _atWork.Count - rows,
            MoreAgentsRow = _atWork.Count > rows ? Combined(rows) : null,
        };
    }

    /// <summary>The agents at work from <paramref name="from"/> on, their numbers added.</summary>
    private FocusAgentRow Combined(int from)
    {
        long tokens = 0;
        var tools = 0;
        var starts = new List<DateTimeOffset>();
        for (var i = from; i < _atWork.Count; i++)
        {
            var row = RowOf(_atWork[i]);
            tokens += row.Tokens ?? 0;
            tools += row.Tools ?? 0;
            starts.AddRange(row.Starts);
        }
        return new FocusAgentRow("", tokens > 0 ? tokens : (long?)null, tools, starts, combined: true);
    }

    /// <summary>
    /// The row of a subagent at work: its description, what it does (or the last tool it called), and its numbers. The children are read
    /// from the end for the last tool, and counted only when the agent did not report its tools.
    /// </summary>
    private static FocusAgentRow RowOf(ToolCallItem tool)
    {
        var agent = tool.Agent;
        var description = agent?.Description is { Length: > 0 } told ? told : tool.Summary;
        string? doing = agent?.Activity is { Length: > 0 } activity ? activity : null;
        var children = tool.Children;
        for (var i = children.Count - 1; doing == null && i >= 0; i--)
        {
            if (children[i] is ToolCallItem call) doing = call.Name;
        }
        var tools = agent?.ToolUses;
        if (tools == null)
        {
            var calls = 0;
            foreach (var child in children)
            {
                if (child is ToolCallItem) calls++;
            }
            if (calls > 0) tools = calls;
        }
        var line = string.IsNullOrEmpty(doing) ? description : string.IsNullOrEmpty(description) ? doing! : description + ": " + doing;
        var starts = agent != null && agent.StartedAt != default ? new[] { agent.StartedAt } : Array.Empty<DateTimeOffset>();
        return new FocusAgentRow(line, agent?.Tokens, tools, starts);
    }
}
