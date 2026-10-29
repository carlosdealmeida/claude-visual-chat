using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using Xunit;
using AssistantBlock = ClaudeVisualChat.Protocol.AssistantBlock;
using PermissionRequested = ClaudeVisualChat.Protocol.PermissionRequested;
using ToolUseBlock = ClaudeVisualChat.Protocol.ToolUseBlock;
using TurnCompleted = ClaudeVisualChat.Protocol.TurnCompleted;

namespace ClaudeVisualChat.Tests.Ui;

/// <summary>What a screen reader gets: the names of the messages, the text before each prompt, and the live region.</summary>
public class AccessibilityTests
{
    private static JsonElement J(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static LiveAnnouncer Announcer(ClaudeChatView view) =>
        view.GetVisualDescendants().OfType<LiveAnnouncer>().Single();

    private static (ClaudeChatView View, Window Window, LiveAnnouncer Announcer) Open(ChatConversation c, ChatStrings? strings = null)
    {
        var view = new ClaudeChatView { Conversation = c };
        if (strings != null) view.Strings = strings;
        var window = UiHelpers.Show(view);
        return (view, window, Announcer(view));
    }

    /// <summary>Says everything that waits, as the clock would, one sentence at a time.</summary>
    private static List<string> Drain(LiveAnnouncer announcer)
    {
        var said = new List<string>();
        for (var i = 0; i < 20 && announcer.Pending > 0; i++)
        {
            announcer.Tick();
            said.Add(announcer.Spoken);
        }
        return said;
    }

    private static List<string> Names(Visual root) =>
        root.GetVisualDescendants().OfType<Control>().Select(AutomationProperties.GetName).Where(n => !string.IsNullOrEmpty(n)).Select(n => n!).ToList();

    private static ChatConversation Round()
    {
        var c = new ChatConversation();
        c.Items.Add(new UserMessageItem("fix the build"));
        c.Items.Add(new ThinkingItem { Text = "hmm" });
        c.Items.Add(new ToolCallItem { Name = "Bash", Summary = "ls", Status = ToolStatus.Succeeded, ToolUseId = "t1" });
        c.Items.Add(new AssistantTextItem { Text = "Done." });
        c.Items.Add(new TurnFooterItem(1000, 1, 1, 0.01, false));
        return c;
    }

    // ---- names ----

    [AvaloniaFact]
    public void Messages_are_named_you_and_claude_for_screen_readers()
    {
        var (view, window, _) = Open(Round());
        var names = Names(view);

        Assert.Contains("You", names);
        Assert.Contains("Claude", names);
        Assert.Contains("Claude, thinking", names);
        Assert.Contains("Claude, Bash", names);
        window.Close();

        var (pt, ptWindow, _) = Open(Round(), ChatStrings.PortugueseBrazil);
        var ptNames = Names(pt);
        Assert.Contains("Você", ptNames);
        Assert.Contains("Claude, pensando", ptNames);
        Assert.Contains("Claude, Bash", ptNames);
        ptWindow.Close();
    }

    [AvaloniaFact]
    public void A_message_held_while_claude_works_is_you_queued()
    {
        var c = new ChatConversation();
        FocusViewTests.SetBusy(c, true);
        c.Submit("one more thing");
        var (view, window, _) = Open(c);

        Assert.Contains("You, queued", Names(view));
        window.Close();
    }

    [AvaloniaFact]
    public void The_text_before_a_prompt_is_cut_at_120_characters_and_stays_in_the_tree()
    {
        var c = new ChatConversation();
        c.Items.Add(new UserMessageItem(new string('a', 121)));
        c.Items.Add(new UserMessageItem("short\nsecond line"));
        var (view, window, _) = Open(c);

        var hidden = view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("ClaudeChatScreenReaderText")).ToList();
        Assert.Equal(2, hidden.Count);
        Assert.Equal("You: " + new string('a', 120), hidden[0].Text);
        Assert.Equal("You: short second line", hidden[1].Text);
        // shown, with no size: IsVisible=false would take it out of the automation tree
        Assert.All(hidden, t =>
        {
            Assert.True(t.IsVisible);
            Assert.Equal(0, t.Bounds.Width);
            Assert.Equal(0, t.Bounds.Height);
            var peer = ControlAutomationPeer.CreatePeerForElement(t);
            Assert.Equal(t.Text, peer.GetName());
            Assert.True(peer.IsControlElement());
        });
        // and the window's tree reaches it: it is not left out as a hidden control would be
        static bool Reaches(AutomationPeer peer, string name) =>
            peer.GetName() == name || peer.GetChildren().Any(child => Reaches(child, name));
        var root = ControlAutomationPeer.CreatePeerForElement(window);
        Assert.True(Reaches(root, "You: short second line"));
        window.Close();
    }

    // ---- the live region ----

    [AvaloniaFact]
    public void The_announcer_queues_at_most_five_texts_one_every_four_hundred_ms()
    {
        var (_, window, announcer) = Open(new ChatConversation());
        Assert.Equal(TimeSpan.FromMilliseconds(400), LiveAnnouncer.Interval);
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(announcer));
        Assert.False(announcer.IsTicking);

        for (var i = 1; i <= 6; i++) announcer.Announce("text " + i);

        Assert.Equal(5, announcer.Pending);                 // the oldest was dropped
        Assert.True(announcer.IsTicking);
        announcer.Tick();
        Assert.Equal("text 2", AutomationProperties.GetName(announcer));
        Assert.Equal(4, announcer.Pending);                 // one per tick
        Assert.Equal(new[] { "text 3", "text 4", "text 5", "text 6" }, Drain(announcer));
        Assert.False(announcer.IsTicking);                  // nothing waits: no timer
        window.Close();
    }

    [AvaloniaFact]
    public void The_queue_holds_while_a_modal_is_over_the_view_and_resumes_when_it_closes()
    {
        var c = new ChatConversation();
        var (view, window, announcer) = Open(c);
        c.ShowAgents();                                     // the Agent map covers the view
        UiHelpers.Pump();
        announcer.Announce("held");

        announcer.Tick();
        announcer.Tick();
        Assert.Equal(1, announcer.Pending);
        Assert.Equal("", announcer.Spoken);
        Assert.True(announcer.IsTicking);                   // it keeps watching for the modal to close

        c.ClosePanel();
        UiHelpers.Pump();
        announcer.Tick();
        Assert.Equal("held", announcer.Spoken);
        Assert.Equal(0, announcer.Pending);
        window.Close();
    }

    [AvaloniaFact]
    public void The_other_modals_pause_it_too()
    {
        var c = new ChatConversation();
        c.Items.Add(new UserMessageItem("q"));
        c.Items.Add(new ToolCallItem { Name = "Bash", Summary = "ls", Status = ToolStatus.Succeeded, ToolUseId = "t1" });
        var (view, window, announcer) = Open(c);

        foreach (var panel in new[] { ChatPanel.Context, ChatPanel.Usage })
        {
            c.InvokeCommand(panel == ChatPanel.Context ? "context" : "usage", "");
            UiHelpers.Pump();
            Assert.NotEqual(ChatPanel.None, c.ShownPanel);
            announcer.Announce("held " + panel);
            announcer.Tick();
            Assert.Equal(1, announcer.Pending);
            c.ClosePanel();
            UiHelpers.Pump();
            announcer.Tick();
            Assert.Equal("held " + panel, announcer.Spoken);
        }
        window.Close();
    }

    [AvaloniaFact]
    public void Claude_working_and_ready_are_announced_with_the_reply_and_its_code_read_as_a_block()
    {
        var c = new ChatConversation();
        var (_, window, announcer) = Open(c);

        FocusViewTests.SetBusy(c, true);
        Assert.Equal(new[] { "Claude is working." }, Drain(announcer));

        c.Items.Add(new AssistantTextItem { Text = "Here you go:\n```csharp\nvar a = 1;\nvar b = 2;\n```\nAll done." });
        c.Items.Add(new AssistantTextItem { Text = "Final words.\n```\none line\n```" });
        FocusViewTests.SetBusy(c, false);

        Assert.Equal(new[] { "Claude: Final words.\n(code block, 1 line)", "Ready for your input." }, Drain(announcer));
        window.Close();
    }

    [AvaloniaFact]
    public void The_code_of_a_reply_is_read_as_a_block_with_its_lines()
    {
        var strings = ChatStrings.English;
        Assert.Equal("Here:\n(code block, 2 lines)\nAll done.",
            AnnouncementFeed.Speakable("Here:\n```csharp\nvar a = 1;\nvar b = 2;\n```\nAll done.", strings));
        Assert.Equal("(code block, 3 lines)", AnnouncementFeed.Speakable("~~~\na\nb\nc", strings));   // a fence never closed runs to the end
        Assert.Equal("(bloco de código, 12 linhas)",
            AnnouncementFeed.Speakable("```\n" + string.Join("\n", Enumerable.Repeat("x", 12)) + "\n```", ChatStrings.PortugueseBrazil));
    }

    [AvaloniaFact]
    public void Compacting_start_up_hooks_a_queued_message_and_an_error_are_announced()
    {
        var c = new ChatConversation();
        var (_, window, announcer) = Open(c);

        c.Apply(new ClaudeVisualChat.Protocol.StatusChanged("compacting", null));
        c.Apply(new ClaudeVisualChat.Protocol.HookStarted("h1", "SessionStart"));
        Assert.Equal(new[] { "Compacting conversation.", "Running start-up hooks." }, Drain(announcer));

        FocusViewTests.SetBusy(c, true);
        Drain(announcer);
        c.Submit("also this");                              // held until Claude takes it
        Assert.Equal(new[] { "Message queued." }, Drain(announcer));

        c.Apply(new TurnCompleted("error_during_execution", true, 10, 0, 1, 1));
        Assert.Contains("Error: The turn ended with an error", Drain(announcer));
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("Edit", "Claude is requesting permission to use Edit.")]
    [InlineData("ExitPlanMode", "Claude has finished a plan and is waiting for your review.")]
    [InlineData("AskUserQuestion", "Claude is asking you a question.")]
    public void A_permission_request_is_announced(string tool, string expected)
    {
        var c = new ChatConversation();
        var (_, window, announcer) = Open(c);

        c.Apply(new PermissionRequested("r1", tool, tool, null, "t1", J("{}"), null));

        Assert.Equal(new[] { expected }, Drain(announcer));
        window.Close();
    }

    [AvaloniaFact]
    public void A_permission_request_is_announced_in_portuguese_too()
    {
        var c = new ChatConversation();
        var (_, window, announcer) = Open(c, ChatStrings.PortugueseBrazil);

        c.Apply(new PermissionRequested("r1", "Bash", "Bash", null, "t1", J("{}"), null));

        Assert.Equal(new[] { "O Claude pede permissão para usar Bash." }, Drain(announcer));
        window.Close();
    }

    [AvaloniaFact]
    public void Nothing_is_announced_on_replay_or_for_what_the_view_found_already_there()
    {
        var c = new ChatConversation();
        var (_, window, announcer) = Open(c);

        c.LoadTranscript(FixtureFiles.TranscriptLines("error"));   // a history with a failed turn, replies, a footer
        Assert.Equal(0, announcer.Pending);
        Assert.Equal("", announcer.Spoken);
        window.Close();

        // a conversation that is busy, with a request waiting, when the view comes to it: it says nothing of them
        var busy = new ChatConversation();
        FocusViewTests.SetBusy(busy, true);
        busy.Apply(new PermissionRequested("r1", "Edit", "Edit", null, "t1", J("{}"), null));
        var (_, other, again) = Open(busy);
        Assert.Equal(0, again.Pending);
        other.Close();
    }

    [AvaloniaFact]
    public void An_error_is_announced_with_the_message_the_claude_gave()
    {
        var c = new ChatConversation();
        var (_, window, announcer) = Open(c);

        c.Apply(new TurnCompleted("error_during_execution", true, 10, 0, 1, 1) { ErrorMessage = "Credit balance is too low" });
        Assert.Contains("Error: Credit balance is too low", Drain(announcer));

        c.BrowserError = "No browser connected";
        Assert.Equal(new[] { "Error: No browser connected" }, Drain(announcer));
        window.Close();
    }

    [AvaloniaFact]
    public void A_full_queue_drops_the_oldest_so_a_late_request_is_read()
    {
        var c = new ChatConversation();
        var (_, window, announcer) = Open(c);
        for (var i = 1; i <= 5; i++) announcer.Announce("old " + i);

        c.Apply(new PermissionRequested("r1", "Edit", "Edit", null, "t1", J("{}"), null));

        var said = Drain(announcer);
        Assert.Equal(5, said.Count);
        Assert.DoesNotContain("old 1", said);
        Assert.Equal("Claude is requesting permission to use Edit.", said[^1]);
        window.Close();
    }
}
