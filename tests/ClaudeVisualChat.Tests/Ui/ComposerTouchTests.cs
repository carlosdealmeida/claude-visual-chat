using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using ClaudeVisualChat.Protocol;
using Xunit;

namespace ClaudeVisualChat.Tests.Ui;

/// <summary>The reply box of the touch layout (<see cref="ChatLayout.Touch"/>): +, the model, the context ring, send; the agents at the end of the list.</summary>
public class ComposerTouchTests
{
    [AvaloniaFact]
    public void Touch_bar_has_add_model_ring_and_send()
    {
        var c = TouchSamples.Opus();
        c.Apply(new ContextUsageReported(TouchSamples.Context));
        var view = UiHelpers.TouchView(c);
        UiHelpers.ShowPhone(view);
        var names = UiHelpers.ButtonNames(UiHelpers.Composer(view));
        Assert.Contains("Adicionar", names);
        Assert.Contains("Opus 5.5 Alto", names);
        Assert.Contains("Enviar", names);
        Assert.Contains(names, n => n.StartsWith("85% do contexto restante"));   // the ring, 15% used
        Assert.DoesNotContain("Mostrar o menu de comandos (/)", names);
        Assert.DoesNotContain(UiHelpers.Composer(view).GetVisualDescendants().OfType<ComboBox>(), b => b.IsEffectivelyVisible);
        foreach (var b in UiHelpers.Composer(view).GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible))
            Assert.True(b.Bounds.Width >= 44 && b.Bounds.Height >= 44, AutomationProperties.GetName(b));
    }

    [AvaloniaFact]
    public void Touch_placeholder_is_the_apps()
    {
        var c = TouchSamples.Opus();
        var view = UiHelpers.TouchView(c);
        UiHelpers.ShowPhone(view);
        Assert.Equal("Digite / para ver os comandos", UiHelpers.Input(view).Watermark);
        c.Submit("x");                                                   // busy: the queue placeholder
        UiHelpers.Pump();
        Assert.Equal("Pôr outra mensagem na fila…", UiHelpers.Input(view).Watermark);
    }

    [AvaloniaFact]
    public void Touch_enter_breaks_the_line_and_ctrl_enter_sends()
    {
        var c = TouchSamples.Opus();
        var sent = new List<string>();
        c.MessageSubmitted += (_, e) => sent.Add(e.Text);
        var view = UiHelpers.TouchView(c);
        var window = UiHelpers.ShowPhone(view);
        UiHelpers.Input(view).Focus();
        UiHelpers.Type(window, "first");
        UiHelpers.Press(window, Key.Enter);
        Assert.Empty(sent);
        Assert.Contains("\n", UiHelpers.Input(view).Text);
        UiHelpers.Press(window, Key.Enter, RawInputModifiers.Control);
        Assert.Single(sent);
    }

    [AvaloniaFact]
    public void Touch_agents_chip_sits_at_the_end_of_the_list()
    {
        var view = UiHelpers.TouchView(UiHelpers.Recording("subagent", "stream"));
        UiHelpers.ShowPhone(view);
        var chips = view.GetVisualDescendants().OfType<AgentsChip>().Where(a => a.IsEffectivelyVisible).ToList();
        var chip = Assert.Single(chips);
        Assert.NotNull(chip.FindAncestorOfType<ChatItemList>());            // at the end of the list, scrolls with it
        Assert.StartsWith("Agentes · ", UiHelpers.Texts(chip).First());
    }

    [AvaloniaFact]
    public void Touch_list_keeps_its_end_when_the_view_gets_shorter()
    {
        var c = new ChatConversation();
        for (var i = 0; i < 30; i++) c.Submit("message " + i);
        var view = UiHelpers.TouchView(c);
        var window = UiHelpers.ShowPhone(view);
        Assert.True(UiHelpers.AtEnd(UiHelpers.Scroller(view)));
        window.Height = 520;                                             // the keyboard came up
        UiHelpers.PumpUntil(() => UiHelpers.AtEnd(UiHelpers.Scroller(view)));
    }

    [AvaloniaFact]
    public void Desktop_bar_is_unchanged()
    {
        var view = new ClaudeChatView { Conversation = TouchSamples.Opus() };
        UiHelpers.Show(view);
        var composer = UiHelpers.Composer(view);
        Assert.False(composer.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "TouchBar").IsEffectivelyVisible);
        Assert.Contains("Show command menu (/)", UiHelpers.ButtonNames(composer));
    }

    // ---- more of the same, one behavior each ----

    private static T Part<T>(ChatComposer composer, string name) where T : Control =>
        composer.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    private static Border Box(ChatComposer composer) =>
        composer.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("ClaudeChatComposer"));

    [Fact]
    public void Touch_texts_in_both_languages()
    {
        Assert.Equal(("Type / for commands", "Digite / para ver os comandos"),
            (ChatStrings.English.InputPlaceholderTouch, ChatStrings.PortugueseBrazil.InputPlaceholderTouch));
        // agents at work: how many; none at work: how many there were
        Assert.Equal("Agents · 2 running", ChatStrings.English.FormatAgentsChipTouch(2, 5));
        Assert.Equal("Agents · 5", ChatStrings.English.FormatAgentsChipTouch(0, 5));
        Assert.Equal("Agentes · 2 rodando", ChatStrings.PortugueseBrazil.FormatAgentsChipTouch(2, 5));
        Assert.Equal("Agentes · 5", ChatStrings.PortugueseBrazil.FormatAgentsChipTouch(0, 5));
        Assert.Equal("Agentes · 1.200", ChatStrings.PortugueseBrazil.FormatAgentsChipTouch(0, 1200));  // the language's separators
    }

    [AvaloniaFact]
    public void Touch_samples_are_the_claudes_list_and_a_long_session()
    {
        Assert.Equal(new[] { "opus", "claude-fable-5-1", "sonnet", "haiku", "claude-sonnet-5", "claude-opus-5", "claude-fable-5", "claude-opus-4-8" },
            TouchSamples.Models.Select(m => m.Value));
        Assert.Equal(5, TouchSamples.Models[0].EffortLevels.Count);
        Assert.Empty(TouchSamples.Models.Single(m => m.Value == "haiku").EffortLevels);
        Assert.Equal(15.06, ContextRing.UsedOf(TouchSamples.Context), 2);
        var c = TouchSamples.Opus();
        Assert.Equal("Opus 5.5", c.CurrentModel!.DisplayName);
        Assert.Equal("high", c.Effort);
    }

    [AvaloniaFact]
    public void Touch_box_is_a_rounded_one_that_rings_in_clay_with_the_focus_whatever_the_mode()
    {
        var c = TouchSamples.Opus();
        var view = UiHelpers.TouchView(c);
        UiHelpers.ShowPhone(view);
        var box = Box(UiHelpers.Composer(view));
        Assert.Equal(new CornerRadius(24), box.CornerRadius);
        Assert.Equal(new Thickness(12, 10, 10, 8), box.Padding);
        Assert.Same(box.FindResource("ClaudeChat.InputBackground"), box.Background);
        Assert.Same(box.FindResource("ClaudeChat.BorderStrong"), box.BorderBrush);

        c.Apply(new StatusChanged(null, "plan"));                         // the desktop's box takes the mode's color
        UiHelpers.Input(view).Focus();
        UiHelpers.Pump();
        Assert.Contains("plan", box.Classes);
        Assert.Same(box.FindResource("ClaudeChat.FocusRing"), box.BorderBrush);
        Assert.Equal(box.FindResource("ClaudeChat.ComposerShadow"), box.BoxShadow); // not the colored ring of the desktop's
    }

    [AvaloniaFact]
    public void Touch_text_box_grows_to_six_lines_then_scrolls()
    {
        var view = UiHelpers.TouchView(TouchSamples.Opus());
        UiHelpers.ShowPhone(view);
        var input = UiHelpers.Input(view);
        Assert.Equal(22.5, input.LineHeight);
        Assert.Equal(6 * 22.5, input.MaxHeight);
        Assert.Equal(new Thickness(0, 0, 0, 8), input.Margin);
        Assert.Equal(new Thickness(0), input.Padding);

        input.Text = "one";
        UiHelpers.Pump();
        Assert.InRange(input.Bounds.Height, 22.5, 23.5);
        input.Text = "one\ntwo\nthree";
        UiHelpers.Pump();
        Assert.InRange(input.Bounds.Height, 3 * 22.5, 3 * 22.5 + 1);
        input.Text = string.Join("\n", Enumerable.Range(1, 12).Select(i => "line " + i));
        UiHelpers.Pump();
        Assert.Equal(6 * 22.5, input.Bounds.Height);                      // and then it scrolls
    }

    [AvaloniaFact]
    public void Touch_shows_only_its_bar_and_leaves_out_the_slash_the_clock_the_chips_and_the_mode()
    {
        var c = TouchSamples.Opus();
        c.Apply(new PromptCacheUsed(TimeSpan.FromHours(1), null));
        c.Apply(new AgentTaskStarted("t1", "toolu_1", "Look around", "Explore", true, 1, "local_agent"));
        c.RemoteControlStatus = ChatRemoteControlStatus.Connected;
        c.RemoteControlUrl = "https://claude.ai/code/session_1";

        var desktop = new ClaudeChatView { Conversation = c };
        UiHelpers.Show(desktop);
        var bar = UiHelpers.Composer(desktop);
        Assert.True(Part<Button>(bar, "AddButton").IsEffectivelyVisible);
        Assert.True(Part<Button>(bar, "CommandsButton").IsEffectivelyVisible);
        Assert.True(Part<PromptCacheClock>(bar, "CacheClock").IsEffectivelyVisible);
        Assert.True(Part<RemoteControlChip>(bar, "RemoteControlChip").IsEffectivelyVisible);
        Assert.True(Part<AgentsChip>(bar, "AgentsChip").IsEffectivelyVisible);
        Assert.True(Part<ComboBox>(bar, "ModePicker").IsEffectivelyVisible);

        var touch = UiHelpers.TouchView(c);
        UiHelpers.ShowPhone(touch);
        var composer = UiHelpers.Composer(touch);
        foreach (var name in new[] { "AddButton", "CommandsButton", "SendButton", "StopButton" })
            Assert.False(Part<Button>(composer, name).IsEffectivelyVisible, name);
        Assert.False(Part<PromptCacheClock>(composer, "CacheClock").IsEffectivelyVisible);
        Assert.False(Part<RemoteControlChip>(composer, "RemoteControlChip").IsEffectivelyVisible);
        Assert.False(Part<AgentsChip>(composer, "AgentsChip").IsEffectivelyVisible);
        Assert.False(Part<ComboBox>(composer, "ModePicker").IsEffectivelyVisible);
        Assert.False(Part<ModelChip>(composer, "ModelChip").IsEffectivelyVisible);
        Assert.True(Part<Border>(composer, "TouchBar").IsEffectivelyVisible);
        Assert.False(Part<Border>(composer, "DesktopBar").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void The_touch_parts_are_in_the_tree_only_while_the_layout_is_touch()
    {
        TestLog.Instance.Clear();
        var view = new ClaudeChatView { Conversation = UiHelpers.Recording("subagent", "stream") };
        UiHelpers.Show(view);
        var composer = UiHelpers.Composer(view);
        int Count<T>(Visual root) where T : Control => root.GetVisualDescendants().OfType<T>().Count();
        void AssertDesktopTree()
        {
            Assert.Equal(1, Count<ContextRing>(view));
            Assert.Equal(1, Count<ModelChip>(view));
            Assert.Equal(1, Count<AgentsChip>(view));                     // the bar's, none at the end of the list
            Assert.Equal(1, composer.GetVisualDescendants().OfType<Button>().Count(b => b.Classes.Contains("ClaudeChatSend")));
            Assert.Equal(1, composer.GetVisualDescendants().OfType<Button>().Count(b => b.Classes.Contains("ClaudeChatStop")));
            Assert.Null(Part<Border>(composer, "TouchBar").Child);
            Assert.False(Part<Border>(composer, "TouchBar").IsVisible);
            Assert.True(Part<Border>(composer, "DesktopBar").IsEffectivelyVisible);
        }
        AssertDesktopTree();

        view.Layout = ChatLayout.Touch;
        UiHelpers.Pump();
        Assert.Equal(2, Count<ContextRing>(view));
        Assert.Equal(2, Count<ModelChip>(view));
        Assert.Equal(2, Count<AgentsChip>(view));                         // the bar's (out of sight) and the list's
        Assert.NotNull(Part<Border>(composer, "TouchBar").Child);

        view.Layout = ChatLayout.Desktop;
        UiHelpers.Pump();
        AssertDesktopTree();

        view.Layout = ChatLayout.Touch;                                   // and again: the same parts, not new ones
        UiHelpers.Pump();
        Assert.Equal(2, Count<ContextRing>(view));
        Assert.Equal(2, Count<AgentsChip>(view));
        Assert.Empty(TestLog.Instance.Entries);
    }

    [AvaloniaFact]
    public void Back_to_desktop_the_box_the_text_box_and_the_chip_are_as_before()
    {
        var c = TouchSamples.Opus();
        c.Apply(new AgentTaskStarted("t1", "toolu_1", "Look around", "Explore", true, 1, "local_agent"));
        var view = UiHelpers.TouchView(c);
        UiHelpers.ShowPhone(view);
        view.Layout = ChatLayout.Desktop;
        UiHelpers.Pump();
        var composer = UiHelpers.Composer(view);
        var box = Box(composer);
        Assert.Equal(new CornerRadius(8), box.CornerRadius);
        Assert.Equal(new Thickness(0), box.Padding);
        Assert.Same(box.FindResource("ClaudeChat.InputBorder"), box.BorderBrush);
        var input = UiHelpers.Input(view);
        Assert.Equal(new Thickness(14, 10, 36, 10), input.Padding);
        Assert.Equal(new Thickness(0), input.Margin);
        Assert.Equal(39.5, input.MinHeight);
        Assert.Equal(200, input.MaxHeight);
        Assert.True(double.IsNaN(input.LineHeight));
        Assert.Equal("Responder ao Claude…", input.Watermark);
        Assert.Equal(new[] { "1 agente" }, UiHelpers.Texts(Part<AgentsChip>(composer, "AgentsChip")));
        Assert.Contains("Mostrar o menu de comandos (/)", UiHelpers.ButtonNames(composer));
    }

    [AvaloniaFact]
    public void Touch_add_and_model_ask_the_view_for_their_sheets()
    {
        var c = TouchSamples.Opus();
        var view = UiHelpers.TouchView(c);
        var window = UiHelpers.ShowPhone(view);
        var composer = UiHelpers.Composer(view);
        var asked = new List<string>();
        composer.AddSheetRequested += (_, _) => asked.Add("add");
        composer.ModelSheetRequested += (_, _) => asked.Add("model");
        var picked = 0;
        composer.PickFilesOverride = () => { picked++; return System.Threading.Tasks.Task.FromResult<IReadOnlyList<string>>(new string[0]); };

        UiHelpers.Click(window, UiHelpers.ButtonWithText(composer, "Adicionar"));
        view.GetVisualDescendants().OfType<AddSheet>().Single().Close();  // the + opens its sheet, which covers the bar: the pill is tapped from the bar as it was
        UiHelpers.Click(window, UiHelpers.ButtonWithText(composer, "Opus 5.5 Alto"));
        Assert.Equal(new[] { "add", "model" }, asked);
        Assert.Equal(0, picked);                                          // the desktop + opens a file dialog, this one a sheet
        Assert.False(composer.HasOpenPopup);                              // and the model chip's menu stays closed
    }

    [AvaloniaFact]
    public void Touch_buttons_take_a_tap_on_the_whole_of_their_target()
    {
        var c = TouchSamples.Opus();
        var view = UiHelpers.TouchView(c);
        var window = UiHelpers.ShowPhone(view);
        var composer = UiHelpers.Composer(view);
        var taps = 0;
        composer.AddSheetRequested += (_, _) => taps++;
        composer.ModelSheetRequested += (_, _) => taps++;
        foreach (var button in new Button[] { Part<Button>(composer, "TouchAddButton"), Part<ModelChip>(composer, "TouchModelChip") })
        {
            var (w, h) = (button.Bounds.Width, button.Bounds.Height);
            // the corners of the target (4 px in: the face is a circle or a pill, the target is not) and the middle of each side
            foreach (var (x, y) in new[] { (4.0, 4.0), (w - 4, 4.0), (4.0, h - 4), (w - 4, h - 4), (w / 2, 1.0), (1.0, h / 2), (w - 1, h / 2), (w / 2, h - 1) })
            {
                var before = taps;
                var at = button.TranslatePoint(new Point(x, y), window)!.Value;
                window.MouseMove(at, RawInputModifiers.None);
                window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
                window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
                UiHelpers.Pump();
                Assert.True(taps == before + 1, button.Name + ": a tap " + x + ", " + y + " in the button");
                foreach (var sheet in view.GetVisualDescendants().OfType<ChatSheet>().ToList()) sheet.Close(); // the + and the pill open their sheets, which cover the bar: each tap starts from the bar as it was
            }
        }
    }

    [AvaloniaFact]
    public void Touch_send_and_stop_follow_the_rules_of_the_desktop_bar()
    {
        var c = TouchSamples.Opus();
        var sent = new List<string>();
        var stops = 0;
        c.MessageSubmitted += (_, e) => sent.Add(e.Text);
        c.StopRequested += (_, _) => stops++;
        var view = UiHelpers.TouchView(c);
        var window = UiHelpers.ShowPhone(view);
        var composer = UiHelpers.Composer(view);
        var (send, stop, input) = (Part<Button>(composer, "TouchSend"), Part<Button>(composer, "TouchStop"), UiHelpers.Input(view));

        Assert.True(send.IsEffectivelyVisible);                           // idle, nothing written: send, not yet enabled
        Assert.False(send.IsEnabled);
        Assert.False(stop.IsEffectivelyVisible);
        input.Text = "hello";
        UiHelpers.Pump();
        Assert.True(send.IsEnabled);
        UiHelpers.Click(window, send);                                    // sends and keeps the keyboard in the box
        Assert.Equal(new[] { "hello" }, sent);
        Assert.Equal("", input.Text);
        Assert.Same(input, UiHelpers.Focused(window));

        Assert.True(c.IsBusy);                                            // Claude works: stop alone while nothing is written
        Assert.True(stop.IsEffectivelyVisible);
        Assert.False(send.IsEffectivelyVisible);
        input.Text = "and this";
        UiHelpers.Pump();
        Assert.True(stop.IsEffectivelyVisible);                           // with something written, send waits in line next to it
        Assert.True(send.IsEffectivelyVisible && send.IsEnabled);
        input.Text = "";
        UiHelpers.Pump();
        UiHelpers.Click(window, stop);
        Assert.Equal(1, stops);

        c.Apply(new TurnCompleted("success", false, 1000, 0.01, 10, 10));
        UiHelpers.Pump();
        Assert.False(stop.IsEffectivelyVisible);
        Assert.True(send.IsEffectivelyVisible);
        Assert.False(send.IsEnabled);
    }

    [AvaloniaFact]
    public void Touch_names_follow_the_texts()
    {
        var view = UiHelpers.TouchView(TouchSamples.Opus());
        UiHelpers.ShowPhone(view);
        var composer = UiHelpers.Composer(view);
        Assert.Equal("Enviar", AutomationProperties.GetName(Part<Button>(composer, "TouchSend")));
        Assert.Equal("Parar", AutomationProperties.GetName(Part<Button>(composer, "TouchStop")));
        view.Strings = ChatStrings.English;
        UiHelpers.Pump();
        Assert.Equal("Send", AutomationProperties.GetName(Part<Button>(composer, "TouchSend")));
        Assert.Equal("Stop", AutomationProperties.GetName(Part<Button>(composer, "TouchStop")));
        Assert.Equal("Add", AutomationProperties.GetName(Part<Button>(composer, "TouchAddButton")));
        Assert.Equal("Opus 5.5 High", AutomationProperties.GetName(Part<ModelChip>(composer, "TouchModelChip")));
        Assert.Equal("Type / for commands", UiHelpers.Input(view).Watermark);
    }

    [AvaloniaFact]
    public void Touch_slash_typed_in_the_empty_box_opens_the_palette()
    {
        var c = TouchSamples.Opus();
        c.Commands = new[] { new ChatCommand("compact", "Free up context", "") };
        var view = UiHelpers.TouchView(c);
        var window = UiHelpers.ShowPhone(view);
        UiHelpers.Input(view).Focus();
        UiHelpers.Type(window, "/");
        Assert.True(UiHelpers.Composer(view).HasOpenPopup);
    }

    [AvaloniaFact]
    public void Touch_ring_shows_from_the_first_token_and_is_drawn_larger_while_the_desktop_one_waits_for_half()
    {
        var c = TouchSamples.Opus();
        c.Apply(new ContextUsageReported(new ContextUsage(10_000, 1_000_000, 1, System.Array.Empty<ContextCategory>())));
        var opened = new List<string>();
        c.CommandInvoked += (_, e) => opened.Add(e.Name);

        var desktop = new ClaudeChatView { Conversation = c };
        UiHelpers.Show(desktop);
        var desktopRing = UiHelpers.Composer(desktop).GetVisualDescendants().OfType<ContextRing>().Single();
        Assert.Equal(ContextRing.ShownAbove, desktopRing.MinimumPercent);
        Assert.False(desktopRing.IsVisible);                              // 1 % used
        var desktopArc = Assert.IsType<ContextRingArc>(desktopRing.Content);   // hidden: its template is not built
        Assert.Equal((5.0, 1.5), (desktopArc.Radius, desktopArc.StrokeThickness));

        var view = UiHelpers.TouchView(c);
        var window = UiHelpers.ShowPhone(view);
        var ring = Part<ContextRing>(UiHelpers.Composer(view), "TouchRing");
        Assert.Equal(0, ring.MinimumPercent);
        Assert.True(ring.IsEffectivelyVisible);
        Assert.StartsWith("99% do contexto restante", AutomationProperties.GetName(ring));
        var arc = ring.GetVisualDescendants().OfType<ContextRingArc>().Single();
        Assert.Equal((9.0, 2.5), (arc.Radius, arc.StrokeThickness));
        Assert.Equal(new Size(44, 44), ring.Bounds.Size);

        UiHelpers.Click(window, ring);                                    // the click of today: the Context panel
        Assert.Equal(new[] { "context" }, opened);
        Assert.Equal(ChatPanel.Context, c.ShownPanel);
    }

    [AvaloniaFact]
    public void Touch_ring_draws_the_share_used_and_the_desktop_ring_keeps_the_extensions_steps()
    {
        static ContextUsageReported Used(long tokens) =>
            new ContextUsageReported(new ContextUsage(tokens, 1_000_000, (int)(tokens / 10_000), System.Array.Empty<ContextCategory>()));
        var c = TouchSamples.Opus();
        c.Apply(Used(150_000));

        var view = UiHelpers.TouchView(c);
        UiHelpers.ShowPhone(view);
        var arc = Part<ContextRing>(UiHelpers.Composer(view), "TouchRing").GetVisualDescendants().OfType<ContextRingArc>().Single();
        Assert.Equal(15, arc.Percent);                                    // 15 % used is 15 % of the circle, not the first step's half
        c.Apply(Used(10_000));
        UiHelpers.Pump();
        Assert.Equal(1, arc.Percent);
        c.Apply(Used(700_000));
        UiHelpers.Pump();
        Assert.Equal(70, arc.Percent);

        var desktop = new ClaudeChatView { Conversation = c };            // the desktop's ring, on the same conversation: the step of today
        UiHelpers.Show(desktop);
        var desktopRing = UiHelpers.Composer(desktop).GetVisualDescendants().OfType<ContextRing>().Single();
        Assert.Equal(75, desktopRing.GetVisualDescendants().OfType<ContextRingArc>().Single().Percent);
    }

    [AvaloniaFact]
    public void Touch_buttons_draw_a_38_px_face_inside_the_44_px_target_in_the_apps_colors()
    {
        var c = TouchSamples.Opus();
        c.Apply(new ContextUsageReported(TouchSamples.Context));
        var view = UiHelpers.TouchView(c);
        UiHelpers.ShowPhone(view);
        var composer = UiHelpers.Composer(view);
        Border Face(Button b) => b.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "PART_TouchFace");

        foreach (var name in new[] { "TouchAddButton", "TouchRing", "TouchSend" })
        {
            var button = Part<Button>(composer, name);
            Assert.Equal(new Size(44, 44), button.Bounds.Size);
            Assert.Equal(new Rect(3, 3, 38, 38), Face(button).Bounds);
            Assert.Equal(new CornerRadius(19), Face(button).CornerRadius);
        }
        Assert.Same(view.FindResource("ClaudeChat.SheetGroupBackground"), Face(Part<Button>(composer, "TouchAddButton")).Background);
        Assert.Same(view.FindResource("ClaudeChat.SendBackground"), Face(Part<Button>(composer, "TouchSend")).Background);
        Assert.Same(view.FindResource("ClaudeChat.SendForeground"), Part<Button>(composer, "TouchSend").Foreground);

        var chip = Part<ModelChip>(composer, "TouchModelChip");            // the pill: 38 high, 14 px at the sides, the level muted
        var pill = Face(chip);
        Assert.Equal(44, chip.Bounds.Height);
        Assert.Equal(38, pill.Bounds.Height);
        Assert.Equal(new Thickness(14, 0), pill.Padding);
        Assert.Same(view.FindResource("ClaudeChat.SheetGroupBackground"), pill.Background);
        var level = chip.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Alto");
        Assert.Same(view.FindResource("ClaudeChat.ForegroundMuted"), level.Foreground);
        Assert.Equal(pill.Bounds.Width, chip.Bounds.Width);               // as wide as its text and padding, left at the bar's left
    }

    [AvaloniaFact]
    public void Touch_bar_fits_the_phone_with_everything_on()
    {
        var c = TouchSamples.Opus();
        c.Apply(new SessionStarted(null, "claude-fable-5-1", null, null, null));   // a longer name, and a longer level
        c.Apply(new SettingsApplied(null, "xhigh", true));
        c.Apply(new ContextUsageReported(TouchSamples.Context));
        c.Submit("x");                                                    // busy, with something written: ring, send and stop
        var view = UiHelpers.TouchView(c);
        UiHelpers.ShowPhone(view);
        UiHelpers.Input(view).Text = "and this";
        UiHelpers.Pump();
        var composer = UiHelpers.Composer(view);
        var bar = Part<Border>(composer, "TouchBar");
        var buttons = bar.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).ToList();
        Assert.Equal(new[] { "TouchAddButton", "TouchModelChip", "TouchRing", "TouchSend", "TouchStop" }, buttons.Select(b => b.Name));
        Assert.True(buttons.Last().TranslatePoint(new Point(buttons.Last().Bounds.Width, 0), bar)!.Value.X <= bar.Bounds.Width + 0.5);
        for (var i = 1; i < buttons.Count; i++)                           // none on another
        {
            var before = buttons[i - 1].TranslatePoint(new Point(buttons[i - 1].Bounds.Width, 0), bar)!.Value.X;
            Assert.True(before <= buttons[i].TranslatePoint(default, bar)!.Value.X + 0.5, buttons[i].Name);
        }
    }

    // ---- the model pill when the bar is full: its texts end in an ellipsis, they are not cut ----

    /// <summary>A dictation that is always there, so that the microphone shows.</summary>
    private sealed class AlwaysThere : IChatDictation
    {
        public bool IsAvailable => true;

        public event EventHandler? AvailabilityChanged { add { } remove { } }

        public System.Threading.Tasks.Task<string?> ListenAsync(IProgress<string>? partial, System.Threading.CancellationToken stop) =>
            System.Threading.Tasks.Task.FromResult<string?>(null);
    }

    /// <summary>
    /// A bar of a phone 360 DIPs wide with all it can hold: the +, the ring, the microphone and send take four targets of 44 of the
    /// bar's 304, and the pill has the 128 that are left (100 for its text, with its padding of 14).
    /// </summary>
    private static (Window Window, ChatComposer Composer, ModelChip Pill) OpenFullBar(ChatConversation c, double width = 360)
    {
        c.Apply(new ContextUsageReported(TouchSamples.Context));
        var view = UiHelpers.TouchView(c);
        view.Dictation = new AlwaysThere();
        var window = UiHelpers.Show(view, width, UiHelpers.PhoneHeight);
        var composer = UiHelpers.Composer(view);
        return (window, composer, Part<ModelChip>(composer, "TouchModelChip"));
    }

    private static Border Face(Button button) =>
        button.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_TouchFace");

    private static TextBlock TextOf(ModelChip pill, string text) =>
        pill.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == text);

    /// <summary>The text was cut by an ellipsis, as the line it was laid out in says.</summary>
    private static bool EndsInEllipsis(TextBlock text) => text.TextLayout.TextLines.Last().HasCollapsed;

    /// <summary>
    /// What the pill shows stays inside it: no text goes past the padding at the right of its face, where the face would cut it. (A text
    /// with no room is a width of 0, which draws nothing, wherever its margin leaves it.)
    /// </summary>
    private static void AssertTextsInsideThePill(ModelChip pill)
    {
        var face = Face(pill);
        var inside = face.Bounds.Width - face.Padding.Right;
        foreach (var text in pill.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && t.Bounds.Width >= 1))
        {
            var right = text.TranslatePoint(new Point(text.Bounds.Width, 0), face)!.Value.X;
            Assert.True(right <= inside + 0.5, "'" + text.Text + "' ends at " + right + " of the " + inside + " the pill has");
        }
    }

    [AvaloniaFact]
    public void Touch_pill_ends_its_level_in_an_ellipsis_when_the_bar_is_full_and_stays_inside_the_bar()
    {
        var c = TouchSamples.Opus();
        c.Apply(new SessionStarted(null, "sonnet", null, null, null));    // "Sonnet 5.5", and its level
        c.Apply(new SettingsApplied(null, "xhigh", true));                // "Muito alto"
        var (window, composer, pill) = OpenFullBar(c);
        Assert.Contains("Ditar", UiHelpers.ButtonNames(composer));        // the microphone is on the bar
        Assert.Contains(UiHelpers.ButtonNames(composer), n => n.StartsWith("85% do contexto restante"));   // and so is the ring

        var bar = Part<Border>(composer, "TouchBar");
        var ring = Part<ContextRing>(composer, "TouchRing");
        Assert.True(pill.TranslatePoint(new Point(pill.Bounds.Width, 0), bar)!.Value.X <= ring.TranslatePoint(default, bar)!.Value.X + 0.5);
        Assert.True(pill.Bounds.Width <= bar.Bounds.Width - 4 * 44 + 0.5);   // the pill is inside the room the other targets leave it
        Assert.Equal(44, pill.Bounds.Height);                             // and it is a target as ever

        AssertTextsInsideThePill(pill);                                   // nothing is cut by the face…
        var name = TextOf(pill, "Sonnet 5.5");
        var level = TextOf(pill, "Muito alto");
        Assert.False(EndsInEllipsis(name));                               // …the name is whole
        Assert.True(EndsInEllipsis(level) || level.Bounds.Width < 1);     // and the level ends in an ellipsis, or has no room even for one
        window.Close();
    }

    [AvaloniaFact]
    public void Touch_pill_ends_a_long_name_in_an_ellipsis_and_leaves_its_level_out()
    {
        var c = TouchSamples.Opus();
        c.AvailableModels = new[]
        {
            new ChatModel("long", "claude-long", "Sonnet 5.5 with an unusually long display name", "", new[] { "low", "xhigh" }),
        };
        c.Apply(new SessionStarted(null, "long", null, null, null));
        c.Apply(new SettingsApplied(null, "xhigh", true));
        var (window, composer, pill) = OpenFullBar(c);
        Assert.Contains("Ditar", UiHelpers.ButtonNames(composer));

        var name = TextOf(pill, "Sonnet 5.5 with an unusually long display name");
        var level = TextOf(pill, "Muito alto");
        Assert.True(EndsInEllipsis(name));                                // the name is cut by an ellipsis, in whatever font the text is drawn
        Assert.True(level.Bounds.Width < 1);                              // nothing is left for the level, and the one ellipsis is the name's
        AssertTextsInsideThePill(pill);
        var bar = Part<Border>(composer, "TouchBar");
        var ring = Part<ContextRing>(composer, "TouchRing");
        Assert.True(pill.TranslatePoint(new Point(pill.Bounds.Width, 0), bar)!.Value.X <= ring.TranslatePoint(default, bar)!.Value.X + 0.5);
        Assert.Equal("Sonnet 5.5 with an unusually long display name Muito alto", AutomationProperties.GetName(pill));   // what a screen reader tells is not cut
        window.Close();
    }

    [AvaloniaFact]
    public void Touch_pill_cuts_nothing_when_its_texts_fit_and_the_desktop_chip_does_not_trim_at_all()
    {
        var c = TouchSamples.Opus();
        var view = UiHelpers.TouchView(c);
        UiHelpers.ShowPhone(view);                                        // 412 DIPs, no microphone: room for "Opus 5.5 Alto"
        var pill = Part<ModelChip>(UiHelpers.Composer(view), "TouchModelChip");
        Assert.All(new[] { TextOf(pill, "Opus 5.5"), TextOf(pill, "Alto") }, t => Assert.False(EndsInEllipsis(t)));
        AssertTextsInsideThePill(pill);

        var desktop = new ClaudeChatView { Conversation = TouchSamples.Opus() };
        UiHelpers.Show(desktop);
        var chip = UiHelpers.Composer(desktop).GetVisualDescendants().OfType<ModelChip>().Single();
        Assert.IsType<StackPanel>(chip.Content);                          // the desktop chip is as it was: as wide as its text
        Assert.All(chip.GetVisualDescendants().OfType<TextBlock>(), t => Assert.Equal(Avalonia.Media.TextTrimming.None, t.TextTrimming));
    }

    [AvaloniaFact]
    public void Touch_agents_chip_says_how_many_are_working_or_how_many_there_were_and_opens_the_map()
    {
        var c = new ChatConversation();
        c.Apply(new AgentTaskStarted("t1", "toolu_1", "Look around", "Explore", true, 1, "local_agent"));
        c.Apply(new AgentTaskStarted("t2", "toolu_2", "Look again", "Explore", true, 1, "local_agent"));
        var view = UiHelpers.TouchView(c);
        var window = UiHelpers.ShowPhone(view);
        var chip = view.GetVisualDescendants().OfType<AgentsChip>().Single(a => a.IsEffectivelyVisible);
        Assert.Equal(new[] { "Agentes · 2 rodando" }, UiHelpers.Texts(chip));
        Assert.Equal(HorizontalAlignment.Left, chip.HorizontalAlignment);
        Assert.Equal(44, chip.Bounds.Height);                             // a finger's height
        var face = chip.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_TouchFace");
        Assert.Equal(38, face.Bounds.Height);                             // an outlined pill: the frame, no fill
        Assert.Equal(new Thickness(1), face.BorderThickness);
        Assert.Same(chip.FindResource("ClaudeChat.BorderStrong"), face.BorderBrush);
        Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(face.Background).Color);

        c.Apply(new AgentTaskFinished("t1", "toolu_1", "completed", "done", null, null, null));
        UiHelpers.Pump();
        Assert.Equal(new[] { "Agentes · 1 rodando" }, UiHelpers.Texts(chip));
        c.Apply(new AgentTaskFinished("t2", "toolu_2", "completed", "done", null, null, null));
        UiHelpers.Pump();
        Assert.Equal(new[] { "Agentes · 2" }, UiHelpers.Texts(chip));      // none working: all of them

        view.Strings = ChatStrings.English;
        UiHelpers.Pump();
        Assert.Equal(new[] { "Agents · 2" }, UiHelpers.Texts(chip));

        UiHelpers.Click(window, chip);
        Assert.Equal(ChatPanel.Agents, c.ShownPanel);
    }

    [AvaloniaFact]
    public void Touch_agents_chip_waits_for_the_first_agent_and_follows_the_working_line()
    {
        var c = new ChatConversation();
        c.Submit("go");                                                   // busy: "Working…" at the end of the list
        var view = UiHelpers.TouchView(c);
        UiHelpers.ShowPhone(view);
        Assert.DoesNotContain(view.GetVisualDescendants().OfType<AgentsChip>(), a => a.IsEffectivelyVisible);

        c.Apply(new AgentTaskStarted("t1", "toolu_1", "Look around", "Explore", true, 1, "local_agent"));
        UiHelpers.Pump();
        var chip = view.GetVisualDescendants().OfType<AgentsChip>().Single(a => a.IsEffectivelyVisible);
        var working = view.GetVisualDescendants().OfType<Panel>().Single(p => p.Classes.Contains("ClaudeChatWorking"));
        Assert.True(working.IsEffectivelyVisible);
        Assert.True(chip.TranslatePoint(default, view)!.Value.Y >= working.TranslatePoint(new Point(0, working.Bounds.Height), view)!.Value.Y);
    }

    [AvaloniaFact]
    public void The_bubbles_width_binding_starts_clean_after_a_collection_while_the_layout_was_desktop()
    {
        // The bubble takes its share of the list's width through a binding that stops with the touch style. Through
        // Bounds.Width it held the boxed Rect only weakly, so a collection in between made it start with a "Null value"
        // error (what made the layout-switching test above fail now and then, more as the box changed height on a switch).
        var c = new ChatConversation();
        c.Submit("q");
        var view = UiHelpers.TouchView(c);
        UiHelpers.ShowPhone(view);
        TestLog.Instance.Clear();
        view.Layout = ChatLayout.Desktop;
        UiHelpers.Pump();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        view.Layout = ChatLayout.Touch;
        UiHelpers.Pump();
        Assert.Empty(TestLog.Instance.Entries);
        var bubble = view.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatUser"));
        Assert.Equal(Math.Floor(UiHelpers.ItemList(view).Bounds.Width * ChatConverters.BubbleShare), bubble.MaxWidth);
    }

    [AvaloniaFact]
    public void Touch_list_keeps_its_end_while_the_keyboard_comes_and_goes_and_the_box_grows()
    {
        var c = new ChatConversation();
        for (var i = 0; i < 30; i++) c.Submit("message " + i);
        var view = UiHelpers.TouchView(c);
        var window = UiHelpers.ShowPhone(view);
        UiHelpers.Input(view).Text = string.Join("\n", Enumerable.Range(1, 6).Select(i => "line " + i));   // the box takes six lines
        UiHelpers.PumpUntil(() => UiHelpers.AtEnd(UiHelpers.Scroller(view)));
        window.Height = 520;                                              // the keyboard comes up
        UiHelpers.PumpUntil(() => UiHelpers.AtEnd(UiHelpers.Scroller(view)));
        window.Height = UiHelpers.PhoneHeight;                            // and goes
        UiHelpers.PumpUntil(() => UiHelpers.AtEnd(UiHelpers.Scroller(view)));
        UiHelpers.Input(view).Text = "";                                  // and the box is one line again
        UiHelpers.PumpUntil(() => UiHelpers.AtEnd(UiHelpers.Scroller(view)));
    }

    // ---- what the "+" sheet makes reachable in touch: a finger's size (44 px) ----

    private static List<Border> PaletteRows(CommandPalette palette) =>
        palette.Child!.GetVisualDescendants().OfType<Border>()
            .Where(b => b.IsEffectivelyVisible && b.Classes.Contains("ClaudeChatMenuItem")).ToList();

    /// <summary>The filter field the palette has when the / button or "Commands" opens it.</summary>
    private static TextBox PaletteFilter(CommandPalette palette) =>
        palette.Child!.GetVisualDescendants().OfType<TextBox>().Single(t => t.IsEffectivelyVisible);

    [AvaloniaFact]
    public void Touch_palette_rows_are_a_target_of_44_and_the_desktops_stay_as_they_were()
    {
        var c = TouchSamples.Opus();
        c.Commands = new[] { new ChatCommand("compact", "Free up context", ""), new ChatCommand("review", "Review a change", "") };
        var view = UiHelpers.TouchView(c);
        var window = UiHelpers.ShowPhone(view);
        var composer = UiHelpers.Composer(view);
        var palette = composer.GetLogicalDescendants().OfType<CommandPalette>().Single();

        composer.OpenCommands();                                          // opened as the "+" sheet's Commands row opens it…
        UiHelpers.Pump();
        var rows = PaletteRows(palette);
        Assert.Equal(c.PaletteCommands.Count, rows.Count);
        Assert.All(rows, r => Assert.True(r.Bounds.Height >= 44, r.Bounds.Height.ToString()));
        Assert.True(PaletteFilter(palette).Bounds.Height >= 44);          // with its filter field, a finger's height too
        composer.ClosePopups();

        UiHelpers.Input(view).Focus();
        UiHelpers.Type(window, "/");                                      // …and by a "/" typed in the empty box
        rows = PaletteRows(palette);
        Assert.Equal(c.PaletteCommands.Count, rows.Count);
        Assert.All(rows, r => Assert.True(r.Bounds.Height >= 44, r.Bounds.Height.ToString()));
        UiHelpers.Type(window, "rev");                                    // filtered: the rows left are as tall
        rows = PaletteRows(palette);
        Assert.Equal(new[] { "/review" }, rows.Select(r => UiHelpers.Texts(r).Single()));
        Assert.All(rows, r => Assert.True(r.Bounds.Height >= 44, r.Bounds.Height.ToString()));
        composer.ClosePopups();

        // the desktop layout's rows are as they were: 4 px over and under the text
        var desktop = new ClaudeChatView { Conversation = c };
        UiHelpers.Show(desktop);
        var desktopComposer = UiHelpers.Composer(desktop);
        desktopComposer.OpenCommands();
        UiHelpers.Pump();
        var desktopPalette = desktopComposer.GetLogicalDescendants().OfType<CommandPalette>().Single();
        var desktopRows = PaletteRows(desktopPalette);
        Assert.Equal(c.PaletteCommands.Count, desktopRows.Count);
        Assert.All(desktopRows, r => Assert.True(r.Bounds.Height < 44, r.Bounds.Height.ToString()));
        Assert.True(PaletteFilter(desktopPalette).Bounds.Height < 44);
    }

    [AvaloniaFact]
    public void Touch_attachment_remove_is_a_target_of_44_that_is_always_there_and_the_desktops_stay_as_they_were()
    {
        var c = TouchSamples.Opus();
        var view = UiHelpers.TouchView(c);
        var window = UiHelpers.ShowPhone(view);
        _ = c.AddImageAsync(UiHelpers.Png(40, 20));
        UiHelpers.PumpUntil(() => c.Attachments.Count == 1);
        Assert.Equal(AddFileResult.Added, c.AddFile("notes.txt", System.Text.Encoding.UTF8.GetBytes("hello")));
        UiHelpers.Pump();
        var strip = UiHelpers.Composer(view).GetVisualDescendants().OfType<AttachmentStrip>().Single();
        List<Button> Pills() => strip.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("ClaudeChatAttachment")).ToList();
        Button RemoveOf(Button pill) => pill.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("ClaudeChatAttachmentRemove"));

        // an image's pill and a file's chip: the × is 44 by 44 inside the pill, there to be seen (no pointer hovers on a screen)
        var pills = Pills();
        Assert.Equal(2, pills.Count);
        foreach (var pill in pills)
        {
            var remove = RemoveOf(pill);
            Assert.True(remove.IsEffectivelyVisible);
            Assert.Equal(1, remove.Opacity);
            Assert.True(remove.Bounds.Width >= 44 && remove.Bounds.Height >= 44, remove.Bounds.ToString());
            Assert.True(remove.Bounds.Height >= 46, remove.Bounds.ToString());   // with room to spare: on a phone the layout rounds to the device's pixels (a density of 2.625), which can take 44 down to 43.8
            Assert.True(pill.Bounds.Height >= remove.Bounds.Height);
            var corner = remove.TranslatePoint(new Point(remove.Bounds.Width, remove.Bounds.Height), pill)!.Value;
            Assert.True(corner.X <= pill.Bounds.Width + 0.5 && corner.Y <= pill.Bounds.Height + 0.5);  // all of the target is in the pill
            var name = pill.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("ClaudeChatAttachmentName"));
            var nameRight = name.TranslatePoint(new Point(name.Bounds.Width, 0), pill)!.Value.X;
            Assert.True(nameRight <= remove.TranslatePoint(default, pill)!.Value.X + 0.5);              // the text stops where the target starts
        }

        // the desktop layout's: a pill of 24 px, a × of 32 px that fades in with the pointer
        view.Layout = ChatLayout.Desktop;
        UiHelpers.Pump();
        foreach (var pill in Pills())
        {
            Assert.Equal(24, pill.Bounds.Height);
            Assert.Equal(32, RemoveOf(pill).Bounds.Width);
            Assert.Equal(0, RemoveOf(pill).Opacity);
        }
        view.Layout = ChatLayout.Touch;
        UiHelpers.Pump();

        // the whole of the target takes the tap, its corners too
        void TapAt(Control control, double x, double y)
        {
            var at = control.TranslatePoint(new Point(x, y), window)!.Value;
            window.MouseMove(at, RawInputModifiers.None);
            window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
            UiHelpers.Pump();
        }
        var image = RemoveOf(Pills()[0]);
        TapAt(image, 3, 3);
        Assert.Empty(c.Attachments);
        Assert.Single(c.Files);                                           // the file's chip is still there
        var file = RemoveOf(Pills().Single());
        TapAt(file, file.Bounds.Width - 3, file.Bounds.Height - 3);
        Assert.Empty(c.Files);
        Assert.Empty(Pills());
    }
}
