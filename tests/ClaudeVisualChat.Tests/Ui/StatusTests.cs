using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using Xunit;
using PermissionCancelled = ClaudeVisualChat.Protocol.PermissionCancelled;
using PermissionRequested = ClaudeVisualChat.Protocol.PermissionRequested;
using PromptCacheUsed = ClaudeVisualChat.Protocol.PromptCacheUsed;
using SystemNotice = ClaudeVisualChat.Protocol.SystemNotice;

namespace ClaudeVisualChat.Tests.Ui;

/// <summary>The bar's cache clock and Remote Control chip, and the banners over the reply box.</summary>
public class StatusTests
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 10, 22, 10, 0, 0, TimeSpan.Zero);

    private sealed class Opened
    {
        /// <param name="inHost">True to put the view in a panel, as a host does that hides it with its own parent.</param>
        public Opened(bool inHost = false)
        {
            Conversation = new ChatConversation { Clock = () => Now };
            Conversation.RemoteControlLinkRequested += (_, e) => Links.Add(e.Url);
            Conversation.CommandInvoked += (_, e) => Commands.Add(e.Name + " " + e.Arguments);
            Conversation.BrowserDisconnectRequested += (_, _) => Disconnects++;
            View = new ClaudeChatView { Conversation = Conversation };
            Host = new Panel();
            if (inHost) Host.Children.Add(View);
            Window = UiHelpers.Show(inHost ? Host : View);
            Composer = UiHelpers.Composer(View);
        }

        public DateTimeOffset Now { get; set; } = Start;
        public ChatConversation Conversation { get; }
        public ClaudeChatView View { get; }
        public Panel Host { get; }
        public Window Window { get; }
        public ChatComposer Composer { get; }
        public List<string> Links { get; } = new List<string>();
        public List<string> Commands { get; } = new List<string>();
        public int Disconnects { get; set; }

        public PromptCacheClock Clock => Composer.GetVisualDescendants().OfType<PromptCacheClock>().Single();

        public RemoteControlChip Chip => Composer.GetVisualDescendants().OfType<RemoteControlChip>().Single();

        public BrowserBanner BrowserBanner => View.GetVisualDescendants().OfType<BrowserBanner>().Single();

        public RemoteControlBanner RemoteControlBanner => View.GetVisualDescendants().OfType<RemoteControlBanner>().Single();

        public Border Box => Composer.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("ClaudeChatComposer"));

        public string ClockText => string.Join("", UiHelpers.Texts(Clock));

        /// <summary>The time moves and the clock's second goes by.</summary>
        public void At(TimeSpan sinceStart)
        {
            Now = Start + sinceStart;
            Clock.Tick();
            UiHelpers.Pump();
        }

        public void Set(Action change)
        {
            change();
            UiHelpers.Pump();
        }
    }

    private static JsonElement J(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static PermissionRequested Bash(string id) =>
        new PermissionRequested(id, "Bash", "Bash", null, "tool-" + id, J("{\"command\":\"ls\"}"), null);

    private static IBrush? Resource(Control control, string key) =>
        control.TryFindResource(key, out var value) ? value as IBrush : null;

    private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    // ---- the cache clock ----

    [AvaloniaFact]
    public void Clock_counts_minutes()
    {
        var o = new Opened();
        var strings = o.View.Strings;

        // no data: not there, no room taken, no timer
        Assert.False(o.Clock.IsVisible);
        Assert.False(o.Clock.IsTicking);

        o.Set(() => o.Conversation.Apply(new PromptCacheUsed(TimeSpan.FromHours(1), null))); // expires at 11:00
        Assert.True(o.Clock.IsEffectivelyVisible);
        Assert.True(o.Clock.IsTicking);
        Assert.Equal("60m", o.ClockText);

        o.At(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(50)); // 58 min 10 s left
        Assert.Equal("59m", o.ClockText);
        Assert.Equal(strings.CacheWarm.Replace("{0}", "59"), ToolTip.GetTip(o.Clock));
        Assert.Equal(strings.CacheWarm.Replace("{0}", "59"), AutomationProperties.GetName(o.Clock));
        Assert.DoesNotContain("cold", o.Clock.Classes);

        o.At(TimeSpan.FromMinutes(59) + TimeSpan.FromSeconds(30)); // 30 s left
        Assert.Equal("1m", o.ClockText);
        Assert.Equal(strings.CacheWarm.Replace("{0}", "1"), ToolTip.GetTip(o.Clock));

        // expired: only the icon, in the error color; idle since the last request (11:15 − 10:00)
        o.At(TimeSpan.FromMinutes(75));
        Assert.Equal("", o.ClockText);
        Assert.Contains("cold", o.Clock.Classes);
        Assert.Equal(strings.CacheExpired.Replace("{0}", "1h 15m"), ToolTip.GetTip(o.Clock));
        Assert.True(o.Clock.IsTicking); // the idle time keeps counting

        // compacted: cold, whatever the time
        o.At(TimeSpan.FromMinutes(10));
        Assert.Equal("50m", o.ClockText);
        o.Set(() => o.Conversation.Apply(new SystemNotice("compact_boundary")));
        Assert.Equal("", o.ClockText);
        Assert.Contains("cold", o.Clock.Classes);
        Assert.Equal(strings.CacheCompacted, ToolTip.GetTip(o.Clock));

        // the next use warms it again
        o.Set(() => o.Conversation.Apply(new PromptCacheUsed(null, null)));
        Assert.DoesNotContain("cold", o.Clock.Classes);
        Assert.Equal("60m", o.ClockText);

        // a cleared conversation has no data: the clock goes and its timer stops
        o.Set(() => o.Conversation.Clear());
        Assert.False(o.Clock.IsVisible);
        Assert.False(o.Clock.IsTicking);
    }

    [AvaloniaFact]
    public void Clock_never_shows_negative_minutes_or_more_than_the_lifetime()
    {
        var o = new Opened();
        o.Set(() => o.Conversation.Apply(new PromptCacheUsed(TimeSpan.FromMinutes(5), null))); // expires at 10:05

        o.At(TimeSpan.FromMinutes(-30)); // our clock went back: still the lifetime at most
        Assert.Equal("5m", o.ClockText);
        Assert.Equal(o.View.Strings.CacheWarm.Replace("{0}", "5"), ToolTip.GetTip(o.Clock));

        o.At(TimeSpan.FromMinutes(5)); // exactly at the expiry: cold, never "0m"
        Assert.Equal("", o.ClockText);
        Assert.Contains("cold", o.Clock.Classes);
        Assert.Equal(o.View.Strings.CacheExpired.Replace("{0}", "5m"), ToolTip.GetTip(o.Clock));

        o.At(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59.5)); // half a second left
        Assert.Equal("1m", o.ClockText);
    }

    [AvaloniaFact]
    public void Clock_catches_up_after_the_host_hides_and_shows_the_view()
    {
        // the host hides the chat with a parent of its own (another page of the app), without taking it out
        var o = new Opened(inHost: true);
        o.Set(() => o.Conversation.Apply(new PromptCacheUsed(TimeSpan.FromHours(1), null)));
        var clock = o.Clock;
        Assert.True(clock.IsTicking);

        o.Set(() => o.Host.IsVisible = false);
        o.At(TimeSpan.FromMinutes(5)); // a second goes by unseen: nothing done, the timer goes on
        Assert.False(clock.IsEffectivelyVisible);
        Assert.True(clock.IsTicking);

        o.Set(() => o.Host.IsVisible = true);
        o.At(TimeSpan.FromMinutes(20)); // the next second shows the time as it is
        Assert.True(clock.IsTicking);
        Assert.Equal("40m", o.ClockText);
        Assert.Equal(o.View.Strings.CacheWarm.Replace("{0}", "40"), ToolTip.GetTip(clock));
    }

    [AvaloniaFact]
    public void Clock_catches_up_after_read_only_and_stops_out_of_the_window()
    {
        var o = new Opened();
        o.Set(() => o.Conversation.Apply(new PromptCacheUsed(TimeSpan.FromHours(1), null)));
        var clock = o.Clock;

        o.Set(() => o.View.IsReadOnly = true); // no reply box, no clock to see
        Assert.False(clock.IsEffectivelyVisible);
        o.At(TimeSpan.FromMinutes(10));

        o.Set(() => o.View.IsReadOnly = false);
        o.At(TimeSpan.FromMinutes(20));
        Assert.True(clock.IsTicking);
        Assert.Equal("40m", o.ClockText);

        o.Set(() => o.Window.Content = null); // out of the window: the timer stops
        Assert.False(clock.IsTicking);
    }

    [AvaloniaFact]
    public void Another_conversation_shows_its_own_cache()
    {
        var o = new Opened();
        o.Set(() => o.Conversation.Apply(new PromptCacheUsed(TimeSpan.FromHours(1), null)));
        Assert.Equal("60m", o.ClockText);

        // one whose cache went cold long ago
        var other = new ChatConversation { Clock = () => o.Now - TimeSpan.FromHours(2) };
        other.Apply(new PromptCacheUsed(TimeSpan.FromMinutes(5), null)); // expired at 08:05
        other.Clock = () => o.Now;
        o.Set(() => o.View.Conversation = other);
        Assert.Equal("", o.ClockText);
        Assert.Contains("cold", o.Clock.Classes);
        Assert.Equal(o.View.Strings.CacheExpired.Replace("{0}", "2h 0m"), ToolTip.GetTip(o.Clock));

        // one with no data: no clock, no timer
        o.Set(() => o.View.Conversation = new ChatConversation());
        Assert.False(o.Clock.IsVisible);
        Assert.False(o.Clock.IsTicking);

        // and back: the first one's minutes, at its time
        o.Set(() => o.View.Conversation = o.Conversation);
        o.At(TimeSpan.FromMinutes(15));
        Assert.Equal("45m", o.ClockText);
        Assert.DoesNotContain("cold", o.Clock.Classes);
    }

    [AvaloniaFact]
    public void Clock_and_chip_sit_in_the_bar_with_the_extension_measures()
    {
        var o = new Opened();
        o.Set(() =>
        {
            o.Conversation.Apply(new PromptCacheUsed(TimeSpan.FromHours(1), null));
            o.Conversation.RemoteControlStatus = ChatRemoteControlStatus.Connected;
            o.Conversation.RemoteControlUrl = "https://claude.ai/code/session_1";
        });
        Assert.Equal(26, o.Clock.Bounds.Height);
        Assert.Equal(18, o.Chip.Bounds.Height);
        Assert.Equal(new CornerRadius(9), o.Chip.CornerRadius);
        Assert.Equal(new Thickness(8, 0), o.Chip.Padding);

        // after the / button, in this order: the clock, the chip
        var commands = UiHelpers.ButtonWithText(o.Composer, o.View.Strings.ShowCommands);
        double X(Control c) => c.TranslatePoint(default, o.Window)!.Value.X;
        Assert.True(X(commands) < X(o.Clock));
        Assert.True(X(o.Clock) < X(o.Chip));
    }

    // ---- the Remote Control chip ----

    [AvaloniaFact]
    public void Remote_control_chip()
    {
        var o = new Opened();
        var strings = o.View.Strings;
        Assert.False(o.Chip.IsVisible); // Off

        o.Set(() =>
        {
            o.Conversation.RemoteControlUrl = "https://claude.ai/code/session_1";
            o.Conversation.RemoteControlStatus = ChatRemoteControlStatus.Connecting;
        });
        Assert.True(o.Chip.IsEffectivelyVisible);
        Assert.Contains(strings.RemoteControl, UiHelpers.Texts(o.Chip));
        Assert.Equal(strings.RemoteControlConnecting, ToolTip.GetTip(o.Chip));
        Assert.Contains("on", o.Chip.Classes);
        UiHelpers.Click(o.Window, o.Chip);
        Assert.Empty(o.Links); // no link while it connects

        o.Set(() => o.Conversation.RemoteControlStatus = ChatRemoteControlStatus.Connected);
        Assert.Equal(strings.RemoteControlActive, ToolTip.GetTip(o.Chip));
        Assert.Equal(strings.RemoteControl, AutomationProperties.GetName(o.Chip));
        Assert.Equal(ColorOf(Resource(o.Chip, "ClaudeChat.BannerTint")), ColorOf(o.Chip.Background));
        Assert.Equal(ColorOf(Resource(o.Chip, "ClaudeChat.RemoteControlForeground")), ColorOf(o.Chip.Foreground));
        UiHelpers.Click(o.Window, o.Chip);
        Assert.Equal(new[] { "https://claude.ai/code/session_1" }, o.Links);
        Assert.False(o.RemoteControlBanner.IsVisible);

        o.Set(() =>
        {
            o.Conversation.RemoteControlError = "bridge closed";
            o.Conversation.RemoteControlStatus = ChatRemoteControlStatus.Error;
        });
        var failed = strings.RemoteControlFailed.Replace("{0}", "bridge closed");
        var hint = strings.RemoteControlFailedHint.Replace("{0}", "bridge closed");
        Assert.True(o.Chip.IsEffectivelyVisible);
        Assert.DoesNotContain("on", o.Chip.Classes); // the grey pill
        Assert.Equal(ColorOf(Resource(o.Chip, "ClaudeChat.PillBackground")), ColorOf(o.Chip.Background));
        Assert.Equal(hint, ToolTip.GetTip(o.Chip)); // the extension's: how to dismiss it
        UiHelpers.Click(o.Window, o.Chip);
        Assert.Single(o.Links);

        // and the red banner over the reply box, whose × turns Remote Control off
        var banner = o.RemoteControlBanner;
        Assert.True(banner.IsEffectivelyVisible);
        Assert.Contains("error", banner.Classes);
        Assert.Contains(failed, UiHelpers.Texts(banner));
        UiHelpers.Click(o.Window, UiHelpers.ButtonWithText(banner, strings.Dismiss));
        Assert.Equal(new[] { "remote-control off" }, o.Commands);

        o.Set(() => o.Conversation.RemoteControlStatus = ChatRemoteControlStatus.Off);
        Assert.False(o.Chip.IsVisible);
        Assert.False(o.RemoteControlBanner.IsVisible);
    }

    // ---- the browser banner ----

    [AvaloniaFact]
    public void Browser_banner()
    {
        var o = new Opened();
        var strings = o.View.Strings;
        Assert.False(o.BrowserBanner.IsVisible); // Off

        var shown = new List<(ChatBrowserStatus Status, string Text)>
        {
            (ChatBrowserStatus.Connecting, strings.BrowserConnecting),
            (ChatBrowserStatus.Connected, strings.BrowserConnected),
            (ChatBrowserStatus.NoBrowser, strings.BrowserNoBrowsers),
        };
        foreach (var (status, text) in shown)
        {
            o.Set(() => o.Conversation.BrowserStatus = status);
            Assert.True(o.BrowserBanner.IsEffectivelyVisible);
            Assert.Contains(text, UiHelpers.Texts(o.BrowserBanner));
            Assert.DoesNotContain("error", o.BrowserBanner.Classes);
        }

        o.Set(() =>
        {
            o.Conversation.BrowserError = "extension not found";
            o.Conversation.BrowserStatus = ChatBrowserStatus.Error;
        });
        Assert.Contains(strings.BrowserFailed.Replace("{0}", "extension not found"), UiHelpers.Texts(o.BrowserBanner));
        Assert.Contains("error", o.BrowserBanner.Classes);

        // the × disconnects
        UiHelpers.Click(o.Window, UiHelpers.ButtonWithText(o.BrowserBanner, strings.DisconnectBrowser));
        Assert.Equal(1, o.Disconnects);

        // a permission request covers it, as another popup does in the extension
        o.Set(() => o.Conversation.Apply(Bash("r1")));
        Assert.False(o.BrowserBanner.IsVisible);
        o.Set(() => o.Conversation.Apply(new PermissionCancelled("r1")));
        Assert.True(o.BrowserBanner.IsEffectivelyVisible);

        o.Set(() => o.Conversation.BrowserStatus = ChatBrowserStatus.Off);
        Assert.False(o.BrowserBanner.IsVisible);
    }

    [AvaloniaFact]
    public void Banner_is_tucked_under_the_reply_box()
    {
        var o = new Opened();
        o.Set(() => o.Conversation.BrowserStatus = ChatBrowserStatus.Connected);
        var banner = o.BrowserBanner;
        var top = o.Box.TranslatePoint(default, o.Window)!.Value;
        var bannerTop = banner.TranslatePoint(default, o.Window)!.Value;

        // the reply box's width, its last 8 px under the box
        Assert.Equal(top.X, bannerTop.X);
        Assert.Equal(o.Box.Bounds.Width, banner.Bounds.Width);
        Assert.Equal(top.Y + 8, bannerTop.Y + banner.Bounds.Height, 3);
        Assert.Equal(new CornerRadius(8, 8, 0, 0), banner.CornerRadius);
        Assert.Equal(new Thickness(16, 6, 6, 12), banner.Padding);
        var text = banner.GetVisualDescendants().OfType<TextBlock>().Single(t => t.IsEffectivelyVisible);
        Assert.Equal(FontWeight.SemiBold, text.FontWeight);
        Assert.Equal(11.05, text.FontSize, 3);

        // the extension's colors: the tint at 50 % over the text, at 25 % over the editor
        Assert.Equal(ColorOf(Resource(banner, "ClaudeChat.BannerBackground")), ColorOf(banner.Background));
        Assert.Equal(ColorOf(Resource(banner, "ClaudeChat.BannerForeground")), ColorOf(banner.BorderBrush));
    }

    [AvaloniaFact]
    public void One_banner_at_a_time_and_none_in_read_only()
    {
        var o = new Opened();
        o.Set(() =>
        {
            o.Conversation.RemoteControlError = "bridge closed";
            o.Conversation.RemoteControlStatus = ChatRemoteControlStatus.Error;
        });
        Assert.True(o.RemoteControlBanner.IsEffectivelyVisible);

        // the browser's banner takes the place, as in the extension
        o.Set(() => o.Conversation.BrowserStatus = ChatBrowserStatus.Connecting);
        Assert.True(o.BrowserBanner.IsEffectivelyVisible);
        Assert.False(o.RemoteControlBanner.IsVisible);
        o.Set(() => o.Conversation.BrowserStatus = ChatBrowserStatus.Off);
        Assert.True(o.RemoteControlBanner.IsEffectivelyVisible);

        // a permission request hides it too
        o.Set(() => o.Conversation.Apply(Bash("r1")));
        Assert.False(o.RemoteControlBanner.IsVisible);
        o.Set(() => o.Conversation.Apply(new PermissionCancelled("r1")));

        o.Set(() =>
        {
            o.Conversation.BrowserStatus = ChatBrowserStatus.Connected;
            o.View.IsReadOnly = true;
        });
        Assert.False(o.BrowserBanner.IsEffectivelyVisible);
        Assert.False(o.RemoteControlBanner.IsEffectivelyVisible);
        Assert.False(o.Chip.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void Texts_follow_the_strings()
    {
        var o = new Opened();
        o.Set(() =>
        {
            o.Conversation.Apply(new PromptCacheUsed(TimeSpan.FromHours(1), null));
            o.Conversation.BrowserStatus = ChatBrowserStatus.Connecting;
            o.Conversation.RemoteControlStatus = ChatRemoteControlStatus.Connecting;
        });
        o.Set(() => o.View.Strings = ChatStrings.PortugueseBrazil);
        var strings = ChatStrings.PortugueseBrazil;
        Assert.Equal(strings.CacheWarm.Replace("{0}", "60"), ToolTip.GetTip(o.Clock));
        Assert.Contains(strings.BrowserConnecting, UiHelpers.Texts(o.BrowserBanner));
        Assert.Equal(strings.RemoteControlConnecting, ToolTip.GetTip(o.Chip));
        Assert.Contains(strings.DisconnectBrowser, UiHelpers.ButtonNames(o.BrowserBanner));
    }
}
