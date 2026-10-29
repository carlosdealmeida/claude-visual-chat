using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The Agent map (<see cref="ChatPanel.Agents"/>), like the extension's: a dialog over the whole view, on a dark
/// backdrop, with the title, how many agents the conversation had and a ×. Under them, the tree: the main agent's card
/// at the left (its model and the tokens in its context), kept in sight while the list scrolls, as the extension's sticky
/// card; at its right the agents, the newest on top, each with its dot (green while it runs, red when it failed or
/// stopped), its description and "7m 14s · 168.5k tokens"; an agent another one called goes under its caller, 32 px in.
/// A click on an agent opens its card over the map: the dot and the description, a back arrow, "Finished ·
/// general-purpose · Sonnet 5.5 · background", who called it, the time and the tokens, what it is doing while it runs,
/// and its answer as the extension shows it (the text as it came, not drawn as markdown, so it can be selected); the
/// card scrolls. Esc, the back arrow or a click beside the card goes back to the list; Esc, the × or a click on the
/// backdrop closes the map (<see cref="ChatConversation.ClosePanel"/>). An agent's change redoes its row in place; the
/// rows are laid out again only when the tree's shape changes. While it shows in a window with an agent running, a
/// timer redoes the running agents' time every second from the conversation's <see cref="ChatConversation.Clock"/>;
/// out of the window it only lets go of the conversation and stops the timer. A view narrower than the tree scrolls it
/// sideways, and the card is never wider than the view less 32 px. It takes the keyboard when it opens and, when it closes with
/// the keyboard in it, gives it back to what had it (<see cref="FocusReturn"/>); when that is gone, or in the touch layout,
/// <see cref="FocusReleased"/> lets the view put the keyboard back.
/// <para>
/// In the touch layout the subtitle speaks of a tap and wraps, and a tree that does not fit the map's width goes down instead
/// (<see cref="IsNarrow"/>): the main agent's card on top, as wide as the map, and the agents under it, as wide as the map too,
/// with the line coming down from the card's left; an agent another one called is still 32 px in, under its caller. The map then
/// scrolls only down. A tree that fits (a tablet) is laid out as on the desktop, whose layout never goes down.
/// </para>
/// </summary>
internal sealed class AgentMapPanel : Border
{
    /// <summary>How far in a level of the tree goes: the extension's 32 px before a column of agents.</summary>
    private const double Indent = 32;

    /// <summary>The main agent's card beside the agents: the tree's first column.</summary>
    private const double MainWidth = 320;

    /// <summary>An agent's row beside the main card: the theme's width of <c>ClaudeChatAgentRow</c>.</summary>
    private const double RowWidth = 300;

    /// <summary>The tree's columns beside each other: the main card's <see cref="MainWidth"/>, then the agents.</summary>
    private static ColumnDefinitions WideColumns() =>
        new ColumnDefinitions { new ColumnDefinition(new GridLength(MainWidth)), new ColumnDefinition(GridLength.Auto) };

    private static readonly StyledProperty<ChatStrings> StringsProperty =
        ClaudeChatView.StringsProperty.AddOwner<AgentMapPanel>();

    private static readonly StyledProperty<ChatLayout> LayoutProperty =
        ClaudeChatView.LayoutProperty.AddOwner<AgentMapPanel>();

    // the map
    private readonly Border _dialog = new Border();
    private readonly TextBlock _title = new TextBlock();
    private readonly TextBlock _subtitle = new TextBlock();
    private readonly Button _close;
    private readonly ScrollViewer _scroller;
    private readonly Grid _tree = new Grid { ColumnDefinitions = WideColumns() };
    private readonly Grid _mainNode = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto") };
    private readonly Ellipse _mainDot = new Ellipse();
    private readonly TextBlock _mainTitle = new TextBlock();
    private readonly TextBlock _mainMeta = new TextBlock();
    private readonly Border _mainStub = new Border();
    private readonly Border _mainRow;

    /// <summary>The main card's lines when it is open: how many agents, and how many in each state.</summary>
    private readonly StackPanel _mainDetails = new StackPanel();
    private readonly StackPanel _agents = new StackPanel();

    /// <summary>True while the main card is open; closed each time the map opens.</summary>
    private bool _mainOpen;

    /// <summary>The line from the bottom of the main card to the agents, while the tree goes down.</summary>
    private readonly Border _mainDown = Line(vertical: true);

    // the card of an agent
    private readonly Border _cardLayer = new Border();
    private readonly Border _card = new Border();
    private readonly Button _back;
    private readonly Ellipse _cardDot = new Ellipse();
    private readonly TextBlock _cardTitle = new TextBlock();
    private readonly TextBlock _cardStatus = new TextBlock();
    private readonly TextBlock _cardCaller = new TextBlock();
    private readonly TextBlock _cardTime = new TextBlock();
    private readonly TextBlock _activity = new TextBlock();
    private readonly TextBlock _noResult = new TextBlock();
    private readonly SelectableTextBlock _result = new SelectableTextBlock();

    /// <summary>What had the keyboard when the map opened, which gets it back when the map closes with it.</summary>
    private readonly FocusReturn _return = new FocusReturn();

    private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>The time lines of the running agents (in the list and on the card), redone every second.</summary>
    private readonly List<(AgentInfo Agent, TextBlock Line)> _clocks = new List<(AgentInfo, TextBlock)>();

    /// <summary>
    /// The agents' rows by <see cref="AgentInfo.Id"/>, kept while the tree keeps its shape: a change of an agent redoes
    /// its row's dot and lines in place, so a row being pressed, hovered or showing its tooltip is never replaced.
    /// </summary>
    private readonly Dictionary<string, AgentRowParts> _rows = new Dictionary<string, AgentRowParts>(StringComparer.Ordinal);

    /// <summary>The shape the rows were laid out for: each agent's id, caller and depth, in the list's order.</summary>
    private readonly List<(string Id, string? ParentId, int Depth)> _shape = new List<(string, string?, int)>();

    /// <summary>The agents whose changes it hears while it shows, and their list.</summary>
    private readonly List<AgentInfo> _watched = new List<AgentInfo>();
    private ObservableCollection<AgentInfo>? _watchedList;

    private ChatConversation? _conversation;
    private ChatConversation? _tracked;
    private bool _attached;
    private bool _buildQueued;

    /// <summary>The <see cref="AgentInfo.Id"/> of the agent whose card is open; null on the list.</summary>
    private string? _cardId;

    /// <summary>How wide the tree is beside the main card, for the agents it was last laid out for (the tree's margins left out).</summary>
    private double _wideWidth = MainWidth;

    private bool _narrow;

    public AgentMapPanel()
    {
        // ---- the map ----
        _title.Classes.Add("ClaudeChatAgentMapTitle");
        _close = IconButton("ClaudeChatRemoveIcon", 20);
        _close.Click += (_, _) => _tracked?.ClosePanel();
        DockPanel.SetDock(_close, Dock.Right);
        var header = new DockPanel { Children = { _close, _title } };
        header.Classes.Add("ClaudeChatAgentMapHeader");
        DockPanel.SetDock(header, Dock.Top);
        _subtitle.Classes.Add("ClaudeChatAgentMapSubtitle");
        DockPanel.SetDock(_subtitle, Dock.Top);

        // the main agent: its card, and the line from it to the agents' column while there are agents
        _mainDot.Classes.Add("ClaudeChatAgentDot");
        _mainTitle.Classes.Add("ClaudeChatAgentRowTitle");
        _mainMeta.Classes.Add("ClaudeChatAgentRowMeta");
        var mainBody = RowBody(_mainDot, _mainTitle, _mainMeta);
        _mainDetails.Classes.Add("ClaudeChatAgentRowBody");
        mainBody.Children.Add(_mainDetails);
        _mainRow = new Border { Child = mainBody };
        _mainRow.Classes.Add("ClaudeChatAgentRow");
        _mainRow.Classes.Add("main");
        AutomationProperties.SetControlTypeOverride(_mainRow, Avalonia.Automation.Peers.AutomationControlType.Button); // a Border that acts as a button
        // a click (or Enter, or Space) opens the card and closes it again; closed it reads like an agent's row
        _mainRow.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(_mainRow).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            ToggleMain();
        };
        _mainRow.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter && e.Key != Key.Space) return;
            e.Handled = true;
            ToggleMain();
        };
        _mainStub.Classes.Add("ClaudeChatAgentLine");
        _mainStub.Classes.Add("stub");
        _mainStub.Classes.Add("main");
        Grid.SetColumn(_mainStub, 1);
        _mainNode.Children.Add(_mainRow);
        _mainNode.Children.Add(_mainStub);
        _mainNode.VerticalAlignment = VerticalAlignment.Center;
        _mainNode.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetColumnSpan(_mainNode, 2);
        _agents.Classes.Add("ClaudeChatAgentColumn");
        _agents.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_agents, 1);
        _mainDown.Classes.Add("down");
        _mainDown.IsVisible = false;
        Grid.SetRow(_mainDown, 1);
        _tree.Classes.Add("ClaudeChatAgentTree");
        _tree.Children.Add(_mainDown);
        _tree.Children.Add(_agents);
        _tree.Children.Add(_mainNode); // drawn over the lines, as the extension's card (z-index 1)
        _scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, // a view narrower than the tree scrolls it sideways
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _tree,
        };
        _scroller.ScrollChanged += (_, _) => KeepMainInSight();
        _mainNode.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty) KeepMainInSight();
        };
        _dialog.Classes.Add("ClaudeChatAgentMap");
        _dialog.Child = new DockPanel { Children = { header, _subtitle, _scroller } };
        KeyboardNavigation.SetTabNavigation(_dialog, KeyboardNavigationMode.Cycle);

        // ---- the card ----
        _back = IconButton("ClaudeChatAgentBackIcon", 20);
        _back.Click += (_, _) => Back();
        _cardDot.Classes.Add("ClaudeChatAgentDot");
        _cardDot.Classes.Add("card");
        DockPanel.SetDock(_cardDot, Dock.Left);
        _cardTitle.Classes.Add("ClaudeChatAgentMapTitle");
        DockPanel.SetDock(_back, Dock.Left);
        var cardHeader = new DockPanel { Children = { _back, _cardDot, _cardTitle } };
        cardHeader.Classes.Add("ClaudeChatAgentMapHeader");
        foreach (var line in new[] { _cardStatus, _cardCaller, _cardTime }) line.Classes.Add("ClaudeChatAgentCardMeta");
        _activity.Classes.Add("ClaudeChatAgentActivity");
        _noResult.Classes.Add("ClaudeChatAgentCardMeta");
        _noResult.Classes.Add("ClaudeChatAgentResult");
        _result.Classes.Add("ClaudeChatAgentResult");
        _card.Classes.Add("ClaudeChatAgentCard");
        _card.Child = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new StackPanel { Children = { cardHeader, _cardStatus, _cardCaller, _cardTime, _activity, _noResult, _result } },
        };
        KeyboardNavigation.SetTabNavigation(_card, KeyboardNavigationMode.Cycle);
        _cardLayer.Classes.Add("ClaudeChatAgentCardLayer");
        _cardLayer.Child = _card;
        _cardLayer.IsVisible = false;
        _cardLayer.PointerPressed += (_, e) =>
        {
            if (!ReferenceEquals(e.Source, _cardLayer)) return; // beside the card, not on it
            e.Handled = true;
            Back();
        };

        Child = new Panel { Children = { _dialog, _cardLayer } };
        _timer.Tick += (_, _) => Tick();
        IsVisible = false;
    }

    /// <summary>The map closed while the keyboard was in it.</summary>
    internal event EventHandler? FocusReleased;

    /// <summary>The conversation whose agents it maps; another one goes back to the list.</summary>
    internal ChatConversation? Conversation
    {
        get => _conversation;
        set
        {
            if (!ReferenceEquals(value, _conversation)) _cardId = null;
            _conversation = value;
            if (_attached) Track(value);
        }
    }

    /// <summary>True while the timer redoes the running agents' time every second: shown, in a window, with an agent running.</summary>
    internal bool IsTicking => _timer.IsEnabled;

    /// <summary>True while an agent's card is open over the map.</summary>
    internal bool ShowsCard => _cardId != null && IsVisible;

    /// <summary>
    /// True while the tree goes down, the agents under the main card: in the touch layout, when the tree laid out as on the desktop
    /// would be wider than the map.
    /// </summary>
    internal bool IsNarrow => _narrow;

    /// <summary>The timer's second: the running agents' time, from the conversation's clock, while it can be seen.</summary>
    internal void Tick()
    {
        if (!IsEffectivelyVisible || _tracked == null) return;
        var strings = Strings;
        var now = _tracked.Clock();
        foreach (var (agent, line) in _clocks) SetLine(line, TimeAndTokens(agent, strings, now));
    }

    /// <summary>From an agent's card back to the list; false when no card is open.</summary>
    internal bool Back()
    {
        if (_cardId == null) return false;
        var id = _cardId;
        _cardId = null;
        _cardLayer.IsVisible = false;
        _result.Text = null;
        FocusRow(id); // the keyboard goes back to the agent's row, as the extension's dialog gives it back to what opened it
        return true;
    }

    private ChatStrings Strings => GetValue(StringsProperty) ?? ChatStrings.English;

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
        Track(null); // the conversation outlives the view: let go of its events, and stop the timer
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StringsProperty)
        {
            if (_attached) Update();
        }
        else if (change.Property == LayoutProperty)
        {
            if (GetValue(LayoutProperty) == ChatLayout.Touch) _subtitle.TextWrapping = TextWrapping.Wrap;
            else _subtitle.ClearValue(TextBlock.TextWrappingProperty);
            if (_attached) Update(); // the subtitle's words, and the tree that may go down or come back up
        }
        else if (change.Property == BoundsProperty)
        {
            // the card: the extension's min(420px, 100vw - 32px) wide and 100vh - 64px high at most
            _card.MaxWidth = Math.Max(0, Bounds.Width - 32);
            _card.MaxHeight = Math.Max(0, Bounds.Height - 64);
            if (IsVisible && _tracked != null && SetNarrow(ShouldBeNarrow())) Build(_tracked);
        }
    }

    /// <summary>
    /// True in the touch layout when the tree, laid out as on the desktop, would not fit in the map: the view's width less the
    /// dialog's margin, border and padding (and 1200 px at most, the dialog's widest) and less the tree's margins.
    /// </summary>
    private bool ShouldBeNarrow()
    {
        if (GetValue(LayoutProperty) != ChatLayout.Touch) return false;
        // the map is arranged only while it shows: before its first showing, the view's width (the map covers all of it)
        var width = Bounds.Width > 0 ? Bounds.Width : (this.GetVisualParent()?.Bounds.Width ?? 0);
        if (width <= 0) return false;
        var dialog = Math.Min(width - _dialog.Margin.Left - _dialog.Margin.Right, _dialog.MaxWidth);
        var room = dialog - _dialog.BorderThickness.Left - _dialog.BorderThickness.Right - _dialog.Padding.Left - _dialog.Padding.Right
                   - _tree.Margin.Left - _tree.Margin.Right;
        return _wideWidth > room + 0.5;
    }

    /// <summary>
    /// Lays the tree out down (the main card on top, the agents under it, all as wide as the map) or as on the desktop. True when
    /// it changed: the rows are laid out again.
    /// </summary>
    private bool SetNarrow(bool narrow)
    {
        if (narrow == _narrow) return false;
        _narrow = narrow;
        _tree.Classes.Set("narrow", narrow);
        _tree.ColumnDefinitions = narrow ? new ColumnDefinitions("*") : WideColumns();
        _tree.RowDefinitions = narrow ? new RowDefinitions("Auto,Auto") : new RowDefinitions();
        Grid.SetColumn(_agents, narrow ? 0 : 1);
        Grid.SetRow(_agents, narrow ? 1 : 0);
        Grid.SetColumnSpan(_mainNode, narrow ? 1 : 2);
        _agents.VerticalAlignment = narrow ? VerticalAlignment.Top : VerticalAlignment.Center;
        if (narrow) _agents.Margin = new Thickness(0, 8, 0, 0);
        else _agents.ClearValue(MarginProperty); // back to what it was: a theme's style may set it
        _mainNode.ColumnDefinitions = new ColumnDefinitions(narrow ? "*,Auto" : "Auto,Auto");
        _mainNode.HorizontalAlignment = narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        _mainNode.VerticalAlignment = narrow ? VerticalAlignment.Top : VerticalAlignment.Center;
        Stretch(_mainRow, narrow);
        // the dialog as wide as the map, which scrolls only down
        if (narrow) _dialog.HorizontalAlignment = HorizontalAlignment.Stretch;
        else _dialog.ClearValue(HorizontalAlignmentProperty);
        _scroller.HorizontalScrollBarVisibility = narrow ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        _scroller.Offset = default;
        _mainNode.RenderTransform = null;
        _shape.Clear(); // the rows again, in the other shape
        return true;
    }

    /// <summary>A row as wide as its column, while the tree goes down; else the theme's width.</summary>
    private static void Stretch(Control row, bool narrow)
    {
        if (narrow)
        {
            row.Width = double.NaN;
            row.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
        else
        {
            row.ClearValue(WidthProperty);
            row.ClearValue(HorizontalAlignmentProperty);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!ReferenceEquals(e.Source, this)) return; // a click on the backdrop, not on the map
        e.Handled = true;
        _tracked?.ClosePanel();
    }

    private void Track(ChatConversation? conversation)
    {
        if (!ReferenceEquals(conversation, _tracked))
        {
            if (_tracked != null) _tracked.PropertyChanged -= OnConversationChanged;
            _tracked = conversation;
            if (_tracked != null) _tracked.PropertyChanged += OnConversationChanged;
        }
        // out of the window, only the events and the timer go: Avalonia is taking the tree apart, and coming back
        // draws it again (and starts the timer)
        if (_attached)
        {
            Update();
        }
        else
        {
            Unwatch();
            _timer.Stop();
        }
    }

    private void OnConversationChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ChatConversation.ShownPanel):
                Update();
                break;
            case nameof(ChatConversation.IsBusy):
                if (IsVisible)
                {
                    _mainDot.Classes.Set("running", _tracked!.IsBusy);
                    if (_mainOpen) QueueBuild(); // "Working" or "Idle"
                }
                break;
            case nameof(ChatConversation.Title):
                if (IsVisible) QueueBuild();
                break;
            case nameof(ChatConversation.Model):
            case nameof(ChatConversation.CurrentModel):
            case nameof(ChatConversation.AvailableModels):
            case nameof(ChatConversation.ContextUsage):
                if (IsVisible) QueueBuild();
                break;
        }
    }

    private void OnAgentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Watch(_tracked);
        QueueBuild();
    }

    private void OnAgentChanged(object? sender, PropertyChangedEventArgs e) => QueueBuild();

    /// <summary>Hears the agents' list and each agent, while the map shows.</summary>
    private void Watch(ChatConversation? conversation)
    {
        Unwatch();
        if (conversation == null) return;
        _watchedList = conversation.Agents;
        _watchedList.CollectionChanged += OnAgentsChanged;
        foreach (var agent in _watchedList)
        {
            agent.PropertyChanged += OnAgentChanged;
            _watched.Add(agent);
        }
    }

    private void Unwatch()
    {
        if (_watchedList != null) _watchedList.CollectionChanged -= OnAgentsChanged;
        _watchedList = null;
        foreach (var agent in _watched) agent.PropertyChanged -= OnAgentChanged;
        _watched.Clear();
    }

    /// <summary>One build for many changes (a progress line changes four fields of an agent).</summary>
    private void QueueBuild()
    {
        if (_buildQueued) return;
        _buildQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _buildQueued = false;
            if (_attached && IsVisible && _tracked != null) Build(_tracked);
        });
    }

    private void Update()
    {
        var conversation = _tracked;
        var shown = conversation != null && conversation.ShownPanel == ChatPanel.Agents;
        var hadFocus = IsKeyboardFocusWithin;
        var opening = shown && !IsVisible;
        if (shown)
        {
            if (opening)
            {
                _cardId = null;
                _mainOpen = false;
            }
            Watch(conversation);
            IsVisible = true;
            Build(conversation!);
            if (opening)
            {
                _return.Remember(this);
                _close.Focus();
            }
        }
        else
        {
            Unwatch();
            _timer.Stop();
            _cardId = null;
            _cardLayer.IsVisible = false;
            _agents.Children.Clear();
            _rows.Clear();
            _shape.Clear();
            _clocks.Clear();
            _result.Text = null;
            IsVisible = false;
            // the keyboard goes back to what had it; the view's rule (a waiting request, the reply box) when that is gone, or in
            // the touch layout, where the reply box with the keyboard is the soft keyboard coming up
            if (!hadFocus) _return.Forget();
            else if (!(CanRestoreFocus() && _return.Restore())) FocusReleased?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>True when the keyboard may go back to where it was: not in the touch layout, and not over a request that waits for it.</summary>
    private bool CanRestoreFocus() => GetValue(LayoutProperty) != ChatLayout.Touch && _tracked?.CurrentPermission == null;

    private void Build(ChatConversation conversation)
    {
        var strings = Strings;
        var now = conversation.Clock();
        _clocks.Clear();

        _title.Text = strings.AgentMapTitle;
        _subtitle.Text = strings.FormatAgentMapSubtitle(conversation.Agents.Count, GetValue(LayoutProperty) == ChatLayout.Touch);
        Label(_close, strings.Close);
        Label(_back, strings.Back);

        BuildMain(conversation, strings);
        var tree = new Tree(conversation.Agents);
        _wideWidth = WideWidth(tree);
        SetNarrow(ShouldBeNarrow());
        _mainStub.IsVisible = conversation.Agents.Count > 0 && !_narrow;
        _mainDown.IsVisible = conversation.Agents.Count > 0 && _narrow;
        if (!SameShape(conversation.Agents)) LayOut(tree, conversation.Agents);
        foreach (var agent in conversation.Agents)
        {
            if (_rows.TryGetValue(agent.Id, out var row)) Fill(row, agent, strings, now);
        }

        var card = _cardId == null ? null : tree.Find(_cardId);
        if (card == null)
        {
            if (_cardLayer.IsKeyboardFocusWithin) _close.Focus(); // its agent went (a session loaded again)
            _cardId = null;
            _cardLayer.IsVisible = false;
            _result.Text = null;
        }
        else
        {
            BuildCard(card, tree, conversation, strings, now);
        }
        UpdateTimer();
    }

    /// <summary>
    /// The main card: the conversation's title (or "This session"); closed, "model · 12.3k tokens in context"; open, "Working" or
    /// "Idle" in front of those, then how many agents, and a line for each state that has any.
    /// </summary>
    private void BuildMain(ChatConversation conversation, ChatStrings strings)
    {
        _mainTitle.Text = string.IsNullOrWhiteSpace(conversation.Title) ? strings.AgentThisSession : conversation.Title!.Trim();
        AutomationProperties.SetName(_mainRow, _mainTitle.Text);
        _mainDot.Classes.Set("running", conversation.IsBusy);
        SetLine(_mainMeta, Join(
            _mainOpen ? (conversation.IsBusy ? strings.AgentMainWorking : strings.AgentMainIdle) : null,
            ModelChip.NameOf(conversation),
            conversation.ContextUsage is { } usage ? strings.FormatAgentContextTokens(usage.TotalTokens) : null));
        _mainDetails.Children.Clear();
        _mainDetails.IsVisible = _mainOpen;
        if (!_mainOpen) return;
        int waiting = 0, running = 0, finished = 0, failed = 0, stopped = 0;
        foreach (var agent in conversation.Agents)
        {
            switch (agent.Status)
            {
                case AgentStatus.Running:
                    if (agent.IsWaitingForPermission) waiting++;
                    else running++;
                    break;
                case AgentStatus.Completed: finished++; break;
                case AgentStatus.Failed: failed++; break;
                case AgentStatus.Stopped: stopped++; break;
            }
        }
        AddMainLine(strings.FormatAgentsChip(conversation.Agents.Count));
        if (running > 0) AddMainLine(strings.FormatAgentCount(strings.AgentCountRunning, running));
        if (waiting > 0) AddMainLine(strings.FormatAgentCount(strings.AgentCountWaiting, waiting));
        if (finished > 0) AddMainLine(strings.FormatAgentCount(strings.AgentCountFinished, finished));
        if (failed > 0) AddMainLine(strings.FormatAgentCount(strings.AgentCountFailed, failed));
        if (stopped > 0) AddMainLine(strings.FormatAgentCount(strings.AgentCountStopped, stopped));
    }

    private void AddMainLine(string text)
    {
        var line = new TextBlock { Text = text };
        line.Classes.Add("ClaudeChatAgentRowMeta");
        _mainDetails.Children.Add(line);
    }

    private void ToggleMain()
    {
        _mainOpen = !_mainOpen;
        if (_tracked != null && IsVisible) Build(_tracked);
    }

    /// <summary>The timer runs while the map shows in a window with a running agent's time to redo.</summary>
    private void UpdateTimer()
    {
        if (_attached && IsVisible && _clocks.Count > 0) _timer.Start();
        else _timer.Stop();
    }

    private bool SameShape(IReadOnlyList<AgentInfo> agents)
    {
        if (agents.Count != _shape.Count) return false;
        for (var i = 0; i < agents.Count; i++)
        {
            var agent = agents[i];
            if (_shape[i] != (agent.Id, agent.ParentId, agent.Depth)) return false;
        }
        return true;
    }

    /// <summary>The tree's rows and lines, anew: a new agent, or one whose caller or depth changed.</summary>
    private void LayOut(Tree tree, IReadOnlyList<AgentInfo> agents)
    {
        var focusedRow = FocusedRowId();
        _agents.Children.Clear();
        _rows.Clear();
        _shape.Clear();
        foreach (var agent in agents) _shape.Add((agent.Id, agent.ParentId, agent.Depth));
        AddLevel(_agents, tree.Roots, tree, linkedUp: false);
        if (focusedRow != null && _cardId == null) FocusRow(focusedRow);
    }

    /// <summary>A column of agents: each with the line from the column's spine, and the agents it called under it.</summary>
    private void AddLevel(Panel column, List<AgentInfo> agents, Tree tree, bool linkedUp)
    {
        for (var i = 0; i < agents.Count; i++)
        {
            var agent = agents[i];
            // an agent whose caller never reached the conversation keeps its depth's place
            var indent = linkedUp ? 0 : Math.Max(0, agent.Depth - 1) * Indent;
            var called = tree.CalledBy(agent);
            Control? nested = null;
            if (called.Count > 0)
            {
                var inner = new StackPanel();
                inner.Classes.Add("ClaudeChatAgentColumn");
                inner.Classes.Add("nested");
                AddLevel(inner, called, tree, linkedUp: true);
                var down = Line(vertical: true);
                down.Classes.Add("down");
                nested = new Panel { Children = { down, inner } };
            }
            var row = AgentRow(agent);
            Stretch(row, _narrow);
            // down, the top level's spine starts at the main card above it, and not at the first row's middle
            column.Children.Add(Branch(row, nested, indent,
                first: i == 0 && !linkedUp && !_narrow, last: i == agents.Count - 1, _narrow));
        }
    }

    /// <summary>
    /// How wide the tree is laid out as on the desktop, its margins left out: the main card and, with agents, the widest of
    /// them, each 32 px further in than its caller (or kept at its depth's place).
    /// </summary>
    private static double WideWidth(Tree tree)
    {
        double agents = 0;
        void Level(List<AgentInfo> level, double before, bool linkedUp)
        {
            foreach (var agent in level)
            {
                var start = before + Indent + (linkedUp ? 0 : Math.Max(0, agent.Depth - 1) * Indent);
                agents = Math.Max(agents, start + RowWidth);
                Level(tree.CalledBy(agent), start, linkedUp: true);
            }
        }
        Level(tree.Roots, 0, linkedUp: false);
        return MainWidth + agents;
    }

    /// <summary>
    /// An agent in its column, like the extension's: 32 px before the row (more for an agent kept at its depth), a
    /// 16 px line from the spine to the row, and the spine (16 px in) from the first row's middle to the last one's,
    /// beside the agents called in between. While the tree goes down (<paramref name="narrow"/>) the row and the agents it
    /// called take the rest of the column's width.
    /// </summary>
    private static Control Branch(Control row, Control? nested, double indent, bool first, bool last, bool narrow)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(Indent + indent, GridUnitType.Pixel),
                new ColumnDefinition(narrow ? new GridLength(1, GridUnitType.Star) : GridLength.Auto),
            },
            RowDefinitions = new RowDefinitions("Auto,Auto"),
        };
        var stub = Line(vertical: false);
        stub.Classes.Add("stub");
        stub.Width = Indent / 2 + indent;
        var halves = new Grid { RowDefinitions = new RowDefinitions("*,*") };
        var up = Line(vertical: true);
        up.IsVisible = !first;
        var downHalf = Line(vertical: true);
        downHalf.IsVisible = !last;
        Grid.SetRow(downHalf, 1);
        halves.Children.Add(up);
        halves.Children.Add(downHalf);
        Grid.SetColumn(row, 1);
        grid.Children.Add(halves);
        grid.Children.Add(stub);
        grid.Children.Add(row);
        if (nested != null)
        {
            var beside = Line(vertical: true);
            beside.IsVisible = !last;
            Grid.SetRow(beside, 1);
            Grid.SetRow(nested, 1);
            Grid.SetColumn(nested, 1);
            grid.Children.Add(beside);
            grid.Children.Add(nested);
        }
        return grid;
    }

    private static Border Line(bool vertical)
    {
        var line = new Border();
        line.Classes.Add("ClaudeChatAgentLine");
        line.Classes.Add(vertical ? "spine" : "stub");
        return line;
    }

    /// <summary>An agent's row, empty: <see cref="Fill"/> puts its dot and lines.</summary>
    private Button AgentRow(AgentInfo agent)
    {
        var id = agent.Id;
        var parts = new AgentRowParts(id);
        parts.Row.Click += (_, _) => OpenCard(id);
        _rows[id] = parts;
        return parts.Row;
    }

    /// <summary>Puts the agent's state in its row, changing only what changed.</summary>
    private void Fill(AgentRowParts parts, AgentInfo agent, ChatStrings strings, DateTimeOffset now)
    {
        SetDotState(parts.Dot, agent.Status);
        if (parts.Title.Text != agent.Description)
        {
            parts.Title.Text = agent.Description;
            ToolTip.SetTip(parts.Row, agent.Description);
            AutomationProperties.SetName(parts.Row, agent.Description);
        }
        SetLine(parts.Meta, TimeAndTokens(agent, strings, now));
        if (agent.Status == AgentStatus.Running) _clocks.Add((agent, parts.Meta));
    }

    /// <summary>The parts of an agent's row that change with it.</summary>
    private sealed class AgentRowParts
    {
        public AgentRowParts(string id)
        {
            Dot.Classes.Add("ClaudeChatAgentDot");
            Title.Classes.Add("ClaudeChatAgentRowTitle");
            Meta.Classes.Add("ClaudeChatAgentRowMeta");
            Row = new Button { Content = RowBody(Dot, Title, Meta), Tag = id };
            Row.Classes.Add("ClaudeChatButton");
            Row.Classes.Add("ClaudeChatAgentRow");
        }

        public Button Row { get; }

        public Ellipse Dot { get; } = new Ellipse();

        public TextBlock Title { get; } = new TextBlock();

        public TextBlock Meta { get; } = new TextBlock();
    }

    /// <summary>A row's inside: the dot and the title on one line (6 px apart), the meta line under them, 14 px in.</summary>
    private static StackPanel RowBody(Ellipse dot, TextBlock title, TextBlock meta)
    {
        DockPanel.SetDock(dot, Dock.Left);
        var head = new DockPanel { Children = { dot, title } };
        head.Classes.Add("ClaudeChatAgentRowHead");
        var body = new StackPanel { Children = { head, meta } };
        body.Classes.Add("ClaudeChatAgentRowBody");
        return body;
    }

    private void OpenCard(string id)
    {
        if (_tracked == null) return;
        _cardId = id;
        Build(_tracked);
        // the arrow is in the card's scroller, whose template comes with the card's first layout
        Dispatcher.UIThread.Post(() =>
        {
            if (_attached && ShowsCard && _cardId == id) _back.Focus();
        }, DispatcherPriority.Loaded);
    }

    private void BuildCard(AgentInfo agent, Tree tree, ChatConversation conversation, ChatStrings strings, DateTimeOffset now)
    {
        SetDotState(_cardDot, agent.Status);
        _cardTitle.Text = agent.Description;
        ToolTip.SetTip(_cardTitle, agent.Description);
        SetLine(_cardStatus, Join(
            strings.AgentStatusLabel(agent.Status),
            agent.AgentType,
            ModelName(agent.Model, conversation.AvailableModels),
            agent.IsBackground ? strings.AgentBackground : null));
        var caller = agent.ParentId == null ? null : tree.Find(agent.ParentId);
        SetLine(_cardCaller, caller != null
            ? strings.FormatAgentSpawnedBy(caller.Description)
            : agent.Depth <= 1 ? strings.AgentSpawnedByMain : null); // a caller that never reached the conversation: not told
        SetLine(_cardTime, TimeAndTokens(agent, strings, now));
        if (agent.Status == AgentStatus.Running) _clocks.Add((agent, _cardTime));
        // what it is doing, while it runs: the extension's activity line
        SetLine(_activity, agent.Status == AgentStatus.Running ? agent.Activity : null);
        var result = agent.Result;
        _result.IsVisible = !string.IsNullOrEmpty(result);
        if (_result.Text != result) _result.Text = result;
        _noResult.Text = strings.AgentNoResult;
        _noResult.IsVisible = string.IsNullOrEmpty(result);
        _cardLayer.IsVisible = true;
    }

    /// <summary>"7m 14s · 168.5k tokens": the time while it runs from its start, else the reported one (none under a second), and the tokens.</summary>
    private static string TimeAndTokens(AgentInfo agent, ChatStrings strings, DateTimeOffset now)
    {
        var ms = agent.Status == AgentStatus.Running ? (long)(now - agent.StartedAt).TotalMilliseconds : agent.DurationMs;
        return Join(
            ms is long time && time >= 1000 ? ChatStrings.FormatAgentTime(time) : null,
            agent.Tokens is long tokens && tokens > 0 ? strings.FormatAgentTokens(tokens) : null);
    }

    /// <summary>
    /// An agent's model as the model chip names it: an offered model's name, else the family and version of a claude id
    /// ("claude-haiku-4-5-20251001" is "Haiku 4.5"), else an alias with a capital ("sonnet" is "Sonnet"), else the id.
    /// </summary>
    internal static string? ModelName(string? id, IReadOnlyList<ChatModel>? models)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (models != null)
        {
            foreach (var model in models)
            {
                if (model.Value == id && model.Value != "default") return model.DisplayName;
            }
            foreach (var model in models)
            {
                if (model.ResolvedModel == id && model.Value != "default") return model.DisplayName;
            }
        }
        var named = ClaudeModelName(id!);
        if (named != null) return named;
        var word = true;
        foreach (var c in id!) word &= c >= 'a' && c <= 'z';
        return word ? char.ToUpperInvariant(id[0]) + id.Substring(1) : id;
    }

    /// <summary>"claude-sonnet-5-5" and "claude-haiku-4-5-20251001[1m]" as "Sonnet 5.5" and "Haiku 4.5 (1M)"; null for another id.</summary>
    private static string? ClaudeModelName(string id)
    {
        var text = id.Trim().ToLowerInvariant();
        var oneMillion = text.EndsWith("[1m]", StringComparison.Ordinal);
        if (oneMillion) text = text.Substring(0, text.Length - 4);
        if (!text.StartsWith("claude-", StringComparison.Ordinal)) return null;
        var parts = text.Substring(7).Split('-');
        if (parts.Length < 2 || (parts[0] != "opus" && parts[0] != "sonnet" && parts[0] != "haiku")) return null;
        if (!Digits(parts[1], 1, 2)) return null;
        var version = parts[1];
        var next = 2;
        if (parts.Length > 2 && Digits(parts[2], 1, 2))
        {
            version += "." + parts[2];
            next = 3;
        }
        if (parts.Length > next + 1 || (parts.Length == next + 1 && !Digits(parts[next], 8, 8))) return null; // only a date may follow
        return char.ToUpperInvariant(parts[0][0]) + parts[0].Substring(1) + " " + version + (oneMillion ? " (1M)" : "");
    }

    private static bool Digits(string text, int min, int max)
    {
        if (text.Length < min || text.Length > max) return false;
        foreach (var c in text)
        {
            if (c < '0' || c > '9') return false;
        }
        return true;
    }

    /// <summary>
    /// The main agent's card stays in sight, as the extension's sticky one: centered beside the agents, but pushed back
    /// in at the top or the bottom when the list scrolls it out (a move of the drawing, not of the layout). Not while the tree
    /// goes down.
    /// </summary>
    private void KeepMainInSight()
    {
        if (_narrow) return; // down, the card is over the agents and scrolls away with them: nothing at its side to keep it company
        var natural = _tree.Margin.Top + _mainNode.Bounds.Top - _scroller.Offset.Y;
        var room = _scroller.Viewport.Height - _mainNode.Bounds.Height;
        var wanted = Math.Max(0, Math.Min(natural, room));
        var shift = wanted - natural;
        var current = (_mainNode.RenderTransform as TranslateTransform)?.Y ?? 0;
        if (Math.Abs(current - shift) < 0.01) return;
        _mainNode.RenderTransform = Math.Abs(shift) < 0.01 ? null : new TranslateTransform(0, shift);
    }

    private string? FocusedRowId()
    {
        foreach (var candidate in _agents.GetLogicalDescendants())
        {
            if (candidate is Button { IsKeyboardFocusWithin: true } button) return button.Tag as string;
        }
        return null;
    }

    private void FocusRow(string id)
    {
        foreach (var candidate in _agents.GetLogicalDescendants())
        {
            if (candidate is Button button && Equals(button.Tag, id))
            {
                button.Focus();
                return;
            }
        }
        _close.Focus();
    }

    private static void SetDotState(Ellipse dot, AgentStatus status)
    {
        dot.Classes.Set("running", status == AgentStatus.Running);
        dot.Classes.Set("failed", status == AgentStatus.Failed || status == AgentStatus.Stopped);
    }

    private static void SetLine(TextBlock line, string? text)
    {
        if (line.Text != text) line.Text = text;
        line.IsVisible = !string.IsNullOrEmpty(text);
    }

    private static string Join(params string?[] parts)
    {
        var kept = new List<string>();
        foreach (var part in parts)
        {
            if (!string.IsNullOrEmpty(part)) kept.Add(part!);
        }
        return string.Join(" · ", kept);
    }

    private static void Label(Control control, string name)
    {
        AutomationProperties.SetName(control, name);
        ToolTip.SetTip(control, name);
    }

    /// <summary>A 28 px icon button, like the extension's: the icon (on a 20 px box) with 4 px around it. The touch layout's styles make it 44 px.</summary>
    private static Button IconButton(string iconClass, double iconSize)
    {
        var icon = new Path();
        icon.Classes.Add(iconClass);
        var box = new Viewbox { Width = iconSize, Height = iconSize, Child = icon };
        var button = new Button { Content = new Border { Width = 20, Height = 20, Child = box } };
        button.Classes.Add("ClaudeChatButton");
        button.Classes.Add("ClaudeChatAgentMapButton");
        return button;
    }

    /// <summary>
    /// The agents as a tree, the newest first at every level: each under the agent that called it when that one is
    /// known (and not in a loop of callers), else at the top.
    /// </summary>
    private sealed class Tree
    {
        private readonly Dictionary<string, AgentInfo> _byId = new Dictionary<string, AgentInfo>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<AgentInfo>> _called = new Dictionary<string, List<AgentInfo>>(StringComparer.Ordinal);

        public Tree(IReadOnlyList<AgentInfo> agents)
        {
            foreach (var agent in agents) _byId[agent.Id] = agent;
            foreach (var agent in agents)
            {
                if (HasCaller(agent))
                {
                    if (!_called.TryGetValue(agent.ParentId!, out var list)) _called[agent.ParentId!] = list = new List<AgentInfo>();
                    list.Add(agent);
                }
                else
                {
                    Roots.Add(agent);
                }
            }
        }

        public List<AgentInfo> Roots { get; } = new List<AgentInfo>();

        public AgentInfo? Find(string id) => _byId.TryGetValue(id, out var agent) ? agent : null;

        public List<AgentInfo> CalledBy(AgentInfo agent) =>
            _called.TryGetValue(agent.Id, out var list) ? list : new List<AgentInfo>();

        /// <summary>True when its caller is in the conversation and the chain of callers reaches the top.</summary>
        private bool HasCaller(AgentInfo agent)
        {
            if (agent.ParentId == null || !_byId.ContainsKey(agent.ParentId)) return false;
            var steps = _byId.Count;
            var current = agent;
            while (current.ParentId != null && _byId.TryGetValue(current.ParentId, out var caller))
            {
                if (ReferenceEquals(caller, agent) || --steps < 0) return false; // a loop
                current = caller;
            }
            return true;
        }
    }
}
