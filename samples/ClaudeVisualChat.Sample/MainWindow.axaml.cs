using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ClaudeVisualChat.Protocol;

namespace ClaudeVisualChat.Sample;

/// <summary>One thing the view sent back, and the line a host would write to the claude's stdin for it.</summary>
public sealed record LogEntry(string Event, string Stdin);

/// <summary>
/// Plays the recordings into a <see cref="Controls.ClaudeChatView"/>: as they arrived (the stream, line by line,
/// stopping at each permission request until it is answered) or as Claude Code keeps them (the session file, at once).
/// The panel on the right shows each event of the conversation and what <see cref="StdinMessages"/> makes of it.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly TimeSpan PerStreamEvent = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan PerOtherLine = TimeSpan.FromMilliseconds(250);
    private static readonly PixelSize DesktopSize = new PixelSize(1280, 800);

    /// <summary>A phone's screen in device-independent pixels, which the touch layout is made for.</summary>
    private static readonly PixelSize PhoneSize = new PixelSize(412, 892);
    private static readonly string[] EffortLevels = { "low", "medium", "high", "xhigh", "max" };

    /// <summary>The models the sample offers, as the claude lists them in its <c>initialize</c> answer.</summary>
    private static readonly ChatModel[] Models =
    {
        new ChatModel("opus", "claude-opus-5-5", "Opus 5.5", "For complex work and everyday tasks", EffortLevels),
        new ChatModel("sonnet", "claude-sonnet-5-5", "Sonnet 5.5", "Most efficient for simpler tasks", EffortLevels),
        new ChatModel("haiku", "claude-haiku-4-5-20251001", "Haiku 4.5", "Fastest for quick answers", Array.Empty<string>()),
    };

    /// <summary>A made-up link: the sample opens nothing.</summary>
    private const string RemoteControlSession = "https://claude.ai/code/session_example";

    private readonly ObservableCollection<LogEntry> _log = new ObservableCollection<LogEntry>();
    private readonly DispatcherTimer _statusTimer;
    private readonly PixelSize _screenshotSize;
    private SampleTexts _texts = SampleTexts.English;
    private ChatConversation? _conversation;
    private FixturePlayer? _player;
    private int _lineCount;
    private bool _finished;
    private bool _answerYes;
    private CancellationTokenSource? _playing;
    private int _requestIds;

    public MainWindow() : this(App.Options)
    {
    }

    internal MainWindow(SampleOptions options)
    {
        InitializeComponent();
        _screenshotSize = options.Touch ? PhoneSize : DesktopSize;
        if (options.Touch) ShowAsPhone();
        Log.ItemsSource = _log;
        ScenarioPicker.ItemsSource = Recordings.Scenarios;
        ScenarioPicker.SelectedItem = options.Scenario;
        LanguagePicker.ItemsSource = new[] { "English", "Português" };
        LanguagePicker.SelectedIndex = options.Portuguese ? 1 : 0;
        LanguagePicker.SelectionChanged += (_, _) => ApplyLanguage();
        ApplyLanguage();
        SourcePicker.SelectedIndex = 0;
        _statusTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => UpdateStatus());
        if (options.Screenshot == null) Opened += (_, _) => _ = PlayAsync(PerStreamEvent, PerOtherLine);
    }

    /// <summary>
    /// <c>--touch</c>: the window is a phone's screen with the view alone in it, in the touch layout. The sample's bar and the log have
    /// no room next to it, so the recording and its language are the ones of the command line (<c>--scenario</c>, <c>--lang</c>), and
    /// it plays when the window opens.
    /// </summary>
    private void ShowAsPhone()
    {
        Title += " (touch)";
        MinWidth = PhoneSize.Width;
        MinHeight = 480;
        Width = PhoneSize.Width;
        Height = PhoneSize.Height;
        SampleBar.IsVisible = false;
        LogSeparator.IsVisible = false;
        LogPanel.IsVisible = false;
        Body.ColumnDefinitions = new ColumnDefinitions("*");
        Chat.Layout = ChatLayout.Touch;
    }

    // ---- the bar ----

    private void OnPlay(object? sender, RoutedEventArgs e) => _ = PlayAsync(PerStreamEvent, PerOtherLine);

    private void OnRestart(object? sender, RoutedEventArgs e) => _ = PlayAsync(PerStreamEvent, PerOtherLine);

    private void ApplyLanguage()
    {
        var portuguese = LanguagePicker.SelectedIndex == 1;
        _texts = portuguese ? SampleTexts.Portuguese : SampleTexts.English;
        Chat.Strings = portuguese ? ChatStrings.PortugueseBrazil : ChatStrings.English;
        RecordingLabel.Text = _texts.Recording;
        var source = Math.Max(0, SourcePicker.SelectedIndex);
        SourcePicker.ItemsSource = new[] { _texts.LiveStream, _texts.History };
        SourcePicker.SelectedIndex = source;
        PlayButton.Content = _texts.Play;
        RestartButton.Content = _texts.Restart;
        LogTitle.Text = _texts.LogTitle;
        LogHint.Text = _texts.LogHint;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_conversation == null) StatusText.Text = "";
        else if (_player == null) StatusText.Text = _texts.HistoryLoaded;
        else if (_finished) StatusText.Text = string.Format(_texts.Done, _lineCount);
        else if (_conversation.CurrentPermission != null) StatusText.Text = _texts.Waiting;
        else StatusText.Text = string.Format(_texts.LineOf, _player.Position, _lineCount);
    }

    // ---- playing ----

    /// <summary>Plays the chosen recording from the start, in a new conversation; whatever was playing stops.</summary>
    private async Task PlayAsync(TimeSpan perStreamEvent, TimeSpan perOtherLine)
    {
        _playing?.Cancel();
        var playing = _playing = new CancellationTokenSource();
        var scenario = (string?)ScenarioPicker.SelectedItem ?? Recordings.Scenarios[0];
        var conversation = Start();
        // the "Switched to" band gives way to the browser's banner (one at a time), so the tour sessions have the browser off
        if (TourSession.Is(scenario)) conversation.BrowserStatus = ChatBrowserStatus.Off;
        _player = null;
        _finished = false;
        PlayButton.IsEnabled = true;
        _statusTimer.Stop();

        if (SourcePicker.SelectedIndex == 1)
        {
            // the session file has no init to say where Claude worked: the recordings' folder, so paths show relative
            conversation.WorkingDirectory = Recordings.WorkingDirectory;
            conversation.LoadTranscript(Recordings.Transcript(scenario));
            if (scenario == DemoSession.Scenario) AttachDemoFile(conversation);
            if (scenario == TourSession.Tour) BookmarkTourReply(conversation);
            UpdateStatus();
            return;
        }

        var lines = Recordings.Stream(scenario);
        var player = _player = new FixturePlayer(lines, conversation) { Prompts = Recordings.Prompts(scenario) };
        // the times and the thought durations are the recording's, not the playing's; once it has played, the time
        // goes on from its end (the agents still at work count their minutes), except in a screenshot
        DateTimeOffset? endedAt = null;
        conversation.Clock = () => player.RecordingTime is { } time
            ? endedAt is { } ended ? time + (DateTimeOffset.Now - ended) : time
            : DateTimeOffset.Now;
        _lineCount = lines.Count;
        PlayButton.IsEnabled = false;
        _statusTimer.Start();
        try
        {
            await player.PlayAsync(perStreamEvent, perOtherLine, playing.Token);
            _finished = true;
            if (!_answerYes) endedAt = DateTimeOffset.Now;
            if (scenario == DemoSession.Scenario) AttachDemoFile(conversation);
            if (scenario == TourSession.Tour) BookmarkTourReply(conversation);
        }
        catch (OperationCanceledException) when (playing.IsCancellationRequested)
        {
            // a newer play took over
        }
        finally
        {
            if (ReferenceEquals(_playing, playing))
            {
                PlayButton.IsEnabled = true;
                _statusTimer.Stop();
                UpdateStatus();
            }
        }
    }

    /// <summary>A new conversation in the view, its events going to the log.</summary>
    private ChatConversation Start()
    {
        if (_conversation is { } old)
        {
            old.MessageSubmitted -= OnMessageSubmitted;
            old.PermissionAnswered -= OnPermissionAnswered;
            old.PermissionModeRequested -= OnPermissionModeRequested;
            old.ModelRequested -= OnModelRequested;
            old.EffortRequested -= OnEffortRequested;
            old.CommandInvoked -= OnCommandInvoked;
            old.BrowserDisconnectRequested -= OnBrowserDisconnectRequested;
            old.RemoteControlLinkRequested -= OnRemoteControlLinkRequested;
            old.SubmitUndone -= OnSubmitUndone;
            old.LinkRequested -= OnLinkRequested;
            old.FeedbackRequested -= OnFeedbackRequested;
            old.StopRequested -= OnStopRequested;
            old.PropertyChanged -= OnConversationChanged;
        }
        _log.Clear();
        _requestIds = 0;
        var conversation = new ChatConversation
        {
            AvailableModels = Models,
            // the host tells the state of the browser and of the Remote Control; here both are on, so the bar shows whole
            BrowserStatus = ChatBrowserStatus.Connected,
            RemoteControlStatus = ChatRemoteControlStatus.Connected,
            RemoteControlUrl = RemoteControlSession,
        };
        conversation.MessageSubmitted += OnMessageSubmitted;
        conversation.PermissionAnswered += OnPermissionAnswered;
        conversation.PermissionModeRequested += OnPermissionModeRequested;
        conversation.ModelRequested += OnModelRequested;
        conversation.EffortRequested += OnEffortRequested;
        conversation.CommandInvoked += OnCommandInvoked;
        conversation.BrowserDisconnectRequested += OnBrowserDisconnectRequested;
        conversation.RemoteControlLinkRequested += OnRemoteControlLinkRequested;
        // the host listens to these, so the view shows the links and the thumbs that need it
        conversation.SubmitUndone += OnSubmitUndone;
        conversation.LinkRequested += OnLinkRequested;
        conversation.FeedbackRequested += OnFeedbackRequested;
        conversation.StopRequested += OnStopRequested;
        conversation.PropertyChanged += OnConversationChanged;
        _conversation = conversation;
        Chat.Conversation = conversation;
        return conversation;
    }

    private void OnConversationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ChatConversation.CurrentPermission) || sender is not ChatConversation conversation) return;
        UpdateStatus();
        if (!_answerYes || conversation.CurrentPermission == null) return;
        // the screenshot's "Yes", after the queue has finished changing
        Dispatcher.UIThread.Post(() =>
        {
            if (conversation.CurrentPermission is { } request) conversation.AnswerPermission(request, PermissionDecision.Allow);
        });
    }

    // ---- what the view sends back, and what the host would write to the claude ----

    private void OnMessageSubmitted(object? sender, MessageSubmittedEventArgs e)
    {
        var what = "MessageSubmitted: \"" + e.Text + "\"" + (e.Images.Count > 0 ? " + " + e.Images.Count + " image(s)" : "")
                   + (e.Files.Count > 0 ? " + " + e.Files.Count + " file(s)" : "");
        if (e.Files.Count == 0)
        {
            Add(what, StdinMessages.UserMessage(e)); // with its uuid: the claude tells when it takes a message sent while it works
            return;
        }
        // the files go to the claude's machine first; the message follows, naming each one on a line of its own
        Add(what, "(the app sends the files first; the message follows when they are there)");
        if (!_answerYes && sender is ChatConversation conversation) _ = UploadAsync(conversation, e);
        else foreach (var file in e.Files) file.Progress = 0.42; // the screenshot shows a file half sent
    }

    /// <summary>
    /// What a host does with a message's files, made up: it sends each one (here it only waits), telling the progress
    /// on the UI thread, sets where the file landed, then writes the message with one marker line per file.
    /// </summary>
    private async Task UploadAsync(ChatConversation conversation, MessageSubmittedEventArgs e)
    {
        foreach (var file in e.Files)
        {
            for (var step = 0; step <= 20; step++)
            {
                if (!ReferenceEquals(_conversation, conversation)) return; // another play took over
                file.Progress = step / 20.0;
                await Task.Delay(150);
            }
            file.RemotePath = Recordings.WorkingDirectory + @"\.uploads\" + file.Name;
        }
        // the text, one marker line per file where it landed, and the message's uuid
        Add("Files sent: " + string.Join(", ", e.Files.Select(f => f.RemotePath)), StdinMessages.UserMessage(e));
    }

    /// <summary>The made-up session leaves a file in the reply box, for the user to send.</summary>
    private static void AttachDemoFile(ChatConversation conversation)
    {
        if (conversation.Files.Count == 0) conversation.AddFile(DemoSession.FileName, DemoSession.FileContent);
    }

    /// <summary>The tour's last reply is bookmarked, as the user would with the button under it, so the bookmark shows.</summary>
    private static void BookmarkTourReply(ChatConversation conversation)
    {
        foreach (var item in conversation.Items.OfType<AssistantTextItem>())
        {
            if (item.Uuid == TourSession.BookmarkedReply) item.IsBookmarked = true;
        }
    }

    private void OnPermissionAnswered(object? sender, PermissionAnsweredEventArgs e) =>
        Add("PermissionAnswered: " + e.Decision + " · " + e.Request.DisplayName, StdinMessages.PermissionResponse(e));

    private void OnPermissionModeRequested(object? sender, PermissionModeRequestedEventArgs e)
    {
        var id = NextRequestId();
        Add("PermissionModeRequested: " + e.Mode, StdinMessages.SetPermissionMode(id, e.Mode));
        // the claude confirms with the mode it is now in; the sample answers for it, so the mode chip moves
        if (sender is ChatConversation conversation)
            Dispatcher.UIThread.Post(() => conversation.AppendLine(ModeConfirmation(id, e.Mode)));
    }

    private void OnModelRequested(object? sender, ModelRequestedEventArgs e) =>
        Add("ModelRequested: " + e.Model, StdinMessages.SetModel(NextRequestId(), e.Model));

    private void OnEffortRequested(object? sender, EffortRequestedEventArgs e)
    {
        Add("EffortRequested: " + (e.Effort ?? "(model's own)"), StdinMessages.ApplyEffort(NextRequestId(), e.Effort));
        // the claude reports the level it applied; the sample answers for it, so the chip moves
        if (sender is ChatConversation conversation)
            Dispatcher.UIThread.Post(() => conversation.Apply(new SettingsApplied(null, e.Effort, true)));
    }

    private void OnCommandInvoked(object? sender, CommandInvokedEventArgs e)
    {
        var conversation = sender as ChatConversation;
        var what = "CommandInvoked: /" + e.Name + (e.Arguments.Length > 0 ? " " + e.Arguments : "");
        switch (e.Name)
        {
            case "context":
                Add(what, StdinMessages.GetContextUsage(NextRequestId()));
                // the claude answers with the numbers; the sample makes some up, so the panel has something to draw
                conversation?.Apply(new ContextUsageReported(new ContextUsage(48_000, 200_000, 24, new[]
                {
                    new ContextCategory("System prompt", 6_000, "used"),
                    new ContextCategory("Messages", 42_000, "used"),
                    new ContextCategory("Free space", 152_000, "free"),
                })));
                break;
            case "usage":
                Add(what, "(no stdin line: the limits come in the rate_limit_event lines; the sample makes some up)");
                conversation?.Apply(new UsageLimitsReported(new UsageLimits(new[]
                {
                    new UsageWindow("five_hour", 0.32, DateTimeOffset.Now.AddHours(3)),
                    new UsageWindow("seven_day", 0.58, DateTimeOffset.Now.AddDays(3)),
                })));
                break;
            case "chrome":
                Add(what, StdinMessages.GetChromeStatus(NextRequestId()));
                break;
            case "remote-control":
            case "rc":
                var turnOn = conversation == null || conversation.RemoteControlStatus == ChatRemoteControlStatus.Off;
                Add(what, StdinMessages.RemoteControl(NextRequestId(), turnOn));
                if (conversation != null)
                {
                    conversation.RemoteControlStatus = turnOn ? ChatRemoteControlStatus.Connected : ChatRemoteControlStatus.Off;
                    conversation.RemoteControlUrl = turnOn ? RemoteControlSession : null;
                }
                break;
            default:
                Add(what, "(no stdin line)");
                break;
        }
    }

    private void OnBrowserDisconnectRequested(object? sender, EventArgs e)
    {
        Add("BrowserDisconnectRequested", "(no stdin line: the host turns its browser extension off)");
        if (sender is ChatConversation conversation) conversation.BrowserStatus = ChatBrowserStatus.Off;
    }

    private void OnRemoteControlLinkRequested(object? sender, RemoteControlLinkRequestedEventArgs e) =>
        Add("RemoteControlLinkRequested: " + e.Url, "(no stdin line: the host opens the link, the sample does not)");

    private void OnSubmitUndone(object? sender, MessageSubmittedEventArgs e) =>
        Add("SubmitUndone: \"" + e.Text + "\"", "(no stdin line: the host takes the message back if the claude has not started on it)");

    private void OnLinkRequested(object? sender, LinkRequestedEventArgs e) =>
        Add("LinkRequested: " + e.Url, "(no stdin line: the host opens the link, the sample does not)");

    private void OnFeedbackRequested(object? sender, FeedbackRequestedEventArgs e) =>
        Add("FeedbackRequested: " + (e.Positive switch { true => "thumbs up", false => "thumbs down", _ => "rating taken back" })
            + (string.IsNullOrEmpty(e.Details) ? "" : " · \"" + e.Details + "\"") + " · " + e.Item.Uuid,
            "(no stdin line: the host sends the rating to Anthropic, the sample sends nothing)");

    private void OnStopRequested(object? sender, EventArgs e)
    {
        Add("StopRequested", StdinMessages.Interrupt(NextRequestId()));
        // on an interrupt the claude cancels the requests it waits on; the recording goes on from there
        if (sender is ChatConversation conversation)
        {
            Dispatcher.UIThread.Post(() =>
            {
                foreach (var request in conversation.PendingPermissions.ToList()) conversation.DismissPermission(request.RequestId);
            });
        }
    }

    private string NextRequestId() => "sample_" + ++_requestIds;

    private static string ModeConfirmation(string requestId, string mode) => JsonSerializer.Serialize(new
    {
        type = "control_response",
        response = new { subtype = "success", request_id = requestId, response = new { mode } },
    });

    private void Add(string what, string stdin)
    {
        _log.Add(new LogEntry(what, stdin));
        Dispatcher.UIThread.Post(() => LogScroller.ScrollToEnd(), DispatcherPriority.Background);
    }

    // ---- the screenshot ----

    /// <summary>
    /// Plays the chosen recording to the end at once, saying yes to every request (or loads its session file), and
    /// saves the window as a PNG (1280×800, or 412×892 with <c>--touch</c>). Used by <c>--screenshot</c>; nothing is clicked.
    /// </summary>
    internal async Task SaveScreenshotAsync(string path, bool history)
    {
        _answerYes = true;
        SourcePicker.SelectedIndex = history ? 1 : 0;
        await PlayAsync(TimeSpan.Zero, TimeSpan.Zero);
        if (!history && !_finished) throw new InvalidOperationException("the recording did not play to the end");
        if (ScenarioPicker.SelectedItem as string == DemoSession.Scenario && _conversation is { } demo)
        {
            // the made-up session: its file sent (half way) with a message, and the Agent map open
            demo.Submit(DemoSession.FileMessage);
            demo.ShowAgents();
        }

        // the last texts are drawn (markdown is redrawn at most every 100 ms) and the list reaches its end
        await Task.Delay(TimeSpan.FromMilliseconds(800));
        if (ClientSize != _screenshotSize.ToSize(1))
            throw new InvalidOperationException("the window is " + ClientSize + ", not " + _screenshotSize);

        var file = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using var bitmap = new RenderTargetBitmap(_screenshotSize, new Vector(96, 96));
        bitmap.Render(this);
        bitmap.Save(file);
    }
}

/// <summary>The sample's own texts (the view's come from <see cref="ChatStrings"/>).</summary>
internal sealed class SampleTexts
{
    public static readonly SampleTexts English = new SampleTexts();

    public static readonly SampleTexts Portuguese = new SampleTexts
    {
        Recording = "Gravação",
        LiveStream = "Stream ao vivo",
        History = "Histórico",
        Play = "Tocar",
        Restart = "Recomeçar",
        LogTitle = "Eventos → stdin",
        LogHint = "O que a view devolveu e a linha que o app mandaria ao stdin do claude.",
        LineOf = "Linha {0} de {1}",
        Waiting = "Esperando a sua resposta",
        Done = "Fim: {0} linhas",
        HistoryLoaded = "Histórico carregado",
    };

    public string Recording { get; init; } = "Recording";

    public string LiveStream { get; init; } = "Live stream";

    public string History { get; init; } = "History";

    public string Play { get; init; } = "Play";

    public string Restart { get; init; } = "Restart";

    public string LogTitle { get; init; } = "Events → stdin";

    public string LogHint { get; init; } = "What the view sent back, and the line the app would write to the claude's stdin.";

    public string LineOf { get; init; } = "Line {0} of {1}";

    public string Waiting { get; init; } = "Waiting for your answer";

    public string Done { get; init; } = "Done: {0} lines";

    public string HistoryLoaded { get; init; } = "Session file loaded";
}
