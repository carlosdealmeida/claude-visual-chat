using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using ClaudeVisualChat.Protocol;
using Xunit;

namespace ClaudeVisualChat.Tests.Ui;

public class ContextRingTests
{
    private sealed class Opened
    {
        public Opened(ChatConversation conversation)
        {
            Conversation = conversation;
            conversation.CommandInvoked += (_, e) => Commands.Add(e.Name);
            View = new ClaudeChatView { Conversation = conversation };
            Host = new ContentControl { Content = View };
            Window = UiHelpers.Show(Host);
        }

        public ChatConversation Conversation { get; }
        public ClaudeChatView View { get; }
        public ContentControl Host { get; }
        public Avalonia.Controls.Window Window { get; }
        public List<string> Commands { get; } = new List<string>();

        public ContextRing Ring => UiHelpers.Composer(View).GetVisualDescendants().OfType<ContextRing>().Single();

        public void Report(long total, long max, int percentage)
        {
            Conversation.Apply(new ContextUsageReported(new ContextUsage(total, max, percentage, Array.Empty<ContextCategory>())));
            UiHelpers.Pump();
        }
    }

    [AvaloniaFact]
    public void Anel_escondido_sem_uso_de_contexto()
    {
        var o = new Opened(new ChatConversation());
        Assert.False(o.Ring.IsVisible);
        o.Window.Close();
    }

    [AvaloniaFact]
    public void Anel_escondido_com_maximo_zero()
    {
        var o = new Opened(new ChatConversation());
        o.Report(500, 0, 0);
        Assert.False(o.Ring.IsVisible);
        o.Window.Close();
    }

    [AvaloniaFact]
    public void Anel_aparece_so_acima_de_50_por_cento()
    {
        var o = new Opened(new ChatConversation());
        o.Report(500, 1000, 50);
        Assert.False(o.Ring.IsVisible); // the extension hides it with 50 % or more left
        o.Report(510, 1000, 51);
        Assert.True(o.Ring.IsEffectivelyVisible);
        o.Report(100, 1000, 10);
        Assert.False(o.Ring.IsVisible);
        o.Window.Close();
    }

    [AvaloniaFact]
    public void Anel_diz_quanto_resta_ate_o_auto_compactar()
    {
        var o = new Opened(new ChatConversation());
        o.Report(700000, 1000000, 70);
        Assert.Equal("30% of context remaining until auto-compact.\nClick to see the context usage.", ToolTip.GetTip(o.Ring));
        Assert.Equal(70.0, o.Ring.Percent);
        o.Window.Close();
    }

    [AvaloniaFact]
    public void Anel_desenha_os_tres_degraus_da_extensao()
    {
        Assert.Equal(50, ContextRing.StepOf(51));
        Assert.Equal(50, ContextRing.StepOf(62));
        Assert.Equal(75, ContextRing.StepOf(63));
        Assert.Equal(75, ContextRing.StepOf(86));
        Assert.Equal(99, ContextRing.StepOf(87));
        Assert.Equal(99, ContextRing.StepOf(100));
        var o = new Opened(new ChatConversation());
        o.Report(700, 1000, 70);
        Assert.Equal(75, o.Ring.GetVisualDescendants().OfType<ContextRingArc>().Single().Percent);
        o.Window.Close();
    }

    [AvaloniaFact]
    public void Anel_usa_a_fracao_dos_tokens_como_a_extensao()
    {
        var o = new Opened(new ChatConversation());
        // the reported percentage is rounded (50), the tokens say 50.4: the extension shows it
        o.Report(504, 1000, 50);
        Assert.True(o.Ring.IsEffectivelyVisible);
        Assert.Equal("50% of context remaining until auto-compact.\nClick to see the context usage.", ToolTip.GetTip(o.Ring));
        Assert.Equal(50, o.Ring.GetVisualDescendants().OfType<ContextRingArc>().Single().Percent);
        o.Report(500, 1000, 50);
        Assert.False(o.Ring.IsVisible);
        // the step edges: 62.5 and 87 belong to the next step
        o.Report(624, 1000, 62);
        Assert.Equal(50, o.Ring.GetVisualDescendants().OfType<ContextRingArc>().Single().Percent);
        o.Report(625, 1000, 62);
        Assert.Equal(75, o.Ring.GetVisualDescendants().OfType<ContextRingArc>().Single().Percent);
        o.Report(869, 1000, 87);
        Assert.Equal(75, o.Ring.GetVisualDescendants().OfType<ContextRingArc>().Single().Percent);
        o.Report(870, 1000, 87);
        Assert.Equal(99, o.Ring.GetVisualDescendants().OfType<ContextRingArc>().Single().Percent);
        o.Window.Close();
    }

    [AvaloniaFact]
    public void Anel_nao_tem_niveis_de_cor()
    {
        var o = new Opened(new ChatConversation());
        foreach (var percent in new[] { 60, 80, 95, 100 })
        {
            o.Report(percent * 10, 1000, percent);
            Assert.DoesNotContain("warning", o.Ring.Classes);
            Assert.DoesNotContain("critical", o.Ring.Classes);
        }
        o.Window.Close();
    }

    [AvaloniaFact]
    public void Anel_limita_porcentagem_acima_de_100()
    {
        var o = new Opened(new ChatConversation());
        o.Report(1200, 1000, 120);
        Assert.Equal(100.0, o.Ring.Percent);
        Assert.StartsWith("0% of context remaining", (string?)ToolTip.GetTip(o.Ring));
        o.Window.Close();
    }

    [AvaloniaFact]
    public void Clique_no_anel_abre_o_painel_de_contexto_sem_compactar()
    {
        var o = new Opened(new ChatConversation());
        o.Report(700000, 1000000, 70);
        var sent = new List<string>();
        o.Conversation.MessageSubmitted += (_, e) => sent.Add(e.Text);
        UiHelpers.Click(o.Window, o.Ring);
        Assert.Equal(ChatPanel.Context, o.Conversation.ShownPanel);
        Assert.Equal(new[] { "context" }, o.Commands); // the extension compacts here; this must not
        Assert.Empty(sent);
        o.Window.Close();
    }

    [AvaloniaFact]
    public void Anel_fala_portugues_com_as_strings_do_pt_BR()
    {
        var o = new Opened(new ChatConversation());
        o.View.Strings = ChatStrings.PortugueseBrazil;
        o.Report(700000, 1000000, 70);
        Assert.Equal("30% do contexto restante até o auto-compactar.\nClique para ver o uso do contexto.", ToolTip.GetTip(o.Ring));
        o.Window.Close();
    }

    [AvaloniaFact]
    public void Anel_fora_da_janela_nao_mexe_na_arvore_e_volta_atualizado()
    {
        var c = new ChatConversation();
        var o = new Opened(c);
        o.Report(700000, 1000000, 70);
        var ring = o.Ring;
        var arc = ring.GetVisualDescendants().OfType<ContextRingArc>().Single();
        var host = o.Host;
        host.Content = new TextBlock { Text = "Tokens" };
        UiHelpers.Pump();

        // detached: the usage and the texts change, and the ring draws nothing (no visual, no tooltip, no class)
        c.Apply(new ContextUsageReported(new ContextUsage(960000, 1000000, 96, Array.Empty<ContextCategory>())));
        o.View.Strings = ChatStrings.PortugueseBrazil;
        UiHelpers.Pump();
        Assert.Equal(70.0, ring.Percent);
        Assert.Equal(75, arc.Percent);
        Assert.StartsWith("30% of context remaining", (string?)ToolTip.GetTip(ring));

        host.Content = o.View;
        UiHelpers.Pump();
        Assert.Equal(96.0, ring.Percent);
        Assert.Equal(99, arc.Percent);
        Assert.StartsWith("4% do contexto restante", (string?)ToolTip.GetTip(ring));
        o.Window.Close();
    }
}
