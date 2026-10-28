using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using SkiaSharp;
using Xunit;
using AgentTaskStarted = ClaudeVisualChat.Protocol.AgentTaskStarted;
using AssistantBlock = ClaudeVisualChat.Protocol.AssistantBlock;
using ContextUsageReported = ClaudeVisualChat.Protocol.ContextUsageReported;
using PermissionRequested = ClaudeVisualChat.Protocol.PermissionRequested;
using PromptCacheUsed = ClaudeVisualChat.Protocol.PromptCacheUsed;
using SessionStarted = ClaudeVisualChat.Protocol.SessionStarted;
using SettingsApplied = ClaudeVisualChat.Protocol.SettingsApplied;
using StatusChanged = ClaudeVisualChat.Protocol.StatusChanged;
using ToolUseBlock = ClaudeVisualChat.Protocol.ToolUseBlock;

namespace ClaudeVisualChat.Tests.Ui;

/// <summary>
/// The view taken out of its host and put back: leaving lets go of the conversation without touching what is drawn
/// (the host's presenter is in the middle of swapping its child), and coming back draws the conversation as it is now.
/// </summary>
public class DetachTests
{
    private static readonly IReadOnlyList<ChatModel> Models = new[]
    {
        new ChatModel("opus", "claude-opus-5-5", "Opus 5.5", "For complex work and everyday tasks", new[] { "low", "medium", "high" }),
    };

    private static JsonElement J(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static byte[] Png(int w, int h)
    {
        using var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        bmp.Erase(new SKColor(30, 120, 200));
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>A conversation with a mode the claude reported, so the reply box's mode list is filled.</summary>
    private static ChatConversation Conversation()
    {
        var conversation = new ChatConversation { AvailableModels = Models };
        conversation.Apply(new SessionStarted("s1", "opus", null, "default", "2.1.306"));
        conversation.Apply(new SettingsApplied(null, "high", true));
        conversation.Apply(new StatusChanged(null, "default"));
        return conversation;
    }

    /// <summary>All of the bar and the view on screen: each control has something drawn to take apart.</summary>
    private static ChatConversation Busy()
    {
        var c = Conversation();
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("t1", "Edit", J("{\"file_path\":\"a.txt\",\"old_string\":\"a\",\"new_string\":\"b\"}"))));
        c.Submit("go on");
        c.Apply(new PermissionRequested("r1", "Edit", "Edit", null, "t1", J("{\"file_path\":\"a.txt\",\"old_string\":\"a\",\"new_string\":\"b\"}"), null));
        c.Apply(new PromptCacheUsed(TimeSpan.FromHours(1), null));
        c.BrowserStatus = ChatBrowserStatus.Connecting;
        c.Attachments.Add(ChatImage.FromBytes(Png(8, 8), "image/png"));
        c.InvokeCommand("usage", "");
        return c;
    }

    /// <summary>
    /// <see cref="Busy"/> with the controls of 0.4.0 on screen too: an agent at work and the Agent map open over the
    /// view (its timer running), the context ring, a sent message with an image and a file in its attachments row, and
    /// a file chip in the reply box.
    /// </summary>
    private static ChatConversation BusyWithAgents()
    {
        var c = Busy();
        c.Apply(new AssistantBlock("m2", null, new ToolUseBlock("toolu_a", "Agent", J("{\"description\":\"Look around\",\"subagent_type\":\"Explore\"}"))));
        c.Apply(new AgentTaskStarted("task1", "toolu_a", "Look around", "Explore", true, 1, "local_agent"));
        c.Apply(new ContextUsageReported(new ContextUsage(140_000, 200_000, 70, Array.Empty<ContextCategory>())));
        Assert.Equal(AddFileResult.Added, c.AddFile("sent.txt", System.Text.Encoding.UTF8.GetBytes("sent")));
        c.Submit("with a file"); // takes Busy's image too: the message's row has a thumbnail and a file
        Assert.Equal(AddFileResult.Added, c.AddFile("notes.txt", System.Text.Encoding.UTF8.GetBytes("notes")));
        c.Files[0].Progress = 0.5;
        c.ShowAgents();
        return c;
    }

    private static ContentPresenter Presenter(ContentControl host) =>
        host.GetVisualDescendants().OfType<ContentPresenter>().First();

    /// <summary>A page of the host app: the content is the page, and a template draws it.</summary>
    private sealed class ChatPage
    {
        public ChatPage(ChatConversation conversation) => Conversation = conversation;

        public ChatConversation Conversation { get; }
    }

    private sealed class OtherPage
    {
    }

    /// <summary>
    /// As a host's shell does: a ContentControl whose content is a page, drawn by a template. Swapping the page, the
    /// presenter takes the view out of the visual tree while it is still in the logical one.
    /// </summary>
    private static ContentControl Shell(ChatConversation conversation) => new ContentControl
    {
        DataTemplates =
        {
            new FuncDataTemplate<ChatPage>((page, _) => new ClaudeChatView { Conversation = page?.Conversation }),
            new FuncDataTemplate<OtherPage>((_, _) => new TextBlock { Text = "Tokens" }),
        },
        Content = new ChatPage(conversation),
    };

    private static void SwapsOut(ChatConversation conversation)
    {
        var host = Shell(conversation);
        var window = UiHelpers.Show(host);
        var view = Assert.IsType<ClaudeChatView>(Presenter(host).Child);
        UiHelpers.Input(view).Focus(); // the user clicked in the reply box
        UiHelpers.Pump();

        host.Content = new OtherPage();
        UiHelpers.Pump();

        var shown = Assert.IsType<TextBlock>(Presenter(host).Child);
        Assert.Equal("Tokens", shown.Text);
        Assert.Contains(shown, Presenter(host).GetVisualChildren());
        window.Close();
    }

    [AvaloniaFact]
    public void A_host_swapping_the_view_out_gets_its_new_content()
    {
        SwapsOut(Conversation());
    }

    [AvaloniaFact]
    public void A_host_swapping_out_a_busy_view_gets_its_new_content()
    {
        SwapsOut(Busy());
    }

    [AvaloniaFact]
    public void A_host_swapping_out_a_view_with_the_agent_map_open_gets_its_new_content()
    {
        var c = BusyWithAgents();
        var host = Shell(c);
        var window = UiHelpers.Show(host);
        var view = Assert.IsType<ClaudeChatView>(Presenter(host).Child);
        UiHelpers.Pump();
        // the 0.4.0 controls are on screen
        var map = view.GetVisualDescendants().OfType<AgentMapPanel>().Single();
        Assert.True(map.IsEffectivelyVisible);
        Assert.True(map.IsTicking);
        Assert.True(view.GetVisualDescendants().OfType<AgentsChip>().Single().IsVisible);
        Assert.True(view.GetVisualDescendants().OfType<ContextRing>().Single().IsVisible);
        Assert.Contains(view.GetVisualDescendants().OfType<MessageAttachments>(), r => r.IsVisible);
        Assert.Equal(2, view.GetVisualDescendants().OfType<FileChipContent>().Count()); // the sent one and the box's
        Handlers(c, out var shown);

        host.Content = new OtherPage();
        UiHelpers.Pump();

        var other = Assert.IsType<TextBlock>(Presenter(host).Child);
        Assert.Equal("Tokens", other.Text);
        Assert.Contains(other, Presenter(host).GetVisualChildren());
        Assert.False(map.IsTicking);
        Handlers(c, out var left);
        Assert.True(left < shown);
        window.Close();
    }

    // ---- out and back ----

    private sealed class Mounted
    {
        public Mounted(ChatConversation conversation)
        {
            Conversation = conversation;
            View = new ClaudeChatView { Conversation = conversation };
            Host = new ContentControl { Content = View };
            Window = UiHelpers.Show(Host);
            Composer = UiHelpers.Composer(View);
        }

        public ChatConversation Conversation { get; }
        public ClaudeChatView View { get; }
        public ContentControl Host { get; }
        public Window Window { get; }
        public ChatComposer Composer { get; }

        public ComboBox ModePicker => Composer.GetVisualDescendants().OfType<ComboBox>().Single();

        public PromptCacheClock Clock => Composer.GetVisualDescendants().OfType<PromptCacheClock>().Single();

        public void Out()
        {
            Host.Content = new TextBlock { Text = "Tokens" };
            UiHelpers.Pump();
        }

        public void In()
        {
            Host.Content = View;
            UiHelpers.Pump();
        }
    }

    private static Panel WorkingRow(ClaudeChatView view) =>
        view.GetVisualDescendants().OfType<Panel>().Single(p => p.Classes.Contains("ClaudeChatWorking"));

    [AvaloniaFact]
    public void Coming_back_draws_what_changed_while_out()
    {
        var m = new Mounted(Conversation());
        var strings = m.View.Strings;
        m.Out();

        var c = m.Conversation;
        c.Apply(new StatusChanged(null, "plan"));
        c.Apply(new SettingsApplied(null, "low", true));
        c.Submit("go on");
        c.BrowserStatus = ChatBrowserStatus.Connected;
        c.Apply(new PromptCacheUsed(TimeSpan.FromHours(1), null));
        c.Attachments.Add(ChatImage.FromBytes(Png(8, 8), "image/png"));
        UiHelpers.Pump();

        m.In();
        Assert.Contains("Plan", UiHelpers.Texts(m.ModePicker));
        Assert.Contains("plan", m.Composer.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("ClaudeChatComposer")).Classes);
        var chip = m.Composer.GetVisualDescendants().OfType<ModelChip>().Single();
        Assert.Contains(strings.EffortLabel("low"), UiHelpers.Texts(chip));
        Assert.Contains(strings.Stop, UiHelpers.ButtonNames(m.Composer));
        Assert.True(m.View.GetVisualDescendants().OfType<BrowserBanner>().Single().IsEffectivelyVisible);
        Assert.Contains(strings.BrowserConnected, UiHelpers.Texts(m.View));
        Assert.True(m.Clock.IsEffectivelyVisible);
        Assert.True(m.Clock.IsTicking);
        Assert.Single(m.Composer.GetVisualDescendants().OfType<AttachmentStrip>().Single()
            .GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("ClaudeChatAttachment"));
        Assert.True(WorkingRow(m.View).IsVisible);

        // and it follows the conversation again
        c.Apply(new StatusChanged(null, "acceptEdits"));
        c.Attachments.Clear();
        UiHelpers.Pump();
        Assert.Contains("Edit automatically", UiHelpers.Texts(m.ModePicker));
        Assert.False(m.Composer.GetVisualDescendants().OfType<AttachmentStrip>().Single().IsVisible);
        m.Window.Close();
    }

    [AvaloniaFact]
    public void A_request_shown_before_comes_back_with_the_pause_started_over()
    {
        var c = Conversation();
        c.Apply(new PermissionRequested("r1", "Bash", "Bash", null, "t1", J("{\"command\":\"ls\"}"), null));
        var m = new Mounted(c);
        var prompt = UiHelpers.Prompt(m.View);
        var changes = new List<bool>();
        prompt.RequestChanged += changes.Add;

        m.Out();
        Assert.Empty(changes); // out of the window, the view hears nothing
        m.In();
        Assert.Single(changes); // back, the request comes on screen again
        Assert.Same(c.CurrentPermission, prompt.Request);
        Assert.True(prompt.IsEffectivelyVisible);
        m.Window.Close();
    }

    [AvaloniaFact]
    public void A_request_waiting_for_the_typing_stops_its_timer_out_of_the_window_and_waits_again_when_back()
    {
        var now = new DateTimeOffset(2026, 10, 27, 10, 0, 0, TimeSpan.Zero);
        var c = Conversation();
        c.Clock = () => now;
        var m = new Mounted(c);
        var prompt = UiHelpers.Prompt(m.View);
        UiHelpers.Input(m.View).Focus();
        UiHelpers.Type(m.Window, "half a thought");
        c.Apply(new PermissionRequested("r1", "Bash", "Bash", null, "t1", J("{\"command\":\"ls\"}"), null));
        UiHelpers.Pump();
        Assert.True(prompt.IsTicking);                                       // it waits for the typing to pause
        Assert.Contains(m.View.Strings.WaitingForPermission, UiHelpers.Texts(m.View));

        m.Out();
        Assert.False(prompt.IsTicking);                                      // out of the window, the timer stops

        m.In();                                                              // back, still typed a moment ago: it waits again
        Assert.True(prompt.IsTicking);
        Assert.Contains(m.View.Strings.WaitingForPermission, UiHelpers.Texts(m.View));
        now = now.AddSeconds(2);
        prompt.Tick();
        UiHelpers.Pump();
        Assert.Contains("ls", UiHelpers.Texts(prompt));                      // and the card comes
        Assert.DoesNotContain(m.View.Strings.WaitingForPermission, UiHelpers.Texts(m.View));
        m.Window.Close();
    }

    [AvaloniaFact]
    public void Option_two_keeps_its_rule_label_and_tooltips_out_of_the_window_and_back()
    {
        var c = Conversation();
        c.Apply(new PermissionRequested("r1", "Bash", "Bash", null, "t1", J("{\"command\":\"npm test\"}"),
            J("[{\"type\":\"addRules\",\"rules\":[{\"toolName\":\"Bash\",\"ruleContent\":\"npm:*\"},{\"toolName\":\"Bash\",\"ruleContent\":\"ls\"},{\"toolName\":\"Bash\",\"ruleContent\":\"pwd\"}],\"destination\":\"userSettings\"}]")));
        var m = new Mounted(c);
        string Label() => string.Concat(UiHelpers.Prompt(m.View).GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Classes.Contains("ClaudeChatOptionText") && t.Inlines is { Count: > 0 })
            .SelectMany(t => t.Inlines!).Select(i => i is Avalonia.Controls.Documents.Run r ? r.Text
                : i is Avalonia.Controls.Documents.InlineUIContainer { Child: TextBlock hint } ? hint.Text : ""));
        List<string?> Tips() => UiHelpers.Prompt(m.View).GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Classes.Contains("ClaudeChatAllowAlwaysHint")).Select(t => ToolTip.GetTip(t) as string).ToList();
        Assert.Equal("Yes, allow npm and 2 more for all projects", Label());

        m.Out();                                                           // taken out: nothing throws, the tree is let go
        m.In();                                                            // back: the same label, with its tooltips
        Assert.Equal("Yes, allow npm and 2 more for all projects", Label());
        Assert.Equal(new[] { "• ls\n• pwd", "Saves to ~/.claude/settings.json" }, Tips());
        m.Window.Close();
    }

    [AvaloniaFact]
    public void A_folded_card_comes_back_folded_after_the_view_returns()
    {
        var c = Conversation();
        c.Apply(new PermissionRequested("r1", "Bash", "Bash", null, "t1", J("{\"command\":\"ls -la\"}"), null));
        var m = new Mounted(c);
        var prompt = UiHelpers.Prompt(m.View);
        UiHelpers.Pump();
        var fold = prompt.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("ClaudeChatPermissionFold"));
        UiHelpers.Click(m.Window, fold);
        Assert.True(prompt.IsFolded);

        m.Out();                                                           // taken out folded: nothing throws
        Assert.False(prompt.IsTicking);
        m.In();                                                            // back: the same request, still folded

        Assert.True(prompt.IsFolded);
        Assert.Same(c.CurrentPermission, prompt.Request);
        Assert.DoesNotContain("ls -la", UiHelpers.Texts(prompt));
        Assert.Contains("Allow this bash command?", UiHelpers.Texts(prompt));
        Assert.Equal("Expand", Avalonia.Automation.AutomationProperties.GetName(fold));
        UiHelpers.Click(m.Window, fold);                                   // and it unfolds as ever
        Assert.False(prompt.IsFolded);
        Assert.Contains("ls -la", UiHelpers.Texts(prompt));

        // a request that came while it was out is another one: it shows unfolded
        UiHelpers.Click(m.Window, fold);
        Assert.True(prompt.IsFolded);
        m.Out();
        c.AnswerPermission(c.CurrentPermission!, PermissionDecision.Deny, null);
        c.Apply(new PermissionRequested("r2", "Bash", "Bash", null, "t2", J("{\"command\":\"pwd\"}"), null));
        m.In();
        Assert.False(prompt.IsFolded);
        Assert.Contains("pwd", UiHelpers.Texts(prompt));
        m.Window.Close();
    }

    [AvaloniaFact]
    public void A_plan_card_that_came_while_out_shows_when_back_with_the_modes_set_meanwhile()
    {
        var c = Conversation();
        var m = new Mounted(c);
        var prompt = UiHelpers.Prompt(m.View);

        m.Out();
        c.Apply(new PermissionRequested("r1", "ExitPlanMode", "ExitPlanMode", null, "t1", J("{\"plan\":\"# Plan\"}"), null));
        m.View.AvailableModes = new[] { "plan", "default", "acceptEdits" }; // no auto, set while out
        Assert.False(prompt.IsTicking);
        m.In();

        Assert.Same(c.CurrentPermission, prompt.Request);
        var texts = UiHelpers.Texts(prompt);
        Assert.Contains("Accept this plan?", texts);
        Assert.Contains("Yes, and auto-accept", texts);
        Assert.DoesNotContain("Yes, and use auto mode", texts);
        m.Window.Close();
    }

    [AvaloniaFact]
    public void The_plan_document_comes_back_after_the_view_returns()
    {
        var c = Conversation();
        var own = Handlers(c);
        c.Apply(new PermissionRequested("r1", "ExitPlanMode", "ExitPlanMode", null, "t1", J("{\"plan\":\"# Rewrite the parser\\n\\n1. Read it\"}"), null));
        var m = new Mounted(c);
        var document = m.View.GetVisualDescendants().OfType<PlanDocumentPanel>().Single();
        Assert.True(document.IsOpen);

        m.Out();                                                           // taken out with the document open: nothing throws
        Assert.Equal(own, Handlers(c));                                    // and it let go of the conversation with the rest
        m.In();                                                            // back: still open, on the same plan
        Assert.True(document.IsOpen);
        Assert.Contains("Rewrite the parser", UiHelpers.Texts(document));
        Assert.Contains(m.View.Strings.PlanReadyForReview, UiHelpers.Texts(document));

        // approved while out: closed when back; a plan that came while out: open when back
        m.Out();
        c.ApprovePlan(c.CurrentPermission!, "default");
        m.In();
        Assert.False(document.IsOpen);
        m.Out();
        c.Apply(new PermissionRequested("r2", "ExitPlanMode", "ExitPlanMode", null, "t2", J("{\"plan\":\"# Use a list\"}"), null));
        m.In();
        Assert.True(document.IsOpen);
        Assert.Contains("Use a list", UiHelpers.Texts(document));

        // and it follows the conversation again
        c.ApprovePlan(c.CurrentPermission!, "default");
        UiHelpers.Pump();
        Assert.False(document.IsOpen);
        m.Window.Close();
    }

    /// <summary>Handlers on the comments of <paramref name="plan"/>: the document's and the card's, while they are in the window.</summary>
    private static int CommentHandlers(PlanReview plan)
    {
        var field = typeof(System.Collections.ObjectModel.ObservableCollection<PlanComment>)
            .GetField("CollectionChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        return ((Delegate?)field.GetValue(plan.Comments))?.GetInvocationList().Length ?? 0;
    }

    [AvaloniaFact]
    public void The_plan_comment_box_and_the_comments_come_back_after_the_view_returns()
    {
        var c = Conversation();
        var own = Handlers(c);
        c.Apply(new PermissionRequested("r1", "ExitPlanMode", "ExitPlanMode", null, "t1", J("{\"plan\":\"# Rewrite the parser\\n\\nIntro line.\"}"), null));
        var m = new Mounted(c);
        var plan = c.CurrentPlan!;
        var box = m.View.GetVisualDescendants().OfType<PlanCommentBox>().Single();
        var prompt = UiHelpers.Prompt(m.View);
        plan.AddComment("Intro", "Rewrite the parser", "Say more");
        box.Open(new PlanCommentRequest(plan, "Intro line.", new Avalonia.Rect(24, 60, 60, 22)));
        box.Field.Text = "half a thought";
        UiHelpers.Pump();
        Assert.Equal(2, CommentHandlers(plan));                            // the document and the card listen to the comments

        m.Out();                                                           // taken out with the box open: nothing throws
        Assert.Equal(own, Handlers(c));                                    // and it let go of the conversation…
        Assert.Equal(0, CommentHandlers(plan));                            // …and of the plan's comments
        plan.AddComment("line", null, "While out");                        // a comment added meanwhile
        m.In();                                                            // back: the box as it was, the card with both comments
        Assert.True(box.IsOpen);
        Assert.Equal("half a thought", box.Field.Text);
        Assert.Equal(2, CommentHandlers(plan));
        Assert.Contains(m.View.Strings.FormatPlanComments(2), UiHelpers.Texts(prompt));

        // the plan answered while out: the box is closed when back
        m.Out();
        c.ApprovePlan(c.CurrentPermission!, "default");
        m.In();
        Assert.False(box.IsOpen);
        Assert.Equal(1, CommentHandlers(plan));                           // the card let go of it (the document keeps its last plan)
        m.Window.Close();
    }

    /// <summary>Handlers on <paramref name="item"/>'s property changes (an item, an agent): the rows that draw it, while they are in the window.</summary>
    private static int ItemHandlers(ChatObservableObject item)
    {
        for (var type = item.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField("PropertyChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly);
            if (field != null) return ((Delegate?)field.GetValue(item))?.GetInvocationList().Length ?? 0;
        }
        throw new InvalidOperationException("no PropertyChanged event");
    }

    [AvaloniaFact]
    public void A_tool_output_opened_before_or_while_out_is_drawn_as_the_item_says_when_back()
    {
        var c = Conversation();
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("t1", "Bash", J("{\"command\":\"seq 20\"}"))));
        c.Apply(new ClaudeVisualChat.Protocol.UserBlock(null,
            new ClaudeVisualChat.Protocol.ToolResultBlock("t1", string.Join("\n", Enumerable.Range(1, 20).Select(i => "line " + i)), false), null, false, false));
        var tool = c.Items.OfType<ToolCallItem>().Single();
        var m = new Mounted(c);
        string Result() => m.View.GetVisualDescendants().OfType<SelectableTextBlock>().Single(t => (t.Text ?? "").StartsWith("line 1", StringComparison.Ordinal)).Text!;
        var attached = ItemHandlers(tool);
        Assert.DoesNotContain("line 20", Result());

        tool.IsOutputExpanded = true;                                      // opened in the window
        UiHelpers.Pump();
        Assert.Contains("line 20", Result());

        m.Out();                                                           // taken out opened: nothing throws, and the item is let go
        Assert.True(ItemHandlers(tool) < attached);
        tool.IsOutputExpanded = false;                                     // cut while out
        tool.IsInputExpanded = true;
        m.In();                                                            // back: drawn as the item is now
        Assert.Equal(attached, ItemHandlers(tool));
        Assert.DoesNotContain("line 20", Result());
        tool.IsOutputExpanded = true;                                      // and it answers again
        UiHelpers.Pump();
        Assert.Contains("line 20", Result());
        m.Window.Close();
    }

    /// <summary>Draws frames until <paramref name="done"/> (an animation of the render clock), 5 s at most.</summary>
    private static void FrameUntil(Func<bool> done)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && clock.Elapsed < TimeSpan.FromSeconds(5))
        {
            System.Threading.Thread.Sleep(20);
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            UiHelpers.Pump();
        }
        Assert.True(done());
    }

    [AvaloniaFact]
    public void A_long_prompt_folding_while_the_view_leaves_stops_and_comes_back_as_the_item_says()
    {
        var c = Conversation();
        var prompt = new UserMessageItem(string.Join("\n", Enumerable.Range(1, 10).Select(i => "line " + i)));
        c.Items.Add(prompt);
        var m = new Mounted(c);
        FoldedText Fold() => m.View.GetVisualDescendants().OfType<FoldedText>().Single(f => ReferenceEquals(f.DataContext, prompt));
        var fold = Fold();
        Assert.Equal(60, fold.Bounds.Height, 0.5);

        UiHelpers.Click(m.Window, fold.ShowMoreButton);                   // opening, and taken out in the middle of it
        Assert.True(fold.IsAnimatingHeight);
        m.Out();                                                          // nothing throws, and the animation stops
        Assert.False(fold.IsAnimatingHeight);
        Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick(); // a late frame finds nothing to do
        UiHelpers.Pump();
        prompt.IsTextExpanded = false;                                    // folded again while out
        m.In();                                                           // back: drawn as the item is now, at once
        fold = Fold();
        Assert.False(fold.IsAnimatingHeight);
        Assert.Equal(60, fold.Bounds.Height, 0.5);
        Assert.True(fold.ShowMoreButton.IsVisible);

        UiHelpers.Click(m.Window, fold.ShowMoreButton);                   // and it answers again
        FrameUntil(() => !fold.IsAnimatingHeight);
        Assert.True(fold.Bounds.Height > 150);
        m.Window.Close();
    }

    [AvaloniaFact]
    public void The_sticky_prompt_lets_go_while_the_view_is_away()
    {
        var c = Conversation();
        var first = new UserMessageItem("First question");
        c.Items.Add(first);
        for (var i = 0; i < 40; i++) c.Items.Add(new AssistantTextItem { Text = "Paragraph " + i });
        var own = ItemHandlers(first);
        var m = new Mounted(c);
        var list = UiHelpers.ItemList(m.View);
        var scroller = UiHelpers.Scroller(m.View);
        StickyPrompt Sticky() => m.View.GetVisualDescendants().OfType<StickyPrompt>().Single();
        list.ScrollIntoView(c.Items[20]);
        UiHelpers.Pump();
        scroller.Offset = new Avalonia.Vector(0, list.ContainerFromItem(c.Items[20])!.TranslatePoint(default, list)!.Value.Y);
        UiHelpers.Pump();
        var sticky = Sticky();
        Assert.True(sticky.IsVisible);
        Assert.Same(first, sticky.Item);
        var pinned = ItemHandlers(first);
        Assert.True(pinned > own);                                        // the pinned bubble follows its prompt (opened or folded)

        // a click waits 300 ms to scroll to the turn: taken out meanwhile, the timer stops and nothing scrolls
        var text = sticky.Bubble.GetVisualDescendants().OfType<SelectableTextBlock>().Single(t => t.Text == "First question");
        UiHelpers.Click(m.Window, text);
        Assert.True(sticky.IsWaiting);
        m.Out();                                                          // nothing throws
        Assert.False(sticky.IsWaiting);
        Assert.True(ItemHandlers(first) < pinned);                        // and it lets go of the prompt
        UiHelpers.Settle();
        first.IsTextExpanded = true;                                      // changed while out
        c.Items.Add(new AssistantTextItem { Text = "One more paragraph" });

        m.In();                                                           // back: pinned again, as the prompt is now
        UiHelpers.Pump();
        sticky = Sticky();
        Assert.True(sticky.IsVisible);
        Assert.Same(first, sticky.Item);
        Assert.Equal(pinned, ItemHandlers(first));
        Assert.True(sticky.Bubble.GetVisualDescendants().OfType<FoldedText>().Single().IsExpanded);
        first.IsTextExpanded = false;                                     // and it follows the prompt again
        UiHelpers.Pump();
        Assert.False(sticky.Bubble.GetVisualDescendants().OfType<FoldedText>().Single().IsExpanded);
        m.Window.Close();
    }

    [AvaloniaFact]
    public void A_background_agents_line_lets_go_of_its_agent_out_of_the_window_and_tells_what_changed_when_back()
    {
        var c = Conversation();
        c.AppendLine(AgentMapTests.AgentCall("toolu_a1", "Write a file"));
        c.AppendLine(AgentMapTests.TaskStarted("ta1", "toolu_a1", "Write a file"));
        c.AppendLine(AgentMapTests.BashCall("toolu_b1", parent: "toolu_a1"));
        var agent = c.Agents.Single();
        var own = ItemHandlers(agent);                                     // the conversation's own, before any view
        var m = new Mounted(c);
        var strings = m.View.Strings;
        string Line() => m.View.GetVisualDescendants().OfType<Panel>().Single(p => p.Classes.Contains("ClaudeChatAgentLine"))
            .GetVisualDescendants().OfType<TextBlock>().Single().Text!;
        Assert.True(ItemHandlers(agent) > own);                            // the row follows its agent
        Assert.StartsWith(strings.AgentWorkingInBackground, Line());

        m.Out();                                                           // out: nothing throws, and the row lets go of the agent
        Assert.Equal(own, ItemHandlers(agent));
        c.AppendLine(AgentMapTests.BashRequest("r1", "toolu_b1", "ta1")); // it asks while out
        UiHelpers.Pump();
        Assert.True(agent.IsWaitingForPermission);

        m.In();                                                            // back: drawn as the agent is now
        Assert.True(ItemHandlers(agent) > own);
        Assert.StartsWith(strings.AgentWaitingForPermission, Line());
        c.AnswerPermission(c.CurrentPermission!, PermissionDecision.Allow); // and it follows again
        UiHelpers.Pump();
        Assert.StartsWith(strings.AgentWorkingInBackground, Line());
        m.Window.Close();
    }

    [AvaloniaFact]
    public void A_copy_check_timer_stops_when_the_view_leaves()
    {
        var gate = new System.Threading.Tasks.TaskCompletionSource<bool>();
        var copied = new List<string>();
        ChatClipboard.Override = text => { copied.Add(text); return gate.Task; };
        try
        {
            var c = Conversation();
            c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("t1", "Bash", J("{\"command\":\"seq 20\\nls\"}"))));
            var tool = c.Items.OfType<ToolCallItem>().Single();
            var m = new Mounted(c);
            ToolCallView Row() => m.View.GetVisualDescendants().OfType<ToolCallView>().Single();
            Button Copy() => Row().GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("ClaudeChatCopyCode"));
            Avalonia.Media.Geometry? IconData() => Copy().GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single().Data;
            var attached = ItemHandlers(tool);

            // the clipboard answers after the view left: the check is not shown for a view that is out
            UiHelpers.Click(m.Window, Copy());
            Assert.Equal(new[] { "seq 20\nls" }, copied);
            var row = Row();
            m.Out();
            gate.SetResult(true);
            UiHelpers.Pump();
            Assert.False(row.IsCopied);
            Assert.False(row.IsCopyTicking);
            Assert.True(ItemHandlers(tool) < attached);

            // a check on screen goes when the view leaves: no timer ticks out of the window, and back it is a copy icon again
            m.In();
            Assert.Equal(attached, ItemHandlers(tool));
            var copyIcon = IconData();
            ChatClipboard.Override = text => { copied.Add(text); return System.Threading.Tasks.Task.CompletedTask; };
            UiHelpers.Click(m.Window, Copy());
            Assert.True(Row().IsCopied);
            Assert.True(Row().IsCopyTicking);
            Assert.NotSame(copyIcon, IconData());

            m.Out();
            Assert.False(row.IsCopied);
            Assert.False(row.IsCopyTicking);
            row.EndCopied();                                               // a late tick finds nothing to do
            m.In();
            Assert.False(Row().IsCopied);
            Assert.False(Row().IsCopyTicking);
            Assert.Same(copyIcon, IconData());
            Assert.Equal("Copy code to clipboard", Avalonia.Automation.AutomationProperties.GetName(Copy()));
            UiHelpers.Click(m.Window, Copy());                             // and it copies again
            Assert.True(Row().IsCopied);
            Assert.Equal(3, copied.Count);

            // a list that reuses the view for another call drops the check without a sound
            var other = new ToolCallItem { Name = "Bash", Summary = "ls", Status = ToolStatus.Succeeded };
            Row().DataContext = other;
            UiHelpers.Pump();
            Assert.False(Row().IsCopied);
            Assert.False(Row().IsCopyTicking);
            m.Window.Close();
        }
        finally { ChatClipboard.Override = null; }
    }

    [AvaloniaFact]
    public void A_copy_is_asked_of_the_view_only_while_it_is_in()
    {
        var gate = new System.Threading.Tasks.TaskCompletionSource<bool>();
        var copied = new List<string>();
        ChatClipboard.Override = text => { copied.Add(text); return System.Threading.Tasks.Task.CompletedTask; };
        try
        {
            var c = Conversation();
            c.Items.Add(new UserMessageItem("question"));
            c.Items.Add(new AssistantTextItem { Text = "the answer" });
            c.Items.Add(new TurnFooterItem(null, null, null, null, false));
            var m = new Mounted(c);

            m.Out();                                                       // out: nobody writes to a clipboard, and nothing is said
            c.Submit("/copy");
            UiHelpers.Pump();
            Assert.Empty(copied);
            Assert.Empty(c.Items.OfType<MetaLineItem>());

            m.In();                                                        // back: it copies, and the line is drawn
            c.Submit("/copy");
            UiHelpers.Pump();
            Assert.Equal(new[] { "the answer" }, copied);
            Assert.Equal("Copied to clipboard (10 characters, 1 line)", Assert.Single(c.Items.OfType<MetaLineItem>()).Text);
            Assert.Contains(m.View.GetVisualDescendants().OfType<SelectableTextBlock>(), t => t.Classes.Contains("ClaudeChatMetaLine"));

            // the clipboard answers after the view left: the line is the conversation's, so it is said, and drawn when the view is back
            ChatClipboard.Override = text => { copied.Add(text); return gate.Task; };
            c.Submit("/copy");
            m.Out();
            gate.SetResult(true);
            UiHelpers.Pump();
            Assert.Equal(2, c.Items.OfType<MetaLineItem>().Count());
            m.In();
            Assert.Equal(2, m.View.GetVisualDescendants().OfType<SelectableTextBlock>().Count(t => t.Classes.Contains("ClaudeChatMetaLine")));
            m.Window.Close();
        }
        finally { ChatClipboard.Override = null; }
    }

    [AvaloniaFact]
    public void The_code_block_copy_check_timer_stops_when_the_view_leaves()
    {
        var gate = new System.Threading.Tasks.TaskCompletionSource<bool>();
        var copied = new List<string>();
        ChatClipboard.Override = text => { copied.Add(text); return gate.Task; };
        try
        {
            var c = Conversation();
            var answer = new AssistantTextItem { Text = "Run it:\n\n```cs\nvar x = 1;\n```" };
            c.Items.Add(answer);
            var m = new Mounted(c);
            CodeCopyButton Copy() => m.View.GetVisualDescendants().OfType<CodeCopyButton>().Single();
            Avalonia.Media.Geometry? IconData() => Copy().GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single().Data;

            // the clipboard answers after the view left: the check is not shown for a view that is out
            UiHelpers.Click(m.Window, Copy());
            Assert.Equal(new[] { "var x = 1;" }, copied);
            var button = Copy();
            m.Out();
            gate.SetResult(true);
            UiHelpers.Pump();
            Assert.False(button.IsCopied);
            Assert.False(button.IsTicking);

            // a check on screen goes when the view leaves: no timer ticks out of the window, and back it is a copy icon again
            m.In();
            var copyIcon = IconData();
            ChatClipboard.Override = text => { copied.Add(text); return System.Threading.Tasks.Task.CompletedTask; };
            UiHelpers.Click(m.Window, Copy());
            Assert.True(Copy().IsCopied);
            Assert.True(Copy().IsTicking);
            Assert.NotSame(copyIcon, IconData());

            button = Copy();
            m.Out();
            Assert.False(button.IsCopied);
            Assert.False(button.IsTicking);
            button.EndCopied();                                            // a late tick finds nothing to do
            m.In();
            Assert.False(Copy().IsCopied);
            Assert.False(Copy().IsTicking);
            Assert.Same(copyIcon, IconData());
            Assert.Equal("Copy code to clipboard", Avalonia.Automation.AutomationProperties.GetName(Copy()));

            // the item's text changes (a recycled row shows another answer): the new block has a button of its own, with no check
            UiHelpers.Click(m.Window, Copy());
            Assert.True(Copy().IsCopied);
            answer.Text = "Another:\n\n```\nls\n```";
            Wait(() => Copy().IsCopied == false && !Copy().IsTicking);
            UiHelpers.DrawFrames();
            UiHelpers.Click(m.Window, Copy());
            Assert.Equal("ls", copied[copied.Count - 1]);
            m.Window.Close();
        }
        finally { ChatClipboard.Override = null; }
    }

    private static void Wait(Func<bool> done)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && clock.Elapsed < TimeSpan.FromSeconds(5))
        {
            System.Threading.Thread.Sleep(20);
            UiHelpers.Pump();
        }
        Assert.True(done());
    }

    [AvaloniaFact]
    public void A_chrome_screenshot_and_its_preview_come_back_after_the_view_returns()
    {
        var c = Conversation();
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("t1", "mcp__claude-in-chrome__computer", J("{\"action\":\"screenshot\"}"))));
        c.Apply(new ClaudeVisualChat.Protocol.UserBlock(null, new ClaudeVisualChat.Protocol.ToolResultBlock("t1", "ok", false)
        {
            Images = new[] { new ClaudeVisualChat.Protocol.ImageBlock("image/png", Convert.ToBase64String(Png(300, 100))) },
        }, null, false, false));
        var tool = c.Items.OfType<ToolCallItem>().Single();
        var m = new Mounted(c);
        Button Thumb() => m.View.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("ClaudeChatMessageImage"));
        var preview = m.View.GetVisualDescendants().OfType<ImagePreview>().Single();
        var inList = ItemHandlers(tool);
        Assert.NotNull(Thumb());

        UiHelpers.Click(m.Window, Thumb());
        Assert.True(preview.IsEffectivelyVisible);
        m.Out();                                                           // out with the preview open: nothing throws
        Assert.Equal(0, ItemHandlers(tool));                               // and the row lets go of the call
        tool.ResultImages = null;                                          // changed while out
        m.In();                                                            // back: drawn as the call is now
        Assert.Equal(inList, ItemHandlers(tool));
        Assert.DoesNotContain(m.View.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("ClaudeChatMessageImage"));
        tool.ResultImages = new[] { ChatImage.FromBytes(Png(40, 80), "image/png") };
        UiHelpers.Pump();
        Assert.Equal(42, Thumb().Bounds.Width, 1);                         // and a new screenshot shows again
        m.Window.Close();
    }

    [AvaloniaFact]
    public void The_response_actions_stop_their_check_and_let_go_of_the_reply_when_the_view_leaves()
    {
        var gate = new System.Threading.Tasks.TaskCompletionSource<bool>();
        var copied = new List<string>();
        ChatClipboard.Override = text => { copied.Add(text); return gate.Task; };
        try
        {
            var c = Conversation();
            c.Items.Add(new UserMessageItem("Fix it"));
            var answer = new AssistantTextItem { Text = "Fixed.", Uuid = "u-1" };
            c.Items.Add(answer);
            c.Items.Add(new TurnFooterItem(1000, 1, 1, null, false));
            var own = ItemHandlers(answer);
            var hostOwn = HostHandlers(c);
            var m = new Mounted(c);
            ResponseActions Actions() => m.View.GetVisualDescendants().OfType<ResponseActions>().Single();
            Avalonia.Media.Geometry? IconData() => Actions().CopyButton.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single().Data;
            Assert.True(ItemHandlers(answer) > own);                         // the row follows its reply (the bookmark, the rating)
            Assert.True(HostHandlers(c) > hostOwn);                          // and whether the host takes feedback and opens links

            // the clipboard answers after the view left: the check is not shown for a view that is out
            UiHelpers.Click(m.Window, Actions().CopyButton);
            Assert.Equal(new[] { "Fixed." }, copied);
            var actions = Actions();
            m.Out();
            Assert.Equal(own, ItemHandlers(answer));                         // out: it lets go of the reply
            Assert.Equal(hostOwn, HostHandlers(c));                          // and of the conversation
            gate.SetResult(true);
            UiHelpers.Pump();
            Assert.False(actions.IsCopied);
            Assert.False(actions.IsTicking);

            // a check on screen goes when the view leaves, and back it is a copy icon again
            answer.IsBookmarked = true;                                      // marked while out
            c.FeedbackRequested += (_, _) => { };                            // and the host began to take feedback
            m.In();
            Assert.True(ItemHandlers(answer) > own);
            Assert.True(HostHandlers(c) > hostOwn);
            Assert.Contains("marked", Actions().BookmarkButton.Classes);     // drawn as the reply is now
            Assert.True(Actions().GoodButton.IsEffectivelyVisible);
            var copyIcon = IconData();
            ChatClipboard.Override = text => { copied.Add(text); return System.Threading.Tasks.Task.CompletedTask; };
            UiHelpers.Click(m.Window, Actions().CopyButton);
            Assert.True(Actions().IsCopied);
            Assert.True(Actions().IsTicking);
            Assert.NotSame(copyIcon, IconData());

            actions = Actions();
            m.Out();
            Assert.False(actions.IsCopied);
            Assert.False(actions.IsTicking);
            actions.EndCopied();                                             // a late tick finds nothing to do
            m.In();
            Assert.False(Actions().IsCopied);
            Assert.Same(copyIcon, IconData());
            Assert.Equal("Copy response to clipboard", Avalonia.Automation.AutomationProperties.GetName(Actions().CopyButton));

            // a list that reuses the row for another turn drops the check without a sound
            var current = Actions();
            UiHelpers.Click(m.Window, current.CopyButton);
            Assert.True(current.IsCopied);
            current.DataContext = new TurnFooterItem(1, 1, 1, null, false);
            UiHelpers.Pump();
            Assert.False(current.IsCopied);
            Assert.False(current.IsTicking);
            m.Window.Close();
        }
        finally { ChatClipboard.Override = null; }
    }

    /// <summary>How many listen to the conversation's internal <c>HostHandlersChanged</c> (the rows under the replies).</summary>
    private static int HostHandlers(ChatConversation conversation)
    {
        var field = typeof(ChatConversation).GetField("HostHandlersChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        return ((Delegate?)field!.GetValue(conversation))?.GetInvocationList().Length ?? 0;
    }

    [AvaloniaFact]
    public void What_went_wrong_comes_back_after_the_view_returns()
    {
        var c = Conversation();
        c.Items.Add(new UserMessageItem("Fix it"));
        var answer = new AssistantTextItem { Text = "Fixed.", Uuid = "u-1" };
        c.Items.Add(answer);
        c.Items.Add(new TurnFooterItem(1000, 1, 1, null, false));
        var asked = new List<FeedbackRequestedEventArgs>();
        c.FeedbackRequested += (_, e) => asked.Add(e);
        var m = new Mounted(c);
        var dialog = m.View.GetVisualDescendants().OfType<FeedbackDialog>().Single();
        Assert.True(m.View.ShowFeedback(answer));
        dialog.Field.Text = "half a thought";
        UiHelpers.Pump();

        m.Out();                                                           // taken out with the box open: nothing throws
        m.In();                                                            // back: the box as it was
        Assert.True(dialog.IsOpen);
        Assert.Equal("half a thought", dialog.Field.Text);
        UiHelpers.DrawFrames();
        UiHelpers.Click(m.Window, dialog.SendButton);                      // and it sends as ever
        Assert.False(dialog.IsOpen);
        Assert.Equal("half a thought", Assert.Single(asked).Details);
        m.Window.Close();
    }

    [AvaloniaFact]
    public void The_diff_view_comes_back_after_the_view_returns()
    {
        var c = Conversation();
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("t1", "Edit", J("{\"file_path\":\"C:/work/src/a.cs\",\"old_string\":\"x\",\"new_string\":\"y\"}"))));
        var tool = c.Items.OfType<ToolCallItem>().Single();
        var m = new Mounted(c);
        var panel = m.View.GetVisualDescendants().OfType<DiffViewPanel>().Single();
        var inList = ItemHandlers(tool);                                    // the row's
        Assert.True(m.View.ShowDiff(tool));
        UiHelpers.Pump();
        Assert.True(panel.IsOpen);
        Assert.Equal(inList + 1, ItemHandlers(tool));                       // the Diff View follows its call

        m.Out();                                                           // taken out with the Diff View open: nothing throws
        Assert.Equal(0, ItemHandlers(tool));                               // and the row and the Diff View let go of the call
        tool.Input = J("{\"file_path\":\"C:/work/src/b.cs\",\"old_string\":\"x\",\"new_string\":\"z\"}"); // changed while out
        m.In();                                                            // back: still open, drawn as the call is now
        Assert.True(panel.IsOpen);
        Assert.Equal(inList + 1, ItemHandlers(tool));
        Assert.Contains("C:/work/src/b.cs", UiHelpers.Texts(panel));
        Assert.Contains("z", UiHelpers.Texts(panel));
        var close = UiHelpers.ButtonWithText(panel, m.View.Strings.Close);
        Assert.True(close.IsEffectivelyVisible);
        Assert.Same(close, UiHelpers.Focused(m.Window));                   // back in the window, the keyboard is on the x

        UiHelpers.DrawFrames();                                            // the window hit-tests the last frame drawn
        UiHelpers.Click(m.Window, close);                                  // and it closes as ever
        Assert.False(panel.IsOpen);
        Assert.Equal(inList, ItemHandlers(tool));
        m.Window.Close();
    }

    [AvaloniaFact]
    public void The_diff_view_does_not_take_the_keyboard_from_the_host_when_back()
    {
        var c = Conversation();
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("t1", "Edit", J("{\"file_path\":\"C:/work/src/a.cs\",\"old_string\":\"x\",\"new_string\":\"y\"}"))));
        var tool = c.Items.OfType<ToolCallItem>().Single();
        var view = new ClaudeChatView { Conversation = c };
        var slot = new ContentControl { Content = view };
        var hostBox = new TextBox();                                       // the host's own field, beside the view
        var window = UiHelpers.Show(new StackPanel { Children = { hostBox, slot } });
        var panel = view.GetVisualDescendants().OfType<DiffViewPanel>().Single();
        Assert.True(view.ShowDiff(tool));
        UiHelpers.Pump();
        Assert.True(panel.IsOpen);

        slot.Content = null;                                               // out with the Diff View open
        UiHelpers.Pump();
        hostBox.Focus();
        Assert.Same(hostBox, UiHelpers.Focused(window));
        slot.Content = view;                                               // back: the host's field keeps the keyboard
        UiHelpers.Pump();
        Assert.True(panel.IsOpen);
        Assert.Same(hostBox, UiHelpers.Focused(window));
        window.Close();
    }

    [AvaloniaFact]
    public void A_request_that_came_while_out_shows_when_back()
    {
        var m = new Mounted(Conversation());
        var prompt = UiHelpers.Prompt(m.View);
        m.Out();
        m.Conversation.Apply(new PermissionRequested("r1", "Bash", "Bash", null, "t1", J("{\"command\":\"ls -la\"}"), null));
        UiHelpers.Pump();
        Assert.False(prompt.IsVisible);

        m.In();
        Assert.Same(m.Conversation.CurrentPermission, prompt.Request);
        Assert.True(prompt.IsEffectivelyVisible);
        Assert.Contains("ls -la", UiHelpers.Texts(prompt));
        m.Window.Close();
    }

    // ---- in and out without a word in the log ----

    [AvaloniaTheory]
    [InlineData(ChatLayout.Desktop, false)]
    [InlineData(ChatLayout.Desktop, true)]
    [InlineData(ChatLayout.Touch, false)]
    [InlineData(ChatLayout.Touch, true)]
    public void Coming_in_and_going_out_logs_no_binding_warning(ChatLayout layout, bool readOnly)
    {
        TestLog.Instance.Clear();
        var view = new ClaudeChatView { Conversation = Busy(), Layout = layout, IsReadOnly = readOnly };
        var host = new ContentControl { Content = view };
        var window = UiHelpers.Show(host);
        Assert.Equal(!readOnly, UiHelpers.Composer(view).IsEffectivelyVisible);

        host.Content = new TextBlock { Text = "Tokens" };                   // the host takes the view out…
        UiHelpers.Pump();
        host.Content = view;                                                 // …puts it back…
        UiHelpers.Pump();
        Assert.Equal(!readOnly, UiHelpers.Composer(view).IsEffectivelyVisible);
        view.IsReadOnly = !readOnly;                                         // …it follows its property as before…
        UiHelpers.Pump();
        Assert.Equal(readOnly, UiHelpers.Composer(view).IsEffectivelyVisible);
        window.Close();                                                      // …and the window goes with the view in it
        UiHelpers.Pump();
        Assert.Empty(TestLog.Instance.Entries);

        // a host that draws its page with a template and binds the view to it: the page going away takes the data context too
        var page = new ChatPage(Busy());
        var shell = new ContentControl
        {
            DataTemplates =
            {
                new FuncDataTemplate<ChatPage>((_, _) =>
                {
                    var templated = new ClaudeChatView { Layout = layout, IsReadOnly = readOnly };
                    templated[!ClaudeChatView.ConversationProperty] = new Avalonia.Data.Binding(nameof(ChatPage.Conversation));
                    return templated;
                }),
            },
            Content = page,
        };
        window = UiHelpers.Show(shell);
        Assert.Same(page.Conversation, Assert.IsType<ClaudeChatView>(Presenter(shell).Child).Conversation);
        shell.Content = null;
        UiHelpers.Pump();
        shell.Content = page;
        UiHelpers.Pump();
        window.Close();
        UiHelpers.Pump();
        Assert.Empty(TestLog.Instance.Entries);
    }

    // ---- out, the conversation no longer reaches the view ----

    /// <summary>
    /// Handlers on the conversation's PropertyChanged, on its attachments', files' and agents' CollectionChanged, and
    /// on each agent's and file's PropertyChanged (the reply box's files and the sent messages').
    /// </summary>
    private static int Handlers(ChatConversation c)
    {
        const System.Reflection.BindingFlags field = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        int Count(object target, Type owner, string name) =>
            ((Delegate?)owner.GetField(name, field)!.GetValue(target))?.GetInvocationList().Length ?? 0;
        var count = Count(c, typeof(ChatObservableObject), "PropertyChanged")
                    + Count(c.Attachments, typeof(System.Collections.ObjectModel.ObservableCollection<ChatImage>), "CollectionChanged")
                    + Count(c.Files, typeof(System.Collections.ObjectModel.ObservableCollection<ChatFile>), "CollectionChanged")
                    + Count(c.Agents, typeof(System.Collections.ObjectModel.ObservableCollection<AgentInfo>), "CollectionChanged");
        foreach (var agent in c.Agents) count += Count(agent, typeof(ChatObservableObject), "PropertyChanged");
        var files = c.Files.Concat(c.Items.OfType<UserMessageItem>().SelectMany(m => m.Files)).Distinct();
        foreach (var file in files) count += Count(file, typeof(ChatObservableObject), "PropertyChanged");
        return count;
    }

    private static void Handlers(ChatConversation c, out int count) => count = Handlers(c);

    [AvaloniaFact]
    public void Out_of_the_window_the_conversation_no_longer_reaches_the_view()
    {
        var c = Conversation();
        var own = Handlers(c); // the conversation's own, before any view
        c.Apply(new PromptCacheUsed(TimeSpan.FromHours(1), null));
        c.Attachments.Add(ChatImage.FromBytes(Png(8, 8), "image/png"));
        var m = new Mounted(c);
        Assert.True(Handlers(c) > own);
        Assert.True(m.Clock.IsTicking);
        var picker = m.ModePicker;
        var modes = picker.ItemsSource;
        var strip = m.Composer.GetVisualDescendants().OfType<AttachmentStrip>().Single();

        m.Out();
        Assert.Equal(own, Handlers(c)); // every control let go, the strip too
        Assert.False(m.Clock.IsTicking);
        Assert.Same(modes, picker.ItemsSource); // leaving drew nothing

        c.Apply(new StatusChanged(null, "plan"));
        c.Submit("go on");
        c.Attachments.Add(ChatImage.FromBytes(Png(8, 8), "image/png"));
        UiHelpers.Pump();
        Assert.Same(modes, picker.ItemsSource);
        Assert.DoesNotContain("Plan", UiHelpers.Texts(picker));
        Assert.Single(strip.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("ClaudeChatAttachment"));
        Assert.False(m.Clock.IsTicking);
        m.Window.Close();
    }

    [AvaloniaFact]
    public void Out_of_the_window_the_0_4_controls_let_go_of_the_conversation_its_agents_and_its_files()
    {
        var c = BusyWithAgents();
        var own = Handlers(c); // the conversation's own, before any view
        var m = new Mounted(c);
        var map = m.View.GetVisualDescendants().OfType<AgentMapPanel>().Single();
        Assert.True(map.IsTicking);
        Assert.True(Handlers(c) > own);

        m.Out();
        Assert.Equal(own, Handlers(c)); // the chip, the ring, the map, the strip and every file chip let go
        Assert.False(map.IsTicking);

        // what changes while out is drawn when back
        c.Apply(new AgentTaskStarted("task2", "toolu_b", "Look again", "Explore", true, 1, "local_agent"));
        c.Files[0].Progress = null;
        UiHelpers.Pump();
        m.In();
        Assert.True(map.IsTicking);
        Assert.Contains("2 agents", UiHelpers.Texts(m.Composer.GetVisualDescendants().OfType<AgentsChip>().Single()));
        Assert.Equal(2, map.GetVisualDescendants().OfType<Button>().Count(b => b.Classes.Contains("ClaudeChatAgentRow") && b.IsEffectivelyVisible));
        m.Window.Close();
    }
}
