using System;
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
using ClaudeVisualChat.Protocol;
using Xunit;

namespace ClaudeVisualChat.Tests.Ui;

/// <summary>
/// The host's back button (<see cref="ClaudeChatView.CloseTopLayer"/>), which closes one layer of the view per call, the
/// top-most first; and what the touch layout does about the layers that it closes: the ✕ of the banners, of the image preview and
/// of the Agent map (and its back arrow) are targets of 44 DIPs, and the keyboard goes to a request that waits and to nobody else
/// (the reply box with the keyboard is the soft keyboard coming up).
/// </summary>
public class CloseTopLayerTests
{
    /// <summary>A clock where a second goes by between any two readings, so no answer ever comes too soon: these tests are not about the pause.</summary>
    private sealed class SlowClock : IPromptClock
    {
        private TimeSpan _now = TimeSpan.FromHours(1);

        public TimeSpan Now => _now += TimeSpan.FromSeconds(1);
    }

    private sealed class Opened
    {
        public Opened(ChatConversation conversation, ChatLayout layout = ChatLayout.Touch)
        {
            Conversation = conversation;
            View = new ClaudeChatView { Conversation = conversation, Layout = layout, Strings = ChatStrings.PortugueseBrazil };
            View.Prompt.Clock = new SlowClock();
            Window = layout == ChatLayout.Touch ? UiHelpers.ShowPhone(View) : UiHelpers.Show(View);
        }

        public ChatConversation Conversation { get; }
        public ClaudeChatView View { get; }
        public Window Window { get; }
        public ChatStrings Strings => View.Strings;

        public ChatComposer Composer => UiHelpers.Composer(View);
        public TextBox Input => UiHelpers.Input(View);
        public PermissionPrompt Prompt => UiHelpers.Prompt(View);
        public ImagePreview Preview => View.GetVisualDescendants().OfType<ImagePreview>().Single();
        public ModelSheet ModelSheet => View.GetVisualDescendants().OfType<ModelSheet>().Single();
        public AddSheet AddSheet => View.GetVisualDescendants().OfType<AddSheet>().Single();
        public UsageSheet UsageSheet => View.GetVisualDescendants().OfType<UsageSheet>().Single();
        public AgentMapPanel Map => View.GetVisualDescendants().OfType<AgentMapPanel>().Single();
        public CommandPalette Palette => Composer.GetLogicalDescendants().OfType<CommandPalette>().Single();
        public BrowserBanner BrowserBanner => View.GetVisualDescendants().OfType<BrowserBanner>().Single();
        public RemoteControlBanner RemoteControlBanner => View.GetVisualDescendants().OfType<RemoteControlBanner>().Single();

        /// <summary>The model pill of the touch bar.</summary>
        public ModelChip Pill => Composer.GetVisualDescendants().OfType<ModelChip>().Single(c => c.Name == "TouchModelChip");

        /// <summary>The thumbnail of the image in a message, which a tap opens in the preview.</summary>
        public Button Thumbnail => View.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && b.Classes.Contains("ClaudeChatMessageImage"));

        public Border Card => Map.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatAgentCard"));

        /// <summary>The field of "No", when it is open.</summary>
        public TextBox? DenyField => Prompt.GetVisualDescendants().OfType<TextBox>().SingleOrDefault(t => t.IsEffectivelyVisible);

        public Button Option(string text) => UiHelpers.ButtonWithText(Prompt, text);

        public string Title(ChatSheet sheet) =>
            sheet.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("ClaudeChatSheetTitle")).Text ?? "";

        /// <summary>Opens the card of the one agent of the map: the phone is narrower than the tree, so the agents' column is reached by scrolling sideways.</summary>
        public void OpenCard()
        {
            var scroller = Map.GetVisualDescendants().OfType<ScrollViewer>().First();
            scroller.Offset = new Vector(scroller.Extent.Width, 0);
            UiHelpers.Pump();
            var row = Map.GetVisualDescendants().OfType<Button>()
                .Single(b => b.IsEffectivelyVisible && b.Classes.Contains("ClaudeChatAgentRow") && !b.Classes.Contains("main"));
            UiHelpers.Click(Window, row);
        }
    }

    private static JsonElement J(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static PermissionRequested Bash(string id) =>
        new PermissionRequested(id, "Bash", "Bash", null, "tool-" + id, J("{\"command\":\"ls\"}"), null);

    /// <summary>The recorded session with a subagent, on Opus with a high effort and the models to choose from.</summary>
    private static ChatConversation AgentsConversation()
    {
        var c = UiHelpers.Recording("subagent", "stream");
        c.AvailableModels = TouchSamples.Models;
        c.Apply(new SessionStarted("s1", "opus", null, "default", "2.1.310"));
        c.Apply(new SettingsApplied(null, "high", true));
        return c;
    }

    /// <summary>A conversation whose message has an image, as the one a user sent from the + sheet.</summary>
    private static ChatConversation WithImageMessage()
    {
        var c = TouchSamples.Opus();
        c.Items.Add(new UserMessageItem("look at this", new[] { ChatImage.FromBytes(UiHelpers.Png(300, 200), "image/png") }));
        return c;
    }

    /// <summary>The keyboard is nowhere: nothing that takes text has it (the soft keyboard stays down), and it is not left on a control that is hidden.</summary>
    private static void AssertTheKeyboardIsNowhere(Opened ui)
    {
        Assert.False(ui.Input.IsFocused);
        Assert.DoesNotContain(ui.View.GetVisualDescendants().OfType<TextBox>(), t => t.IsFocused);
        Assert.Null(UiHelpers.Focused(ui.Window));
    }

    /// <summary>
    /// The middle of each edge of the control, a little inside, and its middle belong to the control: it is on the screen, with nothing
    /// drawn over it. What is under a point is found in the last frame drawn, and the headless window draws one only when asked.
    /// </summary>
    private static void AssertReachable(Window window, Control control)
    {
        UiHelpers.DrawFrames();
        var width = control.Bounds.Width;
        var height = control.Bounds.Height;
        foreach (var (x, y) in new[] { (width / 2, 2.0), (width / 2, height - 2), (2.0, height / 2), (width - 2, height / 2), (width / 2, height / 2) })
        {
            var at = control.TranslatePoint(new Point(x, y), window)!.Value;
            var hit = window.InputHitTest(at) as Visual;
            Assert.True(hit != null && (ReferenceEquals(hit, control) || control.IsVisualAncestorOf(hit)),
                "the point (" + x + ", " + y + ") of the control is " + (hit?.GetType().Name ?? "nothing"));
        }
    }

    private static void AssertTargetOf44(Control control) =>
        Assert.True(control.Bounds.Width >= 44 && control.Bounds.Height >= 44, "the target is " + control.Bounds.Size);

    // ---- the brief ----

    [AvaloniaFact]
    public void Close_top_layer_without_layers_is_false()
    {
        var view = UiHelpers.TouchView(TouchSamples.Opus());
        var window = UiHelpers.ShowPhone(view);
        Assert.False(view.CloseTopLayer());
        window.Close();

        Assert.False(new ClaudeChatView().CloseTopLayer());                  // no conversation, never in a window
    }

    [AvaloniaFact]
    public void Close_top_layer_goes_one_layer_at_a_time()
    {
        var ui = new Opened(AgentsConversation());

        // a sheet on its second page: the page goes back to the first, and then the sheet closes
        UiHelpers.Click(ui.Window, ui.Pill);
        UiHelpers.Click(ui.Window, UiHelpers.ButtonWithText(ui.ModelSheet, "Esforço"));
        Assert.Equal("Esforço", ui.Title(ui.ModelSheet));
        Assert.True(ui.View.CloseTopLayer());
        Assert.True(ui.ModelSheet.IsOpen);
        Assert.Equal("Selecionar modelo", ui.Title(ui.ModelSheet));
        Assert.True(ui.View.CloseTopLayer());
        Assert.False(ui.ModelSheet.IsOpen);

        // the card of an agent goes back to the map, and then the map closes
        ui.Conversation.ShowAgents();
        UiHelpers.Pump();
        ui.OpenCard();
        Assert.True(ui.Card.IsEffectivelyVisible);
        Assert.True(ui.View.CloseTopLayer());
        Assert.False(ui.Card.IsEffectivelyVisible);
        Assert.Equal(ChatPanel.Agents, ui.Conversation.ShownPanel);
        Assert.True(ui.Map.IsEffectivelyVisible);
        Assert.True(ui.View.CloseTopLayer());
        Assert.Equal(ChatPanel.None, ui.Conversation.ShownPanel);
        Assert.False(ui.Map.IsVisible);

        // the usage sheet lives from the panel: closing the sheet is closing the panel
        ui.Conversation.InvokeCommand("context", "");
        UiHelpers.Pump();
        Assert.True(ui.UsageSheet.IsOpen);
        Assert.True(ui.View.CloseTopLayer());
        Assert.Equal(ChatPanel.None, ui.Conversation.ShownPanel);
        Assert.False(ui.UsageSheet.IsOpen);
        Assert.False(ui.View.CloseTopLayer());
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Close_top_layer_closes_the_deny_field()
    {
        var c = new ChatConversation();
        c.Apply(Bash("r1"));
        var ui = new Opened(c);
        UiHelpers.Click(ui.Window, ui.Option(ui.Strings.PermissionNo));
        Assert.True(ui.DenyField!.IsFocused);

        Assert.True(ui.View.CloseTopLayer());
        Assert.Null(ui.DenyField);                                           // the field is closed…
        Assert.Equal("r1", ui.Prompt.Request!.RequestId);                    // …and the request is still there to answer
        Assert.True(ui.Prompt.IsEffectivelyVisible);
        Assert.True(ui.Option(ui.Strings.PermissionNo).IsFocused);           // the keyboard is on the answer that opened it, as Esc in the field leaves it
        Assert.False(ui.View.CloseTopLayer());                               // the request itself is no layer: it has to be answered

        UiHelpers.Pump();                                                    // the answers moved down with the detail, which has its room back
        UiHelpers.Click(ui.Window, ui.Option(ui.Strings.PermissionNo));      // and the field opens again, empty
        Assert.NotNull(ui.DenyField);
        Assert.Equal("", ui.DenyField!.Text ?? "");
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Close_top_layer_leaves_the_keyboard_where_the_user_put_it()
    {
        var c = new ChatConversation();
        c.Apply(Bash("r1"));
        var ui = new Opened(c);
        UiHelpers.Click(ui.Window, ui.Option(ui.Strings.PermissionNo));
        ui.Input.Focus();                                                    // the user went on to the reply box, with the field still open
        UiHelpers.Pump();
        Assert.NotNull(ui.DenyField);

        Assert.True(ui.View.CloseTopLayer());
        Assert.Null(ui.DenyField);
        Assert.True(ui.Input.IsFocused);                                     // it is not taken from there to "No"
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Close_top_layer_closes_the_image_preview_first()
    {
        var c = WithImageMessage();
        var ui = new Opened(c);
        UiHelpers.Click(ui.Window, ui.Thumbnail);
        Assert.True(ui.Preview.IsVisible);
        c.InvokeCommand("context", "");                                      // the usage sheet comes up under the preview
        UiHelpers.Pump();
        Assert.True(ui.UsageSheet.IsOpen);

        Assert.True(ui.View.CloseTopLayer());
        Assert.False(ui.Preview.IsVisible);
        Assert.True(ui.UsageSheet.IsOpen);                                   // the preview was over it
        Assert.True(ui.View.CloseTopLayer());
        Assert.False(ui.UsageSheet.IsOpen);
        Assert.Equal(ChatPanel.None, c.ShownPanel);
        Assert.False(ui.View.CloseTopLayer());
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Close_top_layer_works_on_the_desktop_layout()
    {
        var ui = new Opened(TouchSamples.Opus(), ChatLayout.Desktop);
        UiHelpers.Click(ui.Window, UiHelpers.ButtonWithText(ui.Composer, ui.Strings.ShowCommands));
        Assert.True(ui.Palette.IsOpen);
        Assert.True(ui.View.CloseTopLayer());
        Assert.False(ui.Palette.IsOpen);
        Assert.False(ui.View.CloseTopLayer());

        // the panels of the desktop are layers too
        ui.Conversation.InvokeCommand("usage", "");
        UiHelpers.Pump();
        Assert.Equal(ChatPanel.Usage, ui.Conversation.ShownPanel);
        Assert.True(ui.View.CloseTopLayer());
        Assert.Equal(ChatPanel.None, ui.Conversation.ShownPanel);
        Assert.False(ui.View.CloseTopLayer());
        ui.Window.Close();
    }

    // ---- the order: the top-most layer first ----

    [AvaloniaFact]
    public void Close_top_layer_closes_the_usage_sheet_before_the_model_sheet()
    {
        var ui = new Opened(TouchSamples.Opus());
        ui.ModelSheet.Open();
        ui.Conversation.InvokeCommand("context", "");                        // the host asks for the panel with the model sheet open
        UiHelpers.Pump();
        Assert.True(ui.ModelSheet.IsOpen);
        Assert.True(ui.UsageSheet.IsOpen);                                   // it sits over the model sheet

        Assert.True(ui.View.CloseTopLayer());
        Assert.False(ui.UsageSheet.IsOpen);
        Assert.Equal(ChatPanel.None, ui.Conversation.ShownPanel);
        Assert.True(ui.ModelSheet.IsOpen);
        Assert.True(ui.View.CloseTopLayer());
        Assert.False(ui.ModelSheet.IsOpen);
        Assert.False(ui.View.CloseTopLayer());
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Close_top_layer_follows_the_order_of_the_layers_on_the_screen()
    {
        var ui = new Opened(AgentsConversation());
        var image = ChatImage.FromBytes(UiHelpers.Png(300, 200), "image/png");

        // from the bottom up: the map and the card of an agent, the model sheet, the add sheet, a menu of the reply box, the preview
        ui.Conversation.ShowAgents();
        UiHelpers.Pump();
        ui.OpenCard();
        ui.ModelSheet.Open();
        ui.AddSheet.Open();
        ui.Composer.OpenCommands();
        ui.View.ShowPreview(image, "image.png");

        (bool Preview, bool Menu, bool Add, bool Model, bool Card, bool Map) State() =>
            (ui.Preview.IsVisible, ui.Palette.IsOpen, ui.AddSheet.IsOpen, ui.ModelSheet.IsOpen, ui.Map.ShowsCard, ui.Conversation.ShownPanel == ChatPanel.Agents);

        Assert.Equal((true, true, true, true, true, true), State());
        Assert.True(ui.View.CloseTopLayer());
        Assert.Equal((false, true, true, true, true, true), State());        // the preview
        Assert.True(ui.View.CloseTopLayer());
        Assert.Equal((false, false, true, true, true, true), State());       // the menu
        Assert.True(ui.View.CloseTopLayer());
        Assert.Equal((false, false, false, true, true, true), State());      // the add sheet
        Assert.True(ui.View.CloseTopLayer());
        Assert.Equal((false, false, false, false, true, true), State());     // the model sheet
        Assert.True(ui.View.CloseTopLayer());
        Assert.Equal((false, false, false, false, false, true), State());    // the card
        Assert.True(ui.View.CloseTopLayer());
        Assert.Equal((false, false, false, false, false, false), State());   // the map
        Assert.False(ui.View.CloseTopLayer());
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Close_top_layer_closes_the_map_that_covers_a_request_before_its_field()
    {
        var c = AgentsConversation();
        c.Apply(Bash("r1"));
        var ui = new Opened(c);
        UiHelpers.Click(ui.Window, ui.Option(ui.Strings.PermissionNo));
        c.ShowAgents();                                                      // the map covers the request, on the phone as on the desktop
        UiHelpers.Pump();
        Assert.NotNull(ui.DenyField);
        Assert.True(ui.Map.IsEffectivelyVisible);

        Assert.True(ui.View.CloseTopLayer());
        Assert.Equal(ChatPanel.None, c.ShownPanel);
        Assert.NotNull(ui.DenyField);
        Assert.True(ui.View.CloseTopLayer());
        Assert.Null(ui.DenyField);
        Assert.False(ui.View.CloseTopLayer());
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Close_top_layer_leaves_a_field_that_the_user_cannot_see()
    {
        var c = new ChatConversation();
        c.Apply(Bash("r1"));
        var ui = new Opened(c);
        UiHelpers.Click(ui.Window, ui.Option(ui.Strings.PermissionNo));

        ui.View.IsReadOnly = true;                                           // the request and its field go out of sight
        UiHelpers.Pump();
        Assert.False(ui.View.CloseTopLayer());                               // nothing to close for the user: the host's back button does its own

        ui.View.IsReadOnly = false;
        UiHelpers.Pump();
        Assert.NotNull(ui.DenyField);                                        // the field is as it was left
        Assert.True(ui.View.CloseTopLayer());
        Assert.Null(ui.DenyField);
        ui.Window.Close();
    }

    [AvaloniaTheory]
    [InlineData(ChatLayout.Desktop)]
    [InlineData(ChatLayout.Touch)]
    public void Close_top_layer_closes_the_plan_document_alone(ChatLayout layout)
    {
        var c = new ChatConversation();
        c.Apply(new PermissionRequested("r1", "ExitPlanMode", "ExitPlanMode", null, "t1", J("{\"plan\":\"# Rewrite the parser\"}"), null));
        var ui = new Opened(c, layout);
        var document = ui.View.GetVisualDescendants().OfType<PlanDocumentPanel>().Single();
        UiHelpers.Click(ui.Window, ui.Option(ui.Strings.PlanKeepPlanning));
        if (layout == ChatLayout.Desktop)
        {
            c.Apply(new PermissionRequested("r2", "ExitPlanMode", "ExitPlanMode", null, "t2", J("{\"plan\":\"# Use a list\"}"), null));
            UiHelpers.Pump();
            ui.Prompt.GetVisualDescendants().OfType<TextBox>().Single().Focus(); // the desktop card's field is always there
            UiHelpers.Type(ui.Window, "shorter");
        }
        Assert.NotNull(ui.DenyField);                                        // the field of "No", under the document
        Assert.True(document.IsOpen);
        Assert.False(ui.View.IsSheetOpen);

        Assert.True(ui.View.CloseTopLayer());                                // one call: the document, only it
        Assert.False(document.IsOpen);
        Assert.False(ui.View.IsSheetOpen);
        Assert.NotNull(ui.Prompt.Request);                                   // the request is still there to answer…
        Assert.NotNull(ui.DenyField);                                        // …with its field as it was
        if (layout == ChatLayout.Touch)
        {
            Assert.True(ui.View.CloseTopLayer());                            // then the field
            Assert.Null(ui.DenyField);
        }
        Assert.False(ui.View.CloseTopLayer());
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Close_top_layer_closes_a_sheet_over_the_plan_document_first()
    {
        var c = UiHelpers.Recording("plan", "transcript");                   // a history: no request, so a sheet may open
        c.AvailableModels = TouchSamples.Models;
        c.Apply(new SessionStarted("s1", "opus", null, "default", "2.1.310"));
        var ui = new Opened(c);
        var document = ui.View.GetVisualDescendants().OfType<PlanDocumentPanel>().Single();
        var call = c.Items.OfType<ToolCallItem>().Single(t => t.Name == "ExitPlanMode");
        Assert.True(ui.View.ShowPlan(call));
        ui.ModelSheet.Open();
        UiHelpers.Pump();
        Assert.True(document.IsOpen);
        Assert.True(ui.ModelSheet.IsOpen);

        Assert.True(ui.View.CloseTopLayer());
        Assert.False(ui.ModelSheet.IsOpen);
        Assert.True(document.IsOpen);
        Assert.True(ui.View.CloseTopLayer());
        Assert.False(document.IsOpen);
        Assert.False(ui.View.CloseTopLayer());
        ui.Window.Close();
    }

    // ---- the touch layout: the ✕ of the banners, of the preview and of the Agent map are targets of 44 ----

    [AvaloniaFact]
    public void Touch_browser_banner_close_is_a_target_of_44()
    {
        var c = TouchSamples.Opus();
        c.BrowserStatus = ChatBrowserStatus.Connected;
        var disconnects = 0;
        c.BrowserDisconnectRequested += (_, _) => disconnects++;
        var ui = new Opened(c);
        var close = UiHelpers.ButtonWithText(ui.BrowserBanner, ui.Strings.DisconnectBrowser);

        AssertTargetOf44(close);
        AssertReachable(ui.Window, close);                                   // the reply box is drawn over the last 8 DIPs of the banner

        // a finger at the foot of the target, where the reply box would take it if the target ran under it
        var foot = close.TranslatePoint(new Point(close.Bounds.Width / 2, close.Bounds.Height - 2), ui.Window)!.Value;
        ui.Window.MouseMove(foot, RawInputModifiers.None);
        ui.Window.MouseDown(foot, MouseButton.Left, RawInputModifiers.None);
        ui.Window.MouseUp(foot, MouseButton.Left, RawInputModifiers.None);
        UiHelpers.Pump();
        Assert.Equal(1, disconnects);

        // the desktop's is as it was
        ui.View.Layout = ChatLayout.Desktop;
        UiHelpers.Pump();
        Assert.Equal(new Size(20, 20), close.Bounds.Size);
        Assert.Equal(new Thickness(16, 6, 6, 12), ui.BrowserBanner.Padding);
        ui.View.Layout = ChatLayout.Touch;                                   // and the phone's is back with the layout
        UiHelpers.Pump();
        AssertTargetOf44(close);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Touch_remote_control_banner_close_is_a_target_of_44()
    {
        var c = TouchSamples.Opus();
        c.RemoteControlError = "bridge closed";
        c.RemoteControlStatus = ChatRemoteControlStatus.Error;
        var commands = new List<string>();
        c.CommandInvoked += (_, e) => commands.Add(e.Name + " " + e.Arguments);
        var ui = new Opened(c);
        var close = UiHelpers.ButtonWithText(ui.RemoteControlBanner, ui.Strings.Dismiss);

        AssertTargetOf44(close);
        AssertReachable(ui.Window, close);
        UiHelpers.Click(ui.Window, close);
        Assert.Equal(new[] { "remote-control off" }, commands);

        ui.View.Layout = ChatLayout.Desktop;
        UiHelpers.Pump();
        Assert.Equal(new Size(20, 20), close.Bounds.Size);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Touch_preview_close_is_a_target_of_44()
    {
        var ui = new Opened(WithImageMessage());
        UiHelpers.Click(ui.Window, ui.Thumbnail);
        var close = UiHelpers.ButtonWithText(ui.Preview, ui.Strings.Close);

        AssertTargetOf44(close);
        AssertReachable(ui.Window, close);
        var corner = new Rect(close.TranslatePoint(default, ui.Window)!.Value, close.Bounds.Size);
        Assert.True(new Rect(0, 0, UiHelpers.PhoneWidth, UiHelpers.PhoneHeight).Contains(corner), "the target sticks out of the screen: " + corner);

        // the desktop's is as it was
        ui.View.Layout = ChatLayout.Desktop;
        UiHelpers.Pump();
        Assert.Equal(new Size(28, 28), close.Bounds.Size);
        ui.View.Layout = ChatLayout.Touch;
        UiHelpers.Pump();
        AssertTargetOf44(close);

        UiHelpers.Click(ui.Window, close);
        Assert.False(ui.Preview.IsVisible);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Touch_agent_map_close_and_back_arrow_are_targets_of_44()
    {
        var ui = new Opened(AgentsConversation());
        ui.Conversation.ShowAgents();
        UiHelpers.Pump();
        var close = UiHelpers.ButtonWithText(ui.Map, ui.Strings.Close);

        AssertTargetOf44(close);
        AssertReachable(ui.Window, close);

        ui.OpenCard();                                                       // the back arrow is on the card of an agent, over the map
        var back = UiHelpers.ButtonWithText(ui.Map, ui.Strings.Back);
        AssertTargetOf44(back);
        AssertReachable(ui.Window, back);

        UiHelpers.Click(ui.Window, back);                                    // a tap on each does what it does on the desktop
        Assert.False(ui.Card.IsEffectivelyVisible);
        Assert.Equal(ChatPanel.Agents, ui.Conversation.ShownPanel);
        UiHelpers.Click(ui.Window, close);
        Assert.Equal(ChatPanel.None, ui.Conversation.ShownPanel);

        // the desktop's are as they were
        ui.Conversation.ShowAgents();
        UiHelpers.Pump();
        ui.OpenCard();
        ui.View.Layout = ChatLayout.Desktop;
        UiHelpers.Pump();
        Assert.Equal(new Size(28, 28), close.Bounds.Size);
        Assert.Equal(new Size(28, 28), back.Bounds.Size);
        ui.View.Layout = ChatLayout.Touch;                                   // and the phone's are back with the layout
        UiHelpers.Pump();
        AssertTargetOf44(close);
        AssertTargetOf44(back);
        ui.Window.Close();
    }

    // ---- the touch layout: the keyboard goes to a request that waits and to nobody else ----

    [AvaloniaFact]
    public void Touch_closing_the_preview_does_not_give_the_keyboard_to_the_reply_box()
    {
        var ui = new Opened(WithImageMessage());
        UiHelpers.Click(ui.Window, ui.Thumbnail);
        Assert.True(ui.Preview.IsKeyboardFocusWithin);                       // the preview took it, on its ✕
        UiHelpers.Click(ui.Window, UiHelpers.ButtonWithText(ui.Preview, ui.Strings.Close));
        Assert.False(ui.Preview.IsVisible);
        AssertTheKeyboardIsNowhere(ui);

        UiHelpers.Click(ui.Window, ui.Thumbnail);                            // Esc…
        UiHelpers.Press(ui.Window, Key.Escape);
        Assert.False(ui.Preview.IsVisible);
        AssertTheKeyboardIsNowhere(ui);

        UiHelpers.Click(ui.Window, ui.Thumbnail);                            // …and the host's back button
        Assert.True(ui.View.CloseTopLayer());
        Assert.False(ui.Preview.IsVisible);
        AssertTheKeyboardIsNowhere(ui);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Touch_a_request_that_waited_for_the_preview_gets_the_keyboard_when_it_closes()
    {
        var ui = new Opened(WithImageMessage());
        UiHelpers.Click(ui.Window, ui.Thumbnail);
        ui.Conversation.Apply(Bash("r1"));                                   // it comes while the preview covers the view
        UiHelpers.Pump();
        Assert.True(ui.Prompt.IsEffectivelyVisible);
        Assert.False(ui.Prompt.IsKeyboardFocusWithin);

        Assert.True(ui.View.CloseTopLayer());
        Assert.True(ui.Option(ui.Strings.PermissionYes).IsFocused);          // its first answer, as ever
        Assert.False(ui.Input.IsFocused);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Touch_closing_the_agent_map_does_not_give_the_keyboard_to_the_reply_box()
    {
        var ui = new Opened(AgentsConversation());
        ui.Conversation.ShowAgents();
        UiHelpers.Pump();
        Assert.True(ui.Map.IsKeyboardFocusWithin);                           // the map took it when it opened
        UiHelpers.Press(ui.Window, Key.Escape);
        Assert.Equal(ChatPanel.None, ui.Conversation.ShownPanel);
        AssertTheKeyboardIsNowhere(ui);

        ui.Conversation.ShowAgents();
        UiHelpers.Pump();
        Assert.True(ui.View.CloseTopLayer());
        Assert.Equal(ChatPanel.None, ui.Conversation.ShownPanel);
        AssertTheKeyboardIsNowhere(ui);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Touch_closing_the_usage_sheet_does_not_give_the_keyboard_to_the_reply_box()
    {
        var ui = new Opened(TouchSamples.Opus());
        ui.Conversation.InvokeCommand("context", "");
        UiHelpers.Pump();
        Assert.True(ui.UsageSheet.IsKeyboardFocusWithin);
        UiHelpers.Press(ui.Window, Key.Escape);
        Assert.False(ui.UsageSheet.IsOpen);
        AssertTheKeyboardIsNowhere(ui);

        ui.Conversation.InvokeCommand("context", "");
        UiHelpers.Pump();
        Assert.True(ui.View.CloseTopLayer());
        Assert.False(ui.UsageSheet.IsOpen);
        AssertTheKeyboardIsNowhere(ui);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Touch_closing_the_preview_leaves_the_keyboard_on_a_hidden_control_of_the_host()
    {
        // the view lets go of a keyboard that the layer it closed left on a hidden control of its own, and of no other: what
        // lies outside the view is the host's
        var view = UiHelpers.TouchView(WithImageMessage());
        var sibling = new TextBox();
        var panel = new Border { Child = sibling };                          // the host's own panel
        var host = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Children = { view, panel } };
        Grid.SetRow(panel, 1);
        var window = UiHelpers.ShowPhone(host);
        var thumbnail = view.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && b.Classes.Contains("ClaudeChatMessageImage"));
        var preview = view.GetVisualDescendants().OfType<ImagePreview>().Single();

        UiHelpers.Click(window, thumbnail);
        Assert.True(preview.IsVisible);
        sibling.Focus();                                                     // the host's control takes the keyboard…
        panel.IsVisible = false;                                             // …and its panel is hidden, which leaves it there, as with the preview's ✕
        UiHelpers.Pump();
        Assert.False(sibling.IsEffectivelyVisible);
        Assert.Same(sibling, UiHelpers.Focused(window));

        Assert.True(view.CloseTopLayer());
        Assert.False(preview.IsVisible);
        Assert.Same(sibling, UiHelpers.Focused(window));                     // the view does not take it off a control that is not its own
        window.Close();
    }

    [AvaloniaFact]
    public void Desktop_closing_the_preview_still_gives_the_keyboard_to_the_reply_box()
    {
        var ui = new Opened(WithImageMessage(), ChatLayout.Desktop);
        UiHelpers.Click(ui.Window, ui.Thumbnail);
        Assert.True(ui.Preview.IsKeyboardFocusWithin);
        UiHelpers.Press(ui.Window, Key.Escape);
        Assert.False(ui.Preview.IsVisible);
        Assert.True(ui.Input.IsFocused);                                     // as it always did there

        UiHelpers.Click(ui.Window, ui.Thumbnail);
        Assert.False(ui.Input.IsFocused);
        Assert.True(ui.View.CloseTopLayer());                                // and through the host's back button
        Assert.True(ui.Input.IsFocused);
        ui.Window.Close();
    }

    // ---- the plan's document under the passing layers, and its comment box over them (0.6.0, R4b) ----

    private static PermissionRequested PlanRequest(string id = "r1") =>
        new PermissionRequested(id, "ExitPlanMode", "ExitPlanMode", null, "t-" + id, J("{\"plan\":\"# Rewrite the parser\\n\\nIntro line.\"}"), null);

    /// <summary>The middle of <paramref name="control"/> belongs to <paramref name="over"/>: it is drawn over it there.</summary>
    private static void AssertCovers(Window window, Control over, Control control)
    {
        UiHelpers.DrawFrames();
        var middle = control.TranslatePoint(new Point(control.Bounds.Width / 2, Math.Min(control.Bounds.Height / 2, 60)), window)!.Value;
        var hit = window.InputHitTest(middle) as Visual;
        Assert.True(hit != null && (ReferenceEquals(hit, over) || over.IsVisualAncestorOf(hit)), "the point is " + (hit?.GetType().Name ?? "nothing"));
    }

    [AvaloniaFact]
    public void Close_top_layer_closes_the_map_over_the_plan_document_before_the_document()
    {
        var c = new ChatConversation();
        c.Apply(PlanRequest());
        var ui = new Opened(c, ChatLayout.Desktop);
        var document = ui.View.GetVisualDescendants().OfType<PlanDocumentPanel>().Single();
        Assert.True(document.IsOpen);

        c.ShowAgents();                                                      // the map opened with the document open: over it, whole
        UiHelpers.Pump();
        Assert.True(ui.Map.IsVisible);
        AssertCovers(ui.Window, ui.Map, document);
        var mapClose = ui.Map.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible);
        AssertReachable(ui.Window, mapClose);

        Assert.True(ui.View.CloseTopLayer());                                // the map first…
        Assert.False(ui.Map.IsVisible);
        Assert.True(document.IsOpen);
        Assert.True(ui.View.CloseTopLayer());                                // …then the document
        Assert.False(document.IsOpen);
        Assert.False(ui.View.CloseTopLayer());
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Close_top_layer_and_esc_close_the_usage_panel_over_the_plan_document_before_the_document()
    {
        var c = TouchSamples.Opus();
        c.Apply(PlanRequest());
        var ui = new Opened(c, ChatLayout.Desktop);
        var document = ui.View.GetVisualDescendants().OfType<PlanDocumentPanel>().Single();
        var panel = ui.View.GetVisualDescendants().OfType<UsagePanel>().Single();

        c.InvokeCommand("usage", "");                                        // /usage with the document open: over it, whole
        UiHelpers.Pump();
        Assert.True(panel.IsVisible);
        var panelRect = new Rect(panel.TranslatePoint(default, ui.View)!.Value, panel.Bounds.Size);
        var documentRect = new Rect(document.TranslatePoint(default, ui.View)!.Value, document.Bounds.Size);
        Assert.True(panelRect.Intersects(documentRect), panelRect + " / " + documentRect); // where they meet…
        AssertReachable(ui.Window, panel);                                   // …the panel is the one on top
        Assert.True(ui.View.CloseTopLayer());
        Assert.Equal(ChatPanel.None, c.ShownPanel);
        Assert.True(document.IsOpen);
        Assert.True(ui.View.CloseTopLayer());
        Assert.False(document.IsOpen);

        // the same with Esc, the card's option having the keyboard: the panel, then the document, then the card's "No"
        var answers = new List<string>();
        c.PermissionAnswered += (_, e) => answers.Add(e.Request.RequestId);
        UiHelpers.Click(ui.Window, UiHelpers.ButtonWithText(ui.Prompt, ui.Strings.PlanKeepPlanning)); // refused: r1 goes, the document stays closed
        c.Apply(PlanRequest("r2"));
        UiHelpers.Pump();
        Assert.True(document.IsOpen);
        c.InvokeCommand("usage", "");
        UiHelpers.Pump();
        ui.Prompt.FocusFirstOption();
        UiHelpers.Press(ui.Window, Key.Escape);
        Assert.Equal(ChatPanel.None, c.ShownPanel);
        Assert.True(document.IsOpen);
        UiHelpers.Press(ui.Window, Key.Escape);
        Assert.False(document.IsOpen);
        Assert.Equal(new[] { "r1" }, answers);
        UiHelpers.Press(ui.Window, Key.Escape);
        Assert.Equal(new[] { "r1", "r2" }, answers);
        ui.Window.Close();
    }

    [AvaloniaTheory]
    [InlineData(ChatLayout.Desktop)]
    [InlineData(ChatLayout.Touch)]
    public void Close_top_layer_closes_the_comment_box_over_the_map_and_the_document_first(ChatLayout layout)
    {
        var c = new ChatConversation();
        c.Apply(PlanRequest());
        var ui = new Opened(c, layout);
        var document = ui.View.GetVisualDescendants().OfType<PlanDocumentPanel>().Single();
        var box = ui.View.GetVisualDescendants().OfType<PlanCommentBox>().Single();
        c.ShowAgents();
        UiHelpers.Pump();
        box.Open(new PlanCommentRequest(c.CurrentPlan!, "Intro", new Rect(24, 60, 40, 22)));
        UiHelpers.Pump();
        var frame = box.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatPlanCommentBox"));
        AssertReachable(ui.Window, frame);                                   // over the map
        Assert.False(ui.View.IsSheetOpen);                                   // no sheet

        Assert.True(ui.View.CloseTopLayer());                                // the box…
        Assert.False(box.IsOpen);
        Assert.True(ui.Map.IsVisible);
        Assert.True(ui.View.CloseTopLayer());                                // …the map…
        Assert.False(ui.Map.IsVisible);
        Assert.True(document.IsOpen);
        Assert.True(ui.View.CloseTopLayer());                                // …the document
        Assert.False(document.IsOpen);
        Assert.False(ui.View.CloseTopLayer());
        Assert.NotNull(ui.Prompt.Request);                                   // and the request is still there to answer
        ui.Window.Close();
    }

    [AvaloniaTheory]
    [InlineData(ChatLayout.Desktop)]
    [InlineData(ChatLayout.Touch)]
    public void Close_top_layer_closes_the_diff_view_alone_under_the_preview(ChatLayout layout)
    {
        var c = TouchSamples.Opus();
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("t1", "Edit", J("{\"file_path\":\"a.cs\",\"old_string\":\"x\",\"new_string\":\"y\"}"))));
        c.Apply(PlanRequest());                                              // the plan's document is open under it
        var ui = new Opened(c, layout);
        var document = ui.View.GetVisualDescendants().OfType<PlanDocumentPanel>().Single();
        var diff = ui.View.GetVisualDescendants().OfType<DiffViewPanel>().Single();
        Assert.True(ui.View.ShowDiff(c.Items.OfType<ToolCallItem>().Single(t => t.Name == "Edit")));
        ui.View.ShowPreview(ChatImage.FromBytes(UiHelpers.Png(300, 200), "image/png"), "image.png");
        UiHelpers.Pump();
        Assert.True(diff.IsOpen);
        Assert.True(document.IsOpen);
        Assert.False(ui.View.IsSheetOpen);                                   // the Diff View is no sheet

        Assert.True(ui.View.CloseTopLayer());                                // the preview over it first
        Assert.False(ui.Preview.IsVisible);
        Assert.True(diff.IsOpen);
        Assert.True(ui.View.CloseTopLayer());                                // one call: the Diff View, only it
        Assert.False(diff.IsOpen);
        Assert.True(document.IsOpen);
        Assert.False(ui.View.IsSheetOpen);
        Assert.True(ui.View.CloseTopLayer());                                // then the document
        Assert.False(document.IsOpen);
        ui.Window.Close();
    }

    [AvaloniaTheory]
    [InlineData(ChatLayout.Desktop)]
    [InlineData(ChatLayout.Touch)]
    public void Close_top_layer_closes_what_went_wrong_alone_under_the_diff_view(ChatLayout layout)
    {
        var c = TouchSamples.Opus();
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("t1", "Edit", J("{\"file_path\":\"a.cs\",\"old_string\":\"x\",\"new_string\":\"y\"}"))));
        var answer = new AssistantTextItem { Text = "Done.", Uuid = "u-1" };
        c.Items.Add(answer);
        c.Items.Add(new TurnFooterItem(1000, 1, 1, null, false));
        var asked = new List<FeedbackRequestedEventArgs>();
        c.FeedbackRequested += (_, e) => asked.Add(e);
        c.Apply(PlanRequest());                                              // the plan's document is open under it all
        var ui = new Opened(c, layout);
        var document = ui.View.GetVisualDescendants().OfType<PlanDocumentPanel>().Single();
        var diff = ui.View.GetVisualDescendants().OfType<DiffViewPanel>().Single();
        var feedback = ui.View.GetVisualDescendants().OfType<FeedbackDialog>().Single();
        Assert.True(ui.View.ShowFeedback(answer));
        Assert.True(ui.View.ShowDiff(c.Items.OfType<ToolCallItem>().Single(t => t.Name == "Edit")));
        UiHelpers.Pump();
        Assert.True(feedback.IsOpen);
        Assert.True(diff.IsOpen);
        Assert.False(ui.View.IsSheetOpen);                                   // the box is no sheet

        Assert.True(ui.View.CloseTopLayer());                                // the Diff View over it first
        Assert.False(diff.IsOpen);
        Assert.True(feedback.IsOpen);
        Assert.True(ui.View.CloseTopLayer());                                // one call: the box, only it
        Assert.False(feedback.IsOpen);
        Assert.True(document.IsOpen);
        Assert.Empty(asked);                                                 // closed as Skip: nothing sent
        Assert.True(ui.View.CloseTopLayer());                                // then the document
        Assert.False(document.IsOpen);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Close_top_layer_closes_a_sheet_over_the_comment_box_first()
    {
        var c = TouchSamples.Opus();
        c.Apply(PlanRequest());
        var ui = new Opened(c);
        var box = ui.View.GetVisualDescendants().OfType<PlanCommentBox>().Single();
        box.Open(new PlanCommentRequest(c.CurrentPlan!, "Intro", new Rect(24, 60, 40, 22)));
        ui.View.ShowPreview(ChatImage.FromBytes(UiHelpers.Png(300, 200), "image/png"), "image.png");
        UiHelpers.Pump();
        Assert.True(ui.Preview.IsVisible);
        Assert.True(ui.View.CloseTopLayer());
        Assert.False(ui.Preview.IsVisible);
        Assert.True(box.IsOpen);
        Assert.True(ui.View.CloseTopLayer());
        Assert.False(box.IsOpen);
        ui.Window.Close();
    }
}
