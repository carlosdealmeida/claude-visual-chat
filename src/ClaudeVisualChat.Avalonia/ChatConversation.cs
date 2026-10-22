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
    private const string InterruptedPrefix = "[Request interrupted";
    private const string TaskNotificationPrefix = "<task-notification>";
    private const string RejectedResultPrefix = "User rejected";
    private const string RejectedTextPrefix = "The user doesn't want to proceed";
    private const string DefaultModelValue = "default";
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
    private StreamJsonParser _parser = new StreamJsonParser();
    private PendingPermission? _currentPermission;
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
        _cacheAnchor = null; // the cache belongs to the items; the browser and Remote Control belong to the host
        PromptCacheExpiresAt = null;
        PromptCacheTtl = null;
        IsPromptCacheCompacted = false;
        IsBusy = false;
        UpdateStreaming();
    }

    // ---- user actions ----

    /// <summary>
    /// Sends a message with the <see cref="Attachments"/>, which it empties; blank text is ignored unless there is an
    /// attachment. Works while busy: the claude queues it.
    /// </summary>
    public bool Submit(string text)
    {
        var blank = string.IsNullOrWhiteSpace(text);
        if (blank && Attachments.Count == 0) return false;
        if (!blank && TryLocalCommand(text.Trim(), out var name, out var arguments))
        {
            InvokeCommand(name, arguments);
            return true;
        }
        if (blank) text = "";
        var images = Attachments.Count == 0 ? null : new List<ChatImage>(Attachments);
        Attachments.Clear();
        var submitted = new MessageSubmittedEventArgs(text, images) { Owner = this };
        _cacheAnchor = _clock();
        if (EchoSubmittedMessages)
        {
            submitted.Item = new UserMessageItem(text, images) { Timestamp = _clock() };
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
        // the images go back to the reply box unless it already holds others
        if (Attachments.Count == 0)
        {
            foreach (var image in submitted.Images) Attachments.Add(image);
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
        // a top-level user line (message or tool result) is a request: the cache lifetime counts from it
        if (e.ParentToolUseId == null) _cacheAnchor = e.Timestamp ?? _clock();
        if (e.Block is ToolResultBlock result)
            OnToolResult(e, result);
        else if (e.Block is TextContentBlock text)
            OnUserText(e, text.Text, ImagesOf(e));
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
        if (text.Length == 0 && images == null) return;
        if (e.ParentToolUseId == null && _echoes.Count > 0 && _echoes[0].Text == text)
        {
            _echoes.RemoveAt(0); // the claude repeated what Submit already shows
            return;
        }
        Place(new UserMessageItem(text, images) { Timestamp = e.Timestamp }, e.ParentToolUseId);
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

        /// <summary>When the block started to arrive, by the conversation's clock.</summary>
        public DateTimeOffset StartedAt { get; set; }

        public long Elapsed(DateTimeOffset now) => Math.Max(0, (long)Math.Round((now - StartedAt).TotalMilliseconds));
    }
}
