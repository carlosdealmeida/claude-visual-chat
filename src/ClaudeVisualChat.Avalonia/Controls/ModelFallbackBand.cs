using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The band over the reply box when the claude's safeguards switched the model, like the extension's: "Switched to {model}"
/// with a "Why?" that opens the explanation (and "Hide" that closes it) and a × that dismisses it
/// (<see cref="ChatConversation.DismissModelFallback"/>). It wears the other banners' look and sits in their place, one at a
/// time with them, and a permission request covers it. A newer switch shows again, with the explanation closed.
/// </summary>
internal sealed class ModelFallbackBand : Border
{
    private static readonly StyledProperty<ChatStrings> StringsProperty =
        ClaudeChatView.StringsProperty.AddOwner<ModelFallbackBand>();

    private readonly TextBlock _headline = new TextBlock { TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
    private readonly TextBlock _dot = new TextBlock { Text = "·" };
    private readonly TextBlock _why = new TextBlock();
    private readonly TextBlock _body = new TextBlock();
    private readonly Button _whyButton;
    private readonly Button _close;

    private ChatConversation? _conversation;
    private ChatConversation? _tracked;
    private bool _attached;
    private object? _shown;
    private bool _whyOpen;

    public ModelFallbackBand()
    {
        Classes.Add("ClaudeChatBanner");
        _headline.Classes.Add("ClaudeChatBannerText");
        _body.Classes.Add("ClaudeChatBannerText");
        _body.Classes.Add("ClaudeChatBandBody");
        _body.IsVisible = false;
        _dot.Classes.Add("ClaudeChatBannerText");
        _dot.Classes.Add("ClaudeChatBandDot");
        _why.Classes.Add("ClaudeChatBandWhyText");
        _whyButton = new Button { Content = _why };
        _whyButton.Classes.Add("ClaudeChatButton");
        _whyButton.Classes.Add("ClaudeChatBandWhy");
        _whyButton.Click += (_, _) =>
        {
            _whyOpen = !_whyOpen;
            Update();
        };
        _close = ChatBanner.CreateCloseButton();
        _close.Click += (_, _) => _tracked?.DismissModelFallback();
        DockPanel.SetDock(_close, Dock.Right);

        var lead = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        lead.Children.Add(_headline);
        lead.Children.Add(_dot);
        lead.Children.Add(_whyButton);
        var row = new DockPanel { Children = { _close, lead } };
        Child = new StackPanel { Children = { row, _body } };
        IsVisible = false;
    }

    /// <summary>The conversation whose last switch it shows.</summary>
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
    /// The headline gives way (an ellipsis) so a long model id never pushes "Why?" and the × out of the band: its width is
    /// what is left of the band after them.
    /// </summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        if (_headline.IsVisible && _whyButton.Bounds.Width > 0)
        {
            var taken = Padding.Left + Padding.Right + BorderThickness.Left + BorderThickness.Right
                + _close.Bounds.Width + _close.Margin.Left + _close.Margin.Right
                + _whyButton.Bounds.Width + _dot.Bounds.Width + _dot.Margin.Left + _dot.Margin.Right;
            var room = Math.Max(1, finalSize.Width - taken);
            if (double.IsInfinity(_headline.MaxWidth) || Math.Abs(_headline.MaxWidth - room) > 0.5)
            {
                _headline.MaxWidth = room;
                InvalidateMeasure();
            }
        }
        return size;
    }

    private ChatStrings Strings => GetValue(StringsProperty) ?? ChatStrings.English;

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
        // out of the window, only the events go: coming back draws it again
        if (_attached) Update();
    }

    private void OnConversationChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ChatConversation.LastModelFallback):
            case nameof(ChatConversation.CurrentPermission):
            case nameof(ChatConversation.AvailableModels):
            case nameof(ChatConversation.BrowserStatus):
            case nameof(ChatConversation.RemoteControlStatus):
                Update();
                break;
        }
    }

    private void Update()
    {
        var conversation = _tracked;
        var fallback = conversation?.LastModelFallback;
        if (!ReferenceEquals(fallback, _shown))
        {
            _shown = fallback;
            _whyOpen = false; // a newer switch comes with its explanation closed
        }
        // a permission request covers it, and so does another banner: one at a time
        var visible = fallback != null && conversation!.CurrentPermission == null
            && conversation.BrowserStatus == ChatBrowserStatus.Off
            && conversation.RemoteControlStatus != ChatRemoteControlStatus.Error;
        IsVisible = visible;
        if (!visible) return;

        var strings = Strings;
        var models = conversation!.AvailableModels;
        var now = AgentMapPanel.ModelName(fallback!.FallbackModel, models) ?? strings.Claude;
        var before = AgentMapPanel.ModelName(fallback.OriginalModel, models) ?? strings.Claude;
        _headline.Text = strings.FormatSwitchedTo(now);
        _why.Text = _whyOpen ? strings.ModelFallbackHide : strings.ModelFallbackWhy;
        AutomationProperties.SetName(_whyButton, _why.Text);
        _body.IsVisible = _whyOpen;
        _body.Text = _whyOpen ? strings.FormatModelFallbackWhy(before, now) : null;
        var name = strings.Dismiss;
        AutomationProperties.SetName(_close, name);
        ToolTip.SetTip(_close, name);
    }
}
