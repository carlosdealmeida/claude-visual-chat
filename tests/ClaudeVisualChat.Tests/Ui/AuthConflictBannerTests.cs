using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using ClaudeVisualChat.Protocol;
using Xunit;

namespace ClaudeVisualChat.Tests.Ui;

/// <summary>The warning over the reply box when an auth token and an API key are both set.</summary>
public class AuthConflictBannerTests
{
    private static AuthConflictBanner BannerOf(ClaudeChatView view) => view.GetVisualDescendants().OfType<AuthConflictBanner>().Single();

    private static string Text(AuthConflictBanner banner) => string.Concat(UiHelpers.Texts(banner));

    private static (ChatConversation Conversation, ClaudeChatView View, AuthConflictBanner Banner, Avalonia.Controls.Window Window) Open(
        string token, string key)
    {
        var c = new ChatConversation();
        c.Apply(new AuthConflictReported(new AuthConflict(token, key)));
        var view = new ClaudeChatView { Conversation = c };
        var window = UiHelpers.Show(view);
        return (c, view, BannerOf(view), window);
    }

    [AvaloniaTheory]
    [InlineData("profile", "ANTHROPIC_API_KEY",
        "Claude Code is not using ANTHROPIC_API_KEY, because your Anthropic profile takes priority. Keep one and remove the other, then start a new conversation (restart your editor if you removed one from your shell).")]
    [InlineData("ANTHROPIC_AUTH_TOKEN", "/login managed key",
        "Claude Code has both ANTHROPIC_AUTH_TOKEN and an API key saved by /login, so it may not use the credential you expect. To use only ANTHROPIC_AUTH_TOKEN, run \"claude auth logout\" in a terminal to remove the saved key, then start a new conversation.")]
    [InlineData("claude.ai", "ANTHROPIC_API_KEY",
        "Claude Code has both your claude.ai login and ANTHROPIC_API_KEY, so it may not use the credential you expect. Keep one and remove the other, then start a new conversation (restart your editor if you removed one from your shell).")]
    [InlineData("claude.ai", "/login managed key",
        "Claude Code has both your claude.ai login and an API key saved by /login, so it may not use the credential you expect. Keep one and remove the other, then start a new conversation (restart your editor if you removed one from your shell).")]
    public void The_sentences_follow_the_two_sources(string token, string key, string expected)
    {
        var (_, _, banner, window) = Open(token, key);
        Assert.True(banner.IsEffectivelyVisible);
        Assert.Equal(expected, Text(banner));
        window.Close();
    }

    [AvaloniaFact]
    public void A_helper_source_reads_as_its_phrase()
    {
        var (_, _, banner, window) = Open("apiKeyHelper", "ANTHROPIC_API_KEY");
        Assert.StartsWith("Claude Code has both an apiKeyHelper setting and ANTHROPIC_API_KEY,", Text(banner));
        window.Close();
    }

    [AvaloniaFact]
    public void Shows_only_when_no_other_banner_is_open()
    {
        var (c, _, banner, window) = Open("claude.ai", "ANTHROPIC_API_KEY");
        Assert.True(banner.IsEffectivelyVisible);

        c.BrowserStatus = ChatBrowserStatus.Connected;
        UiHelpers.Pump();
        Assert.False(banner.IsVisible);
        c.BrowserStatus = ChatBrowserStatus.Off;
        UiHelpers.Pump();
        Assert.True(banner.IsVisible);

        c.RemoteControlStatus = ChatRemoteControlStatus.Error;
        UiHelpers.Pump();
        Assert.False(banner.IsVisible);
        c.RemoteControlStatus = ChatRemoteControlStatus.Off;
        UiHelpers.Pump();
        Assert.True(banner.IsVisible);

        c.Apply(new ModelRefusalFallback("claude-opus-5-5", "claude-sonnet-5-5", "cyber"));
        UiHelpers.Pump();
        Assert.False(banner.IsVisible);
        c.DismissModelFallback();
        UiHelpers.Pump();
        Assert.True(banner.IsVisible);

        c.Apply(new PermissionRequested("r1", "Bash", "Bash", null, "t1", System.Text.Json.JsonDocument.Parse("{\"command\":\"ls\"}").RootElement.Clone(), null));
        UiHelpers.Pump();
        Assert.False(banner.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void Shows_nothing_in_a_read_only_view()
    {
        var (_, view, banner, window) = Open("claude.ai", "ANTHROPIC_API_KEY");
        view.IsReadOnly = true;
        UiHelpers.Pump();
        Assert.False(banner.IsEffectivelyVisible);
        view.IsReadOnly = false;
        UiHelpers.Pump();
        Assert.True(banner.IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void The_close_button_dismisses()
    {
        var (c, view, banner, window) = Open("claude.ai", "ANTHROPIC_API_KEY");
        var close = UiHelpers.ButtonWithText(banner, view.Strings.DismissWarning);
        Assert.NotNull(close);
        close.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        UiHelpers.Pump();

        Assert.Equal("claude.ai\nANTHROPIC_API_KEY", c.DismissedAuthConflictKey);
        Assert.False(banner.IsVisible);

        // another pair shows again
        c.Apply(new AuthConflictReported(new AuthConflict("profile", "ANTHROPIC_API_KEY")));
        UiHelpers.Pump();
        Assert.True(banner.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void Texts_follow_the_strings()
    {
        var (_, view, banner, window) = Open("claude.ai", "ANTHROPIC_API_KEY");
        view.Strings = ChatStrings.PortugueseBrazil;
        UiHelpers.Pump();
        Assert.StartsWith("O Claude Code tem seu login do claude.ai e ANTHROPIC_API_KEY ao mesmo tempo", Text(banner));
        Assert.Contains(ChatStrings.PortugueseBrazil.DismissWarning, UiHelpers.ButtonNames(banner));
        window.Close();
    }
}
