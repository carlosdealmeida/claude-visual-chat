using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using Xunit;
using ControlAcknowledged = ClaudeVisualChat.Protocol.ControlAcknowledged;
using SessionStarted = ClaudeVisualChat.Protocol.SessionStarted;
using StatusChanged = ClaudeVisualChat.Protocol.StatusChanged;
using TextContentBlock = ClaudeVisualChat.Protocol.TextContentBlock;
using TurnCompleted = ClaudeVisualChat.Protocol.TurnCompleted;
using UserBlock = ClaudeVisualChat.Protocol.UserBlock;

namespace ClaudeVisualChat.Tests.Ui;

public class ComposerTests
{
    private static JsonElement J(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private sealed class Opened
    {
        public Opened(ChatConversation conversation, ChatStrings? strings)
        {
            Conversation = conversation;
            conversation.MessageSubmitted += (_, e) => Sent.Add(e.Text);
            conversation.StopRequested += (_, _) => Stops++;
            conversation.PermissionModeRequested += (_, e) => Modes.Add(e.Mode);
            View = new ClaudeChatView { Conversation = conversation };
            if (strings != null) View.Strings = strings;
            Window = UiHelpers.Show(View);
            Composer = UiHelpers.Composer(View);
            Input = UiHelpers.Input(View);
            Unhandled = UiHelpers.UnhandledKeys(Window);
        }

        public ChatConversation Conversation { get; }
        public ClaudeChatView View { get; }
        public Window Window { get; }
        public ChatComposer Composer { get; }
        public TextBox Input { get; }
        public List<Key> Unhandled { get; }
        public List<string> Sent { get; } = new List<string>();
        public int Stops { get; set; }
        public List<string> Modes { get; } = new List<string>();

        /// <summary>What the reply box shows: its texts and the names of its icon buttons.</summary>
        public List<string> Texts => UiHelpers.Texts(Composer).Concat(UiHelpers.ButtonNames(Composer)).ToList();

        public ComboBox ModePicker => Composer.GetVisualDescendants().OfType<ComboBox>().Single();

        public ModelChip ModelChip => Composer.GetVisualDescendants().OfType<ModelChip>().Single();

        public void Type(string text)
        {
            Input.Focus();
            UiHelpers.Type(Window, text);
        }
    }

    private static Opened Open(ChatConversation? conversation = null, ChatStrings? strings = null) =>
        new Opened(conversation ?? new ChatConversation(), strings);

    private static object? Resource(Control view, string key) =>
        view.TryFindResource(key, out var value) ? value : null;

    private static List<string> Labels(ComboBox picker) =>
        picker.Items.Cast<object>().Select(i => i.ToString() ?? "").ToList();

    private static void Pick(ComboBox picker, string label)
    {
        picker.SelectedItem = picker.Items.Cast<object>().Single(i => i.ToString() == label);
        UiHelpers.Pump();
    }

    // ---- the brief ----

    [AvaloniaFact]
    public void Enter_sends_and_clears()
    {
        var ui = Open();
        ui.Type("oi");

        UiHelpers.Press(ui.Window, Key.Enter);

        Assert.Equal(new[] { "oi" }, ui.Sent);
        Assert.Equal("", ui.Input.Text ?? "");
        Assert.True(ui.Input.IsFocused);
        Assert.DoesNotContain(Key.Enter, ui.Unhandled);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Shift_enter_breaks_the_line()
    {
        var ui = Open();
        ui.Type("oi");

        UiHelpers.Press(ui.Window, Key.Enter, RawInputModifiers.Shift);
        UiHelpers.Type(ui.Window, "tchau");

        Assert.Empty(ui.Sent);
        Assert.Equal("oi\ntchau", (ui.Input.Text ?? "").Replace("\r\n", "\n"));

        UiHelpers.Press(ui.Window, Key.Enter);
        Assert.Equal("oi\ntchau", Assert.Single(ui.Sent).Replace("\r\n", "\n"));
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Busy_shows_stop_and_esc_stops()
    {
        var ui = Open();
        Assert.DoesNotContain("Stop", ui.Texts);
        ui.Input.Focus();

        UiHelpers.Press(ui.Window, Key.Escape);
        Assert.Equal(0, ui.Stops);
        Assert.Contains(Key.Escape, ui.Unhandled); // not busy: the app gets the Esc

        ui.Type("go");
        UiHelpers.Press(ui.Window, Key.Enter);
        Assert.True(ui.Conversation.IsBusy);
        Assert.Contains("Stop", ui.Texts);

        ui.Unhandled.Clear();
        UiHelpers.Press(ui.Window, Key.Escape);
        Assert.Equal(1, ui.Stops);
        Assert.DoesNotContain(Key.Escape, ui.Unhandled);

        UiHelpers.Click(ui.Window, UiHelpers.ButtonWithText(ui.Composer, "Stop"));
        Assert.Equal(2, ui.Stops);

        ui.Conversation.Apply(new TurnCompleted("error_during_execution", true, 10, 0, 1, 1));
        UiHelpers.Pump();
        Assert.DoesNotContain("Stop", ui.Texts);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Picking_plan_asks_for_plan_mode()
    {
        var ui = Open();
        Assert.Equal(new[] { "Plan", "Manual", "Edit automatically", "Auto" }, Labels(ui.ModePicker));
        Assert.Contains("Manual", UiHelpers.Texts(ui.ModePicker)); // the current mode, "default"

        Pick(ui.ModePicker, "Plan");

        Assert.Equal(new[] { "plan" }, ui.Modes);
        Assert.Equal("default", ui.Conversation.PermissionMode); // only the claude changes it
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Without_models_there_is_no_model_chip()
    {
        var ui = Open();
        Assert.Null(ui.Conversation.AvailableModels);
        Assert.False(ui.ModelChip.IsEffectivelyVisible);
        Assert.True(ui.ModePicker.IsEffectivelyVisible);
        ui.Window.Close();
    }

    // ---- the bar ----

    private static double X(Opened ui, Control control) => control.TranslatePoint(default, ui.Composer)!.Value.X;

    [AvaloniaFact]
    public void The_bar_has_the_extension_order()
    {
        var ui = Open();
        ui.Conversation.AvailableModels = new[]
        {
            new ChatModel("opus", "claude-opus-5-5", "Opus 5.5", "", new[] { "low", "high" }),
            new ChatModel("sonnet", "claude-sonnet-5-5", "Sonnet 5.5", "", new[] { "low", "high" }),
        };
        ui.Conversation.Apply(new SessionStarted("s1", "sonnet", null, "default", "2.1.286"));
        UiHelpers.Pump();
        var box = ui.Composer.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("ClaudeChatComposer"));
        var add = UiHelpers.ButtonWithText(ui.Composer, "Add");
        var commands = UiHelpers.ButtonWithText(ui.Composer, "Show command menu (/)");

        // +, /, (the clock and the Remote Control chip, empty for now), the model; then the mode and send at the right
        Assert.Equal(box.Bounds.X + 1 + 5, X(ui, add), 1);
        Assert.Equal(new Size(26, 26), add.Bounds.Size);
        Assert.Equal(new Size(26, 26), commands.Bounds.Size);
        Assert.Equal(X(ui, add) + 26 + 2, X(ui, commands), 1);
        Assert.True(X(ui, ui.ModelChip) >= X(ui, commands) + 26 + 2 + 6);
        Assert.True(X(ui, ui.ModePicker) > X(ui, ui.ModelChip) + ui.ModelChip.Bounds.Width);
        Assert.True(X(ui, SendButton(ui)) > X(ui, ui.ModePicker));
        Assert.Equal(add.TranslatePoint(default, ui.Composer)!.Value.Y, SendButton(ui).TranslatePoint(default, ui.Composer)!.Value.Y, 1);
        Assert.Equal("Add", ToolTip.GetTip(add));
        Assert.Equal("Show command menu (/)", ToolTip.GetTip(commands));
        // the extension's 26 px icons, in the text's color
        var plus = add.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single();
        Assert.Equal(new Size(26, 26), plus.Bounds.Size);
        Assert.Equal(13, plus.Data!.Bounds.Width + plus.StrokeThickness, 1);
        Assert.Same(Resource(ui.View, "ClaudeChat.Foreground"), add.Foreground);

        // / opens the command palette: it sends nothing and leaves the text alone
        ui.Type("hi");
        UiHelpers.Click(ui.Window, commands);
        Assert.True(ui.Composer.GetLogicalDescendants().OfType<CommandPalette>().Single().IsOpen);
        Assert.Empty(ui.Sent);
        Assert.Equal("hi", ui.Input.Text);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Bar_buttons_in_portuguese()
    {
        var ui = Open(strings: ChatStrings.PortugueseBrazil);
        Assert.Contains("Adicionar", ui.Texts);
        Assert.Contains("Mostrar o menu de comandos (/)", ui.Texts);
        ui.View.Strings = ChatStrings.English;
        UiHelpers.Pump();
        Assert.Contains("Add", ui.Texts);
        ui.Window.Close();
    }

    // ---- the reply box ----

    [AvaloniaFact]
    public void Send_is_off_while_there_is_nothing_to_send()
    {
        var ui = Open();
        var send = UiHelpers.ButtonWithText(ui.Composer, "Send");
        Assert.False(send.IsEnabled);

        ui.Type("   ");
        Assert.False(send.IsEnabled);
        UiHelpers.Press(ui.Window, Key.Enter);
        Assert.Empty(ui.Sent);
        Assert.DoesNotContain("\n", ui.Input.Text ?? ""); // Enter never breaks the line

        ui.Input.Clear();
        ui.Type("run the tests");
        Assert.True(send.IsEnabled);
        // the extension's square: an arrow, ivory on clay in the default mode
        Assert.Same(Resource(ui.View, "ClaudeChat.SendBackground"), send.Background);
        Assert.Same(Resource(ui.View, "ClaudeChat.SendForeground"), send.Foreground);

        UiHelpers.Click(ui.Window, send);

        Assert.Equal(new[] { "run the tests" }, ui.Sent);
        Assert.Equal("", ui.Input.Text ?? "");
        Assert.False(send.IsEnabled);
        Assert.True(ui.Input.IsFocused); // ready for the next one
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Messages_can_be_sent_while_busy()
    {
        var ui = Open();
        ui.Type("first");
        UiHelpers.Press(ui.Window, Key.Enter);
        ui.Type("second");
        UiHelpers.Press(ui.Window, Key.Enter);

        Assert.Equal(new[] { "first", "second" }, ui.Sent);
        Assert.True(UiHelpers.ButtonWithText(ui.Composer, "Stop").IsEffectivelyVisible);
        ui.Window.Close();
    }

    // ---- send and stop (Ruling 28) ----

    private static Button SendButton(Opened ui) =>
        ui.Composer.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("ClaudeChatSend"));

    private static Button StopButton(Opened ui) =>
        ui.Composer.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("ClaudeChatStop"));

    [AvaloniaFact]
    public void Idle_shows_only_send()
    {
        var ui = Open();
        Assert.True(SendButton(ui).IsEffectivelyVisible);
        Assert.False(StopButton(ui).IsEffectivelyVisible);
        ui.Type("hello");
        Assert.True(SendButton(ui).IsEffectivelyVisible);
        Assert.False(StopButton(ui).IsEffectivelyVisible);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Busy_and_empty_shows_only_stop_like_the_extension()
    {
        var ui = Open();
        ui.Conversation.Submit("go");
        UiHelpers.Pump();
        Assert.False(SendButton(ui).IsEffectivelyVisible);
        Assert.True(StopButton(ui).IsEffectivelyVisible);
        var stop = StopButton(ui).TranslatePoint(default, ui.Composer)!.Value;
        var box = ui.Composer.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("ClaudeChatComposer"));
        Assert.Equal(box.Bounds.Right - 1 - 5 - 26, stop.X, 1); // the extension's square, at the right of the bar

        UiHelpers.Click(ui.Window, StopButton(ui));
        Assert.Equal(1, ui.Stops);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Busy_with_text_shows_send_and_stop_side_by_side()
    {
        var ui = Open();
        ui.Conversation.Submit("go");
        ui.Type("and then this");

        var send = SendButton(ui);
        var stop = StopButton(ui);
        Assert.True(send.IsEffectivelyVisible);
        Assert.True(send.IsEnabled);
        Assert.True(stop.IsEffectivelyVisible);
        var s = send.TranslatePoint(default, ui.Composer)!.Value;
        var t = stop.TranslatePoint(default, ui.Composer)!.Value;
        Assert.Equal(s.Y, t.Y);
        Assert.Equal(s.X + 26 + 2, t.X, 1); // send, then stop at its right, 2 px apart

        // stop with text in the box: Claude is asked to stop, and the text stays
        UiHelpers.Click(ui.Window, stop);
        Assert.Equal(1, ui.Stops);
        Assert.Equal("and then this", ui.Input.Text);
        Assert.Empty(ui.Sent.Skip(1));

        // send: it goes to the queue, the box empties and only stop is left
        UiHelpers.Click(ui.Window, send);
        Assert.Equal(new[] { "go", "and then this" }, ui.Sent);
        Assert.False(SendButton(ui).IsEffectivelyVisible);
        Assert.True(StopButton(ui).IsEffectivelyVisible);

        ui.Conversation.Apply(new TurnCompleted("success", false, 10, 0, 1, 1));
        UiHelpers.Pump();
        Assert.True(SendButton(ui).IsEffectivelyVisible); // idle again
        Assert.False(StopButton(ui).IsEffectivelyVisible);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Send_and_stop_show_the_focus_ring()
    {
        var ui = Open();
        ui.Conversation.Submit("go");
        ui.Type("more");

        foreach (var button in new[] { SendButton(ui), StopButton(ui) })
        {
            var frame = button.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_ChatButton");
            Assert.Equal(new Thickness(0), frame.BorderThickness);
            button.Focus(NavigationMethod.Tab); // the keyboard: focus-visible
            UiHelpers.Pump();
            Assert.Same(Resource(ui.View, "ClaudeChat.FocusBorder"), frame.BorderBrush);
            Assert.Equal(new Thickness(1), frame.BorderThickness); // 1 px, just inside the square
        }
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void The_send_arrow_has_the_extension_proportions()
    {
        var ui = Open();
        var arrow = SendButton(ui).GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single();
        Assert.Equal(new Size(20, 20), arrow.Bounds.Size); // the extension's 20 px icon
        var shape = arrow.Data!.Bounds;
        Assert.Equal(3, shape.Top, 2);
        Assert.Equal(17, shape.Bottom, 2);
        Assert.Equal(10.7, shape.Width, 2); // its point
        Assert.Equal(10, shape.Center.X, 2);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Without_a_conversation_the_reply_box_is_off()
    {
        var view = new ClaudeChatView();
        var window = UiHelpers.Show(view);
        Assert.False(UiHelpers.Composer(view).IsEffectivelyEnabled);

        var c = new ChatConversation();
        view.Conversation = c;
        UiHelpers.Pump();
        Assert.True(UiHelpers.Composer(view).IsEffectivelyEnabled);
        window.Close();
    }

    // ---- switching conversations ----

    [AvaloniaFact]
    public void The_draft_stays_with_its_conversation()
    {
        var a = new ChatConversation();
        var b = new ChatConversation();
        var sentToA = new List<string>();
        var sentToB = new List<string>();
        a.MessageSubmitted += (_, e) => sentToA.Add(e.Text);
        b.MessageSubmitted += (_, e) => sentToB.Add(e.Text);
        var ui = Open(a);
        ui.Type("for machine A");

        ui.View.Conversation = b;
        UiHelpers.Pump();
        Assert.Equal("", ui.Input.Text ?? ""); // B starts empty
        ui.Input.Focus();
        UiHelpers.Press(ui.Window, Key.Z, RawInputModifiers.Control);
        Assert.Equal("", ui.Input.Text ?? ""); // nor does undo bring A's draft into B
        UiHelpers.Press(ui.Window, Key.Enter);
        Assert.Empty(sentToA);
        Assert.Empty(sentToB);
        ui.Type("for machine B");

        ui.View.Conversation = a;
        UiHelpers.Pump();
        Assert.Equal("for machine A", ui.Input.Text);
        ui.View.Conversation = b;
        UiHelpers.Pump();
        Assert.Equal("for machine B", ui.Input.Text);
        ui.View.Conversation = new ChatConversation();
        UiHelpers.Pump();
        Assert.Equal("", ui.Input.Text ?? ""); // a new conversation starts with an empty box

        ui.View.Conversation = a;
        UiHelpers.Pump();
        ui.Input.Focus();
        UiHelpers.Press(ui.Window, Key.Enter);
        Assert.Equal(new[] { "for machine A" }, sentToA);
        Assert.Empty(sentToB);
        ui.View.Conversation = b; // sent: A's draft is gone
        UiHelpers.Pump();
        ui.View.Conversation = a;
        UiHelpers.Pump();
        Assert.Equal("", ui.Input.Text ?? "");
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_message_taken_back_returns_to_the_box()
    {
        var c = new ChatConversation();
        MessageSubmittedEventArgs? sent = null;
        c.MessageSubmitted += (_, e) => sent = e;
        var ui = Open(c);

        ui.Type("oi");
        UiHelpers.Press(ui.Window, Key.Enter);
        Assert.Equal("", ui.Input.Text ?? "");
        Assert.Contains("Stop", ui.Texts);
        c.UndoSubmit(sent!);
        UiHelpers.Pump();
        Assert.Equal("oi", ui.Input.Text);
        Assert.DoesNotContain("Stop", ui.Texts);

        // something written since stays: the text taken back does not replace it
        UiHelpers.Press(ui.Window, Key.Enter);
        ui.Type("outra");
        c.UndoSubmit(sent!);
        UiHelpers.Pump();
        Assert.Equal("outra", ui.Input.Text);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_message_refused_at_once_stays_in_the_box()
    {
        var c = new ChatConversation();
        c.MessageSubmitted += (_, e) => c.UndoSubmit(e);
        var ui = Open(c);

        ui.Type("oi");
        UiHelpers.Press(ui.Window, Key.Enter);

        Assert.Equal(new[] { "oi" }, ui.Sent);
        Assert.Equal("oi", ui.Input.Text);
        Assert.Empty(c.Items);
        Assert.DoesNotContain("Stop", ui.Texts);
        ui.Window.Close();
    }

    /// <summary>Handlers on the conversation's PropertyChanged and on its requests' CollectionChanged.</summary>
    private static int Handlers(ChatConversation c)
    {
        const System.Reflection.BindingFlags field = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var changed = (System.Delegate?)typeof(ChatObservableObject).GetField("PropertyChanged", field)!.GetValue(c);
        var pending = (System.Delegate?)typeof(System.Collections.ObjectModel.ObservableCollection<PendingPermission>)
            .GetField("CollectionChanged", field)!.GetValue(c.PendingPermissions);
        return (changed?.GetInvocationList().Length ?? 0) + (pending?.GetInvocationList().Length ?? 0);
    }

    [AvaloniaFact]
    public void A_conversation_left_behind_no_longer_reaches_the_view()
    {
        var a = new ChatConversation();
        var b = new ChatConversation();
        var own = Handlers(a); // the conversation's own, before any view
        Assert.Equal(own, Handlers(b));
        var ui = Open(a);
        Assert.True(Handlers(a) > own);

        ui.View.Conversation = b;
        UiHelpers.Pump();
        Assert.Equal(own, Handlers(a)); // the view let go of A's events
        Assert.True(Handlers(b) > own);

        // what happens in A no longer moves the request nor the reply box
        var prompt = UiHelpers.Prompt(ui.View);
        a.Apply(new ClaudeVisualChat.Protocol.PermissionRequested("r1", "Bash", "Bash", null, "t1", J("{\"command\":\"ls\"}"), null));
        a.Apply(new StatusChanged(null, "plan"));
        a.Submit("busy now");
        UiHelpers.Pump();
        Assert.False(prompt.IsEffectivelyVisible);
        Assert.DoesNotContain("Plan", UiHelpers.Texts(ui.ModePicker));
        Assert.DoesNotContain("Stop", ui.Texts);

        // leaving the window lets go of B too
        ui.Window.Content = null;
        UiHelpers.Pump();
        Assert.Equal(own, Handlers(b));
        ui.Window.Close();
    }

    // ---- the mode ----

    [AvaloniaFact]
    public void Mode_picker_follows_what_the_claude_reports()
    {
        var ui = Open();
        Pick(ui.ModePicker, "Plan");
        ui.Conversation.Apply(new ControlAcknowledged("c1", false, J("{\"mode\":\"plan\"}")));
        UiHelpers.Pump();
        Assert.Equal("plan", ui.Conversation.PermissionMode);
        Assert.Contains("Plan", UiHelpers.Texts(ui.ModePicker));

        ui.Conversation.Apply(new StatusChanged(null, "acceptEdits")); // changed somewhere else
        UiHelpers.Pump();
        Assert.Contains("Edit automatically", UiHelpers.Texts(ui.ModePicker));

        ui.Conversation.Apply(new StatusChanged(null, "bypassPermissions")); // not offered: the raw name
        UiHelpers.Pump();
        Assert.Contains("bypassPermissions", UiHelpers.Texts(ui.ModePicker));

        Assert.Equal(new[] { "plan" }, ui.Modes); // following the claude asks for nothing
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_refused_mode_change_puts_the_chip_back()
    {
        var ui = Open();
        Pick(ui.ModePicker, "Plan");
        Assert.Equal(new[] { "plan" }, ui.Modes);
        Assert.Contains("Manual", UiHelpers.Texts(ui.ModePicker)); // only the claude changes the mode

        ui.Conversation.Apply(new ControlAcknowledged("c1", true, null)); // the claude says no
        UiHelpers.Pump();

        Assert.Equal("default", ui.Conversation.PermissionMode);
        Assert.Contains("Manual", UiHelpers.Texts(ui.ModePicker));
        Assert.DoesNotContain("Plan", UiHelpers.Texts(ui.ModePicker));
        Assert.Equal(new[] { "plan" }, ui.Modes);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_mode_confirmed_as_it_was_shows_the_real_one()
    {
        var ui = Open();
        ui.Conversation.Apply(new StatusChanged(null, "acceptEdits"));
        UiHelpers.Pump();
        Pick(ui.ModePicker, "Auto");

        ui.Conversation.Apply(new ControlAcknowledged("c1", false, J("{\"mode\":\"acceptEdits\"}")));
        UiHelpers.Pump();

        Assert.Equal("acceptEdits", ui.Conversation.PermissionMode);
        Assert.Contains("Edit automatically", UiHelpers.Texts(ui.ModePicker));
        Assert.DoesNotContain("Auto", UiHelpers.Texts(ui.ModePicker));
        Assert.Equal(new[] { "auto" }, ui.Modes);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Modes_come_from_available_modes_and_unknown_ones_show_raw()
    {
        var ui = Open();
        ui.View.AvailableModes = new[] { "default", "dontAsk" };
        UiHelpers.Pump();

        Assert.Equal(new[] { "Manual", "dontAsk" }, Labels(ui.ModePicker));
        Assert.Contains("Manual", UiHelpers.Texts(ui.ModePicker));

        Pick(ui.ModePicker, "dontAsk");
        Assert.Equal(new[] { "dontAsk" }, ui.Modes);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Portuguese_texts()
    {
        var ui = Open(strings: ChatStrings.PortugueseBrazil);
        Assert.Equal("Responder ao Claude…", ui.Input.Watermark);
        Assert.Contains("Enviar", ui.Texts); // the square's name: it shows an arrow
        ui.Conversation.Submit("x");
        ui.Type("y"); // busy, with something written: both buttons
        UiHelpers.Pump();

        // while Claude works, what is sent waits in line
        Assert.Equal("Pôr outra mensagem na fila…", ui.Input.Watermark);
        Assert.Contains("Enviar", ui.Texts);
        Assert.Contains("Parar", ui.Texts);
        Assert.Equal(new[] { "Plano", "Manual", "Editar sozinho", "Automático" }, Labels(ui.ModePicker));
        Assert.Contains("Manual", UiHelpers.Texts(ui.ModePicker));

        ui.View.Strings = ChatStrings.English;
        UiHelpers.Pump();
        Assert.Equal("Queue another message…", ui.Input.Watermark);
        Assert.Contains("Send", ui.Texts);
        Assert.Contains("Stop", ui.Texts);
        Assert.Contains("Manual", UiHelpers.Texts(ui.ModePicker));
        Assert.Empty(ui.Modes); // relabeling asks for nothing
        ui.Window.Close();
    }

    // ---- recalling prompts, shortcuts and Ctrl+Enter (0.6.0) ----

    private static UserBlock Said(string text, string? parent = null, bool synthetic = false) =>
        new UserBlock(parent, new TextContentBlock(text), null, false, false) { IsSynthetic = synthetic };

    private static ChatConversation WithHistory(params string[] prompts)
    {
        var conversation = new ChatConversation();
        foreach (var prompt in prompts) conversation.Apply(Said(prompt));
        return conversation;
    }

    private static string Lines(TextBox box) => (box.Text ?? "").Replace("\r\n", "\n");

    /// <summary>A key pressed on the box itself, as the window's keyboard would deliver it, for a box that cannot take the focus.</summary>
    private static void Raise(Control target, Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Source = target, Key = key, KeyModifiers = modifiers });

    [AvaloniaFact]
    public void Up_at_the_start_recalls_the_previous_prompt_and_down_restores_the_draft()
    {
        var ui = Open(WithHistory("one", "two"));
        ui.Type("draft");
        UiHelpers.Press(ui.Window, Key.Home);

        UiHelpers.Press(ui.Window, Key.Up);
        Assert.Equal("two", ui.Input.Text);
        Assert.Equal(0, ui.Input.CaretIndex);          // the caret stays at the start, so that the next Up goes on
        UiHelpers.Press(ui.Window, Key.Up);
        Assert.Equal("one", ui.Input.Text);
        UiHelpers.Press(ui.Window, Key.Up);
        Assert.Equal("one", ui.Input.Text);            // the oldest: nowhere further back, and the key is the box's
        Assert.Equal(0, ui.Input.CaretIndex);

        UiHelpers.Press(ui.Window, Key.Down);          // the first Down takes the caret to the end (the box does not), the next one recalls
        Assert.Equal("one", ui.Input.Text);
        Assert.Equal(3, ui.Input.CaretIndex);
        UiHelpers.Press(ui.Window, Key.Down);
        Assert.Equal("two", ui.Input.Text);
        Assert.Equal(3, ui.Input.CaretIndex);          // after a Down the caret is at the end: the next Down goes on
        UiHelpers.Press(ui.Window, Key.Down);
        Assert.Equal("draft", ui.Input.Text);          // past the newest: the draft is back
        Assert.Equal(5, ui.Input.CaretIndex);

        UiHelpers.Press(ui.Window, Key.Down);          // the walk is over: Down is the caret's again
        Assert.Equal("draft", ui.Input.Text);
        Assert.Empty(ui.Sent);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Up_pressed_again_and_again_walks_back_without_touching_the_caret()
    {
        var ui = Open(WithHistory("one", "two", "three", "four"));
        ui.Input.Focus();

        var seen = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            UiHelpers.Press(ui.Window, Key.Up);
            seen.Add(ui.Input.Text ?? "");
        }
        Assert.Equal(new[] { "four", "three", "two", "one", "one" }, seen);

        seen.Clear();
        for (var i = 0; i < 5; i++)
        {
            UiHelpers.Press(ui.Window, Key.Down);
            seen.Add(ui.Input.Text ?? "");
        }
        // the first Down takes the caret to the end of "one" (the box does not), then each Down recalls the next; the last gives the draft back
        Assert.Equal(new[] { "one", "two", "three", "four", "" }, seen);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void The_box_leaves_the_caret_where_it_is_on_the_first_line_and_the_composer_takes_it_to_the_start()
    {
        // Avalonia's text box does nothing with Up on its first line (the browser moves to the start), and the recall depends on it
        var ui = Open(WithHistory("old"));
        ui.Type("typed");

        UiHelpers.Press(ui.Window, Key.Up);
        Assert.Equal("typed", ui.Input.Text);
        Assert.Equal(0, ui.Input.CaretIndex);           // the first Up: to the start
        UiHelpers.Press(ui.Window, Key.Up);
        Assert.Equal("old", ui.Input.Text);             // the second one recalls

        // with nothing to recall the box keeps its own behaviour
        var empty = Open();
        empty.Type("typed");
        UiHelpers.Press(empty.Window, Key.Up);
        Assert.Equal(5, empty.Input.CaretIndex);
        empty.Window.Close();
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Up_at_the_oldest_prompt_leaves_the_key_to_the_box_and_a_multi_line_prompt_is_walked_line_by_line()
    {
        var ui = Open(WithHistory("only\nthen two lines"));
        ui.Input.Focus();
        UiHelpers.Press(ui.Window, Key.Up);
        Assert.Equal("only\nthen two lines", Lines(ui.Input));
        Assert.Equal(0, ui.Input.CaretIndex);

        UiHelpers.Press(ui.Window, Key.Up);              // the oldest: not taken by the composer
        Assert.Equal("only\nthen two lines", Lines(ui.Input));
        Assert.Equal(0, ui.Input.CaretIndex);

        // Down on the first line of two moves the box's own caret down, and only on the last line takes it to the end
        UiHelpers.Press(ui.Window, Key.Down);
        Assert.NotEqual(0, ui.Input.CaretIndex);
        UiHelpers.Press(ui.Window, Key.Down);
        Assert.Equal(ui.Input.Text!.Length, ui.Input.CaretIndex);
        UiHelpers.Press(ui.Window, Key.Down);
        Assert.Equal("", ui.Input.Text ?? "");           // the draft was empty
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Editing_a_recalled_prompt_and_pressing_down_goes_on_to_the_next_newer_one()
    {
        var ui = Open(WithHistory("one", "two"));
        ui.Input.Focus();
        UiHelpers.Press(ui.Window, Key.Up);
        UiHelpers.Press(ui.Window, Key.Up);
        Assert.Equal("one", ui.Input.Text);
        ui.Input.CaretIndex = 3;
        UiHelpers.Type(ui.Window, "!");                  // edited: "one!"
        Assert.Equal("one!", ui.Input.Text);

        UiHelpers.Press(ui.Window, Key.Down);            // the edit is not kept, as in the extension
        Assert.Equal("two", ui.Input.Text);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_prompt_sent_during_the_walk_is_not_in_it()
    {
        var conversation = WithHistory("one", "two");
        var ui = Open(conversation);
        ui.Input.Focus();
        UiHelpers.Press(ui.Window, Key.Up);
        Assert.Equal("two", ui.Input.Text);

        conversation.Apply(Said("three"));               // the list was taken at the first Up
        UiHelpers.Press(ui.Window, Key.Up);
        Assert.Equal("one", ui.Input.Text);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void An_empty_box_recalls_with_up_and_nothing_to_recall_leaves_the_key_to_the_box()
    {
        var empty = Open();
        empty.Input.Focus();
        UiHelpers.Press(empty.Window, Key.Up);
        Assert.Equal("", empty.Input.Text ?? "");
        empty.Window.Close();

        var ui = Open(WithHistory("only"));
        ui.Input.Focus();
        UiHelpers.Press(ui.Window, Key.Up);
        Assert.Equal("only", ui.Input.Text);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Up_in_the_middle_of_the_text_moves_the_caret_only()
    {
        var ui = Open(WithHistory("old"));
        ui.Type("abc");
        ui.Input.CaretIndex = 1;

        UiHelpers.Press(ui.Window, Key.Up);
        Assert.Equal("abc", ui.Input.Text);
        UiHelpers.Press(ui.Window, Key.Down);
        Assert.Equal("abc", ui.Input.Text);

        // a selection is not the start of the text either
        ui.Input.SelectionStart = 0;
        ui.Input.SelectionEnd = 2;
        UiHelpers.Press(ui.Window, Key.Up);
        Assert.Equal("abc", ui.Input.Text);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void The_prompts_recalled_are_the_users_trimmed_with_the_queue_and_without_synthetic_or_subagent_lines()
    {
        var conversation = new ChatConversation();
        conversation.Apply(Said("  padded  "));
        conversation.Apply(Said("synthetic", synthetic: true));
        conversation.Apply(Said("from a subagent", parent: "toolu_unknown"));
        conversation.Apply(Said("repeat"));
        conversation.Apply(Said("repeat"));
        var ui = Open(conversation);
        ui.Type("go");                                  // sent: Claude is busy from here, and the next ones wait in line
        UiHelpers.Press(ui.Window, Key.Enter);
        Assert.True(conversation.IsBusy);
        conversation.Submit("  queued  ");
        Assert.Single(conversation.QueuedMessages);

        ui.Input.Focus();
        var seen = new List<string>();
        for (var i = 0; i < 7; i++)
        {
            UiHelpers.Press(ui.Window, Key.Up);
            seen.Add(ui.Input.Text ?? "");
        }

        // newest first; the queue is the newest of all, and Up works while Claude is busy
        Assert.Equal(new[] { "queued", "go", "repeat", "repeat", "padded", "padded", "padded" }, seen);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Clearing_the_box_ends_the_walk()
    {
        var ui = Open(WithHistory("one", "two"));
        ui.Input.Focus();
        UiHelpers.Press(ui.Window, Key.Up);
        Assert.Equal("two", ui.Input.Text);

        ui.Input.Clear();
        UiHelpers.Pump();
        UiHelpers.Press(ui.Window, Key.Up);

        Assert.Equal("two", ui.Input.Text);             // a new walk from the newest, not the next older one
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Another_conversation_starts_its_own_walk()
    {
        var first = WithHistory("first one");
        var second = WithHistory("second one");
        var ui = Open(first);
        ui.Input.Focus();
        UiHelpers.Press(ui.Window, Key.Up);
        Assert.Equal("first one", ui.Input.Text);

        ui.View.Conversation = second;
        UiHelpers.Pump();
        ui.Input.Focus();
        Assert.Equal("", ui.Input.Text ?? "");
        UiHelpers.Press(ui.Window, Key.Up);

        Assert.Equal("second one", ui.Input.Text);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_recalled_slash_command_does_not_open_the_palette_until_edited()
    {
        var ui = Open(WithHistory("/compact", "/"));
        var palette = ui.Composer.GetLogicalDescendants().OfType<CommandPalette>().Single();
        ui.Input.Focus();

        UiHelpers.Press(ui.Window, Key.Up);
        Assert.Equal("/", ui.Input.Text);
        Assert.False(palette.IsOpen);
        UiHelpers.Press(ui.Window, Key.Up);
        Assert.Equal("/compact", ui.Input.Text);
        Assert.False(palette.IsOpen);

        // edited: the box emptied and a "/" typed opens it as always
        ui.Input.Clear();
        UiHelpers.Type(ui.Window, "/");
        Assert.True(palette.IsOpen);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void With_the_palette_open_the_arrows_are_the_palettes()
    {
        var conversation = WithHistory("an old prompt");
        var ui = Open(conversation);
        ui.Type("/");
        var palette = ui.Composer.GetLogicalDescendants().OfType<CommandPalette>().Single();
        Assert.True(palette.IsOpen);

        UiHelpers.Press(ui.Window, Key.Up);

        Assert.Equal("/", ui.Input.Text);
        Assert.True(palette.IsOpen);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Ctrl_j_breaks_the_line()
    {
        var ui = Open();
        ui.Type("ab");
        ui.Input.CaretIndex = 1;

        UiHelpers.Press(ui.Window, Key.J, RawInputModifiers.Control);

        Assert.Equal("a\nb", Lines(ui.Input));
        Assert.Equal(ui.Input.Text!.IndexOf('b'), ui.Input.CaretIndex);          // the caret is after the break
        Assert.Empty(ui.Sent);
        Assert.DoesNotContain(Key.J, ui.Unhandled);
        UiHelpers.Type(ui.Window, "X");
        Assert.Equal("a\nXb", Lines(ui.Input));

        // with another modifier it is not the shortcut
        UiHelpers.Press(ui.Window, Key.J, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.Equal("a\nXb", Lines(ui.Input));
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Shift_tab_cycles_the_available_modes_in_the_extension_order()
    {
        var ui = Open();
        ui.Conversation.Apply(new SessionStarted("s1", "sonnet", null, "default", "2.1.310"));
        ui.View.AvailableModes = new[] { "plan", "default", "acceptEdits" };
        UiHelpers.Pump();
        ui.Input.Focus();

        // default -> acceptEdits -> plan -> default; the mode moves only when the claude confirms it
        foreach (var expected in new[] { "acceptEdits", "plan", "default" })
        {
            UiHelpers.Press(ui.Window, Key.Tab, RawInputModifiers.Shift);
            Assert.Equal(expected, ui.Modes.Last());
            ui.Conversation.Apply(new StatusChanged(null, expected));
        }

        Assert.Equal(new[] { "acceptEdits", "plan", "default" }, ui.Modes);
        Assert.DoesNotContain(Key.Tab, ui.Unhandled);
        Assert.True(ui.Input.IsFocused);                // the focus did not move backwards
        Assert.Empty(ui.Sent);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Shift_tab_goes_through_auto_when_it_is_offered_and_asks_nothing_when_there_is_no_other_mode()
    {
        var ui = Open();
        ui.Conversation.Apply(new SessionStarted("s1", "sonnet", null, "plan", "2.1.310"));
        ui.Input.Focus();
        UiHelpers.Press(ui.Window, Key.Tab, RawInputModifiers.Shift);
        Assert.Equal(new[] { "auto" }, ui.Modes);

        ui.Conversation.Apply(new StatusChanged(null, "auto"));
        ui.View.AvailableModes = new[] { "auto" };
        UiHelpers.Pump();
        UiHelpers.Press(ui.Window, Key.Tab, RawInputModifiers.Shift);
        Assert.Single(ui.Modes);                        // auto is the only one offered, and it is the current one
        ui.Window.Close();
    }

    [Fact]
    public void The_next_mode_follows_the_cycle_among_the_modes_offered()
    {
        var all = new[] { "plan", "default", "acceptEdits", "auto" };
        Assert.Equal("acceptEdits", ChatComposer.NextMode("default", all));
        Assert.Equal("plan", ChatComposer.NextMode("acceptEdits", all));
        Assert.Equal("auto", ChatComposer.NextMode("plan", all));
        Assert.Equal("default", ChatComposer.NextMode("auto", all));
        Assert.Equal("default", ChatComposer.NextMode("bypassPermissions", all));     // outside the cycle: from its start
        Assert.Equal("plan", ChatComposer.NextMode(null, new[] { "default", "plan" }));  // none yet counts as default
        Assert.Equal("plan", ChatComposer.NextMode("default", new[] { "plan", "default" }));
        Assert.Equal("plan", ChatComposer.NextMode("auto", new[] { "plan" }));          // the current one is not offered
        Assert.Null(ChatComposer.NextMode("default", new[] { "default" }));
        Assert.Null(ChatComposer.NextMode("default", System.Array.Empty<string>()));
    }

    [AvaloniaFact]
    public void Shift_tab_and_ctrl_j_do_nothing_with_the_palette_open()
    {
        var ui = Open();
        ui.Conversation.Apply(new SessionStarted("s1", "sonnet", null, "default", "2.1.310"));
        ui.Type("/");
        var palette = ui.Composer.GetLogicalDescendants().OfType<CommandPalette>().Single();
        Assert.True(palette.IsOpen);

        UiHelpers.Press(ui.Window, Key.Tab, RawInputModifiers.Shift);
        UiHelpers.Press(ui.Window, Key.J, RawInputModifiers.Control);

        Assert.Empty(ui.Modes);                          // the mode does not move behind an open menu
        Assert.Equal("/", ui.Input.Text);
        Assert.True(palette.IsOpen);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Shift_tab_takes_a_mode_outside_the_cycle_to_the_first_of_it()
    {
        var ui = Open();
        ui.Conversation.Apply(new SessionStarted("s1", "sonnet", null, "bypassPermissions", "2.1.310"));
        ui.Input.Focus();

        UiHelpers.Press(ui.Window, Key.Tab, RawInputModifiers.Shift);

        Assert.Equal(new[] { "default" }, ui.Modes);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void With_ctrl_enter_to_send_enter_breaks_the_line_and_ctrl_enter_sends()
    {
        var ui = Open();
        Assert.False(ui.View.UseCtrlEnterToSend);
        ui.Type("oi");
        UiHelpers.Press(ui.Window, Key.Enter, RawInputModifiers.Control);
        Assert.Empty(ui.Sent);                          // off: Ctrl+Enter is what it always was, and Enter sends

        ui.View.UseCtrlEnterToSend = true;
        UiHelpers.Pump();
        ui.Input.Clear();
        ui.Type("oi");
        UiHelpers.Press(ui.Window, Key.Enter);
        UiHelpers.Type(ui.Window, "tchau");
        Assert.Empty(ui.Sent);
        Assert.Equal("oi\ntchau", Lines(ui.Input));
        UiHelpers.Press(ui.Window, Key.Enter, RawInputModifiers.Shift);
        Assert.Empty(ui.Sent);                          // Shift+Enter still breaks the line

        UiHelpers.Press(ui.Window, Key.Enter, RawInputModifiers.Control);
        Assert.Equal("oi\ntchau\n", Assert.Single(ui.Sent).Replace("\r\n", "\n"));
        Assert.Equal("", ui.Input.Text ?? "");
        Assert.DoesNotContain(Key.Enter, ui.Unhandled);

        ui.Type("de novo");
        UiHelpers.Press(ui.Window, Key.Enter, RawInputModifiers.Meta);
        Assert.Equal("de novo", ui.Sent.Last());         // Cmd+Enter too

        ui.View.UseCtrlEnterToSend = false;
        UiHelpers.Pump();
        ui.Type("volta");
        UiHelpers.Press(ui.Window, Key.Enter);
        Assert.Equal("volta", ui.Sent.Last());
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Ctrl_alt_f_asks_for_the_focus_view_while_the_keyboard_is_in_the_view()
    {
        var ui = Open();
        var toggles = 0;
        ui.View.FocusViewToggleRequested += (_, _) => toggles++;
        ui.Input.Focus();

        UiHelpers.Press(ui.Window, Key.F, RawInputModifiers.Control | RawInputModifiers.Alt);

        Assert.Equal(1, toggles);
        Assert.DoesNotContain(Key.F, ui.Unhandled);
        Assert.Equal("", ui.Input.Text ?? "");
        UiHelpers.Press(ui.Window, Key.F, RawInputModifiers.Control);
        UiHelpers.Press(ui.Window, Key.F, RawInputModifiers.Alt);
        Assert.Equal(1, toggles);                        // both modifiers, and nothing else
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Altgr_f_that_types_a_character_is_not_the_focus_views_shortcut()
    {
        // on Windows AltGr is Ctrl+Alt: a layout where AltGr+F gives a character keeps it
        var ui = Open();
        var toggles = 0;
        ui.View.FocusViewToggleRequested += (_, _) => toggles++;
        ui.Input.Focus();

        ui.Window.KeyPress(Key.F, RawInputModifiers.Control | RawInputModifiers.Alt, PhysicalKey.F, "ƒ");
        UiHelpers.Pump();

        Assert.Equal(0, toggles);
        Assert.Contains(Key.F, ui.Unhandled);            // not taken: the character goes on to the box
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_read_only_view_ignores_the_new_shortcuts()
    {
        var ui = Open(WithHistory("old"));
        ui.Conversation.Apply(new SessionStarted("s1", "sonnet", null, "default", "2.1.310"));
        var toggles = 0;
        ui.View.FocusViewToggleRequested += (_, _) => toggles++;
        ui.Input.Focus();
        Raise(ui.Input, Key.Up);
        Assert.Equal("old", ui.Input.Text);              // the same keys act while the view can be answered in
        ui.Input.Clear();
        Raise(ui.Input, Key.J, KeyModifiers.Control);
        Raise(ui.Input, Key.Tab, KeyModifiers.Shift);
        Raise(ui.Input, Key.F, KeyModifiers.Control | KeyModifiers.Alt);
        Assert.Equal(1, toggles);
        Assert.Single(ui.Modes);
        Assert.NotEqual("", ui.Input.Text ?? "");        // the line was broken
        ui.Input.Clear();
        ui.Modes.Clear();
        toggles = 0;

        ui.View.IsReadOnly = true;
        UiHelpers.Pump();
        Raise(ui.Input, Key.J, KeyModifiers.Control);
        Raise(ui.Input, Key.Tab, KeyModifiers.Shift);
        Raise(ui.Input, Key.F, KeyModifiers.Control | KeyModifiers.Alt);
        Raise(ui.Input, Key.Up);
        UiHelpers.Press(ui.Window, Key.J, RawInputModifiers.Control);
        UiHelpers.Press(ui.Window, Key.Tab, RawInputModifiers.Shift);
        UiHelpers.Press(ui.Window, Key.F, RawInputModifiers.Control | RawInputModifiers.Alt);
        UiHelpers.Press(ui.Window, Key.Up);

        Assert.Equal(0, toggles);
        Assert.Empty(ui.Modes);
        Assert.Equal("", ui.Input.Text ?? "");
        ui.Window.Close();
    }
}
