using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using ClaudeVisualChat.Imaging;
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
    private const string InterruptedText = "[Request interrupted by user]";
    private const string ToolInterruptedText = "[Request interrupted by user for tool use]";
    private const string TaskNotificationPrefix = TaskNotificationText.Prefix;
    private const string AgentTaskType = "local_agent";
    private const string RejectedResultPrefix = "User rejected";
    private const string RejectedTextPrefix = "The user doesn't want to proceed";
    private const string RejectionReasonLead = "The user provided the following reason for the rejection: ";
    private const string DefaultModelValue = "default";
    private const string ExitPlanModeTool = "ExitPlanMode";
    private const string AutoMode = "auto";
    private const string AcceptEditsMode = "acceptEdits";
    private static readonly string[] LocalCommandNames = { "context", "usage", "remote-control", "rc", "chrome" };
    private static readonly IReadOnlyList<string> LocalCommandList = new System.Collections.ObjectModel.ReadOnlyCollection<string>(LocalCommandNames);

    private readonly Dictionary<string, ToolCallItem> _tools = new Dictionary<string, ToolCallItem>();
    private readonly HashSet<ToolCallItem> _nestedTools = new HashSet<ToolCallItem>();
    private readonly List<ToolCallItem> _turnTools = new List<ToolCallItem>();
    private readonly List<LiveBlock> _live = new List<LiveBlock>();
    /// <summary>Sent messages shown at once, oldest first, whose repetition by the claude has not arrived.</summary>
    private readonly List<MessageSubmittedEventArgs> _echoes = new List<MessageSubmittedEventArgs>();
    /// <summary>Messages sent since the conversation was idle, while the claude has not answered: they alone keep it busy.</summary>
    private readonly List<MessageSubmittedEventArgs> _unanswered = new List<MessageSubmittedEventArgs>();
    /// <summary>Agents a session file left without an end: a task line from the live claude brings them back to running.</summary>
    private readonly HashSet<AgentInfo> _agentsStoppedByHistory = new HashSet<AgentInfo>();
    /// <summary>An agent was added or its state changed since the agents were last looked at (see <see cref="Apply"/>).</summary>
    private bool _agentsChanged;
    /// <summary>How many times the agents' states were worked out; a test's way to see that a piece of text does not.</summary>
    internal int AgentStateUpdates { get; private set; }
    private readonly Dictionary<PendingPermission, AgentInfo> _requestAgents = new Dictionary<PendingPermission, AgentInfo>(); // queued requests without agent_id, and their agent
    private StreamJsonParser _parser = new StreamJsonParser();
    private int _runningAgents;
    private PendingPermission? _currentPermission;
    private PlanReview? _currentPlan;
    /// <summary>A plan was approved with the auto mode, and the claude has neither applied nor refused it yet (see <see cref="ApprovePlan"/>).</summary>
    private bool _autoModeAsked;
    private bool _isBusy;
    private bool _isStreaming;
    private string? _sessionId;
    private string? _model;
    private string? _requestedModel;
    private string? _effort;
    private string? _requestedEffort;
    private string? _workingDirectory;
    private IReadOnlyList<ChatCommand> _commands = Array.Empty<ChatCommand>();
    private IReadOnlyList<string> _terminalCommands = Array.Empty<string>();
    private IReadOnlyList<ChatModel>? _availableModels;
    private ChatPanel _shownPanel;
    private ContextUsage? _contextUsage;
    private UsageLimits? _usageLimits;
    private string _permissionMode = "default";
    private bool _interruptedThisTurn;
    private Func<DateTimeOffset> _clock = () => DateTimeOffset.Now;
    private DateTimeOffset? _cacheAnchor;
    private DateTimeOffset? _promptCacheExpiresAt;
    private TimeSpan? _promptCacheTtl;
    private bool _isPromptCacheCompacted;
    private ChatBrowserStatus _browserStatus;
    private string? _browserError;
    private ChatRemoteControlStatus _remoteControlStatus;
    private string? _remoteControlUrl;
    private string? _remoteControlError;

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

    /// <summary>
    /// Raised when the user asks for another effort level (<see cref="SetEffort"/>), before the claude confirms it:
    /// the host sends <c>apply_flag_settings</c>; the level waits in <see cref="RequestedEffort"/>, and
    /// <see cref="Effort"/> changes only when the claude reports it.
    /// </summary>
    public event EventHandler<EffortRequestedEventArgs>? EffortRequested;

    /// <summary>
    /// Raised for a local command (<see cref="LocalCommands"/>), by <see cref="InvokeCommand"/> and so by
    /// <see cref="Submit"/>: the host acts on it (the Remote Control, the browser); nothing goes to the claude.
    /// </summary>
    public event EventHandler<CommandInvokedEventArgs>? CommandInvoked;

    public event EventHandler? StopRequested;

    /// <summary>Raised by <see cref="DisconnectBrowser"/> while the browser is not <see cref="ChatBrowserStatus.Off"/>: the host disconnects it and sets <see cref="BrowserStatus"/>.</summary>
    public event EventHandler? BrowserDisconnectRequested;

    /// <summary>Raised by <see cref="OpenRemoteControl"/> when there is a link: the host opens the URL; the component never does.</summary>
    public event EventHandler<RemoteControlLinkRequestedEventArgs>? RemoteControlLinkRequested;

    /// <summary>Images waiting to go with the next message; <see cref="Submit"/> takes them all.</summary>
    public ObservableCollection<ChatImage> Attachments { get; } = new ObservableCollection<ChatImage>();

    /// <summary>The most images one message may carry.</summary>
    public int MaxImages { get; set; } = 10;

    /// <summary>The most bytes all the images of one message may add up to (about 5 MB once in base64).</summary>
    public int ImageBudgetBytes { get; set; } = 3_750_000;

    /// <summary>The longest side, in pixels, an attached image keeps.</summary>
    public int MaxImageSide { get; set; } = 1568;

    /// <summary>
    /// PDF, text and CSV files waiting to go with the next message (<see cref="AddFileAsync"/>, <see cref="AddFile"/>);
    /// <see cref="Submit"/> takes them all. Images go to <see cref="Attachments"/>, with their own limits.
    /// </summary>
    public ObservableCollection<ChatFile> Files { get; } = new ObservableCollection<ChatFile>();

    /// <summary>The most files one message may carry.</summary>
    public int MaxFiles { get; set; } = 5;

    /// <summary>The largest file that may be attached, in bytes (25 MB by default).</summary>
    public long MaxFileBytes { get; set; } = 25L * 1024 * 1024;

    public ObservableCollection<ChatItem> Items { get; } = new ObservableCollection<ChatItem>();

    /// <summary>
    /// The subagents of the conversation, the newest first: each Agent call, filled by the claude's task lines and by
    /// the call's result (or, in a session file, by the task notification).
    /// </summary>
    public ObservableCollection<AgentInfo> Agents { get; } = new ObservableCollection<AgentInfo>();

    /// <summary>How many of the <see cref="Agents"/> are <see cref="AgentStatus.Running"/>.</summary>
    public int RunningAgents
    {
        get => _runningAgents;
        private set => SetField(ref _runningAgents, value);
    }

    /// <summary>Permission requests in arrival order; the view shows <see cref="CurrentPermission"/>.</summary>
    public ObservableCollection<PendingPermission> PendingPermissions { get; } =
        new ObservableCollection<PendingPermission>();

    public PendingPermission? CurrentPermission => _currentPermission;

    /// <summary>
    /// The plan of the <c>ExitPlanMode</c> request on screen (<see cref="CurrentPermission"/>); null when that is another
    /// request, or none. A new one for each request; the one that leaves keeps its <see cref="PlanReview.Outcome"/>.
    /// </summary>
    internal PlanReview? CurrentPlan => _currentPlan;

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
        private set
        {
            if (SetField(ref _model, value)) OnPropertyChanged(nameof(CurrentModel));
        }
    }

    /// <summary>The models the session offers; null until the claude tells. Also filled by <c>ModelsAvailable</c>.</summary>
    public IReadOnlyList<ChatModel>? AvailableModels
    {
        get => _availableModels;
        set
        {
            if (SetField(ref _availableModels, value)) OnPropertyChanged(nameof(CurrentModel));
        }
    }

    /// <summary>
    /// The offered model that <see cref="Model"/> is: the one whose value equals it, else the first whose resolved
    /// model equals it, leaving out the <c>default</c> entry; then the same two tries with a trailing <c>[...]</c>
    /// suffix of the id stripped (<c>claude-opus-5-5[1m]</c>); null when none is.
    /// </summary>
    public ChatModel? CurrentModel
    {
        get
        {
            if (_availableModels == null || _model == null) return null;
            var found = Find(_model);
            if (found != null) return found;
            var open = ModelSuffixStart(_model);
            return open > 0 ? Find(_model.Substring(0, open)) : null;
        }
    }

    /// <summary>The trailing <c>[...]</c> of <see cref="Model"/> (<c>[1m]</c>) when <see cref="CurrentModel"/> matched without it; else null.</summary>
    public string? ModelSuffix
    {
        get
        {
            if (_availableModels == null || _model == null || Find(_model) != null) return null;
            var open = ModelSuffixStart(_model);
            return open > 0 && Find(_model.Substring(0, open)) != null ? _model.Substring(open) : null;
        }
    }

    private static int ModelSuffixStart(string model) =>
        model.EndsWith("]", StringComparison.Ordinal) ? model.LastIndexOf('[') : -1;

    private ChatModel? Find(string id)
    {
        foreach (var model in _availableModels!)
        {
            if (model.Value == id) return model;
        }
        foreach (var model in _availableModels!)
        {
            if (model.ResolvedModel == id && model.Value != DefaultModelValue) return model;
        }
        return null;
    }

    /// <summary>The effort the claude reports; null when it has none (or has not told). <see cref="SetEffort"/> only asks.</summary>
    public string? Effort
    {
        get => _effort;
        private set => SetField(ref _effort, value);
    }

    /// <summary>The effort asked for with <see cref="SetEffort"/> and not yet confirmed; null when nothing waits.</summary>
    public string? RequestedEffort
    {
        get => _requestedEffort;
        private set => SetField(ref _requestedEffort, value);
    }

    /// <summary>The commands the claude lists (its <c>initialize</c> answer); set by the host or by <c>CommandsAvailable</c>.</summary>
    public IReadOnlyList<ChatCommand> Commands
    {
        get => _commands;
        set
        {
            if (SetField(ref _commands, value ?? Array.Empty<ChatCommand>())) OnPropertyChanged(nameof(PaletteCommands));
        }
    }

    /// <summary>The commands the terminal handles itself, from the session start; they are not for the palette.</summary>
    public IReadOnlyList<string> TerminalCommands
    {
        get => _terminalCommands;
        private set
        {
            if (SetField(ref _terminalCommands, value)) OnPropertyChanged(nameof(PaletteCommands));
        }
    }

    /// <summary>The names of the commands the conversation handles itself, without the slash.</summary>
    public static IReadOnlyList<string> LocalCommands => LocalCommandList;

    /// <summary>
    /// What the palette lists: <see cref="Commands"/> without the terminal ones, plus the <see cref="LocalCommands"/>
    /// the claude did not list (with no description), by name.
    /// </summary>
    public IReadOnlyList<ChatCommand> PaletteCommands
    {
        get
        {
            var list = new List<ChatCommand>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var command in _commands)
            {
                if (_terminalCommands.Contains(command.Name) || !names.Add(command.Name)) continue;
                list.Add(command);
            }
            foreach (var name in LocalCommandNames)
            {
                if (names.Add(name)) list.Add(new ChatCommand(name, "", ""));
            }
            return list;
        }
    }

    /// <summary>The panel to show over the reply box; <see cref="ClosePanel"/> hides it.</summary>
    public ChatPanel ShownPanel
    {
        get => _shownPanel;
        private set => SetField(ref _shownPanel, value);
    }

    /// <summary>The context window usage the claude last reported; null until it does.</summary>
    public ContextUsage? ContextUsage
    {
        get => _contextUsage;
        private set => SetField(ref _contextUsage, value);
    }

    /// <summary>The rate limit windows the claude last reported; null until it does.</summary>
    public UsageLimits? UsageLimits
    {
        get => _usageLimits;
        private set => SetField(ref _usageLimits, value);
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
        private set
        {
            if (value == AutoMode) _autoModeAsked = false; // applied: an error from now on is not its refusal
            SetField(ref _permissionMode, value);
        }
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

    /// <summary>
    /// When the prompt cache expires: the last request (the last top-level user line, or the message just sent) plus
    /// the cache's lifetime, never beyond <c>Clock() + lifetime</c>. Null until the claude reports cache use.
    /// </summary>
    public DateTimeOffset? PromptCacheExpiresAt
    {
        get => _promptCacheExpiresAt;
        private set => SetField(ref _promptCacheExpiresAt, value);
    }

    /// <summary>The lifetime of the cache the claude last reported (1 h or 5 min); kept when a later report has none.</summary>
    public TimeSpan? PromptCacheTtl
    {
        get => _promptCacheTtl;
        private set => SetField(ref _promptCacheTtl, value);
    }

    /// <summary>True after a compaction until the next cache use is reported: the cache is cold.</summary>
    public bool IsPromptCacheCompacted
    {
        get => _isPromptCacheCompacted;
        private set => SetField(ref _isPromptCacheCompacted, value);
    }

    /// <summary>The browser extension's state; the host sets it.</summary>
    public ChatBrowserStatus BrowserStatus
    {
        get => _browserStatus;
        set => SetField(ref _browserStatus, value);
    }

    /// <summary>Why the browser is in <see cref="ChatBrowserStatus.Error"/>; the host sets it.</summary>
    public string? BrowserError
    {
        get => _browserError;
        set => SetField(ref _browserError, value);
    }

    /// <summary>The Remote Control state; the host sets it, and the claude's <c>bridge_state</c> moves it.</summary>
    public ChatRemoteControlStatus RemoteControlStatus
    {
        get => _remoteControlStatus;
        set => SetField(ref _remoteControlStatus, value);
    }

    /// <summary>The Remote Control session link; the host sets it, or the claude's answer brings it.</summary>
    public string? RemoteControlUrl
    {
        get => _remoteControlUrl;
        set => SetField(ref _remoteControlUrl, value);
    }

    /// <summary>Why the Remote Control failed.</summary>
    public string? RemoteControlError
    {
        get => _remoteControlError;
        set => SetField(ref _remoteControlError, value);
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
                TerminalCommands = started.TerminalCommands;
                break;
            case CommandsAvailable available:
                Commands = available.Commands;
                break;
            case ModelsAvailable models:
                AvailableModels = models.Models;
                break;
            case SettingsApplied applied:
                if (applied.Model != null)
                {
                    Model = applied.Model;
                    RequestedModel = null;
                }
                if (applied.HasEffort)
                {
                    Effort = applied.Effort;
                    RequestedEffort = null;
                }
                break;
            case ContextUsageReported context:
                ContextUsage = context.Usage;
                break;
            case UsageLimitsReported limits:
                UsageLimits = limits.Limits;
                break;
            case CommandEcho echo:
                OnCommandEcho(echo);
                break;
            case CommandOutput output:
                Items.Add(new CommandOutputItem(output.Text));
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
            case PromptCacheUsed cache:
                OnPromptCacheUsed(cache);
                break;
            case RemoteControlChanged remote:
                OnRemoteControlChanged(remote);
                break;
            case AgentTaskStarted started:
                OnAgentTaskStarted(started);
                break;
            case AgentTaskProgress progress:
                OnAgentTaskProgress(progress);
                break;
            case AgentTaskUpdated updated:
                OnAgentTaskUpdated(updated);
                break;
            case AgentTaskFinished finished:
                OnAgentTaskFinished(finished);
                break;
        }
        UpdateStreaming();
        // the agents are looked at after what can change them (a task line, an Agent call or its result, a turn that ends), not at every piece of text
        if (_agentsChanged || e is AgentTaskStarted || e is AgentTaskProgress || e is AgentTaskUpdated || e is AgentTaskFinished)
            UpdateRunningAgents();
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
        SettleAgentsLeftRunning();
        UpdateStreaming();
        UpdateRunningAgents();
    }

    /// <summary>Empties the items, the agents and the pending requests. Session details stay.</summary>
    public void Clear()
    {
        Items.Clear();
        Agents.Clear();
        _agentsStoppedByHistory.Clear();
        if (PendingPermissions.Count > 0) PendingPermissions.Clear();
        _tools.Clear();
        _nestedTools.Clear();
        _turnTools.Clear();
        _live.Clear();
        _parser = new StreamJsonParser();
        _echoes.Clear();
        _unanswered.Clear();
        _interruptedThisTurn = false;
        _autoModeAsked = false;
        _cacheAnchor = null; // the cache belongs to the items; the browser and Remote Control belong to the host
        PromptCacheExpiresAt = null;
        PromptCacheTtl = null;
        IsPromptCacheCompacted = false;
        IsBusy = false;
        UpdateStreaming();
        UpdateRunningAgents();
    }

    // ---- user actions ----

    /// <summary>
    /// Sends a message with the <see cref="Attachments"/> and the <see cref="Files"/>, which it empties; blank text is
    /// ignored unless there is an image or a file. Works while busy: the claude queues it.
    /// </summary>
    public bool Submit(string text)
    {
        var blank = string.IsNullOrWhiteSpace(text);
        if (blank && Attachments.Count == 0 && Files.Count == 0) return false;
        if (!blank && TryLocalCommand(text.Trim(), out var name, out var arguments))
        {
            InvokeCommand(name, arguments);
            return true;
        }
        if (blank) text = "";
        var images = Attachments.Count == 0 ? null : new List<ChatImage>(Attachments);
        Attachments.Clear();
        var files = Files.Count == 0 ? null : new List<ChatFile>(Files);
        Files.Clear();
        var submitted = new MessageSubmittedEventArgs(text, images, files) { Owner = this };
        _cacheAnchor = _clock();
        if (EchoSubmittedMessages)
        {
            submitted.Item = new UserMessageItem(text, images, files) { Timestamp = _clock() };
            Items.Add(submitted.Item);
            _echoes.Add(submitted);
        }
        if (!IsBusy || _unanswered.Count > 0) _unanswered.Add(submitted);
        IsBusy = true;
        MessageSubmitted?.Invoke(this, submitted);
        return true;
    }

    /// <summary>
    /// Fits the image to <see cref="ImageBudgetBytes"/> and <see cref="MaxImageSide"/> off the UI thread and adds it to
    /// <see cref="Attachments"/>. Await it on the UI thread: the adding happens in the continuation.
    /// </summary>
    public async Task<AddImageResult> AddImageAsync(byte[] encoded)
    {
        if (encoded == null) throw new ArgumentNullException(nameof(encoded));
        if (Attachments.Count >= MaxImages) return AddImageResult.TooMany;
        var remaining = ImageBudgetBytes - AttachedBytes();
        var side = MaxImageSide;
        var fitted = await Task.Run(() => ImageFitter.Fit(encoded, remaining, side));
        if (fitted == null)
            return ImageFitter.Measure(encoded) == null ? AddImageResult.NotAnImage : AddImageResult.TooBig;
        // other images may have come while this one was being fitted
        if (Attachments.Count >= MaxImages) return AddImageResult.TooMany;
        if (fitted.Data.Length > ImageBudgetBytes - AttachedBytes()) return AddImageResult.TooBig;
        Attachments.Add(ChatImage.FromBytes(fitted.Data, fitted.MediaType));
        return AddImageResult.Added;
    }

    /// <summary>Takes an image out of <see cref="Attachments"/>.</summary>
    public void RemoveAttachment(ChatImage image)
    {
        if (image == null) throw new ArgumentNullException(nameof(image));
        Attachments.Remove(image);
    }

    /// <summary>
    /// Adds a PDF, text or CSV file from the disk to <see cref="Files"/>: a <c>.pdf</c> that begins with
    /// <c>%PDF-</c>, or a <c>.txt</c>, <c>.log</c>, <c>.md</c>, <c>.json</c> or <c>.csv</c> with no null byte in its
    /// first 8 KB, of <see cref="MaxFileBytes"/> at most. Only the size and the first 8 KB are read, off the UI thread:
    /// the host reads the file itself, in parts, from <see cref="ChatFile.LocalPath"/>. Await it on the UI thread: the
    /// adding happens in the continuation. A file that cannot be read is <see cref="AddFileResult.NotSupported"/>.
    /// </summary>
    public async Task<AddFileResult> AddFileAsync(string path)
    {
        if (path == null) throw new ArgumentNullException(nameof(path));
        if (Files.Count >= MaxFiles) return AddFileResult.TooMany;
        var maxBytes = MaxFileBytes;
        var found = await Task.Run(() => InspectFile(path, maxBytes));
        if (found.Result != AddFileResult.Added) return found.Result;
        // other files may have come while this one was being read
        if (Files.Count >= MaxFiles) return AddFileResult.TooMany;
        Files.Add(new ChatFile(found.Name, found.Size, found.Kind, found.FullPath));
        return AddFileResult.Added;
    }

    /// <summary>
    /// Adds a file that does not come from the disk (a paste, a download) to <see cref="Files"/>, by its name and its
    /// bytes, with the rules of <see cref="AddFileAsync"/>; the bytes stay in <see cref="ChatFile.Content"/>.
    /// </summary>
    public AddFileResult AddFile(string name, byte[] content)
    {
        if (name == null) throw new ArgumentNullException(nameof(name));
        if (content == null) throw new ArgumentNullException(nameof(content));
        if (Files.Count >= MaxFiles) return AddFileResult.TooMany;
        var fileName = ChatFile.LastSegment(name);
        if (ChatFile.KindOf(fileName) is not { } kind) return AddFileResult.NotSupported;
        if (content.Length > MaxFileBytes) return AddFileResult.TooBig;
        if (!ChatFile.StartFits(kind, content, content.Length)) return AddFileResult.NotSupported;
        Files.Add(new ChatFile(fileName, content.Length, kind, null, content));
        return AddFileResult.Added;
    }

    /// <summary>Takes a file out of <see cref="Files"/>.</summary>
    public void RemoveFile(ChatFile file)
    {
        if (file == null) throw new ArgumentNullException(nameof(file));
        Files.Remove(file);
    }

    /// <summary>The kind, the size and the first bytes of a file on the disk; never throws.</summary>
    private static FoundFile InspectFile(string path, long maxBytes)
    {
        try
        {
            var full = System.IO.Path.GetFullPath(path);
            var name = System.IO.Path.GetFileName(full);
            if (ChatFile.KindOf(name) is not { } kind) return new FoundFile(AddFileResult.NotSupported);
            var info = new System.IO.FileInfo(full);
            if (!info.Exists) return new FoundFile(AddFileResult.NotSupported);
            if (info.Length > maxBytes) return new FoundFile(AddFileResult.TooBig);
            var head = new byte[ChatFile.TextCheckBytes];
            var count = 0;
            using (var stream = new System.IO.FileStream(full, System.IO.FileMode.Open, System.IO.FileAccess.Read,
                       System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete))
            {
                int read;
                while (count < head.Length && (read = stream.Read(head, count, head.Length - count)) > 0) count += read;
            }
            if (!ChatFile.StartFits(kind, head, count)) return new FoundFile(AddFileResult.NotSupported);
            return new FoundFile(AddFileResult.Added, name, full, info.Length, kind);
        }
        catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException || e is ArgumentException
                                  || e is NotSupportedException || e is System.Security.SecurityException)
        {
            return new FoundFile(AddFileResult.NotSupported);
        }
    }

    private readonly struct FoundFile
    {
        public FoundFile(AddFileResult result, string name = "", string fullPath = "", long size = 0, ChatFileKind kind = ChatFileKind.Text)
        {
            Result = result;
            Name = name;
            FullPath = fullPath;
            Size = size;
            Kind = kind;
        }

        public AddFileResult Result { get; }
        public string Name { get; }
        public string FullPath { get; }
        public long Size { get; }
        public ChatFileKind Kind { get; }
    }

    private static int DecodedLength(string data)
    {
        var length = data.Length;
        var padding = 0;
        if (length > 0 && data[length - 1] == '=') padding++;
        if (length > 1 && data[length - 2] == '=') padding++;
        return length / 4 * 3 - padding + (length % 4 == 0 ? 0 : length % 4 - 1);
    }

    private int AttachedBytes()
    {
        var total = 0;
        foreach (var image in Attachments) total += DecodedLength(image.Data);
        return total;
    }

    /// <summary>
    /// Takes back a message the host could not deliver (no connection, refused, a file that did not get through): its
    /// images and files go back to the reply box when it holds none, it leaves <see cref="Items"/>, the
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
        // the images go back to the reply box unless it already holds others
        if (Attachments.Count == 0)
        {
            foreach (var image in submitted.Images) Attachments.Add(image);
        }
        // so do the files, no longer being sent: their × shows again, and a new send starts over
        if (Files.Count == 0)
        {
            foreach (var file in submitted.Files)
            {
                file.Progress = null;
                file.RemotePath = null;
                Files.Add(file);
            }
        }
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
        var index = IndexOfPermission(request.RequestId);
        if (index < 0) return;
        var queued = PendingPermissions[index];
        // the plan knows how it ended before it stops being the current one
        if (_currentPlan != null && ReferenceEquals(_currentPlan.Request, queued))
            _currentPlan.Outcome = decision == PermissionDecision.Deny ? PlanOutcome.Rejected : PlanOutcome.Approved;
        PendingPermissions.RemoveAt(index);
        PermissionAnswered?.Invoke(this, new PermissionAnsweredEventArgs(queued, decision, message));
    }

    /// <summary>
    /// The plan card's "Yes" for an <c>ExitPlanMode</c> request: asks for <paramref name="mode"/> (<see cref="SetPermissionMode"/>)
    /// and allows the request right after, without waiting, as the claude applies them in the order it reads them. If the auto
    /// mode was asked for and the claude answers a control request with an error before it applies it, acceptEdits is asked for
    /// instead, once. A request that already left the queue gets neither.
    /// </summary>
    internal void ApprovePlan(PendingPermission request, string mode)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (mode == null) throw new ArgumentNullException(nameof(mode));
        if (IndexOfPermission(request.RequestId) < 0) return; // the claude no longer waits for it
        _autoModeAsked = mode == AutoMode;
        SetPermissionMode(mode);
        AnswerPermission(request, PermissionDecision.Allow);
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

    /// <summary>
    /// Asks for an effort level (null for the model's own): raises <see cref="EffortRequested"/>; the level waits in
    /// <see cref="RequestedEffort"/>, and <see cref="Effort"/> changes when the claude reports it.
    /// </summary>
    public void SetEffort(string? level)
    {
        RequestedEffort = level;
        EffortRequested?.Invoke(this, new EffortRequestedEventArgs(level));
    }

    /// <summary>
    /// Runs a local command: raises <see cref="CommandInvoked"/>, and <c>context</c> and <c>usage</c> also show their
    /// panel. Nothing goes to the claude and no item is added.
    /// </summary>
    public void InvokeCommand(string name, string arguments)
    {
        if (name == null) throw new ArgumentNullException(nameof(name));
        arguments ??= "";
        if (name == "context") ShownPanel = ChatPanel.Context;
        else if (name == "usage") ShownPanel = ChatPanel.Usage;
        CommandInvoked?.Invoke(this, new CommandInvokedEventArgs(name, arguments));
    }

    /// <summary>Asks the host to disconnect the browser (raises <see cref="BrowserDisconnectRequested"/>) unless it is off; the state changes when the host sets it.</summary>
    public void DisconnectBrowser()
    {
        if (_browserStatus == ChatBrowserStatus.Off) return;
        BrowserDisconnectRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Asks the host to open the Remote Control link (raises <see cref="RemoteControlLinkRequested"/>); nothing without a link.</summary>
    public void OpenRemoteControl()
    {
        var url = _remoteControlUrl;
        if (url == null || url.Length == 0) return;
        RemoteControlLinkRequested?.Invoke(this, new RemoteControlLinkRequestedEventArgs(url));
    }

    /// <summary>Hides the panel.</summary>
    public void ClosePanel() => ShownPanel = ChatPanel.None;

    /// <summary>Shows the Agent map (<see cref="ChatPanel.Agents"/>), as the "N agents" chip does; <see cref="ClosePanel"/> hides it.</summary>
    public void ShowAgents() => ShownPanel = ChatPanel.Agents;

    /// <summary>A trimmed text of the form <c>/name arguments</c> whose name is one of <see cref="LocalCommands"/>.</summary>
    private static bool TryLocalCommand(string text, out string name, out string arguments)
    {
        name = arguments = "";
        if (text.Length < 2 || text[0] != '/') return false;
        var end = 1;
        while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
        var token = text.Substring(1, end - 1);
        if (Array.IndexOf(LocalCommandNames, token) < 0) return false;
        name = token;
        arguments = text.Substring(end).Trim();
        return true;
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
        ReviveFromHistory(e.ParentToolUseId);
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
                if (ToolPresentation.HiddenTools.Contains(tool.Name)) break;
                if (live?.Item is ToolCallItem streamedTool)
                {
                    if (streamedTool.ToolUseId.Length == 0 && tool.Id.Length > 0)
                    {
                        // the block started without an id: take it now so results and subagent items find the call
                        streamedTool.ToolUseId = tool.Id;
                        Register(streamedTool);
                    }
                    Fill(streamedTool, tool);
                    Reveal(live); // its input is whole: if the stop did not bring it yet, it shows now
                    Complete(live);
                }
                else
                {
                    var item = new ToolCallItem { ToolUseId = tool.Id, Name = tool.Name, Status = ToolStatus.Running };
                    Fill(item, tool);
                    AddTool(item, e.ParentToolUseId);
                }
                if (IsAgentTool(tool.Name)) OnAgentCall(tool, e.ParentToolUseId, e.Timestamp);
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
        ReviveFromHistory(e.ParentToolUseId);
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
                // a call shows once its input is complete (the block's stop, or its assistant line), like the extension's; a hidden
                // tool's never does. Until then "Working…" is all there is: the call is not what IsStreaming counts.
                if (ToolPresentation.HiddenTools.Contains(tool.Name)) return;
                var call = new ToolCallItem
                {
                    ToolUseId = tool.Id, Name = tool.Name, Input = tool.Input, Status = ToolStatus.Running,
                };
                _live.Add(new LiveBlock(e.MessageId, e.ParentToolUseId, e.Index, call) { Hidden = true });
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
        Reveal(live);
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

    /// <summary>A streamed call whose input is complete takes its place in the list (once).</summary>
    private void Reveal(LiveBlock live)
    {
        if (!live.Hidden || live.Item is not ToolCallItem call) return;
        live.Hidden = false;
        AddTool(call, live.ParentToolUseId);
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
        // a top-level user line (message or tool result) is a request: the cache lifetime counts from it
        if (e.ParentToolUseId == null) _cacheAnchor = e.Timestamp ?? _clock();
        ReviveFromHistory(e.ParentToolUseId); // a subagent's own tool result: it runs
        if (e.Block is ToolResultBlock result)
            OnToolResult(e, result);
        else if (e.Block is TextContentBlock text)
            OnUserText(e, text.Text, ImagesOf(e));
    }

    private void OnToolResult(UserBlock e, ToolResultBlock result)
    {
        RemovePermissionsFor(result.ToolUseId);
        if (FindAgent(result.ToolUseId) is { } agent) OnAgentResult(agent, e, result);
        if (!_tools.TryGetValue(result.ToolUseId, out var tool)) return;
        var cut = ToolPresentation.Truncate(result.Text, ToolPresentation.OutputLines);
        tool.ResultText = cut.Text;
        tool.HiddenResultLines = cut.HiddenLines;
        tool.FullResultText = KeptResult(tool.Name, result.Text);
        tool.RejectionReason = RefusalReason(result);
        tool.ResultImages = ToolPresentation.ChromeAction(tool.Name) == "computer" ? ResultImagesOf(result) : null; // only that action draws them
        tool.Status = ResultStatus(e, result);
    }

    /// <summary>The images a tool result carried, or null when it had none. Kept only for the Claude in Chrome <c>computer</c> action, the one row that draws them.</summary>
    private static IReadOnlyList<ChatImage>? ResultImagesOf(ToolResultBlock result)
    {
        if (result.Images.Count == 0) return null;
        var images = new List<ChatImage>(result.Images.Count);
        foreach (var image in result.Images) images.Add(new ChatImage(image.MediaType, image.Data));
        return images;
    }

    /// <summary>The whole result for the item: without ANSI in a command's output, and cut at the cap.</summary>
    private static string KeptResult(string toolName, string text)
    {
        if (toolName == "Bash" || toolName == "PowerShell" || toolName == "TaskOutput") text = AnsiText.Strip(text);
        return text.Length > ToolPresentation.MaxKeptResultChars ? text.Substring(0, ToolPresentation.MaxKeptResultChars) : text;
    }

    /// <summary>
    /// What the user typed when they refused a call. The claude says it never ran by the meta of the result; its text is the
    /// typed text, unless the user typed none, and then it is the claude's own sentence. A refusal can also come as the claude's
    /// sentence followed by "The user provided the following reason for the rejection: " and the typed text: the reason is what
    /// follows. The sentence alone has no reason.
    /// </summary>
    private static string? RefusalReason(ToolResultBlock result)
    {
        var text = result.Text;
        if (text.StartsWith(RejectedTextPrefix, StringComparison.Ordinal))
        {
            var lead = text.IndexOf(RejectionReasonLead, StringComparison.Ordinal);
            if (lead < 0) return null;
            var typed = text.Substring(lead + RejectionReasonLead.Length).Trim();
            return typed.Length == 0 ? null : typed;
        }
        if (result.NonExecutionKind != "permission-rule" && result.NonExecutionKind != "user-rejected") return null;
        return text.Trim().Length == 0 ? null : text;
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

    /// <summary>The images of a user line, leaving out those that came without data.</summary>
    private static IReadOnlyList<ChatImage>? ImagesOf(UserBlock e)
    {
        List<ChatImage>? images = null;
        foreach (var image in e.Images)
        {
            if (image.Data.Length == 0) continue;
            (images ??= new List<ChatImage>()).Add(new ChatImage(image.MediaType, image.Data));
        }
        return images;
    }

    private void OnUserText(UserBlock e, string text, IReadOnlyList<ChatImage>? images)
    {
        // only the claude's two exact texts are interruptions; any other text is the user's message as written
        if (text == InterruptedText || text == ToolInterruptedText)
        {
            _interruptedThisTurn = true;
            AddNotice(text == InterruptedText ? NoticeKind.Interrupted : NoticeKind.ToolInterrupted, e.ParentToolUseId);
            return;
        }
        // a task notification is the claude telling itself that a background subagent finished: it ends the agent and
        // stays out of the conversation
        if (text.StartsWith(TaskNotificationPrefix, StringComparison.Ordinal))
        {
            if (TaskNotificationText.Read(text) is { } finished) OnAgentTaskFinished(finished);
            return;
        }
        if (e.IsMeta) return;
        if (e.IsCompactSummary)
        {
            AddNotice(NoticeKind.Compacted, e.ParentToolUseId);
            return;
        }
        // the files the host sent are named by lines at the end of the text: chips, not text
        var (shown, paths) = AttachedFileLine.Split(text);
        if (shown.Length == 0 && images == null && paths.Count == 0) return;
        // Split takes the line breaks before the markers off: a sent text that ended with one still matches
        if (e.ParentToolUseId == null && _echoes.Count > 0
            && (_echoes[0].Text == text || _echoes[0].Text.TrimEnd('\r', '\n') == shown))
        {
            _echoes.RemoveAt(0); // the claude repeated what Submit already shows
            return;
        }
        List<ChatFile>? files = null;
        foreach (var path in paths) (files ??= new List<ChatFile>()).Add(ChatFile.FromRemotePath(path));
        Place(new UserMessageItem(shown, images, files) { Timestamp = e.Timestamp }, e.ParentToolUseId);
    }

    private void OnCommandEcho(CommandEcho e)
    {
        var text = "/" + e.Name + (e.Arguments.Length == 0 ? "" : " " + e.Arguments);
        if (_echoes.Count > 0 && _echoes[0].Text == text)
        {
            _echoes.RemoveAt(0); // the claude repeated what Submit already shows
            return;
        }
        Items.Add(new UserMessageItem(text));
    }

    // ---- agents ----

    private static bool IsAgentTool(string name) => name == "Agent" || name == "Task";

    private AgentInfo? FindAgent(string? toolUseId)
    {
        if (string.IsNullOrEmpty(toolUseId)) return null;
        foreach (var agent in Agents)
        {
            if (agent.Id == toolUseId) return agent;
        }
        return null;
    }

    /// <summary>The agent of a task line: by its call's id, else by the task id.</summary>
    private AgentInfo? FindAgent(string? toolUseId, string taskId)
    {
        var agent = FindAgent(toolUseId);
        if (agent != null) return agent;
        foreach (var candidate in Agents)
        {
            if (candidate.TaskId == taskId) return candidate;
        }
        return null;
    }

    private AgentInfo NewAgent(string id, DateTimeOffset startedAt)
    {
        _agentsChanged = true;
        var agent = new AgentInfo(id) { StartedAt = startedAt };
        Agents.Insert(0, agent);
        return agent;
    }

    private void UpdateRunningAgents()
    {
        _agentsChanged = false;
        var running = 0;
        foreach (var agent in Agents)
        {
            if (agent.Status == AgentStatus.Running) running++;
        }
        RunningAgents = running;
        UpdateAgentStates();
    }

    /// <summary>An Agent (or Task) call with its whole input: it starts the agent, or completes the one a task line started.</summary>
    private void OnAgentCall(ToolUseBlock tool, string? parentToolUseId, DateTimeOffset? timestamp)
    {
        _agentsChanged = true;
        if (tool.Id.Length == 0) return;
        var agent = FindAgent(tool.Id);
        if (agent == null)
        {
            agent = NewAgent(tool.Id, timestamp ?? _clock());
            agent.Depth = parentToolUseId == null ? 1 : (FindAgent(parentToolUseId)?.Depth ?? 1) + 1;
            if (JsonHelpers.Prop(tool.Input, "run_in_background") is { ValueKind: JsonValueKind.True })
                agent.IsBackground = true;
        }
        else if (timestamp != null)
        {
            agent.StartedAt = timestamp.Value;
        }
        agent.ParentId ??= parentToolUseId;
        if (agent.Description.Length == 0) agent.Description = JsonHelpers.Str(tool.Input, "description") ?? "";
        agent.AgentType ??= JsonHelpers.Str(tool.Input, "subagent_type");
        if (JsonHelpers.Str(tool.Input, "prompt") is { Length: > 0 } prompt) agent.Prompt = prompt; // the call's own, over a task line's
        LinkCall(agent);
    }

    /// <summary>The Agent call's row gets its agent, for the prompt and the background line.</summary>
    private void LinkCall(AgentInfo agent)
    {
        if (_tools.TryGetValue(agent.Id, out var call)) call.Agent = agent;
    }

    private void OnAgentTaskStarted(AgentTaskStarted e)
    {
        // a command the agent runs (local_bash) is a task too, but no agent
        if (e.TaskType.Length > 0 && e.TaskType != AgentTaskType) return;
        var agent = FindAgent(e.ToolUseId, e.TaskId);
        if (agent == null)
        {
            if (e.TaskType.Length == 0 || e.ToolUseId.Length == 0) return;
            agent = NewAgent(e.ToolUseId, _clock());
        }
        agent.TaskId = e.TaskId;
        agent.IsBackground = e.IsBackground;
        agent.Depth = e.Depth;
        if (agent.Description.Length == 0) agent.Description = e.Description;
        agent.AgentType ??= e.AgentType;
        if (agent.Prompt == null && !string.IsNullOrEmpty(e.Prompt)) agent.Prompt = e.Prompt;
        Resume(agent);
        ReviveFromHistory(agent.ParentId);
        LinkCall(agent);
    }

    private void OnAgentTaskProgress(AgentTaskProgress e)
    {
        var agent = FindAgent(e.ToolUseId, e.TaskId);
        if (agent == null) return;
        agent.TaskId ??= e.TaskId;
        if (e.Activity != null) agent.Activity = e.Activity;
        if (e.Tokens != null) agent.Tokens = e.Tokens;
        if (e.ToolUses != null) agent.ToolUses = e.ToolUses;
        if (e.DurationMs != null) agent.DurationMs = e.DurationMs;
        Resume(agent);
        ReviveFromHistory(agent.ParentId);
    }

    /// <summary>A failure or a stop ends the agent here; <c>completed</c> waits for the notification or the result, which bring it.</summary>
    private void OnAgentTaskUpdated(AgentTaskUpdated e)
    {
        var agent = FindAgent(null, e.TaskId);
        if (agent == null || agent.Status != AgentStatus.Running || e.Status == null) return;
        var status = EndStatus(e.Status);
        if (status != AgentStatus.Completed) agent.Status = status;
    }

    private void OnAgentTaskFinished(AgentTaskFinished e)
    {
        var agent = FindAgent(e.ToolUseId, e.TaskId);
        if (agent == null) return;
        _agentsStoppedByHistory.Remove(agent);
        agent.TaskId ??= e.TaskId;
        agent.Status = EndStatus(e.Status);
        if (e.Summary != null) agent.Result = e.Summary;
        if (e.Tokens != null) agent.Tokens = e.Tokens;
        if (e.ToolUses != null) agent.ToolUses = e.ToolUses;
        if (e.DurationMs != null) agent.DurationMs = e.DurationMs;
    }

    /// <summary>
    /// The result of the Agent call. A foreground agent's (<c>totalTokens</c>) ends it with its totals; a background
    /// one's (<c>isAsync</c>) only tells the model, and the notification ends it later. Without the structured result
    /// (an older claude, a refusal, an error), the tool result's own status and text are used.
    /// </summary>
    private void OnAgentResult(AgentInfo agent, UserBlock e, ToolResultBlock result)
    {
        _agentsChanged = true;
        if (e.ToolUseResult is { ValueKind: JsonValueKind.Object } structured)
        {
            var agentId = JsonHelpers.Str(structured, "agentId");
            if (agentId != null && agent.TaskId != null && agentId != agent.TaskId) return; // another call's result
            if (agentId != null) agent.TaskId ??= agentId;
            if (JsonHelpers.Str(structured, "resolvedModel") is { } model) agent.Model = model;
            agent.AgentType ??= JsonHelpers.Str(structured, "agentType");
            if (JsonHelpers.Bool(structured, "isAsync"))
            {
                agent.IsBackground = true;
                return;
            }
            if (JsonHelpers.Long(structured, "totalTokens") is { } tokens)
            {
                _agentsStoppedByHistory.Remove(agent);
                agent.Status = AgentStatus.Completed;
                agent.Tokens = tokens;
                if (JsonHelpers.Long(structured, "totalDurationMs") is { } duration) agent.DurationMs = duration;
                if (JsonHelpers.Long(structured, "totalToolUseCount") is { } uses) agent.ToolUses = (int)uses;
                agent.Result = ResultText(JsonHelpers.Prop(structured, "content")) ?? result.Text;
                return;
            }
        }
        if (agent.Status != AgentStatus.Running) return;
        switch (ResultStatus(e, result))
        {
            case ToolStatus.Rejected:
                agent.Status = AgentStatus.Stopped;
                break;
            case ToolStatus.Failed:
                agent.Status = AgentStatus.Failed;
                agent.Result = result.Text;
                break;
            default:
                if (agent.IsBackground) break; // the launch receipt of a background agent: it ends with its notification
                agent.Status = AgentStatus.Completed;
                agent.Result = result.Text;
                break;
        }
    }

    /// <summary>The <c>content</c> of an agent's result: a string, or the texts of its blocks joined by a blank line.</summary>
    private static string? ResultText(JsonElement? content)
    {
        if (content == null) return null;
        var c = content.Value;
        if (c.ValueKind == JsonValueKind.String) return c.GetString();
        if (c.ValueKind != JsonValueKind.Array) return null;
        var texts = new List<string>();
        foreach (var block in c.EnumerateArray())
        {
            if (JsonHelpers.Str(block, "type") == "text" && JsonHelpers.Str(block, "text") is { } text) texts.Add(text);
        }
        return texts.Count == 0 ? null : string.Join("\n\n", texts);
    }

    private static AgentStatus EndStatus(string status)
    {
        switch (status)
        {
            case "":
            case "completed":
                return AgentStatus.Completed;
            case "failed":
            case "error":
                return AgentStatus.Failed;
            default: // killed, stopped…
                return AgentStatus.Stopped;
        }
    }

    /// <summary>
    /// At the end of a session file, every agent with no end is <see cref="AgentStatus.Stopped"/>, remembered as
    /// stopped by the history: a sign of life from the live claude brings it back (<see cref="ReviveFromHistory"/>,
    /// <see cref="Resume"/>).
    /// </summary>
    private void SettleAgentsLeftRunning()
    {
        foreach (var agent in Agents)
        {
            if (agent.Status != AgentStatus.Running) continue;
            _agentsChanged = true;
            agent.Status = AgentStatus.Stopped;
            _agentsStoppedByHistory.Add(agent);
        }
    }

    /// <summary>
    /// A line of a subagent (its <c>parent_tool_use_id</c>) proves that agent runs: when the history had stopped it,
    /// it runs again, and so do the callers the history had stopped.
    /// </summary>
    private void ReviveFromHistory(string? parentToolUseId)
    {
        if (_agentsStoppedByHistory.Count == 0) return;
        var agent = FindAgent(parentToolUseId);
        var steps = Agents.Count; // a guard against a loop of parents
        while (agent != null && steps-- > 0)
        {
            if (_agentsStoppedByHistory.Remove(agent))
            {
                _agentsChanged = true;
                agent.Status = AgentStatus.Running;
            }
            agent = FindAgent(agent.ParentId);
        }
    }

    /// <summary>A task line of the agent itself: it runs, whatever ended it before (the history, or an end it was resumed from).</summary>
    private void Resume(AgentInfo agent)
    {
        _agentsStoppedByHistory.Remove(agent);
        _agentsChanged = true;
        agent.Status = AgentStatus.Running;
    }

    /// <summary>
    /// A failed or interrupted turn takes its foreground agents with it, and the foreground agents those called.
    /// Background agents go on, with the agents they call.
    /// </summary>
    private void StopForegroundAgents()
    {
        // oldest first, so a caller is settled before the agents it called
        for (var i = Agents.Count - 1; i >= 0; i--)
        {
            var agent = Agents[i];
            if (agent.Status != AgentStatus.Running || agent.IsBackground) continue;
            var caller = FindAgent(agent.ParentId);
            if (caller == null || caller.Status != AgentStatus.Running)
            {
                _agentsChanged = true;
                agent.Status = AgentStatus.Stopped;
            }
        }
    }

    // ---- permissions ----

    private void OnPermissionRequested(PermissionRequested e)
    {
        foreach (var queued in PendingPermissions)
        {
            if (queued.RequestId == e.RequestId) return;
        }
        // the call of a plan may come without it (input {}): the request carries it, and the row and its document read it there
        if (e.ToolName == ExitPlanModeTool && e.ToolUseId != null && _tools.TryGetValue(e.ToolUseId, out var call)
            && !PlanReview.HasPlan(call.Input) && PlanReview.HasPlan(e.Input))
        {
            call.Input = e.Input;
            call.Summary = ToolPresentation.Summary(e.ToolName, e.Input, WorkingDirectory);
        }
        var request = new PendingPermission(
            e.RequestId, e.ToolName, e.DisplayName ?? e.ToolName, e.Description, e.ToolUseId,
            ToolPresentation.Summary(e.ToolName, e.Input, WorkingDirectory), e.Input, e.Suggestions)
        {
            AgentId = e.AgentId,
            DefaultToNo = e.DefaultToNo,
            SuppressAlwaysAllowRule = e.SuppressAlwaysAllowRule,
        };
        if (e.AgentId == null && AskingAgent(e.ToolUseId) is { } asking) _requestAgents[request] = asking;
        PendingPermissions.Add(request);
    }

    /// <summary>
    /// The agent a request that names none belongs to (no recording shows a subagent's request yet, so the claude may leave
    /// <c>agent_id</c> out): the agent whose step asks; none when the step is the main agent's own; else, for a step the
    /// conversation has not seen, the agent that started running last. Decided when the request comes.
    /// </summary>
    private AgentInfo? AskingAgent(string? toolUseId)
    {
        if (toolUseId != null && _tools.TryGetValue(toolUseId, out var step))
        {
            if (!_nestedTools.Contains(step)) return null; // the main agent's own call
            foreach (var agent in Agents)
            {
                if (_tools.TryGetValue(agent.Id, out var call) && call.Children.Contains(step)) return agent;
            }
        }
        foreach (var agent in Agents) // newest first
        {
            if (agent.Status == AgentStatus.Running) return agent;
        }
        return null;
    }

    /// <summary>The agent a queued request belongs to: by its <c>agent_id</c> (the agent's task id), else as decided when it came.</summary>
    private AgentInfo? AgentOf(PendingPermission request)
    {
        if (request.AgentId == null) return _requestAgents.TryGetValue(request, out var linked) ? linked : null;
        foreach (var agent in Agents)
        {
            if (agent.TaskId == request.AgentId) return agent;
        }
        return null;
    }

    /// <summary>
    /// After every change of the agents or of the queue: which agents wait for a request of theirs, and what the agents each one
    /// started (at any depth) are doing. Each agent is looked at once, from the agents it started up.
    /// </summary>
    private void UpdateAgentStates()
    {
        AgentStateUpdates++;
        if (Agents.Count == 0) return;
        HashSet<AgentInfo>? asking = null;
        foreach (var request in PendingPermissions)
        {
            if (AgentOf(request) is { } agent) (asking ??= new HashSet<AgentInfo>()).Add(agent);
        }
        foreach (var agent in Agents)
            agent.IsWaitingForPermission = agent.Status == AgentStatus.Running && asking != null && asking.Contains(agent);

        Dictionary<string, List<AgentInfo>>? started = null;
        foreach (var agent in Agents)
        {
            if (agent.ParentId == null) continue;
            started ??= new Dictionary<string, List<AgentInfo>>();
            if (!started.TryGetValue(agent.ParentId, out var list)) started[agent.ParentId] = list = new List<AgentInfo>();
            list.Add(agent);
        }
        if (started == null)
        {
            foreach (var agent in Agents) agent.Nested = AgentNesting.None; // no agent started another
            return;
        }
        var nested = new Dictionary<AgentInfo, AgentNesting>();
        foreach (var agent in Agents) agent.Nested = Nesting(agent, started, nested);
    }

    /// <summary>What the agents <paramref name="agent"/> started, at any depth, are doing; each agent's is worked out once.</summary>
    private static AgentNesting Nesting(AgentInfo agent, Dictionary<string, List<AgentInfo>> started, Dictionary<AgentInfo, AgentNesting> done)
    {
        if (done.TryGetValue(agent, out var known)) return known;
        done[agent] = AgentNesting.None; // a loop of parents ends here
        var nesting = AgentNesting.None;
        if (started.TryGetValue(agent.Id, out var children))
        {
            foreach (var child in children)
            {
                nesting |= Nesting(child, started, done);
                if (child.IsWaitingForPermission) nesting |= AgentNesting.Waiting;
                nesting |= child.Status switch
                {
                    AgentStatus.Running => AgentNesting.Working,
                    AgentStatus.Failed => AgentNesting.Failed,
                    AgentStatus.Stopped => AgentNesting.Stopped,
                    _ => AgentNesting.None,
                };
            }
        }
        done[agent] = nesting;
        return nesting;
    }

    private PendingPermission? RemovePermission(string? requestId)
    {
        var index = IndexOfPermission(requestId);
        if (index < 0) return null;
        var queued = PendingPermissions[index];
        PendingPermissions.RemoveAt(index);
        return queued;
    }

    private int IndexOfPermission(string? requestId)
    {
        for (var i = 0; i < PendingPermissions.Count; i++)
        {
            if (PendingPermissions[i].RequestId == requestId) return i;
        }
        return -1;
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
        if (e.Action == NotifyCollectionChangedAction.Reset) _requestAgents.Clear();
        else if (e.OldItems != null)
        {
            foreach (PendingPermission gone in e.OldItems) _requestAgents.Remove(gone);
        }
        UpdateAgentStates();
        var first = PendingPermissions.Count > 0 ? PendingPermissions[0] : null;
        // the plan first: whoever hears that the current request changed finds its plan already there
        if (!ReferenceEquals(_currentPlan?.Request, first))
        {
            var plan = first != null && first.ToolName == ExitPlanModeTool ? new PlanReview(first) : null;
            SetField(ref _currentPlan, plan, nameof(CurrentPlan));
        }
        SetField(ref _currentPermission, first, nameof(CurrentPermission));
    }

    // ---- control, turn end and notices ----

    private void OnControlAcknowledged(ControlAcknowledged e)
    {
        if (e.IsError)
        {
            // the auto mode a plan was approved with was refused: the next best, which needs no gate
            if (!_autoModeAsked) return;
            _autoModeAsked = false;
            SetPermissionMode(AcceptEditsMode);
            return;
        }
        if (e.Response is not { ValueKind: JsonValueKind.Object } response) return;
        if (response.TryGetProperty("mode", out var mode) && mode.ValueKind == JsonValueKind.String)
            PermissionMode = mode.GetString() ?? PermissionMode;
    }

    private void OnTurnCompleted(TurnCompleted e)
    {
        RemoveTurnPermissions();
        EndStreaming();
        if (e.IsError || _interruptedThisTurn)
        {
            RejectToolsLeftRunning();
            StopForegroundAgents();
        }
        if (e.IsError && !_interruptedThisTurn) Items.Add(new NoticeItem(NoticeKind.TurnFailed));
        Items.Add(new TurnFooterItem(e.DurationMs, e.InputTokens, e.OutputTokens, e.CostUsd, e.IsError));
        _turnTools.Clear();
        _interruptedThisTurn = false;
        _autoModeAsked = false; // the claude answers a mode request at once: an error after the turn is about something else
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

    private void OnPromptCacheUsed(PromptCacheUsed e)
    {
        IsPromptCacheCompacted = false;
        if (e.Ttl != null) PromptCacheTtl = e.Ttl;
        if (_promptCacheTtl == null) return;
        var now = _clock();
        var anchor = _cacheAnchor ?? now;
        if (anchor > now) anchor = now; // an anchor from a clock ahead of ours never pushes the expiry past now + lifetime
        PromptCacheExpiresAt = anchor + _promptCacheTtl.Value;
    }

    private void OnRemoteControlChanged(RemoteControlChanged e)
    {
        if (e.Url != null) RemoteControlUrl = e.Url;
        switch (e.State)
        {
            case "ready":
                RemoteControlStatus = ChatRemoteControlStatus.Connecting;
                RemoteControlError = null;
                break;
            case "connected":
                RemoteControlStatus = ChatRemoteControlStatus.Connected;
                RemoteControlError = null;
                break;
            case "failed":
                RemoteControlStatus = ChatRemoteControlStatus.Error;
                RemoteControlError = e.Detail;
                break;
        }
    }

    private void OnSystemNotice(SystemNotice e)
    {
        if (e.Subtype == "compact_boundary") IsPromptCacheCompacted = true;
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

        /// <summary>A tool call whose input is still arriving: its item is not in the list yet.</summary>
        public bool Hidden { get; set; }

        /// <summary>When the block started to arrive, by the conversation's clock.</summary>
        public DateTimeOffset StartedAt { get; set; }

        public long Elapsed(DateTimeOffset now) => Math.Max(0, (long)Math.Round((now - StartedAt).TotalMilliseconds));
    }
}
