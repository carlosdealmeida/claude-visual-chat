using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ClaudeVisualChat.Protocol;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The card of a message that did not come from the user, like the extension's: one italic line in the secondary color, "Message from
/// @maria:" and the first words of the message, then a chevron of 16 px, closed until it is opened to the sender's full name, the
/// attributes and the message with its line breaks. A message whose sender the claude did not record says "Sender not recorded: …" in
/// one line, and opens to the raw attributes and parts; the notice of a task opens to a list of names and values. Whether it is open is the
/// item's (<see cref="ExternalMessageItem.IsExpanded"/>), so a row of the list reused for another item shows that item's card; the card
/// follows its item only while it is in the window.
/// </summary>
internal sealed class ExternalMessageCard : Border
{
    private static readonly StyledProperty<ChatStrings> StringsProperty = ClaudeChatView.StringsProperty.AddOwner<ExternalMessageCard>();

    /// <summary>The gap between the names and the values of a list, as the extension draws it (4 px between rows, 12 between columns).</summary>
    private const double ColumnGap = 12;

    private const double RowGap = 4;

    private readonly ToggleButton _head = new ToggleButton();
    private readonly Grid _line = new Grid();
    private readonly TextBlock _label = new TextBlock();
    private readonly TextBlock _preview = new TextBlock();
    private readonly StackPanel _details = new StackPanel();

    private ExternalMessageItem? _tracked;
    private bool _attached;

    public ExternalMessageCard()
    {
        _head.Classes.Add("ClaudeChatDisclosure");
        _head.Classes.Add("ClaudeChatExternalHead");
        _head.HorizontalAlignment = HorizontalAlignment.Stretch;
        _line.Classes.Add("ClaudeChatExternalLine");
        _line.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        _line.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        _line.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        _label.Classes.Add("ClaudeChatExternalText");
        _label.Classes.Add("ClaudeChatExternalLabel");
        _preview.Classes.Add("ClaudeChatExternalText");
        _preview.Classes.Add("ClaudeChatExternalPreview");
        _preview.Margin = new Thickness(4, 0, 0, 0);
        var chevron = new Path { Margin = new Thickness(4, 0, 0, 0) };
        chevron.Classes.Add("ClaudeChatChevron");
        Grid.SetColumn(_label, 0);
        Grid.SetColumn(_preview, 1);
        Grid.SetColumn(chevron, 2);
        _line.Children.Add(_label);
        _line.Children.Add(_preview);
        _line.Children.Add(chevron);
        _head.Content = _line;
        _head.Click += (_, _) =>
        {
            if (_tracked != null) _tracked.IsExpanded = _head.IsChecked == true;
        };
        _details.IsVisible = false;
        Classes.Add("ClaudeChatExternal");
        Child = new StackPanel { Children = { _head, _details } };
    }

    /// <summary>The head: the line that opens and closes the card.</summary>
    internal ToggleButton Head => _head;

    /// <summary>The words of the line before the first words of the message.</summary>
    internal TextBlock Label => _label;

    /// <summary>The first words of the message, in the line; hidden when the label already holds them.</summary>
    internal TextBlock PreviewText => _preview;

    /// <summary>True while the card follows its item (only while in the window).</summary>
    internal bool IsTracking => _tracked != null;

    private ChatStrings Strings => GetValue(StringsProperty) ?? ChatStrings.English;

    /// <summary>Styled as a Border: the theme's <c>Border.ClaudeChatExternal</c>.</summary>
    protected override Type StyleKeyOverride => typeof(Border);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Track(DataContext as ExternalMessageItem);
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
        if (_attached) Track(DataContext as ExternalMessageItem);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StringsProperty && _attached) Update();
    }

    private void Track(ExternalMessageItem? item)
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
        var hasPreview = item.Preview.Length > 0;
        if (item.Sender != null)
        {
            // the sender is known: "Message from @maria:" and the first words after it
            _label.Text = strings.FormatMessageFrom(item.Kind, item.Sender, hasPreview);
            _preview.Text = item.Preview;
            _preview.IsVisible = hasPreview;
            SetLabelFlow(false);
        }
        else
        {
            // a wrapped notice says what it is; any other message does not know its sender, and holds its first words in the line
            _label.Text = strings.TaggedLabel(item.Kind) ?? strings.FormatSenderNotRecorded(item.Preview);
            _preview.Text = "";
            _preview.IsVisible = false;
            SetLabelFlow(true);
        }
        AutomationProperties.SetName(_head, hasPreview && item.Sender != null ? _label.Text + " " + item.Preview : _label.Text);
        _head.IsChecked = item.IsExpanded;
        _details.IsVisible = item.IsExpanded;
        // what opens is built only while the card is open: a closed card of a long message costs the list nothing
        _details.Children.Clear();
        if (item.IsExpanded) FillDetails(item, strings);
    }

    /// <summary>A label that stands for the whole line fills it and is cut with "…"; one before the first words never gives way.</summary>
    private void SetLabelFlow(bool fills)
    {
        Grid.SetColumnSpan(_label, fills ? 2 : 1);
        _label.TextTrimming = fills ? TextTrimming.CharacterEllipsis : TextTrimming.None;
    }

    // ---- what opens ----

    private void FillDetails(ExternalMessageItem item, ChatStrings strings)
    {
        if (item.Sender != null)
        {
            if (item.SenderName != null) _details.Children.Add(Sender(strings.FormatMessageFromSender(item.SenderName)));
            if (ExtraAttributes(item) is { Length: > 0 } extra) _details.Children.Add(Sender(extra));
            _details.Children.Add(Body(item.Body));
            return;
        }
        if (item.UsesColumns)
        {
            _details.Children.Add(Columns(item.Attributes.Count > 0 ? AttributeLine(item, false) : null, item.Sections, strings));
            return;
        }
        if (item.Attributes.Count > 0) _details.Children.Add(Sender(AttributeLine(item, false)));
        foreach (var section in item.Sections)
        {
            var label = strings.TaggedLabel(section.Label);
            var text = section.Text.Length == 0 || section.Text.Trim().Length == 0 ? strings.TaggedNothing : section.Text;
            _details.Children.Add(Body(text, label == null ? null : label + ": "));
        }
    }

    /// <summary>The attributes of a channel's tag beyond its source, which the line already names; none for the other sorts of message.</summary>
    private static string? ExtraAttributes(ExternalMessageItem item) =>
        item.Kind == TaggedMessage.ChannelKind && item.Attributes.Count > 1 ? AttributeLine(item, true) : null;

    /// <summary>The attributes as the tag wrote them, <c>key="value"</c> and a space between.</summary>
    private static string AttributeLine(ExternalMessageItem item, bool withoutSource)
    {
        var sb = new StringBuilder();
        foreach (var attribute in item.Attributes)
        {
            if (withoutSource && attribute.Key == "source") continue;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(attribute.Key).Append("=\"").Append(attribute.Value).Append('"');
        }
        return sb.ToString();
    }

    private static SelectableTextBlock Sender(string text)
    {
        var block = new SelectableTextBlock { Text = text };
        block.Classes.Add("ClaudeChatExternalText");
        block.Classes.Add("ClaudeChatExternalSender");
        return block;
    }

    /// <summary>A part of the message as it was written, line breaks kept, after its name in italic when it has one.</summary>
    private static SelectableTextBlock Body(string text, string? name = null)
    {
        var block = new SelectableTextBlock();
        if (name == null)
        {
            block.Text = text;
        }
        else
        {
            block.Inlines!.Add(new Run(name) { FontStyle = FontStyle.Italic });
            block.Inlines.Add(new Run(text));
        }
        block.Classes.Add("ClaudeChatExternalText");
        block.Classes.Add("ClaudeChatExternalBody");
        return block;
    }

    /// <summary>
    /// A list of two columns, the names in italic: 4 px between the rows and 12 between the columns. A part that is a list of its own
    /// (the usage of a task) is drawn inside the value, behind a rule of 1 px and 8 px of padding.
    /// </summary>
    private static Grid Columns(string? attributes, IReadOnlyList<TaggedSection> sections, ChatStrings strings, bool nested = false)
    {
        var grid = new Grid { Margin = new Thickness(0, nested ? 0 : 4, 0, 0) };
        grid.Classes.Add("ClaudeChatExternalColumns");
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(ColumnGap, GridUnitType.Pixel));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        if (attributes != null) AddRow(grid, null, new SelectableTextBlock { Text = attributes });
        foreach (var section in sections)
        {
            var name = strings.TaggedLabel(section.Label) ?? section.Label;
            Control value;
            if (section.Lines is { Count: > 0 } lines)
            {
                var inner = new Border { Child = Columns(null, lines, strings, true) };
                inner.Classes.Add("ClaudeChatExternalNested");
                value = inner;
            }
            else
            {
                value = new SelectableTextBlock { Text = section.Text.Trim().Length == 0 ? strings.TaggedNothing : section.Text };
            }
            AddRow(grid, name == null ? "" : name + ":", value);
        }
        return grid;
    }

    private static void AddRow(Grid grid, string? name, Control value)
    {
        var row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var top = row == 0 ? 0 : RowGap;
        var nameBlock = new TextBlock { Text = name ?? "", Margin = new Thickness(0, top, 0, 0) };
        nameBlock.Classes.Add("ClaudeChatExternalText");
        nameBlock.Classes.Add("ClaudeChatExternalName");
        Grid.SetRow(nameBlock, row);
        grid.Children.Add(nameBlock);
        if (value is TextBlock text)
        {
            text.Classes.Add("ClaudeChatExternalText");
            text.Classes.Add("ClaudeChatExternalValue");
        }
        value.Margin = new Thickness(0, top, 0, 0);
        Grid.SetRow(value, row);
        Grid.SetColumn(value, 2);
        grid.Children.Add(value);
    }
}
