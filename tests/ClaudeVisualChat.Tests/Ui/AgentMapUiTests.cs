using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using Xunit;
using SessionStarted = ClaudeVisualChat.Protocol.SessionStarted;

namespace ClaudeVisualChat.Tests.Ui;

/// <summary>The "N agents" chip of the reply box's bar and the Agent map it opens.</summary>
public class AgentMapUiTests
{
    private const string Description = "Read and summarize three text files";

    // A foreground agent after the recorded one: the Agent call, then its result with the totals.
    private const string ForegroundCall =
        "{\"type\":\"assistant\",\"message\":{\"model\":\"claude-haiku-4-5-20251001\",\"id\":\"msg_fg1\",\"type\":\"message\",\"role\":\"assistant\"," +
        "\"content\":[{\"type\":\"tool_use\",\"id\":\"toolu_fg1\",\"name\":\"Agent\",\"input\":{\"description\":\"Haiku about rain\"," +
        "\"subagent_type\":\"general-purpose\",\"prompt\":\"Reply with a haiku about rain.\",\"run_in_background\":false}}]}," +
        "\"parent_tool_use_id\":null,\"session_id\":\"s1\"}";

    private const string ForegroundResult =
        "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":[{\"tool_use_id\":\"toolu_fg1\",\"type\":\"tool_result\"," +
        "\"content\":[{\"type\":\"text\",\"text\":\"Rain on the roof\"}]}]},\"parent_tool_use_id\":null,\"session_id\":\"s1\"," +
        "\"tool_use_result\":{\"status\":\"completed\",\"prompt\":\"Reply with a haiku about rain.\"," +
        "\"agentId\":\"a03380de4640e243e\",\"agentType\":\"general-purpose\"," +
        "\"content\":[{\"type\":\"text\",\"text\":\"Rain on the roof\"}]," +
        "\"resolvedModel\":\"claude-haiku-4-5-20251001\",\"totalDurationMs\":5573,\"totalTokens\":38119,\"totalToolUseCount\":2}}";

    private static string[] Subagent => FixtureFiles.StreamLines("subagent");

    /// <summary>The recorded subagent's lines up to the first one that contains <paramref name="marker"/>, with it.</summary>
    private static IEnumerable<string> Until(string marker)
    {
        var lines = Subagent;
        var index = Array.FindIndex(lines, l => l.Contains(marker));
        Assert.True(index > 0);
        return lines.Take(index + 1);
    }

    private static ChatConversation Play(IEnumerable<string> lines, ChatConversation? conversation = null)
    {
        conversation ??= new ChatConversation();
        foreach (var line in lines) conversation.AppendLine(line);
        return conversation;
    }

    private sealed class Opened
    {
        public Opened(ChatConversation conversation, double width = UiHelpers.Width)
        {
            Conversation = conversation;
            conversation.StopRequested += (_, _) => Stops++;
            View = new ClaudeChatView { Conversation = conversation };
            Window = UiHelpers.Show(View, width);
        }

        public ChatConversation Conversation { get; }
        public ClaudeChatView View { get; }
        public Window Window { get; }
        public int Stops { get; private set; }

        public AgentsChip Chip => UiHelpers.Composer(View).GetVisualDescendants().OfType<AgentsChip>().Single();

        public AgentMapPanel Map => View.GetVisualDescendants().OfType<AgentMapPanel>().Single();

        /// <summary>The agents' rows in the map, top to bottom (the main agent's and the tasks' left out).</summary>
        public List<Button> Rows => Map.GetVisualDescendants().OfType<Button>()
            .Where(b => b.IsEffectivelyVisible && b.Classes.Contains("ClaudeChatAgentRow") && !b.Classes.Contains("main") && !b.Classes.Contains("task"))
            .ToList();

        /// <summary>The background tasks' rows in the map, top to bottom.</summary>
        public List<Button> TaskRows => Map.GetVisualDescendants().OfType<Button>()
            .Where(b => b.IsEffectivelyVisible && b.Classes.Contains("ClaudeChatAgentRow") && b.Classes.Contains("task"))
            .ToList();

        public Border Card => Map.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatAgentCard"));

        /// <summary>The card's Stop button, when it shows.</summary>
        public Button? StopButton => Card.GetVisualDescendants().OfType<Button>()
            .SingleOrDefault(b => b.IsEffectivelyVisible && b.Classes.Contains("ClaudeChatAgentStop"));

        public Avalonia.Controls.Shapes.Ellipse CardDot => Card.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().Single();
    }

    [AvaloniaFact]
    public void Chip_mostra_zero_agentes_e_abre_o_mapa()
    {
        var c = new ChatConversation();
        var o = new Opened(c);
        Assert.False(o.Chip.IsVisible); // nothing to map

        // a session with no agent: no chip, as the extension's (2.1.309) shows it only once the map has an agent
        c.Apply(new SessionStarted("s1", "opus", null, "default", "2.1.306"));
        UiHelpers.Pump();
        Assert.False(o.Chip.IsVisible);

        // the map still opens from code, empty
        c.ShowAgents();
        UiHelpers.Pump();
        Assert.True(o.Map.IsEffectivelyVisible);
        var texts = UiHelpers.Texts(o.Map);
        Assert.Contains("Agent map", texts);
        Assert.Contains("0 agents · click an agent for details", texts);
        Assert.Contains("This session", texts); // no title: the extension's words, not "Main agent"
        Assert.Contains("opus", texts); // the model, as the model chip names it
        Assert.Empty(o.Rows);
        c.ClosePanel();
        UiHelpers.Pump();

        // an agent that came and went: the chip shows, counting none at work
        Play(new[] { ForegroundCall, ForegroundResult }, c);
        UiHelpers.Pump();
        Assert.True(o.Chip.IsEffectivelyVisible);
        Assert.Contains("0 agents", UiHelpers.Texts(o.Chip));
        Assert.DoesNotContain("running", o.Chip.Classes);
        Assert.False(o.Map.IsVisible);

        UiHelpers.Click(o.Window, o.Chip);
        Assert.Equal(ChatPanel.Agents, c.ShownPanel);
        Assert.True(o.Map.IsEffectivelyVisible);
        Assert.True(o.Map.IsKeyboardFocusWithin); // it takes the keyboard, as the extension's dialog
        Assert.Contains("1 agent · click an agent for details", UiHelpers.Texts(o.Map));
        Assert.Single(o.Rows);
        c.ClosePanel();

        // a cleared conversation has no agent again
        c.Clear();
        UiHelpers.Pump();
        Assert.False(o.Chip.IsVisible);
        o.Window.Close();
    }

    // ---- 0.6.0: the dot's states, /tasks and the main card ----

    private static string Call(string id, string description) =>
        "{\"type\":\"assistant\",\"message\":{\"id\":\"msg_" + id + "\",\"type\":\"message\",\"role\":\"assistant\"," +
        "\"content\":[{\"type\":\"tool_use\",\"id\":\"" + id + "\",\"name\":\"Agent\",\"input\":{\"description\":\"" + description + "\"," +
        "\"subagent_type\":\"general-purpose\"}}]},\"parent_tool_use_id\":null,\"session_id\":\"s1\"}";

    private static string Ends(string id, string status) =>
        "{\"type\":\"system\",\"subtype\":\"task_notification\",\"task_id\":\"t_" + id + "\",\"tool_use_id\":\"" + id + "\"," +
        "\"status\":\"" + status + "\",\"summary\":\"x\",\"usage\":{\"total_tokens\":10,\"tool_uses\":1,\"duration_ms\":20}}";

    private static Color DotColor(AgentsChip chip) =>
        Assert.IsAssignableFrom<ISolidColorBrush>(chip.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().Single().Fill).Color;

    private static string? Tip(AgentsChip chip) => ToolTip.GetTip(chip) as string;

    [AvaloniaTheory]
    [InlineData(ChatLayout.Desktop)]
    [InlineData(ChatLayout.Touch)]
    public void A_waiting_agent_turns_the_chip_dot_blue_with_its_tooltip(ChatLayout layout)
    {
        var c = Play(new[] { Call("toolu_a", "Look around"), Call("toolu_b", "Write tests") });
        var view = new ClaudeChatView { Conversation = c, Layout = layout };
        var window = UiHelpers.Show(view, UiHelpers.Width);
        var chip = view.GetVisualDescendants().OfType<AgentsChip>().Single(x => x.IsEffectivelyVisible);
        Assert.Equal(Color.Parse("#89D185"), DotColor(chip)); // at work
        Assert.Equal("Agents are working · Click to open the agent map", Tip(chip));

        c.Agents.Single(a => a.Id == "toolu_b").IsWaitingForPermission = true; // one of them asks, and the other works on
        UiHelpers.Pump();
        Assert.Equal(Color.Parse("#3B82F6"), DotColor(chip));
        Assert.Equal("An agent is waiting for your permission · Click to open the agent map", Tip(chip));
        Assert.Contains("2 ", chip.GetVisualDescendants().OfType<TextBlock>().Single().Text); // both count, in either layout

        c.Agents.Single(a => a.Id == "toolu_b").IsWaitingForPermission = false;
        UiHelpers.Pump();
        Assert.Equal(Color.Parse("#89D185"), DotColor(chip));
        window.Close();
    }

    [AvaloniaFact]
    public void A_failed_agent_turns_the_dot_red_only_while_nothing_works()
    {
        var c = Play(new[] { Call("toolu_a", "Look around"), Call("toolu_b", "Write tests"), Ends("toolu_a", "failed") });
        var o = new Opened(c);
        var error = Assert.IsAssignableFrom<ISolidColorBrush>(o.View.FindResource("ClaudeChat.Error")).Color;
        Assert.Equal(AgentStatus.Failed, c.Agents.Single(a => a.Id == "toolu_a").Status);

        // one still works: the green
        Assert.Equal(Color.Parse("#89D185"), DotColor(o.Chip));
        Assert.Equal("Agents are working · Click to open the agent map", Tip(o.Chip));

        // it ends: nothing works and one failed, so the red
        c.AppendLine(Ends("toolu_b", "completed"));
        UiHelpers.Pump();
        Assert.Equal(0, c.RunningAgents);
        Assert.Equal(error, DotColor(o.Chip));
        Assert.Equal("An agent failed · Click to open the agent map", Tip(o.Chip));

        // another starts to work: the red goes, and does not stick
        c.AppendLine(Call("toolu_c", "Read one file"));
        UiHelpers.Pump();
        Assert.Equal(Color.Parse("#89D185"), DotColor(o.Chip));
        c.AppendLine(Ends("toolu_c", "completed"));
        UiHelpers.Pump();
        Assert.Equal(error, DotColor(o.Chip));
        o.Window.Close();

        // stopped agents do not count as failed: the idle dot
        var stopped = Play(new[] { Call("toolu_a", "Look around"), Ends("toolu_a", "stopped") });
        var idle = new Opened(stopped);
        Assert.Equal(AgentStatus.Stopped, stopped.Agents.Single().Status);
        Assert.NotEqual(error, DotColor(idle.Chip));
        Assert.Equal(0.4, idle.Chip.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().Single().Opacity);
        Assert.Equal("Click to open the agent map", Tip(idle.Chip));
        idle.Window.Close();
    }

    [AvaloniaFact]
    public void The_main_card_reads_this_session_without_a_title_and_the_title_when_set()
    {
        var c = Play(new[] { Call("toolu_a", "Look around") });
        var o = new Opened(c);
        c.ShowAgents();
        UiHelpers.Pump();
        Assert.Contains("This session", UiHelpers.Texts(o.Map));
        Assert.DoesNotContain("Main agent", UiHelpers.Texts(o.Map));

        c.Title = "Fix the build";
        UiHelpers.Pump();
        Assert.Contains("Fix the build", UiHelpers.Texts(o.Map));
        Assert.DoesNotContain("This session", UiHelpers.Texts(o.Map));

        c.Title = "  ";
        UiHelpers.Pump();
        Assert.Contains("This session", UiHelpers.Texts(o.Map));
        o.Window.Close();
    }

    [AvaloniaFact]
    public void The_open_main_card_lists_waiting_and_failed_counts()
    {
        var c = Play(new[] { Call("toolu_a", "Look around"), Call("toolu_b", "Write tests"), Ends("toolu_a", "failed") });
        c.Apply(new SessionStarted("s1", "opus", null, "default", "2.1.306"));
        var o = new Opened(c);
        c.Agents.Single(a => a.Id == "toolu_b").IsWaitingForPermission = true;
        c.ShowAgents();
        UiHelpers.Pump();
        var main = o.Map.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatAgentRow") && b.Classes.Contains("main"));

        // closed: the model and nothing else
        Assert.Contains("opus", UiHelpers.Texts(main));
        Assert.DoesNotContain("Working · opus", UiHelpers.Texts(main));
        Assert.DoesNotContain("2 agents", UiHelpers.Texts(main));
        var closed = main.Bounds.Height;

        // a click opens it: Working (the calls have no result yet), the model, the agents, a line for each state that has any
        UiHelpers.Click(o.Window, main);
        UiHelpers.Pump();
        var texts = UiHelpers.Texts(main);
        Assert.Contains("Working · opus", texts);
        Assert.Contains("2 agents", texts);
        Assert.Contains("1 waiting for your permission", texts);
        Assert.Contains("1 failed", texts);
        Assert.DoesNotContain("0 running", texts);
        Assert.DoesNotContain(texts, t => t.StartsWith("0 ", System.StringComparison.Ordinal));
        Assert.True(main.Bounds.Height > closed);

        // another click closes it
        UiHelpers.Click(o.Window, main);
        UiHelpers.Pump();
        Assert.DoesNotContain("2 agents", UiHelpers.Texts(main));
        Assert.Equal(closed, main.Bounds.Height, 1);

        // opened again, and the map closed and opened: closed again
        UiHelpers.Click(o.Window, main);
        UiHelpers.Pump();
        c.ClosePanel();
        UiHelpers.Pump();
        c.ShowAgents();
        UiHelpers.Pump();
        Assert.DoesNotContain("2 agents", UiHelpers.Texts(main));
        o.Window.Close();
    }

    [AvaloniaFact]
    public void The_open_main_card_lines_read_and_come_in_the_extensions_order()
    {
        var c = Play(new[]
        {
            Call("toolu_a", "A"), Call("toolu_b", "B"), Call("toolu_c", "C"), Call("toolu_d", "D"), Call("toolu_e", "E"), Call("toolu_f", "F"),
            Ends("toolu_c", "completed"), Ends("toolu_d", "failed"), Ends("toolu_e", "stopped"),
        });
        c.Apply(new SessionStarted("s1", "opus", null, "default", "2.1.306"));
        var o = new Opened(c);
        c.Agents.Single(a => a.Id == "toolu_b").IsWaitingForPermission = true; // a and f work, b waits
        c.ShowAgents();
        UiHelpers.Pump();
        var main = o.Map.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatAgentRow") && b.Classes.Contains("main"));
        UiHelpers.Click(o.Window, main);
        UiHelpers.Pump();
        Assert.Equal(
            new[] { "This session", "Working · opus", "6 agents", "2 working", "1 waiting for your permission", "1 finished", "1 failed", "1 stopped" },
            UiHelpers.Texts(main));
        o.Window.Close();
    }

    [AvaloniaFact]
    public void The_main_card_is_a_button_to_assistive_tools_and_enter_and_space_toggle_it()
    {
        var c = Play(new[] { Call("toolu_a", "A") });
        var o = new Opened(c);
        c.ShowAgents();
        UiHelpers.Pump();
        var main = o.Map.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatAgentRow") && b.Classes.Contains("main"));
        Assert.Equal(Avalonia.Automation.Peers.AutomationControlType.Button, Avalonia.Automation.AutomationProperties.GetControlTypeOverride(main));
        Assert.True(main.Focusable);

        main.Focus();
        UiHelpers.Press(o.Window, Key.Enter);
        UiHelpers.Pump();
        Assert.Contains("1 agent", UiHelpers.Texts(main));
        UiHelpers.Press(o.Window, Key.Enter);
        UiHelpers.Pump();
        Assert.DoesNotContain("1 agent", UiHelpers.Texts(main));
        UiHelpers.Press(o.Window, Key.Space);
        UiHelpers.Pump();
        Assert.Contains("1 agent", UiHelpers.Texts(main));
        UiHelpers.Press(o.Window, Key.Space);
        UiHelpers.Pump();
        Assert.DoesNotContain("1 agent", UiHelpers.Texts(main));
        o.Window.Close();
    }

    [AvaloniaFact]
    public void In_the_touch_layout_the_main_card_is_at_least_a_finger_high()
    {
        var c = Play(new[] { Call("toolu_a", "A") });
        var view = new ClaudeChatView { Conversation = c, Layout = ChatLayout.Touch };
        var window = UiHelpers.Show(view, 384, 832);
        c.ShowAgents();
        UiHelpers.Pump();
        var main = view.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatAgentRow") && b.Classes.Contains("main"));
        Assert.True(main.MinHeight >= 44);
        Assert.True(main.Bounds.Height >= 44);
        UiHelpers.Click(window, main);
        UiHelpers.Pump();
        Assert.Contains("1 agent", UiHelpers.Texts(main)); // a tap opens it here too
        window.Close();
    }

    [AvaloniaFact]
    public void Chip_acende_enquanto_um_agente_roda()
    {
        var c = Play(Until("\"task_started\""));
        var o = new Opened(c);
        Assert.Equal(1, c.RunningAgents);
        Assert.Contains("running", o.Chip.Classes);
        Assert.Contains("1 agent", UiHelpers.Texts(o.Chip));

        Play(Subagent.SkipWhile(l => !l.Contains("\"task_started\"")).Skip(1), c);
        UiHelpers.Pump();
        Assert.Equal(0, c.RunningAgents);
        Assert.DoesNotContain("running", o.Chip.Classes);
        Assert.Contains("0 agents", UiHelpers.Texts(o.Chip));
        o.Window.Close();
    }

    [AvaloniaFact]
    public void Mapa_lista_o_mais_novo_primeiro_com_tempo_e_tokens()
    {
        var c = Play(Subagent.Append(ForegroundCall).Append(ForegroundResult));
        var o = new Opened(c);
        c.ShowAgents();
        UiHelpers.Pump();

        Assert.Contains("2 agents · click an agent for details", UiHelpers.Texts(o.Map));
        var rows = o.Rows;
        Assert.Equal(2, rows.Count);
        Assert.Contains("Haiku about rain", UiHelpers.Texts(rows[0]));
        Assert.Contains("5s · 38.1k tokens", UiHelpers.Texts(rows[0]));
        Assert.Contains(Description, UiHelpers.Texts(rows[1]));
        Assert.Contains("5s · 26.3k tokens", UiHelpers.Texts(rows[1]));

        // the rows' frame (.row_iHnHpw + .serverItem_IHCQeQ): 300 px, 6 px by 10 px inside, a 1 px border; the main card 320
        Assert.All(rows, r => Assert.Equal((300.0, new Avalonia.Thickness(10, 6), new Avalonia.Thickness(1)), (r.Bounds.Width, r.Padding, r.BorderThickness)));
        var main = o.Map.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatAgentRow"));
        Assert.Equal((320.0, new Avalonia.Thickness(10, 6), new Avalonia.Thickness(1)), (main.Bounds.Width, main.Padding, main.BorderThickness));

        // measured on the extension's map (the owner's print of 23/10): rows 48 px high, 8 px apart; the main card's
        // line starts at its border, and the rows begin 32 px after the card
        Assert.All(rows.Append<Control>(main), r => Assert.Equal(48, r.Bounds.Height, 1));
        Assert.Equal(8, rows[1].TranslatePoint(default, o.Map)!.Value.Y - rows[0].TranslatePoint(new Point(0, 48), o.Map)!.Value.Y, 1);
        var cardRight = main.TranslatePoint(new Point(main.Bounds.Width, 0), o.Map)!.Value.X;
        var stub = o.Map.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("stub") && b.Classes.Contains("main"));
        Assert.Equal(cardRight, stub.TranslatePoint(default, o.Map)!.Value.X, 1);
        Assert.Equal(cardRight + 32, rows[0].TranslatePoint(default, o.Map)!.Value.X, 1);
        // the subtitle on the text's 1.5 line
        Assert.Equal(19.5, o.Map.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("ClaudeChatAgentMapSubtitle")).LineHeight);
        o.Window.Close();
    }

    [AvaloniaFact]
    public void A_running_agents_card_says_what_it_is_doing()
    {
        var c = Play(Until("\"task_progress\""));
        var o = new Opened(c);
        c.ShowAgents();
        UiHelpers.Pump();
        UiHelpers.Click(o.Window, o.Rows.Single());

        var texts = UiHelpers.Texts(o.Card);
        Assert.Contains("Running · general-purpose · Haiku 4.5 · background", texts);
        var activity = o.Card.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("ClaudeChatAgentActivity"));
        Assert.True(activity.IsEffectivelyVisible);
        Assert.Equal("Reading a.txt", activity.Text);
        Assert.Contains("No result yet", texts);
        // the card's lines on the text's 1.5 line (.85em), the activity 10 px under them
        var meta = o.Card.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("ClaudeChatAgentCardMeta"));
        Assert.Equal(16.575, meta.LineHeight, 3);
        Assert.Equal(new Thickness(0, 10, 0, 0), activity.Margin);

        // once it ends, the activity goes and the answer comes
        Play(Subagent.SkipWhile(l => !l.Contains("\"task_progress\"")).Skip(1), c);
        UiHelpers.Pump();
        Assert.False(activity.IsVisible);
        Assert.Contains("Finished · general-purpose · Haiku 4.5 · background", UiHelpers.Texts(o.Card));
        o.Window.Close();
    }

    [AvaloniaFact]
    public void The_dialogs_border_follows_the_keyboard()
    {
        var c = Play(Subagent);
        var o = new Opened(c);
        c.ShowAgents();
        UiHelpers.Pump();
        var dialog = o.Map.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatAgentMap"));
        Assert.True(dialog.IsKeyboardFocusWithin);
        // the input's active border at 65 %, as the extension's dialog with the keyboard in it
        Assert.Equal(Avalonia.Media.Color.Parse("#A62A2B2C"), ((Avalonia.Media.ISolidColorBrush)dialog.BorderBrush!).Color);
        o.Window.Close();
    }

    [AvaloniaFact]
    public void Tempo_de_quem_roda_sobe_pelo_relogio()
    {
        var now = new DateTimeOffset(2026, 10, 23, 10, 0, 0, TimeSpan.Zero);
        var c = new ChatConversation { Clock = () => now };
        Play(Until("\"task_progress\""), c);
        var agent = Assert.Single(c.Agents);
        Assert.Equal(AgentStatus.Running, agent.Status);
        var o = new Opened(c);
        c.ShowAgents();
        UiHelpers.Pump();
        Assert.True(o.Map.IsTicking);

        now = agent.StartedAt + TimeSpan.FromSeconds(7 * 60 + 14);
        o.Map.Tick();
        Assert.Contains("7m 14s · 24.8k tokens", UiHelpers.Texts(o.Rows.Single()));
        o.Window.Close();
    }

    [AvaloniaFact]
    public void Clique_abre_a_ficha_e_Esc_volta()
    {
        var c = Play(Subagent);
        var agent = Assert.Single(c.Agents);
        var o = new Opened(c);
        c.ShowAgents();
        UiHelpers.Pump();
        Assert.False(o.Card.IsEffectivelyVisible);

        UiHelpers.Click(o.Window, o.Rows.Single());
        Assert.True(o.Card.IsEffectivelyVisible);
        var texts = UiHelpers.Texts(o.Card);
        Assert.Contains(Description, texts);
        Assert.Contains("Finished · general-purpose · Haiku 4.5 · background", texts);
        Assert.Contains("Spawned by the main agent", texts);
        Assert.Contains("5s · 26.3k tokens", texts);
        // the answer as the extension shows it: the text as it came, on a 1.4 line, 10 px under the lines
        var result = o.Card.GetVisualDescendants().OfType<SelectableTextBlock>().Single(t => t.Classes.Contains("ClaudeChatAgentResult"));
        Assert.Equal(agent.Result, result.Text);
        Assert.Equal(18.2, result.LineHeight, 2);
        Assert.Equal(TextWrapping.Wrap, result.TextWrapping);
        Assert.Empty(o.Card.GetVisualDescendants().OfType<MarkdownBlock>());
        Assert.Contains("Back", UiHelpers.ButtonNames(o.Card));

        UiHelpers.Press(o.Window, Key.Escape);
        Assert.False(o.Card.IsEffectivelyVisible);
        Assert.Equal(ChatPanel.Agents, c.ShownPanel); // back on the list, the map still open
        Assert.True(o.Map.IsEffectivelyVisible);
        Assert.Single(o.Rows);
        Assert.Equal(0, o.Stops);
        o.Window.Close();
    }

    [AvaloniaFact]
    public void Esc_na_lista_fecha_o_modal()
    {
        var c = Play(Subagent);
        var o = new Opened(c);
        c.ShowAgents();
        UiHelpers.Pump();
        Assert.True(o.Map.IsEffectivelyVisible);

        Assert.True(o.Map.IsKeyboardFocusWithin);

        UiHelpers.Press(o.Window, Key.Escape);
        Assert.Equal(ChatPanel.None, c.ShownPanel);
        Assert.False(o.Map.IsVisible);
        Assert.Equal(0, o.Stops);

        // the X closes it too
        c.ShowAgents();
        UiHelpers.Pump();
        UiHelpers.Click(o.Window, UiHelpers.ButtonWithText(o.Map, "Close"));
        Assert.Equal(ChatPanel.None, c.ShownPanel);
        Assert.False(o.Map.IsVisible);
        o.Window.Close();
    }

    [AvaloniaFact]
    public void Linha_apertada_sobrevive_ao_progresso_e_abre_a_ficha()
    {
        var c = Play(Until("\"task_started\""));
        var o = new Opened(c);
        c.ShowAgents();
        UiHelpers.Pump();
        var row = o.Rows.Single();
        var center = row.TranslatePoint(new Avalonia.Point(row.Bounds.Width / 2, row.Bounds.Height / 2), o.Window)!.Value;
        o.Window.MouseMove(center, RawInputModifiers.None);
        o.Window.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
        UiHelpers.Pump();

        // a progress line while the button is down: the row is filled again in place, not replaced
        c.AppendLine(Subagent.First(l => l.Contains("\"task_progress\"")));
        UiHelpers.Pump();
        Assert.Same(row, o.Rows.Single());
        Assert.Contains("24.8k tokens", string.Join(" ", UiHelpers.Texts(row)));

        o.Window.MouseUp(center, MouseButton.Left, RawInputModifiers.None);
        UiHelpers.Pump();
        Assert.True(o.Card.IsEffectivelyVisible);
        o.Window.Close();
    }

    [AvaloniaFact]
    public void Numa_view_estreita_a_arvore_rola_de_lado_e_a_ficha_cabe()
    {
        const double width = 600;
        var c = Play(Subagent);
        var o = new Opened(c, width);
        c.ShowAgents();
        UiHelpers.Pump();

        // 320 + 32 + 300 + 8 of tree in 600 - 2 × 16 - 2 × 17: the agents' column is reached by scrolling sideways
        var scroller = o.Map.GetVisualDescendants().OfType<ScrollViewer>().First();
        Assert.True(scroller.Extent.Width > scroller.Viewport.Width + 1, "the tree scrolls sideways");
        scroller.Offset = new Avalonia.Vector(scroller.Extent.Width, 0);
        UiHelpers.Pump();
        var row = o.Rows.Single();
        var rowRight = row.TranslatePoint(new Avalonia.Point(row.Bounds.Width, 0), scroller)!.Value.X;
        Assert.True(rowRight <= scroller.Viewport.Width + 1, "the row's right edge is reachable");

        UiHelpers.Click(o.Window, row);
        var card = o.Card;
        Assert.True(card.IsEffectivelyVisible);
        Assert.True(card.Bounds.Width <= width - 32 + 0.5, "the card is min(420, 100vw - 32) wide");
        var left = card.TranslatePoint(default, o.View)!.Value.X;
        Assert.True(left >= 16 - 0.5 && left + card.Bounds.Width <= width - 16 + 0.5, "the card fits in the view");
        o.Window.Close();
    }

    [AvaloniaFact]
    public void Trocar_de_conversa_com_o_mapa_aberto_solta_a_antiga()
    {
        var first = Play(Until("\"task_progress\""));
        var own = Handlers(first);
        var o = new Opened(first);
        first.ShowAgents();
        UiHelpers.Pump();
        Assert.True(o.Map.IsTicking);
        Assert.True(Handlers(first) > own);

        var second = Play(Subagent);
        o.View.Conversation = second;
        UiHelpers.Pump();
        Assert.Equal(own, Handlers(first)); // the map (and every control) let go of the old conversation's agents
        Assert.False(o.Map.IsVisible); // the new one does not show its map
        Assert.False(o.Map.IsTicking);

        second.ShowAgents();
        UiHelpers.Pump();
        Assert.True(o.Map.IsEffectivelyVisible);
        Assert.Contains("Read and summarize three text files", UiHelpers.Texts(o.Rows.Single()));
        Assert.Equal(own, Handlers(first));
        o.Window.Close();
    }

    [Theory]
    [InlineData("claude-haiku-4-5-20251001", "Haiku 4.5")]
    [InlineData("claude-sonnet-5-5", "Sonnet 5.5")]
    [InlineData("claude-opus-5-5[1m]", "Opus 5.5 (1M)")]
    [InlineData("claude-opus-4", "Opus 4")]
    [InlineData("sonnet", "Sonnet")]
    [InlineData("claude-3-5-sonnet-20241022", "claude-3-5-sonnet-20241022")]
    [InlineData("some-model", "some-model")]
    public void Nome_do_modelo_do_agente(string id, string expected) =>
        Assert.Equal(expected, AgentMapPanel.ModelName(id, null));

    [Fact]
    public void Nome_do_modelo_do_agente_vem_da_lista_quando_ela_tem()
    {
        var models = new[] { new ChatModel("sonnet", "claude-sonnet-5-5", "Sonnet 5.5 (latest)", "", Array.Empty<string>()) };
        Assert.Equal("Sonnet 5.5 (latest)", AgentMapPanel.ModelName("claude-sonnet-5-5", models));
        Assert.Equal("Sonnet 5.5 (latest)", AgentMapPanel.ModelName("sonnet", models));
        Assert.Null(AgentMapPanel.ModelName(null, models));
    }

    // ---- 0.11.0: the background tasks ----

    private static string Snapshot(params (string Id, string Description)[] tasks) =>
        "{\"type\":\"system\",\"subtype\":\"background_tasks_changed\",\"tasks\":[" +
        string.Join(",", tasks.Select(t => "{\"task_id\":\"" + t.Id + "\",\"task_type\":\"local_workflow\",\"description\":\"" + t.Description + "\"}")) +
        "],\"session_id\":\"s1\"}";

    private static string TaskStarted(string id) =>
        "{\"type\":\"system\",\"subtype\":\"task_started\",\"task_id\":\"" + id + "\",\"tool_use_id\":\"toolu_" + id + "\"," +
        "\"description\":\"Ping the agents\",\"task_type\":\"local_workflow\",\"session_id\":\"s1\"}";

    private static string TaskKilled(string id) =>
        "{\"type\":\"system\",\"subtype\":\"task_updated\",\"task_id\":\"" + id + "\",\"patch\":{\"status\":\"killed\"},\"session_id\":\"s1\"}";

    private static string TaskNotified(string id, string status) =>
        "{\"type\":\"system\",\"subtype\":\"task_notification\",\"task_id\":\"" + id + "\",\"tool_use_id\":\"toolu_" + id + "\"," +
        "\"status\":\"" + status + "\",\"summary\":\"Ping the agents\",\"session_id\":\"s1\"}";

    private static string StopAnswer(string requestId) =>
        "{\"type\":\"control_response\",\"response\":{\"subtype\":\"success\",\"request_id\":\"" + requestId + "\",\"response\":{}}}";

    private static string StopRefused(string requestId) =>
        "{\"type\":\"control_response\",\"response\":{\"subtype\":\"error\",\"request_id\":\"" + requestId + "\",\"error\":\"No task found\"}}";

    private static List<string> TaskTitles(Opened o) =>
        o.TaskRows.Select(r => r.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("ClaudeChatAgentRowTitle")).Text ?? "").ToList();

    /// <summary>
    /// The workflow <c>w1</c> started 5 s ago by the conversation's clock, its map open on the list; a host listening to the stops,
    /// which names each one s1, s2… (<c>RequestId</c> left null when <paramref name="sendsRequests"/> is false).
    /// </summary>
    private sealed class Workflow
    {
        public DateTimeOffset Now = new DateTimeOffset(2026, 11, 22, 10, 0, 0, TimeSpan.Zero);

        public Workflow(bool host = true, bool sendsRequests = true)
        {
            Conversation = new ChatConversation { Clock = () => Now };
            Conversation.AppendLine(Snapshot(("w1", "Ping the agents")));
            Conversation.AppendLine(TaskStarted("w1"));
            if (host)
            {
                Conversation.TaskStopRequested += (_, e) =>
                {
                    Asked.Add(e);
                    if (sendsRequests) e.RequestId = "s" + Asked.Count;
                };
            }
            Now = Now.AddSeconds(5);
            Ui = new Opened(Conversation);
            Conversation.ShowAgents();
            UiHelpers.Pump();
        }

        public ChatConversation Conversation { get; }
        public Opened Ui { get; }
        public List<TaskStopRequestedEventArgs> Asked { get; } = new List<TaskStopRequestedEventArgs>();

        public void OpenDetail()
        {
            UiHelpers.Click(Ui.Window, Ui.TaskRows.Single());
            Assert.True(Ui.Card.IsEffectivelyVisible);
        }
    }

    [AvaloniaFact]
    public void The_map_lists_the_background_tasks_under_their_header()
    {
        var c = new ChatConversation();
        var o = new Opened(c);
        c.ShowAgents();
        UiHelpers.Pump();
        Assert.DoesNotContain(UiHelpers.Texts(o.Map), t => t.Contains("background task"));
        Assert.Empty(o.TaskRows);

        c.AppendLine(Snapshot(("w1", "Ping the agents")));
        UiHelpers.Pump();
        Assert.Contains("1 background task · click a task for details", UiHelpers.Texts(o.Map));
        Assert.Equal(new[] { "Ping the agents" }, TaskTitles(o));
        Assert.Equal("0 agents · click an agent for details", o.Map.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("ClaudeChatAgentMapSubtitle")).Text);

        c.AppendLine(Snapshot(("w1", "Ping the agents"), ("w2", "Review the diff")));
        UiHelpers.Pump();
        Assert.Contains("2 background tasks · click a task for details", UiHelpers.Texts(o.Map));
        Assert.Equal(new[] { "Ping the agents", "Review the diff" }, TaskTitles(o));

        // under the tree, the agents' rows' frame: 300 px, 48 high, 8 apart; the header 12 px over the first, the section 16 px
        // under the tree's main card
        var rows = o.TaskRows;
        var main = o.Map.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatAgentRow") && b.Classes.Contains("main"));
        var header = o.Map.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("ClaudeChatAgentTasksHeader"));
        Assert.All(rows, r => Assert.Equal((300.0, 48.0), (r.Bounds.Width, Math.Round(r.Bounds.Height))));
        Assert.True(header.TranslatePoint(default, o.Map)!.Value.Y > main.TranslatePoint(new Point(0, main.Bounds.Height), o.Map)!.Value.Y);
        Assert.Equal(main.TranslatePoint(default, o.Map)!.Value.X, rows[0].TranslatePoint(default, o.Map)!.Value.X, 1);
        Assert.Equal(12, rows[0].TranslatePoint(default, o.Map)!.Value.Y - header.TranslatePoint(new Point(0, header.Bounds.Height), o.Map)!.Value.Y, 1);
        Assert.Equal(8, rows[1].TranslatePoint(default, o.Map)!.Value.Y - rows[0].TranslatePoint(new Point(0, 48), o.Map)!.Value.Y, 1);
        Assert.Equal("Ping the agents", ToolTip.GetTip(rows[0]));
        Assert.Equal("Ping the agents", Avalonia.Automation.AutomationProperties.GetName(rows[0]));
        // a running dot on each
        Assert.All(rows, r => Assert.Contains("running", r.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().Single().Classes));

        // in Portuguese
        o.View.Strings = ChatStrings.PortugueseBrazil;
        UiHelpers.Pump();
        Assert.Contains("2 tarefas em segundo plano · clique numa tarefa para ver os detalhes", UiHelpers.Texts(o.Map));
        o.Window.Close();
    }

    [AvaloniaFact]
    public void A_task_row_reads_workflow_and_its_time()
    {
        var now = new DateTimeOffset(2026, 11, 22, 10, 0, 0, TimeSpan.Zero);
        var c = new ChatConversation { Clock = () => now };
        c.AppendLine(Snapshot(("w1", "Ping the agents"), ("w2", "")));
        var o = new Opened(c);
        c.ShowAgents();
        UiHelpers.Pump();

        // no start yet: only the kind; without a description, the kind is the title too
        Assert.Equal(new[] { "Ping the agents", "workflow" }, TaskTitles(o));
        Assert.Contains("workflow", UiHelpers.Texts(o.TaskRows[0]));
        Assert.True(o.Map.IsTicking); // the clock goes while there is a row

        c.AppendLine(TaskStarted("w1"));
        UiHelpers.Pump();
        Assert.Contains("workflow", UiHelpers.Texts(o.TaskRows[0])); // under a second: no time
        now = now.AddSeconds(123);
        o.Map.Tick();
        Assert.Contains("workflow · 2m 3s", UiHelpers.Texts(o.TaskRows[0]));
        Assert.DoesNotContain(UiHelpers.Texts(o.TaskRows[1]), t => t.Contains("·"));
        o.Window.Close();
    }

    [AvaloniaFact]
    public void The_rows_go_when_the_snapshot_comes_empty()
    {
        var c = new ChatConversation();
        c.AppendLine(Snapshot(("w1", "Ping the agents")));
        var o = new Opened(c);
        c.ShowAgents();
        UiHelpers.Pump();
        Assert.Single(o.TaskRows);
        Assert.True(o.Map.IsTicking);

        c.AppendLine(Snapshot());
        UiHelpers.Pump();
        Assert.Empty(o.TaskRows);
        Assert.DoesNotContain(UiHelpers.Texts(o.Map), t => t.Contains("background task"));
        Assert.False(o.Map.IsTicking);
        o.Window.Close();
    }

    [AvaloniaFact]
    public void Slash_tasks_shows_the_section_with_no_agent_and_no_chip()
    {
        var c = new ChatConversation();
        c.AppendLine(Snapshot(("w1", "Ping the agents")));
        var o = new Opened(c);
        Assert.False(o.Chip.IsVisible); // one workflow and no agent: no chip

        Assert.True(c.Submit("/tasks"));
        UiHelpers.Pump();
        Assert.True(o.Map.IsEffectivelyVisible);
        Assert.Contains("1 background task · click a task for details", UiHelpers.Texts(o.Map));
        Assert.Empty(o.Rows);
        Assert.Single(o.TaskRows);
        Assert.False(o.Chip.IsVisible);
        o.Window.Close();
    }

    [AvaloniaFact]
    public void A_task_opens_its_detail()
    {
        var w = new Workflow();
        var o = w.Ui;
        w.OpenDetail();
        Assert.True(o.Map.ShowsCard);
        var texts = UiHelpers.Texts(o.Card);
        Assert.Contains("Ping the agents", texts);
        Assert.Contains("Running · 5s", texts);
        Assert.Contains("running", o.CardDot.Classes);
        Assert.NotNull(o.StopButton);
        Assert.Equal("Stop", UiHelpers.Texts(o.StopButton!).Single());
        Assert.True(o.StopButton!.IsEnabled);
        // the agent's card lines do not show: who called it, the tokens, the answer
        Assert.DoesNotContain("Spawned by the main agent", texts);
        Assert.DoesNotContain("No result yet", texts);
        Assert.False(o.Card.GetVisualDescendants().OfType<SelectableTextBlock>().Single().IsVisible);
        Assert.DoesNotContain("The task could not be stopped. It may have finished already.", texts);

        // the time goes on while it runs
        w.Now = w.Now.AddSeconds(60);
        o.Map.Tick();
        Assert.Contains("Running · 1m 5s", UiHelpers.Texts(o.Card));

        // Esc goes back to the list, its row with the keyboard
        UiHelpers.Press(o.Window, Key.Escape);
        Assert.False(o.Map.ShowsCard);
        Assert.Equal(ChatPanel.Agents, w.Conversation.ShownPanel);
        Assert.True(o.TaskRows.Single().IsKeyboardFocusWithin);
        Assert.Empty(w.Asked);
        o.Window.Close();
    }

    [AvaloniaFact]
    public void Without_a_host_listening_there_is_no_stop_button()
    {
        var w = new Workflow(host: false);
        var o = w.Ui;
        w.OpenDetail();
        Assert.Null(o.StopButton);
        Assert.Contains("Running · 5s", UiHelpers.Texts(o.Card));

        // a host that begins listening gets the button, and loses it when it stops
        EventHandler<TaskStopRequestedEventArgs> handler = (_, e) => e.RequestId = "s1";
        w.Conversation.TaskStopRequested += handler;
        UiHelpers.Pump();
        Assert.NotNull(o.StopButton);
        w.Conversation.TaskStopRequested -= handler;
        UiHelpers.Pump();
        Assert.Null(o.StopButton);
        o.Window.Close();
    }

    [AvaloniaFact]
    public void Stop_says_stopping_until_the_answer_comes()
    {
        var w = new Workflow();
        var o = w.Ui;
        w.OpenDetail();
        var stop = o.StopButton!;
        UiHelpers.Click(o.Window, stop);
        Assert.Equal("w1", Assert.Single(w.Asked).TaskId);

        Assert.Same(stop, o.StopButton);
        Assert.False(stop.IsEnabled);
        Assert.Equal("Stopping…", UiHelpers.Texts(stop).Single());
        Assert.Contains("Stopping… · 5s", UiHelpers.Texts(o.Card));
        Assert.Contains("running", o.CardDot.Classes);

        // a second click does nothing while it waits
        UiHelpers.Click(o.Window, stop);
        Assert.Single(w.Asked);
        o.Window.Close();
    }

    [AvaloniaFact]
    public void A_stopped_task_shows_how_it_ended_and_its_time_stands_still()
    {
        var w = new Workflow();
        var o = w.Ui;
        w.OpenDetail();
        UiHelpers.Click(o.Window, o.StopButton!);

        // what the claude writes after a stop_task, in its order: the answer last
        foreach (var line in new[] { TaskKilled("w1"), TaskNotified("w1", "stopped"), Snapshot(), StopAnswer("s1") })
            w.Conversation.AppendLine(line);
        UiHelpers.Pump();
        Assert.True(o.Map.ShowsCard); // the task left the list, and its detail stays
        Assert.Contains("Stopped · 5s", UiHelpers.Texts(o.Card));
        Assert.Null(o.StopButton);
        Assert.Contains("failed", o.CardDot.Classes);
        Assert.DoesNotContain("running", o.CardDot.Classes);

        w.Now = w.Now.AddSeconds(60);
        o.Map.Tick();
        UiHelpers.Pump();
        Assert.Contains("Stopped · 5s", UiHelpers.Texts(o.Card));

        // back on the list, the row is gone
        UiHelpers.Press(o.Window, Key.Escape);
        Assert.Empty(o.TaskRows);
        o.Window.Close();
    }

    [AvaloniaFact]
    public void A_task_that_ends_while_its_stop_waits_shows_how_it_ended_and_no_answer_is_needed()
    {
        // with a notification: its status and its dot, not "Stopping…"
        var w = new Workflow();
        var o = w.Ui;
        w.OpenDetail();
        UiHelpers.Click(o.Window, o.StopButton!);
        w.Conversation.AppendLine(TaskNotified("w1", "stopped"));
        w.Conversation.AppendLine(Snapshot());
        UiHelpers.Pump();
        Assert.True(o.Map.ShowsCard);
        Assert.Contains("Stopped · 5s", UiHelpers.Texts(o.Card));
        Assert.DoesNotContain(UiHelpers.Texts(o.Card), t => t.Contains("Stopping…"));
        Assert.Null(o.StopButton);
        Assert.Contains("failed", o.CardDot.Classes);
        Assert.DoesNotContain("running", o.CardDot.Classes);
        o.Window.Close();

        // with none: "Ended", the neutral dot
        var quiet = new Workflow();
        quiet.OpenDetail();
        UiHelpers.Click(quiet.Ui.Window, quiet.Ui.StopButton!);
        quiet.Conversation.AppendLine(Snapshot());
        UiHelpers.Pump();
        Assert.Contains("Ended · 5s", UiHelpers.Texts(quiet.Ui.Card));
        Assert.DoesNotContain(UiHelpers.Texts(quiet.Ui.Card), t => t.Contains("Stopping…"));
        Assert.Null(quiet.Ui.StopButton);
        Assert.DoesNotContain("running", quiet.Ui.CardDot.Classes);
        Assert.DoesNotContain("failed", quiet.Ui.CardDot.Classes);
        quiet.Ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_failed_stop_says_so_and_gives_the_button_back()
    {
        const string failure = "The task could not be stopped. It may have finished already.";
        var w = new Workflow();
        var o = w.Ui;
        w.OpenDetail();
        UiHelpers.Click(o.Window, o.StopButton!);
        w.Conversation.AppendLine(StopRefused("s1"));
        UiHelpers.Pump();
        Assert.Contains(failure, UiHelpers.Texts(o.Card));
        var stop = o.StopButton!;
        Assert.True(stop.IsEnabled);
        Assert.Equal("Stop", UiHelpers.Texts(stop).Single());
        Assert.Contains("Running · 5s", UiHelpers.Texts(o.Card));
        // the line under the button, in the error color
        var line = o.Card.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == failure);
        Assert.True(line.TranslatePoint(default, o.Card)!.Value.Y >= stop.TranslatePoint(new Point(0, stop.Bounds.Height), o.Card)!.Value.Y);
        Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(o.View.FindResource("ClaudeChat.Error")).Color,
                     Assert.IsAssignableFrom<ISolidColorBrush>(line.Foreground).Color);

        // asked again: the line goes while it waits
        UiHelpers.Click(o.Window, stop);
        Assert.DoesNotContain(failure, UiHelpers.Texts(o.Card));
        o.Window.Close();

        // a host that sent nothing: the same at once
        var silent = new Workflow(sendsRequests: false);
        silent.OpenDetail();
        UiHelpers.Click(silent.Ui.Window, silent.Ui.StopButton!);
        Assert.Contains(failure, UiHelpers.Texts(silent.Ui.Card));
        Assert.True(silent.Ui.StopButton!.IsEnabled);
        silent.Ui.Window.Close();
    }

    [AvaloniaFact]
    public void The_open_detail_keeps_a_task_that_ended_by_itself()
    {
        var w = new Workflow();
        var o = w.Ui;
        w.OpenDetail();
        w.Conversation.AppendLine(TaskNotified("w1", "completed"));
        w.Conversation.AppendLine(Snapshot());
        UiHelpers.Pump();
        Assert.True(o.Map.ShowsCard);
        Assert.Contains("Finished · 5s", UiHelpers.Texts(o.Card));
        Assert.Null(o.StopButton);
        Assert.DoesNotContain("running", o.CardDot.Classes);
        Assert.DoesNotContain("failed", o.CardDot.Classes);
        o.Window.Close();

        // with no notification: "Ended", the neutral dot
        var quiet = new Workflow();
        quiet.OpenDetail();
        quiet.Conversation.AppendLine(Snapshot());
        UiHelpers.Pump();
        Assert.Contains("Ended · 5s", UiHelpers.Texts(quiet.Ui.Card));
        Assert.Null(quiet.Ui.StopButton);
        Assert.DoesNotContain("running", quiet.Ui.CardDot.Classes);
        Assert.DoesNotContain("failed", quiet.Ui.CardDot.Classes);
        quiet.Ui.Window.Close();
    }

    [AvaloniaFact]
    public void The_detail_closes_when_the_view_gets_another_conversation()
    {
        var w = new Workflow();
        var o = w.Ui;
        w.OpenDetail();

        // the session begins again: the detail goes, the map stays
        w.Conversation.Clear();
        UiHelpers.Pump();
        Assert.False(o.Map.ShowsCard);
        Assert.False(o.Card.IsEffectivelyVisible);
        Assert.True(o.Map.IsEffectivelyVisible);

        // closing the map closes it too
        w.Conversation.AppendLine(Snapshot(("w1", "Ping the agents")));
        UiHelpers.Pump();
        w.OpenDetail();
        w.Conversation.ClosePanel();
        UiHelpers.Pump();
        w.Conversation.ShowAgents();
        UiHelpers.Pump();
        Assert.False(o.Map.ShowsCard);

        // another conversation in the view
        w.OpenDetail();
        var second = new ChatConversation();
        second.AppendLine(Snapshot(("w9", "Another workflow")));
        o.View.Conversation = second;
        UiHelpers.Pump();
        Assert.False(o.Map.ShowsCard);
        second.ShowAgents();
        UiHelpers.Pump();
        Assert.False(o.Map.ShowsCard);
        Assert.False(o.Card.IsEffectivelyVisible);
        Assert.Equal(new[] { "Another workflow" }, TaskTitles(o));
        o.Window.Close();
    }

    // ---- out of the window ----

    /// <summary>Handlers on the conversation's PropertyChanged, on its agents' list and on each agent.</summary>
    private static int Handlers(ChatConversation c)
    {
        const System.Reflection.BindingFlags field = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        int Count(object target, Type type, string name) =>
            ((Delegate?)type.GetField(name, field)!.GetValue(target))?.GetInvocationList().Length ?? 0;
        var total = Count(c, typeof(ChatObservableObject), "PropertyChanged")
                    + Count(c.Agents, typeof(System.Collections.ObjectModel.ObservableCollection<AgentInfo>), "CollectionChanged");
        foreach (var agent in c.Agents) total += Count(agent, typeof(ChatObservableObject), "PropertyChanged");
        return total;
    }

    [AvaloniaFact]
    public void Modal_desmontado_para_o_relogio()
    {
        var c = Play(Until("\"task_progress\""));
        var own = Handlers(c);
        var view = new ClaudeChatView { Conversation = c };
        var host = new ContentControl { Content = view };
        var window = UiHelpers.Show(host);
        c.ShowAgents();
        UiHelpers.Pump();
        var map = view.GetVisualDescendants().OfType<AgentMapPanel>().Single();
        Assert.True(map.IsTicking);
        Assert.True(Handlers(c) > own);

        host.Content = new TextBlock { Text = "Tokens" };
        UiHelpers.Pump();
        Assert.False(map.IsTicking);
        Assert.Equal(own, Handlers(c)); // every control let go, the map too
        Assert.True(map.IsVisible); // leaving drew nothing

        host.Content = view;
        UiHelpers.Pump();
        Assert.True(map.IsTicking);
        Assert.True(map.IsEffectivelyVisible);
        window.Close();
    }
}
