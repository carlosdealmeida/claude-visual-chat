using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// A panel of information over the end of the list, which <c>/context</c> and <c>/usage</c> open
/// (<see cref="ChatConversation.ShownPanel"/>). The extension shows these in VS Code dialogs, so the panel takes the
/// frame of its menus instead: the menu's colors, the input's border, 8 px corners, a shadow; a title and a ×
/// (<see cref="ChatConversation.ClosePanel"/>) over the body, which scrolls when it is taller than the list. "…" while the conversation is busy and until
/// the claude sends the data. It never takes the keyboard by itself; when it closes with the keyboard in it,
/// <see cref="FocusReleased"/> lets the view put the keyboard back. Read-only information: it shows in a read-only
/// view too.
/// </summary>
internal abstract class ChatInfoPanel : Border
{
    private static readonly StyledProperty<ChatStrings> StringsProperty =
        ClaudeChatView.StringsProperty.AddOwner<ChatInfoPanel>();

    private readonly TextBlock _title = new TextBlock();
    private readonly Button _close;
    private readonly Border _body = new Border();
    private ChatConversation? _conversation;
    private ChatConversation? _tracked;
    private bool _attached;

    protected ChatInfoPanel()
    {
        Classes.Add("ClaudeChatPanel");
        _title.Classes.Add("ClaudeChatPanelTitle");
        var icon = new Path();
        icon.Classes.Add("ClaudeChatRemoveIcon");
        var iconBox = new Viewbox { Child = icon };
        iconBox.Classes.Add("ClaudeChatRemoveIcon");
        _close = new Button { Content = iconBox };
        _close.Classes.Add("ClaudeChatButton");
        _close.Classes.Add("ClaudeChatPanelClose");
        _close.Click += (_, _) => _tracked?.ClosePanel();
        DockPanel.SetDock(_close, Dock.Right);
        var header = new DockPanel { Children = { _close, _title } };
        header.Classes.Add("ClaudeChatPanelHeader");
        DockPanel.SetDock(header, Dock.Top);
        _body.Classes.Add("ClaudeChatPanelBody");
        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _body,
        };
        Child = new DockPanel { Children = { header, scroller } };
        IsVisible = false;
    }

    /// <summary>The panel closed while the keyboard was in it.</summary>
    internal event EventHandler? FocusReleased;

    /// <summary>The conversation whose panel it shows.</summary>
    internal ChatConversation? Conversation
    {
        get => _conversation;
        set
        {
            _conversation = value;
            if (_attached) Track(value);
        }
    }

    /// <summary>The panel this control draws.</summary>
    protected abstract ChatPanel Kind { get; }

    protected abstract string Title(ChatStrings strings);

    /// <summary>The body for the conversation's data; null while there is none yet.</summary>
    protected abstract Control? Build(ChatConversation conversation, ChatStrings strings);

    /// <summary>True when the conversation's property is one the body shows.</summary>
    protected abstract bool Shows(string? propertyName);

    protected ChatStrings Strings => GetValue(StringsProperty) ?? ChatStrings.English;

    /// <summary>Styled as a Border: the theme's <c>Border.ClaudeChatPanel</c>.</summary>
    protected override Type StyleKeyOverride => typeof(Border);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Track(_conversation);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        Track(null); // the conversation outlives the view: let go of its events
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StringsProperty) Update();
    }

    private void Track(ChatConversation? conversation)
    {
        if (!ReferenceEquals(conversation, _tracked))
        {
            if (_tracked != null) _tracked.PropertyChanged -= OnConversationChanged;
            _tracked = conversation;
            if (_tracked != null) _tracked.PropertyChanged += OnConversationChanged;
        }
        // out of the window, only the events go: Avalonia is taking the tree apart, and coming back draws it again
        if (_attached) Update();
    }

    private void OnConversationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatConversation.ShownPanel) || e.PropertyName == nameof(ChatConversation.IsBusy) || (IsVisible && Shows(e.PropertyName))) Update();
    }

    private void Update()
    {
        var conversation = _tracked;
        var shown = conversation != null && conversation.ShownPanel == Kind;
        var hadFocus = IsKeyboardFocusWithin;
        if (shown)
        {
            var strings = Strings;
            _title.Text = Title(strings);
            AutomationProperties.SetName(_close, strings.Close);
            ToolTip.SetTip(_close, strings.Close);
            _body.Child = Build(conversation!, strings) ?? Empty(conversation!.IsBusy ? strings.PanelLoading : Unavailable(strings));
        }
        else
        {
            _body.Child = null;
        }
        IsVisible = shown;
        if (!shown && hadFocus) FocusReleased?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The line shown when there is no data and the conversation is idle.</summary>
    protected abstract string Unavailable(ChatStrings strings);

    private static Control Empty(string line)
    {
        var text = new TextBlock { Text = line };
        text.Classes.Add("ClaudeChatPanelLoading");
        return text;
    }

    // ---- pieces of the bodies ----

    /// <summary>A row: a color square (none when <paramref name="swatch"/> is null), the name, and a value at the right.</summary>
    protected static Border Row(string name, string value, string? swatch, bool muted)
    {
        var square = new Border();
        square.Classes.Add("ClaudeChatContextSwatch");
        if (swatch != null) square.Classes.Add(swatch);
        var label = new TextBlock { Text = name };
        label.Classes.Add("ClaudeChatPanelName");
        Grid.SetColumn(label, 1);
        var amount = new TextBlock { Text = value };
        amount.Classes.Add("ClaudeChatPanelValue");
        Grid.SetColumn(amount, 2);
        var row = new Border { Child = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Children = { square, label, amount } } };
        row.Classes.Add("ClaudeChatPanelRow");
        row.Classes.Set("muted", muted);
        return row;
    }

    /// <summary>A bar split in pieces as long as their weights; a piece without a class is the bare track.</summary>
    protected static Border Bar(string barClass, IEnumerable<(double Weight, string? PieceClass, string? Color)> pieces)
    {
        var grid = new Grid();
        foreach (var (weight, pieceClass, color) in pieces)
        {
            if (weight <= 0) continue;
            grid.ColumnDefinitions.Add(new ColumnDefinition(weight, GridUnitType.Star));
            if (pieceClass == null) continue;
            var piece = new Border();
            piece.Classes.Add(pieceClass);
            if (color != null) piece.Classes.Add(color);
            Grid.SetColumn(piece, grid.ColumnDefinitions.Count - 1);
            grid.Children.Add(piece);
        }
        var bar = new Border { Child = grid };
        bar.Classes.Add(barClass);
        return bar;
    }

    protected static TextBlock Text(string text, string textClass)
    {
        var block = new TextBlock { Text = text };
        block.Classes.Add(textClass);
        return block;
    }
}

/// <summary>
/// <c>/context</c>: "36,174 / 200,000 (18%)", a bar split by the used categories in their order (the colors
/// <c>ClaudeChat.ContextColor1</c> to <c>6</c>, round again after the sixth), the free space as the bare track; then a
/// row per category: the used ones with their color, the free space with the track's, the deferred ones (loaded only
/// when needed: not in the bar) in the secondary color, without a color.
/// </summary>
internal sealed class ContextPanel : ChatInfoPanel
{
    internal const int Colors = 6;

    protected override ChatPanel Kind => ChatPanel.Context;

    protected override string Title(ChatStrings strings) => strings.ContextTitle;

    protected override string Unavailable(ChatStrings strings) => strings.ContextUnavailable;

    protected override bool Shows(string? propertyName) => propertyName == nameof(ChatConversation.ContextUsage);

    protected override Control? Build(ChatConversation conversation, ChatStrings strings)
    {
        var usage = conversation.ContextUsage;
        if (usage == null) return null;
        var pieces = new List<(double, string?, string?)>();
        var used = new List<Control>();
        var free = new List<Control>();
        var muted = new List<Control>();
        long usedTokens = 0;
        foreach (var category in usage.Categories)
        {
            var tokens = strings.FormatNumber(category.Tokens);
            switch (category.Kind)
            {
                case "used":
                    var color = "color" + (used.Count % Colors + 1);
                    pieces.Add((Math.Max(0, category.Tokens), "ClaudeChatContextSegment", color));
                    used.Add(Row(category.Name, tokens, color, muted: false));
                    usedTokens += Math.Max(0, category.Tokens);
                    break;
                case "free":
                    free.Add(Row(category.Name, tokens, "free", muted: false));
                    break;
                default: // deferred, or a kind it does not know: listed, not in the bar
                    muted.Add(Row(category.Name, tokens, null, muted: true));
                    break;
            }
        }
        pieces.Add((Math.Max(0, usage.MaxTokens - usedTokens), null, null)); // the rest of the window: the track
        var rows = new StackPanel();
        rows.Classes.Add("ClaudeChatPanelRows");
        rows.Children.AddRange(used);
        rows.Children.AddRange(free);
        rows.Children.AddRange(muted);
        return new StackPanel
        {
            Children =
            {
                Text(strings.FormatContextTotal(usage), "ClaudeChatContextTotal"),
                Bar("ClaudeChatContextBar", pieces),
                rows,
            },
        };
    }
}

/// <summary>
/// <c>/usage</c>: a block per rate limit window the claude reported: its name ("Current session (5h)", "Weekly
/// (7 days)", else the raw key) with the percentage used at the right, a bar filled up to it, and when it renews, in
/// local time ("Resets 3:30 PM", "Resets Sat 8:05 AM" on another day than the conversation's
/// <see cref="ChatConversation.Clock"/>).
/// </summary>
internal sealed class UsagePanel : ChatInfoPanel
{
    protected override ChatPanel Kind => ChatPanel.Usage;

    protected override string Title(ChatStrings strings) => strings.UsageTitle;

    protected override string Unavailable(ChatStrings strings) => strings.UsageUnavailable;

    protected override bool Shows(string? propertyName) => propertyName == nameof(ChatConversation.UsageLimits);

    protected override Control? Build(ChatConversation conversation, ChatStrings strings)
    {
        var limits = conversation.UsageLimits;
        if (limits == null || limits.Windows.Count == 0) return null;
        var now = conversation.Clock();
        var windows = new StackPanel();
        windows.Classes.Add("ClaudeChatUsageWindows");
        foreach (var window in limits.Windows)
        {
            var share = Math.Max(0, Math.Min(1, window.Utilization));
            var head = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Children = { Text(strings.UsageWindowLabel(window.Name), "ClaudeChatPanelName") },
            };
            var percent = Text(strings.FormatUsagePercent(window.Utilization), "ClaudeChatPanelValue");
            Grid.SetColumn(percent, 1);
            head.Children.Add(percent);
            var block = new StackPanel
            {
                Children =
                {
                    head,
                    Bar("ClaudeChatUsageBar", new (double, string?, string?)[] { (share, "ClaudeChatUsageFill", null), (1 - share, null, null) }),
                },
            };
            block.Classes.Add("ClaudeChatUsageWindow");
            if (window.ResetsAt is DateTimeOffset at) block.Children.Add(Text(strings.FormatUsageResets(at, now), "ClaudeChatUsageResets"));
            windows.Children.Add(block);
        }
        return windows;
    }
}
