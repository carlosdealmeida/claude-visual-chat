using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The divider of a command that printed one line, like the extension's: the text in the middle at .9em, cut with "…" when it does
/// not fit, and the date line's wavy rule on each side (<see cref="DateSeparator.DrawWave"/>, laid out with the same measures). A
/// label that is cut, or any label with a shorter one (<see cref="DividerItem.ShortText"/>) in a view of 330 px or less, can be tapped,
/// or pressed with Enter or Space, to show the whole text in as many lines as it takes, and again to close it. Whether it is open is the
/// item's (<see cref="DividerItem.IsOpen"/>), so a row of the list reused for another item shows that item's divider; the divider follows
/// its item only while it is in the window.
/// </summary>
internal sealed class DividerLine : Control
{
    /// <summary>The color of the text and of the rules.</summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<DividerLine>();

    private static readonly StyledProperty<bool> IsTinyProperty = ClaudeChatView.IsTinyProperty.AddOwner<DividerLine>();

    private readonly TextBlock _label = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };

    private bool _attached;
    private DividerItem? _item;
    private bool _openable;
    private Rect _leftRule;
    private Rect _rightRule;

    public DividerLine()
    {
        _label.Classes.Add("ClaudeChatDateLabel");
        _label.Classes.Add("ClaudeChatDividerLabel");
        _label.TextAlignment = TextAlignment.Center;
        LogicalChildren.Add(_label);
        VisualChildren.Add(_label);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>The text shown.</summary>
    internal TextBlock Label => _label;

    /// <summary>True while the divider follows its item and its view (only while in the window).</summary>
    internal bool IsTracking => _item != null;

    /// <summary>Whether a tap shows (or hides) the whole text: the label is cut, or has a shorter one in a narrow view, or is open.</summary>
    internal bool IsOpenable => _openable;

    /// <summary>Where the rules are drawn, in the divider's own space.</summary>
    internal Rect LeftRule => _leftRule;

    internal Rect RightRule => _rightRule;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Track();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        Track();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_attached) Track();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ForegroundProperty) InvalidateVisual();
        else if (change.Property == IsTinyProperty && _attached) Update();
    }

    /// <summary>Follows the item; out of the window, lets go of it: coming back draws the divider as it is then.</summary>
    private void Track()
    {
        var item = _attached ? DataContext as DividerItem : null;
        if (!ReferenceEquals(item, _item))
        {
            if (_item != null) _item.PropertyChanged -= OnItemChanged;
            _item = item;
            if (_item != null) _item.PropertyChanged += OnItemChanged;
        }
        if (_attached) Update();
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e) => Update();

    private void Update()
    {
        var item = _item;
        if (item == null) return;
        var short_ = GetValue(IsTinyProperty) && !item.IsOpen && item.ShortText != null;
        _label.Text = short_ ? item.ShortText : item.Text;
        _label.TextWrapping = item.IsOpen ? TextWrapping.Wrap : TextWrapping.NoWrap;
        _label.TextTrimming = item.IsOpen ? TextTrimming.None : TextTrimming.CharacterEllipsis;
        AutomationProperties.SetName(this, item.Text);
        InvalidateMeasure();
    }

    private void SetOpenable(bool value)
    {
        if (value == _openable) return;
        _openable = value;
        Focusable = value;
        Cursor = value ? new Cursor(StandardCursorType.Hand) : null;
    }

    private void Toggle()
    {
        if (_item != null && _openable) _item.IsOpen = !_item.IsOpen;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton != MouseButton.Left || !_openable) return;
        if (!new Rect(Bounds.Size).Contains(e.GetPosition(this))) return;
        Toggle();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if ((e.Key != Key.Enter && e.Key != Key.Space) || !_openable) return;
        Toggle();
        e.Handled = true;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var rules = 2 * (DateSeparator.MinRuleWidth + DateSeparator.Gap);
        var room = Math.Max(0, availableSize.Width - rules);
        var open = _item?.IsOpen == true;
        if (open)
        {
            SetOpenable(true); // once open it can be closed, whatever the width
        }
        else
        {
            // cut: the text on one line is wider than the room the rules leave
            _label.Measure(Size.Infinity);
            var cut = !double.IsInfinity(room) && _label.DesiredSize.Width > room + 0.5;
            SetOpenable(cut || (GetValue(IsTinyProperty) && _item?.ShortText != null));
        }
        _label.Measure(new Size(room, double.PositiveInfinity));
        var height = Math.Max(_label.DesiredSize.Height, DateSeparator.WaveHeight) + 2 * DateSeparator.VerticalPadding;
        var width = double.IsInfinity(availableSize.Width) ? _label.DesiredSize.Width + rules : availableSize.Width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var labelWidth = Math.Min(_label.DesiredSize.Width, Math.Max(0, finalSize.Width - 2 * (DateSeparator.MinRuleWidth + DateSeparator.Gap)));
        var labelHeight = _label.DesiredSize.Height;
        var middle = finalSize.Height / 2;
        var labelLeft = (finalSize.Width - labelWidth) / 2;
        _label.Arrange(new Rect(labelLeft, middle - labelHeight / 2, labelWidth, labelHeight));
        var ruleWidth = Math.Max(0, labelLeft - DateSeparator.Gap);
        _leftRule = new Rect(0, middle - DateSeparator.WaveHeight / 2, ruleWidth, DateSeparator.WaveHeight);
        _rightRule = new Rect(finalSize.Width - ruleWidth, middle - DateSeparator.WaveHeight / 2, ruleWidth, DateSeparator.WaveHeight);
        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size)); // the whole line takes the tap
        if (Foreground is not { } brush) return;
        DateSeparator.DrawWave(context, brush, _leftRule);
        DateSeparator.DrawWave(context, brush, _rightRule);
    }
}
