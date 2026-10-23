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

        /// <summary>The agents' rows in the map, top to bottom (the main agent's left out).</summary>
        public List<Button> Rows => Map.GetVisualDescendants().OfType<Button>()
            .Where(b => b.IsEffectivelyVisible && b.Classes.Contains("ClaudeChatAgentRow") && !b.Classes.Contains("main"))
            .ToList();

        public Border Card => Map.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatAgentCard"));
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
        Assert.Contains("Main agent", texts);
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
