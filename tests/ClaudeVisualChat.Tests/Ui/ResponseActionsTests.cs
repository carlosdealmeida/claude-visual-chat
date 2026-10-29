using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using Xunit;

namespace ClaudeVisualChat.Tests.Ui;

/// <summary>
/// The actions under a finished turn (the extension's row under a reply): copy the answer, bookmark it and, when the host takes
/// feedback, rate it (thumbs down asks "What went wrong?"), with the link that tells how the feedback is used when the host opens
/// links. On the desktop the row is at the end of the turn and shows under the pointer or the keyboard; in the touch layout the
/// turn's one copy is the 0.5.0 button, and only the host's actions join it, always shown, at 44 px.
/// </summary>
public class ResponseActionsTests
{
    /// <summary>A finished turn of two texts with a tool call between them; the last text came from a line with a uuid.</summary>
    private static (ChatConversation C, AssistantTextItem First, AssistantTextItem Last) Turn()
    {
        var c = new ChatConversation();
        c.Items.Add(new UserMessageItem("Fix the build"));
        var first = new AssistantTextItem { Text = "Looking at the build.", Uuid = "u-1" };
        c.Items.Add(first);
        c.Items.Add(new ToolCallItem { Name = "Bash", Summary = "dotnet build", Status = ToolStatus.Succeeded });
        var last = new AssistantTextItem { Text = "It builds now.", Uuid = "u-2" };
        c.Items.Add(last);
        c.Items.Add(new TurnFooterItem(1200, 10, 20, 0.01, false));
        return (c, first, last);
    }

    private static (ClaudeChatView View, Window Window) Open(ChatConversation c, ChatLayout layout = ChatLayout.Desktop)
    {
        var view = new ClaudeChatView { Conversation = c, Layout = layout };
        var window = layout == ChatLayout.Touch ? UiHelpers.ShowPhone(view) : UiHelpers.Show(view);
        return (view, window);
    }

    private static List<ResponseActions> AllActions(ClaudeChatView view) =>
        view.GetVisualDescendants().OfType<ResponseActions>().ToList();

    private static ResponseActions Actions(ClaudeChatView view) => AllActions(view).Single();

    private static FeedbackDialog Dialog(ClaudeChatView view) => view.GetVisualDescendants().OfType<FeedbackDialog>().Single();

    /// <summary>The pointer away from the list's turns: over the top left corner of the window.</summary>
    private static void PointerAway(Window window)
    {
        window.MouseMove(new Point(2, 2), RawInputModifiers.None);
        UiHelpers.Pump();
    }

    /// <summary>How much of <paramref name="control"/> shows: its opacity times its parents' (the thumbs are clear as a group).</summary>
    private static double Shown(Control control)
    {
        var opacity = 1.0;
        for (Visual? v = control; v != null && !(v is ClaudeChatView); v = v.GetVisualParent()) opacity *= v.Opacity;
        return opacity;
    }

    /// <summary>The keyboard to the reply box, off the row (a button the keyboard is on shows).</summary>
    private static void KeyboardAway(ClaudeChatView view)
    {
        UiHelpers.Input(view).Focus();
        UiHelpers.Pump();
    }

    private static void PointerOver(Window window, Control control)
    {
        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(center, RawInputModifiers.None);
        UiHelpers.Pump();
    }

    [AvaloniaFact]
    public void Copy_response_copies_the_text_blocks_joined_by_blank_lines()
    {
        var copied = new List<string>();
        ChatClipboard.Override = text => { copied.Add(text); return Task.CompletedTask; };
        try
        {
            var (c, _, _) = Turn();
            var (view, window) = Open(c);
            var actions = Actions(view);
            var copy = actions.CopyButton;
            Assert.True(copy.IsEffectivelyVisible);
            Assert.Equal("Copy response to clipboard", AutomationProperties.GetName(copy));
            Assert.Equal("Copy response", ToolTip.GetTip(copy));
            PointerAway(window);
            Assert.Equal(0, Shown(copy));                                     // only under the pointer or the keyboard
            PointerOver(window, actions);
            Assert.Equal(1, Shown(copy));
            Assert.Equal(20, copy.Bounds.Width, 1);
            Assert.Equal(20, copy.Bounds.Height, 1);

            UiHelpers.Click(window, copy);
            Assert.Equal(new[] { "Looking at the build.\n\nIt builds now." }, copied); // the turn's texts, a blank line between them
            Assert.True(actions.IsCopied);
            Assert.True(actions.IsTicking);                                    // the check, for 2 s
            Assert.Equal("Copied", AutomationProperties.GetName(copy));
            actions.EndCopied();                                               // the tick
            Assert.False(actions.IsCopied);
            Assert.Equal("Copy response to clipboard", AutomationProperties.GetName(copy));

            // a clipboard that refuses it confirms nothing
            ChatClipboard.Override = _ => throw new System.InvalidOperationException("refused");
            UiHelpers.Click(window, copy);
            Assert.False(actions.IsCopied);
            window.Close();

            // the touch layout has one copy per turn: the 0.5.0 button, never a second one beside it
            ChatClipboard.Override = text => { copied.Add(text); return Task.CompletedTask; };
            var (touch, touchWindow) = Open(Turn().C, ChatLayout.Touch);
            Assert.Single(touch.GetVisualDescendants().OfType<CopyTurnButton>(), b => b.IsEffectivelyVisible);
            Assert.DoesNotContain(AllActions(touch), a => a.CopyButton.IsEffectivelyVisible);
            touchWindow.Close();
        }
        finally { ChatClipboard.Override = null; }
    }

    [AvaloniaFact]
    public void Bookmark_toggles_and_a_bookmarked_reply_keeps_its_icon_visible()
    {
        var (c, first, last) = Turn();
        var (view, window) = Open(c);
        var actions = Actions(view);
        var bookmark = actions.BookmarkButton;
        Assert.Equal("Bookmark response", AutomationProperties.GetName(bookmark));
        Assert.Equal("Bookmark response", ToolTip.GetTip(bookmark));
        PointerAway(window);
        Assert.Equal(0, Shown(bookmark));

        UiHelpers.Click(window, bookmark);
        Assert.True(last.IsBookmarked);                                        // the turn's last text is the one marked
        Assert.False(first.IsBookmarked);
        Assert.Equal(new[] { last }, c.Bookmarks);
        Assert.Equal("Remove bookmark", AutomationProperties.GetName(bookmark));
        Assert.Equal("Remove bookmark", ToolTip.GetTip(bookmark));
        Assert.Contains("marked", bookmark.Classes);                           // the filled icon
        KeyboardAway(view);
        PointerAway(window);
        Assert.Equal(1, Shown(bookmark));                                     // a marked reply keeps it in sight
        Assert.Equal(0, Shown(actions.CopyButton));                           // and only it

        UiHelpers.Click(window, bookmark);                                     // again: unmarked
        Assert.False(last.IsBookmarked);
        Assert.Empty(c.Bookmarks);
        Assert.DoesNotContain("marked", bookmark.Classes);
        Assert.Equal(1, Shown(bookmark));                                      // the keyboard is on it: it shows
        KeyboardAway(view);
        PointerAway(window);
        Assert.Equal(0, Shown(bookmark));

        last.IsBookmarked = true;                                              // the item tells the row, wherever it changed
        UiHelpers.Pump();
        Assert.Equal("Remove bookmark", AutomationProperties.GetName(bookmark));
        window.Close();

        // the touch layout: the bookmark (it is local, no host needed) beside the turn's copy button, always shown, a finger's target
        var (tc, _, tlast) = Turn();
        var (touch, touchWindow) = Open(tc, ChatLayout.Touch);
        var touchActions = AllActions(touch).Single(a => a.IsEffectivelyVisible);
        var touchBookmark = touchActions.BookmarkButton;
        Assert.True(touchBookmark.IsEffectivelyVisible);
        Assert.Equal(1, Shown(touchBookmark));
        Assert.Equal(44, touchBookmark.Bounds.Width, 1);
        Assert.Equal(44, touchBookmark.Bounds.Height, 1);
        Assert.Equal("Bookmark response", AutomationProperties.GetName(touchBookmark));
        var copy = touch.GetVisualDescendants().OfType<CopyTurnButton>().Single(b => b.IsEffectivelyVisible);
        var copyAt = copy.TranslatePoint(default, touch)!.Value;
        var bookmarkAt = touchBookmark.TranslatePoint(default, touch)!.Value;
        Assert.Equal(copyAt.Y, bookmarkAt.Y, 1);                               // on the copy button's line
        Assert.True(bookmarkAt.X >= copyAt.X + copy.Bounds.Width - 0.5);       // after it
        UiHelpers.Click(touchWindow, touchBookmark);
        Assert.True(tlast.IsBookmarked);
        Assert.Equal(new[] { tlast }, tc.Bookmarks);
        Assert.Equal("Remove bookmark", AutomationProperties.GetName(touchBookmark));

        // and in a read-only view, where the bookmark still works
        touch.IsReadOnly = true;
        UiHelpers.Pump();
        Assert.True(touchBookmark.IsEffectivelyVisible);
        Assert.Equal(1, Shown(touchBookmark));
        UiHelpers.Click(touchWindow, touchBookmark);
        Assert.False(tlast.IsBookmarked);
        Assert.Empty(tc.Bookmarks);
        touchWindow.Close();
    }

    [AvaloniaFact]
    public void The_conversation_keeps_its_bookmarks_in_the_order_they_were_marked_and_forgets_them_with_its_items()
    {
        var (c, first, last) = Turn();
        c.ToggleBookmark(last);
        c.ToggleBookmark(first);
        Assert.Equal(new[] { last, first }, c.Bookmarks);
        c.ToggleBookmark(last);
        Assert.Equal(new[] { first }, c.Bookmarks);
        Assert.False(last.IsBookmarked);
        c.Clear();
        Assert.Empty(c.Bookmarks);

        var (other, _, kept) = Turn();
        other.ToggleBookmark(kept);
        other.LoadTranscript(FixtureFiles.TranscriptLines("bash"));
        Assert.Empty(other.Bookmarks);
    }

    [AvaloniaTheory]
    [InlineData(ChatLayout.Desktop)]
    [InlineData(ChatLayout.Touch)]
    public void Without_a_feedback_subscriber_there_are_no_thumbs(ChatLayout layout)
    {
        var (c, _, _) = Turn();
        var (view, window) = Open(c, layout);
        Assert.DoesNotContain(AllActions(view), a => a.GoodButton.IsEffectivelyVisible || a.BadButton.IsEffectivelyVisible);
        Assert.DoesNotContain("Good response", UiHelpers.ButtonNames(view));
        Assert.DoesNotContain("Bad response", UiHelpers.ButtonNames(view));

        // the host subscribes: the thumbs come
        var asked = new List<FeedbackRequestedEventArgs>();
        System.EventHandler<FeedbackRequestedEventArgs> handler = (_, e) => asked.Add(e);
        c.FeedbackRequested += handler;
        UiHelpers.Pump();
        var actions = AllActions(view).Single(a => a.IsEffectivelyVisible);
        Assert.True(actions.GoodButton.IsEffectivelyVisible);
        Assert.True(actions.BadButton.IsEffectivelyVisible);
        Assert.Contains("Good response", UiHelpers.ButtonNames(view));
        Assert.Contains("Bad response", UiHelpers.ButtonNames(view));
        Assert.Equal("Good response", ToolTip.GetTip(actions.GoodButton));
        PointerAway(window);
        if (layout == ChatLayout.Touch)
        {
            // no pointer to hover with: always shown, a finger's target, beside the turn's copy button
            Assert.Equal(1, Shown(actions.GoodButton));
            Assert.Equal(1, Shown(actions.BadButton));
            Assert.Equal(44, actions.GoodButton.Bounds.Height, 1);
            Assert.Equal(44, actions.BadButton.Bounds.Width, 1);
            var copy = view.GetVisualDescendants().OfType<CopyTurnButton>().Single(b => b.IsEffectivelyVisible);
            var copyAt = copy.TranslatePoint(default, view)!.Value;
            var goodAt = actions.GoodButton.TranslatePoint(default, view)!.Value;
            Assert.Equal(copyAt.Y, goodAt.Y, 1);                                // on the same line
            Assert.True(goodAt.X >= copyAt.X + copy.Bounds.Width - 0.5);       // after it
        }
        else
        {
            Assert.Equal(0, Shown(actions.GoodButton));
            Assert.Equal(20, actions.GoodButton.Bounds.Height, 1);
        }

        // the host lets go: they go again
        c.FeedbackRequested -= handler;
        UiHelpers.Pump();
        Assert.DoesNotContain(AllActions(view), a => a.GoodButton.IsEffectivelyVisible);
        Assert.Empty(asked);
        window.Close();

        // a reply without the line's uuid cannot be rated (the host has nothing to name it by)
        var (bare, _, bareLast) = Turn();
        bareLast.Uuid = null;
        bare.FeedbackRequested += (_, _) => { };
        var (bareView, bareWindow) = Open(bare, layout);
        Assert.DoesNotContain(AllActions(bareView), a => a.GoodButton.IsEffectivelyVisible);
        bareWindow.Close();
    }

    [AvaloniaFact]
    public void Thumbs_down_opens_what_went_wrong_and_send_raises_feedback_requested()
    {
        var (c, _, last) = Turn();
        var asked = new List<FeedbackRequestedEventArgs>();
        c.FeedbackRequested += (_, e) => asked.Add(e);
        var stops = 0;
        c.StopRequested += (_, _) => stops++;
        var (view, window) = Open(c);
        var actions = Actions(view);
        var dialog = Dialog(view);
        Assert.False(dialog.IsOpen);

        // down: the rating goes at once, and the box asks what went wrong
        UiHelpers.Click(window, actions.BadButton);
        Assert.Single(asked);
        Assert.Same(last, asked[0].Item);
        Assert.Equal(false, asked[0].Positive);
        Assert.Null(asked[0].Details);
        Assert.Contains("selected", actions.BadButton.Classes);
        Assert.True(dialog.IsOpen);
        Assert.Contains("What went wrong?", UiHelpers.Texts(dialog));
        Assert.Equal("Tell us more (optional)", dialog.Field.Watermark);
        Assert.Contains("Skip", UiHelpers.Texts(dialog));
        Assert.Contains("Send feedback", UiHelpers.Texts(dialog));
        Assert.Same(dialog.Field, UiHelpers.Focused(window));
        UiHelpers.Type(window, "It broke the tests");
        UiHelpers.Press(window, Key.Enter);                                    // Enter breaks the line
        Assert.True(dialog.IsOpen);
        UiHelpers.Press(window, Key.Enter, RawInputModifiers.Control);         // Ctrl+Enter sends
        Assert.False(dialog.IsOpen);
        Assert.Equal(2, asked.Count);
        Assert.Same(last, asked[1].Item);
        Assert.Equal(false, asked[1].Positive);
        Assert.Equal("It broke the tests", asked[1].Details);                  // trimmed

        // a rated reply keeps its thumbs in sight; down again clears the rating
        Assert.Same(actions.BadButton, UiHelpers.Focused(window));             // the keyboard went back to the thumb
        KeyboardAway(view);
        PointerAway(window);
        Assert.Equal(1, Shown(actions.BadButton));
        UiHelpers.Click(window, actions.BadButton);
        Assert.Equal(3, asked.Count);
        Assert.Null(asked[2].Positive);
        Assert.Null(asked[2].Details);
        Assert.False(dialog.IsOpen);                                           // clearing asks nothing
        Assert.DoesNotContain("selected", actions.BadButton.Classes);

        // up: the rating, no box
        UiHelpers.Click(window, actions.GoodButton);
        Assert.Equal(true, asked[3].Positive);
        Assert.False(dialog.IsOpen);
        Assert.Contains("selected", actions.GoodButton.Classes);
        Assert.DoesNotContain("selected", actions.BadButton.Classes);

        // down from up: the box again; Skip closes it and sends nothing more
        UiHelpers.Click(window, actions.BadButton);
        Assert.Equal(false, asked[4].Positive);
        Assert.True(dialog.IsOpen);
        UiHelpers.Click(window, dialog.SkipButton);
        Assert.False(dialog.IsOpen);
        Assert.Equal(5, asked.Count);

        // Esc closes the box and only it, while Claude works and a request waits under it: Claude is not stopped, and the request
        // does not take the Esc for its "No"
        var answers = 0;
        c.PermissionAnswered += (_, _) => answers++;
        using (var input = System.Text.Json.JsonDocument.Parse("{\"command\":\"ls\"}"))
        {
            c.Apply(new ClaudeVisualChat.Protocol.AssistantBlock("m9", null,
                new ClaudeVisualChat.Protocol.ToolUseBlock("t9", "Bash", input.RootElement.Clone())));
            c.Apply(new ClaudeVisualChat.Protocol.PermissionRequested("r9", "Bash", "Bash", null, "t9", input.RootElement.Clone(), null));
        }
        UiHelpers.Pump();
        Assert.True(c.IsBusy);
        Assert.NotNull(c.CurrentPermission);
        UiHelpers.DrawFrames();
        UiHelpers.Click(window, actions.BadButton);                            // clears
        UiHelpers.Click(window, actions.BadButton);                            // down again
        Assert.True(dialog.IsOpen);
        Assert.Same(dialog.Field, UiHelpers.Focused(window));
        UiHelpers.Press(window, Key.Escape);
        Assert.False(dialog.IsOpen);
        Assert.Equal(0, stops);
        Assert.Equal(0, answers);
        Assert.NotNull(c.CurrentPermission);                                   // still waiting to be answered
        Assert.Equal(7, asked.Count);

        // what goes to the host stops at 4,000 characters: the field takes no more, and the conversation cuts what it is given
        UiHelpers.Click(window, actions.BadButton);                            // clears
        UiHelpers.Click(window, actions.BadButton);                            // down again
        Assert.Equal(4000, dialog.Field.MaxLength);
        dialog.Field.Text = new string('x', 4500);
        UiHelpers.Click(window, dialog.SendButton);
        Assert.Equal(4000, asked[asked.Count - 1].Details!.Length);
        c.SendFeedbackDetails(last, "  " + new string('y', 4500) + "  ");
        Assert.Equal(new string('y', 4000), asked[asked.Count - 1].Details);
        window.Close();
    }

    [AvaloniaFact]
    public void The_feedback_details_never_end_in_half_an_emoji()
    {
        var (c, last, _) = Turn();
        string? sent = null;
        c.FeedbackRequested += (_, e) => sent = e.Details;
        c.SendFeedbackDetails(last, new string('y', 3999) + "😀 and more");
        Assert.Equal(new string('y', 3999), sent);
    }

    [AvaloniaFact]
    public void The_learn_link_shows_only_with_a_link_subscriber()
    {
        var (c, _, _) = Turn();
        c.FeedbackRequested += (_, _) => { };
        var (view, window) = Open(c);
        var actions = Actions(view);
        Assert.False(actions.LinkButton.IsEffectivelyVisible);                 // no one to open it
        Assert.DoesNotContain("Learn how we use your feedback", UiHelpers.Texts(view));

        UiHelpers.Click(window, actions.GoodButton);
        Assert.Contains("Thanks for your feedback", UiHelpers.Texts(actions));
        Assert.False(actions.LinkButton.IsEffectivelyVisible);
        Assert.DoesNotContain(" · ", UiHelpers.Texts(actions));

        // the host opens links: the link comes, and a click asks the host to open the page
        var links = new List<string>();
        c.LinkRequested += (_, e) => links.Add(e.Url);
        UiHelpers.Pump();
        Assert.True(actions.LinkButton.IsEffectivelyVisible);
        Assert.Contains("Thanks for your feedback", UiHelpers.Texts(actions));
        Assert.Contains("Learn how we use it", UiHelpers.Texts(actions.LinkButton));
        UiHelpers.Click(window, actions.LinkButton);
        Assert.Equal(new[] { "https://code.claude.com/docs/en/data-usage" }, links);

        // before a rating it says what it is about
        UiHelpers.Click(window, actions.GoodButton);                           // clears
        Assert.DoesNotContain("Thanks for your feedback", UiHelpers.Texts(actions));
        Assert.Contains("Learn how we use your feedback", UiHelpers.Texts(actions.LinkButton));
        window.Close();
    }

    [AvaloniaFact]
    public void A_partial_or_subagent_reply_has_no_actions()
    {
        // a reply still arriving: no end of turn yet, so nothing under it
        var c = new ChatConversation();
        c.FeedbackRequested += (_, _) => { };
        c.Items.Add(new UserMessageItem("Fix the build"));
        c.Items.Add(new AssistantTextItem { Text = "Looking at", IsStreaming = true, Uuid = "u-1" });
        var (view, window) = Open(c);
        Assert.DoesNotContain(AllActions(view), a => a.IsEffectivelyVisible);
        window.Close();

        // what a subagent wrote is inside its call: a turn with only that has no answer of its own
        var s = new ChatConversation();
        s.FeedbackRequested += (_, _) => { };
        s.Items.Add(new UserMessageItem("Ask an agent"));
        var agent = new ToolCallItem { Name = "Agent", Summary = "Look around", Status = ToolStatus.Succeeded };
        agent.Children.Add(new AssistantTextItem { Text = "Found it.", Uuid = "u-9" });
        s.Items.Add(agent);
        s.Items.Add(new TurnFooterItem(1200, 10, 20, 0.01, false));
        var (subView, subWindow) = Open(s);
        Assert.DoesNotContain(AllActions(subView), a => a.IsEffectivelyVisible);
        Assert.DoesNotContain("Copy response to clipboard", UiHelpers.ButtonNames(subView));
        subWindow.Close();
    }

    [AvaloniaFact]
    public void A_reply_takes_the_uuid_of_its_line()
    {
        var c = new ChatConversation();
        c.AppendLine("{\"type\":\"assistant\",\"uuid\":\"6b9d2d49\",\"message\":{\"id\":\"m1\",\"content\":[{\"type\":\"text\",\"text\":\"Hi\"}]}}");
        Assert.Equal("6b9d2d49", c.Items.OfType<AssistantTextItem>().Single().Uuid);

        // streamed: the item comes before its line, and takes the uuid when the line arrives
        var s = UiHelpers.Recording("bash", "stream");
        Assert.Equal("069a0666-a0b5-4969-a721-defbdfef2689", s.Items.OfType<AssistantTextItem>().Last().Uuid);
        var t = UiHelpers.Recording("bash", "transcript");
        Assert.Equal("069a0666-a0b5-4969-a721-defbdfef2689", t.Items.OfType<AssistantTextItem>().Last().Uuid);
    }
}
