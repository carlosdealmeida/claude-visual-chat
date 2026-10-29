using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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
        Assert.True(fold.IsBackstopWaiting);
        m.Out();                                                          // nothing throws, and the animation stops
        Assert.False(fold.IsAnimatingHeight);
        Assert.False(fold.IsBackstopWaiting);                             // and so does the timer that would end it
        fold.EndAnimation();                                              // a late tick finds nothing to do
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

    /// <summary>Whether <paramref name="view"/> itself listens to the conversation's items changing.</summary>
    private static bool ListensToItems(ChatConversation c, ClaudeChatView view)
    {
        var field = typeof(System.Collections.ObjectModel.ObservableCollection<ChatItem>)
            .GetField("CollectionChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var handlers = (Delegate?)field!.GetValue(c.Items);
        return handlers != null && handlers.GetInvocationList().Any(d => ReferenceEquals(d.Target, view));
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
        Assert.True(ListensToItems(c, m.View));                           // the view follows the list, for the search of the pinned prompt
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
        Assert.False(ListensToItems(c, m.View));                          // and the view of the list
        UiHelpers.Settle();
        first.IsTextExpanded = true;                                      // changed while out
        c.Items.Add(new AssistantTextItem { Text = "One more paragraph" });

        m.In();                                                           // back: pinned again, as the prompt is now
        UiHelpers.Pump();
        sticky = Sticky();
        Assert.True(sticky.IsVisible);
        Assert.Same(first, sticky.Item);
        Assert.Equal(pinned, ItemHandlers(first));
        Assert.True(ListensToItems(c, m.View));
        Assert.True(sticky.Bubble.GetVisualDescendants().OfType<FoldedText>().Single().IsExpanded);
        first.IsTextExpanded = false;                                     // and it follows the prompt again
        UiHelpers.Pump();
        Assert.False(sticky.Bubble.GetVisualDescendants().OfType<FoldedText>().Single().IsExpanded);
        m.Window.Close();
    }

    private static DateTimeOffset LocalTime(int year, int month, int day, int hour)
    {
        var time = new DateTime(year, month, day, hour, 0, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(time, TimeZoneInfo.Local.GetUtcOffset(time));
    }

    [AvaloniaFact]
    public void A_date_line_lets_go_of_the_list_and_its_item_out_of_the_window_and_comes_back_as_they_are()
    {
        var c = Conversation();
        c.Clock = () => LocalTime(2026, 10, 26, 12);
        var prompt = new UserMessageItem("first") { Timestamp = LocalTime(2026, 10, 25, 9) };
        c.Items.Add(prompt);
        var own = ItemHandlers(prompt);
        var m = new Mounted(c);
        List<string?> Lines() => m.View.GetVisualDescendants().OfType<DateSeparator>().Where(d => d.IsEffectivelyVisible)
            .OrderBy(d => d.TranslatePoint(default, m.View)!.Value.Y).Select(d => d.Date).ToList();
        var line = m.View.GetVisualDescendants().OfType<DateSeparator>().Single(d => ReferenceEquals(d.DataContext, prompt));
        Assert.Equal(new[] { "Sun, Oct 25" }, Lines());
        Assert.True(line.IsTracking);
        Assert.True(ItemHandlers(prompt) > own);                          // it follows its item's time

        m.Out();                                                          // nothing throws
        Assert.False(line.IsTracking);                                    // it lets go of the list
        Assert.Equal(own, ItemHandlers(prompt));                          // and of its item
        Assert.Equal("Sun, Oct 25", line.Date);                           // and leaving drew nothing
        // changed while out: a message of the same day before it, and the view's language
        c.Items.Insert(0, new UserMessageItem("earlier") { Timestamp = LocalTime(2026, 10, 25, 8) });
        m.View.Strings = ChatStrings.PortugueseBrazil;
        UiHelpers.Pump();

        m.In();                                                           // back: drawn as the list is now
        Assert.Equal(new[] { "dom, 25 de out" }, Lines());                // over the earlier one, not over this one
        Assert.True(ItemHandlers(prompt) > own);
        prompt.Timestamp = LocalTime(2026, 10, 26, 9);                    // and it follows its item again
        UiHelpers.Pump();
        Assert.Equal(new[] { "dom, 25 de out", "seg, 26 de out" }, Lines());
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

    /// <summary>Who listens to the queue (a notice on a held message comes as the queue's change too).</summary>
    private static int QueueHandlers(ChatConversation c) =>
        ((Delegate?)typeof(System.Collections.ObjectModel.ObservableCollection<UserMessageItem>)
            .GetField("CollectionChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(c.QueuedMessages))?.GetInvocationList().Length ?? 0;

    [AvaloniaFact]
    public void The_queued_messages_let_go_while_out_and_come_back_as_they_are_now()
    {
        var c = Conversation();
        var sent = new List<MessageSubmittedEventArgs>();
        c.MessageSubmitted += (_, e) => sent.Add(e);
        c.Submit("first");
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("t1", "Bash", J("{\"command\":\"sleep 5\"}"))));
        c.Submit("held one");
        var own = QueueHandlers(c);
        var m = new Mounted(c);
        var list = m.View.GetVisualDescendants().OfType<QueuedMessageList>().Single();
        Assert.Equal(new[] { "held one" }, UiHelpers.Texts(list));
        Assert.True(QueueHandlers(c) > own);

        m.Out();                                                           // taken out: nothing throws, the queue is let go
        Assert.Equal(own, QueueHandlers(c));
        c.Submit("held two");                                              // while out: one more, the first one taken,
        c.AppendLine("{\"type\":\"command_lifecycle\",\"command_uuid\":\"" + sent[1].Uuid + "\",\"state\":\"completed\"}");
        c.AppendLine("{\"type\":\"command_lifecycle\",\"command_uuid\":\"" + sent[2].Uuid + "\",\"state\":\"queued\",\"ran_ahead\":true}"); // the other ran at once
        UiHelpers.Pump();

        m.In();                                                            // back: the queue as it is now
        Assert.Equal(new[] { "held two", m.View.Strings.RanAtOnce }, UiHelpers.Texts(list));
        Assert.True(QueueHandlers(c) > own);

        // and it follows the conversation again
        c.AppendLine("{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"duration_ms\":10,\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}");
        UiHelpers.Pump();
        Assert.False(list.IsVisible);
        m.Window.Close();
    }

    [AvaloniaFact]
    public void The_thinking_count_stops_its_timer_when_the_view_leaves_and_comes_back_right()
    {
        long now = 1000;
        TokenCountUp.ClockOverride = () => now;
        try
        {
            var c = Conversation();
            c.Apply(new ClaudeVisualChat.Protocol.BlockStarted("m1", 0, null, new ClaudeVisualChat.Protocol.ThinkingBlock("")));
            var thought = c.Items.OfType<ThinkingItem>().Single();
            c.Apply(new ClaudeVisualChat.Protocol.ThinkingTokens(2000));
            var own = ItemHandlers(thought);
            var m = new Mounted(c);
            ThinkingLabel Label() => m.View.GetVisualDescendants().OfType<ThinkingLabel>().Single();
            var label = Label();
            Assert.True(ItemHandlers(thought) > own);                          // the line follows its thought
            Assert.True(label.IsTicking);                                      // and the count is climbing
            now += 600;
            label.Tick();
            Assert.Equal("Thinking... · 1.0k tokens", label.Text);

            m.Out();                                                           // taken out in the middle of the climb: nothing throws,
            Assert.False(label.IsTicking);                                     // no timer ticks out of the window,
            Assert.False(label.IsTracking);
            Assert.Equal(own, ItemHandlers(thought));                          // and the thought is let go
            label.Tick();                                                      // a late tick finds nothing to do
            Assert.False(label.IsTicking);
            now += 10_000;                                                     // a longer wait out, and the count changed meanwhile
            c.Apply(new ClaudeVisualChat.Protocol.ThinkingTokens(3000));
            UiHelpers.Pump();
            Assert.Equal("Thinking... · 1.0k tokens", label.Text);            // leaving drew nothing, and nothing was drawn while out

            m.In();                                                            // back: drawn as it is now, climbing from where it was
            label = Label();                                                   // (the list built its rows again)
            Assert.True(ItemHandlers(thought) > own);
            Assert.True(label.IsTicking);
            Assert.Equal("Thinking... · 2.0k tokens", label.Text);
            now += 3000;
            label.Tick();
            Assert.Equal("Thinking... · 3.0k tokens", label.Text);
            Assert.False(label.IsTicking);

            m.Out();                                                           // out when it has settled: back, it rests
            m.In();
            label = Label();
            Assert.False(label.IsTicking);
            Assert.Equal("Thinking... · 3.0k tokens", label.Text);

            // the thought ends while out: back, it says how long it took, with no timer
            c.Apply(new ClaudeVisualChat.Protocol.ThinkingTokens(4000));
            Assert.True(label.IsTicking);
            m.Out();
            Assert.False(label.IsTicking);
            c.Apply(new AssistantBlock("m1", null, new ClaudeVisualChat.Protocol.ThinkingBlock("hm")) { ThinkingDurationMs = 5000 });
            m.In();
            label = Label();
            Assert.False(label.IsTicking);
            Assert.Equal("Thought for 5s", label.Text);
            m.Window.Close();
        }
        finally { TokenCountUp.ClockOverride = null; }
    }

    [AvaloniaFact]
    public void The_working_indicator_stops_its_timers_when_the_view_leaves()
    {
        long now = 1000;
        TokenCountUp.ClockOverride = () => now;
        try
        {
            var c = Conversation();
            c.Submit("go");
            var m = new Mounted(c);
            var row = m.View.GetVisualDescendants().OfType<WorkingIndicator>().Single();
            Assert.True(row.IsGlyphMoving);                                    // the star moves
            Assert.True(row.IsTicking);                                        // and the verb waits for its time
            var verb = row.Verb;
            now += 10_000;
            row.Tick();                                                        // the verb changed meanwhile
            var changed = row.Verb;
            Assert.NotEqual(verb, changed);

            m.Out();                                                           // taken out with the line showing: nothing throws,
            Assert.False(row.IsGlyphMoving);                                   // and no timer runs out of the window
            Assert.False(row.IsTicking);
            row.StepGlyph();                                                   // late ticks find nothing to do
            now += 100;
            row.Tick();
            Assert.Equal(changed, row.Verb);
            Assert.False(row.IsGlyphMoving);
            Assert.False(row.IsTicking);
            c.Apply(new StatusChanged("compacting", null));                    // what changes meanwhile is drawn when it is back
            c.Apply(new StatusChanged(null, null));
            UiHelpers.Pump();

            m.In();                                                            // back: as it was, and the timers run again
            Assert.True(row.IsEffectivelyVisible);
            Assert.Equal(changed + "...", row.FullText);
            Assert.True(row.IsGlyphMoving);
            Assert.True(row.IsTicking);

            m.Out();                                                           // out again with the line in the middle of a state
            c.Apply(new StatusChanged("compacting", null));
            m.In();
            Assert.Equal("Compacting...", row.FullText);
            Assert.True(row.IsGlyphMoving);

            m.Out();                                                           // out when the line is over: back, it is hidden and still
            c.Apply(new ClaudeVisualChat.Protocol.TurnCompleted("success", false, 10, 0, 1, 1));
            m.In();
            Assert.False(row.IsEffectivelyVisible);
            Assert.False(row.IsGlyphMoving);
            Assert.False(row.IsTicking);
            m.Window.Close();
        }
        finally { TokenCountUp.ClockOverride = null; }
    }

    [AvaloniaFact]
    public void A_divider_lets_go_of_its_item_out_of_the_window_and_comes_back_as_it_is()
    {
        var text = string.Join(" ", System.Linq.Enumerable.Repeat("a long line that a command printed", 12));
        var item = new DividerItem(text);
        var c = Conversation();
        c.Items.Add(item);
        var own = ItemHandlers(item);
        var m = new Mounted(c);
        DividerLine Line() => m.View.GetVisualDescendants().OfType<DividerLine>().Single();
        var line = Line();
        Assert.True(line.IsTracking);
        Assert.True(ItemHandlers(item) > own);
        UiHelpers.Click(m.Window, line);                                   // opened
        Assert.True(item.IsOpen);

        m.Out();                                                           // taken out open: nothing throws, and the item is let go
        Assert.False(line.IsTracking);
        Assert.Equal(own, ItemHandlers(item));
        item.IsOpen = false;                                               // closed while out
        UiHelpers.Pump();

        m.In();                                                            // back: drawn as the item is now
        line = Line();
        Assert.True(line.IsTracking);
        Assert.True(ItemHandlers(item) > own);
        Assert.Equal(Avalonia.Media.TextWrapping.NoWrap, line.Label.TextWrapping);
        UiHelpers.Click(m.Window, line);                                   // and it opens, and follows its item again
        Assert.True(item.IsOpen);
        item.IsOpen = false;
        UiHelpers.Pump();
        Assert.Equal(Avalonia.Media.TextWrapping.NoWrap, line.Label.TextWrapping);
        m.Window.Close();
    }

    [AvaloniaFact]
    public void A_compaction_card_lets_go_of_its_item_out_of_the_window_and_comes_back_as_it_is()
    {
        var item = new CompactionItem { Trigger = "manual", TokensBefore = 36326, Summary = "the first summary" };
        var c = Conversation();
        c.Items.Add(item);
        var own = ItemHandlers(item);
        var m = new Mounted(c);
        CompactionCard Card() => m.View.GetVisualDescendants().OfType<CompactionCard>().Single();
        var card = Card();
        Assert.True(card.IsTracking);
        Assert.True(ItemHandlers(item) > own);
        UiHelpers.Click(m.Window, card.Head);                              // opened
        Assert.True(item.IsExpanded);
        Assert.Contains("the first summary", UiHelpers.MarkdownText(card));

        m.Out();                                                           // taken out open: nothing throws, and the item is let go
        Assert.False(card.IsTracking);
        Assert.Equal(own, ItemHandlers(item));
        item.Summary = "the second summary";                               // changed while out
        m.View.Strings = ChatStrings.PortugueseBrazil;
        UiHelpers.Pump();

        m.In();                                                            // back: drawn as the item is now
        card = Card();                                                     // (the list built its rows again)
        Assert.True(card.IsTracking);
        Assert.True(ItemHandlers(item) > own);
        Assert.Equal("Conversa compactada · manual · 36k tokens liberados", card.Label.Text);
        Assert.True(card.Summary.IsVisible);
        Assert.Contains("the second summary", UiHelpers.MarkdownText(card));
        UiHelpers.Click(m.Window, card.Head);                              // and it closes, and follows its item again
        Assert.False(item.IsExpanded);
        item.IsExpanded = true;
        UiHelpers.Pump();
        Assert.True(card.Summary.IsVisible);
        m.Window.Close();
    }

    [AvaloniaFact]
    public void An_external_message_card_lets_go_of_its_item_out_of_the_window_and_comes_back_as_it_is()
    {
        var item = new ExternalMessageItem("cross-session")
        {
            Sender = "maria", SenderName = "Maria Souza", Preview = "run the tests", Body = "run the tests\nplease",
        };
        var c = Conversation();
        c.Items.Add(item);
        var own = ItemHandlers(item);
        var m = new Mounted(c);
        ExternalMessageCard Card() => m.View.GetVisualDescendants().OfType<ExternalMessageCard>().Single();
        var card = Card();
        Assert.True(card.IsTracking);
        Assert.True(ItemHandlers(item) > own);
        UiHelpers.Click(m.Window, card.Head);                              // opened
        Assert.True(item.IsExpanded);
        Assert.Contains("run the tests\nplease", UiHelpers.Texts(card));

        m.Out();                                                           // taken out open: nothing throws, and the item is let go
        Assert.False(card.IsTracking);
        Assert.Equal(own, ItemHandlers(item));
        m.View.Strings = ChatStrings.PortugueseBrazil;                     // changed while out
        UiHelpers.Pump();

        m.In();                                                            // back: drawn as the item is now
        card = Card();                                                     // (the list built its rows again)
        Assert.True(card.IsTracking);
        Assert.True(ItemHandlers(item) > own);
        Assert.Equal("Mensagem de @maria:", card.Label.Text);
        Assert.Contains("De Maria Souza", UiHelpers.Texts(card));
        Assert.Contains("run the tests\nplease", UiHelpers.Texts(card));
        UiHelpers.Click(m.Window, card.Head);                              // and it closes, and follows its item again
        Assert.False(item.IsExpanded);
        item.IsExpanded = true;
        UiHelpers.Pump();
        Assert.Contains("run the tests\nplease", UiHelpers.Texts(card));
        m.Window.Close();
    }

    [AvaloniaFact]
    public void The_invisible_characters_notice_comes_back_as_it_was_and_follows_the_conversation_again()
    {
        var m = new Mounted(Conversation());
        var input = UiHelpers.Input(m.View);
        var notice = m.Composer.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "Notice");
        input.Text = "oi" + char.ConvertFromUtf32(0x200B);
        input.Focus();
        UiHelpers.Press(m.Window, Avalonia.Input.Key.Enter);
        Assert.True(notice.IsEffectivelyVisible);

        m.Out();
        m.In();

        // it has no timer and no event of the host's: it is as it was, and still follows the request that hides it
        Assert.True(notice.IsEffectivelyVisible);
        Assert.Equal("oi", input.Text);
        Assert.Contains("Removed 1 invisible character \u00B7 review and press Enter to send", UiHelpers.Texts(m.Composer));
        m.Conversation.Apply(new PermissionRequested("r1", "Bash", "Bash", null, "t1", System.Text.Json.JsonDocument.Parse("{\"command\":\"ls\"}").RootElement.Clone(), null));
        UiHelpers.Pump();
        Assert.False(notice.IsVisible);
        m.Out();
        m.Conversation.AnswerPermission(m.Conversation.CurrentPermission!, PermissionDecision.Allow);   // answered while out
        m.In();
        Assert.True(notice.IsEffectivelyVisible);
        UiHelpers.Click(m.Window, m.Composer.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NoticeClose"));
        Assert.False(notice.IsVisible);
        m.Window.Close();
    }

    [AvaloniaFact]
    public void The_argument_hint_and_the_open_palette_come_back_right_after_the_view_returns()
    {
        var m = new Mounted(Conversation());
        m.Conversation.Commands = new[] { new ChatCommand("review", "Review a pull request", "<pr>") };
        var input = UiHelpers.Input(m.View);
        var hint = m.Composer.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "ArgumentHint");
        input.Text = "/review ";
        UiHelpers.Pump();
        Assert.True(hint.IsVisible);

        // the commands change while the view is away: nothing follows them there, and the hint is right when the view is back
        m.Out();
        m.Conversation.Commands = new[] { new ChatCommand("review", "Review a pull request", "") };
        m.In();
        Assert.False(hint.IsVisible);
        m.Out();
        m.Conversation.Commands = new[] { new ChatCommand("review", "Review a pull request", "<pull request>") };
        m.In();
        Assert.True(hint.IsVisible);
        Assert.Equal("/review <pull request>", string.Concat(hint.Inlines!.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text)));
        m.Conversation.Commands = new ChatCommand[0];     // and it follows them again
        UiHelpers.Pump();
        Assert.False(hint.IsVisible);

        // the palette open when the view leaves is closed, with a row that keeps it open too; back, it opens again as it did
        input.Text = "";
        var palette = Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(m.Composer).OfType<CommandPalette>().Single();
        var kept = new PaletteAction(PaletteSection.Settings, "Detach switch", _ => { }, _ => true);
        CommandPalette.Actions.Add(kept);
        try
        {
            m.Composer.OpenCommands();
            Assert.True(palette.IsOpen);
            m.Out();
            Assert.False(palette.IsOpen);
            m.In();
            Assert.False(palette.IsOpen);
            m.Composer.OpenCommands();
            UiHelpers.Pump();
            Assert.True(palette.IsOpen);
            Assert.Contains("Detach switch", UiHelpers.Texts(palette.Child!));
        }
        finally
        {
            CommandPalette.Actions.Remove(kept);
        }
        m.Window.Close();
    }

    [AvaloniaFact]
    public void The_drop_overlay_lets_go_when_the_view_leaves_and_works_again_when_back()
    {
        var c = Conversation();
        var m = new Mounted(c);
        var overlay = UiHelpers.DropOverlay(m.View);
        var file = new AttachmentTests.FakeFile("one.png", Png(8, 8));
        var files = UiHelpers.Dragged(file);
        UiHelpers.Drag(DragDrop.DragEnterEvent, files, m.View);
        Assert.True(overlay.IsVisible);

        // taken out in the middle of a drag: the overlay goes, and the view takes no more of the drag
        UiHelpers.Drag(DragDrop.DragLeaveEvent, files, m.View); // a hide waiting its turn finds nothing to do
        m.Out();
        UiHelpers.Settle();
        Assert.False(overlay.IsVisible);
        UiHelpers.Drag(DragDrop.DragEnterEvent, files, m.View);
        UiHelpers.Drag(DragDrop.DragOverEvent, files, m.View);
        UiHelpers.Drag(DragDrop.DropEvent, files, m.View);
        UiHelpers.Settle();
        Assert.False(overlay.IsVisible);
        Assert.Empty(c.Attachments);
        Assert.Equal(0, file.Opens);

        // back: the same view takes a drag again, once
        m.In();
        UiHelpers.Drag(DragDrop.DragEnterEvent, files, m.View);
        Assert.True(overlay.IsVisible);
        UiHelpers.Drag(DragDrop.DropEvent, files, m.View);
        Assert.False(overlay.IsVisible);
        UiHelpers.PumpUntil(() => c.Attachments.Count == 1);
        Assert.Equal(1, file.Opens);

        // out and in again with no drag: nothing shows
        m.Out();
        m.In();
        Assert.False(overlay.IsVisible);
        m.Window.Close();
    }

    /// <summary>Who listens to the conversation's list, to its requests, and to its steps and their agents.</summary>
    private static int FocusHandlers(ChatConversation c)
    {
        const System.Reflection.BindingFlags field = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        int Count(object target, Type owner, string name) =>
            ((Delegate?)owner.GetField(name, field)!.GetValue(target))?.GetInvocationList().Length ?? 0;
        var count = Count(c, typeof(ChatObservableObject), "PropertyChanged")
                    + Count(c.Items, typeof(System.Collections.ObjectModel.ObservableCollection<ChatItem>), "CollectionChanged")
                    + Count(c.PendingPermissions, typeof(System.Collections.ObjectModel.ObservableCollection<PendingPermission>), "CollectionChanged");
        foreach (var item in c.Items) count += Count(item, typeof(ChatObservableObject), "PropertyChanged");
        foreach (var agent in c.Agents) count += Count(agent, typeof(ChatObservableObject), "PropertyChanged");
        return count;
    }

    [AvaloniaFact]
    public void The_focus_projection_lets_go_of_the_items_while_the_view_is_away()
    {
        var c = Conversation();
        FocusViewTests.SetBusy(c, true); // a turn under way
        c.Items.Add(new UserMessageItem("go"));
        c.Items.Add(new ThinkingItem { DurationMs = 2000 });
        var bash = new ToolCallItem { Name = "Bash", Status = ToolStatus.Running, ToolUseId = "t1" };
        c.Items.Add(bash);
        var own = FocusHandlers(c); // the conversation's own, before any view
        var m = new Mounted(c);
        m.View.FocusView = true;
        UiHelpers.Pump();
        var list = UiHelpers.ItemList(m.View);
        var projection = Assert.IsType<FocusProjection>(list.ItemsSource);
        Assert.True(projection.IsStarted);
        Assert.True(FocusHandlers(c) > own);
        Assert.Contains("Running Bash…", UiHelpers.Texts(m.View));

        m.Out();
        Assert.Equal(own, FocusHandlers(c)); // the projection let go of the list, the requests, the steps
        Assert.False(projection.IsStarted);
        Assert.Same(projection, list.ItemsSource); // leaving drew nothing

        // what changes while out is drawn when back: the fold is read again, whole
        bash.Status = ToolStatus.Succeeded;
        c.Items.Add(new ToolCallItem { Name = "Read", Status = ToolStatus.Succeeded, ToolUseId = "t2" });
        c.Items.Add(new AssistantTextItem { Text = "Done." });
        UiHelpers.Pump();
        Assert.Equal(2, projection.Count);
        m.In();
        Assert.True(projection.IsStarted);
        Assert.Same(projection, list.ItemsSource);
        Assert.Equal(3, projection.Count);
        Assert.Contains("2 tool calls", UiHelpers.Texts(m.View)); // the calls only, the thought among them
        Assert.DoesNotContain("Running Bash…", UiHelpers.Texts(m.View));
        Assert.Contains("Done.", UiHelpers.MarkdownText(m.View));

        // out with the Focus view turned off meanwhile: back, every row
        m.Out();
        m.View.FocusView = false; // the list takes the conversation's items again (Avalonia's list follows them, as it always did)
        Assert.False(projection.IsStarted);
        m.In();
        Assert.Same(c.Items, list.ItemsSource);
        Assert.Equal(2, m.View.GetVisualDescendants().OfType<ToolCallView>().Count(t => t.IsEffectivelyVisible));
        m.Window.Close();
    }

    [AvaloniaFact]
    public void The_clock_of_a_focus_folds_live_agents_ticks_only_while_the_view_is_in()
    {
        var c = Conversation();
        FocusViewTests.SetBusy(c, true);
        var now = new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);
        c.Clock = () => now;
        var call = new ToolCallItem { Name = "Agent", Status = ToolStatus.Running, ToolUseId = "toolu_a" };
        call.Agent = new AgentInfo("toolu_a") { Description = "Look around", Status = AgentStatus.Running, StartedAt = now };
        c.Items.Add(new UserMessageItem("go"));
        c.Items.Add(call);
        var m = new Mounted(c);
        m.View.FocusView = true;
        UiHelpers.Pump();
        FocusAgentStats Stats() => m.View.GetVisualDescendants().OfType<FocusAgentStats>().Single(s => s.IsEffectivelyVisible);
        var stats = Stats();
        Assert.True(stats.IsTicking);

        m.Out();
        Assert.False(stats.IsTicking); // out of the window, the clock stops
        now = now.AddSeconds(42);
        m.In();
        Assert.True(Stats().IsTicking);
        Assert.Equal("42s", Stats().Text); // and coming back shows the time as it is now

        // the agent done: no row, no clock
        call.Agent.Status = AgentStatus.Completed;
        UiHelpers.Pump();
        Assert.DoesNotContain(m.View.GetVisualDescendants().OfType<FocusAgentStats>(), s => s.IsTicking);
        m.Window.Close();
    }

    [AvaloniaFact]
    public void The_context_and_account_modals_let_go_of_the_conversation_while_out_and_come_back_as_they_were()
    {
        var m = new Mounted(Conversation());
        var c = m.Conversation;
        c.Apply(new ContextUsageReported(new ContextUsage(30, 100, 30, new[] { new ContextCategory("Messages", 30, "used") })));
        c.Apply(new ClaudeVisualChat.Protocol.AccountReported(new AccountInfo("Claude AI", "a@b.co", null, "Max")));
        c.Apply(new ClaudeVisualChat.Protocol.UsageLimitsReported(new UsageLimits(new[] { new UsageWindow("five_hour", 0.5, null) })));

        foreach (var command in new[] { "context", "usage" })
        {
            c.InvokeCommand(command, "");
            UiHelpers.Pump();
            ChatInfoPanel Modal() => command == "context"
                ? m.View.GetVisualDescendants().OfType<ContextPanel>().Single()
                : m.View.GetVisualDescendants().OfType<UsagePanel>().Single();
            var panel = Modal();
            var before = UiHelpers.Texts(panel);
            Assert.True(panel.IsEffectivelyVisible);
            Handlers(c, out var shown);

            m.Out();                                              // out of the window: the events go, nothing throws
            Handlers(c, out var left);
            Assert.True(left < shown);
            c.Apply(new ClaudeVisualChat.Protocol.AccountReported(new AccountInfo("API Key", null, null, null)));
            c.ClosePanel();                                       // the panel closes while the view is out
            c.InvokeCommand(command, "");

            m.In();                                               // and back: it is drawn as the conversation is now
            Handlers(c, out var back);
            Assert.Equal(shown, back);
            Assert.True(Modal().IsEffectivelyVisible);
            if (command == "usage") Assert.Contains("API Key", UiHelpers.Texts(Modal()));
            else Assert.Equal(before, UiHelpers.Texts(Modal()));
            c.Apply(new ClaudeVisualChat.Protocol.AccountReported(new AccountInfo("Claude AI", "a@b.co", null, "Max")));
            c.ClosePanel();
            UiHelpers.Pump();
            Assert.False(Modal().IsVisible);
        }
        m.Window.Close();
    }
}
