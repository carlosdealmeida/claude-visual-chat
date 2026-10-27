using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// One tool call, like the extension draws it: a dot (its color tells the state), the tool's name in bold and what it
/// works on (a file in the link color, a command's description, the lines read); under it a line such as "Modified"
/// or "47 lines", and a box with the diff, the start of the file written, or the command (IN) and its output (OUT,
/// wrapped); "Run in background" under a command started so; for a subagent, its items behind an arrow. The dot of a
/// running call blinks only while the conversation works. Long texts are cut so a huge result never slows the list.
/// In the touch layout (pseudo-class <c>:touch</c>) the call is a block with a frame and its dot is the header's first
/// piece.
/// </summary>
internal partial class ToolCallView : UserControl
{
    /// <summary>Most characters of output shown.</summary>
    internal const int MaxOutputChars = 4000;

    /// <summary>Most characters of a diff line or a to-do shown.</summary>
    internal const int MaxLineChars = 400;

    /// <summary>Most diff rows shown; the rest becomes "+N lines".</summary>
    internal const int MaxDiffRows = 200;

    /// <summary>
    /// Lines of a command or a written file put in its box, which shows about four of them; each is cut at
    /// <see cref="MaxLineChars"/>. The permission request shows them whole.
    /// </summary>
    internal const int BoxLines = 8;

    private static readonly StyledProperty<ChatStrings> StringsProperty =
        ClaudeChatView.StringsProperty.AddOwner<ToolCallView>();

    private static readonly StyledProperty<bool> IsWorkingProperty =
        ClaudeChatView.IsWorkingProperty.AddOwner<ToolCallView>();

    private static readonly StyledProperty<ChatLayout> LayoutProperty =
        ClaudeChatView.LayoutProperty.AddOwner<ToolCallView>();

    private ToolCallItem? _item;
    private bool _attached;

    public ToolCallView()
    {
        InitializeComponent();
        Refresh();
    }

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
        else if (change.Property == LayoutProperty) UpdateTouch();
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
        // out of the window, only the events go: Avalonia is taking the tree apart, and coming back draws it again
        if (_attached) Refresh();
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
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
            case nameof(ToolCallItem.HiddenResultLines):
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
        if (IsFileTool(name))
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
    }

    private void UpdateStatus()
    {
        var status = _item?.Status ?? ToolStatus.Running;
        // blinks only while the conversation works: a call left without a result in a history, or in a subagent the
        // turn ended without, keeps a still gray dot
        var blinks = status == ToolStatus.Running && GetValue(IsWorkingProperty);
        SetState(Row, status, blinks); // the timeline's dot
        SetState(StatusDot, status, blinks); // the touch layout's, in the header
        RejectedText.Text = Strings.ToolRejected;
        RejectedText.IsVisible = status == ToolStatus.Rejected;
    }

    /// <summary>The classes the dots take their color (and their blinking) from.</summary>
    private static void SetState(Control dot, ToolStatus status, bool blinks)
    {
        dot.Classes.Set("running", blinks);
        dot.Classes.Set("succeeded", status == ToolStatus.Succeeded);
        dot.Classes.Set("failed", status == ToolStatus.Failed);
        dot.Classes.Set("rejected", status == ToolStatus.Rejected);
    }

    private void UpdateChildren()
    {
        var hasChildren = _item != null && _item.Children.Count > 0;
        Arrow.IsVisible = hasChildren;
        Header.IsHitTestVisible = hasChildren;
        Header.Focusable = hasChildren;
        Header.Classes.Set("interactive", hasChildren); // the touch layout gives it a finger's height
    }

    // ---- the body ----

    private void UpdateBody()
    {
        Body.Children.Clear();
        var item = _item;
        if (item != null)
        {
            var strings = Strings;
            var name = item.Name;
            var failed = item.Status == ToolStatus.Failed;
            var hasDiff = item.Diff != null && item.Diff.Count > 0;
            var hasTodos = !hasDiff && item.Todos != null && item.Todos.Count > 0;

            var subtitle = Subtitle(item, strings);
            if (subtitle != null) Body.Children.Add(Line(subtitle, "ClaudeChatSecondary"));

            // a diff or a list stands for the output, unless the call failed; a refused call ran nothing; a Read or a
            // subagent that worked keeps quiet, like the extension (the file's lines, the subagent's launch details)
            var quiet = item.Status == ToolStatus.Succeeded && QuietWhenDone(name);
            var showOutput = failed || (!hasDiff && !hasTodos && item.Status != ToolStatus.Rejected && !quiet);
            var output = showOutput && (!string.IsNullOrEmpty(item.ResultText) || item.HiddenResultLines > 0);

            if (name == "Write" && WrittenText(item) is { } written)
                Body.Children.Add(ToolBody(RowContent(Text(written, "ClaudeChatToolText"), whole: false)));
            else if (hasDiff)
                Body.Children.Add(DiffBox(item.Diff!, strings));
            else if (hasTodos)
                Body.Children.Add(TodoList(item.Todos!));

            var rows = new List<(string Label, Control Content)>();
            // the command's first lines: the box shows four, and the request showed it whole
            if (IsCommand(name) && Str(item.Input, "command") is { Length: > 0 } command)
                rows.Add((strings.ToolInput, RowContent(Text(ChatText.ClipLines(command, BoxLines, MaxLineChars), "ClaudeChatToolText"), whole: false)));
            if (output) rows.Add((strings.ToolOutput, Output(item, strings)));
            if (rows.Count > 0) Body.Children.Add(ToolBody(Rows(rows)));
            if (IsCommand(name) && RunsInBackground(item.Input))
            {
                var note = Line(strings.RunInBackground, "ClaudeChatSecondary");
                note.Classes.Add("background");
                Body.Children.Add(note);
            }
        }
        Body.IsVisible = Body.Children.Count > 0;
    }

    /// <summary>The line under the name: what an edit changed, how long a written file is.</summary>
    private static string? Subtitle(ToolCallItem item, ChatStrings strings)
    {
        var failed = item.Status == ToolStatus.Failed;
        switch (item.Name)
        {
            case "Edit":
            case "MultiEdit":
                if (failed) return strings.EditFailed;
                return ToolPresentation.EditLineChange(item.Name, item.Input) is { } change ? strings.FormatEditChange(change) : null;
            case "Write":
                if (failed) return strings.WriteFailed;
                return ToolPresentation.WriteLineCount(item.Name, item.Input) is { } lines ? strings.FormatLines(lines) : null;
            default:
                return null;
        }
    }

    /// <summary>Tools whose output, when they work, is not worth showing in the list.</summary>
    private static bool QuietWhenDone(string toolName) =>
        toolName == "Read" || toolName == "Agent" || toolName == "Task";

    private static bool IsFileTool(string toolName) =>
        toolName == "Read" || toolName == "Edit" || toolName == "Write" || toolName == "MultiEdit" || toolName == "NotebookEdit";

    private static bool IsCommand(string toolName) => toolName == "Bash" || toolName == "PowerShell";

    /// <summary>A command started with <c>run_in_background: true</c>.</summary>
    private static bool RunsInBackground(JsonElement input) =>
        input.ValueKind == JsonValueKind.Object
        && input.TryGetProperty("run_in_background", out var value)
        && value.ValueKind == JsonValueKind.True;

    /// <summary>
    /// The diff in the tool's box, cut at 200 px like the extension's; the permission prompt draws it the same way,
    /// <paramref name="whole"/>, inside its own scrolling area.
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

    /// <summary>What goes in a row: 4 px inside; at most 60 px with a fade, unless <paramref name="whole"/>.</summary>
    private static Border RowContent(Control child, bool whole)
    {
        // measured at full height inside a stack, so the lines past 60 px are cut by the frame, not left out by the text
        var content = new Border { Child = new StackPanel { Children = { child } } };
        content.Classes.Add("ClaudeChatToolContent");
        if (whole) content.Classes.Add("whole");
        return content;
    }

    /// <summary>The output, with "+N lines" under it when it was cut; red when the call failed.</summary>
    private static Control Output(ToolCallItem item, ChatStrings strings)
    {
        var stack = new StackPanel();
        if (!string.IsNullOrEmpty(item.ResultText))
        {
            // the output wraps, so a long error is read whole; the command and a written file do not
            var text = new SelectableTextBlock { Text = ChatText.Clip(item.ResultText, MaxOutputChars), TextWrapping = TextWrapping.Wrap };
            text.Classes.Add("ClaudeChatToolText");
            text.Classes.Add("ClaudeChatOutput");
            if (item.Status == ToolStatus.Failed) text.Classes.Add("failed");
            stack.Children.Add(text);
        }
        if (item.HiddenResultLines > 0) stack.Children.Add(Line(strings.FormatMoreLines(item.HiddenResultLines), "ClaudeChatMoreLines"));
        return RowContent(stack, whole: true);
    }

    /// <summary>The first lines of a written file, as text, each cut at <see cref="MaxLineChars"/>.</summary>
    private static string? WrittenText(ToolCallItem item)
    {
        var content = Str(item.Input, "content");
        if (content == null)
        {
            // no input to read (an item built by hand): the diff's lines
            if (item.Diff == null || item.Diff.Count == 0) return null;
            var lines = new List<string>();
            foreach (var line in item.Diff)
            {
                if (line.Kind == DiffKind.Added && lines.Count < BoxLines) lines.Add(ChatText.Clip(line.Text, MaxLineChars));
            }
            return string.Join("\n", lines);
        }
        return ChatText.ClipLines(content, BoxLines, MaxLineChars);
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

    private static TextBlock Text(string text, string style)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.NoWrap };
        block.Classes.Add(style);
        return block;
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
