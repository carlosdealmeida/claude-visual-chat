using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The reply box, like the extension's: the text over a bar with the model, the mode and the square buttons: send
/// (enabled when something is written) and, while Claude works, stop; with nothing written, only stop shows, as in the
/// extension; with text, both, and what is sent waits in line.
/// Enter sends and clears; Shift+Enter breaks the line; Esc stops Claude while it works, and otherwise goes on to the
/// app. The pickers show what the claude reports (a model asked for shows, marked pending, until the claude reports
/// one); picking only asks for a change through the conversation's events. The box takes the mode's color: classes
/// <c>plan</c>, <c>acceptEdits</c> and <c>auto</c> on its frame.
/// </summary>
internal partial class ChatComposer : UserControl
{
    private static readonly StyledProperty<ChatStrings> StringsProperty =
        ClaudeChatView.StringsProperty.AddOwner<ChatComposer>();

    /// <summary>A mode: its icon on the extension's 26 px box, and its name.</summary>
    private static readonly FuncDataTemplate<Choice> ModeTemplate = new FuncDataTemplate<Choice>((choice, _) =>
    {
        var icon = new Path();
        icon.Classes.Add("ClaudeChatModeIcon");
        icon.Bind(Path.DataProperty, icon.GetResourceObservable(IconKey(choice?.Value)));
        var label = new TextBlock { Text = choice?.Label };
        label.Classes.Add("ClaudeChatModeLabel");
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, label } };
    });

    /// <summary>What was written in each conversation and not sent; it goes with the conversation.</summary>
    private readonly ConditionalWeakTable<ChatConversation, string> _drafts = new ConditionalWeakTable<ChatConversation, string>();

    private ChatConversation? _conversation;
    private ChatConversation? _tracked;
    private IReadOnlyList<string> _modes = Array.Empty<string>();
    private IReadOnlyList<string>? _models;
    private bool _attached;

    /// <summary>True while the pickers are set from the conversation, so the change is not the user's.</summary>
    private bool _syncing;

    public ChatComposer()
    {
        InitializeComponent();
        ModePicker.ItemTemplate = ModeTemplate;
        Input.AddHandler(KeyDownEvent, OnInputKeyDown, RoutingStrategies.Tunnel);
        Input.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty) return;
            UpdateSend();
            UpdateBusy(); // with text, the square sends; empty while Claude works, it stops
        };
        SendButton.Click += (_, _) =>
        {
            Send();
            Input.Focus();
        };
        StopButton.Click += (_, _) => _tracked?.Stop();
        ModePicker.SelectionChanged += OnModePicked;
        ModelPicker.SelectionChanged += OnModelPicked;
        IsEnabled = false;
        Refresh();
    }

    /// <summary>
    /// The conversation the reply box writes to. What was written and not sent stays with its conversation: another
    /// one shows its own draft, or an empty box, so Enter never sends a draft to the wrong conversation.
    /// </summary>
    internal ChatConversation? Conversation
    {
        get => _conversation;
        set
        {
            if (!ReferenceEquals(value, _conversation))
            {
                KeepDraft(_conversation);
                _conversation = value;
                ShowDraft(value);
            }
            IsEnabled = value != null;
            if (_attached) Track(value);
        }
    }

    private void KeepDraft(ChatConversation? conversation)
    {
        if (conversation == null) return;
        _drafts.Remove(conversation);
        var text = Input.Text;
        if (!string.IsNullOrEmpty(text)) _drafts.Add(conversation, text!);
    }

    private void ShowDraft(ChatConversation? conversation)
    {
        var text = conversation != null && _drafts.TryGetValue(conversation, out var draft) ? draft : "";
        Input.Text = text;
        Input.CaretIndex = text.Length;
        // the undo history belongs to the text that left: Ctrl+Z must not bring another conversation's draft back
        Input.IsUndoEnabled = false;
        Input.IsUndoEnabled = true;
    }

    /// <summary>The permission modes offered; one without a label shows its name.</summary>
    internal IReadOnlyList<string>? Modes
    {
        get => _modes;
        set
        {
            _modes = value ?? Array.Empty<string>();
            RebuildModes();
        }
    }

    /// <summary>The models offered; null hides the model picker.</summary>
    internal IReadOnlyList<string>? Models
    {
        get => _models;
        set
        {
            _models = value;
            RebuildModels();
        }
    }

    /// <summary>Nothing written yet (spaces do not count).</summary>
    internal bool IsEmpty => string.IsNullOrWhiteSpace(Input.Text);

    internal bool FocusInput() => Input.Focus();

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
        if (change.Property == StringsProperty) Refresh();
    }

    private void Track(ChatConversation? conversation)
    {
        if (!ReferenceEquals(conversation, _tracked))
        {
            if (_tracked != null) _tracked.PropertyChanged -= OnConversationChanged;
            _tracked = conversation;
            if (_tracked != null) _tracked.PropertyChanged += OnConversationChanged;
        }
        Refresh();
    }

    private void OnConversationChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ChatConversation.IsBusy):
                UpdateBusy();
                break;
            case nameof(ChatConversation.PermissionMode):
                SelectMode();
                break;
            case nameof(ChatConversation.Model):
            case nameof(ChatConversation.RequestedModel):
                SelectModel();
                break;
        }
    }

    private void Refresh()
    {
        var strings = Strings;
        Label(SendButton, strings.Send);
        Label(StopButton, strings.Stop);
        UpdateBusy();
        UpdateSend();
        RebuildModes();
        RebuildModels();
    }

    private static void Label(Button button, string name)
    {
        AutomationProperties.SetName(button, name);
        ToolTip.SetTip(button, name);
    }

    /// <summary>
    /// While Claude works, stop shows and what is sent waits in line; send shows next to it once something is written
    /// (alone, stop is the extension's square). Idle, only send.
    /// </summary>
    private void UpdateBusy()
    {
        var busy = _tracked != null && _tracked.IsBusy;
        var strings = Strings;
        Input.Watermark = busy ? strings.InputPlaceholderBusy : strings.InputPlaceholder;
        StopButton.IsVisible = busy;
        SendButton.IsVisible = !busy || !IsEmpty;
    }

    private void UpdateSend() => SendButton.IsEnabled = _tracked != null && !IsEmpty;

    // ---- keys and buttons ----

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None) return; // Shift+Enter: the text box breaks the line
        if (e.Key == Key.Enter)
        {
            e.Handled = true; // Enter is the send key, never a line break
            Send();
        }
        else if (e.Key == Key.Escape && _tracked != null && _tracked.IsBusy)
        {
            e.Handled = true; // only when it stops something; otherwise the Esc is the app's
            _tracked.Stop();
        }
    }

    private void Send()
    {
        var conversation = _tracked;
        if (conversation == null || IsEmpty) return;
        if (conversation.Submit(Input.Text ?? "")) Input.Clear();
    }

    // ---- pickers ----

    private void RebuildModes()
    {
        var strings = Strings;
        var items = new List<Choice>(_modes.Count);
        foreach (var mode in _modes) items.Add(new Choice(mode, strings.ModeLabel(mode)));
        _syncing = true;
        try
        {
            ModePicker.ItemsSource = items;
        }
        finally
        {
            _syncing = false;
        }
        SelectMode();
    }

    /// <summary>Shows the mode the claude reports; one that is not offered shows as the placeholder.</summary>
    private void SelectMode()
    {
        var mode = _tracked?.PermissionMode;
        Choice? shown = null;
        if (ModePicker.ItemsSource is List<Choice> items)
        {
            foreach (var item in items)
            {
                if (item.Value == mode) shown = item;
            }
        }
        _syncing = true;
        try
        {
            ModePicker.SelectedItem = shown;
            ModePicker.PlaceholderText = shown == null && mode != null ? Strings.ModeLabel(mode) : null;
        }
        finally
        {
            _syncing = false;
        }
        // the frame and the square take the color of the mode the claude is in
        Box.Classes.Set("plan", mode == "plan");
        Box.Classes.Set("acceptEdits", mode == "acceptEdits");
        Box.Classes.Set("auto", mode == "auto" || mode == "bypassPermissions");
    }

    /// <summary>The resource with a mode's icon; a mode without one shows the default mode's.</summary>
    private static string IconKey(string? mode) => mode switch
    {
        "plan" => "ClaudeChat.Icon.ModePlan",
        "acceptEdits" => "ClaudeChat.Icon.ModeAcceptEdits",
        "auto" or "bypassPermissions" => "ClaudeChat.Icon.ModeAuto",
        _ => "ClaudeChat.Icon.ModeDefault",
    };

    private void OnModePicked(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _tracked == null || ModePicker.SelectedItem is not Choice choice) return;
        _tracked.SetPermissionMode(choice.Value);
        // the chip shows the mode the claude is in, not the one asked for: it moves when the claude confirms, and
        // stays when the claude refuses or confirms the mode it already had (both leave PermissionMode as it is);
        // posted, since the picker is still applying the user's choice
        Dispatcher.UIThread.Post(SelectMode);
    }

    private void RebuildModels()
    {
        ModelPicker.IsVisible = _models != null;
        _syncing = true;
        try
        {
            ModelPicker.ItemsSource = _models == null ? null : new List<string>(_models);
        }
        finally
        {
            _syncing = false;
        }
        SelectModel();
    }

    /// <summary>
    /// Shows the model asked for while the claude has not confirmed it (marked pending), else the model the claude
    /// reports; one that is not offered shows as the placeholder.
    /// </summary>
    private void SelectModel()
    {
        var requested = _tracked?.RequestedModel;
        var model = requested ?? _tracked?.Model;
        var offered = model != null && _models != null && Contains(_models, model);
        _syncing = true;
        try
        {
            ModelPicker.SelectedItem = offered ? model : null;
            ModelPicker.PlaceholderText = offered ? null : model;
            ModelPicker.Classes.Set("pending", requested != null);
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnModelPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _tracked == null || ModelPicker.SelectedItem is not string model) return;
        _tracked.SetModel(model);
    }

    private static bool Contains(IReadOnlyList<string> list, string value)
    {
        foreach (var item in list)
        {
            if (item == value) return true;
        }
        return false;
    }

    /// <summary>A picker entry: the value sent to the claude and the label shown.</summary>
    private sealed class Choice
    {
        public Choice(string value, string label)
        {
            Value = value;
            Label = label;
        }

        public string Value { get; }

        public string Label { get; }

        public override string ToString() => Label;
    }
}
