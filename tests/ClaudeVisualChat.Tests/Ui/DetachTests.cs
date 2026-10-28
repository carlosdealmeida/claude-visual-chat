using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
