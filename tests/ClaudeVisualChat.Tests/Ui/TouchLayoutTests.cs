using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using Xunit;

namespace ClaudeVisualChat.Tests.Ui;

/// <summary>The touch layout (<see cref="ChatLayout.Touch"/>): the phone's sizes, the message list in the style of the app.</summary>
public class TouchLayoutTests
{
    [AvaloniaFact]
    public void Layout_is_desktop_by_default()
    {
        var view = new ClaudeChatView();
        UiHelpers.Show(view);
        Assert.Equal(ChatLayout.Desktop, view.Layout);
        Assert.DoesNotContain(":touch", view.Classes);
        Assert.Equal(13.0, view.FindResource("ClaudeChat.FontSize"));
    }

    [AvaloniaFact]
    public void Touch_uses_the_phone_sizes()
    {
        var view = UiHelpers.TouchView(new ChatConversation());
        view.Resources = new ResourceDictionary();   // the host swaps the whole dictionary, as XAML does after the attributes
        UiHelpers.ShowPhone(view);
        Assert.Contains(":touch", view.Classes);
        Assert.Equal(15.0, view.FindResource("ClaudeChat.FontSize"));
        Assert.Equal(new Thickness(16, 12, 16, 16), view.FindResource("ClaudeChat.ListPadding"));
        view.Layout = ChatLayout.Desktop;          // back to the desktop: the usual sizes
        UiHelpers.Pump();
        Assert.Equal(13.0, view.FindResource("ClaudeChat.FontSize"));
    }

    [AvaloniaFact]
    public void Touch_user_message_is_a_bubble_at_the_right()
    {
        var c = new ChatConversation();
        c.Submit("Run the tests and tell me what failed");
        var view = UiHelpers.TouchView(c);
        UiHelpers.ShowPhone(view);
        var bubble = view.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatUser"));
        Assert.Equal(HorizontalAlignment.Right, bubble.HorizontalAlignment);
        Assert.True(bubble.Bounds.Width <= 0.85 * UiHelpers.ItemList(view).Bounds.Width + 0.5);
        Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Classes.Contains("ClaudeChatStamp") && t.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void Touch_answers_and_tools_have_no_timeline()
    {
        var view = UiHelpers.TouchView(UiHelpers.Recording("bash", "stream"));
        UiHelpers.ShowPhone(view);
        var all = view.GetVisualDescendants().ToList();
        Assert.DoesNotContain(all.OfType<Ellipse>(), e => e.Name == "PART_Dot" && e.IsEffectivelyVisible);
        Assert.DoesNotContain(all.OfType<Rectangle>(), r => r.Name == "PART_Line" && r.IsEffectivelyVisible);
        var block = all.OfType<ToolCallView>().First().GetVisualDescendants().OfType<Border>()
            .First(b => b.Classes.Contains("ClaudeChatToolBlock"));
        Assert.Equal(new Thickness(1), block.BorderThickness);
        Assert.Equal(new CornerRadius(12), block.CornerRadius);
    }

    [AvaloniaFact]
    public void Touch_turn_ends_with_copy_instead_of_the_footer()
    {
        string? copied = null;
        CopyTurnButton.CopyOverride = text => { copied = text; return Task.CompletedTask; };
        try
        {
            var c = UiHelpers.Recording("bash", "stream");
            var view = UiHelpers.TouchView(c);
            var window = UiHelpers.ShowPhone(view);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBlock>(),
                t => t.Classes.Contains("ClaudeChatFooter") && t.IsEffectivelyVisible);
            var copy = view.GetVisualDescendants().OfType<CopyTurnButton>().Last(b => b.IsEffectivelyVisible);
            UiHelpers.Click(window, copy);
            var footer = c.Items.ToList().FindLastIndex(i => i is TurnFooterItem);
            Assert.Equal(ChatTurns.AnswerText(c.Items, footer), copied);
            Assert.False(string.IsNullOrEmpty(copied));
        }
        finally { CopyTurnButton.CopyOverride = null; }
    }

    [AvaloniaFact]
    public void Back_to_desktop_brings_the_timeline_and_the_footer_back()
    {
        var view = UiHelpers.TouchView(UiHelpers.Recording("bash", "stream"));
        UiHelpers.ShowPhone(view);
        view.Layout = ChatLayout.Desktop;
        UiHelpers.Pump();
        Assert.Contains(view.GetVisualDescendants().OfType<Ellipse>(), e => e.Name == "PART_Dot" && e.IsEffectivelyVisible);
        Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Classes.Contains("ClaudeChatFooter") && t.IsEffectivelyVisible);
        Assert.DoesNotContain(view.GetVisualDescendants().OfType<CopyTurnButton>(), b => b.IsEffectivelyVisible);
    }

    // ---- more of the same, one behavior each ----

    private static ChatConversation Turn(params ChatItem[] items)
    {
        var c = new ChatConversation();
        foreach (var item in items) c.Items.Add(item);
        return c;
    }

    private static TurnFooterItem Footer() => new TurnFooterItem(null, null, null, null, false);

    [AvaloniaFact]
    public void Touch_bubble_wraps_a_long_message_within_85_percent_of_the_list()
    {
        var c = new ChatConversation();
        c.Submit(string.Join(" ", Enumerable.Repeat("a message that is long enough to wrap", 6)));
        var view = UiHelpers.TouchView(c);
        UiHelpers.ShowPhone(view);
        var bubble = view.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatUser"));
        var list = UiHelpers.ItemList(view).Bounds.Width;
        Assert.InRange(bubble.Bounds.Width, 0.85 * list - 90, Math.Floor(0.85 * list));  // as wide as it may (to its widest line)
        Assert.Equal(list - 16, bubble.TranslatePoint(new Point(bubble.Bounds.Width, 0), view)!.Value.X); // at the list's padding
        Assert.True(bubble.Bounds.Height > 2 * 22.5 + 18);                                // and wraps
    }

    [AvaloniaFact]
    public void Touch_sizes_are_merged_once_and_the_apps_own_keys_win()
    {
        var view = UiHelpers.TouchView(new ChatConversation());
        view.Resources["ClaudeChat.SmallFontSize"] = 14.0;                  // a key in the resources themselves
        view.Resources.MergedDictionaries.Add(new ResourceDictionary { ["ClaudeChat.CodeFontSize"] = 16.0 }); // and one merged
        var window = UiHelpers.ShowPhone(view);
        Assert.Equal(2, view.Resources.MergedDictionaries.Count);           // the app's and the touch sizes
        Assert.Equal(15.0, view.FindResource("ClaudeChat.FontSize"));
        Assert.Equal(14.0, view.FindResource("ClaudeChat.SmallFontSize"));
        Assert.Equal(16.0, view.FindResource("ClaudeChat.CodeFontSize"));

        window.Content = null;                                              // out of the window and back
        UiHelpers.Pump();
        window.Content = view;
        UiHelpers.Pump();
        view.Layout = ChatLayout.Touch;                                     // the layout it already has
        Assert.Equal(2, view.Resources.MergedDictionaries.Count);

        view.Layout = ChatLayout.Desktop;                                   // the sizes go, the app's stay
        UiHelpers.Pump();
        Assert.Single(view.Resources.MergedDictionaries);
        Assert.Equal(13.0, view.FindResource("ClaudeChat.FontSize"));
        Assert.Equal(14.0, view.FindResource("ClaudeChat.SmallFontSize"));
        Assert.Equal(16.0, view.FindResource("ClaudeChat.CodeFontSize"));
    }

    [AvaloniaFact]
    public void Touch_tool_dot_leads_the_header_in_the_color_of_the_state()
    {
        var view = UiHelpers.TouchView(Turn(
            new ToolCallItem { Name = "Read", Summary = "a.txt", Status = ToolStatus.Succeeded },
            new ToolCallItem { Name = "Write", Summary = "b.txt", Status = ToolStatus.Failed }));
        UiHelpers.ShowPhone(view);
        var dots = view.GetVisualDescendants().OfType<Ellipse>().Where(e => e.Name == "StatusDot").ToList();
        Assert.Equal(2, dots.Count);
        Assert.All(dots, d => Assert.True(d.IsEffectivelyVisible));
        Assert.All(dots, d => Assert.Equal(new Size(8, 8), d.Bounds.Size));
        Assert.Same(dots[0].FindResource("ClaudeChat.DotSucceeded"), dots[0].Fill);
        Assert.Same(dots[1].FindResource("ClaudeChat.DotFailed"), dots[1].Fill);
        var name = view.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Read");
        Assert.True(dots[0].TranslatePoint(default, view)!.Value.X + 8 <= name.TranslatePoint(default, view)!.Value.X);
    }

    [AvaloniaFact]
    public void Touch_subagent_header_is_a_finger_high_and_opens_the_block()
    {
        var agent = new ToolCallItem { Name = "Agent", Summary = "dig", Status = ToolStatus.Succeeded };
        agent.Children.Add(new ToolCallItem { Name = "Read", Summary = "b.txt", Status = ToolStatus.Succeeded });
        var view = UiHelpers.TouchView(Turn(agent));
        var window = UiHelpers.ShowPhone(view);
        var header = view.GetVisualDescendants().OfType<ToggleButton>().Single(b => b.Name == "Header" && b.IsHitTestVisible);
        Assert.True(header.Bounds.Height >= 44);
        Assert.DoesNotContain("Read", UiHelpers.Texts(view));
        UiHelpers.Click(window, header);
        Assert.True(agent.IsExpanded);
        Assert.Contains("Read", UiHelpers.Texts(view));
        UiHelpers.Click(window, header);
        Assert.False(agent.IsExpanded);
        Assert.DoesNotContain("Read", UiHelpers.Texts(view));
    }

    [AvaloniaFact]
    public void Touch_targets_are_at_least_44_px()
    {
        var agent = new ToolCallItem { Name = "Agent", Summary = "dig", Status = ToolStatus.Succeeded };
        agent.Children.Add(new ToolCallItem { Name = "Read", Summary = "b.txt", Status = ToolStatus.Succeeded });
        var view = UiHelpers.TouchView(Turn(new UserMessageItem("q"),
            new ThinkingItem { Text = "a thought", DurationMs = 2000 },
            new ThinkingItem(),                                              // nothing to open: no target
            new ToolCallItem { Name = "Read", Summary = "a.txt", Status = ToolStatus.Succeeded }, // no children: no target
            agent, new AssistantTextItem { Text = "done" }, Footer()));
        UiHelpers.ShowPhone(view);
        var targets = view.GetVisualDescendants().OfType<Button>() // a ToggleButton is a Button
            .Where(b => b.IsEffectivelyVisible && b.IsHitTestVisible && b.Focusable && b.GetVisualAncestors().OfType<ChatItemList>().Any())
            .ToList();
        Assert.Equal(3, targets.Count);                                      // the thought, the subagent's header, copy
        Assert.All(targets, t => Assert.True(t.Bounds.Width >= 44 && t.Bounds.Height >= 44, t.GetType().Name + " " + t.Bounds));
    }

    [AvaloniaFact]
    public void Copy_button_confirms_with_a_check_for_a_moment()
    {
        var copied = new List<string>();
        CopyTurnButton.CopyOverride = text => { copied.Add(text); return Task.CompletedTask; };
        try
        {
            var view = UiHelpers.TouchView(Turn(new UserMessageItem("q"), new AssistantTextItem { Text = "the answer" }, Footer()));
            var window = UiHelpers.ShowPhone(view);
            var copy = view.GetVisualDescendants().OfType<CopyTurnButton>().Single(b => b.IsEffectivelyVisible);
            var icon = copy.GetVisualDescendants().OfType<Path>().Single();
            Assert.Equal(new Size(44, 44), copy.Bounds.Size);
            Assert.Equal("Copiar", AutomationProperties.GetName(copy));
            Assert.Equal(icon.FindResource("ClaudeChat.Icon.Copy"), icon.Data);

            UiHelpers.Click(window, copy);
            Assert.Equal(new[] { "the answer" }, copied);
            Assert.Equal("Copiado", AutomationProperties.GetName(copy));
            Assert.Equal(icon.FindResource("ClaudeChat.Icon.Check"), icon.Data);

            Assert.True(copy.IsTicking);                                     // the timer takes it away
            copy.Tick();                                                     // 1.5 s later
            UiHelpers.Pump();
            Assert.False(copy.IsTicking);
            Assert.Equal("Copiar", AutomationProperties.GetName(copy));
            Assert.Equal(icon.FindResource("ClaudeChat.Icon.Copy"), icon.Data);

            view.Strings = ChatStrings.English;                              // the name follows the texts
            UiHelpers.Pump();
            Assert.Equal("Copy", AutomationProperties.GetName(copy));
        }
        finally { CopyTurnButton.CopyOverride = null; }
    }

    [AvaloniaFact]
    public void Copy_button_takes_a_tap_on_all_of_its_44_px()
    {
        var taps = 0;
        CopyTurnButton.CopyOverride = _ => { taps++; return Task.CompletedTask; };
        try
        {
            var view = UiHelpers.TouchView(Turn(new UserMessageItem("q"), new AssistantTextItem { Text = "the answer" }, Footer()));
            var window = UiHelpers.ShowPhone(view);
            var copy = view.GetVisualDescendants().OfType<CopyTurnButton>().Single(b => b.IsEffectivelyVisible);
            // the four corners (4 px in: the rounded corners cut only the last pixels) and the middle of each side
            foreach (var (x, y) in new[] { (4.0, 4.0), (40.0, 4.0), (4.0, 40.0), (40.0, 40.0), (22.0, 1.0), (1.0, 22.0), (43.0, 22.0), (22.0, 43.0) })
            {
                var before = taps;
                var at = copy.TranslatePoint(new Point(x, y), window)!.Value;
                window.MouseMove(at, RawInputModifiers.None);
                window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
                window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
                UiHelpers.Pump();
                Assert.True(taps == before + 1, "a tap " + x + ", " + y + " in the button");
            }
        }
        finally { CopyTurnButton.CopyOverride = null; }
    }

    [AvaloniaFact]
    public void A_turn_that_wrote_nothing_has_no_copy_button()
    {
        var view = UiHelpers.TouchView(Turn(new UserMessageItem("q"),
            new ToolCallItem { Name = "Read", Summary = "a.txt", Status = ToolStatus.Succeeded }, Footer()));
        UiHelpers.ShowPhone(view);
        Assert.DoesNotContain(view.GetVisualDescendants().OfType<CopyTurnButton>(), b => b.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void Touch_working_line_has_no_timeline_column_to_make_room_for()
    {
        var c = new ChatConversation();
        c.Submit("hi");
        var view = UiHelpers.TouchView(c);
        UiHelpers.ShowPhone(view);
        var dot = view.GetVisualDescendants().OfType<Ellipse>().Single(e => e.Classes.Contains("ClaudeChatWorkingDot"));
        Assert.True(dot.IsEffectivelyVisible);
        Assert.Equal(new Thickness(0), dot.Margin);
        view.Layout = ChatLayout.Desktop;
        UiHelpers.Pump();
        Assert.Equal(new Thickness(9, 0, 0, 0), dot.Margin);
    }

    [AvaloniaFact]
    public void Switching_the_layout_back_and_forth_logs_no_binding_or_property_warnings()
    {
        TestLog.Instance.Clear();
        var agent = new ToolCallItem { Name = "Agent", Summary = "dig", Status = ToolStatus.Succeeded, IsExpanded = true };
        agent.Children.Add(new ToolCallItem { Name = "Read", Summary = "b.txt", Status = ToolStatus.Succeeded });
        var view = UiHelpers.TouchView(Turn(new UserMessageItem("q"), new ThinkingItem { Text = "a thought", IsExpanded = true },
            agent, new AssistantTextItem { Text = "done" }, Footer()));
        UiHelpers.ShowPhone(view);
        view.Layout = ChatLayout.Desktop;
        UiHelpers.Pump();
        view.Layout = ChatLayout.Touch;
        UiHelpers.Pump();
        Assert.Empty(TestLog.Instance.Entries);
    }

    [AvaloniaFact]
    public void Back_to_desktop_the_user_box_and_the_tool_call_are_as_before()
    {
        var view = UiHelpers.TouchView(Turn(new UserMessageItem("q"),
            new ToolCallItem { Name = "Read", Summary = "a.txt", Status = ToolStatus.Succeeded }));
        UiHelpers.ShowPhone(view);
        view.Layout = ChatLayout.Desktop;
        UiHelpers.Pump();
        var box = view.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatUser"));
        Assert.Equal(HorizontalAlignment.Stretch, box.HorizontalAlignment);
        Assert.Equal(new Thickness(1), box.BorderThickness);
        Assert.Equal(new CornerRadius(6), box.CornerRadius);
        Assert.Equal(new Thickness(6, 4), box.Padding);
        Assert.Same(box.FindResource("ClaudeChat.InputBackground"), box.Background);
        Assert.Equal(double.PositiveInfinity, box.MaxWidth);
        var block = view.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatToolBlock"));
        Assert.Equal(new Thickness(0), block.BorderThickness);
        Assert.Equal(new Thickness(0), block.Padding);
        Assert.DoesNotContain(view.GetVisualDescendants().OfType<Ellipse>(), e => e.Name == "StatusDot" && e.IsEffectivelyVisible);
    }
}
