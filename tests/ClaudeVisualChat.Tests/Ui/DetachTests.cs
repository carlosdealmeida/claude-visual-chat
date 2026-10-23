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
using AssistantBlock = ClaudeVisualChat.Protocol.AssistantBlock;
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

    // ---- out, the conversation no longer reaches the view ----

    /// <summary>Handlers on the conversation's PropertyChanged and on its attachments' CollectionChanged.</summary>
    private static int Handlers(ChatConversation c)
    {
        const System.Reflection.BindingFlags field = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var changed = (Delegate?)typeof(ChatObservableObject).GetField("PropertyChanged", field)!.GetValue(c);
        var attachments = (Delegate?)typeof(System.Collections.ObjectModel.ObservableCollection<ChatImage>)
            .GetField("CollectionChanged", field)!.GetValue(c.Attachments);
        return (changed?.GetInvocationList().Length ?? 0) + (attachments?.GetInvocationList().Length ?? 0);
    }

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
}
