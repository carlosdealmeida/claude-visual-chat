using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using ClaudeVisualChat.Protocol;
using Xunit;

namespace ClaudeVisualChat.Tests.Ui;

/// <summary>The "Context usage" and "Account &amp; Usage" modals that /context and /usage open.</summary>
public class UsagePanelTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

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

        public static List<T> WithClass<T>(Visual root, string name) where T : Control =>
            root.GetVisualDescendants().OfType<T>().Where(b => b.Classes.Contains(name)).ToList();

        public static List<Border> Borders(Visual root, string name) => WithClass<Border>(root, name);
    }

    private static object? Resource(Control view, string key) => view.TryFindResource(key, out var value) ? value : null;

    private static Color ColorOf(Control view, string key) => Assert.IsAssignableFrom<ISolidColorBrush>(Resource(view, key)).Color;

    private static Color Fill(Border border) => Assert.IsAssignableFrom<ISolidColorBrush>(border.Background).Color;

    private static TextBlock Block(Visual root, string text) => root.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == text);

    private static ContextUsage Usage(int categories, long each = 100, long max = 10000, string kind = "used") =>
        new ContextUsage(
            each * categories, max, 1,
            Enumerable.Range(1, categories).Select(i => new ContextCategory("Category " + i, each, kind)).ToList());

    /// <summary>r5's order of the bar: orange, blue, green, yellow, purple, red, cyan, the foreground: the numbers of the colors that are those.</summary>
    private static readonly int[] R5Order = { 3, 1, 4, 5, 2, 6, 7, 8 };

    private static UsageLimits Limits(params UsageWindow[] windows) => new UsageLimits(windows);

    // ---- the protocol ----

    [Fact]
    public void The_context_answer_reads_model_memory_files_and_agents_when_present()
    {
        const string body =
            "{\"categories\":[{\"name\":\"Messages\",\"tokens\":10,\"kind\":\"used\"}],\"totalTokens\":10,\"maxTokens\":100,\"percentage\":10,"
            + "\"model\":\"claude-opus-5-5\","
            + "\"memoryFiles\":[{\"path\":\"/home/u/.claude/CLAUDE.md\",\"type\":\"User\",\"tokens\":120},{\"path\":\"CLAUDE.md\",\"tokens\":30}],"
            + "\"agents\":[{\"agentType\":\"reviewer\",\"source\":\"projectSettings\",\"tokens\":55}]}";
        string Reply(string response) =>
            "{\"type\":\"control_response\",\"response\":{\"subtype\":\"success\",\"request_id\":\"q\",\"response\":" + response + "}}";

        var usage = new StreamJsonParser().Parse(Reply(body)).OfType<ContextUsageReported>().Single().Usage;
        Assert.Equal("claude-opus-5-5", usage.Model);
        Assert.Equal(new[] { new ContextFile("/home/u/.claude/CLAUDE.md", 120), new ContextFile("CLAUDE.md", 30) }, usage.MemoryFiles);
        Assert.Equal(new[] { new ContextAgent("reviewer", 55) }, usage.Agents);

        // what a host strips does not arrive: the lists are empty and the model is none
        var bare = new StreamJsonParser().Parse(Reply("{\"categories\":[],\"totalTokens\":0,\"maxTokens\":100,\"percentage\":0}"))
            .OfType<ContextUsageReported>().Single().Usage;
        Assert.Null(bare.Model);
        Assert.Empty(bare.MemoryFiles);
        Assert.Empty(bare.Agents);

        // the account of the initialize reply: the login is told from tokenSource, apiKeySource and apiProvider, the plan from subscriptionType
        AccountInfo Account(string fields) => new StreamJsonParser().Parse(Reply("{\"commands\":[],\"account\":{" + fields + "}}"))
            .OfType<AccountReported>().Single().Account;
        Assert.Equal(new AccountInfo("claudeai", "a@b.co", "Org", "Max"),
            Account("\"email\":\"a@b.co\",\"organization\":\"Org\",\"subscriptionType\":\"max\",\"tokenSource\":\"claude.ai\",\"apiProvider\":\"firstParty\""));
        Assert.Equal("claudeai", Account("\"email\":\"x\",\"tokenSource\":\"CLAUDE_CODE_OAUTH_TOKEN\"").AuthMethod);
        Assert.Equal("api-key", Account("\"email\":\"x\",\"tokenSource\":\"none\",\"apiKeySource\":\"ANTHROPIC_API_KEY\"").AuthMethod);
        Assert.Equal("bedrock", Account("\"email\":\"x\",\"apiProvider\":\"bedrock\",\"apiKeySource\":\"ANTHROPIC_API_KEY\"").AuthMethod);
        Assert.Equal("vertex", Account("\"email\":\"x\",\"apiProvider\":\"vertex\"").AuthMethod);
        Assert.Null(Account("\"email\":\"x\",\"tokenSource\":\"none\"").AuthMethod);          // none or unknown: no login form, never the raw text
        Assert.Null(Account("\"email\":\"x\",\"tokenSource\":\"something-new\"").AuthMethod);
        Assert.Equal("Team", Account("\"email\":\"x\",\"subscriptionType\":\"team\"").Plan);
        Assert.Null(Account("\"email\":\"x\",\"subscriptionType\":\"none\"").Plan);
        var failed = new StreamJsonParser().Parse(
            "{\"type\":\"control_response\",\"response\":{\"subtype\":\"error\",\"request_id\":\"q\",\"error\":\"boom\"}}");
        Assert.Equal("boom", failed.OfType<ControlAcknowledged>().Single().Error);

        // the limits of the get_usage reply the host passes on: 0 to 100, ISO dates; the model's and the Sonnet's only come this way
        var limits = new StreamJsonParser().Parse(Reply(
            "{\"rate_limits_available\":true,\"rate_limits\":{\"five_hour\":{\"utilization\":50,\"resets_at\":\"2026-10-26T13:00:00Z\"},"
            + "\"seven_day\":{\"utilization\":12.5,\"resets_at\":null},\"seven_day_sonnet\":{\"utilization\":3,\"resets_at\":\"2026-10-29T00:00:00+00:00\"},"
            + "\"model_scoped\":[{\"display_name\":\"Opus\",\"utilization\":25,\"resets_at\":\"2026-10-29T00:00:00Z\"},{\"display_name\":\"Haiku\",\"utilization\":null,\"resets_at\":null}]}}"))
            .OfType<UsageLimitsReported>().Single().Limits;
        Assert.Equal(new[] { "five_hour", "seven_day", "seven_day_sonnet", "model_scoped" }, limits.Windows.Select(w => w.Name).ToArray());
        Assert.Equal(0.5, limits.Windows[0].Utilization, 6);
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 13, 0, 0, TimeSpan.Zero), limits.Windows[0].ResetsAt);
        Assert.Equal(0.125, limits.Windows[1].Utilization, 6);
        Assert.Null(limits.Windows[1].ResetsAt);
        Assert.Equal(0.03, limits.Windows[2].Utilization, 6);
        Assert.Equal("Opus", limits.Windows[3].Model);
        Assert.Equal(0.25, limits.Windows[3].Utilization, 6);                                       // a model without a figure is left out
        // a rate_limit_event has no model_scoped: it is not read there
        var fromEvent = new StreamJsonParser().Parse(
            "{\"type\":\"rate_limit_event\",\"rate_limit_info\":{\"unifiedWindows\":{\"five_hour\":{\"utilization\":0.5,\"resetsAt\":1792698600}},"
            + "\"model_scoped\":[{\"model\":\"Opus\",\"utilization\":0.25}]}}").OfType<UsageLimitsReported>().Single().Limits;
        Assert.Single(fromEvent.Windows);

        // the turn's API time
        var turn = new StreamJsonParser().Parse(
            "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"duration_ms\":2000,\"duration_api_ms\":1500,\"total_cost_usd\":0.01}")
            .OfType<TurnCompleted>().Single();
        Assert.Equal(1500, turn.ApiDurationMs);
        Assert.Equal(2000, turn.DurationMs);
    }

    [Fact]
    public void The_get_usage_reply_merges_with_the_rate_limit_events()
    {
        var c = new ChatConversation();
        c.Apply(new UsageLimitsReported(Limits(
            new UsageWindow("five_hour", 0.5, null), new UsageWindow("seven_day_sonnet", 0.3, null), new UsageWindow("model_scoped", 0.2, null) { Model = "Opus" })));
        c.Apply(new UsageLimitsReported(Limits(new UsageWindow("five_hour", 0.6, null)))); // a rate_limit_event after
        Assert.Equal(3, c.UsageLimits!.Windows.Count);
        Assert.Equal(0.6, c.UsageLimits.Windows.Single(w => w.Name == "five_hour").Utilization);
        Assert.Contains(c.UsageLimits.Windows, w => w.Model == "Opus");
    }

    [Fact]
    public void A_path_in_any_home_folder_is_written_with_a_tilde()
    {
        Assert.Equal("~/x.md", ContextPanel.Tilde("/home/u/x.md"));
        Assert.Equal("~/y.md", ContextPanel.Tilde("/Users/ana/y.md"));
        Assert.Equal("~/z.md", ContextPanel.Tilde("/root/z.md"));
        Assert.Equal("~\\z.md", ContextPanel.Tilde("C:\\Users\\ana\\z.md"));
        Assert.Equal("/opt/w.md", ContextPanel.Tilde("/opt/w.md"));
        Assert.Equal("CLAUDE.md", ContextPanel.Tilde("CLAUDE.md"));
    }

    [Fact]
    public void The_session_cost_follows_the_running_total_of_the_process_and_adds_a_new_one()
    {
        var c = new ChatConversation();
        c.Apply(new TurnCompleted("success", false, 1000, 0.05, 1, 1) { ApiDurationMs = 800 });
        c.Apply(new TurnCompleted("success", false, 2000, 0.12, 1, 1) { ApiDurationMs = 2300 }); // the process's totals: 0.07 and 1500 ms more
        Assert.Equal(0.12, c.SessionCostUsd, 6);
        Assert.Equal(3000, c.SessionDurationMs);   // the turns' own time, added up: the wall time
        Assert.Equal(2300, c.SessionApiDurationMs);
        c.Apply(new TurnCompleted("success", false, 500, 0.03, 1, 1) { ApiDurationMs = 400 });  // lower: a new process, which adds
        Assert.Equal(0.15, c.SessionCostUsd, 6);
        Assert.Equal(2700, c.SessionApiDurationMs);
        c.Apply(new TurnCompleted("success", false, 0, 0.03, 1, 1) { ApiDurationMs = 0 });      // a turn that never reached the API
        c.Apply(new TurnCompleted("success", false, null, null, 1, 1));
        Assert.Equal(0.15, c.SessionCostUsd, 6);
        Assert.Equal(2700, c.SessionApiDurationMs);
        Assert.Equal(3500, c.SessionDurationMs);
    }

    // ---- "Context usage" ----

    [AvaloniaFact]
    public void Context_usage_is_a_modal_with_free_space_as_a_row_and_no_deferred_categories()
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

        // a modal: the backdrop covers the view, the dialog is the side bar's with its frame
        Assert.InRange(panel.Bounds.Width, ui.View.Bounds.Width - 1, ui.View.Bounds.Width + 1);
        Assert.InRange(panel.Bounds.Height, ui.View.Bounds.Height - 1, ui.View.Bounds.Height + 1);
        Assert.Equal(ColorOf(ui.View, "ClaudeChat.ModalOverlay"), Fill(panel));
        var dialog = panel.Dialog;
        Assert.Equal(440, dialog.MaxWidth);
        Assert.True(dialog.Bounds.Width <= 440);
        Assert.Equal(new CornerRadius(8), dialog.CornerRadius);
        Assert.Equal(new Thickness(1), dialog.BorderThickness);
        Assert.Equal(new Thickness(16), dialog.Padding);
        Assert.Equal(ColorOf(ui.View, "ClaudeChat.Background"), Fill(dialog));
        var viewCenter = ui.View.Bounds.Width / 2;
        Assert.Equal(viewCenter, dialog.TranslatePoint(default, ui.View)!.Value.X + dialog.Bounds.Width / 2, 0);

        var texts = UiHelpers.Texts(panel);
        Assert.Equal("Context usage", texts[0]);
        var title = Block(panel, "Context usage");
        Assert.Equal(16, title.FontSize);
        Assert.Equal(FontWeight.SemiBold, title.FontWeight);
        Assert.Contains("36,174 / 200,000 tokens (18%)", texts);
        Assert.Equal(new[] { "Close" }, UiHelpers.ButtonNames(panel));

        // the bar: 8 px, round, a piece per category that is neither deferred nor free, in the colors in turn
        var bar = Assert.Single(Opened.Borders(panel, "ClaudeChatContextBar"));
        Assert.Equal(8, bar.Bounds.Height);
        Assert.Equal(new CornerRadius(4), bar.CornerRadius);
        var pieces = Opened.Borders(panel, "ClaudeChatContextSegment");
        var used = new[] { 6579L, 23669, 408, 1888, 3630 };
        Assert.Equal(used.Length, pieces.Count);
        for (var i = 0; i < used.Length; i++)
        {
            Assert.Equal(bar.Bounds.Width * used[i] / 200000, pieces[i].Bounds.Width, 0);
            Assert.Equal(ColorOf(ui.View, "ClaudeChat.ContextColor" + R5Order[i]), Fill(pieces[i]));
        }

        // the table: its header, a row per category in the window, then the free space with an empty swatch; no deferred category
        var header = Block(panel, "CATEGORY");
        Assert.Equal(11, header.FontSize);
        Assert.Equal(0.5, header.Opacity);
        Assert.Contains("TOKENS", texts);
        Assert.Contains("USAGE", texts);
        var rows = Opened.WithClass<Grid>(panel, "ClaudeChatModalTableRow");
        Assert.Equal(
            new[]
            {
                "System prompt|6,579|3.3%", "System tools|23,669|11.8%", "Memory files|408|0.2%", "Skills|1,888|0.9%",
                "Messages|3,630|1.8%", "Free space|163,826|81.9%",
            },
            rows.Select(r => string.Join("|", UiHelpers.Texts(r))).ToArray());
        Assert.DoesNotContain(texts, t => t.Contains("deferred"));
        var swatches = rows.Select(r => Opened.Borders(r, "ClaudeChatContextSwatch").Single()).ToList();
        for (var i = 0; i < used.Length; i++) Assert.Equal(Fill(pieces[i]), Fill(swatches[i]));
        Assert.Null(swatches[5].Background);                        // the free space's swatch is empty
        var name = Block(rows[0], "System prompt");
        Assert.Equal(13, name.FontSize);
        Assert.Equal(0.7, Block(rows[0], "6,579").Opacity);
        Assert.Equal(12, Block(rows[0], "3.3%").FontSize);
        Assert.Equal(0.5, Block(rows[0], "3.3%").Opacity);
        Assert.DoesNotContain("MEMORY FILES", texts);               // the lists show only when the claude sent them
        Assert.DoesNotContain("CUSTOM AGENTS", texts);

        // in Portuguese
        ui.View.Strings = ChatStrings.PortugueseBrazil;
        UiHelpers.Pump();
        texts = UiHelpers.Texts(panel);
        Assert.Equal("Uso do contexto", texts[0]);
        Assert.Contains("36.174 / 200.000 tokens (18 %)", texts);
        Assert.Contains("CATEGORIA", texts);
        Assert.Equal(new[] { "Fechar" }, UiHelpers.ButtonNames(panel));
        ui.View.Strings = ChatStrings.English;
        UiHelpers.Pump();

        // the colors are eight and come round: the ninth category has the first's
        c.Apply(new ContextUsageReported(Usage(9)));
        UiHelpers.Pump();
        pieces = Opened.Borders(panel, "ClaudeChatContextSegment");
        Assert.Equal(9, pieces.Count);
        for (var i = 0; i < 8; i++) Assert.Equal(ColorOf(ui.View, "ClaudeChat.ContextColor" + R5Order[i]), Fill(pieces[i]));
        Assert.Equal(ColorOf(ui.View, "ClaudeChat.ContextColor3"), Fill(pieces[0]));      // the first category is orange
        Assert.Equal(Fill(pieces[0]), Fill(pieces[8]));

        // a category of a kind it does not know (2.1.312's "buffer") is drawn like the others; one without tokens is not listed
        c.Apply(new ContextUsageReported(new ContextUsage(
            30, 100, 30, new[] { new ContextCategory("Messages", 20, "used"), new ContextCategory("Autocompact buffer", 10, "buffer"), new ContextCategory("Empty", 0, "used") })));
        UiHelpers.Pump();
        Assert.Equal(2, Opened.Borders(panel, "ClaudeChatContextSegment").Count);
        Assert.Contains("Autocompact buffer", UiHelpers.Texts(panel));
        Assert.DoesNotContain("Empty", UiHelpers.Texts(panel));

        // a category under a tenth of a percent
        c.Apply(new ContextUsageReported(new ContextUsage(5, 10000, 0, new[] { new ContextCategory("Tiny", 5, "used") })));
        UiHelpers.Pump();
        Assert.Contains("<0.1%", UiHelpers.Texts(panel));
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Memory_files_list_shows_the_top_five()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var inHome = System.IO.Path.Combine(home, ".claude", "CLAUDE.md");
        var c = new ChatConversation();
        c.Apply(new ContextUsageReported(Usage(2).WithLists(
            "claude-opus-5-5",
            new[]
            {
                new ContextFile("a.md", 1), new ContextFile("b.md", 2), new ContextFile("c.md", 3), new ContextFile("d.md", 4),
                new ContextFile("e.md", 5), new ContextFile("f.md", 6), new ContextFile(inHome, 1200),
            },
            Enumerable.Range(1, 7).Select(i => new ContextAgent("agent-" + i, (8 - i) * 100)).ToArray())));
        var ui = new Opened(c);
        c.InvokeCommand("context", "");
        UiHelpers.Pump();
        var panel = ui.Context;

        var texts = UiHelpers.Texts(panel);
        Assert.Contains("MEMORY FILES", texts);
        Assert.Contains("CUSTOM AGENTS", texts);
        Assert.Contains("claude-opus-5-5", texts);                 // the model, over the total
        var files = Opened.WithClass<Grid>(panel, "ClaudeChatModalListRow");
        Assert.Equal(10, files.Count);                              // five and five
        var memory = files.Take(5).Select(r => UiHelpers.Texts(r)[0]).ToArray();
        Assert.Equal(new[] { "~" + inHome.Substring(home.Length), "f.md", "e.md", "d.md", "c.md" }, memory);   // the biggest five, the biggest first
        Assert.Equal("1.2k", UiHelpers.Texts(files[0])[1]);
        Assert.Equal(new[] { "agent-1", "agent-2", "agent-3", "agent-4", "agent-5" }, files.Skip(5).Select(r => UiHelpers.Texts(r)[0]).ToArray());
        Assert.DoesNotContain("a.md", texts);
        Assert.Contains("/memory", texts);
        Assert.Contains("/agents", texts);
        Assert.DoesNotContain("agent-6", texts);
        Assert.Equal(11, Block(panel, "MEMORY FILES").FontSize);
        Assert.Equal(12, Block(files[1], "f.md").FontSize);

        // the model's name when the session offers it
        c.Apply(new ModelsAvailable(new[] { new ChatModel("opus", "claude-opus-5-5", "Opus 5.5", "", new string[0]) }));
        UiHelpers.Pump();
        Assert.Contains("Opus 5.5", UiHelpers.Texts(panel));
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Context_usage_waits_for_its_data_and_says_when_it_failed()
    {
        var c = new ChatConversation();
        var ui = new Opened(c);
        c.InvokeCommand("context", "");
        UiHelpers.Pump();
        // idle (a transcript opened from a file, say): no "Loading" that would never end
        Assert.Equal(new[] { "Context usage", "No context data yet" }, UiHelpers.Texts(ui.Context));
        c.Submit("go"); // busy: the data is on its way
        UiHelpers.Pump();
        Assert.Equal(new[] { "Context usage", "Loading context usage…" }, UiHelpers.Texts(ui.Context));
        Assert.Empty(Opened.Borders(ui.Context, "ClaudeChatContextBar"));

        c.Apply(new ControlAcknowledged("q", true, null) { Error = "no session" });
        UiHelpers.Pump();
        Assert.Equal(new[] { "Context usage", "Failed to load context usage: no session" }, UiHelpers.Texts(ui.Context));
        ui.View.Strings = ChatStrings.PortugueseBrazil;
        UiHelpers.Pump();
        Assert.Equal("Falha ao carregar o uso do contexto: no session", UiHelpers.Texts(ui.Context)[1]);
        ui.View.Strings = ChatStrings.English;

        // the answer comes while it is open
        foreach (var line in FixtureFiles.StreamLines("commands")) c.AppendLine(line);
        UiHelpers.Pump();
        Assert.Equal(ChatPanel.Context, c.ShownPanel);
        Assert.Contains("36,174 / 200,000 tokens (18%)", UiHelpers.Texts(ui.Context));
        Assert.DoesNotContain(UiHelpers.Texts(ui.Context), t => t.StartsWith("Failed", StringComparison.Ordinal));
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void The_modals_of_a_loaded_transcript_say_there_is_no_data()
    {
        var c = new ChatConversation();
        c.LoadTranscript(new string[0]);
        var ui = new Opened(c);
        c.InvokeCommand("usage", "");
        UiHelpers.Pump();
        Assert.Equal(new[] { "Account & Usage", "No usage data yet" }, UiHelpers.Texts(ui.Usage));
        c.InvokeCommand("context", "");
        UiHelpers.Pump();
        Assert.Equal(new[] { "Context usage", "No context data yet" }, UiHelpers.Texts(ui.Context));
        ui.View.Strings = ChatStrings.PortugueseBrazil;
        UiHelpers.Pump();
        Assert.Equal(new[] { "Uso do contexto", "Ainda sem dados de contexto" }, UiHelpers.Texts(ui.Context));
        ui.Window.Close();
    }

    // ---- "Account & Usage" ----

    [AvaloniaFact]
    public void Usage_bars_round_down_and_turn_clay_at_eighty_percent()
    {
        var c = new ChatConversation { Clock = () => Now };
        c.Apply(new UsageLimitsReported(Limits(
            new UsageWindow("five_hour", 0.799, Now.AddHours(1)),
            new UsageWindow("seven_day", 0.8, Now.AddDays(3)),
            new UsageWindow("seven_day_sonnet", 0.01, Now.AddDays(3)),
            new UsageWindow("seven_day_overage_included", 0.9, Now.AddDays(3)),            // 2.1.312's, which the extension draws no bar for
            new UsageWindow("model_scoped", 0.1, Now.AddDays(3)) { Model = "Opus" })));
        var ui = new Opened(c);
        c.InvokeCommand("usage", "");
        UiHelpers.Pump();
        var panel = ui.Usage;
        Assert.True(panel.IsEffectivelyVisible);
        Assert.DoesNotContain("seven_day_overage_included", UiHelpers.Texts(panel));
        Assert.False(ui.Context.IsVisible);

        Assert.Equal(400, panel.Dialog.MaxWidth);
        Assert.Equal(Fill(ui.Context), Fill(panel));
        var texts = UiHelpers.Texts(panel);
        Assert.Equal("Account & Usage", texts[0]);
        Assert.Contains("USAGE", texts);
        Assert.Equal(12, Block(panel, "USAGE").FontSize);
        Assert.Equal(0.7, Block(panel, "USAGE").Opacity);
        Assert.Equal(
            new[] { "Session (5hr)", "79%", "Weekly (7 day)", "80%", "Weekly Sonnet", "1%", "Weekly Opus", "10%" },
            texts.Where(t => t == "Session (5hr)" || t == "Weekly (7 day)" || t == "Weekly Sonnet" || t == "Weekly Opus" || t.EndsWith("%", StringComparison.Ordinal)).ToArray());
        Assert.DoesNotContain("ACCOUNT", texts);

        var bars = Opened.Borders(panel, "ClaudeChatUsageMeter");
        var fills = Opened.Borders(panel, "ClaudeChatUsageMeterFill");
        Assert.Equal(4, bars.Count);
        Assert.Equal(4, fills.Count);
        Assert.All(bars, bar =>
        {
            Assert.Equal(6, bar.Bounds.Height);
            Assert.Equal(new CornerRadius(3), bar.CornerRadius);
        });
        Assert.Equal(bars[0].Bounds.Width * 0.799, fills[0].Bounds.Width, 0);
        Assert.Equal(ColorOf(ui.View, "ClaudeChat.ProgressBackground"), Fill(fills[0]));
        Assert.Equal(Color.Parse("#D97757"), Fill(fills[1]));       // 80 % and over
        Assert.Equal(ColorOf(ui.View, "ClaudeChat.ProgressBackground"), Fill(fills[2]));
        Assert.Equal(ColorOf(ui.View, "ClaudeChat.UsageClay"), Fill(fills[1]));

        // a bar between 79 and 80 has not turned: the share is cut down, not rounded up
        c.Apply(new UsageLimitsReported(Limits(new UsageWindow("five_hour", 0.7999, Now.AddHours(1)))));
        UiHelpers.Pump();
        Assert.Contains("79%", UiHelpers.Texts(panel));
        Assert.Equal(ColorOf(ui.View, "ClaudeChat.ProgressBackground"), Fill(Opened.Borders(panel, "ClaudeChatUsageMeterFill")[0])); // the report replaced the session's window; the others stay

        ui.View.Strings = ChatStrings.PortugueseBrazil;
        UiHelpers.Pump();
        Assert.Equal("Conta e uso", UiHelpers.Texts(panel)[0]);
        Assert.Contains("Sessão (5h)", UiHelpers.Texts(panel));
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Resets_read_in_minutes_hours_or_days()
    {
        var c = new ChatConversation { Clock = () => Now };
        c.Apply(new UsageLimitsReported(Limits(
            new UsageWindow("five_hour", 0.1, Now.AddMinutes(45).AddSeconds(30)),
            new UsageWindow("seven_day", 0.1, Now.AddHours(3).AddMinutes(59)),
            new UsageWindow("seven_day_sonnet", 0.1, Now.AddDays(2).AddHours(23)),
            new UsageWindow("model_scoped", 0.1, Now.AddSeconds(20)) { Model = "Opus" },
            new UsageWindow("model_scoped", 0.1, Now.AddMinutes(-5)) { Model = "Haiku" })));
        var ui = new Opened(c);
        c.InvokeCommand("usage", "");
        UiHelpers.Pump();

        var resets = Opened.WithClass<TextBlock>(ui.Usage, "ClaudeChatModalResets");
        Assert.Equal(new[] { "Resets in 45m", "Resets in 3h", "Resets in 2d", "Resets soon", "Resets soon" }, resets.Select(t => t.Text).ToArray());
        Assert.Equal(11, resets[0].FontSize);
        Assert.Equal(0.5, resets[0].Opacity);
        Assert.DoesNotContain(UiHelpers.Texts(ui.Usage), t => t.StartsWith("Resets ", StringComparison.Ordinal) && t.Contains(":"));

        ui.View.Strings = ChatStrings.PortugueseBrazil;
        UiHelpers.Pump();
        Assert.Equal(
            new[] { "Renova em 45m", "Renova em 3h", "Renova em 2d", "Renova em breve", "Renova em breve" },
            Opened.WithClass<TextBlock>(ui.Usage, "ClaudeChatModalResets").Select(t => t.Text).ToArray());
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void The_account_section_shows_only_with_account_data()
    {
        var c = new ChatConversation { Clock = () => Now };
        c.Apply(new UsageLimitsReported(Limits(new UsageWindow("five_hour", 0.1, Now.AddHours(1)))));
        var ui = new Opened(c);
        c.InvokeCommand("usage", "");
        UiHelpers.Pump();
        Assert.DoesNotContain("ACCOUNT", UiHelpers.Texts(ui.Usage));
        Assert.DoesNotContain("Email", UiHelpers.Texts(ui.Usage));

        c.Apply(new AccountReported(new AccountInfo("claudeai", "ana@example.com", null, "Max")));
        UiHelpers.Pump();
        var texts = UiHelpers.Texts(ui.Usage);
        Assert.Equal(
            new[] { "ACCOUNT", "Auth method", "Claude AI", "Email", "ana@example.com", "Plan", "Max", "USAGE" },
            texts.SkipWhile(t => t != "ACCOUNT").TakeWhile(t => t != "Session (5hr)").ToArray());   // no row for the organization it did not say
        Assert.True(texts.IndexOf("ACCOUNT") < texts.IndexOf("USAGE"));

        // a login form it does not know, or none, has no row, and the raw text never shows
        c.Apply(new AccountReported(new AccountInfo("CLAUDE_CODE_OAUTH_TOKEN", null, null, null)));
        UiHelpers.Pump();
        Assert.DoesNotContain("Auth method", UiHelpers.Texts(ui.Usage));
        Assert.DoesNotContain("CLAUDE_CODE_OAUTH_TOKEN", UiHelpers.Texts(ui.Usage));
        c.Apply(new AccountReported(new AccountInfo("bedrock", null, null, null)));
        UiHelpers.Pump();
        Assert.Contains("Amazon Bedrock", UiHelpers.Texts(ui.Usage));
        c.Apply(new AccountReported(new AccountInfo("claudeai", "ana@example.com", null, "Max")));

        // without limits: the session's figures, or the line that says there are none
        var bare = new ChatConversation();
        bare.Apply(new AccountReported(new AccountInfo("api-key", null, null, null)));
        ui.View.Conversation = bare;
        bare.InvokeCommand("usage", "");
        UiHelpers.Pump();
        Assert.Contains("Plan limits aren't available for this login method.", UiHelpers.Texts(ui.Usage));
        bare.Apply(new TurnCompleted("success", false, 4200, 0.0839, 10, 5) { ApiDurationMs = 3100 });
        UiHelpers.Pump();
        texts = UiHelpers.Texts(ui.Usage);
        Assert.DoesNotContain("Plan limits aren't available for this login method.", texts);
        Assert.Equal(new[] { "Total cost", "$0.08", "Total duration (API)", "3.1 s", "Total duration (wall)", "4.2 s" }, texts.SkipWhile(t => t != "Total cost").ToArray());

        ui.View.Strings = ChatStrings.PortugueseBrazil;
        UiHelpers.Pump();
        Assert.Contains("Custo total", UiHelpers.Texts(ui.Usage));
        Assert.Contains("CONTA", UiHelpers.Texts(ui.Usage));
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Manage_usage_shows_only_with_a_link_subscriber_and_raises_link_requested()
    {
        var c = new ChatConversation { Clock = () => Now };
        c.Apply(new AccountReported(new AccountInfo("claudeai", null, null, "Pro")));
        c.Apply(new UsageLimitsReported(Limits(new UsageWindow("five_hour", 0.1, Now.AddHours(1)))));
        var ui = new Opened(c);
        c.InvokeCommand("usage", "");
        UiHelpers.Pump();
        Assert.DoesNotContain("Manage usage on claude.ai", UiHelpers.Texts(ui.Usage)); // the host opens no link

        var urls = new List<string>();
        c.LinkRequested += (_, e) => urls.Add(e.Url);                                   // it listens now
        UiHelpers.Pump();
        Assert.Contains("Manage usage on claude.ai", UiHelpers.Texts(ui.Usage));
        var link = UiHelpers.ButtonWithText(ui.Usage, "Manage usage on claude.ai");
        UiHelpers.Click(ui.Window, link);
        Assert.Equal(new[] { "https://claude.ai/settings/usage" }, urls);
        Assert.Equal(ChatPanel.Usage, c.ShownPanel);                                     // asking the host leaves it open

        ui.View.Strings = ChatStrings.PortugueseBrazil;
        UiHelpers.Pump();
        Assert.Contains("Gerenciar o uso em claude.ai", UiHelpers.Texts(ui.Usage));
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Account_and_usage_waits_for_its_data_and_retries_when_it_failed()
    {
        var c = new ChatConversation();
        var ui = new Opened(c);
        var asked = new List<string>();
        c.CommandInvoked += (_, e) => asked.Add(e.Name);
        c.InvokeCommand("usage", "");
        UiHelpers.Pump();
        Assert.Equal(new[] { "Account & Usage", "No usage data yet" }, UiHelpers.Texts(ui.Usage));
        c.Submit("go");
        UiHelpers.Pump();
        Assert.Equal(new[] { "Account & Usage", "Loading usage data…" }, UiHelpers.Texts(ui.Usage));

        c.Apply(new ControlAcknowledged("q", true, null));
        UiHelpers.Pump();
        Assert.Equal(new[] { "Account & Usage", "Failed to load usage data", "Retry" }, UiHelpers.Texts(ui.Usage));
        asked.Clear();
        UiHelpers.Click(ui.Window, UiHelpers.ButtonWithText(ui.Usage, "Retry"));
        Assert.Equal(new[] { "usage" }, asked);                                           // it asks again
        Assert.Equal(new[] { "Account & Usage", "Loading usage data…" }, UiHelpers.Texts(ui.Usage));

        c.AppendLine("{\"type\":\"rate_limit_event\",\"rate_limit_info\":{\"unifiedWindows\":{\"five_hour\":{\"utilization\":0.01,\"resetsAt\":1792698600}}}}");
        UiHelpers.Pump();
        Assert.Contains("Session (5hr)", UiHelpers.Texts(ui.Usage));
        Assert.DoesNotContain("Retry", UiHelpers.Texts(ui.Usage));
        ui.Window.Close();
    }

    // ---- closing, the keyboard, the view ----

    [AvaloniaFact]
    public void Esc_the_cross_and_the_backdrop_close_both_modals()
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

        // the ×
        foreach (var command in new[] { "context", "usage" })
        {
            c.InvokeCommand(command, "");
            UiHelpers.Pump();
            UiHelpers.Click(ui.Window, UiHelpers.ButtonWithText(command == "context" ? ui.Context : ui.Usage, "Close"));
            Assert.Equal(ChatPanel.None, c.ShownPanel);
        }

        // a click on the backdrop, and not one on the dialog
        foreach (var command in new[] { "context", "usage" })
        {
            c.InvokeCommand(command, "");
            UiHelpers.Pump();
            UiHelpers.DrawFrames();
            var dialog = (command == "context" ? (ChatInfoPanel)ui.Context : ui.Usage).Dialog;
            UiHelpers.Click(ui.Window, dialog);
            Assert.NotEqual(ChatPanel.None, c.ShownPanel);
            var corner = new Point(6, 6);
            ui.Window.MouseDown(corner, MouseButton.Left, RawInputModifiers.None);
            ui.Window.MouseUp(corner, MouseButton.Left, RawInputModifiers.None);
            UiHelpers.Pump();
            Assert.Equal(ChatPanel.None, c.ShownPanel);
        }
        Assert.Equal(1, ui.Stops);

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
    public void The_keyboard_goes_to_the_modal_and_back_to_what_had_it()
    {
        var c = UiHelpers.Recording("commands", "stream");
        var ui = new Opened(c);
        var input = UiHelpers.Input(ui.View);
        input.Focus();
        c.InvokeCommand("context", "");
        UiHelpers.Pump();
        Assert.True(ui.Context.IsKeyboardFocusWithin);                 // it took the keyboard…
        Assert.Equal(new[] { "Close" }, UiHelpers.ButtonNames(ui.Context));
        UiHelpers.Press(ui.Window, Key.Escape);
        Assert.Equal(ChatPanel.None, c.ShownPanel);
        Assert.True(input.IsFocused);                                  // …and gave it back

        // what had it is not the reply box: the same
        var other = ui.View.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible && b.Focusable && !ui.Context.IsVisualAncestorOf(b));
        other.Focus();
        c.InvokeCommand("usage", "");
        UiHelpers.Pump();
        Assert.True(ui.Usage.IsKeyboardFocusWithin);
        UiHelpers.Press(ui.Window, Key.Escape);
        Assert.True(other.IsFocused);

        // the keyboard was never in the modal (the user clicked elsewhere): it stays where it is
        c.InvokeCommand("context", "");
        UiHelpers.Pump();
        input.Focus();
        UiHelpers.Press(ui.Window, Key.Escape);
        Assert.Equal(ChatPanel.None, c.ShownPanel);
        Assert.True(input.IsFocused);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_request_waits_for_the_keyboard_while_a_modal_is_open()
    {
        var c = UiHelpers.Recording("commands", "stream");
        var answers = new List<PermissionAnsweredEventArgs>();
        c.PermissionAnswered += (_, e) => answers.Add(e);
        var ui = new Opened(c);
        c.InvokeCommand("context", "");
        UiHelpers.Pump();
        Assert.True(ui.Context.IsKeyboardFocusWithin);

        using (var input = System.Text.Json.JsonDocument.Parse("{\"command\":\"ls\"}"))
            c.Apply(new PermissionRequested("r1", "Bash", "Bash", null, "tool-r1", input.RootElement.Clone(), null));
        UiHelpers.Pump();

        var prompt = UiHelpers.Prompt(ui.View);
        Assert.True(ui.Context.IsVisible);                // it stays
        Assert.True(ui.Context.IsKeyboardFocusWithin);    // the modal covers the request, so the request waits for the keyboard
        Assert.False(prompt.IsKeyboardFocusWithin);

        UiHelpers.Press(ui.Window, Key.Escape);           // the modal closes and the request gets the keyboard
        Assert.Equal(ChatPanel.None, c.ShownPanel);
        Assert.True(prompt.IsKeyboardFocusWithin);
        Assert.Empty(answers);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void The_modals_show_in_read_only_and_follow_the_conversation()
    {
        var a = UiHelpers.Recording("commands", "stream");
        var b = new ChatConversation();
        var ui = new Opened(a);
        ui.View.IsReadOnly = true;
        a.InvokeCommand("context", "");
        UiHelpers.Pump();
        Assert.True(ui.Context.IsEffectivelyVisible);
        Assert.Contains("36,174 / 200,000 tokens (18%)", UiHelpers.Texts(ui.Context));

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
}

internal static class UsageTestExtensions
{
    /// <summary>The usage with the model, memory files and agents the claude listed.</summary>
    public static ContextUsage WithLists(this ContextUsage usage, string model, IReadOnlyList<ContextFile> files, IReadOnlyList<ContextAgent> agents) =>
        usage with { Model = model, MemoryFiles = files, Agents = agents };
}
