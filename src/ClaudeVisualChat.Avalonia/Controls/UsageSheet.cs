using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The usage sheet of the touch layout, in the style of the "Usage" sheet of Anthropic's Claude app; it takes the place of the
/// <c>/context</c> and <c>/usage</c> panels, which stay hidden in that layout (<see cref="ChatInfoPanel"/>). It lives from the
/// conversation's panel (<see cref="ChatConversation.ShownPanel"/> is <see cref="ChatPanel.Context"/> or <see cref="ChatPanel.Usage"/>:
/// the context ring of the reply box, the command palette and the host set it): it opens when the panel does, while no permission
/// request waits, and the ✕, the veil, Esc and the host's back button end the panel (<see cref="ChatConversation.ClosePanel"/>),
/// which closes the sheet. Like the panels it replaces, it is information only, so it shows in a read-only view too.
/// <para>
/// The header has the ✕, "Usage" and an action ↻ that asks for the data again: the commands <c>context</c> and <c>usage</c>, one after the
/// other, which the host answers; the sheet stays, since both panels land on it. Under "Context" comes the card with the context
/// window: its name and the share used, a bar of the used categories in the colors of the <c>/context</c> panel and the tokens of
/// the window; a tap opens and closes the category by category lines (the claude's own names, the tokens, the share of the window,
/// and "—" for a deferred category, which is not in the window). Under the card comes what the host puts there
/// (<see cref="HostContent"/>, shown by a <see cref="ContentControl"/> so the host's data templates make it), or, without it, "Plan
/// usage" with a card per limit window the claude reported: its name, the share used, a bar and when it renews. While the claude has sent no
/// context data, a line says so ("…" while it works).
/// </para>
/// </summary>
internal sealed class UsageSheet : ChatSheet
{
    /// <summary>What a category shows where its share of the window would be, when it has none: a deferred one is not in the window.</summary>
    private const string NoShare = "—";

    /// <summary>The host's content: one control for the life of the sheet, so a control the host gives is never in two places at once.</summary>
    private readonly ContentControl _host = new ContentControl();

    /// <summary>The category lines of the card, and the chevron that tells whether they are open.</summary>
    private Control? _categories;
    private Path? _chevron;

    /// <summary>True while the category lines are open; they start closed each time the sheet opens.</summary>
    private bool _expanded;

    public UsageSheet()
    {
        Closed += (_, _) => _expanded = false;
    }

    /// <summary>What the host shows under the context window instead of the plan's limits; null for the limits.</summary>
    internal object? HostContent
    {
        get => _host.Content;
        set
        {
            _host.Content = value;
            Rebuild();
        }
    }

    protected override bool FollowsState => true;

    protected override bool CanOpen(ChatConversation conversation) => conversation.ShownPanel is ChatPanel.Context or ChatPanel.Usage;

    protected override bool Shows(string? propertyName) =>
        propertyName is nameof(ChatConversation.ShownPanel) or nameof(ChatConversation.ContextUsage)
            or nameof(ChatConversation.UsageLimits) or nameof(ChatConversation.IsBusy);

    protected override string Title(ChatStrings strings) => strings.UsageTitle;

    /// <summary>
    /// The panel is the reason of the sheet: closing it is closing the panel, and the sheet follows it, so the sheet never hides
    /// while the panel stays open. A panel that is already closed says nothing when it is closed again (it was, while the view was
    /// out of the window, and a request waits now), so the sheet closes itself after it as well: in the usual case it has followed
    /// the panel by then, and that is a no-op.
    /// </summary>
    protected override void Dismiss()
    {
        Conversation?.ClosePanel();
        Close();
    }

    protected override Button? HeaderAction(ChatConversation conversation, ChatStrings strings)
    {
        var icon = new Path();
        icon.Classes.Add("ClaudeChatSheetRefreshIcon");
        return HeaderButton(icon, strings.Refresh, () => AskAgain(conversation));
    }

    /// <summary>The host answers each command with fresh data; both panels land on this sheet, which stays open.</summary>
    private static void AskAgain(ChatConversation conversation)
    {
        conversation.InvokeCommand("context", "");
        conversation.InvokeCommand("usage", "");
    }

    protected override void Fill(Panel body, ChatConversation conversation, ChatStrings strings)
    {
        _categories = null;
        _chevron = null;
        body.Children.Add(Label(strings.ContextSection));
        body.Children.Add(Group(new[]
        {
            conversation.ContextUsage is { } usage
                ? ContextCard(usage, strings)
                : ChatInfoPanel.Text(conversation.IsBusy ? strings.PanelLoading : strings.ContextUnavailable, "ClaudeChatSheetNote"),
        }));

        if (_host.Content != null)
        {
            body.Children.Add(_host);
        }
        else if (conversation.UsageLimits is { Windows.Count: > 0 } limits)
        {
            body.Children.Add(Label(strings.PlanUsage));
            var now = conversation.Clock();
            foreach (var window in limits.Windows) body.Children.Add(Group(new[] { WindowCard(window, now, strings) }));
        }
    }

    // ---- the context window ----

    /// <summary>The card, a button that opens and closes the lines, and the lines under it: the claude's categories.</summary>
    private Control ContextCard(ContextUsage usage, ChatStrings strings)
    {
        var name = ChatInfoPanel.Text(strings.ContextWindow, "ClaudeChatSheetName");
        var percent = strings.FormatPercentUsed(ContextRing.UsedOf(usage));
        var share = ChatInfoPanel.Text(percent, "ClaudeChatSheetValue");
        _chevron = new Path();
        _chevron.Classes.Add("ClaudeChatSheetChevron");
        Grid.SetColumn(share, 1);
        Grid.SetColumn(_chevron, 2);
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Children = { name, share, _chevron } };

        var body = new StackPanel { Children = { head, ChatInfoPanel.Bar("ClaudeChatSheetBar", ContextPanel.BarPieces(usage)) } };
        body.Classes.Add("ClaudeChatSheetCardBody");
        var spoken = strings.ContextWindow + ", " + percent;
        if (usage.MaxTokens > 0) // a window the claude did not report has no "of"
        {
            var tokens = strings.FormatTokensOfWindow(usage.TotalTokens, usage.MaxTokens);
            body.Children.Add(ChatInfoPanel.Text(tokens, "ClaudeChatSheetCaption"));
            spoken += ", " + tokens;
        }

        var card = Tappable(body, Toggle, spoken, 44);
        card.Classes.Add("ClaudeChatSheetCard");
        _categories = Categories(usage, strings);
        ShowCategories();
        return new StackPanel { Children = { card, _categories } };
    }

    /// <summary>The card was tapped: the lines open or close where they are, so the card keeps the keyboard it has.</summary>
    private void Toggle()
    {
        _expanded = !_expanded;
        ShowCategories();
    }

    private void ShowCategories()
    {
        if (_categories != null) _categories.IsVisible = _expanded;
        _chevron?.Classes.Set("expanded", _expanded);
    }

    /// <summary>
    /// A line per category, in the /context panel's order (the used ones, the free space, the deferred ones): its dot, the name the
    /// claude gave it, its tokens and its share of the window, in columns that line up from one line to the next.
    /// </summary>
    private static Control Categories(ContextUsage usage, ChatStrings strings)
    {
        var lines = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), IsVisible = false };
        lines.Classes.Add("ClaudeChatSheetCategories");
        var used = 0;
        foreach (var category in usage.Categories.Where(c => c.Kind == "used")
                     .Concat(usage.Categories.Where(c => c.Kind == "free"))
                     .Concat(usage.Categories.Where(c => c.Kind != "used" && c.Kind != "free")))
        {
            string? color = null;
            switch (category.Kind)
            {
                case "used":
                    color = ContextPanel.ColorOf(used++);
                    break;
                case "free":
                    color = "free"; // the track's color
                    break;
            }
            var inWindow = color != null; // a deferred category, or one of a kind it does not know, is not in the window: no color, no share

            var row = lines.RowDefinitions.Count;
            lines.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var dot = new Border();
            dot.Classes.Add("ClaudeChatContextSwatch"); // the /context panel's dot and colors…
            dot.Classes.Add("ClaudeChatSheetDot");      // …at 8 px
            if (color != null) dot.Classes.Add(color);
            var name = ChatInfoPanel.Text(category.Name, "ClaudeChatSheetCategory");
            name.Classes.Set("muted", !inWindow);
            var tokens = ChatInfoPanel.Text(strings.FormatShortTokens(category.Tokens), "ClaudeChatSheetAmount");
            var percent = ChatInfoPanel.Text(
                inWindow && usage.MaxTokens > 0 ? strings.FormatSharePercent((double)category.Tokens / usage.MaxTokens) : NoShare,
                "ClaudeChatSheetAmount");
            Place(lines, row, dot, name, tokens, percent);
        }
        return lines;
    }

    private static void Place(Grid lines, int row, params Control[] cells)
    {
        for (var column = 0; column < cells.Length; column++)
        {
            Grid.SetRow(cells[column], row);
            Grid.SetColumn(cells[column], column);
            lines.Children.Add(cells[column]);
        }
    }

    // ---- the plan's limits ----

    /// <summary>A limit window: its name, the share used, a bar filled up to it and, when the claude says, when it renews.</summary>
    private static Control WindowCard(UsageWindow window, DateTimeOffset now, ChatStrings strings)
    {
        var name = ChatInfoPanel.Text(strings.UsageWindowLabel(window.Name), "ClaudeChatSheetName");
        var percent = ChatInfoPanel.Text(strings.FormatPercentUsed(window.Utilization * 100), "ClaudeChatSheetValue");
        Grid.SetColumn(percent, 1);
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { name, percent } };
        var used = Math.Max(0, Math.Min(1, window.Utilization));
        var bar = ChatInfoPanel.Bar("ClaudeChatSheetBar", new (double, string?, string?)[] { (used, "ClaudeChatUsageFill", null), (1 - used, null, null) });

        var body = new StackPanel { Children = { head, bar } };
        body.Classes.Add("ClaudeChatSheetCardBody");
        if (window.ResetsAt is DateTimeOffset at) body.Children.Add(ChatInfoPanel.Text(strings.FormatUsageResets(at, now), "ClaudeChatSheetCaption"));
        var card = new Border { Child = body };
        card.Classes.Add("ClaudeChatSheetBlock");
        return card;
    }
}
