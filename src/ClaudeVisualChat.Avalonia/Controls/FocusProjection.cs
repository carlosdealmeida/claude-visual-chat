using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The conversation's list as the Focus view shows it, like the extension's: in each round (a prompt up to its
/// <see cref="TurnFooterItem"/>), every run of steps (tool calls with their results, thoughts) is one <see cref="FocusFoldItem"/>; what a
/// subagent wrote is inside its call, so it goes with it. The prompts, Claude's text and every other line stay as they are, and so do the
/// latest TodoWrite (the plan of the work) and the call of a background agent (its row tells what the agent does), which cut a run in
/// two. The live subagents are rows of their fold; the permission request is the view's, under the list.
/// <para>
/// It follows <see cref="ChatConversation.Items"/> step by step and tells its own changes one row at a time (Add, Remove, Replace): an
/// item that comes at the end costs a constant time, however long the list; any other change reads only the run it touches. It tells
/// Reset only when the conversation's list does, or when <see cref="Start"/> reads it whole again (the view coming back). While stopped
/// (the view out of the window) it holds no handler on the conversation or its items, and keeps the rows it had.
/// </para>
/// </summary>
internal sealed class FocusProjection : IReadOnlyList<object>, IList, INotifyCollectionChanged
{
    private const string TodoWrite = "TodoWrite";

    private readonly ChatConversation _conversation;
    private readonly List<object> _rows = new List<object>();

    /// <summary>Each item of the conversation's list and the row that holds it: itself, or its fold.</summary>
    private readonly Dictionary<ChatItem, object> _holderOf = new Dictionary<ChatItem, object>();

    /// <summary>The agents of the calls followed, and their calls.</summary>
    private readonly Dictionary<AgentInfo, ToolCallItem> _agentCalls = new Dictionary<AgentInfo, ToolCallItem>();

    private readonly Dictionary<ToolCallItem, AgentInfo> _agentOf = new Dictionary<ToolCallItem, AgentInfo>();

    /// <summary>The calls followed by their tool use id, to find the fold a request is about.</summary>
    private readonly Dictionary<string, ToolCallItem> _callsById = new Dictionary<string, ToolCallItem>(StringComparer.Ordinal);

    private ToolCallItem? _latestTodo;
    private bool _started;

    internal FocusProjection(ChatConversation conversation)
    {
        _conversation = conversation ?? throw new ArgumentNullException(nameof(conversation));
    }

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    /// <summary>The conversation it is made over.</summary>
    internal ChatConversation Conversation => _conversation;

    /// <summary>True between <see cref="Start"/> and <see cref="Stop"/>.</summary>
    internal bool IsStarted => _started;

    public int Count => _rows.Count;

    public object this[int index] => _rows[index];

    /// <summary>
    /// Reads the conversation's list whole, tells a Reset and follows it from now on. The folds are made again, closed (what was open
    /// while the view was away is forgotten).
    /// </summary>
    internal void Start()
    {
        if (_started) return;
        _started = true;
        _conversation.Items.CollectionChanged += OnItemsChanged;
        _conversation.PendingPermissions.CollectionChanged += OnPermissionsChanged;
        Rebuild();
    }

    /// <summary>Lets go of the conversation, its items and their agents; the rows stay as they are (nothing is drawn while away).</summary>
    internal void Stop()
    {
        if (!_started) return;
        _started = false;
        _conversation.Items.CollectionChanged -= OnItemsChanged;
        _conversation.PendingPermissions.CollectionChanged -= OnPermissionsChanged;
        UnwatchAll();
    }

    /// <summary>An Agent (or Task) call.</summary>
    internal static bool IsAgentTool(string name) => name == "Agent" || name == "Task";

    // ---- what is folded ----

    /// <summary>A step of a round, which a fold may stand for: a tool call or a thought.</summary>
    private static bool IsStep(ChatItem item) => item is ToolCallItem || item is ThinkingItem;

    /// <summary>A step that stays in sight: the latest TodoWrite, and the call of an agent that runs in the background.</summary>
    private bool StaysShown(ChatItem item) =>
        item is ToolCallItem call && (ReferenceEquals(call, _latestTodo) || IsBackgroundAgent(call));

    private bool IsFolded(ChatItem item) => IsStep(item) && !StaysShown(item);

    /// <summary>An Agent call whose agent runs in the background: as its agent tells, or the call's own flag while it is linked to none.</summary>
    private static bool IsBackgroundAgent(ToolCallItem call)
    {
        if (call.Agent is { } agent) return agent.IsBackground;
        return IsAgentTool(call.Name) && call.Input.ValueKind == JsonValueKind.Object
            && call.Input.TryGetProperty("run_in_background", out var flag) && flag.ValueKind == JsonValueKind.True;
    }

    private static bool IsTodo(ChatItem item) => item is ToolCallItem { Name: TodoWrite };

    // ---- the whole list ----

    private void Rebuild()
    {
        UnwatchAll();
        _rows.Clear();
        _holderOf.Clear();
        var items = _conversation.Items;
        _latestTodo = null;
        for (var i = items.Count - 1; i >= 0 && _latestTodo == null; i--)
        {
            if (IsTodo(items[i])) _latestTodo = (ToolCallItem)items[i];
        }
        FocusFoldItem? open = null; // the fold the next folded step joins
        foreach (var item in items)
        {
            Watch(item);
            if (IsFolded(item))
            {
                if (open == null)
                {
                    open = NewFold();
                    _rows.Add(open);
                }
                open.Items.Add(item);
                _holderOf[item] = open;
            }
            else
            {
                open = null;
                _rows.Add(item);
                _holderOf[item] = item;
            }
        }
        foreach (var row in _rows)
        {
            if (row is FocusFoldItem fold) fold.Refresh();
        }
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    private FocusFoldItem NewFold() => new FocusFoldItem(WaitingFor);

    /// <summary>What a running call of the main agent waits for: a request with its tool use id, or nothing.</summary>
    private FocusLive WaitingFor(ToolCallItem call)
    {
        if (call.ToolUseId.Length == 0) return FocusLive.None;
        foreach (var request in _conversation.PendingPermissions)
        {
            if (request.ToolUseId != call.ToolUseId) continue;
            return request.ToolName == "AskUserQuestion" ? FocusLive.Answer : FocusLive.Permission;
        }
        return FocusLive.None;
    }

    // ---- the conversation's changes ----

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var items = _conversation.Items;
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems != null && e.NewStartingIndex + e.NewItems.Count == items.Count:
                // the usual change: items at the end
                for (var i = 0; i < e.NewItems.Count; i++) Append((ChatItem)e.NewItems[i]!, e.NewStartingIndex + i);
                break;
            case NotifyCollectionChangedAction.Add when e.NewItems != null:
                foreach (ChatItem item in e.NewItems) Watch(item);
                if (ContainsTodo(e.NewItems)) FindLatestTodo();
                Rework(e.NewStartingIndex, e.NewStartingIndex + e.NewItems.Count, null);
                break;
            case NotifyCollectionChangedAction.Remove when e.OldItems != null:
                Forget(e.OldItems);
                if (ContainsTodo(e.OldItems)) FindLatestTodo();
                Rework(e.OldStartingIndex, e.OldStartingIndex, e.OldItems);
                break;
            case NotifyCollectionChangedAction.Replace when e.NewItems != null && e.OldItems != null:
                Forget(e.OldItems);
                foreach (ChatItem item in e.NewItems) Watch(item);
                if (ContainsTodo(e.OldItems) || ContainsTodo(e.NewItems)) FindLatestTodo();
                Rework(e.NewStartingIndex, e.NewStartingIndex + e.NewItems.Count, e.OldItems);
                break;
            case NotifyCollectionChangedAction.Move when e.OldItems != null:
                // the moved items keep their followers; the old place and the new are reworked one after the other
                Rework(e.OldStartingIndex, e.OldStartingIndex, e.OldItems, keep: true);
                Rework(e.NewStartingIndex, e.NewStartingIndex + e.OldItems.Count, null);
                break;
            default:
                Rebuild();
                break;
        }
    }

    private static bool ContainsTodo(IList items)
    {
        foreach (ChatItem item in items)
        {
            if (IsTodo(item)) return true;
        }
        return false;
    }

    /// <summary>
    /// An item at the end of the list: a row at the end of the rows, or a step that joins the fold at the end. A constant time, unless it
    /// is a TodoWrite, which folds the one before it (a run in the middle).
    /// </summary>
    private void Append(ChatItem item, int index)
    {
        Watch(item);
        ToolCallItem? earlierTodo = null;
        if (IsTodo(item))
        {
            earlierTodo = _latestTodo;
            _latestTodo = (ToolCallItem)item;
        }
        if (IsFolded(item))
        {
            var items = _conversation.Items;
            var last = _rows.Count > 0 ? _rows[_rows.Count - 1] as FocusFoldItem : null;
            if (last != null && index > 0 && _holderOf.TryGetValue(items[index - 1], out var before) && ReferenceEquals(before, last))
            {
                _holderOf[item] = last;
                last.Add(item);
            }
            else
            {
                var fold = NewFold();
                fold.Add(item);
                _holderOf[item] = fold;
                InsertRow(_rows.Count, fold);
            }
        }
        else
        {
            _holderOf[item] = item;
            InsertRow(_rows.Count, item);
        }
        if (earlierTodo != null) ReworkAround(earlierTodo);
    }

    /// <summary>Finds the latest TodoWrite again, from the end; the one it was and the one it is now are reworked.</summary>
    private void FindLatestTodo()
    {
        var items = _conversation.Items;
        ToolCallItem? latest = null;
        for (var i = items.Count - 1; i >= 0 && latest == null; i--)
        {
            if (IsTodo(items[i])) latest = (ToolCallItem)items[i];
        }
        var earlier = _latestTodo;
        _latestTodo = latest;
        if (earlier != null && !ReferenceEquals(earlier, latest) && _holderOf.ContainsKey(earlier)) ReworkAround(earlier);
        if (latest != null && !ReferenceEquals(earlier, latest) && _holderOf.ContainsKey(latest)) ReworkAround(latest);
    }

    /// <summary>The run around <paramref name="item"/> is read again: it came to stay in sight, or to fold.</summary>
    private void ReworkAround(ChatItem item)
    {
        var items = _conversation.Items;
        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(items[i], item)) continue;
            Rework(i, i + 1, null);
            return;
        }
    }

    /// <summary>
    /// Reads again the items from <paramref name="start"/> to <paramref name="end"/> of the list (as it is now), with the runs of steps
    /// on both sides of them, and puts their rows in the place of the rows they had: the rows that stay are left, a fold that keeps any
    /// of its steps stays the same fold (open or closed as it was), and the rest is told one row at a time. <paramref name="removed"/>
    /// are items that left the list from there. The time is that of the runs read and of finding their place among the rows.
    /// </summary>
    private void Rework(int start, int end, IList? removed, bool keep = false)
    {
        var items = _conversation.Items;
        var from = start;
        var to = end;
        while (from > 0 && IsStep(items[from - 1])) from--;
        while (to < items.Count && IsStep(items[to])) to++;

        // the rows the items there had lie between the rows of the items around them, which are not steps and hold themselves
        var rowFrom = from > 0 ? RowIndexOf(items[from - 1]) + 1 : 0;
        var rowTo = to < items.Count ? RowIndexOf(items[to]) : _rows.Count;
        if (rowFrom < 0 || rowTo < rowFrom)
        {
            Rebuild(); // the rows lost their place: never expected, but the list is never left wrong
            return;
        }

        // the folds there, by the steps they held, to be used again
        var folds = new Dictionary<ChatItem, FocusFoldItem>();
        for (var r = rowFrom; r < rowTo; r++)
        {
            if (!(_rows[r] is FocusFoldItem fold)) continue;
            foreach (var step in fold.Items) folds[step] = fold;
        }
        if (removed != null)
        {
            foreach (ChatItem item in removed)
            {
                if (keep) continue;
                _holderOf.Remove(item);
            }
        }

        // the rows they have now
        var rows = new List<object>();
        var used = new HashSet<FocusFoldItem>();
        List<ChatItem>? run = null;
        FocusFoldItem? runFold = null;
        void EndRun()
        {
            if (run == null) return;
            var fold = runFold ?? NewFold();
            used.Add(fold);
            fold.SetItems(run);
            foreach (var step in run) _holderOf[step] = fold;
            rows.Add(fold);
            run = null;
            runFold = null;
        }
        for (var i = from; i < to; i++)
        {
            var item = items[i];
            if (IsFolded(item))
            {
                run ??= new List<ChatItem>();
                run.Add(item);
                if (runFold == null && folds.TryGetValue(item, out var had) && !used.Contains(had)) runFold = had;
            }
            else
            {
                EndRun();
                rows.Add(item);
                _holderOf[item] = item;
            }
        }
        EndRun();
        Splice(rowFrom, rowTo, rows);
    }

    /// <summary>
    /// The rows from <paramref name="from"/> to <paramref name="to"/> become <paramref name="rows"/>: the same rows at the start and at
    /// the end are left, the others are replaced one by one, and what is more or less is added or removed.
    /// </summary>
    private void Splice(int from, int to, List<object> rows)
    {
        var oldCount = to - from;
        var head = 0;
        while (head < oldCount && head < rows.Count && ReferenceEquals(_rows[from + head], rows[head])) head++;
        var tail = 0;
        while (tail < oldCount - head && tail < rows.Count - head
               && ReferenceEquals(_rows[to - 1 - tail], rows[rows.Count - 1 - tail])) tail++;
        var oldMiddle = oldCount - head - tail;
        var newMiddle = rows.Count - head - tail;
        var at = from + head;
        var replaced = Math.Min(oldMiddle, newMiddle);
        for (var i = 0; i < replaced; i++) ReplaceRow(at + i, rows[head + i]);
        for (var i = replaced; i < oldMiddle; i++) RemoveRow(at + replaced);
        for (var i = replaced; i < newMiddle; i++) InsertRow(at + i, rows[head + i]);
    }

    /// <summary>Where the row of <paramref name="item"/> (a row of its own) is; looked for from the end, where the changes are.</summary>
    private int RowIndexOf(object item)
    {
        for (var i = _rows.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_rows[i], item)) return i;
        }
        return -1;
    }

    private void InsertRow(int index, object row)
    {
        _rows.Insert(index, row);
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, row, index));
    }

    private void RemoveRow(int index)
    {
        var row = _rows[index];
        _rows.RemoveAt(index);
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, row, index));
    }

    private void ReplaceRow(int index, object row)
    {
        var old = _rows[index];
        _rows[index] = row;
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, row, old, index));
    }

    // ---- the items' changes ----

    private void Watch(ChatItem item)
    {
        if (item is ToolCallItem call)
        {
            call.PropertyChanged += OnCallChanged;
            if (call.ToolUseId.Length > 0) _callsById[call.ToolUseId] = call;
            WatchAgent(call, call.Agent);
        }
        else if (item is ThinkingItem thought)
        {
            thought.PropertyChanged += OnThoughtChanged;
        }
    }

    private void Unwatch(ChatItem item)
    {
        if (item is ToolCallItem call)
        {
            call.PropertyChanged -= OnCallChanged;
            if (call.ToolUseId.Length > 0 && _callsById.TryGetValue(call.ToolUseId, out var known) && ReferenceEquals(known, call))
                _callsById.Remove(call.ToolUseId);
            WatchAgent(call, null);
        }
        else if (item is ThinkingItem thought)
        {
            thought.PropertyChanged -= OnThoughtChanged;
        }
    }

    private void Forget(IList items)
    {
        foreach (ChatItem item in items) Unwatch(item);
    }

    private void UnwatchAll()
    {
        foreach (var item in _holderOf.Keys) Unwatch(item);
        foreach (var agent in _agentCalls.Keys) agent.PropertyChanged -= OnAgentChanged;
        _agentCalls.Clear();
        _agentOf.Clear();
        _callsById.Clear();
    }

    /// <summary>The call's agent is <paramref name="agent"/> now (null for none): the one it had before is let go.</summary>
    private void WatchAgent(ToolCallItem call, AgentInfo? agent)
    {
        if (_agentOf.TryGetValue(call, out var had))
        {
            if (ReferenceEquals(had, agent)) return;
            had.PropertyChanged -= OnAgentChanged;
            _agentCalls.Remove(had);
            _agentOf.Remove(call);
        }
        if (agent == null || _agentCalls.ContainsKey(agent)) return;
        _agentCalls[agent] = call;
        _agentOf[call] = agent;
        agent.PropertyChanged += OnAgentChanged;
    }

    private void OnCallChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!(sender is ToolCallItem call)) return;
        switch (e.PropertyName)
        {
            case nameof(ToolCallItem.Agent):
                WatchAgent(call, call.Agent);
                Reconsider(call);
                break;
            case nameof(ToolCallItem.Input):
                Reconsider(call);
                break;
            case nameof(ToolCallItem.Name):
                FindLatestTodo();
                Reconsider(call);
                break;
            case nameof(ToolCallItem.ToolUseId):
                if (call.ToolUseId.Length > 0) _callsById[call.ToolUseId] = call;
                RefreshFoldOf(call);
                break;
            case nameof(ToolCallItem.Status):
            case nameof(ToolCallItem.Summary):
                RefreshFoldOf(call);
                break;
        }
    }

    private void OnThoughtChanged(object? sender, PropertyChangedEventArgs e)
    {
        // not its text, which comes in pieces: the label reads only whether it arrives and how long it took
        if (sender is ThinkingItem thought
            && (e.PropertyName == nameof(ThinkingItem.IsStreaming) || e.PropertyName == nameof(ThinkingItem.DurationMs)))
            RefreshFoldOf(thought);
    }

    private void OnAgentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!(sender is AgentInfo agent) || !_agentCalls.TryGetValue(agent, out var call)) return;
        if (e.PropertyName == nameof(AgentInfo.IsBackground)) Reconsider(call);
        else RefreshFoldOf(call);
    }

    /// <summary>A request came or went: the fold of the call it is about reads it.</summary>
    private void OnPermissionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var row in _rows)
            {
                if (row is FocusFoldItem fold) fold.Refresh();
            }
            return;
        }
        RefreshFoldsOf(e.NewItems);
        RefreshFoldsOf(e.OldItems);
    }

    private void RefreshFoldsOf(IList? requests)
    {
        if (requests == null) return;
        foreach (PendingPermission request in requests)
        {
            if (request.ToolUseId is { Length: > 0 } id && _callsById.TryGetValue(id, out var call)) RefreshFoldOf(call);
        }
    }

    private void RefreshFoldOf(ChatItem item)
    {
        if (_holderOf.TryGetValue(item, out var holder) && holder is FocusFoldItem fold) fold.Update(item);
    }

    /// <summary>A call may have come to stay in sight, or to fold: its run is read again when it did.</summary>
    private void Reconsider(ToolCallItem call)
    {
        if (!_holderOf.TryGetValue(call, out var holder)) return;
        var folded = holder is FocusFoldItem;
        if (folded == IsFolded(call))
        {
            if (holder is FocusFoldItem fold) fold.Update(call);
            return;
        }
        ReworkAround(call);
    }

    // ---- the lists' members ----

    public IEnumerator<object> GetEnumerator() => _rows.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _rows.GetEnumerator();

    public bool Contains(object? value) => value != null && IndexOf(value) >= 0;

    /// <summary>Looked for from the end: the rows asked about are mostly the last ones, on screen.</summary>
    public int IndexOf(object? value)
    {
        if (value == null) return -1;
        return RowIndexOf(value);
    }

    public void CopyTo(Array array, int index) => ((ICollection)_rows).CopyTo(array, index);

    bool IList.IsFixedSize => false;

    bool IList.IsReadOnly => true;

    bool ICollection.IsSynchronized => false;

    object ICollection.SyncRoot => ((ICollection)_rows).SyncRoot;

    object? IList.this[int index]
    {
        get => _rows[index];
        set => throw ReadOnly();
    }

    int IList.Add(object? value) => throw ReadOnly();

    void IList.Clear() => throw ReadOnly();

    void IList.Insert(int index, object? value) => throw ReadOnly();

    void IList.Remove(object? value) => throw ReadOnly();

    void IList.RemoveAt(int index) => throw ReadOnly();

    private static NotSupportedException ReadOnly() => new NotSupportedException("The Focus view's list follows the conversation's; it is read only.");
}
