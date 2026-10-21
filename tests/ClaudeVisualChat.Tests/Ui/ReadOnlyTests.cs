using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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
}
