using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using Xunit;

namespace ClaudeVisualChat.Tests.Ui;

/// <summary>
/// In the touch layout a row scrolled off the list is kept and shows the next item of its kind, so scrolling on a phone does not build
/// the row again each time. On the desktop rows are built as they always were.
/// </summary>
public class RowReuseTests
{
    private static ChatConversation Long(int turns = 40)
    {
        var c = new ChatConversation();
        for (var i = 0; i < turns; i++)
        {
            c.Items.Add(new UserMessageItem("Question " + i));
            c.Items.Add(new ThinkingItem { Text = "thought " + i });
            c.Items.Add(new ToolCallItem
            {
                Name = "Read",
                Summary = "file" + i + ".cs",
                Status = i % 2 == 0 ? ToolStatus.Succeeded : ToolStatus.Failed,
                ToolUseId = "t" + i,
                Input = JsonDocument.Parse("{\"file_path\":\"file" + i + ".cs\"}").RootElement,
            });
            c.Items.Add(new AssistantTextItem { Text = "Answer " + i });
        }
        return c;
    }

    private static List<ContentPresenter> Containers(ClaudeChatView view) =>
        view.GetVisualDescendants().OfType<VirtualizingStackPanel>().First().Children.OfType<ContentPresenter>().ToList();

    private static List<ContentPresenter> Shown(ClaudeChatView view) =>
        Containers(view).Where(c => c.IsVisible && c.Child != null).ToList();

    /// <summary>Goes down the list and back, and counts every row control that showed.</summary>
    private static HashSet<Control> ScrollDownAndUp(ClaudeChatView view)
    {
        var scroller = UiHelpers.Scroller(view);
        var seen = new HashSet<Control>();
        void Look()
        {
            UiHelpers.Pump();
            foreach (var c in Shown(view)) seen.Add(c.Child!);
        }
        Look();
        for (var y = 0.0; y <= 3000; y += 60)
        {
            scroller.Offset = new Vector(0, y);
            Look();
        }
        for (var y = 3000.0; y >= 0; y -= 60)
        {
            scroller.Offset = new Vector(0, y);
            Look();
        }
        return seen;
    }

    private static HashSet<object> ItemsShown(ClaudeChatView view) =>
        new HashSet<object>(Shown(view).Select(c => c.Content!));

    [AvaloniaFact]
    public void Touch_reuses_the_rows_while_scrolling()
    {
        var view = UiHelpers.TouchView(Long());
        UiHelpers.ShowPhone(view);
        var built = ScrollDownAndUp(view);
        // a screen of each of the 4 kinds is enough for the whole trip down and back, past dozens of items
        Assert.InRange(built.Count, 4, 30);
    }

    [AvaloniaFact]
    public void Desktop_builds_a_row_for_each_item_that_shows()
    {
        var view = new ClaudeChatView { Conversation = Long() };
        UiHelpers.Show(view, UiHelpers.PhoneWidth, UiHelpers.PhoneHeight);
        var built = ScrollDownAndUp(view);
        Assert.True(built.Count > 30, "built " + built.Count);
    }

    [AvaloniaFact]
    public void A_reused_row_is_always_the_kind_of_its_item()
    {
        var view = UiHelpers.TouchView(Long());
        UiHelpers.ShowPhone(view);
        var scroller = UiHelpers.Scroller(view);
        for (var y = 0.0; y <= 3000; y += 60)
        {
            scroller.Offset = new Vector(0, y);
            UiHelpers.Pump();
            foreach (var c in Shown(view))
            {
                switch (c.Content)
                {
                    case ToolCallItem: Assert.IsType<ToolCallView>(c.Child); break;
                    case AssistantTextItem:
                    case ThinkingItem: Assert.IsType<ChatTimelineRow>(c.Child); break;
                    case UserMessageItem: Assert.IsType<StackPanel>(c.Child); break;
                }
            }
        }
    }

    [AvaloniaFact]
    public void A_reused_row_shows_its_new_item_and_nothing_of_the_old()
    {
        var view = UiHelpers.TouchView(Long());
        UiHelpers.ShowPhone(view);
        var scroller = UiHelpers.Scroller(view);
        for (var y = 0.0; y <= 3000; y += 120)
        {
            scroller.Offset = new Vector(0, y);
            UiHelpers.Pump();
            foreach (var c in Shown(view))
            {
                Assert.Same(c.Content, c.Child!.DataContext);
                var texts = string.Join(" | ", UiHelpers.Texts(c.Child));
                var markdown = UiHelpers.MarkdownText(c.Child);
                switch (c.Content)
                {
                    case UserMessageItem user: Assert.Contains(user.Text, texts); break;
                    case ToolCallItem tool:
                        Assert.Contains(tool.Summary, texts);
                        Assert.Equal(tool.Status == ToolStatus.Failed,
                            c.Child.GetVisualDescendants().OfType<Control>().Any(x => x.IsEffectivelyVisible && x.Classes.Contains("failed")));
                        break;
                    case AssistantTextItem answer: Assert.Equal(answer.Text, markdown); break;
                }
            }
        }
    }

    [AvaloniaFact]
    public void A_row_opened_stays_with_its_item_and_not_with_the_reused_control()
    {
        var c = Long();
        var opened = c.Items.OfType<ThinkingItem>().First();
        opened.IsExpanded = true;
        var view = UiHelpers.TouchView(c);
        UiHelpers.ShowPhone(view);
        var scroller = UiHelpers.Scroller(view);
        for (var y = 0.0; y <= 3000; y += 60)
        {
            scroller.Offset = new Vector(0, y);
            UiHelpers.Pump();
            foreach (var shown in Shown(view).Where(s => s.Content is ThinkingItem))
            {
                var item = (ThinkingItem)shown.Content!;
                var thought = shown.Child!.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("ClaudeChatThought"));
                Assert.Equal(item.IsExpanded, thought.IsVisible);
            }
        }
    }

    [AvaloniaFact]
    public void An_item_removed_leaves_the_list_and_the_rest_keep_their_rows_right()
    {
        var c = Long(10);
        var view = UiHelpers.TouchView(c);
        UiHelpers.ShowPhone(view);
        var first = c.Items[0];
        c.Items.RemoveAt(0);
        UiHelpers.Pump();
        Assert.DoesNotContain(first, ItemsShown(view));
        foreach (var s in Shown(view)) Assert.Same(s.Content, s.Child!.DataContext);
    }

    [AvaloniaFact]
    public void Leaving_the_touch_layout_does_not_show_an_old_row()
    {
        var view = UiHelpers.TouchView(Long());
        UiHelpers.ShowPhone(view);
        ScrollDownAndUp(view);
        view.Layout = ChatLayout.Desktop;
        UiHelpers.Pump();
        var scroller = UiHelpers.Scroller(view);
        scroller.Offset = new Vector(0, 1500);
        UiHelpers.Pump();
        foreach (var s in Shown(view)) Assert.Same(s.Content, s.Child!.DataContext);
    }

    [AvaloniaFact]
    public void Another_conversation_shows_only_its_own_items()
    {
        var view = UiHelpers.TouchView(Long());
        UiHelpers.ShowPhone(view);
        ScrollDownAndUp(view);
        var other = Long(5);
        view.Conversation = other;
        UiHelpers.Pump();
        Assert.NotEmpty(Shown(view));
        foreach (var s in Shown(view))
        {
            Assert.Contains(s.Content!, other.Items.Cast<object>());
            Assert.Same(s.Content, s.Child!.DataContext);
        }
    }
}
