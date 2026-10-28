using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// One tool call, like the extension draws it: a dot (its color tells the state), the tool's name in bold and what it
/// works on (a file in the link color, a command's description, the lines read); under it a line such as "Modified"
/// or "47 lines", and a box with the diff, the start of the file written, or the command (IN) and its output (OUT,
/// that scrolls sideways); "Run in background" under a command started so; for a subagent, its items behind an arrow. The dot
/// of a running call blinks only while the conversation works. Every text in the box is cut at 60 px with a fade; a long one
/// (<see cref="ExpandableChars"/>, <see cref="ExpandableLines"/>) opens whole in place with a click, Enter or Space, and a
/// huge one stops at <see cref="MaxExpandedChars"/>, so a huge result never slows the list. A refused command, edit or write
/// says what the user typed on a line of its own. An edit reads its net change ("Added 2 lines", "Removed 1 line", "Modified"), a
/// write its lines ("Write succeeded" for a content that is not a text); a click on the diff, Enter or Space opens the Diff View over
/// the whole view. In a desktop panel narrower than <see cref="ClaudeChatView.NarrowWidth"/> the box of an edit or a write goes,
/// like the extension's; the touch layout keeps it.
/// A plan (<c>ExitPlanMode</c>) reads "Claude’s Plan" and its file, then whether the user approved it, with no box; a click on
/// its header opens the plan again in the document over the list.
/// A subagent's call (Agent or Task) reads "Agent:" and its description, and its box holds only the prompt (IN), never an output;
/// a background agent's row adds a line with a dot that tells what it is doing, that a request of its waits below, or how it
/// ended, and its steps stay off the list (the map has them).
/// In the touch layout (pseudo-class <c>:touch</c>) the call is a block with a frame and its dot is the header's first
/// piece.
/// </summary>
internal partial class ToolCallView : UserControl
{
    /// <summary>A text in the box with more characters than this can be opened whole.</summary>
    internal const int ExpandableChars = 250;

    /// <summary>A text in the box with more lines than this can be opened whole.</summary>
    internal const int ExpandableLines = 3;

    /// <summary>Most characters of a text opened whole; past this it ends with an ellipsis.</summary>
    internal const int MaxExpandedChars = 100_000;

    /// <summary>Height kept under a line that scrolls sideways, for the bar that is drawn over it.</summary>
    private const double ScrollBarRoom = 12;

    /// <summary>Most characters of the reason a user gave for refusing a call (what the claude gets back is cut there too).</summary>
    private const int MaxReasonChars = 4000;

    /// <summary>Most characters of a diff line or a to-do shown.</summary>
    internal const int MaxLineChars = 400;

    /// <summary>Most diff rows shown; the rest becomes "+N lines".</summary>
    internal const int MaxDiffRows = 200;

    /// <summary>
    /// Lines of a command or a written file put in its box, which shows about four of them; each is cut at
    /// <see cref="MaxLineChars"/>. The permission request shows them whole.
    /// </summary>
    internal const int BoxLines = 8;

    /// <summary>The tool that asks to leave plan mode with a plan: its row has a line of its own and opens the plan's document.</summary>
    private const string PlanTool = "ExitPlanMode";

    /// <summary>The name an Agent (or Task) call reads, with its colon: the tool's name, the same in every language.</summary>
    private const string AgentTitle = "Agent:";

    private static readonly StyledProperty<ChatStrings> StringsProperty =
        ClaudeChatView.StringsProperty.AddOwner<ToolCallView>();

    private static readonly StyledProperty<bool> IsWorkingProperty =
        ClaudeChatView.IsWorkingProperty.AddOwner<ToolCallView>();

    private static readonly StyledProperty<ChatLayout> LayoutProperty =
        ClaudeChatView.LayoutProperty.AddOwner<ToolCallView>();

    private static readonly StyledProperty<bool> IsNarrowProperty =
        ClaudeChatView.IsNarrowProperty.AddOwner<ToolCallView>();

    /// <summary>The key of the layer over the diff, among the box's focusable parts.</summary>
    private const string DiffKey = "diff";

    private ToolCallItem? _item;
    private AgentInfo? _agent; // the agent an Agent call started, while the row is in the window
    private DockPanel? _agentLine; // the line under a background agent's row, while it has one
    private bool _attached;
    private NavigationMethod _focusMethod; // how a text of the box got the focus, so a redraw gives it back the same way (a key keeps the ring)

    public ToolCallView()
    {
        InitializeComponent();
        Header.Click += OnHeaderClick;
        Refresh();
    }

    /// <summary>
    /// A click on the header of a plan's row opens the plan again, in the document over the list; the toggle it also is (for a
    /// subagent's steps) stays as it was.
    /// </summary>
    private void OnHeaderClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var item = _item;
        if (item == null || !IsPlanRow(item)) return;
        Header.IsChecked = false;
        this.FindAncestorOfType<ClaudeChatView>()?.ShowPlan(item);
    }

    /// <summary>An <c>ExitPlanMode</c> row with the plan's text, which its header opens.</summary>
    private static bool IsPlanRow(ToolCallItem item) => item.Name == PlanTool && PlanReview.HasPlan(item.Input);

    private ChatStrings Strings => GetValue(StringsProperty) ?? ChatStrings.English;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Track(DataContext as ToolCallItem);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        Track(null); // the item outlives the view: let go of its events
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_attached) Track(DataContext as ToolCallItem);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StringsProperty) Refresh();
        else if (change.Property == IsWorkingProperty) UpdateStatus();
        else if (change.Property == LayoutProperty)
        {
            UpdateTouch();
            if (_item != null && HidesBoxWhenNarrow(_item.Name)) UpdateBody(); // the phone keeps the box of an edit
        }
        else if (change.Property == IsNarrowProperty && _item != null && HidesBoxWhenNarrow(_item.Name))
        {
            UpdateBody();
        }
    }

    /// <summary>
    /// In the touch layout (pseudo-class <c>:touch</c>) the call is a block of its own and the timeline's dot is not
    /// drawn: the header has a dot of its own, at its start.
    /// </summary>
    private void UpdateTouch()
    {
        var touch = GetValue(LayoutProperty) == ChatLayout.Touch;
        PseudoClasses.Set(":touch", touch);
        StatusDot.IsVisible = touch;
    }

    private void Track(ToolCallItem? item)
    {
        if (ReferenceEquals(item, _item)) return;
        if (_item != null)
        {
            _item.PropertyChanged -= OnItemChanged;
            _item.Children.CollectionChanged -= OnChildrenChanged;
        }
        _item = item;
        if (_item != null)
        {
            _item.PropertyChanged += OnItemChanged;
            _item.Children.CollectionChanged += OnChildrenChanged;
        }
        TrackAgent(_item?.Agent);
        // out of the window, only the events go: Avalonia is taking the tree apart, and coming back draws it again
        if (_attached) Refresh();
    }

    /// <summary>An Agent call's row follows its agent too: the prompt, whether it runs in the background, and its line's state.</summary>
    private void TrackAgent(AgentInfo? agent)
    {
        if (ReferenceEquals(agent, _agent)) return;
        if (_agent != null) _agent.PropertyChanged -= OnAgentChanged;
        _agent = agent;
        if (_agent != null) _agent.PropertyChanged += OnAgentChanged;
    }

    private void OnAgentChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AgentInfo.IsBackground):
                UpdateChildren();
                UpdateBody();
                break;
            case nameof(AgentInfo.Prompt):
                UpdateBody();
                break;
            case nameof(AgentInfo.Status):
            case nameof(AgentInfo.Activity):
            case nameof(AgentInfo.Result):
            case nameof(AgentInfo.Description):
            case nameof(AgentInfo.IsWaitingForPermission):
            case nameof(AgentInfo.Nested):
                UpdateAgentLine();
                break;
        }
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ToolCallItem.Agent):
                TrackAgent(_item?.Agent);
                UpdateChildren();
                UpdateBody();
                break;
            case nameof(ToolCallItem.IsExpanded):
                UpdateChildren();
                break;
            case nameof(ToolCallItem.Name):
            case nameof(ToolCallItem.Summary):
            case nameof(ToolCallItem.Input):
                UpdateHeader();
                UpdateBody();
                break;
            case nameof(ToolCallItem.Status):
                UpdateStatus();
                UpdateBody();
                break;
            case nameof(ToolCallItem.Diff):
            case nameof(ToolCallItem.Todos):
            case nameof(ToolCallItem.ResultText):
            case nameof(ToolCallItem.FullResultText):
            case nameof(ToolCallItem.RejectionReason):
            case nameof(ToolCallItem.IsOutputExpanded):
            case nameof(ToolCallItem.IsInputExpanded):
                UpdateBody();
                break;
        }
    }

    private void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateChildren();

    private void Refresh()
    {
        UpdateHeader();
        UpdateStatus();
        UpdateChildren();
        UpdateBody();
    }

    // ---- the header ----

    private void UpdateHeader()
    {
        var item = _item;
        var name = item?.Name ?? "";
        ToolName.Text = ToolPresentation.McpName(name) is { } mcp ? mcp.Server + " · " + mcp.Tool : name;

        var detail = item?.Summary ?? "";
        var kind = "mono";
        if (name == PlanTool)
        {
            // "Claude's Plan" and its file, as a link, when the call carries the plan; "Plan Mode" when it does not
            var plan = item != null && PlanReview.HasPlan(item.Input);
            ToolName.Text = plan ? Strings.PlanTitle : Strings.PlanModeTool;
            detail = plan ? ToolPresentation.PlanFileName(item!.Input) ?? "" : "";
            kind = "file";
        }
        else if (IsAgentTool(name))
        {
            // "Agent:" with its colon, a Task call too, like the extension's; the description follows in the editor's font
            ToolName.Text = AgentTitle;
        }
        else if (IsFileTool(name))
        {
            kind = "file";
        }
        else if (IsCommand(name))
        {
            // the command itself goes in the box under the name; its description, when it has one, goes here
            detail = Str(item?.Input, "description") ?? "";
            kind = "plain";
        }
        DetailText.Text = ChatText.ClipLines(detail, BoxLines, MaxLineChars);
        DetailText.IsVisible = detail.Length > 0;
        DetailText.Classes.Set("file", kind == "file");
        DetailText.Classes.Set("plain", kind == "plain");

        var range = item == null ? null : ToolPresentation.ReadRange(name, item.Input);
        RangeText.Text = range is { } lines ? Strings.FormatReadRange(lines) : "";
        RangeText.IsVisible = range != null;
        UpdateInteraction();
    }

    private void UpdateStatus()
    {
        var status = _item?.Status ?? ToolStatus.Running;
        // blinks only while the conversation works: a call left without a result in a history, or in a subagent the
        // turn ended without, keeps a still gray dot
        var blinks = status == ToolStatus.Running && GetValue(IsWorkingProperty);
        SetState(Row, status, blinks); // the timeline's dot
        SetState(StatusDot, status, blinks); // the touch layout's, in the header
    }

    /// <summary>
    /// The classes the dots take their color (and their blinking) from. A refused call has its own class, but the styles
    /// paint it red like a failed one: the extension never writes "Rejected".
    /// </summary>
    private static void SetState(Control dot, ToolStatus status, bool blinks)
    {
        dot.Classes.Set("running", blinks);
        dot.Classes.Set("succeeded", status == ToolStatus.Succeeded);
        dot.Classes.Set("failed", status == ToolStatus.Failed);
        dot.Classes.Set("rejected", status == ToolStatus.Rejected);
    }

    /// <summary>
    /// A subagent's steps, behind the arrow; a background agent's are not on the list (only its line and the map tell about it),
    /// like the extension's.
    /// </summary>
    private void UpdateChildren()
    {
        var steps = ShowsSteps(_item);
        Arrow.IsVisible = steps;
        ChildList.IsVisible = steps && _item!.IsExpanded;
        UpdateInteraction();
    }

    private bool ShowsSteps(ToolCallItem? item) => item != null && item.Children.Count > 0 && !RunsInBackground(item);

    /// <summary>An Agent call whose agent runs in the background (by its task line, its result, or the call's own flag).</summary>
    private bool RunsInBackground(ToolCallItem item) =>
        IsAgentTool(item.Name) && (_agent != null ? _agent.IsBackground : RunsInBackground(item.Input));

    /// <summary>The header takes clicks and the keyboard when it opens something: a subagent's steps, or a plan's document.</summary>
    private void UpdateInteraction()
    {
        var item = _item;
        var opens = item != null && (ShowsSteps(item) || IsPlanRow(item));
        Header.IsHitTestVisible = opens;
        Header.Focusable = opens;
        Header.Classes.Set("interactive", opens); // the touch layout gives it a finger's height
    }

    // ---- the body ----

    private void UpdateBody()
    {
        var focused = FocusedContent();
        Body.Children.Clear();
        _agentLine = null;
        var item = _item;
        if (item != null)
        {
            var strings = Strings;
            var name = item.Name;
            var failed = item.Status == ToolStatus.Failed;
            var refused = item.Status == ToolStatus.Rejected;
            var hasDiff = item.Diff != null && item.Diff.Count > 0;
            var hasTodos = !hasDiff && item.Todos != null && item.Todos.Count > 0;

            var subtitle = Subtitle(item, strings);
            if (subtitle != null) Body.Children.Add(Line(subtitle, "ClaudeChatSecondary"));
            // what the user typed when they refused: for a command, it stands for the output
            if (refused && TellsReason(name) && !string.IsNullOrEmpty(item.RejectionReason))
                Body.Children.Add(ReasonLine(item.RejectionReason!, strings));

            // a diff or a list stands for the output, unless the call failed; a refused edit, write or command ran nothing, and
            // says why on the line above; any other refused call shows what the claude answered; a Read that worked keeps quiet,
            // like the extension (the file's lines)
            var quiet = item.Status == ToolStatus.Succeeded && QuietWhenDone(name);
            // a plan has no box: its line tells how it ended, and its document holds its text; a subagent's call shows only its
            // prompt, whatever came back
            var showOutput = name != PlanTool && !IsAgentTool(name)
                && (failed || (!hasDiff && !hasTodos && !quiet && !(refused && TellsReason(name))));
            var result = item.FullResultText ?? item.ResultText;
            var output = showOutput && !string.IsNullOrEmpty(result);

            // the box of an edit or a write goes in a panel under 500 px, like the extension's; the phone keeps it
            var boxHidden = HidesBoxWhenNarrow(name) && GetValue(IsNarrowProperty) && GetValue(LayoutProperty) != ChatLayout.Touch;
            if (boxHidden && (name == "Write" || hasDiff))
            {
                // nothing: the lines above it say what it did
            }
            else if (name == "Write" && WrittenText(item) is { } written)
                Body.Children.Add(ToolBody(TextRow(item, written, input: true, strings)));
            else if (hasDiff)
                Body.Children.Add(ToolBody(ListDiff(item, strings)));
            else if (hasTodos)
                Body.Children.Add(TodoList(item.Todos!));

            var rows = new List<(string Label, Control Content)>();
            // the command: the box shows its first lines, and the request showed it whole
            if (IsCommand(name) && Str(item.Input, "command") is { Length: > 0 } command)
                rows.Add((strings.ToolInput, TextRow(item, command, input: true, strings)));
            // what a subagent was asked: the call's prompt, else its task line's
            if (IsAgentTool(name) && (Str(item.Input, "prompt") ?? _agent?.Prompt) is { Length: > 0 } prompt)
                rows.Add((strings.ToolInput, TextRow(item, prompt, input: true, strings)));
            if (output) rows.Add((strings.ToolOutput, TextRow(item, result!, input: false, strings)));
            if (rows.Count > 0) Body.Children.Add(ToolBody(Rows(rows)));
            if (IsCommand(name) && RunsInBackground(item.Input))
            {
                var note = Line(strings.RunInBackground, "ClaudeChatSecondary");
                note.Classes.Add("background");
                Body.Children.Add(note);
            }
            if (IsAgentTool(name) && _agent is { IsBackground: true })
            {
                _agentLine = AgentLine();
                Body.Children.Add(_agentLine);
                UpdateAgentLine();
            }
        }
        Body.IsVisible = Body.Children.Count > 0;
        if (focused != null) Refocus(focused);
    }

    // ---- the line under a background agent's row ----

    /// <summary>An 8 px dot and a muted .85em text, the whole line at 70 %; <see cref="UpdateAgentLine"/> fills it.</summary>
    private static DockPanel AgentLine()
    {
        var dot = new Ellipse();
        dot.Classes.Add("ClaudeChatAgentLineDot");
        DockPanel.SetDock(dot, Dock.Left);
        var text = new TextBlock();
        text.Classes.Add("ClaudeChatAgentLineText");
        var line = new DockPanel { Children = { dot, text } };
        line.Classes.Add("ClaudeChatAgentLine");
        return line;
    }

    /// <summary>
    /// What the background agent is doing, waits for or how it ended, and what the agents it started do; the dot's color goes
    /// with it: green while it (or an agent it started) works, blue while a request waits, red when it failed or was stopped (or
    /// an agent it started did), gray once it finished. Only the line changes: the prompt above keeps its state and its focus.
    /// </summary>
    private void UpdateAgentLine()
    {
        var agent = _agent;
        if (_agentLine == null || agent == null) return;
        var waiting = agent.IsWaitingForPermission;
        var nested = agent.Nested;
        var error = agent.Status == AgentStatus.Failed ? agent.Result : null;
        var text = (TextBlock)_agentLine.Children[1];
        text.Text = Strings.FormatBackgroundAgent(agent.Status, waiting, agent.Activity, error, agent.Description, nested);

        string state;
        if (waiting || (nested & AgentNesting.Waiting) != 0) state = "waiting";
        else if (agent.Status == AgentStatus.Failed || agent.Status == AgentStatus.Stopped) state = "failed";
        else if ((nested & (AgentNesting.Failed | AgentNesting.Stopped)) != 0) state = "failed";
        else if (agent.Status == AgentStatus.Running || (nested & AgentNesting.Working) != 0) state = "running";
        else state = "idle";
        var dot = _agentLine.Children[0];
        dot.Classes.Set("running", state == "running");
        dot.Classes.Set("waiting", state == "waiting");
        dot.Classes.Set("failed", state == "failed");
    }

    /// <summary>"Reason:" in bold and what the user typed, in the muted line under the name.</summary>
    private static TextBlock ReasonLine(string reason, ChatStrings strings)
    {
        var line = new TextBlock();
        line.Inlines!.Add(new Run(strings.ToolReason) { FontWeight = FontWeight.Bold });
        line.Inlines.Add(new Run(" " + ChatText.Clip(reason, MaxReasonChars)));
        line.Classes.Add("ClaudeChatSecondary");
        return line;
    }

    // ---- a text in the box: cut at 60 px, whole with a click ----

    /// <summary>Whether a text is long enough to open whole: past <see cref="ExpandableChars"/> characters or <see cref="ExpandableLines"/> lines.</summary>
    internal static bool IsExpandable(string text)
    {
        if (text.Length > ExpandableChars) return true;
        var lines = 1;
        var end = text.Length;
        while (end > 0 && (text[end - 1] == '\n' || text[end - 1] == '\r')) end--; // a line break at the end starts no line
        for (var i = 0; i < end; i++)
        {
            if (text[i] == '\n' && ++lines > ExpandableLines) return true;
        }
        return false;
    }

    /// <summary>The text for a row: its first lines while cut (the frame cuts it at 60 px), the text up to the cap when whole.</summary>
    private static string Shown(string text, bool whole)
    {
        if (!whole) return ChatText.ClipLines(text, BoxLines, MaxLineChars);
        return text.Length > MaxExpandedChars ? text.Substring(0, MaxExpandedChars) + "…" : text;
    }

    private const string InputKey = "in";
    private const string OutputKey = "out";

    /// <summary>
    /// What goes in a row of the box: the editor's font at .85em, no wrapping, 4 px inside, at most 60 px with a fade. An
    /// <paramref name="input"/> is the command or the file written; the rest is the call's output, a string on the code
    /// background that scrolls sideways. A long one takes clicks, Enter and Space and opens whole in place; the state is in the
    /// item, because the list recycles its rows.
    /// </summary>
    private Border TextRow(ToolCallItem item, string text, bool input, ChatStrings strings)
    {
        var expandable = IsExpandable(text);
        var whole = expandable && (input ? item.IsInputExpanded : item.IsOutputExpanded);

        TextBlock block = input ? new TextBlock() : new SelectableTextBlock();
        block.Text = Shown(text, whole);
        block.TextWrapping = TextWrapping.NoWrap;
        block.Classes.Add("ClaudeChatToolText");
        Control shown = block;
        if (!input)
        {
            block.Classes.Add("ClaudeChatOutput");
            // the bar is drawn over the text: a line wider than the row gets room under it, so the bar does not cover the click
            var room = new Border { Height = ScrollBarRoom, IsVisible = false };
            var scroller = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = new StackPanel { Children = { block, room } },
            };
            scroller.ScrollChanged += (_, _) => room.IsVisible = scroller.Extent.Width > scroller.Viewport.Width + 0.5;
            var holder = new Border { Child = scroller };
            holder.Classes.Add("ClaudeChatToolResult");
            shown = holder;
        }

        // measured at full height inside a stack, so the lines past 60 px are cut by the frame, not left out by the text
        var content = new Border { Child = new StackPanel { Children = { shown } } };
        content.Classes.Add("ClaudeChatToolContent");
        if (whole) content.Classes.Add("whole");
        if (!expandable) return content;

        content.Classes.Add("expandable");
        content.Background = Brushes.Transparent; // the whole row takes the click, not only the letters
        content.Tag = input ? InputKey : OutputKey;
        content.Focusable = true;
        content.Cursor = new Cursor(StandardCursorType.Hand);
        AutomationProperties.SetName(content, whole ? strings.ToolCutText : strings.ToolShowFullText);
        // the second click of a double click selects a word of the text: only the first one toggles
        var clicks = 1;
        content.AddHandler(InputElement.PointerPressedEvent, (_, e) => clicks = e.ClickCount, Avalonia.Interactivity.RoutingStrategies.Bubble, true);
        content.GotFocus += (_, e) => _focusMethod = e.NavigationMethod;
        content.Tapped += (_, e) =>
        {
            // a drag on the scroll bar is not a click on the text
            if (clicks > 1 || e.Source is Visual source && source.FindAncestorOfType<ScrollBar>(true) != null) return;
            e.Handled = true;
            Toggle(item, input);
        };
        content.KeyDown += (_, e) =>
        {
            if ((e.Key != Key.Enter && e.Key != Key.Space) || e.KeyModifiers != KeyModifiers.None) return;
            e.Handled = true;
            Toggle(item, input);
        };
        return content;
    }

    private static void Toggle(ToolCallItem item, bool input)
    {
        if (input) item.IsInputExpanded = !item.IsInputExpanded;
        else item.IsOutputExpanded = !item.IsOutputExpanded;
    }

    /// <summary>
    /// The diff of the list, 200 px at most, under a layer that opens the Diff View over the whole view with the whole change; the
    /// layer keeps the focus across a redraw, like a text of the box.
    /// </summary>
    private DiffView ListDiff(ToolCallItem item, ChatStrings strings)
    {
        var diff = new DiffView(item.Diff!, strings);
        diff.ExpandRequested += (_, _) => this.FindAncestorOfType<ClaudeChatView>()?.ShowDiff(item);
        var layer = diff.ExpandControl!;
        layer.Tag = DiffKey;
        layer.GotFocus += (_, e) => _focusMethod = e.NavigationMethod;
        return diff;
    }

    /// <summary>The key (IN, OUT or the diff) of the part of the box that has the focus, or null when none of them does.</summary>
    private string? FocusedContent()
    {
        if (Body.Children.Count == 0 || TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Visual focus
            || !Body.IsVisualAncestorOf(focus)) return null;
        foreach (var content in Body.GetVisualDescendants().OfType<Border>())
        {
            if (content.Tag is string key && content.Focusable && (ReferenceEquals(content, focus) || content.IsVisualAncestorOf(focus)))
                return key;
        }
        return null;
    }

    /// <summary>The box was drawn again under the keyboard (or the finger): the same part keeps the focus.</summary>
    private void Refocus(string key)
    {
        foreach (var content in Body.GetVisualDescendants().OfType<Border>())
        {
            if (Equals(content.Tag, key) && content.Focusable)
            {
                content.Focus(_focusMethod);
                return;
            }
        }
    }

    /// <summary>The line under the name: what an edit changed, how long a written file is.</summary>
    private static string? Subtitle(ToolCallItem item, ChatStrings strings)
    {
        // a refusal is an error result for the extension: "failed", and its reason on the next line; a call cut short, which
        // got no result, did not fail
        var failed = item.Status == ToolStatus.Failed
            || (item.Status == ToolStatus.Rejected && (item.FullResultText ?? item.ResultText) != null);
        switch (item.Name)
        {
            case "Edit":
            case "MultiEdit":
                if (failed) return strings.EditFailed;
                return ToolPresentation.EditLineChange(item.Name, item.Input) is { } change ? strings.FormatEditChange(change) : null;
            case "Write":
                if (failed) return strings.WriteFailed;
                if (ToolPresentation.WriteLineCount(item.Name, item.Input) is { } lines) return strings.FormatLines(lines);
                // a content that is not a text, which the box shows as JSON
                return NonTextContent(item.Input) != null ? strings.WriteSucceeded : null;
            case PlanTool:
                // nothing while it waits; a refusal comes back as an error (or a rejection): the claude stays in plan mode
                return item.Status switch
                {
                    ToolStatus.Succeeded => strings.PlanApproved,
                    ToolStatus.Failed or ToolStatus.Rejected => strings.PlanStayedInPlanMode,
                    _ => null,
                };
            default:
                return null;
        }
    }

    /// <summary>Tools whose output, when they work, is not worth showing in the list (a subagent's call never shows one).</summary>
    private static bool QuietWhenDone(string toolName) => toolName == "Read";

    private static bool IsFileTool(string toolName) =>
        toolName == "Read" || toolName == "Edit" || toolName == "Write" || toolName == "MultiEdit" || toolName == "NotebookEdit";

    private static bool IsCommand(string toolName) => toolName == "Bash" || toolName == "PowerShell";

    /// <summary>The calls that start a subagent: Agent, and Task (its older name), drawn alike.</summary>
    private static bool IsAgentTool(string toolName) => toolName == "Agent" || toolName == "Task";

    /// <summary>The tools whose box goes in a desktop panel narrower than <see cref="ClaudeChatView.NarrowWidth"/>: Edit and Write.</summary>
    private static bool HidesBoxWhenNarrow(string toolName) => toolName == "Edit" || toolName == "Write";

    /// <summary>The content of a Write that is not a text (the claude sent an object, say), or null.</summary>
    private static JsonElement? NonTextContent(JsonElement input) =>
        input.ValueKind == JsonValueKind.Object && input.TryGetProperty("content", out var content) && content.ValueKind != JsonValueKind.String
            ? content
            : (JsonElement?)null;

    /// <summary>The tools whose refusal says what the user typed on a line of its own: Edit, Write and the commands.</summary>
    private static bool TellsReason(string toolName) => toolName == "Edit" || toolName == "Write" || IsCommand(toolName);

    /// <summary>A command started with <c>run_in_background: true</c>.</summary>
    private static bool RunsInBackground(JsonElement input) =>
        input.ValueKind == JsonValueKind.Object
        && input.TryGetProperty("run_in_background", out var value)
        && value.ValueKind == JsonValueKind.True;

    /// <summary>
    /// A diff in a tool's box, cut at 200 px like the extension's; the permission prompt draws it <paramref name="whole"/>, inside
    /// its own scrolling area, with no 200 px cut and no layer to open the Diff View (the list's own goes through <see cref="ListDiff"/>).
    /// </summary>
    internal static Control DiffBox(IReadOnlyList<DiffLine> diff, ChatStrings strings, bool whole = false) =>
        ToolBody(new DiffView(diff, strings, whole));

    /// <summary>The box under a tool: a thin frame, 5 px corners.</summary>
    private static Border ToolBody(Control child)
    {
        var box = new Border { Child = child };
        box.Classes.Add("ClaudeChatToolBody");
        return box;
    }

    /// <summary>The rows of the box, 4 px inside, a line between them, each label (IN, OUT) in a column as wide as the widest.</summary>
    private static Control Rows(List<(string Label, Control Content)> rows)
    {
        var stack = new StackPanel();
        Grid.SetIsSharedSizeScope(stack, true);
        for (var i = 0; i < rows.Count; i++)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto) { SharedSizeGroup = "ClaudeChatToolLabel" });
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var label = Line(rows[i].Label, "ClaudeChatToolLabel");
            label.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
            grid.Children.Add(label);
            Grid.SetColumn(rows[i].Content, 1);
            grid.Children.Add(rows[i].Content);

            var frame = new Border { Child = grid };
            frame.Classes.Add("ClaudeChatToolRow");
            if (i > 0) frame.Classes.Add("next");
            stack.Children.Add(frame);
        }
        return stack;
    }

    /// <summary>
    /// The file written, whole; the box cuts it. A content that is not a text, as indented JSON, like the extension's. Without the
    /// input to read (an item built by hand), the diff's added lines.
    /// </summary>
    private static string? WrittenText(ToolCallItem item)
    {
        var content = Str(item.Input, "content");
        if (content != null) return content;
        if (NonTextContent(item.Input) is { } other)
            return JsonSerializer.Serialize(other, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // shown, not sent: accents stay as they are
            });
        if (item.Diff == null || item.Diff.Count == 0) return null;
        var lines = new List<string>();
        foreach (var line in item.Diff)
        {
            if (line.Kind == DiffKind.Added) lines.Add(ChatText.Clip(line.Text, MaxLineChars));
        }
        return string.Join("\n", lines);
    }

    private static Control TodoList(IReadOnlyList<TodoEntry> todos)
    {
        var list = new StackPanel();
        list.Classes.Add("ClaudeChatTodos");
        foreach (var todo in todos)
        {
            string mark, state;
            switch (todo.Status)
            {
                case TodoStatus.Completed:
                    mark = "✓";
                    state = "completed";
                    break;
                case TodoStatus.InProgress:
                    mark = "✽";
                    state = "inProgress";
                    break;
                default:
                    mark = "";
                    state = "pending";
                    break;
            }
            var box = new Border { Child = Line(mark, "ClaudeChatTodoMark") };
            box.Classes.Add("ClaudeChatTodoBox");
            box.Classes.Add(state);
            var textBlock = Line(ChatText.Clip(todo.Text, MaxLineChars), "ClaudeChatTodo");
            textBlock.Classes.Add(state);
            var row = new DockPanel();
            row.Classes.Add("ClaudeChatTodoRow");
            row.Classes.Add(state);
            DockPanel.SetDock(box, Dock.Left);
            row.Children.Add(box);
            row.Children.Add(textBlock);
            list.Children.Add(row);
        }
        return list;
    }

    private static TextBlock Line(string text, string style)
    {
        var line = new TextBlock { Text = text };
        line.Classes.Add(style);
        return line;
    }

    private static string? Str(JsonElement? input, string name) =>
        input is { ValueKind: JsonValueKind.Object } obj
        && obj.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
