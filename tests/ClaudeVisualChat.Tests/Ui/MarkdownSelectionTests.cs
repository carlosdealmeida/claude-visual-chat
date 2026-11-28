using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using ColorTextBlock.Avalonia;
using Xunit;

namespace ClaudeVisualChat.Tests.Ui;

/// <summary>Selecting the words of a reply in Markdown with the mouse, and copying them.</summary>
public class MarkdownSelectionTests
{
    private const string First = "First paragraph with some words.";
    private const string Second = "Second paragraph with more words.";
    private const string TwoParagraphs = First + "\n\n" + Second;

    private static (MarkdownBlock Block, Window Window, List<CTextBlock> Texts) Open(string markdown, Control? beside = null)
    {
        var block = new MarkdownBlock { Markdown = markdown, RenderInterval = System.TimeSpan.Zero };
        var panel = new StackPanel { Children = { block } };
        if (beside != null) panel.Children.Add(beside);
        var window = UiHelpers.Show(panel, 500);
        return (block, window, TextsOf(block));
    }

    private static List<CTextBlock> TextsOf(Visual root) => root.GetVisualDescendants().OfType<CTextBlock>().ToList();

    /// <summary>A point of the window on the middle of the line of <paramref name="text"/>, <paramref name="x"/> px from its left.</summary>
    private static Point At(Window window, CTextBlock text, double x) =>
        text.TranslatePoint(new Point(x, text.Bounds.Height / 2), window)!.Value;

    private static void Drag(Window window, Point from, Point to)
    {
        window.MouseMove(from, RawInputModifiers.None);
        window.MouseDown(from, MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(from + (to - from) / 2, RawInputModifiers.LeftMouseButton);
        window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        window.MouseUp(to, MouseButton.Left, RawInputModifiers.None);
        UiHelpers.Pump();
    }

    private static void Click(Window window, Point at)
    {
        window.MouseMove(at, RawInputModifiers.None);
        window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
        UiHelpers.Pump();
    }

    private static List<string> CopyWith(System.Action act)
    {
        var copied = new List<string>();
        ChatClipboard.Override = text => { copied.Add(text); return Task.CompletedTask; };
        try { act(); }
        finally { ChatClipboard.Override = null; }
        return copied;
    }

    [AvaloniaFact]
    public void Dragging_over_a_paragraph_selects_the_words_under_it()
    {
        var (_, window, texts) = Open(TwoParagraphs);

        Drag(window, At(window, texts[0], 1), At(window, texts[0], 70));

        var selected = texts[0].GetSelectedText();
        Assert.StartsWith("First", selected);
        Assert.StartsWith(selected, First);
        Assert.True(selected.Length < First.Length, $"only the words under the drag: '{selected}'");
        Assert.Equal("", texts[1].GetSelectedText());
        window.Close();
    }

    [AvaloniaFact]
    public void Dragging_from_one_paragraph_into_the_next_selects_across_them_either_way()
    {
        var (_, window, texts) = Open(TwoParagraphs);

        Drag(window, At(window, texts[0], 1), At(window, texts[1], 40));
        Assert.Equal(First, texts[0].GetSelectedText());
        var down = texts[1].GetSelectedText();
        Assert.StartsWith("Sec", down);
        Assert.StartsWith(down, Second);

        Drag(window, At(window, texts[1], 40), At(window, texts[0], 1)); // from the end back up to the start: the same words
        Assert.Equal(First, texts[0].GetSelectedText());
        Assert.Equal(down, texts[1].GetSelectedText());
        window.Close();
    }

    [AvaloniaFact]
    public void Ctrl_C_copies_the_selection_one_line_per_block()
    {
        var (block, window, texts) = Open(TwoParagraphs);
        Drag(window, At(window, texts[0], 1), At(window, texts[1], 40));
        Assert.True(block.IsFocused, "the selection takes the keyboard, as a page's does");

        var copied = CopyWith(() =>
        {
            UiHelpers.Press(window, Key.C, RawInputModifiers.Control);
            UiHelpers.Pump();
        });

        Assert.Equal(new[] { First + "\n" + texts[1].GetSelectedText() }, copied);
        window.Close();
    }

    [AvaloniaFact]
    public void A_click_selects_nothing_and_ends_a_selection()
    {
        var (_, window, texts) = Open(TwoParagraphs);
        Drag(window, At(window, texts[0], 1), At(window, texts[0], 70));
        Assert.NotEqual("", texts[0].GetSelectedText());

        Click(window, At(window, texts[1], 30));

        Assert.Equal("", texts[0].GetSelectedText());
        Assert.Equal("", texts[1].GetSelectedText());
        var copied = CopyWith(() =>
        {
            UiHelpers.Press(window, Key.C, RawInputModifiers.Control);
            UiHelpers.Pump();
        });
        Assert.Empty(copied);
        window.Close();
    }

    [AvaloniaFact]
    public void Another_control_taking_the_keyboard_ends_the_selection()
    {
        var box = new TextBox();
        var (_, window, texts) = Open(TwoParagraphs, box);
        Drag(window, At(window, texts[0], 1), At(window, texts[0], 70));
        Assert.NotEqual("", texts[0].GetSelectedText());

        Click(window, box.TranslatePoint(new Point(10, box.Bounds.Height / 2), window)!.Value);

        Assert.True(box.IsFocused);
        Assert.Equal("", texts[0].GetSelectedText());
        window.Close();
    }

    [AvaloniaFact]
    public void A_new_drawing_of_the_text_drops_the_selection()
    {
        var (block, window, texts) = Open(TwoParagraphs);
        Drag(window, At(window, texts[0], 1), At(window, texts[1], 40));

        block.Markdown = TwoParagraphs + " And more."; // the answer still arriving
        UiHelpers.Pump();

        Assert.All(TextsOf(block), t => Assert.Equal("", t.GetSelectedText()));
        var copied = CopyWith(() =>
        {
            UiHelpers.Press(window, Key.C, RawInputModifiers.Control);
            UiHelpers.Pump();
        });
        Assert.Empty(copied);
        window.Close();
    }

    [AvaloniaFact]
    public void A_press_that_does_not_move_does_not_take_the_keyboard()
    {
        var box = new TextBox();
        var (block, window, texts) = Open(TwoParagraphs, box);
        Assert.True(box.Focus());

        Click(window, At(window, texts[0], 30));

        Assert.True(box.IsFocused, "a click on the text leaves the keyboard where it was");
        Assert.False(block.IsFocused);
        window.Close();
    }

    [AvaloniaFact]
    public void The_selection_takes_the_color_of_the_themes_text_selection()
    {
        var (_, window, texts) = Open(TwoParagraphs);
        Drag(window, At(window, texts[0], 1), At(window, texts[0], 70));

        Assert.True(window.TryFindResource("TextControlSelectionHighlightColor", window.ActualThemeVariant, out var expected));
        Assert.Same(expected, texts[0].SelectionBrush);
        window.Close();
    }

    [AvaloniaFact]
    public void The_plan_document_keeps_its_own_selection()
    {
        var plan = new MarkdownBlock { Markdown = "- an item of the plan\n- another item", SelectsText = false, RenderInterval = System.TimeSpan.Zero };
        var window = UiHelpers.Show(plan, 500);
        var item = TextsOf(plan).First();

        Drag(window, At(window, item, 1), At(window, item, 60));

        Assert.All(TextsOf(plan), t => Assert.Equal("", t.GetSelectedText()));
        window.Close();
    }

    [AvaloniaFact]
    public void A_drag_can_start_beside_the_words_of_a_short_line()
    {
        var (_, window, texts) = Open("Short.\n\n" + Second);
        var beside = texts[0].TranslatePoint(new Point(400, texts[0].Bounds.Height / 2), window)!.Value; // far right of "Short."

        Drag(window, beside, At(window, texts[1], 40));

        Assert.StartsWith("Sec", texts[1].GetSelectedText());
        window.Close();
    }

    [AvaloniaFact]
    public void A_drag_right_after_a_click_still_selects()
    {
        var (_, window, texts) = Open(TwoParagraphs);
        var from = At(window, texts[0], 1);
        Click(window, from);

        Drag(window, from, At(window, texts[0], 70)); // the press is the second of a double click

        Assert.StartsWith("First", texts[0].GetSelectedText());
        window.Close();
    }

    [AvaloniaFact]
    public void A_drag_whose_release_never_came_stops_following_the_pointer()
    {
        var button = new Button { Content = "beside" };
        var pressed = 0;
        button.Click += (_, _) => pressed++;
        var (_, window, texts) = Open(TwoParagraphs, button);
        var from = At(window, texts[0], 1);
        window.MouseMove(from, RawInputModifiers.None);
        window.MouseDown(from, MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(At(window, texts[0], 70), RawInputModifiers.LeftMouseButton);
        UiHelpers.Pump();
        var selected = texts[0].GetSelectedText();
        Assert.NotEqual("", selected);

        // the button came up somewhere the window did not see (another window took the pointer): the next move has no button down
        window.MouseMove(At(window, texts[1], 60), RawInputModifiers.None);
        UiHelpers.Pump();

        Assert.Equal(selected, texts[0].GetSelectedText());
        Assert.Equal("", texts[1].GetSelectedText());
        Click(window, button.TranslatePoint(new Point(10, button.Bounds.Height / 2), window)!.Value);
        Assert.Equal(1, pressed); // and the pointer is not the block's any more
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("- first item\n- second item")]
    [InlineData("1. first item\n2. second item")]
    public void The_marker_of_a_list_item_is_copied_at_the_start_of_its_line_and_is_not_a_line_of_its_own(string markdown)
    {
        var (block, window, _) = Open(markdown);
        var all = TextsOf(block);
        var markers = all.Where(t => t.Classes.Contains("ListMarker")).ToList();
        var items = all.Where(t => !t.Classes.Contains("ListMarker")).ToList();
        Assert.Equal(2, markers.Count);
        Assert.Equal(2, items.Count);

        Drag(window, At(window, items[0], 1), At(window, items[1], 200));
        var copied = CopyWith(() =>
        {
            UiHelpers.Press(window, Key.C, RawInputModifiers.Control);
            UiHelpers.Pump();
        });

        Assert.Equal(new[] { markers[0].Text.Trim() + " first item\n" + markers[1].Text.Trim() + " second item" }, copied);
        Assert.All(markers, m => Assert.Equal("", m.GetSelectedText())); // the marker itself is never painted as selected
        window.Close();
    }

    [AvaloniaFact]
    public void A_list_item_selected_from_its_middle_is_copied_without_its_marker()
    {
        var (block, window, _) = Open("- first item\n- second item");
        var item = TextsOf(block).First(t => !t.Classes.Contains("ListMarker"));

        Drag(window, At(window, item, 30), At(window, item, 200));
        var copied = CopyWith(() =>
        {
            UiHelpers.Press(window, Key.C, RawInputModifiers.Control);
            UiHelpers.Pump();
        });

        var text = Assert.Single(copied);
        Assert.EndsWith("item", text);
        Assert.True(text.Length < "first item".Length, $"from the middle: '{text}'");
        window.Close();
    }

    [AvaloniaFact]
    public void Esc_ends_the_selection_and_lets_the_keyboard_go()
    {
        var (block, window, texts) = Open(TwoParagraphs);
        Drag(window, At(window, texts[0], 1), At(window, texts[0], 70));
        Assert.True(block.IsFocused);

        UiHelpers.Press(window, Key.Escape);
        UiHelpers.Pump();

        Assert.Equal("", texts[0].GetSelectedText());
        Assert.False(block.IsFocused);
        Assert.False(block.Focusable);
        window.Close();
    }

    private static (ClaudeChatView View, Window Window, MarkdownBlock Answer, List<CTextBlock> Texts) OpenInAView()
    {
        var c = new ChatConversation();
        c.Items.Add(new UserMessageItem("question"));
        c.Items.Add(new AssistantTextItem { Text = TwoParagraphs });
        c.Items.Add(new TurnFooterItem(null, null, null, null, false));
        var view = new ClaudeChatView { Conversation = c };
        var window = UiHelpers.Show(view);
        var answer = view.GetVisualDescendants().OfType<MarkdownBlock>().Single(b => b.Classes.Contains("ClaudeChatAnswer"));
        return (view, window, answer, TextsOf(answer));
    }

    [AvaloniaFact]
    public void In_the_conversation_Esc_gives_the_keyboard_back_to_the_reply_box()
    {
        var (view, window, answer, texts) = OpenInAView();
        Drag(window, At(window, texts[0], 1), At(window, texts[0], 70));
        Assert.True(answer.IsFocused);

        UiHelpers.Press(window, Key.Escape);
        UiHelpers.Pump();

        Assert.Equal("", texts[0].GetSelectedText());
        Assert.True(view.ReplyBox.IsKeyboardFocusWithin, "the keyboard is back in the reply box");
        window.Close();
    }

    [AvaloniaFact]
    public void In_the_conversation_a_new_drawing_of_a_reply_that_holds_the_keyboard_gives_it_back_to_the_reply_box()
    {
        var (view, window, answer, texts) = OpenInAView();
        Drag(window, At(window, texts[0], 1), At(window, texts[0], 70));
        Assert.True(answer.IsFocused);

        answer.RenderInterval = System.TimeSpan.Zero; // the text that arrives is drawn at once, not at the next 100 ms
        ((AssistantTextItem)view.Conversation!.Items[1]).Text = TwoParagraphs + " And more.";
        UiHelpers.Pump();

        Assert.False(answer.IsFocused);
        Assert.True(view.ReplyBox.IsKeyboardFocusWithin, "the keyboard is back in the reply box");
        window.Close();
    }

    [AvaloniaFact]
    public void A_reply_in_the_conversation_can_be_selected_and_copied()
    {
        var c = new ChatConversation();
        c.Items.Add(new UserMessageItem("question"));
        c.Items.Add(new AssistantTextItem { Text = TwoParagraphs });
        c.Items.Add(new TurnFooterItem(null, null, null, null, false));
        var view = new ClaudeChatView { Conversation = c };
        var window = UiHelpers.Show(view);
        var answer = view.GetVisualDescendants().OfType<MarkdownBlock>().Single(b => b.Classes.Contains("ClaudeChatAnswer"));
        var texts = TextsOf(answer);

        Drag(window, At(window, texts[0], 1), At(window, texts[1], 40));
        var copied = CopyWith(() =>
        {
            UiHelpers.Press(window, Key.C, RawInputModifiers.Control);
            UiHelpers.Pump();
        });

        Assert.Equal(new[] { First + "\n" + texts[1].GetSelectedText() }, copied);
        Assert.StartsWith("Sec", texts[1].GetSelectedText());
        window.Close();
    }
}
