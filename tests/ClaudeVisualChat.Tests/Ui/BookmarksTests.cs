using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using ClaudeVisualChat.Protocol;
using Xunit;

namespace ClaudeVisualChat.Tests.Ui;

/// <summary>The bookmarks panel, beside the conversation or below it.</summary>
public class BookmarksTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Turns of a prompt, a reply and a footer; the replies are "Reply 1 …", each with a second line, written one hour apart.</summary>
    private static (ChatConversation C, List<AssistantTextItem> Replies) Talk(int turns)
    {
        var c = new ChatConversation { Clock = () => Now };
        var replies = new List<AssistantTextItem>();
        for (var i = 1; i <= turns; i++)
        {
            c.Items.Add(new UserMessageItem("Question " + i));
            var reply = new AssistantTextItem
            {
                Text = "Reply " + i + " first line\nand a second line of reply " + i,
                Uuid = "u-" + i,
                Timestamp = Now - TimeSpan.FromHours(turns - i) - TimeSpan.FromMinutes(5),
            };
            replies.Add(reply);
            c.Items.Add(reply);
            c.Items.Add(new TurnFooterItem(1000, 1, 1, null, false));
        }
        return (c, replies);
    }

    private static (ClaudeChatView View, Window Window) Open(ChatConversation c, double width = UiHelpers.Width, double height = UiHelpers.Height,
        ChatLayout layout = ChatLayout.Desktop)
    {
        var view = new ClaudeChatView { Conversation = c, Layout = layout };
        var window = UiHelpers.Show(view, width, height);
        return (view, window);
    }

    private static BookmarksPanel Panel(ClaudeChatView view) => view.GetVisualDescendants().OfType<BookmarksPanel>().Single();

    private static List<Button> Rows(BookmarksPanel panel) =>
        panel.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("ClaudeChatBookmarkRow")).ToList();

    private static Button Named(BookmarksPanel panel, string name) =>
        panel.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && b.Classes.Contains(name));

    private static Point Origin(Visual of, Visual window) => of.TranslatePoint(default, window)!.Value;

    private static void Mark(ChatConversation c, params AssistantTextItem[] items)
    {
        foreach (var item in items) c.ToggleBookmark(item);
    }

    // ---- the conversation keeps the list ----

    [Fact]
    public void The_conversation_lists_the_bookmarks_in_the_order_they_were_marked_and_empties_them_with_the_items()
    {
        var (c, replies) = Talk(3);
        Mark(c, replies[2], replies[0]);
        Assert.Equal(new[] { replies[2], replies[0] }, c.Bookmarks);
        c.ToggleBookmark(replies[2]);
        Assert.Equal(new[] { replies[0] }, c.Bookmarks);
        Assert.False(replies[2].IsBookmarked);
        var changes = 0;
        ((System.Collections.Specialized.INotifyCollectionChanged)c.Bookmarks).CollectionChanged += (_, _) => changes++;
        c.Clear();
        Assert.Empty(c.Bookmarks);
        Assert.Equal(1, changes);

        var (loaded, answers) = Talk(2);
        Mark(loaded, answers[0]);
        loaded.LoadTranscript(Array.Empty<string>());
        Assert.Empty(loaded.Bookmarks);
    }

    // ---- the place ----

    [AvaloniaFact]
    public void Bookmarks_open_beside_the_conversation_at_thirty_percent()
    {
        var (c, replies) = Talk(4);
        Mark(c, replies[1]);
        var (view, window) = Open(c);
        var panel = Panel(view);
        Assert.False(panel.IsVisible);
        Assert.Equal(UiHelpers.Width, view.Bounds.Width, 1);                 // closed, it takes no room

        view.ShowBookmarks();
        UiHelpers.Pump();
        Assert.True(panel.IsOpen);
        Assert.Equal(300, panel.Bounds.Width, 1);                            // 30 % of 1000
        Assert.Equal(UiHelpers.Width - 300, Origin(panel, window).X, 1);    // at the right, the chat taking the rest
        Assert.Equal(new Thickness(1, 0, 0, 0), panel.BorderThickness);      // 1 px line on its left
        Assert.Equal(view.Bounds.Height, panel.Bounds.Height, 1);
        Assert.Equal(UiHelpers.Width - 300, UiHelpers.Scroller(view).Bounds.Width + 0, 1);
        Assert.Equal("Bookmarks", UiHelpers.Texts(panel).First());
        Assert.Equal(Avalonia.Media.FontWeight.SemiBold, panel.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Bookmarks").FontWeight);
        window.Close();

        // 30 % is kept between 240 and 420 px
        var (_, wide) = Open(Talk(1).C, 1600, 700);
        var wideView = (ClaudeChatView)wide.Content!;
        wideView.ShowBookmarks();
        UiHelpers.Pump();
        Assert.Equal(420, Panel(wideView).Bounds.Width, 1);
        wide.Close();
        var (_, mid) = Open(Talk(1).C, 600, 700);
        var midView = (ClaudeChatView)mid.Content!;
        midView.ShowBookmarks();
        UiHelpers.Pump();
        Assert.Equal(240, Panel(midView).Bounds.Width, 1);                   // 30 % of 600 is 180
        mid.Close();

        // closing gives the room back
        var (v2, w2) = Open(Talk(1).C);
        v2.ShowBookmarks();
        v2.HideBookmarks();
        UiHelpers.Pump();
        Assert.False(Panel(v2).IsOpen);
        Assert.Equal(UiHelpers.Width, UiHelpers.Scroller(v2).Bounds.Width, 1);
        w2.Close();
    }

    [AvaloniaFact]
    public void A_narrow_view_puts_bookmarks_below_the_chat()
    {
        var (c, replies) = Talk(4);
        Mark(c, replies[0]);
        var (view, window) = Open(c, 500, 700);                              // 500 px or less
        var panel = Panel(view);
        view.ShowBookmarks();
        UiHelpers.Pump();
        Assert.Equal(315, panel.Bounds.Height, 1);                           // 45 % of 700
        Assert.Equal(500, panel.Bounds.Width, 1);
        Assert.Equal(700 - 315, Origin(panel, window).Y, 1);                 // under the chat
        Assert.Equal(new Thickness(0, 1, 0, 0), panel.BorderThickness);
        var composer = UiHelpers.Composer(view);
        Assert.True(Origin(composer, window).Y + composer.Bounds.Height <= 700 - 315 + 1); // the chat, reply box and all, has the rest
        window.Close();

        // between 180 and 360 px
        var (_, tall) = Open(Talk(1).C, 480, 1000);
        var tallView = (ClaudeChatView)tall.Content!;
        tallView.ShowBookmarks();
        UiHelpers.Pump();
        Assert.Equal(360, Panel(tallView).Bounds.Height, 1);
        tall.Close();
        var (_, short1) = Open(Talk(1).C, 480, 300);
        var shortView = (ClaudeChatView)short1.Content!;
        shortView.ShowBookmarks();
        UiHelpers.Pump();
        Assert.Equal(180, Panel(shortView).Bounds.Height, 1);
        short1.Close();

        // 501 px is beside again
        var (beside, w) = Open(Talk(1).C, 501, 700);
        beside.ShowBookmarks();
        UiHelpers.Pump();
        Assert.Equal(new Thickness(1, 0, 0, 0), Panel(beside).BorderThickness);
        w.Close();
    }

    [AvaloniaFact]
    public void The_resize_bar_is_six_pixels_moves_sixteen_with_the_arrows_and_a_double_click_resets()
    {
        var (c, _) = Talk(2);
        var (view, window) = Open(c);
        var panel = Panel(view);
        view.ShowBookmarks();
        UiHelpers.Pump();
        Assert.Equal(6, panel.Handle.Bounds.Width, 1);
        Assert.Equal(300, panel.Bounds.Width, 1);

        panel.Handle.Focus();
        UiHelpers.Press(window, Key.Left);                                   // toward the chat: larger
        Assert.Equal(316, panel.Bounds.Width, 1);
        UiHelpers.Press(window, Key.Right);
        UiHelpers.Press(window, Key.Right);
        Assert.Equal(284, panel.Bounds.Width, 1);
        UiHelpers.Press(window, Key.Up);                                     // an arrow across the bar does nothing
        Assert.Equal(284, panel.Bounds.Width, 1);
        for (var i = 0; i < 20; i++) UiHelpers.Press(window, Key.Right);
        Assert.Equal(200, panel.Bounds.Width, 1);                            // never under 200 px
        for (var i = 0; i < 60; i++) UiHelpers.Press(window, Key.Left);
        Assert.Equal(700, panel.Bounds.Width, 1);                            // nor over 70 %

        // dragged: the bar follows the pointer; toward the chat the panel grows, away from it shrinks
        var start = panel.Handle.TranslatePoint(new Point(3, 100), window)!.Value;
        window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(new Point(start.X + 50, start.Y), RawInputModifiers.LeftMouseButton);
        window.MouseUp(new Point(start.X + 50, start.Y), MouseButton.Left, RawInputModifiers.None);
        UiHelpers.Pump();
        Assert.Equal(650, panel.Bounds.Width, 1);
        start = panel.Handle.TranslatePoint(new Point(3, 100), window)!.Value;
        window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(new Point(start.X - 100, start.Y), RawInputModifiers.LeftMouseButton);
        window.MouseUp(new Point(start.X - 100, start.Y), MouseButton.Left, RawInputModifiers.None);
        UiHelpers.Pump();
        Assert.Equal(700, panel.Bounds.Width, 1);                            // 750 asked, 70 % kept

        panel.ResetSize();                                                   // what the bar's double click does (the headless mouse has no click count)
        UiHelpers.Pump();
        Assert.Equal(300, panel.Bounds.Width, 1);                            // back to the size it opens at
        Assert.Equal("Resize bookmarks", Avalonia.Automation.AutomationProperties.GetName(panel.Handle));
        window.Close();
    }

    // ---- the list ----

    [AvaloniaFact]
    public void The_empty_panel_explains_how_to_bookmark()
    {
        var (c, replies) = Talk(2);
        var (view, window) = Open(c);
        view.ShowBookmarks();
        UiHelpers.Pump();
        var panel = Panel(view);
        Assert.Contains("Bookmark a response with the bookmark button under it to keep it here.", UiHelpers.Texts(panel));
        Assert.Empty(Rows(panel));
        Assert.DoesNotContain("Click a bookmark to show it here.", UiHelpers.Texts(panel));

        c.ToggleBookmark(replies[0]);                                        // the panel follows the list while open
        UiHelpers.Pump();
        Assert.DoesNotContain("Bookmark a response with the bookmark button under it to keep it here.", UiHelpers.Texts(panel));
        Assert.Contains("Click a bookmark to show it here.", UiHelpers.Texts(panel));
        Assert.Single(Rows(panel));

        view.Strings = ChatStrings.PortugueseBrazil;
        UiHelpers.Pump();
        Assert.Equal("Respostas marcadas", UiHelpers.Texts(panel).First());
        Assert.Contains(ChatStrings.PortugueseBrazil.BookmarksSelectHint, UiHelpers.Texts(panel));
        window.Close();
    }

    [AvaloniaFact]
    public void Each_row_shows_the_first_line_at_13_and_how_long_ago_at_11()
    {
        var (c, replies) = Talk(3);
        replies[0].Timestamp = Now - TimeSpan.FromSeconds(20);
        replies[1].Timestamp = Now - TimeSpan.FromMinutes(5);
        replies[2].Timestamp = Now - TimeSpan.FromHours(2) - TimeSpan.FromMinutes(10);
        Mark(c, replies[0], replies[1], replies[2]);
        var extra = new AssistantTextItem { Text = "\n  Old one  \nmore", Timestamp = Now - TimeSpan.FromDays(3) - TimeSpan.FromHours(5) };
        c.Items.Add(extra);
        c.ToggleBookmark(extra);
        var (view, window) = Open(c);
        view.ShowBookmarks();
        UiHelpers.Pump();
        var rows = Rows(Panel(view));
        Assert.Equal(4, rows.Count);
        Assert.Equal(new[] { "Reply 1 first line", "Reply 2 first line", "Reply 3 first line", "Old one" },
            rows.Select(r => r.GetVisualDescendants().OfType<TextBlock>().First().Text).ToArray());
        Assert.Equal(new[] { "just now", "5m ago", "2h ago", "3d ago" },
            rows.Select(r => r.GetVisualDescendants().OfType<TextBlock>().Skip(1).First().Text).ToArray());
        var first = rows[0].GetVisualDescendants().OfType<TextBlock>().ToList();
        Assert.Equal(13, first[0].FontSize);
        Assert.Equal(11, first[1].FontSize);
        Assert.Equal(new[] { "Reply 1 first line" }, rows.Take(1).Select(r => r.GetVisualDescendants().OfType<TextBlock>().First().Text));
        window.Close();

        var (pt, w2) = Open(c);
        pt.Strings = ChatStrings.PortugueseBrazil;
        pt.ShowBookmarks();
        UiHelpers.Pump();
        Assert.Equal("há 5 min", Rows(Panel(pt))[1].GetVisualDescendants().OfType<TextBlock>().Skip(1).First().Text);
        w2.Close();
    }

    [AvaloniaFact]
    public void The_list_takes_a_third_of_the_panel_at_most_and_scrolls()
    {
        var (c, replies) = Talk(30);
        Mark(c, replies.ToArray());
        var (view, window) = Open(c);
        view.ShowBookmarks();
        UiHelpers.Pump();
        var panel = Panel(view);
        var scroller = panel.GetVisualDescendants().OfType<ScrollViewer>().First();
        Assert.Equal(0.35 * panel.Bounds.Height, scroller.Bounds.Height, 1);
        Assert.True(scroller.Extent.Height > scroller.Viewport.Height);
        window.Close();
    }

    // ---- the reply picked ----

    [AvaloniaFact]
    public void A_picked_bookmark_shows_the_whole_reply_with_the_link_and_the_button()
    {
        var (c, replies) = Talk(3);
        Mark(c, replies[0], replies[2]);
        var (view, window) = Open(c);
        view.ShowBookmarks();
        UiHelpers.Pump();
        var panel = Panel(view);
        Assert.Null(panel.Selected);

        UiHelpers.Click(window, Rows(panel)[1]);
        Assert.Same(replies[2], panel.Selected);
        Assert.Contains("selected", Rows(panel)[1].Classes);
        Assert.DoesNotContain("selected", Rows(panel)[0].Classes);
        Assert.DoesNotContain("Click a bookmark to show it here.", UiHelpers.Texts(panel));
        Assert.Contains("Show in conversation", UiHelpers.Texts(panel));
        Assert.Contains("Remove bookmark", UiHelpers.Texts(panel));
        Assert.Contains("and a second line of reply 3", UiHelpers.MarkdownText(panel));  // the whole reply

        // a reply the list no longer holds cannot be shown
        c.Items.Remove(replies[2]);
        UiHelpers.Click(window, Rows(panel)[0]);
        UiHelpers.Click(window, Rows(panel)[1]);
        Assert.Contains("No longer shown in the conversation", UiHelpers.Texts(panel));
        Assert.DoesNotContain("Show in conversation", UiHelpers.Texts(panel));
        Assert.Contains("Remove bookmark", UiHelpers.Texts(panel));
        window.Close();
    }

    [AvaloniaFact]
    public void Show_in_conversation_scrolls_to_the_reply()
    {
        var (c, replies) = Talk(40);
        Mark(c, replies[2]);
        var (view, window) = Open(c);
        var scroller = UiHelpers.Scroller(view);
        UiHelpers.Settle();
        Assert.True(UiHelpers.AtEnd(scroller));                              // following the end
        view.ShowBookmarks();
        UiHelpers.Pump();
        var panel = Panel(view);
        UiHelpers.Click(window, Rows(panel)[0]);
        UiHelpers.Click(window, Named(panel, "ClaudeChatBookmarkLink"));
        UiHelpers.Settle();

        var list = UiHelpers.ItemList(view);
        var index = c.Items.IndexOf(replies[2]);
        var row = list.ContainerFromIndex(index);
        Assert.NotNull(row);
        var top = row!.TranslatePoint(default, scroller)!.Value.Y;
        Assert.InRange(top, -1, scroller.Viewport.Height - 1);                // the reply is on screen, at the top
        Assert.False(UiHelpers.AtEnd(scroller));

        // and the list does not follow the end any more: a new reply leaves it where it was
        var offset = scroller.Offset.Y;
        c.Items.Add(new UserMessageItem("One more"));
        c.Items.Add(new AssistantTextItem { Text = "More." });
        UiHelpers.Settle();
        Assert.Equal(offset, scroller.Offset.Y, 1);
        window.Close();
    }

    [AvaloniaFact]
    public void Show_in_conversation_works_in_the_focus_view_where_the_text_stays_in_sight()
    {
        var (c, replies) = Talk(30);
        Mark(c, replies[1]);
        var tool = new ToolCallItem { Name = "Bash", Summary = "ls", Status = ToolStatus.Succeeded };
        c.Items.Insert(c.Items.IndexOf(replies[1]) + 1, tool);               // a fold right after the bookmarked reply
        var (view, window) = Open(c);
        view.FocusView = true;
        UiHelpers.Settle();
        view.ShowBookmarks();
        UiHelpers.Pump();
        var panel = Panel(view);
        UiHelpers.Click(window, Rows(panel)[0]);
        UiHelpers.Click(window, Named(panel, "ClaudeChatBookmarkLink"));
        UiHelpers.Settle();
        Assert.False(UiHelpers.AtEnd(UiHelpers.Scroller(view)));
        window.Close();
    }

    [AvaloniaFact]
    public void Remove_bookmark_takes_it_off_the_list_and_the_reply()
    {
        var (c, replies) = Talk(3);
        Mark(c, replies[0], replies[1]);
        var (view, window) = Open(c);
        view.ShowBookmarks();
        UiHelpers.Pump();
        var panel = Panel(view);
        UiHelpers.Click(window, Rows(panel)[0]);
        UiHelpers.Click(window, Named(panel, "ClaudeChatBookmarkRemove"));
        Assert.False(replies[0].IsBookmarked);
        Assert.Equal(new[] { replies[1] }, c.Bookmarks);
        Assert.Single(Rows(panel));
        Assert.Null(panel.Selected);
        Assert.Contains("Click a bookmark to show it here.", UiHelpers.Texts(panel));
        window.Close();
    }

    // ---- the palette ----

    [AvaloniaFact]
    public void The_palette_has_a_bookmarks_row_that_opens_the_panel()
    {
        var action = CommandPalette.Actions.Single(a => a.Label == "Bookmarks");
        Assert.Equal(PaletteSection.Context, action.Section);
        Assert.Equal("Show your bookmarked responses beside the conversation", action.Tip!(ChatStrings.English));
        Assert.Equal("Bookmarks", action.Text!(ChatStrings.English));
        Assert.Equal("Respostas marcadas", action.Text!(ChatStrings.PortugueseBrazil));
        var (c, _) = Talk(1);
        var (view, window) = Open(c);
        action.Run(view);
        UiHelpers.Pump();
        Assert.True(Panel(view).IsOpen);
        window.Close();
    }

    // ---- the keyboard ----

    [AvaloniaFact]
    public void Esc_closes_the_panel_and_the_keyboard_goes_back()
    {
        var (c, replies) = Talk(2);
        Mark(c, replies[0]);
        var (view, window) = Open(c);
        var input = UiHelpers.Input(view);
        input.Focus();
        view.ShowBookmarks();
        UiHelpers.Pump();
        var panel = Panel(view);
        Assert.True(panel.CloseButton.IsFocused);
        UiHelpers.Press(window, Key.Escape);
        Assert.False(panel.IsOpen);
        Assert.True(input.IsFocused);                                        // back where it was
        Assert.Equal(UiHelpers.Width, UiHelpers.Scroller(view).Bounds.Width, 1);

        view.ShowBookmarks();
        UiHelpers.Click(window, panel.CloseButton);
        Assert.False(panel.IsOpen);
        window.Close();
    }

    [AvaloniaFact]
    public void A_request_that_arrives_with_bookmarks_open_keeps_the_focus_rule_and_esc_closes_only_the_top()
    {
        var (c, replies) = Talk(2);
        Mark(c, replies[0]);
        var tool = new ToolCallItem { Name = "Edit", Status = ToolStatus.Succeeded, Input = System.Text.Json.JsonDocument.Parse("{\"file_path\":\"a.cs\",\"old_string\":\"x\",\"new_string\":\"y\"}").RootElement.Clone() };
        c.Items.Add(tool);
        var (view, window) = Open(c);
        var answers = new List<string>();
        c.PermissionAnswered += (_, e) => answers.Add(e.Request.RequestId);
        var panel = Panel(view);
        view.ShowBookmarks();
        UiHelpers.Pump();
        Assert.True(panel.CloseButton.IsFocused);

        // the rule of today: the keyboard is in the view and the reply box has nothing to send, so the request takes it
        c.Apply(new PermissionRequested("r1", "Bash", "Bash", null, "t2", System.Text.Json.JsonDocument.Parse("{\"command\":\"ls\"}").RootElement.Clone(), null));
        UiHelpers.Pump();
        var prompt = UiHelpers.Prompt(view);
        Assert.NotNull(prompt.Request);
        Assert.True(prompt.IsKeyboardFocusWithin);
        Assert.True(panel.IsOpen);

        // with the keyboard in the panel, Esc closes the panel and answers nothing
        panel.CloseButton.Focus();
        UiHelpers.Press(window, Key.Escape);
        Assert.False(panel.IsOpen);
        Assert.Empty(answers);
        Assert.NotNull(prompt.Request);

        // a layer over the panel closes first, then the panel
        view.ShowBookmarks();
        UiHelpers.Pump();
        Assert.True(view.ShowDiff(tool));
        UiHelpers.Pump();
        UiHelpers.Press(window, Key.Escape);                                 // the Diff View, only it
        Assert.True(panel.IsOpen);
        Assert.Empty(answers);
        Assert.True(view.CloseTopLayer());                                   // nothing over it: the panel (the host's back button)
        Assert.False(panel.IsOpen);
        Assert.NotNull(prompt.Request);
        Assert.Empty(answers);
        window.Close();
    }

    [AvaloniaFact]
    public void Esc_closes_the_agent_map_over_the_panel_and_leaves_the_panel()
    {
        var (c, replies) = Talk(2);
        Mark(c, replies[0]);
        var (view, window) = Open(c);
        view.ShowBookmarks();
        UiHelpers.Pump();
        c.ShowAgents();                                                       // the Agent map, over the panel
        UiHelpers.Pump();
        UiHelpers.Press(window, Key.Escape);
        Assert.Equal(ChatPanel.None, c.ShownPanel);                           // the map closed, only it
        Assert.True(Panel(view).IsOpen);
        window.Close();
    }

    // ---- the touch layout ----

    [AvaloniaFact]
    public void In_the_touch_layout_the_panel_is_below_the_chat_with_targets_of_44()
    {
        var (c, replies) = Talk(3);
        Mark(c, replies[0], replies[1]);
        var view = UiHelpers.TouchView(c);
        var window = UiHelpers.ShowPhone(view);
        view.ShowBookmarks();
        UiHelpers.Pump();
        var panel = Panel(view);
        Assert.Equal(new Thickness(0, 1, 0, 0), panel.BorderThickness);
        Assert.Equal(UiHelpers.PhoneWidth, panel.Bounds.Width, 1);
        Assert.True(Origin(panel, window).Y > 0);
        Assert.Equal(UiHelpers.PhoneHeight, Origin(panel, window).Y + panel.Bounds.Height, 1);

        Assert.True(panel.CloseButton.Bounds.Width >= 44 && panel.CloseButton.Bounds.Height >= 44);
        Assert.All(Rows(panel), r => Assert.True(r.Bounds.Height >= 44));
        Assert.True(panel.Handle.Bounds.Height >= 44);                       // the hit area
        var line = panel.Handle.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatBookmarksHandleLine"));
        Assert.Equal(6, line.Bounds.Height, 1);                              // and what shows stays 6 px

        UiHelpers.Click(window, Rows(panel)[0]);
        UiHelpers.Pump();
        Assert.True(Named(panel, "ClaudeChatBookmarkLink").Bounds.Height >= 44);
        Assert.True(Named(panel, "ClaudeChatBookmarkRemove").Bounds.Height >= 44);

        // a wide screen in the touch layout still puts it below
        window.Close();
        var (c2, r2) = Talk(1);
        var tablet = UiHelpers.TouchView(c2);
        var w2 = UiHelpers.Show(tablet, 900, 700);
        tablet.ShowBookmarks();
        UiHelpers.Pump();
        Assert.Equal(new Thickness(0, 1, 0, 0), Panel(tablet).BorderThickness);
        w2.Close();
    }

    // ---- the colors ----

    [AvaloniaFact]
    public void The_panel_uses_the_side_bar_color_and_the_sash_color_is_a_resource()
    {
        var (c, _) = Talk(1);
        var (view, window) = Open(c);
        view.ShowBookmarks();
        UiHelpers.Pump();
        var panel = Panel(view);
        Assert.True(view.TryFindResource("ClaudeChat.SashHoverBorder", out var sash));
        Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(sash);
        Assert.Equal(((Avalonia.Media.ISolidColorBrush)view.FindResource("ClaudeChat.Background")!).Color, ((Avalonia.Media.ISolidColorBrush)panel.Background!).Color);
        window.Close();
    }
}
