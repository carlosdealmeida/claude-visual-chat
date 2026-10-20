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
    private static readonly PixelSize ScreenshotSize = new PixelSize(1280, 800);

    private readonly ObservableCollection<LogEntry> _log = new ObservableCollection<LogEntry>();
    private readonly DispatcherTimer _statusTimer;
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
        Log.ItemsSource = _log;
        Chat.AvailableModels = new[] { "opus", "sonnet", "haiku" };
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
        _player = null;
        _finished = false;
        PlayButton.IsEnabled = true;
        _statusTimer.Stop();

        if (SourcePicker.SelectedIndex == 1)
        {
            // the session file has no init to say where Claude worked: the recordings' folder, so paths show relative
            conversation.WorkingDirectory = Recordings.WorkingDirectory;
            conversation.LoadTranscript(Recordings.Transcript(scenario));
            UpdateStatus();
            return;
        }

        var lines = Recordings.Stream(scenario);
        var player = _player = new FixturePlayer(lines, conversation) { Prompts = Recordings.Prompts(scenario) };
        // the times and the thought durations are the recording's, not the playing's
        conversation.Clock = () => player.RecordingTime ?? DateTimeOffset.Now;
        _lineCount = lines.Count;
        PlayButton.IsEnabled = false;
        _statusTimer.Start();
        try
        {
            await player.PlayAsync(perStreamEvent, perOtherLine, playing.Token);
            _finished = true;
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
            old.StopRequested -= OnStopRequested;
            old.PropertyChanged -= OnConversationChanged;
        }
        _log.Clear();
        _requestIds = 0;
        var conversation = new ChatConversation();
        conversation.MessageSubmitted += OnMessageSubmitted;
        conversation.PermissionAnswered += OnPermissionAnswered;
        conversation.PermissionModeRequested += OnPermissionModeRequested;
        conversation.ModelRequested += OnModelRequested;
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

    private void OnMessageSubmitted(object? sender, MessageSubmittedEventArgs e) =>
        Add("MessageSubmitted: \"" + e.Text + "\"", StdinMessages.UserMessage(e.Text));

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
    /// saves the 1280×800 window as a PNG. Used by <c>--screenshot</c>; nothing is clicked.
    /// </summary>
    internal async Task SaveScreenshotAsync(string path, bool history)
    {
        _answerYes = true;
        SourcePicker.SelectedIndex = history ? 1 : 0;
        await PlayAsync(TimeSpan.Zero, TimeSpan.Zero);
        if (!history && !_finished) throw new InvalidOperationException("the recording did not play to the end");

        // the last texts are drawn (markdown is redrawn at most every 100 ms) and the list reaches its end
        await Task.Delay(TimeSpan.FromMilliseconds(800));
        if (ClientSize != ScreenshotSize.ToSize(1))
            throw new InvalidOperationException("the window is " + ClientSize + ", not " + ScreenshotSize);

        var file = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using var bitmap = new RenderTargetBitmap(ScreenshotSize, new Vector(96, 96));
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
