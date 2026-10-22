using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using Xunit;

namespace ClaudeVisualChat.Tests.Ui;

/// <summary>The /context and /usage panels over the list.</summary>
public class UsagePanelTests
{
    private const string RateLimitLine =
        "{\"type\":\"rate_limit_event\",\"rate_limit_info\":{\"status\":\"allowed\",\"resetsAt\":1792698600,\"rateLimitType\":\"five_hour\",\"unifiedWindows\":{\"five_hour\":{\"utilization\":0.01,\"resetsAt\":1792698600},\"seven_day\":{\"utilization\":0.12,\"resetsAt\":1793016000}}},\"uuid\":\"u1\",\"session_id\":\"s1\"}";

    private static readonly DateTimeOffset FiveHourReset = DateTimeOffset.FromUnixTimeSeconds(1792698600);
    private static readonly DateTimeOffset SevenDayReset = DateTimeOffset.FromUnixTimeSeconds(1793016000);

    private sealed class Opened
    {
        public Opened(ChatConversation conversation)
        {
            Conversation = conversation;
            conversation.StopRequested += (_, _) => Stops++;
            View = new ClaudeChatView { Conversation = conversation };
            Window = UiHelpers.Show(View);
            Unhandled = UiHelpers.UnhandledKeys(Window);
        }

        public ChatConversation Conversation { get; }
        public ClaudeChatView View { get; }
        public Window Window { get; }
        public List<Key> Unhandled { get; }
        public int Stops { get; private set; }

        public ContextPanel Context => View.GetVisualDescendants().OfType<ContextPanel>().Single();
        public UsagePanel Usage => View.GetVisualDescendants().OfType<UsagePanel>().Single();

        public List<Border> WithClass(Visual root, string name) =>
            root.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains(name)).ToList();
    }

    private static object? Resource(Control view, string key) => view.TryFindResource(key, out var value) ? value : null;

    private static Color ColorOf(Control view, string key) => Assert.IsAssignableFrom<ISolidColorBrush>(Resource(view, key)).Color;

    private static Color Fill(Border border) => Assert.IsAssignableFrom<ISolidColorBrush>(border.Background).Color;

    // ---- /context ----

    [AvaloniaFact]
    public void Context_panel()
    {
        var c = UiHelpers.Recording("commands", "stream");
        var ui = new Opened(c);
        Assert.False(ui.Context.IsVisible);
        Assert.False(ui.Usage.IsVisible);

        c.InvokeCommand("context", "");
        UiHelpers.Pump();
        var panel = ui.Context;
        Assert.True(panel.IsEffectivelyVisible);
        Assert.False(ui.Usage.IsVisible);
        var texts = UiHelpers.Texts(panel);
        Assert.Equal("Context usage", texts[0]);
        Assert.Contains("36,174 / 200,000 (18%)", texts);
        Assert.Equal(new[] { "Close" }, UiHelpers.ButtonNames(panel));

        // the bar: one piece per used category, in order, as long as its share of the window; the rest is the track
        var bar = Assert.Single(ui.WithClass(panel, "ClaudeChatContextBar"));
        var pieces = ui.WithClass(panel, "ClaudeChatContextSegment");
        var used = new[] { 6579L, 23669, 408, 1888, 3630 };
        Assert.Equal(used.Length, pieces.Count);
        for (var i = 0; i < used.Length; i++)
        {
            Assert.Equal(bar.Bounds.Width * used[i] / 200000, pieces[i].Bounds.Width, 0);
            Assert.Equal(ColorOf(ui.View, "ClaudeChat.ContextColor" + (i + 1)), Fill(pieces[i]));
        }
        Assert.Equal(pieces[0].TranslatePoint(default, bar)!.Value.X + pieces[0].Bounds.Width, pieces[1].TranslatePoint(default, bar)!.Value.X, 0);

        // a row per category: the used ones with their color, the free space with the track's, the deferred ones muted
        var rows = ui.WithClass(panel, "ClaudeChatPanelRow");
        Assert.Equal(
            new[]
            {
                "System prompt", "6,579", "System tools", "23,669", "Memory files", "408", "Skills", "1,888",
                "Messages", "3,630", "Free space", "163,826", "System tools (deferred)", "23,160",
            },
            rows.SelectMany(UiHelpers.Texts).ToArray());
        var swatches = rows.Select(r => ui.WithClass(r, "ClaudeChatContextSwatch").Single()).ToList();
        for (var i = 0; i < used.Length; i++) Assert.Equal(Fill(pieces[i]), Fill(swatches[i]));
        Assert.Equal(Fill(bar), Fill(swatches[5]));
        Assert.Null(swatches[6].Background);
        var muted = ColorOf(ui.View, "ClaudeChat.ForegroundMuted");
        Assert.All(rows[6].GetVisualDescendants().OfType<TextBlock>(),
            t => Assert.Equal(muted, Assert.IsAssignableFrom<ISolidColorBrush>(t.Foreground).Color));
        Assert.NotEqual(muted, Assert.IsAssignableFrom<ISolidColorBrush>(rows[0].GetVisualDescendants().OfType<TextBlock>().First().Foreground).Color);

        // in Portuguese
        ui.View.Strings = ChatStrings.PortugueseBrazil;
        UiHelpers.Pump();
        texts = UiHelpers.Texts(panel);
        Assert.Equal("Uso do contexto", texts[0]);
        Assert.Contains("36.174 / 200.000 (18 %)", texts);
        Assert.Contains("163.826", texts);
        Assert.Equal(new[] { "Fechar" }, UiHelpers.ButtonNames(panel));

        // the ×
        UiHelpers.Click(ui.Window, UiHelpers.ButtonWithText(panel, "Fechar"));
        Assert.Equal(ChatPanel.None, c.ShownPanel);
        Assert.False(panel.IsVisible);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Context_panel_waits_for_its_data()
    {
        var c = new ChatConversation();
        var ui = new Opened(c);
        c.InvokeCommand("context", "");
        UiHelpers.Pump();
        // idle (a transcript opened from a file, say): no "…" that would never end
        Assert.Equal(new[] { "Context usage", "No context data yet" }, UiHelpers.Texts(ui.Context));
        c.Submit("go"); // busy: the data is on its way
        UiHelpers.Pump();
        Assert.Equal(new[] { "Context usage", "…" }, UiHelpers.Texts(ui.Context));
        Assert.Empty(ui.WithClass(ui.Context, "ClaudeChatContextBar"));

        // the answer comes while it is open
        foreach (var line in FixtureFiles.StreamLines("commands")) c.AppendLine(line);
        UiHelpers.Pump();
        Assert.Equal(ChatPanel.Context, c.ShownPanel);
        Assert.Contains("36,174 / 200,000 (18%)", UiHelpers.Texts(ui.Context));
        Assert.DoesNotContain("…", UiHelpers.Texts(ui.Context));
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void The_panel_is_over_the_list_like_the_menus()
    {
        var c = UiHelpers.Recording("commands", "stream");
        var ui = new Opened(c);
        c.InvokeCommand("context", "");
        UiHelpers.Pump();
        var panel = ui.Context;
        var composer = UiHelpers.Composer(ui.View);
        var scroller = UiHelpers.Scroller(ui.View);

        // above the reply box, inside the list's area, as wide as the reply box at most and centered
        var top = panel.TranslatePoint(default, ui.View)!.Value;
        var box = composer.TranslatePoint(default, ui.View)!.Value;
        Assert.True(top.Y + panel.Bounds.Height <= box.Y);
        Assert.True(top.Y >= scroller.TranslatePoint(default, ui.View)!.Value.Y);
        Assert.True(panel.Bounds.Width <= 680);
        Assert.Equal(ui.View.Bounds.Width / 2, top.X + panel.Bounds.Width / 2, 0);

        // the menus' frame
        Assert.Equal(new CornerRadius(8), panel.CornerRadius);
        Assert.Equal(new Thickness(1), panel.BorderThickness);
        Assert.Equal(ColorOf(ui.View, "ClaudeChat.MenuBackground"), Fill(panel));
        Assert.Equal(Resource(ui.View, "ClaudeChat.PanelShadow"), panel.BoxShadow);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Esc_closes_the_panel_instead_of_stopping_claude()
    {
        var c = UiHelpers.Recording("commands", "stream");
        var ui = new Opened(c);
        var input = UiHelpers.Input(ui.View);
        input.Focus();
        c.Submit("go on");
        Assert.True(c.IsBusy);
        c.InvokeCommand("usage", "");
        UiHelpers.Pump();
        Assert.True(ui.Usage.IsVisible);

        UiHelpers.Press(ui.Window, Key.Escape);
        Assert.Equal(ChatPanel.None, c.ShownPanel);
        Assert.Equal(0, ui.Stops);
        Assert.DoesNotContain(Key.Escape, ui.Unhandled);

        UiHelpers.Press(ui.Window, Key.Escape); // no panel: Esc stops Claude again
        Assert.Equal(1, ui.Stops);

        // with the focus in the panel: Esc closes it and the keyboard goes back to the reply box
        c.InvokeCommand("context", "");
        UiHelpers.Pump();
        UiHelpers.ButtonWithText(ui.Context, "Close").Focus();
        UiHelpers.Press(ui.Window, Key.Escape);
        Assert.Equal(ChatPanel.None, c.ShownPanel);
        Assert.Equal(1, ui.Stops);
        Assert.True(input.IsFocused);

        // the palette open over the reply box takes the Esc first
        c.InvokeCommand("context", "");
        UiHelpers.Pump();
        input.Focus();
        UiHelpers.Type(ui.Window, "/");
        var palette = UiHelpers.Composer(ui.View).GetLogicalDescendants().OfType<CommandPalette>().Single();
        Assert.True(palette.IsOpen);
        UiHelpers.Press(ui.Window, Key.Escape);
        Assert.False(palette.IsOpen);
        Assert.Equal(ChatPanel.Context, c.ShownPanel);
        UiHelpers.Press(ui.Window, Key.Escape);
        Assert.Equal(ChatPanel.None, c.ShownPanel);
        Assert.Equal(1, ui.Stops);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_request_keeps_the_keyboard_over_an_open_panel()
    {
        var c = UiHelpers.Recording("commands", "stream");
        var answers = new List<PermissionAnsweredEventArgs>();
        c.PermissionAnswered += (_, e) => answers.Add(e);
        var ui = new Opened(c);
        c.InvokeCommand("context", "");
        UiHelpers.Pump();
        UiHelpers.ButtonWithText(ui.Context, "Close").Focus(); // the user is in the panel

        using (var input = System.Text.Json.JsonDocument.Parse("{\"command\":\"ls\"}"))
            c.Apply(new ClaudeVisualChat.Protocol.PermissionRequested("r1", "Bash", "Bash", null, "tool-r1", input.RootElement.Clone(), null));
        UiHelpers.Pump();

        var prompt = UiHelpers.Prompt(ui.View);
        Assert.True(ui.Context.IsVisible); // it stays
        Assert.True(prompt.IsKeyboardFocusWithin); // and the request has the keyboard
        Assert.False(ui.Context.IsKeyboardFocusWithin);

        UiHelpers.Press(ui.Window, Key.Escape); // the prompt does nothing with it: the panel closes
        Assert.Equal(ChatPanel.None, c.ShownPanel);
        Assert.True(prompt.IsKeyboardFocusWithin);
        Assert.Empty(answers);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Panels_show_in_read_only_and_follow_the_conversation()
    {
        var a = UiHelpers.Recording("commands", "stream");
        var b = new ChatConversation();
        var ui = new Opened(a);
        ui.View.IsReadOnly = true;
        a.InvokeCommand("context", "");
        UiHelpers.Pump();
        Assert.True(ui.Context.IsEffectivelyVisible);
        Assert.Contains("36,174 / 200,000 (18%)", UiHelpers.Texts(ui.Context));

        ui.View.Conversation = b;
        UiHelpers.Pump();
        Assert.False(ui.Context.IsVisible);
        a.InvokeCommand("usage", ""); // A no longer reaches the view
        UiHelpers.Pump();
        Assert.False(ui.Usage.IsVisible);

        ui.View.Conversation = a;
        UiHelpers.Pump();
        Assert.True(ui.Usage.IsEffectivelyVisible);
        Assert.False(ui.Context.IsVisible);
        ui.Window.Close();
    }

    // ---- /usage ----

    private static string Clock(DateTimeOffset time, string format) =>
        time.ToLocalTime().ToString(format, CultureInfo.InvariantCulture);

    private static string Day(DateTimeOffset time, string names) =>
        names.Split(' ')[(int)time.ToLocalTime().DayOfWeek];

    [AvaloniaFact]
    public void Usage_panel()
    {
        var c = new ChatConversation();
        c.AppendLine(RateLimitLine);
        c.Clock = () => FiveHourReset; // the five-hour window renews today, the weekly one days later
        var ui = new Opened(c);
        c.InvokeCommand("usage", "");
        UiHelpers.Pump();
        var panel = ui.Usage;
        Assert.True(panel.IsEffectivelyVisible);
        Assert.False(ui.Context.IsVisible);

        Assert.Equal(
            new[]
            {
                "Usage",
                "Current session (5h)", "1%", "Resets " + Clock(FiveHourReset, "h:mm tt"),
                "Weekly (7 days)", "12%", "Resets " + Day(SevenDayReset, "Sun Mon Tue Wed Thu Fri Sat") + " " + Clock(SevenDayReset, "h:mm tt"),
            },
            UiHelpers.Texts(panel).Where(t => t.Length > 0).ToArray());

        // a bar per window, filled up to its use
        var bars = ui.WithClass(panel, "ClaudeChatUsageBar");
        var fills = ui.WithClass(panel, "ClaudeChatUsageFill");
        Assert.Equal(2, bars.Count);
        Assert.Equal(2, fills.Count);
        Assert.Equal(bars[0].Bounds.Width * 0.01, fills[0].Bounds.Width, 0);
        Assert.Equal(bars[1].Bounds.Width * 0.12, fills[1].Bounds.Width, 0);
        Assert.Equal(0, fills[1].TranslatePoint(default, bars[1])!.Value.X, 0);

        ui.View.Strings = ChatStrings.PortugueseBrazil;
        UiHelpers.Pump();
        Assert.Equal(
            new[]
            {
                "Uso",
                "Sessão atual (5h)", "1 %", "Renova " + Clock(FiveHourReset, "HH:mm"),
                "Semanal (7 dias)", "12 %", "Renova " + Day(SevenDayReset, "dom seg ter qua qui sex sáb") + " " + Clock(SevenDayReset, "HH:mm"),
            },
            UiHelpers.Texts(panel).Where(t => t.Length > 0).ToArray());

        UiHelpers.Click(ui.Window, UiHelpers.ButtonWithText(panel, "Fechar"));
        Assert.Equal(ChatPanel.None, c.ShownPanel);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Usage_panel_waits_for_its_data()
    {
        var c = new ChatConversation();
        var ui = new Opened(c);
        c.InvokeCommand("usage", "");
        UiHelpers.Pump();
        Assert.Equal(new[] { "Usage", "No usage data yet" }, UiHelpers.Texts(ui.Usage));
        c.Submit("go");
        UiHelpers.Pump();
        Assert.Equal(new[] { "Usage", "…" }, UiHelpers.Texts(ui.Usage));

        c.AppendLine(RateLimitLine);
        UiHelpers.Pump();
        Assert.Contains("Current session (5h)", UiHelpers.Texts(ui.Usage));
        Assert.DoesNotContain("…", UiHelpers.Texts(ui.Usage));
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Usage_panel_of_a_loaded_transcript_says_there_is_no_data()
    {
        var c = new ChatConversation();
        c.LoadTranscript(new string[0]);
        var ui = new Opened(c);
        c.InvokeCommand("usage", "");
        UiHelpers.Pump();
        Assert.Equal(new[] { "Usage", "No usage data yet" }, UiHelpers.Texts(ui.Usage));
        ui.View.Strings = ChatStrings.PortugueseBrazil;
        UiHelpers.Pump();
        Assert.Equal(new[] { "Uso", "Ainda sem dados de uso" }, UiHelpers.Texts(ui.Usage));
        ui.Window.Close();
    }
}
