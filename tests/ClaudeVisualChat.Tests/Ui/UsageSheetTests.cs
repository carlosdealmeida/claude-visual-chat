using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using ClaudeVisualChat.Protocol;
using Xunit;

namespace ClaudeVisualChat.Tests.Ui;

/// <summary>
/// The usage sheet of the touch layout (<see cref="UsageSheet"/>): the context ring of the reply box opens it with the context
/// window by category and, under it, the host's own meters or the plan's limits. It lives from the conversation's
/// <see cref="ChatConversation.ShownPanel"/>, so what the user does to close it ends that state; the desktop's panels stay
/// out of the touch layout, and the desktop layout is as it was.
/// </summary>
public class UsageSheetTests
{
    private sealed class Opened
    {
        public Opened(ChatConversation? conversation = null)
        {
            Conversation = conversation ?? WithContext();
            Conversation.CommandInvoked += (_, e) => Commands.Add(e.Name);
            View = UiHelpers.TouchView(Conversation);
            Window = UiHelpers.ShowPhone(View);
            Sheet = View.GetVisualDescendants().OfType<UsageSheet>().Single();
        }

        public ChatConversation Conversation { get; }
        public ClaudeChatView View { get; }
        public Window Window { get; }
        public UsageSheet Sheet { get; }
        public List<string> Commands { get; } = new List<string>();

        /// <summary>The ring of the touch bar (the desktop bar's, hidden, is in the tree too).</summary>
        public ContextRing Ring => UiHelpers.Composer(View).GetVisualDescendants().OfType<ContextRing>().Single(r => r.Name == "TouchRing");

        public ContextPanel ContextPanel => View.GetVisualDescendants().OfType<ContextPanel>().Single();

        public UsagePanel UsagePanel => View.GetVisualDescendants().OfType<UsagePanel>().Single();

        public List<string> Texts => UiHelpers.Texts(Sheet);

        /// <summary>The card of the context window, which a tap opens and closes.</summary>
        public Button Card => Sheet.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && b.Classes.Contains("ClaudeChatSheetCard"));

        public void OpenFromRing() => UiHelpers.Click(Window, Ring);

        /// <summary>The way the host asks for the panel (the ring's, the palette's), also with no reply box.</summary>
        public void Ask(string command = "context")
        {
            Conversation.InvokeCommand(command, "");
            UiHelpers.Pump();
        }

        public void Tap(Point at)
        {
            Window.MouseMove(at, RawInputModifiers.None);
            Window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
            Window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
            UiHelpers.Pump();
        }
    }

    private static readonly DateTimeOffset Noon = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A conversation on Opus with the context of a long session: 150,600 of 1,000,000 tokens (15 %).</summary>
    private static ChatConversation WithContext()
    {
        var c = TouchSamples.Opus();
        c.Apply(new ContextUsageReported(TouchSamples.Context));
        return c;
    }

    /// <summary>The session limit at 34 % and the week's at 61 %.</summary>
    private static UsageLimitsReported Limits(DateTimeOffset? sessionResets = null, DateTimeOffset? weekResets = null) =>
        new UsageLimitsReported(new UsageLimits(new[]
        {
            new UsageWindow("five_hour", 0.34, sessionResets), new UsageWindow("seven_day", 0.61, weekResets),
        }));

    private static JsonElement J(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static PermissionRequested Permission() =>
        new PermissionRequested("r1", "Bash", "Bash", null, "t1", J("{\"command\":\"ls\"}"), null);

    private static List<Border> WithClass(Visual root, string name) =>
        root.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains(name)).ToList();

    private static Color ColorOf(Control view, string key) => Assert.IsAssignableFrom<ISolidColorBrush>(view.FindResource(key)).Color;

    private static Color Fill(Border border) => Assert.IsAssignableFrom<ISolidColorBrush>(border.Background).Color;

    private static string Clock(DateTimeOffset time, string format) => time.ToLocalTime().ToString(format, CultureInfo.InvariantCulture);

    private static string Day(DateTimeOffset time, string names) => names.Split(' ')[(int)time.ToLocalTime().DayOfWeek];

    /// <summary>What the card says opened: the sheet's texts, then a line per category of <see cref="TouchSamples.Context"/>.</summary>
    private static readonly string[] OpenedPortuguese =
    {
        "Uso", "Contexto", "Janela de contexto", "15% utilizado", "150,6k de 1M tokens",
        "Messages", "113,4k", "11,3%",
        "System tools", "20,4k", "2,0%",
        "Skills", "9,9k", "1,0%",
        "Free space", "816,4k", "81,6%",
        "MCP tools (deferred)", "127,5k", "—",
    };

    // ---- the brief ----

    [AvaloniaFact]
    public void Ring_opens_the_usage_sheet_in_touch()
    {
        TestLog.Instance.Clear();
        var ui = new Opened();
        Assert.False(ui.Sheet.IsOpen);
        Assert.False(ui.Sheet.IsEffectivelyVisible);

        ui.OpenFromRing();
        Assert.True(ui.Sheet.IsOpen);
        Assert.Equal(ChatPanel.Context, ui.Conversation.ShownPanel);
        Assert.Equal(new[] { "Uso", "Contexto", "Janela de contexto", "15% utilizado", "150,6k de 1M tokens" }, ui.Texts);
        Assert.False(ui.ContextPanel.IsEffectivelyVisible);              // the desktop's panels are not the touch layout's
        Assert.False(ui.UsagePanel.IsEffectivelyVisible);
        Assert.Empty(TestLog.Instance.Entries);                          // no binding or property warnings
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Context_card_opens_the_categories()
    {
        var ui = new Opened();
        ui.OpenFromRing();
        Assert.DoesNotContain("Messages", ui.Texts);                     // closed when it opens

        UiHelpers.Click(ui.Window, ui.Card);
        Assert.Equal(OpenedPortuguese, ui.Texts);                        // used ones, the free space, the deferred ones: with "—" for a share

        UiHelpers.Click(ui.Window, ui.Card);                             // and closed again by the same tap
        Assert.DoesNotContain("Messages", ui.Texts);
        Assert.Equal(new[] { "Uso", "Contexto", "Janela de contexto", "15% utilizado", "150,6k de 1M tokens" }, ui.Texts);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Host_content_replaces_the_plan_usage()
    {
        var c = WithContext();
        c.Apply(Limits());
        var ui = new Opened(c);
        ui.View.UsageSheetContent = new TextBlock { Text = "host meters" };
        UiHelpers.Pump();
        ui.OpenFromRing();

        Assert.Equal(new[] { "Uso", "Contexto", "Janela de contexto", "15% utilizado", "150,6k de 1M tokens", "host meters" }, ui.Texts);
        Assert.DoesNotContain("Uso do plano", ui.Texts);

        ui.View.UsageSheetContent = null;                                // taken away while it is open: the plan's limits are back
        UiHelpers.Pump();
        Assert.Contains("Uso do plano", ui.Texts);
        Assert.DoesNotContain("host meters", ui.Texts);

        ui.View.UsageSheetContent = new TextBlock { Text = "other meters" };
        UiHelpers.Pump();
        Assert.Equal("other meters", ui.Texts.Last());
        Assert.DoesNotContain("Uso do plano", ui.Texts);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Without_host_content_the_plan_usage_shows()
    {
        var c = WithContext();
        c.Apply(Limits());
        var ui = new Opened(c);
        ui.OpenFromRing();

        Assert.Equal(new[]
        {
            "Uso", "Contexto", "Janela de contexto", "15% utilizado", "150,6k de 1M tokens",
            "Uso do plano", "Sessão atual (5h)", "34% utilizado", "Semanal (7 dias)", "61% utilizado",
        }, ui.Texts);

        // a bar per window, filled up to its use, in the accent
        var bars = WithClass(ui.Sheet, "ClaudeChatSheetBar");
        Assert.Equal(3, bars.Count);                                     // the context's, then the two windows'
        var fills = WithClass(ui.Sheet, "ClaudeChatUsageFill");
        Assert.Equal(2, fills.Count);
        Assert.Equal(bars[1].Bounds.Width * 0.34, fills[0].Bounds.Width, 0);
        Assert.Equal(bars[2].Bounds.Width * 0.61, fills[1].Bounds.Width, 0);
        Assert.Equal(ColorOf(ui.View, "ClaudeChat.AccentFill"), Fill(fills[0]));
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Refresh_asks_for_the_context_and_the_usage()
    {
        var ui = new Opened();
        ui.OpenFromRing();
        ui.Commands.Clear();                                             // the ring's own "context"

        UiHelpers.Click(ui.Window, UiHelpers.ButtonWithText(ui.Sheet, "Atualizar"));
        Assert.Equal(new[] { "context", "usage" }, ui.Commands);         // the host answers both with fresh data
        Assert.True(ui.Sheet.IsOpen);                                    // and the sheet stays: both panels land on it
        Assert.Equal(ChatPanel.Usage, ui.Conversation.ShownPanel);
        Assert.False(ui.UsagePanel.IsEffectivelyVisible);
        Assert.Contains("Janela de contexto", ui.Texts);

        ui.Commands.Clear();
        UiHelpers.Click(ui.Window, UiHelpers.ButtonWithText(ui.Sheet, "Atualizar"));
        Assert.Equal(new[] { "context", "usage" }, ui.Commands);         // again, from the other panel
        Assert.True(ui.Sheet.IsOpen);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Desktop_keeps_the_panels()
    {
        var c = WithContext();
        var view = new ClaudeChatView { Conversation = c };              // the desktop layout
        var window = UiHelpers.Show(view);
        var sheet = view.GetVisualDescendants().OfType<UsageSheet>().Single();
        var context = view.GetVisualDescendants().OfType<ContextPanel>().Single();
        var usage = view.GetVisualDescendants().OfType<UsagePanel>().Single();

        c.InvokeCommand("context", "");
        UiHelpers.Pump();
        Assert.True(context.IsEffectivelyVisible);
        Assert.Contains("150,600 / 1,000,000 (15%)", UiHelpers.Texts(context));
        Assert.False(sheet.IsOpen);
        Assert.False(sheet.IsEffectivelyVisible);

        c.InvokeCommand("usage", "");
        UiHelpers.Pump();
        Assert.True(usage.IsEffectivelyVisible);
        Assert.False(context.IsVisible);
        Assert.False(sheet.IsOpen);
        window.Close();
    }

    // ---- the sheet lives from the panel: how it closes ----

    [AvaloniaFact]
    public void The_x_the_veil_esc_and_back_end_the_panel_and_the_sheet_never_hides_while_the_panel_is_open()
    {
        var ui = new Opened();
        var unhandled = UiHelpers.UnhandledKeys(ui.Window);
        var panelWhenClosed = new List<ChatPanel>();
        ui.Sheet.Closed += (_, _) => panelWhenClosed.Add(ui.Conversation.ShownPanel);

        ui.OpenFromRing();                                               // the ✕
        Assert.Equal(ChatPanel.Context, ui.Conversation.ShownPanel);
        UiHelpers.Click(ui.Window, UiHelpers.ButtonWithText(ui.Sheet, "Fechar"));
        Assert.Equal(ChatPanel.None, ui.Conversation.ShownPanel);
        Assert.False(ui.Sheet.IsOpen);
        Assert.False(ui.Sheet.IsEffectivelyVisible);

        ui.Ask("usage");                                                 // the veil (the other panel lands on the same sheet)
        Assert.True(ui.Sheet.IsOpen);
        ui.Tap(new Point(UiHelpers.PhoneWidth / 2, 5));
        Assert.Equal(ChatPanel.None, ui.Conversation.ShownPanel);
        Assert.False(ui.Sheet.IsOpen);

        ui.Ask();                                                        // Esc
        Assert.True(ui.Sheet.IsOpen);
        UiHelpers.Press(ui.Window, Key.Escape);
        Assert.Equal(ChatPanel.None, ui.Conversation.ShownPanel);
        Assert.False(ui.Sheet.IsOpen);
        Assert.DoesNotContain(Key.Escape, unhandled);                    // an Esc the sheet took is not the app's

        ui.Ask();                                                        // the host's back button
        Assert.True(ui.Sheet.IsOpen);
        Assert.True(ui.Sheet.Back());
        Assert.Equal(ChatPanel.None, ui.Conversation.ShownPanel);
        Assert.False(ui.Sheet.IsOpen);
        Assert.False(ui.Sheet.Back());                                   // closed: nothing to go back from

        // each time it hid, the panel it lives from was already closed: it never hid and left the panel open to bring it back
        Assert.Equal(new[] { ChatPanel.None, ChatPanel.None, ChatPanel.None, ChatPanel.None }, panelWhenClosed);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_request_that_arrives_ends_the_panel_too_so_that_answering_it_does_not_bring_the_sheet_back()
    {
        var ui = new Opened();
        ui.Ask();
        Assert.True(ui.Sheet.IsOpen);

        ui.Conversation.Apply(Permission());                             // the request comes first: the sheet would cover it
        UiHelpers.Pump();
        Assert.Equal(ChatPanel.None, ui.Conversation.ShownPanel);
        Assert.False(ui.Sheet.IsOpen);

        ui.Conversation.DismissPermission("r1");
        UiHelpers.Pump();
        Assert.False(ui.Sheet.IsOpen);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void While_a_request_waits_the_panel_opens_no_sheet()
    {
        var ui = new Opened();
        ui.Conversation.Apply(Permission());
        UiHelpers.Pump();

        ui.Ask();                                                        // asked for while the claude waits on the user: it would cover the request
        Assert.False(ui.Sheet.IsOpen);
        Assert.False(ui.ContextPanel.IsEffectivelyVisible);
        Assert.True(UiHelpers.Prompt(ui.View).IsEffectivelyVisible);     // what the user has to see
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Another_conversation_and_the_desktop_layout_hide_the_sheet_without_ending_the_panel()
    {
        var a = WithContext();
        var b = TouchSamples.Opus();
        var ui = new Opened(a);
        ui.Ask();
        Assert.True(ui.Sheet.IsOpen);

        ui.View.Conversation = b;                                        // a view of another conversation is not a dismissal
        UiHelpers.Pump();
        Assert.False(ui.Sheet.IsOpen);
        Assert.Equal(ChatPanel.Context, a.ShownPanel);                   // a's panel is as the user left it…
        ui.View.Conversation = a;
        UiHelpers.Pump();
        Assert.True(ui.Sheet.IsOpen);                                    // …and the sheet follows it when a is back
        Assert.Contains("Janela de contexto", ui.Texts);

        b.InvokeCommand("usage", "");                                    // b has a panel of its own open: the view does not show b yet
        UiHelpers.Pump();
        ui.View.Conversation = b;                                        // the sheet is b's now
        UiHelpers.Pump();
        Assert.True(ui.Sheet.IsOpen);
        Assert.Equal(new[] { "Uso", "Contexto", "Ainda sem dados de contexto" }, ui.Texts);
        b.ClosePanel();
        UiHelpers.Pump();
        Assert.False(ui.Sheet.IsOpen);

        ui.View.Conversation = a;
        UiHelpers.Pump();
        Assert.True(ui.Sheet.IsOpen);                                    // the layout: the same state, in the desktop's panel
        ui.View.Layout = ChatLayout.Desktop;
        UiHelpers.Pump();
        Assert.False(ui.Sheet.IsOpen);
        Assert.Equal(ChatPanel.Context, a.ShownPanel);
        Assert.True(ui.ContextPanel.IsEffectivelyVisible);
        ui.View.Layout = ChatLayout.Touch;                               // and back in the sheet
        UiHelpers.Pump();
        Assert.True(ui.Sheet.IsOpen);
        Assert.False(ui.ContextPanel.IsVisible);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_panel_that_is_open_when_the_view_comes_into_the_window_shows_its_sheet()
    {
        var c = WithContext();
        c.InvokeCommand("context", "");                                  // before there is a window to show it in
        var ui = new Opened(c);
        Assert.True(ui.Sheet.IsOpen);
        Assert.Contains("Janela de contexto", ui.Texts);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_panel_that_opens_or_closes_while_the_view_is_out_of_the_window_is_followed_when_it_is_back()
    {
        var c = WithContext();
        var view = UiHelpers.TouchView(c);
        var host = new ContentControl { Content = view };                // a shell that swaps its page
        var window = UiHelpers.ShowPhone(host);
        var sheet = view.GetVisualDescendants().OfType<UsageSheet>().Single();
        var away = new TextBlock { Text = "Tokens" };

        host.Content = away;
        UiHelpers.Pump();
        c.InvokeCommand("context", "");                                  // asked for while nobody listens
        host.Content = view;
        UiHelpers.Pump();
        Assert.True(sheet.IsOpen);                                       // and the sheet is there when the view is back
        Assert.Contains("Janela de contexto", UiHelpers.Texts(sheet));

        host.Content = away;
        UiHelpers.Pump();
        c.ClosePanel();                                                  // closed while it is out: the sheet is not left over
        host.Content = view;
        UiHelpers.Pump();
        Assert.False(sheet.IsOpen);

        c.InvokeCommand("usage", "");
        UiHelpers.Pump();
        Assert.True(sheet.IsOpen);
        host.Content = away;
        UiHelpers.Pump();
        c.Apply(Permission());                                           // a request that came while it was out: the sheet would cover it
        host.Content = view;
        UiHelpers.Pump();
        Assert.False(sheet.IsOpen);
        Assert.Equal(ChatPanel.None, c.ShownPanel);                      // it ended the panel, so the sheet does not come back by itself

        // the panel closed while the view was out too, and then a request came: there is no panel left to end, and the sheet is
        // not left open over the request with nothing but itself to close it
        c.DismissPermission("r1");
        UiHelpers.Pump();
        c.InvokeCommand("context", "");
        UiHelpers.Pump();
        Assert.True(sheet.IsOpen);
        host.Content = away;
        UiHelpers.Pump();
        c.ClosePanel();
        c.Apply(Permission());
        host.Content = view;
        UiHelpers.Pump();
        Assert.False(sheet.IsOpen);
        Assert.Equal(ChatPanel.None, c.ShownPanel);
        Assert.NotNull(c.CurrentPermission);                             // the request is still there for the user to answer
        window.Close();
    }

    [AvaloniaFact]
    public void It_shows_in_a_read_only_view_too()
    {
        var ui = new Opened();
        ui.OpenFromRing();
        ui.View.IsReadOnly = true;                                       // the reply box goes away; this is information
        UiHelpers.Pump();
        Assert.True(ui.Sheet.IsOpen);

        ui.Sheet.Back();
        Assert.False(ui.Sheet.IsOpen);
        ui.Ask();                                                        // asked for by the host: there is no ring in a read-only view
        Assert.True(ui.Sheet.IsOpen);
        Assert.Contains("Janela de contexto", ui.Texts);
        ui.Window.Close();
    }

    // ---- what it shows ----

    [AvaloniaFact]
    public void Without_context_data_it_says_so_and_waits_for_it_while_the_claude_works()
    {
        var c = TouchSamples.Opus();                                     // no data yet, and idle
        var ui = new Opened(c);
        ui.Ask();
        Assert.Equal(new[] { "Uso", "Contexto", "Ainda sem dados de contexto" }, ui.Texts);
        Assert.Empty(WithClass(ui.Sheet, "ClaudeChatSheetBar"));

        c.Submit("go");                                                  // busy: the data is on its way
        UiHelpers.Pump();
        Assert.Equal(new[] { "Uso", "Contexto", "…" }, ui.Texts);

        c.Apply(new ContextUsageReported(TouchSamples.Context));         // the answer comes while it is open
        UiHelpers.Pump();
        Assert.Equal(new[] { "Uso", "Contexto", "Janela de contexto", "15% utilizado", "150,6k de 1M tokens" }, ui.Texts);
        Assert.Single(WithClass(ui.Sheet, "ClaudeChatSheetBar"));
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Without_a_window_the_card_has_the_share_the_claude_gave_and_no_share_per_category()
    {
        var c = TouchSamples.Opus();
        c.Apply(new ContextUsageReported(new ContextUsage(150_600, 0, 15, TouchSamples.Context.Categories)));
        var ui = new Opened(c);
        ui.Ask();
        Assert.Equal(new[] { "Uso", "Contexto", "Janela de contexto", "15% utilizado" }, ui.Texts);   // no "of 0 tokens"

        UiHelpers.Click(ui.Window, ui.Card);
        Assert.Equal(new[]
        {
            "Uso", "Contexto", "Janela de contexto", "15% utilizado",
            "Messages", "113,4k", "—", "System tools", "20,4k", "—", "Skills", "9,9k", "—",
            "Free space", "816,4k", "—", "MCP tools (deferred)", "127,5k", "—",
        }, ui.Texts);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void The_plan_windows_say_when_they_renew_and_a_list_with_none_shows_nothing()
    {
        var sessionResets = Noon.AddHours(3);
        var weekResets = Noon.AddDays(2);
        var c = WithContext();
        c.Clock = () => sessionResets;                                   // the session renews today, the week days later
        c.Apply(Limits(sessionResets, weekResets));
        var ui = new Opened(c);
        ui.OpenFromRing();

        Assert.Equal(new[]
        {
            "Uso", "Contexto", "Janela de contexto", "15% utilizado", "150,6k de 1M tokens",
            "Uso do plano",
            "Sessão atual (5h)", "34% utilizado", "Renova " + Clock(sessionResets, "HH:mm"),
            "Semanal (7 dias)", "61% utilizado", "Renova " + Day(weekResets, "dom seg ter qua qui sex sáb") + " " + Clock(weekResets, "HH:mm"),
        }, ui.Texts);

        c.Apply(new UsageLimitsReported(new UsageLimits(new UsageWindow[0])));
        UiHelpers.Pump();
        Assert.Equal(new[] { "Uso", "Contexto", "Janela de contexto", "15% utilizado", "150,6k de 1M tokens" }, ui.Texts);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Texts_follow_the_language_also_while_the_sheet_is_open()
    {
        var c = WithContext();
        c.Apply(Limits());
        var ui = new Opened(c);
        ui.View.Strings = ChatStrings.English;
        UiHelpers.Pump();
        ui.OpenFromRing();
        UiHelpers.Click(ui.Window, ui.Card);
        Assert.Equal(new[]
        {
            "Usage", "Context", "Context window", "15% used", "150.6k of 1M tokens",
            "Messages", "113.4k", "11.3%", "System tools", "20.4k", "2.0%", "Skills", "9.9k", "1.0%",
            "Free space", "816.4k", "81.6%", "MCP tools (deferred)", "127.5k", "—",
            "Plan usage", "Current session (5h)", "34% used", "Weekly (7 days)", "61% used",
        }, ui.Texts);
        var names = UiHelpers.ButtonNames(ui.Sheet);
        Assert.Contains("Close", names);
        Assert.Contains("Refresh", names);
        Assert.Contains("Context window, 15% used, 150.6k of 1M tokens", names);      // what a screen reader says of the card

        ui.View.Strings = ChatStrings.PortugueseBrazil;                  // changed while it is open: the categories stay open
        UiHelpers.Pump();
        Assert.Equal("Uso", ui.Texts[0]);
        Assert.Contains("113,4k", ui.Texts);
        Assert.Contains("Uso do plano", ui.Texts);
        names = UiHelpers.ButtonNames(ui.Sheet);
        Assert.Contains("Fechar", names);
        Assert.Contains("Atualizar", names);
        Assert.Contains("Janela de contexto, 15% utilizado, 150,6k de 1M tokens", names);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void The_categories_stay_open_while_the_data_changes_and_start_closed_again_when_the_sheet_reopens()
    {
        var c = WithContext();
        var ui = new Opened(c);
        ui.OpenFromRing();
        UiHelpers.Click(ui.Window, ui.Card);
        Assert.Contains("Messages", ui.Texts);

        c.Apply(new ContextUsageReported(new ContextUsage(500_000, 1_000_000, 50, new[]
        {
            new ContextCategory("Messages", 400_000, "used"), new ContextCategory("Free space", 500_000, "free"),
        })));
        UiHelpers.Pump();
        Assert.Equal(new[]
        {
            "Uso", "Contexto", "Janela de contexto", "50% utilizado", "500k de 1M tokens",
            "Messages", "400,0k", "40,0%", "Free space", "500,0k", "50,0%",   // the caption drops the ",0"; the table keeps its decimals aligned
        }, ui.Texts);

        ui.Sheet.Back();                                                 // closed, and opened again: the card is closed
        ui.Ask();
        Assert.DoesNotContain("Messages", ui.Texts);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void The_card_draws_the_bar_by_category_and_a_chevron_that_turns()
    {
        var ui = new Opened();
        ui.OpenFromRing();
        Assert.Equal(new[] { "Uso", "Contexto", "Janela de contexto", "15% utilizado", "150,6k de 1M tokens" }, ui.Texts);

        // the bar: 6 px, round, a piece per used category in the colors of the /context panel, the rest is the track
        var bar = Assert.Single(WithClass(ui.Sheet, "ClaudeChatSheetBar"));
        Assert.Equal(6, bar.Bounds.Height);
        Assert.Equal(new CornerRadius(3), bar.CornerRadius);
        Assert.Same(ui.View.FindResource("ClaudeChat.PillHoverBackground"), bar.Background);
        var pieces = WithClass(ui.Sheet, "ClaudeChatContextSegment");
        var used = new[] { 113_400L, 20_400, 9_900 };
        Assert.Equal(used.Length, pieces.Count);                         // the deferred category is not in the bar
        for (var i = 0; i < used.Length; i++)
        {
            var expected = bar.Bounds.Width * used[i] / 1_000_000;
            Assert.InRange(pieces[i].Bounds.Width, expected - 1, expected + 1);
            Assert.Equal(ColorOf(ui.View, "ClaudeChat.ContextColor" + (i + 1)), Fill(pieces[i]));
        }
        Assert.Equal(0, pieces[0].TranslatePoint(default, bar)!.Value.X, 0);
        Assert.True(pieces.Sum(p => p.Bounds.Width) < bar.Bounds.Width / 2);          // the free window is the bare track after them

        // the text of the card: the name, the share and the tokens; the chevron points down, and up once opened
        var caption = ui.Sheet.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "150,6k de 1M tokens");
        Assert.Equal(12, caption.FontSize);
        Assert.Same(ui.View.FindResource("ClaudeChat.ForegroundMuted"), caption.Foreground);
        var chevron = ui.Card.GetVisualDescendants().OfType<Path>().Single(p => p.Classes.Contains("ClaudeChatSheetChevron"));
        Assert.True(chevron.RenderTransform!.Value.M12 > 0.5, "down");
        UiHelpers.Click(ui.Window, ui.Card);
        Assert.True(chevron.RenderTransform!.Value.M12 < -0.5, "up");
        UiHelpers.Click(ui.Window, ui.Card);
        Assert.True(chevron.RenderTransform!.Value.M12 > 0.5, "down again");
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_category_has_its_dot_of_8_the_free_space_in_the_color_of_the_track()
    {
        var ui = new Opened();
        ui.OpenFromRing();
        UiHelpers.Click(ui.Window, ui.Card);

        var bar = Assert.Single(WithClass(ui.Sheet, "ClaudeChatSheetBar"));
        var dots = WithClass(ui.Sheet, "ClaudeChatSheetDot");
        Assert.Equal(5, dots.Count);                                     // a dot per category, deferred ones too
        Assert.All(dots, d => Assert.Equal(new Size(8, 8), d.Bounds.Size));
        Assert.All(dots, d => Assert.Equal(new CornerRadius(4), d.CornerRadius));
        for (var i = 0; i < 3; i++)
            Assert.Equal(ColorOf(ui.View, "ClaudeChat.ContextColor" + (i + 1)), Fill(dots[i]));   // as the bar's pieces
        Assert.Same(bar.Background, dots[3].Background);                 // the free space: the track's color
        Assert.Null(dots[4].Background);                                 // the deferred one: none, as in the /context panel

        // the columns line up from one category to the next: the tokens and the shares end at the same place
        var cells = ui.Sheet.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("ClaudeChatSheetAmount")).ToList();
        Assert.Equal(10, cells.Count);                                   // the tokens, then the share, of five categories
        var rights = cells.Select(t => Math.Round(t.TranslatePoint(new Point(t.Bounds.Width, 0), ui.Sheet)!.Value.X)).ToList();
        Assert.NotEqual(rights[0], rights[1]);
        for (var i = 2; i < rights.Count; i++) Assert.Equal(rights[i - 2], rights[i]);
        ui.Window.Close();
    }

    // ---- the header, the targets, the keyboard ----

    [AvaloniaFact]
    public void The_header_has_the_x_the_title_and_the_refresh_action_in_targets_of_44()
    {
        var ui = new Opened();
        ui.OpenFromRing();
        UiHelpers.Click(ui.Window, ui.Card);                             // with the categories open too

        var header = ui.Sheet.GetVisualDescendants().OfType<Grid>().Single(g => g.Classes.Contains("ClaudeChatSheetHeader"));
        Assert.Equal("Uso", ui.Sheet.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("ClaudeChatSheetTitle")).Text);
        var close = UiHelpers.ButtonWithText(header, "Fechar");
        var refresh = UiHelpers.ButtonWithText(header, "Atualizar");
        Assert.Equal(new Size(44, 44), close.Bounds.Size);
        Assert.Equal(new Size(44, 44), refresh.Bounds.Size);
        Assert.Equal(0, close.TranslatePoint(default, header)!.Value.X, 1);
        Assert.Equal(header.Bounds.Width, refresh.TranslatePoint(new Point(44, 0), header)!.Value.X, 1);
        var icon = Assert.Single(refresh.GetVisualDescendants().OfType<Path>());
        Assert.Equal(refresh.FindResource("ClaudeChat.Icon.Refresh"), icon.Data);

        Assert.Equal(new[] { "Fechar", "Atualizar" }, UiHelpers.ButtonNames(header));
        Assert.All(ui.Sheet.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible),
            b => Assert.True(b.Bounds.Width >= 44 && b.Bounds.Height >= 44, AutomationProperties.GetName(b)));
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void The_keyboard_goes_into_the_sheet_and_back_to_the_ring()
    {
        var ui = new Opened();
        UiHelpers.Click(ui.Window, ui.Ring);
        Assert.True(ui.Sheet.IsKeyboardFocusWithin);                     // not left on the ring under the veil

        UiHelpers.Click(ui.Window, ui.Card);                             // the card that was pressed stays: the categories open in place
        Assert.True(ui.Sheet.IsKeyboardFocusWithin);
        Assert.True(ui.Card.IsFocused);

        UiHelpers.Press(ui.Window, Key.Escape);
        Assert.False(ui.Sheet.IsOpen);
        Assert.True(ui.Ring.IsFocused);                                  // as the other sheets give it back to where it was
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void The_body_scrolls_when_the_hosts_meters_are_long()
    {
        var c = WithContext();
        c.Apply(Limits());
        var ui = new Opened(c);
        ui.View.UsageSheetContent = new Border { Height = 900 };         // more than a screen of the host's meters
        UiHelpers.Pump();
        ui.OpenFromRing();

        var panel = WithClass(ui.Sheet, "ClaudeChatSheetPanel").Single();
        Assert.InRange(panel.Bounds.Height, 300, ui.Sheet.Bounds.Height * 0.92 + 0.5);   // under 92 % of the view
        var scroller = ui.Sheet.GetVisualDescendants().OfType<ScrollViewer>().First();
        Assert.True(scroller.Extent.Height > scroller.Viewport.Height);
        ui.Window.Close();
    }

    // ---- the layer, and what the host gives ----

    [AvaloniaFact]
    public void A_closed_sheet_is_an_empty_hidden_layer_over_the_other_sheets_and_under_the_preview()
    {
        var ui = new Opened();
        ui.View.UsageSheetContent = new TextBlock { Text = "host meters" };   // the host's content is in the tree only while the sheet is open
        UiHelpers.Pump();
        Assert.Empty(ui.Sheet.GetVisualDescendants());
        Assert.Null(ui.Sheet.Child);
        Assert.False(ui.Sheet.IsVisible);
        Assert.Equal(3, Grid.GetRowSpan(ui.Sheet));

        var layers = ((Grid)ui.Sheet.Parent!).Children;
        var at = layers.IndexOf(ui.Sheet);
        Assert.True(layers.IndexOf(layers.OfType<AgentMapPanel>().Single()) < at);                                                      // over the Agent map…
        Assert.All(layers.OfType<ChatSheet>().Where(s => !ReferenceEquals(s, ui.Sheet)), s => Assert.True(layers.IndexOf(s) < at));   // …and the other sheets…
        Assert.True(layers.IndexOf(layers.OfType<ImagePreview>().Single()) > at);                                                       // …under the preview of an image

        ui.OpenFromRing();
        Assert.NotEmpty(ui.Sheet.GetVisualDescendants());
        Assert.Same(ui.Sheet, UiHelpers.HitTest(ui.Window, new Point(5, 5)));   // the veil takes what is under it
        Assert.True(ui.Sheet.Back());
        Assert.Empty(ui.Sheet.GetVisualDescendants());                   // and is emptied again
        Assert.Equal("host meters", ((TextBlock)ui.View.UsageSheetContent!).Text);   // the host's content is the host's
        ui.OpenFromRing();
        Assert.Contains("host meters", ui.Texts);                        // and is there again when the sheet opens
        ui.Window.Close();
    }

    private sealed record HostMeters(string Label);

    [AvaloniaFact]
    public void Host_content_that_is_not_a_control_is_resolved_by_the_hosts_templates()
    {
        var ui = new Opened();
        ui.View.DataTemplates.Add(new FuncDataTemplate<HostMeters>((meters, _) => new TextBlock { Text = "meters: " + meters!.Label }));
        ui.View.UsageSheetContent = new HostMeters("session 34%");
        UiHelpers.Pump();
        ui.OpenFromRing();
        Assert.Contains("meters: session 34%", ui.Texts);

        ui.View.UsageSheetContent = new HostMeters("session 35%");       // another one, while it is open
        UiHelpers.Pump();
        Assert.Contains("meters: session 35%", ui.Texts);
        Assert.DoesNotContain("meters: session 34%", ui.Texts);

        ui.View.Strings = ChatStrings.English;                           // the sheet is drawn again: the content is not made twice
        UiHelpers.Pump();
        Assert.Single(ui.Texts, t => t == "meters: session 35%");
        ui.Window.Close();
    }
}
