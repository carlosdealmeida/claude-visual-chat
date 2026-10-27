using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The context ring of the reply box's bar, like the extension's (2.1.309) pie: a 20 px drawing of a 5 px radius circle
/// with a 1.5 px round stroke, a track at 15 % of the text color and a clay arc that fills clockwise from the top in
/// three steps, 50, 75 and 99 % (under 62.5 %, under 87 %, from there), by <see cref="ChatConversation.ContextUsage"/>.
/// Like the extension's, it shows only once more than half of the window is used. The tooltip says how much is left until
/// auto-compact (the extension's words); a click opens the Context panel (<c>InvokeCommand("context")</c>, so the host
/// asks for the data). The extension compacts on a click; a stray click here must not compact the conversation.
/// Hidden, taking no room, below that or while the claude has reported no usage with a window. The touch layout's ring
/// shows from the first token on: its <see cref="MinimumPercent"/> is 0, and its arc draws the share used as it is, not in the
/// steps (15 % used is 15 % of the circle); and it asks for nothing while a permission request waits.
/// </summary>
internal sealed class ContextRing : Button
{
    /// <summary>The ring shows once more than this much of the window is used (the extension hides it with 50 % or more left).</summary>
    internal const int ShownAbove = 50;

    private static readonly StyledProperty<ChatStrings> StringsProperty =
        ClaudeChatView.StringsProperty.AddOwner<ContextRing>();

    private static readonly StyledProperty<ChatLayout> LayoutProperty =
        ClaudeChatView.LayoutProperty.AddOwner<ContextRing>();

    private readonly ContextRingArc _arc = new ContextRingArc();

    private ChatConversation? _conversation;
    private ChatConversation? _tracked;
    private bool _attached;
    private int _minimumPercent = ShownAbove;

    public ContextRing()
    {
        Classes.Add("ClaudeChatButton");
        Classes.Add("ClaudeChatBarButton");
        Classes.Add("ClaudeChatContextRing");
        Content = _arc;
        Click += (_, _) => Ask();
        IsVisible = false;
    }

    /// <summary>
    /// What a click does: asks for the context. Not in the touch layout while a permission request waits: the panel is the usage sheet
    /// there, which would cover the request, and does not open then (the ring stays on screen under the request).
    /// </summary>
    private void Ask()
    {
        var conversation = _tracked;
        if (conversation == null) return;
        if (GetValue(LayoutProperty) == ChatLayout.Touch && conversation.CurrentPermission != null) return;
        conversation.InvokeCommand("context", "");
    }

    /// <summary>The conversation whose context it shows.</summary>
    internal ChatConversation? Conversation
    {
        get => _conversation;
        set
        {
            _conversation = value;
            if (_attached) Track(value);
        }
    }

    /// <summary>
    /// The ring shows once more than this much of the window is used: <see cref="ShownAbove"/>, the extension's, unless
    /// set (the touch layout's ring is set to 0).
    /// </summary>
    internal int MinimumPercent
    {
        get => _minimumPercent;
        set
        {
            _minimumPercent = value;
            if (_attached) Update();
        }
    }

    /// <summary>How full the window is, 0 to 100, as the extension computes it: the tokens over the window (the reported percentage when there is no window), kept inside.</summary>
    internal double Percent { get; private set; }

    private ChatStrings Strings => GetValue(StringsProperty) ?? ChatStrings.English;

    /// <summary>Styled as a Button: the theme's <c>Button.ClaudeChatButton</c> template and the ring's class.</summary>
    protected override Type StyleKeyOverride => typeof(Button);

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
        if (change.Property == StringsProperty && _attached) Update(); // out of the window, nothing is drawn
    }

    private void Track(ChatConversation? conversation)
    {
        if (!ReferenceEquals(conversation, _tracked))
        {
            if (_tracked != null) _tracked.PropertyChanged -= OnConversationChanged;
            _tracked = conversation;
            if (_tracked != null) _tracked.PropertyChanged += OnConversationChanged;
        }
        // out of the window, only the events go: Avalonia is taking the tree apart, and coming back draws it again
        if (_attached) Update();
    }

    private void OnConversationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatConversation.ContextUsage)) Update();
    }

    /// <summary>The share the arc draws for a percentage: the extension's three steps (the ring that shows only above half).</summary>
    internal static int StepOf(double percent) => percent < 62.5 ? 50 : percent < 87 ? 75 : 99;

    /// <summary>The extension's share used: <c>min(100 * tokens / window, 100)</c>; the integer percentage without a window.</summary>
    internal static double UsedOf(ContextUsage usage) => usage.MaxTokens > 0
        ? Math.Max(0, Math.Min(100.0 * usage.TotalTokens / usage.MaxTokens, 100))
        : Math.Max(0, Math.Min(usage.Percentage, 100));

    private void Update()
    {
        var usage = _tracked?.ContextUsage;
        var percent = usage == null ? 0 : UsedOf(usage);
        IsVisible = usage != null && usage.MaxTokens > 0 && percent > _minimumPercent;
        if (usage == null) return;
        Percent = percent;
        // the extension's steps start at half, where its ring shows; a ring that shows from lower down draws the share itself
        _arc.Percent = _minimumPercent < ShownAbove ? percent : StepOf(percent);
        var tip = Strings.FormatContextRingTip(percent);
        ToolTip.SetTip(this, tip);
        AutomationProperties.SetName(this, tip);
    }
}

/// <summary>
/// The ring's drawing: the track circle and, over it, the arc; the colors come from the theme's styles. The circle is the
/// extension's 5 px radius with a 1.5 px stroke on its 20 px drawing; the touch layout's is larger (9 and 2.5).
/// </summary>
internal sealed class ContextRingArc : Control
{
    public static readonly StyledProperty<double> PercentProperty =
        AvaloniaProperty.Register<ContextRingArc, double>(nameof(Percent));

    public static readonly StyledProperty<IBrush?> TrackProperty =
        AvaloniaProperty.Register<ContextRingArc, IBrush?>(nameof(Track));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<ContextRingArc, IBrush?>(nameof(Fill));

    /// <summary>The circle's radius, the middle of its stroke.</summary>
    public static readonly StyledProperty<double> RadiusProperty =
        AvaloniaProperty.Register<ContextRingArc, double>(nameof(Radius), 5);

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<ContextRingArc, double>(nameof(StrokeThickness), 1.5);

    static ContextRingArc()
    {
        AffectsRender<ContextRingArc>(PercentProperty, TrackProperty, FillProperty, RadiusProperty, StrokeThicknessProperty);
    }

    public double Percent { get => GetValue(PercentProperty); set => SetValue(PercentProperty, value); }

    public IBrush? Track { get => GetValue(TrackProperty); set => SetValue(TrackProperty, value); }

    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }

    public double Radius { get => GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }

    public double StrokeThickness { get => GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }

    public override void Render(DrawingContext context)
    {
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = Radius;
        var stroke = StrokeThickness;
        if (Track != null)
            context.DrawEllipse(null, new Pen(Track, stroke), center, radius, radius);
        if (Fill == null || Percent <= 0) return;
        var pen = new Pen(Fill, stroke, lineCap: PenLineCap.Round);
        if (Percent >= 100)
        {
            context.DrawEllipse(null, pen, center, radius, radius);
            return;
        }
        var angle = Percent / 100 * 2 * Math.PI;
        var start = new Point(center.X, center.Y - radius);
        var end = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(start, false);
            g.ArcTo(end, new Size(radius, radius), 0, angle > Math.PI, SweepDirection.Clockwise);
            g.EndFigure(false);
        }
        context.DrawGeometry(null, pen, geometry);
    }
}
