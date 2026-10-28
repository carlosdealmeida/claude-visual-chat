using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using Xunit;
using AssistantBlock = ClaudeVisualChat.Protocol.AssistantBlock;
using PermissionRequested = ClaudeVisualChat.Protocol.PermissionRequested;
using ToolUseBlock = ClaudeVisualChat.Protocol.ToolUseBlock;

namespace ClaudeVisualChat.Tests.Ui;

public class ReadOnlyTests
{
    private static ChatConversation WithRequest()
    {
        using var doc = JsonDocument.Parse("{\"command\":\"ls\"}");
        var c = new ChatConversation();
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("t1", "Bash", doc.RootElement.Clone())));
        c.Apply(new PermissionRequested("r1", "Bash", "Bash", null, "tool-r1", doc.RootElement.Clone(), null));
        return c;
    }

    private static (ClaudeChatView View, Window Window) Open(ChatConversation c)
    {
        var view = new ClaudeChatView { Conversation = c };
        return (view, UiHelpers.Show(view));
    }

    [AvaloniaFact]
    public void ReadOnly_is_off_by_default()
    {
        Assert.False(new ClaudeChatView().IsReadOnly);
    }

    [AvaloniaFact]
    public void ReadOnly_hides_composer_and_prompt()
    {
        var c = WithRequest();
        var (view, window) = Open(c);
        Assert.True(UiHelpers.Composer(view).IsEffectivelyVisible);
        Assert.True(UiHelpers.Prompt(view).IsEffectivelyVisible);

        view.IsReadOnly = true;
        UiHelpers.Pump();

        Assert.False(UiHelpers.Composer(view).IsEffectivelyVisible);
        Assert.False(UiHelpers.Prompt(view).IsEffectivelyVisible);
        Assert.Single(c.PendingPermissions); // still in the conversation, only not shown
        window.Close();
    }

    [AvaloniaFact]
    public void A_read_only_view_hides_the_plan_card()
    {
        using var doc = JsonDocument.Parse("{\"plan\":\"# Plan\\n\\n1. Do it\"}");
        var c = new ChatConversation();
        c.Apply(new PermissionRequested("r1", "ExitPlanMode", "ExitPlanMode", null, "tool-r1", doc.RootElement.Clone(), null));
        var (view, window) = Open(c);
        var answers = 0;
        c.PermissionAnswered += (_, _) => answers++;
        c.PermissionModeRequested += (_, _) => answers++;
        Assert.True(UiHelpers.Prompt(view).IsEffectivelyVisible);              // the editable view shows the plan card
        Assert.Contains("Accept this plan?", UiHelpers.Texts(UiHelpers.Prompt(view)));

        view.IsReadOnly = true;
        UiHelpers.Pump();

        Assert.False(UiHelpers.Prompt(view).IsEffectivelyVisible);
        Assert.DoesNotContain("Accept this plan?", UiHelpers.Texts(view));
        UiHelpers.Press(window, Avalonia.Input.Key.D1);                         // its keys answer nothing
        UiHelpers.Press(window, Avalonia.Input.Key.Escape);
        Assert.Equal(0, answers);
        Assert.Same(c.PendingPermissions[0], c.CurrentPlan!.Request);          // still in the conversation, only not shown
        window.Close();
    }

    [AvaloniaFact]
    public void ReadOnly_keeps_items_and_scroll()
    {
        var c = WithRequest();
        for (var i = 0; i < 60; i++)
        {
            using var doc = JsonDocument.Parse("{\"command\":\"echo " + i + "\"}");
            c.Apply(new AssistantBlock("m" + (i + 2), null, new ToolUseBlock("t" + (i + 2), "Bash", doc.RootElement.Clone())));
        }
        var (view, window) = Open(c);
        view.IsReadOnly = true;
        UiHelpers.Pump();

        var scroller = UiHelpers.Scroller(view);
        Assert.True(scroller.Extent.Height > scroller.Viewport.Height); // there is something to scroll
        scroller.Offset = new Vector(0, 0);
        UiHelpers.Pump();
        Assert.Equal(0, scroller.Offset.Y);
        scroller.Offset = new Vector(0, 100);
        UiHelpers.Pump();
        Assert.Equal(100, scroller.Offset.Y, 1);
        scroller.Offset = new Vector(0, 40);
        UiHelpers.Pump();
        Assert.Equal(40, scroller.Offset.Y, 1);

        var first = c.Items.OfType<ToolCallItem>().First();
        UiHelpers.ItemList(view).ScrollIntoView(first);
        UiHelpers.Pump();
        Assert.Contains(first.Summary, UiHelpers.Texts(UiHelpers.ItemList(view)));
        window.Close();
    }

    [AvaloniaFact]
    public void A_read_only_view_keeps_copy_and_bookmark_under_a_reply_and_drops_the_thumbs()
    {
        var c = new ChatConversation();
        c.Items.Add(new UserMessageItem("Fix the build"));
        var answer = new AssistantTextItem { Text = "It builds now.", Uuid = "u-1" };
        c.Items.Add(answer);
        c.Items.Add(new TurnFooterItem(1200, 10, 20, 0.01, false));
        var asked = 0;
        c.FeedbackRequested += (_, _) => asked++;
        var (view, window) = Open(c);
        var actions = view.GetVisualDescendants().OfType<ResponseActions>().Single();
        var dialog = view.GetVisualDescendants().OfType<FeedbackDialog>().Single();
        Assert.True(actions.GoodButton.IsEffectivelyVisible);
        UiHelpers.Click(window, actions.BadButton);                     // the box is open when the view turns read-only
        Assert.True(dialog.IsOpen);

        view.IsReadOnly = true;
        UiHelpers.Pump();

        Assert.False(dialog.IsOpen);                                    // a rating cannot go from here: the box closes, sending nothing
        Assert.Equal(1, asked);
        Assert.True(actions.CopyButton.IsEffectivelyVisible);          // copying is the user's own
        Assert.True(actions.BookmarkButton.IsEffectivelyVisible);      // and so is a bookmark, which is local
        Assert.False(actions.GoodButton.IsEffectivelyVisible);
        Assert.False(actions.BadButton.IsEffectivelyVisible);
        UiHelpers.Click(window, actions.BookmarkButton);
        Assert.True(answer.IsBookmarked);
        Assert.False(view.ShowFeedback(answer));                       // nor does the box open by other ways

        view.IsReadOnly = false;
        UiHelpers.Pump();
        Assert.True(actions.GoodButton.IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void Leaving_read_only_restores_composer_with_draft()
    {
        var c = WithRequest();
        var (view, window) = Open(c);
        UiHelpers.Input(view).Focus();
        UiHelpers.Type(window, "draft");

        view.IsReadOnly = true;
        UiHelpers.Pump();
        view.IsReadOnly = false;
        UiHelpers.Pump();

        Assert.True(UiHelpers.Composer(view).IsEffectivelyVisible);
        Assert.True(UiHelpers.Prompt(view).IsEffectivelyVisible);
        Assert.Equal("draft", UiHelpers.Input(view).Text);
        window.Close();
    }

    [AvaloniaFact]
    public void A_read_only_view_shows_no_drop_overlay()
    {
        var c = new ChatConversation();
        var (view, window) = Open(c);
        var overlay = UiHelpers.DropOverlay(view);
        var list = UiHelpers.ItemList(view);
        var file = new AttachmentTests.FakeFile("one.png", UiHelpers.Png(8, 8));
        var files = UiHelpers.Dragged(file);

        // editable: the overlay comes up for a drag that is in progress
        UiHelpers.Drag(DragDrop.DragEnterEvent, files, list);
        Assert.True(overlay.IsVisible);

        // read-only: a drag in progress loses it, and a new one does not bring it back
        view.IsReadOnly = true;
        UiHelpers.Pump();
        Assert.False(overlay.IsVisible);
        UiHelpers.Drag(DragDrop.DragEnterEvent, files, list);
        var over = UiHelpers.Drag(DragDrop.DragOverEvent, files, list);
        UiHelpers.Pump();
        Assert.False(overlay.IsVisible);
        Assert.DoesNotContain("Drop to attach as context", UiHelpers.Texts(view));
        Assert.Equal(DragDropEffects.None, over.DragEffects); // nor does the view accept the drop

        UiHelpers.Drag(DragDrop.DropEvent, files, list);
        UiHelpers.Settle();
        Assert.Empty(c.Attachments);
        Assert.Equal(0, file.Opens); // not even read

        // editable again: the overlay and the drop are back
        view.IsReadOnly = false;
        UiHelpers.Pump();
        UiHelpers.Drag(DragDrop.DragEnterEvent, files, list);
        Assert.True(overlay.IsVisible);
        UiHelpers.Drag(DragDrop.DropEvent, files, list);
        UiHelpers.PumpUntil(() => c.Attachments.Count == 1);
        window.Close();
    }
}
