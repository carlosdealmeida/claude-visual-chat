using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The extension's modals that <c>/context</c> ("Context usage") and <c>/usage</c> ("Account &amp; Usage") open
/// (<see cref="ChatConversation.ShownPanel"/>): a dialog in the side bar's color over a dark backdrop that covers the whole view,
/// like the Agent map, with a title and a × (<see cref="ChatConversation.ClosePanel"/>) over a body that scrolls when it is taller
/// than the view. "Loading …" while the conversation is busy and until the claude sends the data, and a failure's line when the
/// host's request failed. Esc (the view's), the × and a click on the backdrop close it. It takes the keyboard when it opens and
/// gives it back to what had it (<see cref="FocusReturn"/>), or, when that is gone, lets the view put the keyboard back
/// (<see cref="FocusReleased"/>). Read-only information: it shows in a read-only view too. In the touch layout it never shows: the
/// usage sheet (<see cref="UsageSheet"/>) shows the same information there.
/// </summary>
internal abstract class ChatInfoPanel : Border
{
    private static readonly StyledProperty<ChatStrings> StringsProperty =
        ClaudeChatView.StringsProperty.AddOwner<ChatInfoPanel>();

    private static readonly StyledProperty<ChatLayout> LayoutProperty =
        ClaudeChatView.LayoutProperty.AddOwner<ChatInfoPanel>();

    private readonly Border _dialog = new Border();
    private readonly TextBlock _title = new TextBlock();
    private readonly Button _close;
    private readonly Border _body = new Border();
    private readonly FocusReturn _return = new FocusReturn();
    private ChatConversation? _conversation;
    private ChatConversation? _tracked;
    private bool _attached;

    protected ChatInfoPanel()
    {
        Classes.Add("ClaudeChatModal");
        _dialog.Classes.Add("ClaudeChatInfoModal");
        _dialog.MaxWidth = DialogMaxWidth;
        _title.Classes.Add("ClaudeChatModalTitle");
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
        header.Classes.Add("ClaudeChatModalHeader");
        DockPanel.SetDock(header, Dock.Top);
        _body.Classes.Add("ClaudeChatModalBody");
        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = _body,
        };
        _dialog.Child = new DockPanel { Children = { header, scroller } };
        KeyboardNavigation.SetTabNavigation(_dialog, KeyboardNavigationMode.Cycle);
        Child = _dialog;
        IsVisible = false;
    }

    /// <summary>The panel closed while the keyboard was in it, and there was nothing to give it back to.</summary>
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

    /// <summary>The dialog over the backdrop.</summary>
    internal Border Dialog => _dialog;

    /// <summary>The panel this control draws.</summary>
    protected abstract ChatPanel Kind { get; }

    /// <summary>How wide the dialog is at most.</summary>
    protected abstract double DialogMaxWidth { get; }

    protected abstract string Title(ChatStrings strings);

    /// <summary>The body for the conversation's data; null while there is none yet.</summary>
    protected abstract Control? Build(ChatConversation conversation, ChatStrings strings);

    /// <summary>The body when the host's request for the data failed; null when it did not.</summary>
    protected abstract Control? Failure(ChatConversation conversation, ChatStrings strings);

    /// <summary>True when the conversation's property is one the body shows.</summary>
    protected abstract bool Shows(string? propertyName);

    /// <summary>The line shown while the data is on its way (the conversation is busy).</summary>
    protected abstract string Loading(ChatStrings strings);

    /// <summary>The line shown when there is no data and the conversation is idle.</summary>
    protected abstract string Unavailable(ChatStrings strings);

    protected ChatStrings Strings => GetValue(StringsProperty) ?? ChatStrings.English;

    /// <summary>Styled as a Border: the theme's <c>Border.ClaudeChatModal</c>.</summary>
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
        if (change.Property == StringsProperty || change.Property == LayoutProperty) Update();
    }

    /// <summary>A click on the backdrop, not on the dialog, closes the panel.</summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!ReferenceEquals(e.Source, this)) return;
        e.Handled = true;
        _tracked?.ClosePanel();
    }

    private void Track(ChatConversation? conversation)
    {
        if (!ReferenceEquals(conversation, _tracked))
        {
            if (_tracked != null)
            {
                _tracked.PropertyChanged -= OnConversationChanged;
                _tracked.HostHandlersChanged -= OnHostHandlersChanged;
            }
            _tracked = conversation;
            if (_tracked != null)
            {
                _tracked.PropertyChanged += OnConversationChanged;
                _tracked.HostHandlersChanged += OnHostHandlersChanged;
            }
        }
        // out of the window, only the events go: Avalonia is taking the tree apart, and coming back draws it again
        if (_attached) Update();
        else _return.Forget();
    }

    private void OnConversationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatConversation.ShownPanel) || e.PropertyName == nameof(ChatConversation.IsBusy) || (IsVisible && Shows(e.PropertyName))) Update();
    }

    private void OnHostHandlersChanged(object? sender, EventArgs e)
    {
        if (IsVisible) Update(); // a link that needs the host's handler
    }

    private void Update()
    {
        var conversation = _tracked;
        var shown = conversation != null && conversation.ShownPanel == Kind && GetValue(LayoutProperty) != ChatLayout.Touch;
        var hadFocus = IsKeyboardFocusWithin;
        var opening = shown && !IsVisible;
        if (shown)
        {
            var strings = Strings;
            _title.Text = Title(strings);
            AutomationProperties.SetName(_close, strings.Close);
            ToolTip.SetTip(_close, strings.Close);
            _body.Child = Build(conversation!, strings) ?? Failure(conversation!, strings)
                ?? Empty(conversation!.IsBusy ? Loading(strings) : Unavailable(strings));
            if (opening) _return.Remember(this);
            IsVisible = true;
            if (opening) _close.Focus();
            return;
        }
        _body.Child = null;
        var wasShown = IsVisible;
        IsVisible = false;
        if (!wasShown) return;
        // the keyboard goes back to what had it; to the view's rule (a request that waits, the reply box) when that is gone, or in
        // the touch layout, where the reply box with the keyboard is the soft keyboard coming up
        if (!hadFocus) _return.Forget();
        else if (!(GetValue(LayoutProperty) != ChatLayout.Touch && conversation?.CurrentPermission == null && _return.Restore()))
            FocusReleased?.Invoke(this, EventArgs.Empty);
    }

    private static Control Empty(string line)
    {
        var text = new TextBlock { Text = line };
        text.Classes.Add("ClaudeChatPanelLoading");
        return text;
    }

    // ---- pieces of the bodies ----

    /// <summary>A bar split in pieces as long as their weights; a piece without a class is the bare track.</summary>
    internal static Border Bar(string barClass, IEnumerable<(double Weight, string? PieceClass, string? Color)> pieces)
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

    internal static TextBlock Text(string text, string textClass)
    {
        var block = new TextBlock { Text = text };
        block.Classes.Add(textClass);
        return block;
    }

    /// <summary>The title of a section or a list: small, in capitals.</summary>
    protected static TextBlock SectionTitle(string text, string sectionClass) =>
        Text(text.ToUpperInvariant(), sectionClass);

    /// <summary>A line of two texts: the first takes the room, the second sits at the right.</summary>
    protected static Grid TwoColumns(Control left, Control right)
    {
        Grid.SetColumn(right, 1);
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        line.Children.Add(left);
        line.Children.Add(right);
        return line;
    }
}

/// <summary>
/// "Context usage": the model and "36,174 / 200,000 tokens (18%)", a bar with a piece per category that is neither deferred nor the
/// free space (the colors <c>ClaudeChat.ContextColor1</c> to <c>8</c>, round again after the eighth), a table of Category · Tokens ·
/// Usage with a row per such category and, last, the free space with an empty swatch, then the first five memory files (with
/// "~" for the home folder) and custom agents, each list only when the claude sent any.
/// </summary>
internal sealed class ContextPanel : ChatInfoPanel
{
    /// <summary>The colors of the touch layout's usage sheet, round again after the sixth.</summary>
    internal const int Colors = 6;

    /// <summary>The colors of the modal's bar, round again after the eighth.</summary>
    internal const int ModalColors = 8;

    /// <summary>How many memory files and custom agents the lists show.</summary>
    internal const int ListLength = 5;

    protected override ChatPanel Kind => ChatPanel.Context;

    protected override double DialogMaxWidth => 440;

    protected override string Title(ChatStrings strings) => strings.ContextTitle;

    protected override string Loading(ChatStrings strings) => strings.ContextLoading;

    protected override string Unavailable(ChatStrings strings) => strings.ContextUnavailable;

    protected override bool Shows(string? propertyName) =>
        propertyName == nameof(ChatConversation.ContextUsage) || propertyName == nameof(ChatConversation.ContextError)
        || propertyName == nameof(ChatConversation.AvailableModels);

    /// <summary>
    /// The style class (<c>color1</c> to <c>color6</c>, round again after the sixth) of the used category that comes
    /// <paramref name="index"/>th among the used ones: the color of its piece in the bar and of its dot, in the touch layout.
    /// </summary>
    internal static string ColorOf(int index) => "color" + (index % Colors + 1);

    /// <summary>The same for the modal, which has eight colors.</summary>
    internal static string ModalColorOf(int index) => "color" + ModalOrder[index % ModalColors];

    /// <summary>
    /// The extension's order of the bar: orange, blue, green, yellow, purple, red, cyan, the foreground. The numbers are those of the
    /// <c>ClaudeChat.ContextColor</c> resources (the touch layout's sheet keeps its own order of the first six).
    /// </summary>
    private static readonly int[] ModalOrder = { 3, 1, 4, 5, 2, 6, 7, 8 };

    /// <summary>
    /// The pieces of the bar of <see cref="ChatInfoPanel.Bar"/> in the touch layout: one per used category, in order, as long as its
    /// tokens and in its color, then the rest of the window, the bare track. The deferred and the free categories are not in the bar.
    /// </summary>
    internal static List<(double, string?, string?)> BarPieces(ContextUsage usage)
    {
        var pieces = new List<(double, string?, string?)>();
        long usedTokens = 0;
        foreach (var category in usage.Categories)
        {
            if (category.Kind != "used") continue;
            var tokens = Math.Max(0, category.Tokens);
            pieces.Add((tokens, "ClaudeChatContextSegment", ColorOf(pieces.Count)));
            usedTokens += tokens;
        }
        pieces.Add((Math.Max(0, usage.MaxTokens - usedTokens), null, null)); // the rest of the window: the track
        return pieces;
    }

    /// <summary>The categories the modal draws in its bar and its table: not the free space, not the deferred ones.</summary>
    private static IEnumerable<ContextCategory> InWindow(ContextUsage usage) =>
        usage.Categories.Where(c => c.Kind != "free" && c.Kind != "deferred" && c.Tokens > 0);

    /// <summary>
    /// The pieces of the modal's bar: one per category in the window, in order, as long as its tokens and in its color, then the rest
    /// of the window, the bare track.
    /// </summary>
    internal static List<(double, string?, string?)> ModalBarPieces(ContextUsage usage)
    {
        var pieces = new List<(double, string?, string?)>();
        long usedTokens = 0;
        foreach (var category in InWindow(usage))
        {
            var tokens = Math.Max(0, category.Tokens);
            pieces.Add((tokens, "ClaudeChatContextSegment", ModalColorOf(pieces.Count)));
            usedTokens += tokens;
        }
        pieces.Add((Math.Max(0, usage.MaxTokens - usedTokens), null, null));
        return pieces;
    }

    protected override Control? Failure(ChatConversation conversation, ChatStrings strings)
    {
        if (conversation.ContextError == null) return null;
        var text = strings.ContextFailed.Replace("{0}", conversation.ContextError).TrimEnd(' ', ':');
        return Text(text, "ClaudeChatPanelLoading");
    }

    protected override Control? Build(ChatConversation conversation, ChatStrings strings)
    {
        var usage = conversation.ContextUsage;
        if (usage == null) return null;
        var body = new StackPanel();
        body.Classes.Add("ClaudeChatModalStack");
        if (usage.Model is { Length: > 0 } model) body.Children.Add(Text(ModelName(conversation, model), "ClaudeChatModalModel"));
        body.Children.Add(Text(strings.FormatContextHeader(usage), "ClaudeChatContextTotal"));
        body.Children.Add(Bar("ClaudeChatContextBar", ModalBarPieces(usage)));

        var table = new StackPanel();
        table.Classes.Add("ClaudeChatModalTable");
        table.Children.Add(TableRow(null, strings.ContextColumnCategory, strings.ContextColumnTokens, strings.ContextColumnUsage, header: true));
        var index = 0;
        foreach (var category in InWindow(usage))
            table.Children.Add(TableRow(ModalColorOf(index++), category.Name, strings.FormatNumber(category.Tokens), Share(usage, category, strings), header: false));
        foreach (var category in usage.Categories.Where(c => c.Kind == "free"))
            table.Children.Add(TableRow("empty", category.Name, strings.FormatNumber(category.Tokens), Share(usage, category, strings), header: false));
        body.Children.Add(table);

        // the biggest five, the biggest first, as the extension lists them
        if (usage.MemoryFiles.Count > 0)
            body.Children.Add(List(strings.ContextMemoryFiles, "/memory", usage.MemoryFiles.OrderByDescending(f => f.Tokens).Take(ListLength).Select(f => (Tilde(f.Path), strings.FormatShortTokens(f.Tokens)))));
        if (usage.Agents.Count > 0)
            body.Children.Add(List(strings.ContextCustomAgents, "/agents", usage.Agents.OrderByDescending(a => a.Tokens).Take(ListLength).Select(a => (a.Name, strings.FormatShortTokens(a.Tokens)))));
        return body;
    }

    /// <summary>The model's display name when the session offers it, else the name the claude gave.</summary>
    private static string ModelName(ChatConversation conversation, string model)
    {
        foreach (var offered in conversation.AvailableModels ?? Array.Empty<ChatModel>())
        {
            if (string.Equals(offered.Value, model, StringComparison.Ordinal) || string.Equals(offered.ResolvedModel, model, StringComparison.Ordinal))
                return offered.DisplayName;
        }
        return model;
    }

    private static string Share(ContextUsage usage, ContextCategory category, ChatStrings strings) =>
        usage.MaxTokens > 0 ? strings.FormatContextShare((double)category.Tokens / usage.MaxTokens) : "";

    /// <summary>A row of the table: the swatch (none in the header; "empty" is the free space's, with no fill), the name, the tokens, the share.</summary>
    private static Control TableRow(string? swatch, string name, string tokens, string share, bool header)
    {
        var first = new StackPanel { Orientation = Orientation.Horizontal };
        if (!header)
        {
            var square = new Border();
            square.Classes.Add("ClaudeChatContextSwatch");
            square.Classes.Add(swatch!);
            first.Children.Add(square);
        }
        else
        {
            first.Margin = new Thickness(18, 0, 0, 0); // over the names: past the 10 px swatch and its 8 px
        }
        first.Children.Add(Text(header ? name.ToUpperInvariant() : name, header ? "ClaudeChatModalHeaderCell" : "ClaudeChatModalName"));
        var amount = Text(header ? tokens.ToUpperInvariant() : tokens, header ? "ClaudeChatModalHeaderCell" : "ClaudeChatModalTokens");
        var percent = Text(header ? share.ToUpperInvariant() : share, header ? "ClaudeChatModalHeaderCell" : "ClaudeChatModalShare");
        if (header)
        {
            amount.Classes.Add("ClaudeChatModalHeaderRight");   // TOKENS and USAGE sit at the right, over their figures
            percent.Classes.Add("ClaudeChatModalHeaderRight");
        }
        Grid.SetColumn(amount, 1);
        Grid.SetColumn(percent, 2);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,80,64"), Children = { first, amount, percent } };
        row.Classes.Add(header ? "ClaudeChatModalTableHeader" : "ClaudeChatModalTableRow");
        return row;
    }

    /// <summary>A list under a small title: a line per item with its tokens at the right.</summary>
    private static Control List(string title, string hint, IEnumerable<(string Name, string Tokens)> items)
    {
        var list = new StackPanel();
        list.Classes.Add("ClaudeChatModalList");
        list.Children.Add(TwoColumns(SectionTitle(title, "ClaudeChatModalListTitle"), Text(hint, "ClaudeChatModalListTitle"))); // "/memory", "/agents": where to change them
        foreach (var (name, tokens) in items)
        {
            var line = TwoColumns(Text(name, "ClaudeChatModalListName"), Text(tokens, "ClaudeChatModalListTokens"));
            line.Classes.Add("ClaudeChatModalListRow");
            list.Children.Add(line);
        }
        return list;
    }

    private static readonly System.Text.RegularExpressions.Regex HomeFolder =
        new System.Text.RegularExpressions.Regex(@"^(?:/Users/[^/]+|/home/[^/]+|/root|[A-Za-z]:\\Users\\[^\\]+)(?=[/\\]|$)");

    /// <summary>A path with the home folder written "~".</summary>
    internal static string Tilde(string path)
    {
        // any home folder, not only this machine's: the claude may run on another one
        var match = HomeFolder.Match(path);
        return match.Success ? "~" + path.Substring(match.Length) : path;
    }
}

/// <summary>
/// "Account &amp; Usage": the account section (auth method, e-mail, organization, plan) when the claude reported an account; then
/// the usage section: a bar per limit window the claude reported ("Session (5hr)", "Weekly (7 day)", "Weekly Sonnet" and a "Weekly
/// {model}" for each model's), each with the share used rounded down, filled in the progress bar's color and in the clay's from 80 %,
/// and when it renews as the time left ("Resets in 45m"); with no limits, the session's cost and time, or the line that says the
/// plan's limits are not available. Under them "Manage usage on claude.ai", only while the host opens links.
/// </summary>
internal sealed class UsagePanel : ChatInfoPanel
{
    /// <summary>The page the link opens.</summary>
    internal const string ManageUrl = "https://claude.ai/settings/usage";

    /// <summary>From this share used (a whole percentage) the bar turns clay.</summary>
    internal const int ClayFrom = 80;

    protected override ChatPanel Kind => ChatPanel.Usage;

    protected override double DialogMaxWidth => 400;

    protected override string Title(ChatStrings strings) => strings.AccountUsageTitle;

    protected override string Loading(ChatStrings strings) => strings.UsageLoading;

    protected override string Unavailable(ChatStrings strings) => strings.UsageUnavailable;

    protected override bool Shows(string? propertyName) =>
        propertyName == nameof(ChatConversation.UsageLimits) || propertyName == nameof(ChatConversation.Account)
        || propertyName == nameof(ChatConversation.SessionCostUsd) || propertyName == nameof(ChatConversation.SessionDurationMs) || propertyName == nameof(ChatConversation.SessionApiDurationMs)
        || propertyName == nameof(ChatConversation.UsageError);

    protected override Control? Failure(ChatConversation conversation, ChatStrings strings)
    {
        if (conversation.UsageError == null) return null;
        var retry = new Button { Content = strings.UsageRetry };
        retry.Classes.Add("ClaudeChatButton");
        retry.Classes.Add("ClaudeChatModalRetry");
        retry.Click += (_, _) => conversation.InvokeCommand("usage", "");
        return new StackPanel { Children = { Text(strings.UsageFailed, "ClaudeChatPanelLoading"), retry } };
    }

    protected override Control? Build(ChatConversation conversation, ChatStrings strings)
    {
        var account = conversation.Account;
        var windows = conversation.UsageLimits?.Windows ?? Array.Empty<UsageWindow>();
        var hasTotals = conversation.SessionCostUsd > 0 || conversation.SessionApiDurationMs > 0 || conversation.SessionDurationMs > 0;
        if (account == null && windows.Count == 0 && !hasTotals) return null;

        var body = new StackPanel();
        body.Classes.Add("ClaudeChatModalStack");
        if (account != null) body.Children.Add(AccountSection(account, strings));

        var usage = new StackPanel();
        usage.Classes.Add("ClaudeChatModalSection");
        usage.Children.Add(SectionTitle(strings.AccountUsageSection, "ClaudeChatModalSectionTitle"));
        if (windows.Count > 0)
        {
            var now = conversation.Clock();
            foreach (var window in Ordered(windows)) usage.Children.Add(WindowBar(window, now, strings));
        }
        else if (hasTotals)
        {
            if (conversation.SessionCostUsd > 0) usage.Children.Add(Figure(strings.UsageTotalCost, "$" + strings.FormatMoney(conversation.SessionCostUsd)));
            if (conversation.SessionApiDurationMs > 0) usage.Children.Add(Figure(strings.UsageTotalApiDuration, strings.FormatDuration(conversation.SessionApiDurationMs)));
            if (conversation.SessionDurationMs > 0) usage.Children.Add(Figure(strings.UsageTotalWallDuration, strings.FormatDuration(conversation.SessionDurationMs)));
        }
        else
        {
            usage.Children.Add(Text(strings.UsageNoPlanLimits, "ClaudeChatPanelLoading"));
        }
        body.Children.Add(usage);

        if (conversation.CanOpenLinks)
        {
            var text = Text(strings.UsageManage, "ClaudeChatModalLinkText");
            var link = new Button { Content = text };
            link.Classes.Add("ClaudeChatButton");
            link.Classes.Add("ClaudeChatModalLink");
            link.Click += (_, _) => conversation.RequestLink(ManageUrl);
            body.Children.Add(link);
        }
        return body;
    }

    /// <summary>The session, the week, the week's Sonnet, the models' weeks, then any other window by its name.</summary>
    private static IEnumerable<UsageWindow> Ordered(IReadOnlyList<UsageWindow> windows)
    {
        var order = new[] { "five_hour", "seven_day", "seven_day_sonnet" };
        foreach (var name in order)
        {
            foreach (var window in windows.Where(w => w.Model == null && w.Name == name)) yield return window;
        }
        foreach (var window in windows.Where(w => w.Model != null)) yield return window;
    }

    private static Control AccountSection(AccountInfo account, ChatStrings strings)
    {
        var section = new StackPanel();
        section.Classes.Add("ClaudeChatModalSection");
        section.Children.Add(SectionTitle(strings.AccountSection, "ClaudeChatModalSectionTitle"));
        foreach (var (label, value) in new[]
        {
            (strings.AccountAuthMethod, strings.AuthMethodLabel(account.AuthMethod)), (strings.AccountEmail, account.Email),
            (strings.AccountOrganization, account.Organization), (strings.AccountPlan, account.Plan),
        })
        {
            if (!string.IsNullOrEmpty(value)) section.Children.Add(Figure(label, value!));
        }
        return section;
    }

    /// <summary>A label with its value at the right.</summary>
    private static Control Figure(string label, string value)
    {
        var line = TwoColumns(Text(label, "ClaudeChatModalFigureName"), Text(value, "ClaudeChatModalFigureValue"));
        line.Classes.Add("ClaudeChatModalFigure");
        return line;
    }

    /// <summary>A limit window: its name and the share used (rounded down), a 6 px bar, and when it renews.</summary>
    private static Control WindowBar(UsageWindow window, DateTimeOffset now, ChatStrings strings)
    {
        var share = Math.Max(0, Math.Min(1, window.Utilization));
        var whole = (int)Math.Floor(share * 100 + 1e-9);
        var head = TwoColumns(Text(strings.UsageBarLabel(window), "ClaudeChatModalFigureName"), Text(strings.FormatUsageFloor(window.Utilization), "ClaudeChatModalFigureValue"));
        var bar = Bar("ClaudeChatUsageMeter", new (double, string?, string?)[]
        {
            (share, "ClaudeChatUsageMeterFill", whole >= ClayFrom ? "clay" : null), (1 - share, null, null),
        });
        var block = new StackPanel { Children = { head, bar } };
        block.Classes.Add("ClaudeChatModalWindow");
        if (window.ResetsAt is DateTimeOffset at) block.Children.Add(Text(strings.FormatUsageResetsIn(at, now), "ClaudeChatModalResets"));
        return block;
    }
}
