using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// One tool call, like the extension draws it: a dot (its color tells the state), the tool's name in bold and what it
/// works on (a file in the link color, a command's description, the lines read); under it a line such as "Modified"
/// or "47 lines", and a box with the diff, the start of the file written, or the command (IN) and its output (OUT,
/// that scrolls sideways); "Run in background" under a command started so; for a subagent, its items behind an arrow. The dot
/// of a running call blinks only while the conversation works. Every text in the box is cut at 60 px with a fade; a long one
/// (<see cref="ExpandableChars"/>, <see cref="ExpandableLines"/>) opens whole in place with a click, Enter or Space, and a
/// huge one stops at <see cref="MaxExpandedChars"/>, so a huge result never slows the list. A refused tool call (any tool)
/// says what the user typed on a line of its own. An edit reads its net change ("Added 2 lines", "Removed 1 line", "Modified"), a
/// write its lines ("Write succeeded" for a content that is not a text); a click on the diff, Enter or Space opens the Diff View over
/// the whole view. In a desktop panel narrower than <see cref="ClaudeChatView.NarrowWidth"/> the box of an edit or a write goes,
/// like the extension's; the touch layout keeps it.
/// A plan (<c>ExitPlanMode</c>) reads "Claude’s Plan" and its file, then whether the user approved it, with no box; a click on
/// its header opens the plan again in the document over the list.
/// A subagent's call (Agent or Task) reads "Agent:" and its description, and its box holds only the prompt (IN), never an output;
/// a background agent's row adds a line with a dot that tells what it is doing, that a request of its waits below, or how it
/// ended, and its steps stay off the list (the map has them).
/// A skill reads its name (without the "/") in bold and "skill" in the editor's font, with no body; a TaskOutput reads its name and
/// <c>task: "id"</c>, with only its output (without ANSI) in the box.
/// The command's row (IN) of a Bash or PowerShell call has a button at its top right, shown while the pointer is over the row (always
/// in the touch layout), that copies the whole command through <see cref="ChatClipboard"/>; for 2 s after a copy its icon is a check.
/// While such a command runs and the host listens to <see cref="ChatConversation.TaskOutputRequested"/>, the row asks for what it has
/// printed when it comes on screen and then once a second while it is on screen; what comes back shows in a second box under
/// its card with one OUT row, the text scrolling at 240 px and following its end unless the person scrolled up, and a line
/// that says how much there is when the claude kept only its end. The box goes with the result, or when the call stops running.
/// A Glob or a Grep that worked reads only a line under its name ("Found 3 files", "12 lines of output"), with no box; with output, a
/// click, Enter or Space on the line opens the whole output under it. A Web Fetch that worked has only "Fetched from" and its url in the
/// box (what it fetched stays out); a Web Search says the domains it was allowed or kept away from.
/// An MCP tool reads "Server [tool]" in bold (the server made readable: "Claude AI Gmail"), with its first known input field after
/// it and only an OUT box. Claude in Chrome reads "Claude in Chrome [action]" and what the action does; what worked says so in a line
/// ("Navigation completed"), a computer action also shows its screenshot as a thumbnail that opens in the preview, a read shows its text
/// without the "Tab Context:" section, and an error or a script shows its input as JSON (IN) and the result (OUT).
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
    private const int MaxReasonChars = ToolErrorText.MaxChars;

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

    /// <summary>The Claude in Chrome actions whose result shows in an OUT box (without its "Tab Context:" section).</summary>
    private static readonly string[] ChromeOutActions =
        { "find", "gif_creator", "read_console_messages", "read_network_requests", "read_page", "get_page_text", "update_plan" };

    /// <summary>A screenshot of Claude in Chrome shows 80 px high and at most 160 px wide.</summary>
    internal const double ScreenshotHeight = 80;
    internal const double ScreenshotMaxWidth = 160;

    /// <summary>Decoded once per screenshot, off the UI thread.</summary>
    private static readonly ThumbnailCache Screenshots = ThumbnailCache.Cover(ScreenshotMaxWidth, ScreenshotHeight);

    /// <summary>The Artifact tool: its row reads "Artifact" and the file, then how it went and a link the host opens.</summary>
    private const string ArtifactTool = "Artifact";

    /// <summary>The notebook edit: "Edit Notebook Cell", the notebook and the cell, its state, and a box with the new source.</summary>
    private const string NotebookTool = "NotebookEdit";

    /// <summary>The Skill tool, whose row reads the skill's name in bold and this word after it, in the editor's font.</summary>
    private const string SkillTool = "Skill";
    private const string SkillWord = "skill";

    private static readonly StyledProperty<ChatStrings> StringsProperty =
        ClaudeChatView.StringsProperty.AddOwner<ToolCallView>();

    private static readonly StyledProperty<bool> IsWorkingProperty =
        ClaudeChatView.IsWorkingProperty.AddOwner<ToolCallView>();

    private static readonly StyledProperty<ChatLayout> LayoutProperty =
        ClaudeChatView.LayoutProperty.AddOwner<ToolCallView>();

    private static readonly StyledProperty<bool> IsNarrowProperty =
        ClaudeChatView.IsNarrowProperty.AddOwner<ToolCallView>();

    /// <summary>How long the check shows on the command's copy button after a copy.</summary>
    private static readonly TimeSpan CopiedFor = TimeSpan.FromSeconds(2);

    /// <summary>The key of the layer over the diff, among the box's focusable parts.</summary>
    private const string DiffKey = "diff";

    private ToolCallItem? _item;
    private AgentInfo? _agent; // the agent an Agent call started, while the row is in the window
    private DockPanel? _agentLine; // the line under a background agent's row, while it has one
    private bool _attached;
    private readonly DispatcherTimer _copyTimer = new DispatcherTimer { Interval = CopiedFor };
    private Button? _copyButton; // the copy button on the command's row, while the row has one
    private Path? _copyIcon;
    private bool _copied; // the command was copied a moment ago: the button shows a check (it is the view's, the item has no use for it)
    private int _generation; // counts the redraws of the body: a screenshot decoded after another redraw is for a box no longer there
    private NavigationMethod _focusMethod; // how a text of the box got the focus, so a redraw gives it back the same way (a key keeps the ring)

    /// <summary>How often a running command's row asks the host for what the command has printed.</summary>
    private static readonly TimeSpan AskOutputEvery = TimeSpan.FromSeconds(1);

    /// <summary>The box's text follows its end on an update while the person is closer to the end than this.</summary>
    private const double FollowEndWithin = 4;

    private readonly DispatcherTimer _outputTimer = new DispatcherTimer { Interval = AskOutputEvery };
    private bool _askingForOutput; // the row asks for its command's output: in the window, a running command, a host that listens
    // what hides the row while it is in the tree: its parent, which the touch list hides while the row is off screen, and for a
    // subagent's step every ancestor up to the parent of the outermost row it is in (the folded steps' list is one of them)
    private readonly List<Visual> _containers = new List<Visual>();
    private Border? _liveBox; // the box with what the running command printed, while it is in the tree
    private ToolCallItem? _liveBoxFor; // the call the box was built for: a row given to another call builds its own
    private SelectableTextBlock? _liveText;
    private TextBlock? _liveEnd;
    private bool _liveFollowsEnd = true; // the box's text was at its end (or the person brought it back there)

    public ToolCallView()
    {
        InitializeComponent();
        Header.Click += OnHeaderClick;
        // tunnelling, so a click on the path never reaches the toggle that the header is
        Header.AddHandler(PointerPressedEvent, OnHeaderPointerPressed, RoutingStrategies.Tunnel);
        Header.AddHandler(PointerReleasedEvent, OnHeaderPointerReleased, RoutingStrategies.Tunnel);
        _copyTimer.Tick += (_, _) => EndCopied();
        _outputTimer.Tick += (_, _) => AskForOutput();
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

    /// <summary>
    /// While the row's path is a link (see <see cref="UpdateInteraction"/>) a press on it is the start of a click, and a press on
    /// the rest of the header, which is not a toggle then, does nothing.
    /// </summary>
    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pathPressed = false; // a press that began elsewhere (and a release lost to a scroll) never pairs with a later one
        if (!_pathLink) return;
        if (e.Source is Visual source && IsPathTarget(source))
        {
            _pathPressed = e.GetCurrentPoint(Header).Properties.IsLeftButtonPressed;
            e.Handled = true;
        }
        else if (!OpensSomething())
        {
            e.Handled = true;
        }
    }

    private void OnHeaderPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_pathLink) return;
        var pressed = _pathPressed;
        _pathPressed = false;
        if (pressed && e.Source is Visual source && IsPathTarget(source) && ReleasedOnTarget(e) && _item != null && PathOf(_item) is { } path)
        {
            e.Handled = true;
            _host?.ActivatePath(path, null);
        }
        else if (pressed || !OpensSomething())
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// What takes the click for the path: its text, and in the touch layout (where the header is a finger tall and a tap is not
    /// aimed at the text) the whole header while it has no other action; a header that opens something keeps that for the rest of it.
    /// </summary>
    private bool IsPathTarget(Visual source) =>
        IsInDetail(source) || (GetValue(LayoutProperty) == ChatLayout.Touch && !OpensSomething());

    /// <summary>The pointer is still over the path (or the header, where that takes the tap) when it is let go: dragging off cancels, as for a button.</summary>
    private bool ReleasedOnTarget(PointerReleasedEventArgs e)
    {
        var target = GetValue(LayoutProperty) == ChatLayout.Touch && !OpensSomething() ? (Control)Header : DetailText;
        var point = e.GetPosition(target);
        return point.X >= 0 && point.Y >= 0 && point.X <= target.Bounds.Width && point.Y <= target.Bounds.Height;
    }

    private bool IsInDetail(Visual source) => ReferenceEquals(source, DetailText) || DetailText.IsVisualAncestorOf(source);

    /// <summary>
    /// The file a Read, Edit, Write or notebook call works on, as the call wrote it (the header shows it relative to the working
    /// folder, but the host resolves it itself); null for any other call, or one without a path.
    /// </summary>
    private static string? PathOf(ToolCallItem item)
    {
        if (!IsFileTool(item.Name)) return null;
        var path = Str(item.Input, item.Name == NotebookTool ? "notebook_path" : "file_path");
        return string.IsNullOrEmpty(path) ? null : path;
    }

    /// <summary>An <c>ExitPlanMode</c> row with the plan's text, which its header opens.</summary>
    private static bool IsPlanRow(ToolCallItem item) => item.Name == PlanTool && PlanReview.HasPlan(item.Input);

    private ChatStrings Strings => GetValue(StringsProperty) ?? ChatStrings.English;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        ListenToContainers();
        Track(DataContext as ToolCallItem);
        TrackHost();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        foreach (var container in _containers) container.PropertyChanged -= OnContainerChanged;
        _containers.Clear();
        _attached = false;
        Track(null); // the item outlives the view: let go of its events
        TrackHost();
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
        EndCopied(); // a check belongs to the item it was copied from: the list reuses the view, and out of the window nothing waits
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
        // a row the touch list gives to another call asks about the new command at once, as a row that comes into the window does,
        // and builds a box of its own (the old one's scrolling was the other command's)
        _askingForOutput = false;
        _outputTimer.Stop();
        if (_item != null && !ReferenceEquals(_item, _liveBoxFor)) DropLiveBox();
        TrackAgent(_item?.Agent);
        TrackHost();
        // out of the window, only the events go: Avalonia is taking the tree apart, and coming back draws it again
        if (_attached) Refresh();
    }

    private ChatConversation? _host; // the conversation an Artifact or file row listens to, while the row is in the window
    private bool _pathLink; // the header's path is a link: the host listens to PathActivated and the call names a file
    private bool _pathPressed; // a press on the path is waiting for its release

    private static readonly Cursor PlainCursor = new Cursor(StandardCursorType.Arrow);

    /// <summary>
    /// An Artifact row shows its link only while the host listens to <see cref="ChatConversation.LinkRequested"/>, and the path of
    /// a file row is a link only while it listens to <see cref="ChatConversation.PathActivated"/>, and a command's row asks for its
    /// output only while it listens to <see cref="ChatConversation.TaskOutputRequested"/>, so those rows follow the conversation's host
    /// handlers (as the rows under a reply do) while they are in the window; any other row listens to nothing.
    /// </summary>
    private void TrackHost()
    {
        var follows = _item != null && (_item.Name == ArtifactTool || IsFileTool(_item.Name) || IsCommand(_item.Name));
        var wanted = _attached && follows ? this.FindAncestorOfType<ClaudeChatView>()?.Conversation : null;
        if (!ReferenceEquals(wanted, _host))
        {
            if (_host != null) _host.HostHandlersChanged -= OnHostHandlersChanged;
            _host = wanted;
            if (_host != null) _host.HostHandlersChanged += OnHostHandlersChanged;
            UpdateInteraction();
        }
        UpdateAsking();
    }

    private void OnHostHandlersChanged(object? sender, EventArgs e)
    {
        if (_item?.Name == ArtifactTool) UpdateBody();
        UpdateInteraction();
        UpdateAsking();
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
                TrackHost();
                UpdateHeader();
                UpdateBody();
                UpdateLiveOutput();
                break;
            case nameof(ToolCallItem.Summary):
            case nameof(ToolCallItem.Input):
                UpdateHeader();
                UpdateBody();
                break;
            case nameof(ToolCallItem.Status):
                UpdateStatus();
                UpdateBody();
                UpdateAsking();
                UpdateLiveOutput();
                break;
            case nameof(ToolCallItem.LiveOutput):
            case nameof(ToolCallItem.LiveOutputBytes):
            case nameof(ToolCallItem.LiveOutputTruncated):
                UpdateLiveOutput();
                break;
            case nameof(ToolCallItem.Diff):
            case nameof(ToolCallItem.Todos):
            case nameof(ToolCallItem.ResultText):
            case nameof(ToolCallItem.FullResultText):
            case nameof(ToolCallItem.ResultImages):
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
        UpdateLiveOutput();
    }

    // ---- the header ----

    private void UpdateHeader()
    {
        var item = _item;
        var name = item?.Name ?? "";
        ToolName.Text = TitleOf(name);

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
            ToolName.Text = Strings.AgentToolTitle;
        }
        else if (name == SkillTool)
        {
            // the skill's name, bold, then "skill" in mono; a call whose input has no skill yet reads only the tool's name
            if (detail.Length > 0) ToolName.Text = detail;
            detail = detail.Length > 0 ? SkillWord : "";
        }
        else if (name == "WebFetch" || name == "WebSearch")
        {
            ToolName.Text = name == "WebFetch" ? Strings.WebFetchToolTitle : Strings.WebSearchToolTitle;
        }
        else if (name == "SandboxNetworkAccess")
        {
            ToolName.Text = Strings.NetworkAccessToolTitle; // the host follows, in the editor's font
        }
        else if (name == ArtifactTool)
        {
            ToolName.Text = Strings.ArtifactToolTitle; // the file as the claude gave it, in the editor's font
        }
        else if (name == NotebookTool)
        {
            // "Edit Notebook Cell", then the file's name and ":cell" as a link; a call without a path reads only the name
            ToolName.Text = Strings.NotebookEditTitle;
            detail = item != null && ToolPresentation.NotebookTarget(item.Input) is { } target ? target.File + target.Cell : "";
            kind = "file";
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
        // a screen reader hears the tool by the name the row shows
        AutomationProperties.SetName(this, Strings.FormatClaudeTool(ToolName.Text ?? name));
        DetailText.Text = ChatText.ClipLines(detail, BoxLines, MaxLineChars);
        DetailText.IsVisible = detail.Length > 0;
        DetailText.Classes.Set("file", kind == "file");
        DetailText.Classes.Set("plain", kind == "plain");

        var range = item == null ? null : ToolPresentation.ReadRange(name, item.Input);
        RangeText.Text = range is { } lines ? Strings.FormatReadRange(lines) : "";
        RangeText.IsVisible = range != null;
        UpdateInteraction();
    }

    /// <summary>
    /// The name in bold: the tool's own, or for an MCP tool "Server [tool]" with the server made readable ("Claude AI Gmail
    /// [search]"), and for Claude in Chrome "Claude in Chrome [action]".
    /// </summary>
    private string TitleOf(string name)
    {
        if (ToolPresentation.ChromeAction(name) is { } action) return Strings.ChromeToolTitle + " [" + action + "]";
        return ToolPresentation.McpName(name) is { } mcp ? ToolPresentation.HumanizeServer(mcp.Server) + " [" + mcp.Tool + "]" : name;
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
        // the path of a file call, when the host opens files: the header takes the click for it alone (the toggle stays off)
        _pathLink = item != null && _host?.CanActivatePaths == true && PathOf(item) != null && DetailText.IsVisible;
        _pathPressed &= _pathLink;
        Header.IsHitTestVisible = opens || _pathLink;
        Header.Focusable = opens;
        Header.Classes.Set("interactive", opens || _pathLink); // the touch layout gives it a finger's height
        DetailText.Classes.Set("pathlink", _pathLink);
        // the header's own hand would cover all of it; the pointer is a hand over the path only
        if (_pathLink && !opens) Header.Cursor = PlainCursor;
        else Header.ClearValue(CursorProperty);
    }

    private bool OpensSomething() => _item != null && (ShowsSteps(_item) || IsPlanRow(_item));

    // ---- the body ----

    private void UpdateBody()
    {
        // a short text drawn again that has not been measured yet still waits for the focus it had
        var focused = FocusedContent() ?? _awaitingFocus?.Content.Tag as string;
        _awaitingFocus = null;
        _shortCells.Clear();
        _generation++;
        Body.Children.Clear();
        _agentLine = null;
        _copyButton = null;
        _copyIcon = null;
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
            var result = item.FullResultText ?? item.ResultText;
            // a failed or refused call shows its reason whole and cleaned (null: the claude's standard sentence, nothing to say)
            var isError = failed || refused;
            if (isError && result != null) result = ToolErrorText.Clean(name, result);
            // what the user typed when they refused goes on a line of its own, unless it is too long for one or the claude wrapped it
            var reasonLine = refused && !string.IsNullOrEmpty(item.RejectionReason) && item.RejectionReason!.Length <= MaxReasonChars
                && (item.FullResultText ?? item.ResultText ?? "").IndexOf("<tool_use_error>", StringComparison.Ordinal) < 0;
            // Claude in Chrome: what worked says so in a line (a computer action also shows its screenshot, a read its text); an
            // error and a script show the generic body, with the input as JSON
            var chrome = ToolPresentation.ChromeAction(name);
            var chromeDone = chrome != null && item.Status == ToolStatus.Succeeded && ChromeHasOwnBody(chrome, strings);
            string? chromeOut = null;
            if (chromeDone)
            {
                if (chrome == "computer") subtitle = strings.ChromeActionCompleted;
                else if (Array.IndexOf(ChromeOutActions, chrome) >= 0
                         && ToolPresentation.WithoutTabContext(result ?? "") is { Length: > 0 } text) chromeOut = text;
                else subtitle = strings.ChromeLine(chrome!);
            }
            var opensOutput = CountsOutput(name) && subtitle != null && !string.IsNullOrEmpty(result);
            if (subtitle != null)
                Body.Children.Add(opensOutput ? OutputLine(item, subtitle, strings) : Line(subtitle, "ClaudeChatSecondary"));
            // an artifact that worked reads how, with its link; any other result of it (an error, two links, an unknown start) is the generic OUT row
            var artifact = name == ArtifactTool && item.Status == ToolStatus.Succeeded ? ToolPresentation.ArtifactResult(result) : null;
            if (artifact is { } made) Body.Children.Add(ArtifactLine(made, strings));
            if (name == NotebookTool)
            {
                Body.Children.Add(Line(NotebookState(item.Status, strings), "ClaudeChatSecondary"));
                Body.Children.Add(NotebookBox(Str(item.Input, "new_source"), strings));
            }
            if (chromeDone && chrome == "computer" && item.ResultImages is { Count: > 0 } screens && Screenshot(screens[0], strings) is { } screenshot)
                Body.Children.Add(screenshot);
            // the output of a Glob or a Grep, opened from its line
            if (opensOutput && item.IsOutputExpanded)
                Body.Children.Add(ToolBody(TextRow(item, result!, input: false, strings, interactive: false)));
            // what the user typed when they refused: for a command, it stands for the output
            if (reasonLine)
                Body.Children.Add(ReasonLine(item.RejectionReason!, strings));

            // a diff or a list stands for the output, unless the call failed; a refused edit, write or command ran nothing, and
            // says why on the line above; any other refused call shows what the claude answered; a Read that worked keeps quiet,
            // like the extension (the file's lines)
            var quiet = item.Status == ToolStatus.Succeeded && QuietWhenDone(name);
            // a plan has no box: its line tells how it ended, and its document holds its text; a subagent's call shows only its
            // prompt, whatever came back
            // a skill's row has no body at all: neither its arguments nor what it answered
            // an artifact's line and a notebook's box say it all, with no IN or OUT
            var showOutput = name != PlanTool && name != SkillTool && !IsAgentTool(name) && !chromeDone && artifact == null && name != NotebookTool
                && (failed || (refused && !reasonLine) || (!hasDiff && !hasTodos && !quiet && !reasonLine));
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
            else if (name == "WebFetch" && item.Status == ToolStatus.Succeeded && Str(item.Input, "url") is { Length: > 0 } url)
                Body.Children.Add(ToolBody(FetchedFrom(url, strings)));

            var rows = new List<(string Label, Control Content, bool Copyable)>();
            // the command: the box shows its first lines, and the request showed it whole
            if (IsCommand(name) && Str(item.Input, "command") is { Length: > 0 } command)
                rows.Add((strings.ToolInput, CommandCell(item, command, strings), true));
            // what a subagent was asked: the call's prompt, else its task line's
            if (IsAgentTool(name) && (Str(item.Input, "prompt") ?? _agent?.Prompt) is { Length: > 0 } prompt)
                rows.Add((strings.ToolInput, TextRow(item, prompt, input: true, strings), false));
            // a Chrome call that failed, or ran a script: the input as JSON, then what came back
            if (chrome != null && (failed || chrome == "javascript_tool") && InputJson(item.Input) is { Length: > 0 } json)
                rows.Add((strings.ToolInput, TextRow(item, json, input: true, strings), false));
            if (output) rows.Add((strings.ToolOutput, TextRow(item, result!, input: false, strings, error: isError), false));
            else if (chromeOut != null) rows.Add((strings.ToolOutput, TextRow(item, chromeOut, input: false, strings), false));
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
        UpdateCopyState();
        // a short text is a stop only once it is laid out: its first measure gives it the focus back if its row still cuts it
        if (focused != null && !Refocus(focused))
            _awaitingFocus = _shortCells.FirstOrDefault(cell => Equals(cell.Content.Tag, focused));
    }

    // ---- Claude in Chrome ----

    private const string ScreenshotKey = "screenshot";

    /// <summary>Whether a successful Claude in Chrome action has a body of its own: a line, a screenshot or an OUT box (a script keeps the generic one).</summary>
    private static bool ChromeHasOwnBody(string action, ChatStrings strings) =>
        action != "javascript_tool" && (action == "computer" || strings.ChromeLine(action) != null);

    /// <summary>The call's input as indented JSON, like the file a Write got that is not a text; null without an object to show.</summary>
    private static string? InputJson(JsonElement input) =>
        input.ValueKind == JsonValueKind.Object
            ? JsonSerializer.Serialize(input, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            })
            : null;

    /// <summary>
    /// The thumbnail of a screenshot, 8 px under the line: 80 px high, as wide as its proportion gives up to 160 px, in a 1 px frame
    /// with 8 px corners (the thumbnails of a message's images). A click, Enter or Space shows the image over the view. Null for an
    /// image whose size cannot be read.
    /// </summary>
    private Control? Screenshot(ChatImage image, ChatStrings strings)
    {
        if (image.Width <= 0 || image.Height <= 0) return null;
        var picture = new Image
        {
            // the size is known before the picture, so the list does not move when it comes
            Width = Math.Min(ScreenshotMaxWidth, ScreenshotHeight * image.Width / image.Height),
            Height = ScreenshotHeight,
            Stretch = Stretch.UniformToFill,
        };
        var decoding = Screenshots.Load(image);
        if (decoding.IsCompleted) picture.Source = decoding.Status == TaskStatus.RanToCompletion ? decoding.Result : null;
        else
        {
            var generation = _generation;
            decoding.ContinueWith(done => Dispatcher.UIThread.Post(() =>
            {
                if (generation == _generation && done.Status == TaskStatus.RanToCompletion) picture.Source = done.Result;
            }), TaskScheduler.Default);
        }
        var clip = new Border { Child = picture };
        clip.Classes.Add("ClaudeChatMessageImageClip");
        var name = strings.ChromeScreenshot;
        var button = new Button { Content = clip, Tag = ScreenshotKey, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        button.Classes.Add("ClaudeChatButton");
        button.Classes.Add("ClaudeChatMessageImage");
        AutomationProperties.SetName(button, name);
        ToolTip.SetTip(button, name);
        button.GotFocus += (_, e) => _focusMethod = e.NavigationMethod;
        button.Click += (_, _) => this.FindAncestorOfType<ClaudeChatView>()?.ShowPreview(image, name);
        return button;
    }

    // ---- the Artifact line and the notebook edit's box ----

    private const string ArtifactKey = "artifact";

    /// <summary>
    /// "Created —", "Opened" or "Published" at half strength; with a host that opens links, after the dash a link "Open artifact ↗"
    /// (a finger's height in the touch layout) that asks the host to open the artifact's url. Without one, only the word.
    /// </summary>
    private Control ArtifactLine(ArtifactLink made, ChatStrings strings)
    {
        var conversation = this.FindAncestorOfType<ClaudeChatView>()?.Conversation;
        var link = conversation?.CanOpenLinks == true;
        var word = made.Outcome switch
        {
            ArtifactOutcome.Created => strings.ArtifactCreated,
            ArtifactOutcome.Opened => strings.ArtifactOpened,
            _ => strings.ArtifactPublished,
        };
        var line = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
        line.Classes.Add("ClaudeChatArtifactLine");
        var text = Line(link ? word + " —" : word, "ClaudeChatArtifactText");
        line.Children.Add(text);
        if (link)
        {
            var label = Line(strings.ArtifactOpenLink, "ClaudeChatArtifactLinkText");
            var button = new Button { Content = label, Tag = ArtifactKey };
            button.Classes.Add("ClaudeChatButton");
            button.Classes.Add("ClaudeChatArtifactLink");
            AutomationProperties.SetName(button, strings.ArtifactOpenLink);
            button.GotFocus += (_, e) => _focusMethod = e.NavigationMethod;
            var url = made.Url;
            button.Click += (_, _) => conversation!.RequestLink(url);
            line.Children.Add(button);
        }
        AutomationProperties.SetName(line, word);
        return line;
    }

    /// <summary>"Pending" without a result yet, "Success" once it worked, "Failed" for an error or a refusal.</summary>
    private static string NotebookState(ToolStatus status, ChatStrings strings) => status switch
    {
        ToolStatus.Succeeded => strings.NotebookSuccess,
        ToolStatus.Failed or ToolStatus.Rejected => strings.NotebookFailed,
        _ => strings.NotebookPending,
    };

    /// <summary>The cell's new source whole (up to <see cref="MaxExpandedChars"/>), in a thin frame; "[No content]" when the call has no new source.</summary>
    private static Border NotebookBox(string? source, ChatStrings strings)
    {
        var block = new SelectableTextBlock
        {
            // "[No content]" only without the field; an empty source is an empty box, like the extension's
            Text = source == null ? strings.NotebookNoContent : Shown(source, whole: true),
            TextWrapping = TextWrapping.Wrap, // unclipped, with no scroll bar: a long line wraps and nothing is lost
        };
        block.Classes.Add("ClaudeChatToolText");
        block.Classes.Add("ClaudeChatNotebookText");
        var box = new Border { Child = block };
        box.Classes.Add("ClaudeChatNotebookBox");
        return box;
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

    // ---- the line under a Glob or a Grep, and the box of a Web Fetch ----

    private const string SummaryKey = "summary";

    /// <summary>
    /// The line of a Glob or Grep with output ("Found 3 files"): the secondary line, taking clicks, Enter and Space to open the
    /// output under it and cut it again (the state is in the item). A finger's height in the touch layout.
    /// </summary>
    private Border OutputLine(ToolCallItem item, string text, ChatStrings strings)
    {
        var line = new Border { Child = Line(text, "ClaudeChatSecondary") };
        line.Classes.Add("ClaudeChatOutputLine");
        line.Background = Brushes.Transparent; // the whole row takes the click, not only the letters
        line.Tag = SummaryKey;
        line.Focusable = true;
        line.Cursor = new Cursor(StandardCursorType.Hand);
        AutomationProperties.SetName(line, text + ". " + (item.IsOutputExpanded ? strings.ToolCutText : strings.ToolShowFullText));
        line.GotFocus += (_, e) => _focusMethod = e.NavigationMethod;
        // a fast second click (a double click) is not a second toggle: it would close what the first one opened, like the texts of the box
        var clicks = 1;
        line.AddHandler(InputElement.PointerPressedEvent, (_, e) => clicks = e.ClickCount, Avalonia.Interactivity.RoutingStrategies.Bubble, true);
        line.Tapped += (_, e) =>
        {
            e.Handled = true;
            if (clicks <= 1) Toggle(item, input: false);
        };
        line.KeyDown += (_, e) =>
        {
            if ((e.Key != Key.Enter && e.Key != Key.Space) || e.KeyModifiers != KeyModifiers.None) return;
            e.Handled = true;
            Toggle(item, input: false);
        };
        return line;
    }

    /// <summary>"Fetched from" and the url, plain, at half strength, 8 px from the frame: all that a Web Fetch that worked shows.</summary>
    private static TextBlock FetchedFrom(string url, ChatStrings strings)
    {
        var text = new TextBlock { Text = strings.FormatFetchedFrom(url) };
        text.Classes.Add("ClaudeChatFetched");
        return text;
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
    /// background that scrolls sideways. A long one takes clicks, Enter and Space and opens whole in place (unless
    /// <paramref name="interactive"/> is false: a Glob's output, opened and cut from its line); a short one does too while its row
    /// cuts it (see <see cref="UpdateCut"/>), and opens wrapped, without the sideways scroll. The state is in the item, because
    /// the list recycles its rows.
    /// </summary>
    private Border TextRow(ToolCallItem item, string text, bool input, ChatStrings strings, bool interactive = true, bool error = false)
    {
        var expandable = IsExpandable(text);
        var opensShort = interactive && !error && !expandable; // a short text, which opens only once its row cuts it
        var whole = (expandable || opensShort) && (input ? item.IsInputExpanded : item.IsOutputExpanded);
        var wrapped = opensShort && whole;

        TextBlock block = input ? new TextBlock() : new SelectableTextBlock();
        block.Text = error ? text : Shown(text, whole); // an error is short already (cut at 2000): the frame cuts it at ten lines
        block.TextWrapping = error || wrapped ? TextWrapping.Wrap : TextWrapping.NoWrap;
        block.Classes.Add("ClaudeChatToolText");
        Control shown = block;
        if (error)
        {
            block.Classes.Add("ClaudeChatToolError");
        }
        else if (!input)
        {
            block.Classes.Add("ClaudeChatOutput");
            // the bar is drawn over the text: a line wider than the row gets room under it, so the bar does not cover the click
            var room = new Border { Height = ScrollBarRoom, IsVisible = false };
            var scroller = new ScrollViewer
            {
                // a short output opened wraps in the row's width: nothing is left to scroll to
                HorizontalScrollBarVisibility = wrapped ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto,
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
        if (error)
        {
            content.Classes.Add("error");
            if (expandable && !whole) ClipAtTenLines(content);
        }
        if (!interactive || (!expandable && !opensShort)) return content; // with a line above, that opens and cuts it

        content.Tag = input ? InputKey : OutputKey;
        // a short text closed is a stop only while its row cuts it, which the row knows once it is laid out
        ShortCell? cut = null;
        if (opensShort && !whole)
        {
            cut = new ShortCell(content, block, strings);
            _shortCells.Add(cut);
            content.SizeChanged += (_, _) => UpdateCut(cut);
            // the text's own size changes with its font, which may leave the row's as it was: measured once this layout is over
            block.SizeChanged += (_, _) => Dispatcher.UIThread.Post(() => UpdateCut(cut));
        }
        else
        {
            SetStop(content, true, whole ? strings.ToolCutText : strings.ToolShowFullText);
        }
        // the second click of a double click selects a word of the text: only the first one toggles
        var clicks = 1;
        content.AddHandler(InputElement.PointerPressedEvent, (_, e) => clicks = e.ClickCount, Avalonia.Interactivity.RoutingStrategies.Bubble, true);
        // a click while some of a short output is selected ends that selection and opens nothing (a press on the text takes it
        // away itself; one on the row around it leaves it to the click)
        var selected = false;
        if (opensShort)
            content.AddHandler(InputElement.PointerPressedEvent, (_, _) => selected = HasSelection(block), Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
        content.GotFocus += (_, e) => _focusMethod = e.NavigationMethod;
        content.Tapped += (_, e) =>
        {
            // a drag on the scroll bar is not a click on the text
            if (clicks > 1 || e.Source is Visual source && source.FindAncestorOfType<ScrollBar>(true) != null) return;
            if (cut != null && !cut.IsStop) return; // a short text that fits
            if (opensShort && (selected || HasSelection(block)))
            {
                if (selected && block is SelectableTextBlock selectable) selectable.SelectionEnd = selectable.SelectionStart;
                return;
            }
            e.Handled = true;
            Toggle(item, input);
        };
        content.KeyDown += (_, e) =>
        {
            if ((e.Key != Key.Enter && e.Key != Key.Space) || e.KeyModifiers != KeyModifiers.None) return;
            if (cut != null && !cut.IsStop) return;
            e.Handled = true;
            Toggle(item, input);
        };
        return content;
    }

    /// <summary>Makes a row's content a stop that opens and cuts its text (classes, focus, the hand, its name), or a plain text again.</summary>
    private static void SetStop(Border content, bool stop, string name)
    {
        content.Classes.Set("expandable", stop);
        content.Focusable = stop;
        if (stop)
        {
            content.Background = Brushes.Transparent; // the whole row takes the click, not only the letters
            content.Cursor = new Cursor(StandardCursorType.Hand);
            AutomationProperties.SetName(content, name);
        }
        else
        {
            content.ClearValue(Border.BackgroundProperty);
            content.ClearValue(CursorProperty);
            content.ClearValue(AutomationProperties.NameProperty);
        }
    }

    private static bool HasSelection(TextBlock block) =>
        block is SelectableTextBlock selectable && selectable.SelectionStart != selectable.SelectionEnd;

    /// <summary>A short text closed, which is a stop only while its row cuts it.</summary>
    private sealed class ShortCell
    {
        public ShortCell(Border content, TextBlock block, ChatStrings strings)
        {
            Content = content;
            Block = block;
            Strings = strings;
        }

        public Border Content { get; }
        public TextBlock Block { get; }
        public ChatStrings Strings { get; }
        public bool Measured { get; set; }
        public bool IsStop => Content.Classes.Contains("expandable");
    }

    /// <summary>The short texts of the body, closed: what <see cref="UpdateCut"/> measures (a cell of an earlier body is no longer here).</summary>
    private readonly List<ShortCell> _shortCells = new List<ShortCell>();

    /// <summary>The short text that had the focus before a redraw, waiting for its first measure to know whether it takes it back.</summary>
    private ShortCell? _awaitingFocus;

    /// <summary>
    /// Measures a short text after a layout: it is cut when the width it wants passes the row's by more than half a pixel, or its
    /// height passes the row's (the text measured is the one the row shows: the output already with its symbols). Only a change
    /// touches the content, so a measure that finds what it found before lays nothing out again: the stop's class, focus, hand and
    /// name change no size on the desktop, and the finger's height it takes in the touch layout only makes a cut row taller.
    /// A row that comes to fit while it has the focus lets it go, as a content that leaves the body does.
    /// </summary>
    private void UpdateCut(ShortCell cell)
    {
        if (!_shortCells.Contains(cell)) return;
        var content = cell.Content;
        var room = content.Bounds.Size.Deflate(content.Padding);
        if (room.Width <= 0) return; // not laid out yet
        var layout = cell.Block.TextLayout;
        var cut = layout.Width > room.Width + 0.5 || layout.Height > room.Height + 0.5;
        var first = !cell.Measured;
        cell.Measured = true;
        var changed = cut != cell.IsStop;
        var focused = content.IsFocused;
        if (changed) SetStop(content, cut, cell.Strings.ToolShowFullText);
        if (first && ReferenceEquals(_awaitingFocus, cell))
        {
            _awaitingFocus = null;
            if (cut) content.Focus(_focusMethod); // drawn again under the keyboard, it keeps it
        }
        else if (changed && !cut && focused)
        {
            TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
        }
    }

    /// <summary>Most lines of an error before it fades: 10em of the row content's font (the chat's own, not the text block's .85em).</summary>
    private const double ErrorClipEm = 10;

    /// <summary>Fade at the end of a clipped error, in px.</summary>
    private const double ErrorFadePx = 10;

    /// <summary>The padding of a row's content (4 above and 4 below).</summary>
    private const double ContentPadding = 8;

    /// <summary>Stops a long error at ten times the chat's font size (plus the padding), fading the last 10 px.</summary>
    private void ClipAtTenLines(Border content)
    {
        var fontSize = this.TryFindResource("ClaudeChat.FontSize", out var found) && found is double size ? size : 13;
        var height = ErrorClipEm * fontSize + ContentPadding;
        content.MaxHeight = height;
        content.OpacityMask = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(0, height, RelativeUnit.Absolute),
            GradientStops =
            {
                new GradientStop(Colors.Black, 0),
                new GradientStop(Colors.Black, (height - ErrorFadePx) / height),
                new GradientStop(Colors.Transparent, 1),
            },
        };
    }

    private static void Toggle(ToolCallItem item, bool input)
    {
        if (input) item.IsInputExpanded = !item.IsInputExpanded;
        else item.IsOutputExpanded = !item.IsOutputExpanded;
    }

    // ---- the copy button on the command's row ----

    private const string CopyKey = "copy";

    /// <summary>The command's row: its text, with the copy button over its top right corner.</summary>
    private Grid CommandCell(ToolCallItem item, string command, ChatStrings strings)
    {
        var icon = new Path();
        icon.Classes.Add("ClaudeChatCopyIcon");
        var box = new Viewbox { Child = icon };
        box.Classes.Add("ClaudeChatCopyCodeIcon");
        var button = new Button { Content = box, Tag = CopyKey };
        button.Classes.Add("ClaudeChatButton");
        button.Classes.Add("ClaudeChatCopyCode");
        ToolTip.SetTip(button, strings.CopyCode);
        button.GotFocus += (_, e) => _focusMethod = e.NavigationMethod;
        button.Click += (_, _) => _ = CopyCommandAsync(item, command);
        _copyButton = button;
        _copyIcon = icon;
        var cell = new Grid { Children = { TextRow(item, command, input: true, strings), button } };
        cell.Classes.Add("ClaudeChatCopyCell");
        return cell;
    }

    private async Task CopyCommandAsync(ToolCallItem item, string command)
    {
        if (!await ChatClipboard.SetTextAsync(this, command)) return; // refused: nothing was copied, so nothing is confirmed
        if (!_attached || !ReferenceEquals(_item, item)) return; // the list took the view to another call meanwhile
        _copied = true;
        UpdateCopyState();
        _copyTimer.Stop();
        _copyTimer.Start(); // a second copy gives the check another 2 s
    }

    /// <summary>True from a copy until the check goes away.</summary>
    internal bool IsCopied => _copied;

    /// <summary>True while the timer waits to take the check away.</summary>
    internal bool IsCopyTicking => _copyTimer.IsEnabled;

    /// <summary>The check goes away (its timer's tick, 2 s after a copy): the copy icon and its name come back.</summary>
    internal void EndCopied()
    {
        _copyTimer.Stop();
        if (!_copied) return;
        _copied = false;
        UpdateCopyState();
    }

    private void UpdateCopyState()
    {
        if (_copyButton == null || _copyIcon == null) return;
        _copyIcon.Classes.Set("copied", _copied);
        AutomationProperties.SetName(_copyButton, _copied ? Strings.Copied : Strings.CopyCodeToClipboard);
    }

    // ---- what a running command has printed so far ----

    /// <summary>True while the row asks the host for its command's output (now and then once a second).</summary>
    internal bool IsAskingForOutput => _askingForOutput;

    /// <summary>How long the timer waits between two requests.</summary>
    internal TimeSpan OutputAskInterval => _outputTimer.Interval;

    /// <summary>
    /// The row asks for its command's output while it is in the window, its call is a Bash or PowerShell still running, and the host
    /// listens to <see cref="ChatConversation.TaskOutputRequested"/>; otherwise its timer stands still. While the row is hidden its
    /// ticks ask nothing (see <see cref="AskForOutput"/>). The conversation keeps to one request a second for each command, and none
    /// while the last one waits for its answer.
    /// </summary>
    private void UpdateAsking()
    {
        var item = _item;
        var asks = _attached && item != null && IsCommand(item.Name) && item.Status == ToolStatus.Running && _host?.CanRequestTaskOutput == true;
        if (asks == _askingForOutput) return;
        _askingForOutput = asks;
        _outputTimer.Stop();
        // the first request is a job of its own, as each tick is: never inside the conversation's or the host's call that made the row ask
        if (asks) Dispatcher.UIThread.Post(StartAsking);
    }

    /// <summary>
    /// Listens to the visibility of what hides the row: its parent and, for a step of a subagent, the ancestors up to the parent of
    /// the outermost row it is in.
    /// </summary>
    private void ListenToContainers()
    {
        var last = this.GetVisualParent();
        for (var visual = last; visual != null; visual = visual.GetVisualParent())
        {
            if (visual is ToolCallView outer) last = outer.GetVisualParent() ?? outer;
        }
        for (var visual = this.GetVisualParent(); visual != null; visual = visual.GetVisualParent())
        {
            _containers.Add(visual);
            visual.PropertyChanged += OnContainerChanged;
            if (ReferenceEquals(visual, last)) break;
        }
    }

    /// <summary>
    /// The touch list hides the row's container while the row waits off screen for its next item, and a subagent's folded steps are
    /// hidden with their list; when the row shows again it asks at once, as one that comes into the window does, and then once a
    /// second.
    /// </summary>
    private void OnContainerChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != IsVisibleProperty || !_askingForOutput || !IsEffectivelyVisible) return;
        _outputTimer.Stop();
        Dispatcher.UIThread.Post(StartAsking);
    }

    private void StartAsking()
    {
        if (!_askingForOutput || _outputTimer.IsEnabled) return; // it stopped meanwhile, or a job before this one started it
        _outputTimer.Start(); // the next request a second after this one
        AskForOutput();
    }

    /// <summary>
    /// One request (the timer's tick). A row the touch list keeps hidden, off screen, ticks on and asks nothing until it shows again.
    /// A host handler that throws is not caught, as anywhere a row raises a host event: its exception reaches the dispatcher like any
    /// other.
    /// </summary>
    private void AskForOutput()
    {
        if (!IsEffectivelyVisible) return;
        if (_askingForOutput && _item is { } item && _host is { } host) host.RequestTaskOutput(item);
    }

    /// <summary>
    /// The body under the card of a running command, while its <see cref="ToolCallItem.LiveOutput"/> has text: it is built when the
    /// text first comes and kept while it changes (so the person's scrolling and the focus stay), and leaves the tree when the text
    /// goes or the call stops running.
    /// </summary>
    private void UpdateLiveOutput()
    {
        var item = _item;
        var output = item != null && IsCommand(item.Name) && item.Status == ToolStatus.Running ? item.LiveOutput : null;
        if (string.IsNullOrEmpty(output))
        {
            DropLiveBox();
            return;
        }
        if (_liveBox == null) BuildLiveBox();

        var strings = Strings;
        _liveText!.Text = output;
        AutomationProperties.SetName(_liveText, strings.LiveOutputLabel);
        // the claude kept only the end: how much there is (an answer that did not say leaves the line out, rather than "0 bytes")
        var cut = item!.LiveOutputTruncated && item.LiveOutputBytes > 0;
        _liveEnd!.Text = cut ? strings.FormatLiveOutputEnd(item.LiveOutputBytes) : "";
        _liveEnd.IsVisible = cut;
    }

    /// <summary>The body leaves the tree, with its scrolling: the next text builds a new one.</summary>
    private void DropLiveBox()
    {
        if (_liveBox == null) return;
        LiveOutputSlot.Children.Clear();
        _liveBox = null;
        _liveBoxFor = null;
        _liveText = null;
        _liveEnd = null;
    }

    /// <summary>
    /// The body: a second tool body under the card, with one OUT row (built like the card's rows) whose text scrolls past 240 px, and
    /// the line about the end under it. The styles give the measures.
    /// </summary>
    private void BuildLiveBox()
    {
        var text = new SelectableTextBlock { Focusable = true };
        text.Classes.Add("ClaudeChatLiveOutputText");
        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = text,
        };
        scroller.ScrollChanged += OnLiveScrollChanged;
        var frame = new Border { Child = scroller };
        frame.Classes.Add("ClaudeChatLiveOutputFrame");

        var end = new TextBlock();
        end.Classes.Add("ClaudeChatLiveOutputEnd");

        var box = ToolBody(Rows(new List<(string, Control, bool)> { (Strings.ToolOutput, new StackPanel { Children = { frame, end } }, false) }));
        box.Classes.Add("ClaudeChatLiveOutput");
        _liveBox = box;
        _liveBoxFor = _item;
        _liveText = text;
        _liveEnd = end;
        _liveFollowsEnd = true;
        LiveOutputSlot.Children.Add(box);
    }

    /// <summary>
    /// The text grew: it goes to its end while it followed it. The person scrolled: it follows again only once
    /// they are within <see cref="FollowEndWithin"/> of the end.
    /// </summary>
    private void OnLiveScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scroller) return;
        if (e.ExtentDelta.Y != 0 || e.ViewportDelta.Y != 0)
        {
            if (_liveFollowsEnd) scroller.ScrollToEnd();
        }
        else if (e.OffsetDelta.Y != 0)
        {
            _liveFollowsEnd = scroller.Extent.Height - scroller.Viewport.Height - scroller.Offset.Y < FollowEndWithin;
        }
    }

    /// <summary>
    /// The diff of the list, 200 px at most, under a layer that opens the Diff View over the whole view with the whole change; the
    /// layer keeps the focus across a redraw, like a text of the box.
    /// </summary>
    private DiffView ListDiff(ToolCallItem item, ChatStrings strings)
    {
        var diff = new DiffView(item.Diff!, strings, touch: GetValue(LayoutProperty) == ChatLayout.Touch);
        diff.ExpandRequested += (_, _) => this.FindAncestorOfType<ClaudeChatView>()?.ShowDiff(item);
        var layer = diff.ExpandControl!;
        layer.Tag = DiffKey;
        layer.GotFocus += (_, e) => _focusMethod = e.NavigationMethod;
        return diff;
    }

    /// <summary>The key (IN, OUT, the diff or the copy button) of the part of the box that has the focus, or null when none of them does.</summary>
    private string? FocusedContent()
    {
        if (Body.Children.Count == 0 || TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Visual focus
            || !Body.IsVisualAncestorOf(focus)) return null;
        foreach (var content in Body.GetVisualDescendants().OfType<Control>())
        {
            if (content.Tag is string key && content.Focusable && (ReferenceEquals(content, focus) || content.IsVisualAncestorOf(focus)))
                return key;
        }
        return null;
    }

    /// <summary>The box was drawn again under the keyboard (or the finger): the same part keeps the focus. False when no part takes it now.</summary>
    private bool Refocus(string key)
    {
        foreach (var content in Body.GetVisualDescendants().OfType<Control>())
        {
            if (Equals(content.Tag, key) && content.Focusable)
            {
                content.Focus(_focusMethod);
                return true;
            }
        }
        return false;
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
            case "Glob":
                // an error or a refusal shows the raw text in OUT instead
                return item.Status == ToolStatus.Succeeded
                    ? strings.FormatFilesFound(ToolPresentation.CountOutputLines(item.FullResultText ?? item.ResultText, NoFilesFound))
                    : null;
            case "Grep":
                return item.Status == ToolStatus.Succeeded
                    ? strings.FormatLinesOfOutput(ToolPresentation.CountOutputLines(item.FullResultText ?? item.ResultText))
                    : null;
            case "WebSearch":
                return strings.FormatWebDomains(ToolPresentation.SearchDomains(item.Input, "allowed_domains"),
                    ToolPresentation.SearchDomains(item.Input, "blocked_domains"));
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
    private static bool QuietWhenDone(string toolName) => toolName == "Read" || toolName == "WebFetch" || CountsOutput(toolName);

    /// <summary>What a Glob says when it found nothing (the tool's own text, the same in every language): counted as no files.</summary>
    private const string NoFilesFound = "No files found";

    /// <summary>Glob and Grep: their row is a line that counts the output, which a click opens.</summary>
    private static bool CountsOutput(string toolName) => toolName == "Glob" || toolName == "Grep";

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
    private static Control Rows(List<(string Label, Control Content, bool Copyable)> rows)
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
            if (rows[i].Copyable) frame.Classes.Add("ClaudeChatCopyHost"); // the pointer over it shows its copy button
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
