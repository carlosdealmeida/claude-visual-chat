using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The card of a compaction, like the extension's: one italic line in the secondary color, "Compacted chat · manual · 36k tokens freed",
/// with a chevron of 12 px, closed until it is opened to the summary the claude wrote of the conversation (Markdown, 8 px under the
/// line). A compaction whose summary never came says "Conversation was compacted to free up context." there. Whether it is open is the
/// item's (<see cref="CompactionItem.IsExpanded"/>), so a row of the list reused for another item shows that item's card; the card
/// follows its item only while it is in the window.
/// </summary>
internal sealed class CompactionCard : Border
{
    private static readonly StyledProperty<ChatStrings> StringsProperty = ClaudeChatView.StringsProperty.AddOwner<CompactionCard>();

    private readonly ToggleButton _head = new ToggleButton();
    private readonly TextBlock _label = new TextBlock();
    private readonly MarkdownBlock _summary = new MarkdownBlock();

    private CompactionItem? _tracked;
    private bool _attached;

    public CompactionCard()
    {
        _head.Classes.Add("ClaudeChatDisclosure");
        _head.Classes.Add("ClaudeChatCompactionHead");
        _head.HorizontalAlignment = HorizontalAlignment.Left;
        _label.Classes.Add("ClaudeChatCompactionLabel");
        var chevron = new Path();
        chevron.Classes.Add("ClaudeChatChevron");
        chevron.Classes.Add("small");
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Classes.Add("ClaudeChatCompactionLine");
        line.Children.Add(_label);
        line.Children.Add(chevron);
        _head.Content = line;
        _head.Click += (_, _) =>
        {
            if (_tracked != null) _tracked.IsExpanded = _head.IsChecked == true;
        };
        _summary.Classes.Add("ClaudeChatCompactionSummary");
        _summary.IsVisible = false;
        Classes.Add("ClaudeChatCompaction");
        Child = new StackPanel { Children = { _head, _summary } };
    }

    /// <summary>The head: the line that opens and closes the card.</summary>
    internal ToggleButton Head => _head;

    /// <summary>The line in the head.</summary>
    internal TextBlock Label => _label;

    /// <summary>The summary, drawn only while the card is open.</summary>
    internal MarkdownBlock Summary => _summary;

    /// <summary>True while the card follows its item (only while in the window).</summary>
    internal bool IsTracking => _tracked != null;

    private ChatStrings Strings => GetValue(StringsProperty) ?? ChatStrings.English;

    /// <summary>Styled as a Border: the theme's <c>Border.ClaudeChatCompaction</c>.</summary>
    protected override Type StyleKeyOverride => typeof(Border);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Track(DataContext as CompactionItem);
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
        if (_attached) Track(DataContext as CompactionItem);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StringsProperty && _attached) Update();
    }

    private void Track(CompactionItem? item)
    {
        if (!ReferenceEquals(item, _tracked))
        {
            if (_tracked != null) _tracked.PropertyChanged -= OnItemChanged;
            _tracked = item;
            if (_tracked != null) _tracked.PropertyChanged += OnItemChanged;
        }
        // out of the window, only the events go: Avalonia is taking the tree apart, and coming back draws it again
        if (_attached) Update();
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e) => Update();

    private void Update()
    {
        var item = _tracked;
        if (item == null) return;
        var strings = Strings;
        var title = strings.FormatCompaction(item.Trigger, item.TokensBefore);
        _label.Text = title;
        AutomationProperties.SetName(_head, title);
        _head.IsChecked = item.IsExpanded;
        _summary.IsVisible = item.IsExpanded;
        // the Markdown is read only while the card is open: a closed card of a long summary costs the list nothing
        _summary.Markdown = item.IsExpanded ? (string.IsNullOrEmpty(item.Summary) ? strings.CompactionNoSummary : item.Summary) : null;
    }
}
