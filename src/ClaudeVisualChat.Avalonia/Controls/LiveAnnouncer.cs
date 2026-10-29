using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Threading;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The view's live region for a screen reader: a text with no size (shown, so it stays in the automation tree) whose name is the
/// sentence last announced, with <see cref="AutomationLiveSetting.Polite"/> so the reader says it when it is free. Sentences wait in a
/// queue of five at most (when a sixth comes, the oldest is dropped), one is said every 400 ms, and none while a modal is over the view
/// (<see cref="IsPaused"/>). The timer follows the rule of the window: it runs only while something waits and the view is in the
/// window, and leaving it empties the queue (what it held is stale by the time the view returns).
/// </summary>
internal sealed class LiveAnnouncer : Control
{
    /// <summary>Most sentences waiting at once; when a sentence comes while this many wait, the oldest is dropped.</summary>
    internal const int MaxQueued = 5;

    /// <summary>The time between two sentences.</summary>
    internal static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(400);

    private readonly Queue<string> _queue = new Queue<string>();
    private readonly DispatcherTimer _timer;
    private bool _attached;

    public LiveAnnouncer()
    {
        Width = 0;
        Height = 0;
        IsHitTestVisible = false;
        Focusable = false;
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
        _timer = new DispatcherTimer { Interval = Interval };
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>Whether a modal is over the view: while it is, nothing is said and the queue holds. Asked at every tick.</summary>
    internal Func<bool>? IsPaused { get; set; }

    /// <summary>The sentences waiting.</summary>
    internal int Pending => _queue.Count;

    /// <summary>The sentence announced last; empty before the first.</summary>
    internal string Spoken { get; private set; } = "";

    /// <summary>True while the timer runs: only with something waiting and the view in the window.</summary>
    internal bool IsTicking => _timer.IsEnabled;

    /// <summary>The sentences waiting, in order.</summary>
    internal IReadOnlyCollection<string> Waiting => _queue;

    /// <summary>Adds a sentence to the queue, dropping the oldest when five wait; nothing is queued out of the window or for a blank text.</summary>
    internal void Announce(string text)
    {
        if (!_attached || string.IsNullOrWhiteSpace(text)) return;
        if (_queue.Count >= MaxQueued) _queue.Dequeue(); // the oldest goes, so the newest state (a request, a question, a plan) is read
        _queue.Enqueue(text);
        if (!_timer.IsEnabled) _timer.Start();
    }

    /// <summary>One step of the clock: the next sentence is said, unless a modal is over the view.</summary>
    internal void Tick()
    {
        if (!_attached || _queue.Count == 0)
        {
            _timer.Stop();
            return;
        }
        if (IsPaused?.Invoke() == true) return; // the queue holds; the timer keeps going to see when the modal closes
        Say(_queue.Dequeue());
        if (_queue.Count == 0) _timer.Stop();
    }

    private void Say(string text)
    {
        Spoken = text;
        // a sentence said twice in a row is a change too: the name goes and comes back
        AutomationProperties.SetName(this, null);
        AutomationProperties.SetName(this, text);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        _timer.Stop();
        _queue.Clear();
        Spoken = "";
        AutomationProperties.SetName(this, null);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new LiveAnnouncerPeer(this);

    /// <summary>The peer: a text that is part of the tree, named by the sentence.</summary>
    private sealed class LiveAnnouncerPeer : ControlAutomationPeer
    {
        public LiveAnnouncerPeer(Control owner) : base(owner)
        {
        }

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

        protected override bool IsContentElementCore() => true;

        protected override bool IsControlElementCore() => true;

        protected override string? GetNameCore() => AutomationProperties.GetName(Owner);
    }
}
