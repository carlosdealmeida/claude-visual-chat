using System;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Threading;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The extension's long message: a content (the text of a prompt) taller than <see cref="FoldHeight"/> is cut there, fading out over its
/// last <see cref="FadeHeight"/> px, with "Show more" inside it at the bottom right; shown whole (<see cref="IsExpanded"/>), "Show less"
/// sits under it at the right. The content is measured again whenever the width changes, so a narrower view folds a text that fit before.
/// A click on either button changes the height over <see cref="AnimationDuration"/>, ease-in-out; a change from elsewhere (the item, a
/// recycled row) is drawn at once. On the desktop "Show more" shows under the pointer on the bubble or with the keyboard on it, and in the
/// touch layout always, at 44 px (the styles of the theme). It follows the 0.3.1 rule: out of the window an animation stops where it ends;
/// back, the content is drawn as <see cref="IsExpanded"/> is then.
/// </summary>
internal sealed class FoldedText : ContentControl
{
    /// <summary>Whether the content shows whole; the prompt's <see cref="UserMessageItem.IsTextExpanded"/>, bound both ways.</summary>
    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<FoldedText, bool>(nameof(IsExpanded), defaultBindingMode: BindingMode.TwoWay);

    private static readonly StyledProperty<ChatStrings> StringsProperty =
        ClaudeChatView.StringsProperty.AddOwner<FoldedText>();

    private static readonly StyledProperty<ChatLayout> LayoutProperty =
        ClaudeChatView.LayoutProperty.AddOwner<FoldedText>();

    /// <summary>A content taller than this folds, and folded it is cut here.</summary>
    internal const double FoldHeight = 60;

    /// <summary>How much of the folded content fades out, at its bottom.</summary>
    internal const double FadeHeight = 50;

    /// <summary>How long the height takes to change after a click on "Show more" or "Show less".</summary>
    internal static readonly TimeSpan AnimationDuration = TimeSpan.FromSeconds(0.3);

    /// <summary>CSS's ease-in-out.</summary>
    private static readonly SplineEasing EaseInOut = new SplineEasing(0.42, 0, 0.58, 1);

    private static readonly FuncControlTemplate<FoldedText> FoldTemplate = new FuncControlTemplate<FoldedText>((owner, scope) =>
    {
        var presenter = new ContentPresenter
        {
            Name = "PART_ContentPresenter",
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            [!ContentPresenter.ContentProperty] = owner[!ContentProperty],
            [!ContentPresenter.ContentTemplateProperty] = owner[!ContentTemplateProperty],
        };
        presenter.RegisterInNameScope(scope);
        return new FoldBody(owner, presenter, owner.ShowMoreButton, owner.ShowLessButton);
    });

    private bool _attached;
    private bool _clicked;         // the change of IsExpanded under way is the user's click: it animates
    private bool _isFolded;
    private double _natural;       // the content's whole height at the width it has
    private double _shown;         // the height shown, which the animation moves
    private bool _animating;
    private double _from;          // where the animation started
    private TimeSpan? _start;      // the render clock at its first frame
    private double _progress;      // 0 to 1
    private double _fadeFor = double.NaN; // the height the fade below was made for
    private LinearGradientBrush? _fade;
    private FoldBody? _body;

    /// <summary>
    /// Ends an animation whose frames do not come (a host that draws no frames, or draws them late): the height is then where the
    /// animation would have ended, a little after its time.
    /// </summary>
    private readonly DispatcherTimer _backstop = new DispatcherTimer { Interval = AnimationDuration + TimeSpan.FromMilliseconds(100) };

    public FoldedText()
    {
        ShowMoreButton = FoldButton("ClaudeChatShowMore");
        ShowLessButton = FoldButton("ClaudeChatShowLess");
        ShowMoreButton.Click += (_, _) => Toggle(true);
        ShowLessButton.Click += (_, _) => Toggle(false);
        Template = FoldTemplate;
        _backstop.Tick += (_, _) => EndAnimation();
        Label();
    }

    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>"Show more": inside the cut content, at its bottom right.</summary>
    internal Button ShowMoreButton { get; }

    /// <summary>"Show less": under the whole content, at the right.</summary>
    internal Button ShowLessButton { get; }

    /// <summary>True while the content is taller than <see cref="FoldHeight"/>, so it folds (open or not).</summary>
    internal bool IsFolded => _isFolded;

    /// <summary>True while the height changes after a click.</summary>
    internal bool IsAnimatingHeight => _animating;

    /// <summary>True while the backstop waits to end the animation.</summary>
    internal bool IsBackstopWaiting => _backstop.IsEnabled;

    /// <summary>The backstop's tick: an animation still under way ends where it was going.</summary>
    internal void EndAnimation()
    {
        var was = _animating;
        StopAnimation();
        if (was && _attached) _body?.InvalidateMeasure();
    }

    private void StopAnimation()
    {
        _animating = false;
        _backstop.Stop();
    }

    private ChatStrings Strings => GetValue(StringsProperty) ?? ChatStrings.English;

    private static Button FoldButton(string kind)
    {
        var button = new Button { IsVisible = false };
        button.Classes.Add("ClaudeChatButton");
        button.Classes.Add("ClaudeChatFoldButton");
        button.Classes.Add(kind);
        return button;
    }

    protected override void OnApplyTemplate(Avalonia.Controls.Primitives.TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _body = null;
        foreach (var child in VisualChildren)
            if (child is FoldBody body) _body = body;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        _body?.InvalidateMeasure(); // drawn as it is now
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        StopAnimation(); // a frame that comes later finds nothing to do, and the backstop does not wait; back, the height is where the animation would have ended
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        StopAnimation(); // the list reuses the row for another prompt: drawn at once
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsExpandedProperty)
        {
            if (_clicked && _attached && _isFolded) Animate();
            else StopAnimation();
            _body?.InvalidateMeasure();
        }
        else if (change.Property == StringsProperty)
        {
            Label();
        }
        else if (change.Property == LayoutProperty)
        {
            PseudoClasses.Set(":touch", GetValue(LayoutProperty) == ChatLayout.Touch);
        }
    }

    private void Label()
    {
        ShowMoreButton.Content = Strings.ShowMore;
        ShowLessButton.Content = Strings.ShowLess;
    }

    /// <summary>A click on "Show more" or "Show less": the item's state changes, and the height follows over 0.3 s.</summary>
    private void Toggle(bool expand)
    {
        _clicked = true;
        try
        {
            SetCurrentValue(IsExpandedProperty, expand);
        }
        finally
        {
            _clicked = false;
        }
    }

    private void Animate()
    {
        _from = _shown;
        _start = null;
        _progress = 0;
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return; // no frames to move it: drawn at once
        _backstop.Stop();
        _backstop.Start(); // the whole time again, from here
        if (_animating) return; // a frame is already asked for
        _animating = true;
        top.RequestAnimationFrame(OnFrame);
    }

    private void OnFrame(TimeSpan time)
    {
        if (!_animating || !_attached) return;
        _start ??= time;
        _progress = Math.Min(1, (time - _start.Value).TotalSeconds / AnimationDuration.TotalSeconds);
        if (_progress >= 1) StopAnimation();
        _body?.InvalidateMeasure();
        if (_animating) TopLevel.GetTopLevel(this)?.RequestAnimationFrame(OnFrame);
    }

    /// <summary>The height the content shows now: whole, cut at 60 px, or on the way between them.</summary>
    private double ShownHeight()
    {
        if (!_isFolded) return _natural;
        var target = IsExpanded ? _natural : FoldHeight;
        if (!_animating) return target;
        return _from + (target - _from) * EaseInOut.Ease(_progress);
    }

    /// <summary>The template's panel measures the content at its whole height, then shows as much of it as the fold lets.</summary>
    internal Size MeasureBody(ContentPresenter presenter, Size available)
    {
        presenter.Measure(new Size(available.Width, double.PositiveInfinity));
        _natural = presenter.DesiredSize.Height;
        _isFolded = _natural > FoldHeight + 0.5;
        if (!_isFolded) StopAnimation();
        var expanded = IsExpanded;
        ShowMoreButton.IsVisible = _isFolded && !expanded;
        ShowLessButton.IsVisible = _isFolded && expanded;
        ShowMoreButton.Measure(available);
        ShowLessButton.Measure(available);
        _shown = ShownHeight();
        var width = presenter.DesiredSize.Width;
        var height = _shown;
        if (ShowMoreButton.IsVisible) width = Math.Max(width, ShowMoreButton.DesiredSize.Width);
        if (ShowLessButton.IsVisible)
        {
            width = Math.Max(width, ShowLessButton.DesiredSize.Width);
            height += ShowLessButton.DesiredSize.Height;
        }
        return new Size(width, height);
    }

    internal void ArrangeBody(ContentPresenter presenter, Size size)
    {
        var width = size.Width;
        var shown = _shown;
        presenter.Arrange(new Rect(0, 0, width, _natural));
        var cut = _isFolded && shown < _natural - 0.5;
        presenter.Clip = cut ? new RectangleGeometry(new Rect(0, 0, width, shown)) : null;
        presenter.OpacityMask = _isFolded && !IsExpanded ? Fade(shown) : null;
        var more = ShowMoreButton.DesiredSize;
        ShowMoreButton.Arrange(new Rect(width - more.Width, shown - more.Height, more.Width, more.Height));
        var less = ShowLessButton.DesiredSize;
        ShowLessButton.Arrange(new Rect(width - less.Width, shown, less.Width, less.Height));
    }

    /// <summary>The content fades out over its last 50 px: opaque down to there, gone at the cut.</summary>
    private LinearGradientBrush Fade(double shown)
    {
        if (_fade != null && _fadeFor.Equals(shown)) return _fade;
        _fadeFor = shown;
        _fade = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, Math.Max(0, shown - FadeHeight), RelativeUnit.Absolute),
            EndPoint = new RelativePoint(0, shown, RelativeUnit.Absolute),
            GradientStops = { new GradientStop(Colors.Black, 0), new GradientStop(Colors.Transparent, 1) },
        };
        return _fade;
    }
}

/// <summary>The panel of <see cref="FoldedText"/>'s template: the content, "Show more" over its cut and "Show less" under it.</summary>
internal sealed class FoldBody : Panel
{
    private readonly FoldedText _owner;
    private readonly ContentPresenter _presenter;

    public FoldBody(FoldedText owner, ContentPresenter presenter, Button showMore, Button showLess)
    {
        _owner = owner;
        _presenter = presenter;
        // the buttons are the owner's: a template applied again takes them from the panel of the one before
        (showMore.Parent as Panel)?.Children.Remove(showMore);
        (showLess.Parent as Panel)?.Children.Remove(showLess);
        Children.Add(presenter);
        Children.Add(showMore);
        Children.Add(showLess);
    }

    protected override Size MeasureOverride(Size availableSize) => _owner.MeasureBody(_presenter, availableSize);

    protected override Size ArrangeOverride(Size finalSize)
    {
        _owner.ArrangeBody(_presenter, finalSize);
        return finalSize;
    }
}
