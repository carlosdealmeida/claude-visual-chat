using System;
using System.Globalization;
using Xunit;

namespace ClaudeVisualChat.Tests;

public class ChatStringsTests
{
    private static readonly ChatStrings En = ChatStrings.English;
    private static readonly ChatStrings Pt = ChatStrings.PortugueseBrazil;

    [Theory]
    [InlineData(0, "0", "0")]
    [InlineData(999, "999", "999")]
    [InlineData(1000, "1,000", "1.000")]
    [InlineData(38557, "38,557", "38.557")]
    [InlineData(1234567, "1,234,567", "1.234.567")]
    [InlineData(-4200, "-4,200", "-4.200")]
    public void Numbers_use_the_language_separators(long value, string english, string portuguese)
    {
        Assert.Equal(english, En.FormatNumber(value));
        Assert.Equal(portuguese, Pt.FormatNumber(value));
    }

    [Theory]
    [InlineData(0, "0.0 s")]
    [InlineData(4200, "4.2 s")]
    [InlineData(4249, "4.2 s")]
    [InlineData(4250, "4.3 s")]
    [InlineData(59940, "59.9 s")]
    [InlineData(59950, "1:00")]
    [InlineData(60000, "1:00")]
    [InlineData(125000, "2:05")]
    [InlineData(3_725_000, "62:05")]
    [InlineData(-5, "0.0 s")]
    public void Durations(long ms, string expected) => Assert.Equal(expected, En.FormatDuration(ms));

    [Fact]
    public void Portuguese_duration_uses_a_comma() => Assert.Equal("4,2 s", Pt.FormatDuration(4200));

    [Fact]
    public void Footer_keeps_only_what_is_known()
    {
        Assert.Equal("4.2 s · 38,557 in · 502 out · $0.08", En.FormatFooter(new TurnFooterItem(4200, 38557, 502, 0.0839, false)));
        Assert.Equal("4,2 s · 38.557 entrada · 502 saída · US$ 0,08", Pt.FormatFooter(new TurnFooterItem(4200, 38557, 502, 0.0839, false)));
        Assert.Equal("$1,234.57", En.FormatFooter(new TurnFooterItem(null, null, null, 1234.567, false)));
        Assert.Equal("3.0 s", En.FormatFooter(new TurnFooterItem(3000, null, null, null, false)));
        Assert.Equal("", En.FormatFooter(new TurnFooterItem(null, null, null, null, true)));
    }

    [Fact]
    public void Odd_templates_never_throw()
    {
        var odd = new ChatStrings { Footer = "{9} {x} {0", MoreLines = "{1} {0} {" };
        Assert.Equal("{9} {x} {0", odd.FormatFooter(new TurnFooterItem(1, 1, 1, 1, false)));
        Assert.Equal("{1} 5 {", odd.FormatMoreLines(5));
    }

    [Fact]
    public void Texts_in_both_languages()
    {
        Assert.Equal("+9,988 lines", En.FormatMoreLines(9988));
        Assert.Equal("+9.988 linhas", Pt.FormatMoreLines(9988));
        Assert.Equal("Interrupted by the user", En.Notice(NoticeKind.Interrupted));
        Assert.Equal("Interrompido por você", Pt.Notice(NoticeKind.Interrupted));
        Assert.Equal("Conversa resumida", Pt.Notice(NoticeKind.Compacted));
        Assert.Equal("Tentando de novo a chamada à API…", Pt.Notice(NoticeKind.ApiRetry));
        Assert.Equal("A resposta terminou com erro", Pt.Notice(NoticeKind.TurnFailed));
        Assert.Equal("Pensando", Pt.Thinking);
        Assert.Equal("Recusado", Pt.ToolRejected);
        Assert.Equal("Rejected", En.ToolRejected);
    }

    [Fact]
    public void Prompt_and_reply_box_texts_in_both_languages()
    {
        Assert.Equal("Reply to Claude…", En.InputPlaceholder);
        Assert.Equal("Responder ao Claude…", Pt.InputPlaceholder);
        Assert.Equal(("Send", "Stop"), (En.Send, En.Stop));
        Assert.Equal(("Enviar", "Parar"), (Pt.Send, Pt.Stop));
        Assert.Equal("Claude wants to run this command:", En.PermissionRunCommand);
        Assert.Equal("O Claude quer rodar este comando:", Pt.PermissionRunCommand);
        Assert.Equal("Claude wants to edit this file:", En.PermissionEditFile);
        Assert.Equal("O Claude quer editar este arquivo:", Pt.PermissionEditFile);
        Assert.Equal("Yes", En.PermissionYes);
        Assert.Equal("Sim", Pt.PermissionYes);
        Assert.Equal("Yes, and don't ask again this session", En.PermissionYesAlways);
        Assert.Equal("Sim, e não perguntar de novo nesta conversa", Pt.PermissionYesAlways);
        Assert.Equal("No, and tell Claude what to do differently", En.PermissionNo);
        Assert.Equal("Não, e dizer o que fazer", Pt.PermissionNo);
        Assert.Equal("Tell Claude what to do instead", En.DenyPlaceholder);
        Assert.Equal("Diga ao Claude o que fazer", Pt.DenyPlaceholder);
    }

    [Theory]
    [InlineData("plan", "Plan", "Plano")]
    [InlineData("default", "Manual", "Manual")] // the extension's name for the default mode
    [InlineData("acceptEdits", "Edit automatically", "Editar sozinho")]
    [InlineData("auto", "Auto", "Automático")]
    [InlineData("bypassPermissions", "bypassPermissions", "bypassPermissions")]
    [InlineData("", "", "")]
    public void Modes_have_labels_and_unknown_ones_keep_their_name(string mode, string english, string portuguese)
    {
        Assert.Equal(english, En.ModeLabel(mode));
        Assert.Equal(portuguese, Pt.ModeLabel(mode));
    }

    [Fact]
    public void Tool_name_and_waiting_count_fill_their_templates()
    {
        Assert.Equal("Claude wants to use Fetch:", En.FormatUseTool("Fetch"));
        Assert.Equal("O Claude quer usar Fetch:", Pt.FormatUseTool("Fetch"));
        Assert.Equal("+1 waiting", En.FormatMorePending(1));
        Assert.Equal("+1.200 na fila", Pt.FormatMorePending(1200));
        var odd = new ChatStrings { PermissionUseTool = "{1} {0} {", MorePending = "{x}" };
        Assert.Equal("{1} Fetch {", odd.FormatUseTool("Fetch"));
        Assert.Equal("{x}", odd.FormatMorePending(3));
    }

    [Fact]
    public void Times_show_in_local_time_in_each_language_format()
    {
        var at = new DateTimeOffset(2026, 10, 19, 22, 27, 0, TimeSpan.Zero);
        var local = at.ToLocalTime();
        Assert.Equal(local.ToString("h:mm tt", CultureInfo.InvariantCulture), En.FormatTime(at));
        Assert.Equal(local.ToString("HH:mm", CultureInfo.InvariantCulture), Pt.FormatTime(at));
        Assert.Matches("^[0-9]{1,2}:[0-9]{2} (AM|PM)$", En.FormatTime(at));
    }

    [Theory]
    [InlineData(0, 0, "Modified", "Modificado")]
    [InlineData(1, 0, "Added 1 line", "Adicionou 1 linha")]
    [InlineData(3, 0, "Added 3 lines", "Adicionou 3 linhas")]
    [InlineData(0, 2, "Removed 2 lines", "Removeu 2 linhas")]
    [InlineData(2, 1, "Added 2 lines, removed 1 line", "Adicionou 2 linhas, removeu 1 linha")]
    [InlineData(1200, 0, "Added 1,200 lines", "Adicionou 1.200 linhas")]
    public void Edit_subtitles(int added, int removed, string english, string portuguese)
    {
        Assert.Equal(english, En.FormatEditChange(new LineChange(added, removed)));
        Assert.Equal(portuguese, Pt.FormatEditChange(new LineChange(added, removed)));
    }

    [Fact]
    public void Line_counts_and_read_ranges()
    {
        Assert.Equal("47 lines", En.FormatLines(47));
        Assert.Equal("1 line", En.FormatLines(1));
        Assert.Equal("47 linhas", Pt.FormatLines(47));
        Assert.Equal("1 linha", Pt.FormatLines(1));
        Assert.Equal("(lines 2-61)", En.FormatReadRange(new LineRange(2, 61)));
        Assert.Equal("(from line 2)", En.FormatReadRange(new LineRange(2, null)));
        Assert.Equal("(linhas 2-61)", Pt.FormatReadRange(new LineRange(2, 61)));
        Assert.Equal("(da linha 1001)", Pt.FormatReadRange(new LineRange(1001, null))); // a line number, no separator
    }

    [Fact]
    public void Busy_placeholder_and_body_labels()
    {
        Assert.Equal("Queue another message…", En.InputPlaceholderBusy);
        Assert.Equal("Pôr outra mensagem na fila…", Pt.InputPlaceholderBusy);
        Assert.Equal("IN", En.ToolInput);
        Assert.Equal("OUT", En.ToolOutput);
        Assert.Equal("Edit failed", En.EditFailed);
        Assert.Equal("A gravação falhou", Pt.WriteFailed);
        Assert.Equal("Run in background", En.RunInBackground);
        Assert.Equal("Rodando em segundo plano", Pt.RunInBackground);
        Assert.Equal("Working…", En.Working);
        Assert.Equal("Trabalhando…", Pt.Working);
    }

    [Theory]
    [InlineData(0, "Thought for 0s", "Pensou por 0 s")]
    [InlineData(499, "Thought for 0s", "Pensou por 0 s")]
    [InlineData(500, "Thought for 1s", "Pensou por 1 s")] // rounded like the extension's Math.round
    [InlineData(2600, "Thought for 3s", "Pensou por 3 s")]
    [InlineData(1_234_567, "Thought for 1235s", "Pensou por 1235 s")]
    [InlineData(-5, "Thought for 0s", "Pensou por 0 s")]
    public void How_long_a_thought_took(long ms, string english, string portuguese)
    {
        Assert.Equal(english, En.FormatThoughtFor(ms));
        Assert.Equal(portuguese, Pt.FormatThoughtFor(ms));
    }

    [Fact]
    public void A_time_format_dotnet_refuses_falls_back_to_hours_and_minutes()
    {
        var odd = new ChatStrings { TimeFormat = "%" };
        var at = new DateTimeOffset(2026, 10, 19, 22, 27, 0, TimeSpan.Zero);
        Assert.Equal(at.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture), odd.FormatTime(at));
    }

    [Fact]
    public void A_text_can_be_changed_by_copy()
    {
        var custom = ChatStrings.English with { Thinking = "Hmm" };
        Assert.Equal("Hmm", custom.Thinking);
        Assert.Equal("Thinking", ChatStrings.English.Thinking);
        Assert.Equal(",", custom.ThousandsSeparator);
    }
}
