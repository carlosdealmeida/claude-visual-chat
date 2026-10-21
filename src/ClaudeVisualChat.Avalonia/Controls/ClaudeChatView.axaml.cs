using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// Draws a <see cref="ChatConversation"/> in the style of the Claude Code extension for VS Code. The list sticks to
/// its end while the user is there and stops following when the user scrolls up. Under the list, the permission
/// request Claude waits on (keys 1, 2 and 3 answer while the focus is in it) and the reply box (Enter sends,
/// Shift+Enter breaks the line, Esc stops Claude while it works). What the user does comes back as the
/// conversation's events. A key is marked handled only when the view acts on it, so the app's own shortcuts keep
/// working. Colors and fonts come from the <c>ClaudeChat.*</c> resources, which can be redefined in
/// <see cref="StyledElement.Resources"/>; the default colors are those of VS Code's "Dark 2026" theme, and
/// <c>avares://ClaudeVisualChat.Avalonia/Themes/DarkModern.axaml</c>, merged into the view's resources, gives the
/// "Dark Modern" ones. The app brings its own theme (Fluent or Simple) for the standard controls.
/// </summary>
public partial class ClaudeChatView : UserControl
{
    // before the properties: static fields are set in the order they are written
    private static readonly IReadOnlyList<string> DefaultModes =
        new ReadOnlyCollection<string>(new[] { "plan", "default", "acceptEdits", "auto" });

    public static readonly StyledProperty<ChatConversation?> ConversationProperty =
        AvaloniaProperty.Register<ClaudeChatView, ChatConversation?>(nameof(Conversation));

    /// <summary>The texts; inherited by everything inside the view.</summary>
    public static readonly StyledProperty<ChatStrings> StringsProperty =
        AvaloniaProperty.Register<ClaudeChatView, ChatStrings>(
            nameof(Strings), ChatStrings.English, inherits: true, coerce: (_, value) => value ?? ChatStrings.English);

    public static readonly StyledProperty<IReadOnlyList<string>> AvailableModesProperty =
        AvaloniaProperty.Register<ClaudeChatView, IReadOnlyList<string>>(nameof(AvailableModes), DefaultModes);

    public static readonly StyledProperty<IReadOnlyList<string>?> AvailableModelsProperty =
        AvaloniaProperty.Register<ClaudeChatView, IReadOnlyList<string>?>(nameof(AvailableModels));

    /// <summary>
    /// True to only show the conversation: the reply box and the permission request are hidden, while the list keeps
    /// drawing, scrolling and letting the user select text. Pending requests stay in the conversation, and the
    /// reply box keeps its draft. False (the default) shows everything.
    /// </summary>
    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        AvaloniaProperty.Register<ClaudeChatView, bool>(nameof(IsReadOnly));

    /// <summary>True while the conversation is busy; inherited, so a running tool's dot blinks only then.</summary>
    internal static readonly StyledProperty<bool> IsWorkingProperty =
        AvaloniaProperty.Register<ClaudeChatView, bool>("IsWorking", inherits: true);

    /// <summary>How close to the end (in pixels) still counts as being at the end.</summary>
    private const double EndTolerance = 4;

    /// <summary>True while the list follows new items; false once the user scrolls up.</summary>
    private bool _stickToEnd = true;

    private bool _scrollQueued;

    private ChatConversation? _tracked;
    private bool _attached;

    /// <summary>The list's vertical scroll bar, from the scroll viewer's template (the theme's).</summary>
    private ScrollBar? _verticalBar;

    public ClaudeChatView()
    {
        InitializeComponent();
        Scroller.ScrollChanged += OnScrollChanged;
        Scroller.TemplateApplied += (_, e) => _verticalBar = e.NameScope.Find<ScrollBar>("PART_VerticalScrollBar");
        Composer.Modes = AvailableModes;
        Composer.Models = AvailableModels;
        Prompt.RequestChanged += OnPromptRequestChanged;
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    public ChatConversation? Conversation
    {
        get => GetValue(ConversationProperty);
        set => SetValue(ConversationProperty, value);
    }

    public ChatStrings Strings
    {
        get => GetValue(StringsProperty);
        set => SetValue(StringsProperty, value);
    }

    /// <summary>Permission modes offered to the user. <c>plan, default, acceptEdits, auto</c> by default.</summary>
    public IReadOnlyList<string> AvailableModes
    {
        get => GetValue(AvailableModesProperty);
        set => SetValue(AvailableModesProperty, value);
    }

    /// <summary>Models offered to the user; null (the default) hides the model picker.</summary>
    public IReadOnlyList<string>? AvailableModels
    {
        get => GetValue(AvailableModelsProperty);
        set => SetValue(AvailableModelsProperty, value);
    }

    /// <summary>Shows the conversation only: no reply box, no permission request. False by default.</summary>
    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ConversationProperty)
        {
            ItemList.ItemsSource = Conversation?.Items;
            _stickToEnd = true;
            QueueScrollToEnd();
            Composer.Conversation = Conversation; // before the prompt, which asks the reply box about focus
            Prompt.Conversation = Conversation;
            if (_attached) Track(Conversation);
        }
        else if (change.Property == IsReadOnlyProperty && !IsReadOnly)
        {
            Prompt.RestartPause(); // a request that came back must not be answered by a stray key
        }
        else if (change.Property == StringsProperty)
        {
            WorkingText.Text = Strings.Working;
        }
        else if (change.Property == AvailableModesProperty)
        {
            Composer.Modes = AvailableModes;
        }
        else if (change.Property == AvailableModelsProperty)
        {
            Composer.Models = AvailableModels;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Track(Conversation);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        Track(null); // the conversation outlives the view: let go of its events
    }

    private void Track(ChatConversation? conversation)
    {
        if (!ReferenceEquals(conversation, _tracked))
        {
            if (_tracked != null) _tracked.PropertyChanged -= OnConversationChanged;
            _tracked = conversation;
            if (_tracked != null) _tracked.PropertyChanged += OnConversationChanged;
        }
        UpdateWorking();
    }

    private void OnConversationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatConversation.IsBusy)
            || e.PropertyName == nameof(ChatConversation.IsStreaming)
            || e.PropertyName == nameof(ChatConversation.PermissionMode))
            UpdateWorking();
    }

    /// <summary>
    /// While Claude works, running tools blink; and while nothing is arriving either (no text or thought in pieces),
    /// "Working…" shows at the end of the list, its dot in the color of the mode, like the extension's line.
    /// </summary>
    private void UpdateWorking()
    {
        var conversation = _tracked;
        var busy = conversation != null && conversation.IsBusy;
        SetValue(IsWorkingProperty, busy);
        WorkingText.Text = Strings.Working;
        var on = busy && !conversation!.IsStreaming;
        WorkingRow.IsVisible = on;
        WorkingRow.Classes.Set("on", on); // the dot's blinking, only while it shows
        var mode = conversation?.PermissionMode;
        WorkingRow.Classes.Set("plan", mode == "plan");
        WorkingRow.Classes.Set("acceptEdits", mode == "acceptEdits");
        WorkingRow.Classes.Set("auto", mode == "auto" || mode == "bypassPermissions");
    }

    /// <summary>
    /// A new request takes the keyboard only when the reply box is empty and the focus is in the view or nowhere, so
    /// a number typed in the reply box, or elsewhere in the app, never answers it. While the prompt has the
    /// keyboard, the next request keeps it; when none is left, the reply box gets it back.
    /// </summary>
    private void OnPromptRequestChanged(bool hadFocus)
    {
        if (Prompt.Request != null)
        {
            if (hadFocus || (Composer.IsEmpty && FocusIsHereOrNowhere())) Prompt.FocusFirstOption();
        }
        else if (hadFocus)
        {
            Composer.FocusInput();
        }
    }

    /// <summary>
    /// The keyboard repeating a key held down in the prompt goes nowhere: it would answer the next request, or type
    /// into the reply box once none is left. Taken (handled) only in that case.
    /// </summary>
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (Prompt.IsHeld(e.Key)) e.Handled = true;
    }

    private bool FocusIsHereOrNowhere()
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        return focused == null || focused is TopLevel || IsKeyboardFocusWithin;
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        // the event bubbles: the code blocks of the markdown have scroll viewers of their own
        if (!ReferenceEquals(e.Source, Scroller)) return;

        UpdateGutter();
        var atEnd = DistanceToEnd() <= EndTolerance;
        // When the list gets taller (the request under it goes, the window grows), the scroll viewer brings the offset
        // down to keep it in range, while new items may arrive in the same pass: that is not the user going up. (A
        // scroll up smaller than that growth, in that very pass, is taken for it too; the user's scrolling comes from
        // input, between passes, so this is rare.)
        var keptInRange = e.ViewportDelta.Y > 0 && -e.OffsetDelta.Y <= e.ViewportDelta.Y + 0.5;
        if (e.OffsetDelta.Y < 0 && !atEnd && !keptInRange) _stickToEnd = false; // the user went up to read
        else if (atEnd) _stickToEnd = true;                                      // or came back to the end

        if (_stickToEnd && !atEnd) QueueScrollToEnd(); // new items, or a better estimate of the ones above
    }

    private double DistanceToEnd() => Scroller.Extent.Height - Scroller.Viewport.Height - Scroller.Offset.Y;

    /// <summary>
    /// While the list scrolls under a scroll bar drawn over it (Fluent's, which hides itself: <c>AllowAutoHide</c>),
    /// its right side gives the bar its room (<c>ClaudeChat.ScrollBarGutter</c>), as the extension's scroll bar takes
    /// it; so the bar never covers the text. A bar with a column of its own beside the list (Simple's) already has its
    /// room, and the list keeps all of its width. Narrower, the list only gets taller, and wider only shorter, so it
    /// settles.
    /// </summary>
    private void UpdateGutter()
    {
        var scrolls = Scroller.Extent.Height > Scroller.Viewport.Height + 0.5;
        var gutter = scrolls && ScrollBarCoversList()
            && this.TryFindResource("ClaudeChat.ScrollBarGutter", out var value) && value is double width ? width : 0;
        if (ItemList.Margin.Right != gutter) ItemList.Margin = new Thickness(0, 0, gutter, 0);
    }

    /// <summary>
    /// True when the vertical scroll bar is drawn over the list's area rather than beside it. Read from where the
    /// theme's template puts the two, since the property that asks for it (<c>AllowAutoHide</c>) does not tell
    /// whether the template honors it.
    /// </summary>
    private bool ScrollBarCoversList()
    {
        var bar = _verticalBar;
        var content = Scroller.Presenter;
        if (bar == null || content == null || !bar.IsVisible) return false;
        var barLeft = bar.TranslatePoint(default, Scroller);
        var contentRight = content.TranslatePoint(new Point(content.Bounds.Width, 0), Scroller);
        return barLeft != null && contentRight != null && contentRight.Value.X > barLeft.Value.X + 0.5;
    }

    /// <summary>
    /// Moves to the end after the current layout pass (not inside it, where ScrollChanged is raised), once, however
    /// many times it is asked. The list estimates the height of the items it has not drawn, so reaching the real end
    /// can take a few rounds; each one runs as its own dispatcher job.
    /// </summary>
    private void QueueScrollToEnd()
    {
        if (_scrollQueued) return;
        _scrollQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _scrollQueued = false;
            if (!_stickToEnd || DistanceToEnd() <= EndTolerance) return;
            var end = Math.Max(0, Scroller.Extent.Height - Scroller.Viewport.Height);
            Scroller.SetCurrentValue(ScrollViewer.OffsetProperty, new Vector(Scroller.Offset.X, end));
        });
    }
}
