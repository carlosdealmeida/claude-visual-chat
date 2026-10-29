using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// What the conversation tells a screen reader, as sentences for the <see cref="LiveAnnouncer"/>: Claude starts working, is ready, compacts,
/// runs start-up hooks, queues a message, asks for a permission, asks a question, waits for the review of a plan, fails, and the reply
/// of a finished turn. Only what happens live is told: what was in the conversation when the feed began to follow it, and anything
/// that arrives while it loads a history, is not (a reopened conversation announces nothing). It follows the conversation only while
/// the view is in the window.
/// </summary>
internal sealed class AnnouncementFeed
{
    private const string PlanTool = "ExitPlanMode";
    private const string QuestionTool = "AskUserQuestion";

    private readonly LiveAnnouncer _announcer;
    private readonly Func<ChatStrings> _strings;
    private ChatConversation? _tracked;
    private bool _busy;
    private bool _hooks;
    private string? _status;
    private PendingPermission? _permission;
    private string? _browserError;
    private string? _remoteError;

    private readonly Func<bool> _readOnly;

    /// <param name="announcer">Where the sentences go.</param>
    /// <param name="strings">The texts, as the view has them now.</param>
    /// <param name="readOnly">Whether the view is read-only now: a request for a permission, a plan or an answer is not told then, since the
    /// person cannot answer it there.</param>
    public AnnouncementFeed(LiveAnnouncer announcer, Func<ChatStrings> strings, Func<bool>? readOnly = null)
    {
        _announcer = announcer;
        _strings = strings;
        _readOnly = readOnly ?? (() => false);
    }

    /// <summary>Follows <paramref name="conversation"/>, or lets go of the one followed (null). What it holds now is not told.</summary>
    internal void Track(ChatConversation? conversation)
    {
        if (ReferenceEquals(conversation, _tracked)) return;
        if (_tracked != null)
        {
            _tracked.PropertyChanged -= OnChanged;
            _tracked.Items.CollectionChanged -= OnItemsChanged;
            _tracked.QueuedMessages.CollectionChanged -= OnQueueChanged;
        }
        _tracked = conversation;
        if (_tracked == null) return;
        _busy = _tracked.IsBusy;
        _hooks = _tracked.IsRunningStartupHooks;
        _status = _tracked.Status;
        _permission = _tracked.CurrentPermission;
        _browserError = _tracked.BrowserError;
        _remoteError = _tracked.RemoteControlError;
        _tracked.PropertyChanged += OnChanged;
        _tracked.Items.CollectionChanged += OnItemsChanged;
        _tracked.QueuedMessages.CollectionChanged += OnQueueChanged;
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        var c = _tracked;
        if (c == null) return;
        var live = !c.IsLoadingHistory;
        var strings = _strings();
        switch (e.PropertyName)
        {
            case nameof(ChatConversation.IsBusy):
                var busy = c.IsBusy;
                if (busy == _busy) break;
                _busy = busy;
                if (!live) break;
                if (busy)
                {
                    _announcer.Announce(strings.AnnounceWorking);
                }
                else
                {
                    if (FinalReply(c) is { } reply) _announcer.Announce(strings.FormatAnnounceReply(Speakable(reply, strings)));
                    _announcer.Announce(strings.AnnounceReady);
                }
                break;
            case nameof(ChatConversation.Status):
                var status = c.Status;
                var compacting = status == "compacting" && _status != "compacting";
                _status = status;
                if (compacting && live) _announcer.Announce(strings.AnnounceCompacting);
                break;
            case nameof(ChatConversation.IsRunningStartupHooks):
                var hooks = c.IsRunningStartupHooks;
                var started = hooks && !_hooks;
                _hooks = hooks;
                if (started && live) _announcer.Announce(strings.AnnounceStartupHooks);
                break;
            case nameof(ChatConversation.BrowserError):
                var browser = c.BrowserError;
                var browserFailed = !string.IsNullOrEmpty(browser) && browser != _browserError;
                _browserError = browser;
                if (browserFailed && live) _announcer.Announce(strings.FormatAnnounceError(browser!));
                break;
            case nameof(ChatConversation.RemoteControlError):
                var remote = c.RemoteControlError;
                var remoteFailed = !string.IsNullOrEmpty(remote) && remote != _remoteError;
                _remoteError = remote;
                if (remoteFailed && live) _announcer.Announce(strings.FormatAnnounceError(remote!));
                break;
            case nameof(ChatConversation.CurrentPermission):
                var request = c.CurrentPermission;
                var arrived = request != null && !ReferenceEquals(request, _permission);
                _permission = request;
                if (arrived && live && !_readOnly()) _announcer.Announce(Request(request!, strings));
                break;
        }
    }

    private static string Request(PendingPermission request, ChatStrings strings) =>
        request.ToolName == PlanTool ? strings.AnnouncePlan
        : request.ToolName == QuestionTool ? strings.AnnounceQuestion
        : strings.FormatAnnouncePermission(request.DisplayName);

    private void OnQueueChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && _tracked is { IsLoadingHistory: false })
            _announcer.Announce(_strings().MessageQueued);
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems == null || _tracked is not { IsLoadingHistory: false }) return;
        var strings = _strings();
        foreach (var item in e.NewItems)
        {
            // the error banners of the list: the message the claude gave, or the notice's own text when it gave none
            if (item is NoticeItem { Kind: NoticeKind.TurnFailed or NoticeKind.ApiRetry } notice)
                _announcer.Announce(strings.FormatAnnounceError(string.IsNullOrWhiteSpace(notice.Message) ? strings.Notice(notice.Kind) : notice.Message!));
        }
    }

    /// <summary>The last text Claude wrote in the turn that just ended (back to the message that began it); null when it wrote none.</summary>
    private static string? FinalReply(ChatConversation c)
    {
        for (var i = c.Items.Count - 1; i >= 0; i--)
        {
            var item = c.Items[i];
            if (item is UserMessageItem) return null;
            if (item is AssistantTextItem { Text.Length: > 0 } text) return text.Text;
        }
        return null;
    }

    /// <summary>
    /// The reply as it is read aloud: each fenced block of code (three backticks or three tildes) is said as "(code block, N lines)", its
    /// code left out; a fence never closed runs to the end. The rest is as written.
    /// </summary>
    internal static string Speakable(string markdown, ChatStrings strings)
    {
        var result = new StringBuilder(markdown.Length);
        var fence = (string?)null;
        var codeLines = 0;
        foreach (var line in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (fence == null)
            {
                if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
                {
                    fence = trimmed.Substring(0, 3);
                    codeLines = 0;
                    continue;
                }
                result.Append(line).Append('\n');
            }
            else if (trimmed.StartsWith(fence, StringComparison.Ordinal) && trimmed.TrimEnd('`', '~').Length == 0)
            {
                result.Append(strings.FormatAnnounceCodeBlock(codeLines)).Append('\n');
                fence = null;
            }
            else
            {
                codeLines++;
            }
        }
        if (fence != null) result.Append(strings.FormatAnnounceCodeBlock(codeLines)).Append('\n');
        return result.ToString().Trim();
    }
}
