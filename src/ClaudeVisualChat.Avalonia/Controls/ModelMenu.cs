using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The model chip of the reply box's bar, like the extension's: an 18 px round pill with the model's name and, when
/// the model takes an effort, its level after it ("Opus 5.5 High"; "Auto" without one). It shows what the claude
/// reports: the offered model's name (<see cref="ChatConversation.CurrentModel"/>), else the raw id; a model or a
/// level asked for and not yet confirmed marks it <c>pending</c>. Before the claude reports a model, the chip says
/// "Model", as the extension's; with no list and no model it is hidden. Its tooltip is "Switch model". A click opens and closes the
/// <see cref="ModelMenu"/>, whose keys it takes while the menu is open.
/// </summary>
internal sealed class ModelChip : Button
{
    private static readonly StyledProperty<ChatStrings> StringsProperty =
        ClaudeChatView.StringsProperty.AddOwner<ModelChip>();

    private readonly TextBlock _name = new TextBlock();
    private readonly TextBlock _level = new TextBlock();

    private ChatConversation? _conversation;
    private ChatConversation? _tracked;
    private bool _attached;

    public ModelChip()
    {
        Classes.Add("ClaudeChatButton");
        Classes.Add("ClaudeChatModelChip");
        _level.Classes.Add("ClaudeChatModelChipEffort");
        Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { _name, _level } };
        Click += (_, _) => Menu?.Toggle();
        // before the button's own keys: Enter and Space pick in the menu instead of clicking the chip
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (Menu != null && Menu.HandleKey(e)) e.Handled = true;
        }, RoutingStrategies.Tunnel);
        LostFocus += (_, _) => Menu?.CloseUnlessFocused();
        IsVisible = false;
    }

    /// <summary>The conversation whose model it shows.</summary>
    internal ChatConversation? Conversation
    {
        get => _conversation;
        set
        {
            _conversation = value;
            if (_attached) Track(value);
        }
    }

    /// <summary>The menu the chip opens.</summary>
    internal ModelMenu? Menu { get; set; }

    private ChatStrings Strings => GetValue(StringsProperty) ?? ChatStrings.English;

    /// <summary>Styled as a Button: the theme's <c>Button.ClaudeChatButton</c> template and the chip's class.</summary>
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
        if (change.Property == StringsProperty) Update();
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
        switch (e.PropertyName)
        {
            case nameof(ChatConversation.Model):
            case nameof(ChatConversation.CurrentModel):
            case nameof(ChatConversation.AvailableModels):
            case nameof(ChatConversation.RequestedModel):
            case nameof(ChatConversation.Effort):
            case nameof(ChatConversation.RequestedEffort):
                Update();
                break;
        }
    }

    private void Update()
    {
        var strings = Strings;
        var conversation = _tracked;
        var model = conversation?.Model;
        var current = conversation?.CurrentModel;
        IsVisible = model != null || conversation?.AvailableModels is { Count: > 0 };
        var suffix = conversation?.ModelSuffix;
        _name.Text = current != null ? (suffix == null ? current.DisplayName : current.DisplayName + " " + suffix) : model ?? strings.Model;
        var effort = current != null && current.EffortLevels.Count > 0;
        _level.Text = effort ? strings.EffortLabel(conversation!.Effort) : null;
        _level.IsVisible = effort;
        Classes.Set("pending", conversation?.RequestedModel != null || conversation?.RequestedEffort != null);
        AutomationProperties.SetName(this, effort ? _name.Text + " " + _level.Text : _name.Text);
        ToolTip.SetTip(this, strings.SwitchModel);
    }
}

/// <summary>
/// The model menu, like the extension's: "Select a model", the offered models (the name, the description at half
/// opacity, the current one checked), and under them the "Effort (High)" row with the model's levels on a slider;
/// that row only for a model that takes an effort. The arrows move the mark (the pointer too), Enter or Space picks:
/// a model asks for it (<see cref="ChatConversation.SetModel"/>) and closes; the effort row goes to the next level,
/// round the end (<see cref="ChatConversation.SetEffort"/>), and stays open, as does a click on a notch, which picks
/// that level. Esc closes. The level shown is the one asked for while it waits, else the one the claude reports.
/// </summary>
internal sealed class ModelMenu : ChatMenuPopup
{
    private readonly TextBlock _header = new TextBlock();
    private readonly StackPanel _models = new StackPanel();
    private readonly Border _effortRow = new Border();
    private readonly Border _effortSection = new Border();
    private readonly TextBlock _effortLabel = new TextBlock();
    private readonly TextBlock _effortLevel = new TextBlock();
    private readonly EffortSlider _slider = new EffortSlider();
    private readonly List<Border> _rows = new List<Border>();
    private IReadOnlyList<ChatModel> _shown = Array.Empty<ChatModel>();
    private ChatConversation? _conversation;
    private ChatConversation? _tracked;
    private bool _attached;
    private int _active = -1;

    public ModelMenu()
    {
        _header.Classes.Add("ClaudeChatMenuHeader");
        _models.Classes.Add("ClaudeChatMenuRows");
        _effortLabel.Classes.Add("ClaudeChatMenuLabel");
        _effortLevel.Classes.Add("ClaudeChatEffortLevel");
        _effortRow.Classes.Add("ClaudeChatMenuItem");
        _effortRow.Classes.Add("effort");
        var label = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { _effortLabel, _effortLevel } };
        DockPanel.SetDock(_slider, Dock.Right);
        _effortRow.Child = new DockPanel { Children = { _slider, label } };
        _effortRow.PointerEntered += (_, _) => Mark(_shown.Count);
        _effortRow.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(_effortRow).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            NextEffort();
        };
        _slider.LevelPicked += (_, level) =>
        {
            if (CanAct(_tracked)) _tracked!.SetEffort(level);
            else IsOpen = false;
        };
        _effortSection.Classes.Add("ClaudeChatEffortSection");
        _effortSection.Child = _effortRow;
        var list = new StackPanel { Children = { _header, _models } };
        list.Classes.Add("ClaudeChatMenuList");
        list.Classes.Add("ClaudeChatMenuRows");
        Frame.Child = new StackPanel { Children = { list, _effortSection } };
    }

    /// <summary>The conversation whose models it lists; another one closes the menu.</summary>
    internal ChatConversation? Conversation
    {
        get => _conversation;
        set
        {
            if (!ReferenceEquals(value, _conversation)) IsOpen = false;
            _conversation = value;
            if (_attached) Track(value);
        }
    }

    /// <summary>The chip's click: opens the menu, with the current model marked, or closes it. No list, no menu.</summary>
    internal void Toggle()
    {
        if (IsOpen)
        {
            IsOpen = false;
            return;
        }
        var models = _tracked?.AvailableModels;
        if (models == null || models.Count == 0 || !CanAct(_tracked)) return;
        Build();
        Mark(IndexOfCurrent());
        Show();
    }

    /// <summary>A key pressed on the chip while the menu is open; true when the menu took it.</summary>
    internal bool HandleKey(KeyEventArgs e)
    {
        if (!IsOpen || e.KeyModifiers != KeyModifiers.None) return false;
        var count = RowCount;
        switch (e.Key)
        {
            case Key.Escape:
                IsOpen = false;
                return true;
            case Key.Down:
                if (count > 0) Mark(_active < count - 1 ? _active + 1 : 0);
                return true;
            case Key.Up:
                if (count > 0) Mark(_active > 0 ? _active - 1 : count - 1);
                return true;
            case Key.Enter:
            case Key.Space:
                if (_active >= 0 && _active < _shown.Count) Pick(_shown[_active]);
                else if (_active == _shown.Count && _effortSection.IsVisible) NextEffort();
                else IsOpen = false;
                return true;
            case Key.Tab:
                IsOpen = false; // the keyboard goes on to the next control
                return false;
            default:
                return false;
        }
    }

    /// <summary>The chip lost the keyboard: the menu closes unless the keyboard went into it.</summary>
    internal void CloseUnlessFocused()
    {
        if (!IsOpen) return;
        Dispatcher.UIThread.Post(() =>
        {
            var focused = TopLevel.GetTopLevel(Opener ?? this)?.FocusManager?.GetFocusedElement();
            if (!IsInside(focused)) IsOpen = false;
        });
    }

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
        Track(null);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StringsProperty && IsOpen) Build();
    }

    private int RowCount => _shown.Count + (_effortSection.IsVisible ? 1 : 0);

    private void Track(ChatConversation? conversation)
    {
        if (ReferenceEquals(conversation, _tracked)) return;
        if (_tracked != null) _tracked.PropertyChanged -= OnConversationChanged;
        _tracked = conversation;
        if (_tracked != null) _tracked.PropertyChanged += OnConversationChanged;
    }

    private void OnConversationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!IsOpen) return;
        switch (e.PropertyName)
        {
            case nameof(ChatConversation.AvailableModels):
            case nameof(ChatConversation.CurrentModel):
            case nameof(ChatConversation.Effort):
            case nameof(ChatConversation.RequestedEffort):
                var active = _active;
                Build();
                Mark(active < RowCount ? active : -1);
                break;
        }
    }

    private void Build()
    {
        var strings = Strings;
        var conversation = _tracked;
        var current = conversation?.CurrentModel;
        _shown = conversation?.AvailableModels ?? Array.Empty<ChatModel>();
        _header.Text = strings.SelectModel;
        _models.Children.Clear();
        _rows.Clear();
        for (var i = 0; i < _shown.Count; i++)
        {
            var row = Row(_shown[i], i, ReferenceEquals(_shown[i], current));
            _rows.Add(row);
            _models.Children.Add(row);
        }
        var levels = current?.EffortLevels;
        _effortSection.IsVisible = levels is { Count: > 0 };
        if (levels is { Count: > 0 })
        {
            var level = ShownEffort;
            _effortLabel.Text = strings.Effort;
            _effortLevel.Text = "(" + strings.EffortLabel(level) + ")";
            _slider.Show(levels, level);
        }
    }

    private Border Row(ChatModel model, int index, bool current)
    {
        var name = new TextBlock { Text = model.DisplayName };
        name.Classes.Add("ClaudeChatModelLabel");
        var text = new StackPanel { Children = { name } };
        if (!string.IsNullOrEmpty(model.Description))
        {
            var description = new TextBlock { Text = model.Description };
            description.Classes.Add("ClaudeChatModelDescription");
            text.Children.Add(description);
        }
        var check = new Path { IsVisible = current };
        check.Classes.Add("ClaudeChatMenuCheck");
        var mark = new Panel
        {
            Width = 24,
            Children = { new Viewbox { Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Left, Child = check } },
        };
        DockPanel.SetDock(mark, Dock.Right);
        var row = new Border { Child = new DockPanel { Children = { mark, text } } };
        row.Classes.Add("ClaudeChatMenuItem");
        row.PointerEntered += (_, _) => Mark(index);
        row.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            Pick(model);
        };
        return row;
    }

    private int IndexOfCurrent()
    {
        var current = _tracked?.CurrentModel;
        for (var i = 0; i < _shown.Count; i++)
        {
            if (ReferenceEquals(_shown[i], current)) return i;
        }
        return -1;
    }

    private void Mark(int index)
    {
        _active = index;
        for (var i = 0; i < _rows.Count; i++) _rows[i].Classes.Set("active", i == index);
        _effortRow.Classes.Set("active", index == _shown.Count);
    }

    private string? ShownEffort => _tracked?.RequestedEffort ?? _tracked?.Effort;

    private void Pick(ChatModel model)
    {
        var conversation = _tracked;
        IsOpen = false;
        if (!CanAct(conversation)) return;
        conversation!.SetModel(model.Value);
        Opener?.Focus();
    }

    /// <summary>The next level of the model's, round the end; from none, the first.</summary>
    private void NextEffort()
    {
        if (!CanAct(_tracked))
        {
            IsOpen = false;
            return;
        }
        var levels = _tracked!.CurrentModel?.EffortLevels;
        if (levels == null || levels.Count == 0) return;
        var level = ShownEffort;
        var index = level == null ? -1 : IndexOf(levels, level);
        _tracked.SetEffort(levels[(index + 1) % levels.Count]);
    }

    internal static int IndexOf(IReadOnlyList<string> levels, string level)
    {
        for (var i = 0; i < levels.Count; i++)
        {
            if (levels[i] == level) return i;
        }
        return -1;
    }
}

/// <summary>
/// The effort slider of the model menu, like the extension's: a 76 × 18 track, filled up to a 14 px thumb set 2 px
/// inside, with a 4 px notch per level. Pressing it (or dragging on it) picks the nearest level.
/// </summary>
internal sealed class EffortSlider : Border
{
    private const double TrackWidth = 76;
    private const double TrackHeight = 18;
    private const double ThumbSize = 14;
    private const double Inset = 2;
    private const double NotchSize = 4;
    private const double Travel = TrackWidth - ThumbSize - 2 * Inset;

    private readonly Canvas _canvas = new Canvas();
    private readonly Border _fill = new Border();
    private readonly Border _thumb = new Border();
    private IReadOnlyList<string> _levels = Array.Empty<string>();
    private int _dragged = -1;

    public EffortSlider()
    {
        Classes.Add("ClaudeChatEffortTrack");
        _fill.Classes.Add("ClaudeChatEffortFill");
        _thumb.Classes.Add("ClaudeChatEffortThumb");
        Child = _canvas;
    }

    /// <summary>A level picked with the pointer.</summary>
    internal event EventHandler<string>? LevelPicked;

    /// <summary>Styled as a Border: the theme's <c>Border.ClaudeChatEffortTrack</c>.</summary>
    protected override Type StyleKeyOverride => typeof(Border);

    /// <summary>The model's levels, and the thumb on the given one (on the first when there is none).</summary>
    internal void Show(IReadOnlyList<string> levels, string? level)
    {
        _levels = levels;
        var count = levels.Count;
        var at = count > 1 ? Math.Max(0, level == null ? 0 : ModelMenu.IndexOf(levels, level)) / (double)(count - 1) : 0;
        _canvas.Children.Clear();
        _fill.Width = Inset + at * Travel + ThumbSize + Inset;
        _canvas.Children.Add(_fill);
        for (var i = 0; i < count; i++)
        {
            var notch = new Ellipse();
            notch.Classes.Add("ClaudeChatEffortNotch");
            var center = Inset + (count > 1 ? i / (double)(count - 1) : 0) * Travel + ThumbSize / 2;
            Canvas.SetLeft(notch, center - NotchSize / 2);
            Canvas.SetTop(notch, (TrackHeight - NotchSize) / 2);
            _canvas.Children.Add(notch);
        }
        Canvas.SetLeft(_thumb, Inset + at * Travel);
        Canvas.SetTop(_thumb, Inset);
        _canvas.Children.Add(_thumb);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        e.Handled = true; // the slider's own: the row does not go to the next level
        e.Pointer.Capture(this);
        _dragged = LevelAt(e.GetPosition(this).X);
        Pick(_dragged);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragged < 0) return;
        var index = LevelAt(e.GetPosition(this).X);
        if (index == _dragged) return;
        _dragged = index;
        Pick(index);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragged = -1;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragged = -1;
    }

    private int LevelAt(double x)
    {
        if (_levels.Count <= 1) return 0;
        var width = Bounds.Width > 0 ? Bounds.Width : TrackWidth;
        return (int)Math.Round(Math.Max(0, Math.Min(1, x / width)) * (_levels.Count - 1), MidpointRounding.AwayFromZero);
    }

    private void Pick(int index)
    {
        if (index >= 0 && index < _levels.Count) LevelPicked?.Invoke(this, _levels[index]);
    }
}
