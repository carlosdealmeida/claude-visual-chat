using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.Json;
using ClaudeVisualChat.Protocol;

namespace ClaudeVisualChat;

/// <summary>
/// The conversation as an observable list of items. Feed it the claude's output with <see cref="AppendLine"/> (or a
/// session file with <see cref="LoadTranscript"/>); what the user does comes back as events for the host to send to
/// the claude (see <see cref="StdinMessages"/>). Not thread-safe: call everything on the UI thread.
/// </summary>
public sealed class ChatConversation : ChatObservableObject
{
    private const string InterruptedPrefix = "[Request interrupted";
    private const string TaskNotificationPrefix = "<task-notification>";
    private const string RejectedResultPrefix = "User rejected";
    private const string RejectedTextPrefix = "The user doesn't want to proceed";

    private readonly Dictionary<string, ToolCallItem> _tools = new Dictionary<string, ToolCallItem>();
    private readonly HashSet<ToolCallItem> _nestedTools = new HashSet<ToolCallItem>();
    private readonly List<ToolCallItem> _turnTools = new List<ToolCallItem>();
    private readonly List<LiveBlock> _live = new List<LiveBlock>();
    /// <summary>Sent messages shown at once, oldest first, whose repetition by the claude has not arrived.</summary>
    private readonly List<MessageSubmittedEventArgs> _echoes = new List<MessageSubmittedEventArgs>();
    /// <summary>Messages sent since the conversation was idle, while the claude has not answered: they alone keep it busy.</summary>
    private readonly List<MessageSubmittedEventArgs> _unanswered = new List<MessageSubmittedEventArgs>();
    private StreamJsonParser _parser = new StreamJsonParser();
    private PendingPermission? _currentPermission;
    private bool _isBusy;
    private bool _isStreaming;
    private string? _sessionId;
    private string? _model;
    private string? _requestedModel;
    private string? _workingDirectory;
    private string _permissionMode = "default";
    private bool _interruptedThisTurn;
    private Func<DateTimeOffset> _clock = () => DateTimeOffset.Now;

    public ChatConversation()
    {
        PendingPermissions.CollectionChanged += OnPendingPermissionsChanged;
    }

    public event EventHandler<MessageSubmittedEventArgs>? MessageSubmitted;

    /// <summary>Raised by <see cref="UndoSubmit"/>: the reply box takes the text back when it is empty.</summary>
    public event EventHandler<MessageSubmittedEventArgs>? SubmitUndone;

    public event EventHandler<PermissionAnsweredEventArgs>? PermissionAnswered;

    /// <summary>
    /// Raised when the user asks for another permission mode (<see cref="SetPermissionMode"/>), before the claude
    /// confirms it: the host sends <c>set_permission_mode</c>, and <see cref="PermissionMode"/> changes only when the
    /// claude's answer arrives.
    /// </summary>
    public event EventHandler<PermissionModeRequestedEventArgs>? PermissionModeRequested;

    /// <summary>
    /// Raised when the user asks for another model (<see cref="SetModel"/>), before the claude confirms it: the host
    /// sends <c>set_model</c>; the model waits in <see cref="RequestedModel"/>, and <see cref="Model"/> changes only
    /// when the claude reports it.
    /// </summary>
    public event EventHandler<ModelRequestedEventArgs>? ModelRequested;

    public event EventHandler? StopRequested;

    public ObservableCollection<ChatItem> Items { get; } = new ObservableCollection<ChatItem>();

    /// <summary>Permission requests in arrival order; the view shows <see cref="CurrentPermission"/>.</summary>
    public ObservableCollection<PendingPermission> PendingPermissions { get; } =
        new ObservableCollection<PendingPermission>();

    public PendingPermission? CurrentPermission => _currentPermission;

    /// <summary>True from a submitted message or Claude's first block until the turn ends.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    /// <summary>True while a text or a thought is arriving in pieces (a tool call arriving does not count).</summary>
    public bool IsStreaming
    {
        get => _isStreaming;
        private set => SetField(ref _isStreaming, value);
    }

    public string? SessionId
    {
        get => _sessionId;
        set => SetField(ref _sessionId, value);
    }

    /// <summary>The model the claude reports; <see cref="SetModel"/> only asks for a change.</summary>
    public string? Model
    {
        get => _model;
        private set => SetField(ref _model, value);
    }

    /// <summary>
    /// The model asked for with <see cref="SetModel"/> and not yet confirmed; null when nothing waits. The claude
    /// reports the model in use at the start of every turn (<c>system/init</c>), which clears it.
    /// </summary>
    public string? RequestedModel
    {
        get => _requestedModel;
        private set => SetField(ref _requestedModel, value);
    }

    /// <summary>Paths under this folder show relative in tool summaries. Set it before loading a session file.</summary>
    public string? WorkingDirectory
    {
        get => _workingDirectory;
        set => SetField(ref _workingDirectory, value);
    }

    /// <summary>The mode the claude reports; <see cref="SetPermissionMode"/> only asks for a change.</summary>
    public string PermissionMode
    {
        get => _permissionMode;
        private set => SetField(ref _permissionMode, value);
    }

    /// <summary>
    /// When true (the default), <see cref="Submit"/> adds the message to <see cref="Items"/> at once, since the
    /// claude does not repeat it in its output. Set it to false only with a stream that repeats the user's messages
    /// (for example <c>--replay-user-messages</c>); otherwise sent messages never show. When true and the stream does
    /// repeat them, each repetition matching the oldest sent message not yet repeated is dropped, so nothing shows twice.
    /// </summary>
    public bool EchoSubmittedMessages { get; set; } = true;

    /// <summary>The time a message gets when <see cref="Submit"/> adds it. The local clock by default.</summary>
    public Func<DateTimeOffset> Clock
    {
        get => _clock;
        set => _clock = value ?? throw new ArgumentNullException(nameof(value));
    }

    // ---- input ----

    /// <summary>Applies one line of <c>claude -p --output-format stream-json</c>; odd lines are ignored.</summary>
    public void AppendLine(string line)
    {
        foreach (var e in _parser.Parse(line))
            Apply(e);
    }

    public void Apply(ClaudeEvent e)
    {
        if (e == null) throw new ArgumentNullException(nameof(e));
        switch (e)
        {
            case SessionStarted started:
                if (started.SessionId != null) SessionId = started.SessionId;
                if (started.Model != null) Model = started.Model;
                RequestedModel = null; // the turn runs with the model it reports, whatever was asked
                if (started.WorkingDirectory != null) WorkingDirectory = started.WorkingDirectory;
                if (started.PermissionMode != null) PermissionMode = started.PermissionMode;
                break;
            case StatusChanged status:
                if (status.PermissionMode != null) PermissionMode = status.PermissionMode;
                break;
            case AssistantBlock block:
                OnAssistantBlock(block);
                break;
            case BlockStarted started:
                OnBlockStarted(started);
                break;
            case BlockDelta delta:
                OnBlockDelta(delta);
                break;
            case BlockStopped stopped:
                OnBlockStopped(stopped);
                break;
            case UserBlock user:
                OnUserBlock(user);
                break;
            case PermissionRequested request:
                OnPermissionRequested(request);
                break;
            case PermissionCancelled cancelled:
                RemovePermission(cancelled.RequestId);
                break;
            case ControlAcknowledged ack:
                OnControlAcknowledged(ack);
                break;
            case TurnCompleted completed:
                OnTurnCompleted(completed);
                break;
            case SystemNotice notice:
                OnSystemNotice(notice);
                break;
        }
        UpdateStreaming();
    }

    /// <summary>A text or a thought is still arriving: one of the streamed blocks has not ended.</summary>
    private void UpdateStreaming()
    {
        var streaming = false;
        foreach (var live in _live)
        {
            if (live.Item is AssistantTextItem { IsStreaming: true } || live.Item is ThinkingItem { IsStreaming: true })
            {
                streaming = true;
                break;
            }
        }
        IsStreaming = streaming;
    }

    /// <summary>Replaces the items with a Claude Code session file (<c>.jsonl</c>), line by line.</summary>
    public void LoadTranscript(IEnumerable<string> lines)
    {
        if (lines == null) throw new ArgumentNullException(nameof(lines));
        Clear();
        foreach (var line in lines)
        {
            foreach (var e in TranscriptParser.Parse(line))
                Apply(e);
        }
        EndStreaming();
        // the history holds no turn ends: what it marked belongs to past turns, not to the next live one
        _turnTools.Clear();
        _interruptedThisTurn = false;
        IsBusy = false;
        UpdateStreaming();
    }

    /// <summary>Empties the items and the pending requests. Session details stay.</summary>
    public void Clear()
    {
        Items.Clear();
        if (PendingPermissions.Count > 0) PendingPermissions.Clear();
        _tools.Clear();
        _nestedTools.Clear();
        _turnTools.Clear();
        _live.Clear();
        _parser = new StreamJsonParser();
        _echoes.Clear();
        _unanswered.Clear();
        _interruptedThisTurn = false;
        IsBusy = false;
        UpdateStreaming();
    }

    // ---- user actions ----

    /// <summary>Sends a message; blank text is ignored. Works while busy: the claude queues it.</summary>
    public bool Submit(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var submitted = new MessageSubmittedEventArgs(text) { Owner = this };
        if (EchoSubmittedMessages)
        {
            submitted.Item = new UserMessageItem(text) { Timestamp = _clock() };
            Items.Add(submitted.Item);
            _echoes.Add(submitted);
        }
        if (!IsBusy || _unanswered.Count > 0) _unanswered.Add(submitted);
        IsBusy = true;
        MessageSubmitted?.Invoke(this, submitted);
        return true;
    }

    /// <summary>
    /// Takes back a message the host could not deliver (no connection, refused): it leaves <see cref="Items"/>, the
    /// conversation stops waiting for its repetition, and <see cref="IsBusy"/> goes back to false when only the
    /// messages sent while idle kept it busy and the claude has not answered since. Raises <see cref="SubmitUndone"/>
    /// with the text, even after <see cref="Clear"/> or <see cref="LoadTranscript"/>. Works inside
    /// <see cref="MessageSubmitted"/>. False when the message was already taken back or is not this conversation's.
    /// </summary>
    public bool UndoSubmit(MessageSubmittedEventArgs submitted)
    {
        if (submitted == null) throw new ArgumentNullException(nameof(submitted));
        if (!ReferenceEquals(submitted.Owner, this) || submitted.Undone) return false;
        submitted.Undone = true;
        if (submitted.Item != null) Items.Remove(submitted.Item);
        _echoes.Remove(submitted);
        if (_unanswered.Remove(submitted) && _unanswered.Count == 0) IsBusy = false;
        SubmitUndone?.Invoke(this, submitted);
        return true;
    }

    /// <summary>
    /// Takes the request out of <see cref="PendingPermissions"/> and raises <see cref="PermissionAnswered"/>.
    /// A request that already left the queue (cancelled, answered elsewhere, its tool already has a result, turn
    /// over) raises no <see cref="PermissionAnswered"/>: the claude is no longer waiting for it, and a late answer
    /// would only reach its stdin as noise.
    /// </summary>
    public void AnswerPermission(PendingPermission request, PermissionDecision decision, string? message = null)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        var queued = RemovePermission(request.RequestId);
        if (queued == null) return;
        PermissionAnswered?.Invoke(this, new PermissionAnsweredEventArgs(queued, decision, message));
    }

    /// <summary>Drops a request without answering it (for one answered somewhere else).</summary>
    public void DismissPermission(string requestId) => RemovePermission(requestId);

    /// <summary>
    /// Asks for a mode: raises <see cref="PermissionModeRequested"/>; <see cref="PermissionMode"/> changes when the
    /// claude confirms.
    /// </summary>
    public void SetPermissionMode(string mode)
    {
        if (mode == null) throw new ArgumentNullException(nameof(mode));
        PermissionModeRequested?.Invoke(this, new PermissionModeRequestedEventArgs(mode));
    }

    /// <summary>
    /// Asks for a model: raises <see cref="ModelRequested"/>; the model waits in <see cref="RequestedModel"/>, and
    /// <see cref="Model"/> changes when the claude reports it.
    /// </summary>
    public void SetModel(string model)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));
        RequestedModel = model;
        ModelRequested?.Invoke(this, new ModelRequestedEventArgs(model));
    }

    /// <summary>Asks the claude to stop; the turn ends when its result arrives.</summary>
    public void Stop() => StopRequested?.Invoke(this, EventArgs.Empty);

    // ---- assistant blocks ----

    /// <summary>Claude is answering: the turn keeps the conversation busy, whatever happens to the messages sent.</summary>
    private void ClaudeIsWorking()
    {
        _unanswered.Clear();
        IsBusy = true;
    }

    private void OnAssistantBlock(AssistantBlock e)
    {
        ClaudeIsWorking();
        var live = FindLive(e.MessageId, e.ParentToolUseId, e.Block);
        switch (e.Block)
        {
            case TextContentBlock text:
                if (live?.Item is AssistantTextItem streamedText)
                {
                    streamedText.Text = text.Text;
                    streamedText.IsStreaming = false;
                    streamedText.Timestamp = e.Timestamp;
                    Complete(live);
                }
                else
                {
                    Place(new AssistantTextItem { Text = text.Text, Timestamp = e.Timestamp }, e.ParentToolUseId);
                }
                break;
            case ThinkingBlock thinking:
                if (live?.Item is ThinkingItem streamedThinking)
                {
                    streamedThinking.Text = thinking.Text;
                    streamedThinking.IsStreaming = false;
                    streamedThinking.DurationMs ??= e.ThinkingDurationMs ?? live.Elapsed(_clock());
                    Complete(live);
                }
                else
                {
                    Place(new ThinkingItem { Text = thinking.Text, DurationMs = e.ThinkingDurationMs }, e.ParentToolUseId);
                }
                break;
            case ToolUseBlock tool:
                if (live?.Item is ToolCallItem streamedTool)
                {
                    if (streamedTool.ToolUseId.Length == 0 && tool.Id.Length > 0)
                    {
                        // the block started without an id: take it now so results and subagent items find the call
                        streamedTool.ToolUseId = tool.Id;
                        Register(streamedTool);
                    }
                    Fill(streamedTool, tool);
                    Complete(live);
                }
                else
                {
                    var item = new ToolCallItem { ToolUseId = tool.Id, Name = tool.Name, Status = ToolStatus.Running };
                    Fill(item, tool);
                    AddTool(item, e.ParentToolUseId);
                }
                break;
        }
    }

    private void Fill(ToolCallItem item, ToolUseBlock tool)
    {
        item.Name = tool.Name;
        item.Input = tool.Input;
        item.Summary = ToolPresentation.Summary(tool.Name, tool.Input, WorkingDirectory);
        item.Diff = ToolPresentation.Diff(tool.Name, tool.Input);
        item.Todos = ToolPresentation.Todos(tool.Name, tool.Input);
    }

    private void OnBlockStarted(BlockStarted e)
    {
        ClaudeIsWorking();
        ChatItem item;
        switch (e.Block)
        {
            case TextContentBlock text:
                item = new AssistantTextItem { Text = text.Text, IsStreaming = true };
                break;
            case ThinkingBlock thinking:
                item = new ThinkingItem { Text = thinking.Text, IsStreaming = true };
                break;
            case ToolUseBlock tool:
                var call = new ToolCallItem
                {
                    ToolUseId = tool.Id, Name = tool.Name, Input = tool.Input, Status = ToolStatus.Running,
                };
                _live.Add(new LiveBlock(e.MessageId, e.ParentToolUseId, e.Index, call));
                AddTool(call, e.ParentToolUseId);
                return;
            default:
                return;
        }
        _live.Add(new LiveBlock(e.MessageId, e.ParentToolUseId, e.Index, item) { StartedAt = _clock() });
        Place(item, e.ParentToolUseId);
    }

    private void OnBlockDelta(BlockDelta e)
    {
        var live = FindStreamed(e.MessageId, e.Index);
        if (live == null || live.Completed) return;
        if (e.Kind == DeltaKind.Text && live.Item is AssistantTextItem text)
            text.Text += e.Text;
        else if (e.Kind == DeltaKind.Thinking && live.Item is ThinkingItem thinking)
            thinking.Text += e.Text;
        // input_json deltas are not shown: the assistant line brings the whole input
    }

    private void OnBlockStopped(BlockStopped e)
    {
        var live = FindStreamed(e.MessageId, e.Index);
        if (live == null) return;
        if (live.Item is ThinkingItem { IsStreaming: true } thought) thought.DurationMs ??= live.Elapsed(_clock());
        StopStreaming(live.Item);
        live.Stopped = true;
        if (live.Completed) _live.Remove(live);
    }

    /// <summary>
    /// The first streamed block of that message, of the same kind, not yet completed. A block that came without a
    /// message id (its <c>message_start</c> arrived before the host started listening) belongs to any message of the
    /// same agent.
    /// </summary>
    private LiveBlock? FindLive(string messageId, string? parentToolUseId, ContentBlock block)
    {
        foreach (var live in _live)
        {
            if (live.Completed) continue;
            if (live.MessageId.Length == 0 ? live.ParentToolUseId != parentToolUseId : live.MessageId != messageId) continue;
            switch (block)
            {
                case TextContentBlock _ when live.Item is AssistantTextItem:
                case ThinkingBlock _ when live.Item is ThinkingItem:
                    return live;
                case ToolUseBlock tool when live.Item is ToolCallItem call
                                            && (call.ToolUseId == tool.Id || call.ToolUseId.Length == 0 || tool.Id.Length == 0):
                    return live;
            }
        }
        return null;
    }

    private LiveBlock? FindStreamed(string messageId, int index)
    {
        for (var i = _live.Count - 1; i >= 0; i--)
        {
            var live = _live[i];
            if (live.Index == index && live.MessageId == messageId) return live;
        }
        return null;
    }

    private void Complete(LiveBlock live)
    {
        live.Completed = true;
        if (live.Stopped) _live.Remove(live);
    }

    private void EndStreaming()
    {
        foreach (var live in _live)
            StopStreaming(live.Item);
        _live.Clear();
    }

    private static void StopStreaming(ChatItem item)
    {
        if (item is AssistantTextItem text) text.IsStreaming = false;
        else if (item is ThinkingItem thinking) thinking.IsStreaming = false;
    }

    // ---- user blocks ----

    private void OnUserBlock(UserBlock e)
    {
        if (e.Block is ToolResultBlock result)
            OnToolResult(e, result);
        else if (e.Block is TextContentBlock text)
            OnUserText(e, text.Text);
    }

    private void OnToolResult(UserBlock e, ToolResultBlock result)
    {
        RemovePermissionsFor(result.ToolUseId);
        if (!_tools.TryGetValue(result.ToolUseId, out var tool)) return;
        var cut = ToolPresentation.Truncate(result.Text, ToolPresentation.OutputLines);
        tool.ResultText = cut.Text;
        tool.HiddenResultLines = cut.HiddenLines;
        tool.Status = ResultStatus(e, result);
    }

    private static ToolStatus ResultStatus(UserBlock e, ToolResultBlock result)
    {
        if (e.ToolUseResult is { ValueKind: JsonValueKind.String } structured
            && (structured.GetString() ?? "").StartsWith(RejectedResultPrefix, StringComparison.Ordinal))
            return ToolStatus.Rejected;
        if (result.Text.StartsWith(RejectedTextPrefix, StringComparison.Ordinal))
            return ToolStatus.Rejected;
        // A deny with a message comes back as an error ("Error: <message>"); only the meta tells it never ran.
        if (result.NonExecutionKind == "permission-rule" || result.NonExecutionKind == "user-rejected")
            return ToolStatus.Rejected;
        return result.IsError ? ToolStatus.Failed : ToolStatus.Succeeded;
    }

    private void OnUserText(UserBlock e, string text)
    {
        if (text.StartsWith(InterruptedPrefix, StringComparison.Ordinal))
        {
            _interruptedThisTurn = true;
            AddNotice(NoticeKind.Interrupted, e.ParentToolUseId);
            return;
        }
        // a task notification is the claude telling itself that a background subagent finished
        if (e.IsMeta || text.StartsWith(TaskNotificationPrefix, StringComparison.Ordinal)) return;
        if (e.IsCompactSummary)
        {
            AddNotice(NoticeKind.Compacted, e.ParentToolUseId);
            return;
        }
        if (e.ParentToolUseId == null && _echoes.Count > 0 && _echoes[0].Text == text)
        {
            _echoes.RemoveAt(0); // the claude repeated what Submit already shows
            return;
        }
        Place(new UserMessageItem(text) { Timestamp = e.Timestamp }, e.ParentToolUseId);
    }

    // ---- permissions ----

    private void OnPermissionRequested(PermissionRequested e)
    {
        foreach (var queued in PendingPermissions)
        {
            if (queued.RequestId == e.RequestId) return;
        }
        PendingPermissions.Add(new PendingPermission(
            e.RequestId, e.ToolName, e.DisplayName ?? e.ToolName, e.Description, e.ToolUseId,
            ToolPresentation.Summary(e.ToolName, e.Input, WorkingDirectory), e.Input, e.Suggestions));
    }

    private PendingPermission? RemovePermission(string? requestId)
    {
        for (var i = 0; i < PendingPermissions.Count; i++)
        {
            var queued = PendingPermissions[i];
            if (queued.RequestId != requestId) continue;
            PendingPermissions.RemoveAt(i);
            return queued;
        }
        return null;
    }

    private void RemovePermissionsFor(string toolUseId)
    {
        for (var i = PendingPermissions.Count - 1; i >= 0; i--)
        {
            if (PendingPermissions[i].ToolUseId == toolUseId) PendingPermissions.RemoveAt(i);
        }
    }

    private void OnPendingPermissionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var first = PendingPermissions.Count > 0 ? PendingPermissions[0] : null;
        SetField(ref _currentPermission, first, nameof(CurrentPermission));
    }

    // ---- control, turn end and notices ----

    private void OnControlAcknowledged(ControlAcknowledged e)
    {
        if (e.IsError || e.Response is not { ValueKind: JsonValueKind.Object } response) return;
        if (response.TryGetProperty("mode", out var mode) && mode.ValueKind == JsonValueKind.String)
            PermissionMode = mode.GetString() ?? PermissionMode;
    }

    private void OnTurnCompleted(TurnCompleted e)
    {
        RemoveTurnPermissions();
        EndStreaming();
        if (e.IsError || _interruptedThisTurn) RejectToolsLeftRunning();
        if (e.IsError && !_interruptedThisTurn) Items.Add(new NoticeItem(NoticeKind.TurnFailed));
        Items.Add(new TurnFooterItem(e.DurationMs, e.InputTokens, e.OutputTokens, e.CostUsd, e.IsError));
        _turnTools.Clear();
        _interruptedThisTurn = false;
        _unanswered.Clear();
        IsBusy = false;
    }

    /// <summary>
    /// The turn is over, so the claude no longer waits for its requests. A subagent's requests stay: a subagent
    /// in the background may ask after the result, and they leave by cancel, by the tool result or by dismissal.
    /// </summary>
    private void RemoveTurnPermissions()
    {
        for (var i = PendingPermissions.Count - 1; i >= 0; i--)
        {
            var id = PendingPermissions[i].ToolUseId;
            if (id != null && _tools.TryGetValue(id, out var tool) && _nestedTools.Contains(tool)) continue;
            PendingPermissions.RemoveAt(i);
        }
    }

    /// <summary>
    /// After an interrupted or failed turn, its tool calls still running without a result never ran (the user
    /// stopped while the input was arriving, say). A subagent's work is left alone: its own items, and the call
    /// that started it while any of its items still runs. Those change only with their results.
    /// </summary>
    private void RejectToolsLeftRunning()
    {
        foreach (var tool in _turnTools)
        {
            if (tool.Status == ToolStatus.Running && !_nestedTools.Contains(tool) && !HasRunningChild(tool))
                tool.Status = ToolStatus.Rejected;
        }
    }

    private static bool HasRunningChild(ToolCallItem tool)
    {
        foreach (var child in tool.Children)
        {
            if (child is ToolCallItem call && (call.Status == ToolStatus.Running || HasRunningChild(call)))
                return true;
        }
        return false;
    }

    private void OnSystemNotice(SystemNotice e)
    {
        if (e.Subtype == "compact_boundary") AddNotice(NoticeKind.Compacted, null);
        else if (e.Subtype == "api_retry") AddNotice(NoticeKind.ApiRetry, null);
    }

    /// <summary>Adds a notice unless the same notice is already the last item (one compaction is marked twice).</summary>
    private void AddNotice(NoticeKind kind, string? parentToolUseId)
    {
        var target = Container(parentToolUseId);
        if (target.Count > 0 && target[target.Count - 1] is NoticeItem last && last.Kind == kind) return;
        target.Add(new NoticeItem(kind));
    }

    // ---- placement ----

    private void Register(ToolCallItem item)
    {
        if (item.ToolUseId.Length > 0) _tools[item.ToolUseId] = item;
    }

    /// <summary>Registers a new tool call of this turn and places it; remembers when it belongs to a subagent.</summary>
    private void AddTool(ToolCallItem item, string? parentToolUseId)
    {
        Register(item);
        _turnTools.Add(item);
        var container = Container(parentToolUseId);
        if (!ReferenceEquals(container, Items)) _nestedTools.Add(item);
        container.Add(item);
    }

    private void Place(ChatItem item, string? parentToolUseId) => Container(parentToolUseId).Add(item);

    /// <summary>The subagent's tool call when it is known, else the top-level list.</summary>
    private ObservableCollection<ChatItem> Container(string? parentToolUseId) =>
        parentToolUseId != null && _tools.TryGetValue(parentToolUseId, out var parent) ? parent.Children : Items;

    private sealed class LiveBlock
    {
        public LiveBlock(string messageId, string? parentToolUseId, int index, ChatItem item)
        {
            MessageId = messageId;
            ParentToolUseId = parentToolUseId;
            Index = index;
            Item = item;
        }

        /// <summary>Empty when the block started before its <c>message_start</c> was seen.</summary>
        public string MessageId { get; }

        public string? ParentToolUseId { get; }

        public int Index { get; }

        public ChatItem Item { get; }

        /// <summary>The assistant line for this block arrived.</summary>
        public bool Completed { get; set; }

        /// <summary>The <c>content_block_stop</c> for this block arrived.</summary>
        public bool Stopped { get; set; }

        /// <summary>When the block started to arrive, by the conversation's clock.</summary>
        public DateTimeOffset StartedAt { get; set; }

        public long Elapsed(DateTimeOffset now) => Math.Max(0, (long)Math.Round((now - StartedAt).TotalMilliseconds));
    }
}
