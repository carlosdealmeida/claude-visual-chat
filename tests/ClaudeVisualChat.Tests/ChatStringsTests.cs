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
    public void Copy_texts_in_both_languages()
    {
        Assert.Equal("Copied to clipboard (5 characters, 1 line)", En.FormatCopied("hello"));
        Assert.Equal("Copiado para a área de transferência (5 caracteres, 1 linha)", Pt.FormatCopied("hello"));
        Assert.Equal("Copied to clipboard (38,557 characters, 3 lines)", En.FormatCopied(new string('x', 38555) + "\n\n"));
        Assert.Equal("Copiado para a área de transferência (38.557 caracteres, 3 linhas)", Pt.FormatCopied(new string('x', 38555) + "\n\n"));
        Assert.Equal("Copied to clipboard (0 characters, 1 line)", En.FormatCopied(""));
        Assert.Equal("Only 20 assistant messages available to copy", En.FormatCopyOnly(20));
        Assert.Equal("Só há 20 respostas do assistente para copiar", Pt.FormatCopyOnly(20));
        Assert.Equal("Only 1 assistant messages available to copy", En.FormatCopyOnly(1));
        Assert.Equal("Só há 1 resposta do assistente para copiar", Pt.FormatCopyOnly(1));
        Assert.Equal("Usage: /copy [N] where N is 1 (latest), 2, 3, … Got: {0}", En.FormatCopyUsage("{0}"));
        Assert.Equal("Uso: /copy [N], onde N é 1 (a mais recente), 2, 3, … Recebido: x", Pt.FormatCopyUsage("x"));
        Assert.Equal("Copy Claude's last response to clipboard (or /copy N for the Nth-latest)", En.CommandCopyDescription);
        Assert.Equal("No assistant message to copy", En.CopyNothing);
        Assert.Equal("Could not copy to clipboard", En.CopyFailed);
        Assert.Equal("Copying once the reply finishes", En.CopyWaiting);
        Assert.Equal("Copiando quando a resposta terminar", Pt.CopyWaiting);
        Assert.Equal("Copy cancelled", En.CopyCancelled);
        Assert.Equal("Cópia cancelada", Pt.CopyCancelled);
    }

    [Fact]
    public void The_queue_texts()
    {
        Assert.Equal("You, queued", En.YouQueued);
        Assert.Equal("Você, na fila", Pt.YouQueued);
        Assert.Equal("Message queued.", En.MessageQueued);
        Assert.Equal("Mensagem na fila.", Pt.MessageQueued);
        Assert.Equal("Ran at once, while Claude was working.", En.RanAtOnce);
        Assert.Equal("Rodou na hora, enquanto o Claude trabalhava.", Pt.RanAtOnce);
    }

    [Fact]
    public void Texts_in_both_languages()
    {
        Assert.Equal("+9,988 lines", En.FormatMoreLines(9988));
        Assert.Equal("+9.988 linhas", Pt.FormatMoreLines(9988));
        Assert.Equal("Interrupted", En.Notice(NoticeKind.Interrupted));
        Assert.Equal("Interrompido", Pt.Notice(NoticeKind.Interrupted));
        Assert.Equal("Tool interrupted", En.Notice(NoticeKind.ToolInterrupted));
        Assert.Equal("Ferramenta interrompida", Pt.Notice(NoticeKind.ToolInterrupted));
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
        Assert.Equal("Ask Claude to edit…", En.InputPlaceholder);
        Assert.Equal("Peça ao Claude para editar…", Pt.InputPlaceholder);
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

    [Fact]
    public void File_texts_in_both_languages()
    {
        Assert.Equal("File is larger than 25 MB", En.FormatFileTooBig(25L * 1024 * 1024));
        Assert.Equal("Arquivo maior que 25 MB", Pt.FormatFileTooBig(25L * 1024 * 1024));
        Assert.Equal("Only PDF, text and CSV files", En.FileNotSupported);
        Assert.Equal("Só arquivos PDF, de texto e CSV", Pt.FileNotSupported);
        Assert.Equal("Up to 5 files per message", En.FormatTooManyFiles(5));
        Assert.Equal("Até 5 arquivos por mensagem", Pt.FormatTooManyFiles(5));
        Assert.Equal("Sending 42%", En.FormatFileUploading(0.42));
        Assert.Equal("Enviando 42%", Pt.FormatFileUploading(0.42));
        Assert.Equal("Sending 0%", En.FormatFileUploading(-1));
        Assert.Equal("Sending 100%", En.FormatFileUploading(7));
        Assert.Equal("Images, PDF, text and CSV", En.AttachableFiles);
        Assert.Equal("Imagens, PDF, texto e CSV", Pt.AttachableFiles);
    }

    [Theory]
    [InlineData(0, "0 KB", "0 KB")]
    [InlineData(1, "1 KB", "1 KB")]
    [InlineData(1024, "1 KB", "1 KB")]
    [InlineData(1025, "2 KB", "2 KB")]
    [InlineData(1023 * 1024, "1,023 KB", "1.023 KB")]
    [InlineData(1023 * 1024 + 1, "1 MB", "1 MB")] // 1024 KB once rounded up: in MB
    [InlineData(1024 * 1024 - 1, "1 MB", "1 MB")]
    [InlineData(1024 * 1024, "1 MB", "1 MB")]
    [InlineData(1536 * 1024, "1.5 MB", "1,5 MB")]
    [InlineData(20_000_000, "19.1 MB", "19,1 MB")]
    [InlineData(25L * 1024 * 1024, "25 MB", "25 MB")]
    public void File_sizes_in_KB_or_MB(long bytes, string english, string portuguese)
    {
        Assert.Equal(english, En.FormatFileSize(bytes));
        Assert.Equal(portuguese, Pt.FormatFileSize(bytes));
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
    public void Touch_questions_fill_their_templates()
    {
        // the touch layout's request asks in a few words: the title is the question
        Assert.Equal("Run this command?", En.PermissionRunCommandQuestion);
        Assert.Equal("Rodar este comando?", Pt.PermissionRunCommandQuestion);
        Assert.Equal("Edit {0}?", En.PermissionEditFileQuestion);
        Assert.Equal("Editar {0}?", Pt.PermissionEditFileQuestion);
        Assert.Equal("Use {0}?", En.PermissionUseToolQuestion);
        Assert.Equal("Usar {0}?", Pt.PermissionUseToolQuestion);
        Assert.Equal("Edit OrderService.cs?", En.FormatEditFileQuestion("OrderService.cs"));
        Assert.Equal("Editar OrderService.cs?", Pt.FormatEditFileQuestion("OrderService.cs"));
        Assert.Equal("Use Fetch?", En.FormatUseToolQuestion("Fetch"));
        Assert.Equal("Usar Fetch?", Pt.FormatUseToolQuestion("Fetch"));
        var odd = new ChatStrings { PermissionEditFileQuestion = "{1} {0} {", PermissionUseToolQuestion = "{x}" };
        Assert.Equal("{1} a.cs {", odd.FormatEditFileQuestion("a.cs"));
        Assert.Equal("{x}", odd.FormatUseToolQuestion("Fetch"));
        Assert.Equal("Edit ?", En.FormatEditFileQuestion(null!));                        // never throws
        Assert.Equal("Use ?", En.FormatUseToolQuestion(null!));
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

    [Theory]
    [InlineData(0, "0", "0")]
    [InlineData(340, "340", "340")]
    [InlineData(999, "999", "999")]
    [InlineData(1000, "1.0k", "1,0k")]
    [InlineData(1234, "1.2k", "1,2k")]
    [InlineData(1250, "1.3k", "1,3k")]
    [InlineData(48700, "48.7k", "48,7k")]
    public void The_thinking_estimate_is_whole_below_a_thousand_and_thousands_with_a_decimal_after(long tokens, string english, string portuguese)
    {
        Assert.Equal(" · " + english + " tokens", En.FormatThinkingTokens(tokens));
        Assert.Equal(" · " + portuguese + " tokens", Pt.FormatThinkingTokens(tokens));
    }

    [Fact]
    public void The_thinking_and_compaction_texts_in_both_languages()
    {
        Assert.Equal("Thinking...", En.ThinkingNow);
        Assert.Equal("Pensando...", Pt.ThinkingNow);
        Assert.Equal("Conversation was compacted to free up context.", En.CompactionNoSummary);
        Assert.Equal("A conversa foi compactada para liberar contexto.", Pt.CompactionNoSummary);
        Assert.Equal("Switched to Opus 5.5", En.FormatSwitchedTo("Opus 5.5"));
        Assert.Equal("Modelo trocado para Opus 5.5", Pt.FormatSwitchedTo("Opus 5.5"));
        Assert.Equal("Switched model", En.SwitchedModel);
        Assert.Equal("Modelo trocado", Pt.SwitchedModel);
    }

    [Theory]
    [InlineData(36326, "Compacted chat · manual · 36k tokens freed", "Conversa compactada · manual · 36k tokens liberados")]
    [InlineData(36500, "Compacted chat · manual · 37k tokens freed", "Conversa compactada · manual · 37k tokens liberados")]
    [InlineData(499, "Compacted chat · manual · 0k tokens freed", "Conversa compactada · manual · 0k tokens liberados")]
    [InlineData(1_204_000, "Compacted chat · manual · 1204k tokens freed", "Conversa compactada · manual · 1204k tokens liberados")]
    public void The_compaction_head_rounds_the_thousands_and_names_the_trigger(long before, string english, string portuguese)
    {
        Assert.Equal(english, En.FormatCompaction("manual", before));
        Assert.Equal(portuguese, Pt.FormatCompaction("manual", before));
    }

    [Fact]
    public void The_compaction_head_keeps_only_what_is_known()
    {
        Assert.Equal("Compacted chat · auto", En.FormatCompaction("auto", null));
        Assert.Equal("Compacted chat", En.FormatCompaction("", 36326));
        Assert.Equal("Compacted chat", En.FormatCompaction(null, null));
        Assert.Equal("Conversa compactada", Pt.FormatCompaction(null, null));
    }

    // words that are the same in both languages
    private static readonly string[] SameInBoth =
    {
        nameof(ChatStrings.ToolInput), nameof(ChatStrings.ToolOutput), nameof(ChatStrings.ModeDefault),
        nameof(ChatStrings.RemoteControl),
        nameof(ChatStrings.EffortMax), nameof(ChatStrings.EffortAuto),
        nameof(ChatStrings.UsageResetsDay), nameof(ChatStrings.PanelLoading),
        nameof(ChatStrings.AgentTokens), nameof(ChatStrings.ThinkingTokensEstimate),
        nameof(ChatStrings.AgentToolTitle), nameof(ChatStrings.WebFetchToolTitle), nameof(ChatStrings.WebSearchToolTitle),
        nameof(ChatStrings.ChromeToolTitle), nameof(ChatStrings.TaggedTokens),
    };

    [Fact]
    public void Every_text_differs_between_the_languages_except_the_listed_ones()
    {
        var same = new System.Collections.Generic.List<string>();
        foreach (var p in typeof(ChatStrings).GetProperties())
        {
            if (p.PropertyType != typeof(string)) continue;
            if (p.Name == nameof(ChatStrings.ThousandsSeparator) || p.Name == nameof(ChatStrings.DecimalSeparator)) continue;
            Assert.False(string.IsNullOrEmpty((p.GetValue(En) as string)), p.Name);
            Assert.False(string.IsNullOrEmpty((p.GetValue(Pt) as string)), p.Name);
            if ((p.GetValue(En) as string) == (p.GetValue(Pt) as string) && Array.IndexOf(SameInBoth, p.Name) < 0) same.Add(p.Name);
        }
        Assert.Empty(same);
    }

    [Fact]
    public void Bar_texts_in_english()
    {
        Assert.Equal("Add", En.AddAttachment);
        Assert.Equal("Show command menu (/)", En.ShowCommands);
        Assert.Equal("Prompt cache warm, about {0} min left.", En.CacheWarm);
        Assert.Equal("Select a model", En.SelectModel);
        Assert.Equal("Mostrar o menu de comandos (/)", Pt.ShowCommands);
        Assert.Equal("Cache do prompt ativo, cerca de {0} min restantes.", Pt.CacheWarm);
        Assert.Equal("Cache do prompt provavelmente expirado (parado há {0}).", Pt.CacheExpired);
        Assert.Equal("Dismiss", En.Dismiss);
        Assert.Equal("Dispensar", Pt.Dismiss);
        Assert.Equal("Remote Control error: {0} · Run /remote-control to dismiss", En.RemoteControlFailedHint);
        Assert.Equal("Erro no Remote Control: {0} · Use /remote-control para dispensar", Pt.RemoteControlFailedHint);
        Assert.Equal("Remote Control error: x · Run /remote-control to dismiss", En.FormatRemoteControlFailedHint("x"));
    }

    [Theory]
    [InlineData("low", "Low", "Baixo")]
    [InlineData("medium", "Medium", "Médio")]
    [InlineData("high", "High", "Alto")]
    [InlineData("xhigh", "Extra high", "Muito alto")]
    [InlineData("max", "Max", "Máximo")]
    [InlineData(null, "Auto", "Automático")]
    [InlineData("weird", "weird", "weird")]
    public void Effort_labels(string? level, string english, string portuguese)
    {
        Assert.Equal(english, En.EffortLabel(level));
        Assert.Equal(portuguese, Pt.EffortLabel(level));
    }

    [Theory]
    [InlineData(0, "0m")]
    [InlineData(59, "0m")]
    [InlineData(60, "1m")]
    [InlineData(12 * 60, "12m")]
    [InlineData(75 * 60, "1h 15m")]
    [InlineData(2 * 3600, "2h 0m")]
    [InlineData(27 * 3600, "1d 3h")]
    [InlineData(51 * 3600, "2d 3h")]
    public void Idle_time_is_compact(int seconds, string expected)
    {
        Assert.Equal(expected, En.FormatIdle(TimeSpan.FromSeconds(seconds)));
        Assert.Equal(expected, Pt.FormatIdle(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Panel_texts()
    {
        var usage = new ContextUsage(36174, 200000, 18, Array.Empty<ContextCategory>());
        Assert.Equal("36,174 / 200,000 (18%)", En.FormatContextTotal(usage));
        Assert.Equal("36.174 / 200.000 (18 %)", Pt.FormatContextTotal(usage));

        Assert.Equal("Current session (5h)", En.UsageWindowLabel("five_hour"));
        Assert.Equal("Weekly (7 days)", En.UsageWindowLabel("seven_day"));
        Assert.Equal("Sessão atual (5h)", Pt.UsageWindowLabel("five_hour"));
        Assert.Equal("Semanal (7 dias)", Pt.UsageWindowLabel("seven_day"));
        Assert.Equal("seven_day_opus", Pt.UsageWindowLabel("seven_day_opus"));

        Assert.Equal("1%", En.FormatUsagePercent(0.01));
        Assert.Equal("12 %", Pt.FormatUsagePercent(0.12));
        Assert.Equal("0%", En.FormatUsagePercent(-0.5));
        Assert.Equal("…", En.PanelLoading);

        // today only the time; another day its short name first (2026-10-22 is a Thursday)
        var now = new DateTimeOffset(new DateTime(2026, 10, 22, 9, 0, 0, DateTimeKind.Local));
        var today = new DateTimeOffset(new DateTime(2026, 10, 22, 15, 30, 0, DateTimeKind.Local));
        var saturday = new DateTimeOffset(new DateTime(2026, 10, 24, 8, 5, 0, DateTimeKind.Local));
        Assert.Equal("Resets 3:30 PM", En.FormatUsageResets(today, now));
        Assert.Equal("Resets Sat 8:05 AM", En.FormatUsageResets(saturday, now));
        Assert.Equal("Renova 15:30", Pt.FormatUsageResets(today, now));
        Assert.Equal("Renova sáb 08:05", Pt.FormatUsageResets(saturday, now));
        // a list of day names that is not seven long falls back to the English ones
        Assert.Equal("Resets Sat 8:05 AM", (En with { DayNames = "x y" }).FormatUsageResets(saturday, now));
    }

    [Fact]
    public void The_date_line_names_the_day_and_the_month_and_the_year_outside_this_one()
    {
        DateTimeOffset Local(int year, int month, int day) => new DateTimeOffset(new DateTime(year, month, day, 15, 45, 0, DateTimeKind.Local));
        var now = Local(2025, 10, 26);
        Assert.Equal("Sat, Oct 25", En.FormatDate(Local(2025, 10, 25), now));
        Assert.Equal("sáb, 25 de out", Pt.FormatDate(Local(2025, 10, 25), now));
        Assert.Equal("Thu, Jan 1", En.FormatDate(Local(2026, 1, 1), Local(2026, 3, 1)));
        Assert.Equal("Sat, Oct 25, 2025", En.FormatDate(Local(2025, 10, 25), Local(2026, 10, 28)));
        Assert.Equal("sáb, 25 de out de 2025", Pt.FormatDate(Local(2025, 10, 25), Local(2026, 10, 28)));
        Assert.Equal("Wed, Dec 31, 2025", En.FormatDate(Local(2025, 12, 31), Local(2026, 1, 1)));
        // a list of month names that is not twelve long falls back to the English ones
        Assert.Equal("Sat, Oct 25", (En with { MonthNames = "a b c" }).FormatDate(Local(2025, 10, 25), now));
        // a format of the host's
        Assert.Equal("25/Oct (Sat)", (En with { DateFormat = "{2}/{1} ({0})" }).FormatDate(Local(2025, 10, 25), now));
    }

    [Theory]
    [InlineData(-3, 0, "2026-10-25 15:45:12 GMT-3")]
    [InlineData(5, 30, "2026-10-25 15:45:12 GMT+5:30")]
    [InlineData(-9, -30, "2026-10-25 15:45:12 GMT-9:30")]
    [InlineData(0, 0, "2026-10-25 15:45:12 GMT")]
    [InlineData(1, 0, "2026-10-25 15:45:12 GMT+1")]
    public void The_whole_time_of_a_message_has_its_offset_from_utc(int hours, int minutes, string expected)
    {
        var at = new DateTimeOffset(2026, 10, 25, 15, 45, 12, new TimeSpan(hours, minutes, 0));
        Assert.Equal(expected, ChatStrings.FormatTimeWithOffset(at));
        // the tooltip shows it in local time
        var local = at.ToLocalTime();
        Assert.Equal(ChatStrings.FormatTimeWithOffset(local), ChatStrings.FormatFullTime(at));
    }

    [Theory]
    [InlineData(0, "0", "0")]
    [InlineData(999, "999", "999")]
    [InlineData(1000, "1.0k", "1,0k")]
    [InlineData(26316, "26.3k", "26,3k")]
    [InlineData(168549, "168.5k", "168,5k")]
    [InlineData(999949, "999.9k", "999,9k")]
    [InlineData(999950, "1.0M", "1,0M")]
    [InlineData(1_250_000, "1.3M", "1,3M")]
    public void Agent_tokens_are_short(long tokens, string english, string portuguese)
    {
        Assert.Equal(english, En.FormatShortTokens(tokens));
        Assert.Equal(portuguese, Pt.FormatShortTokens(tokens));
    }

    [Theory]
    [InlineData(0, "0", "0")]
    [InlineData(800, "800", "800")]
    [InlineData(1_000, "1k", "1k")]
    [InlineData(150_000, "150k", "150k")]
    [InlineData(150_600, "150.6k", "150,6k")]
    [InlineData(999_949, "999.9k", "999,9k")]
    [InlineData(999_950, "1M", "1M")]                  // 1.0M once rounded
    [InlineData(1_000_000, "1M", "1M")]
    [InlineData(1_250_000, "1.3M", "1,3M")]
    [InlineData(10_000_000, "10M", "10M")]
    [InlineData(1_000_000_000, "1,000M", "1.000M")]    // a separator in the whole part does not stand for the decimal one
    public void Short_tokens_trimmed_drop_the_trailing_zero(long tokens, string english, string portuguese)
    {
        Assert.Equal(english, En.FormatShortTokensTrimmed(tokens));
        Assert.Equal(portuguese, Pt.FormatShortTokensTrimmed(tokens));
    }

    [Theory]
    [InlineData(0, "0% used", "0% utilizado")]
    [InlineData(14.49, "14% used", "14% utilizado")]
    [InlineData(14.5, "15% used", "15% utilizado")]
    [InlineData(15.06, "15% used", "15% utilizado")]
    [InlineData(34.00000000000001, "34% used", "34% utilizado")]   // 0.34 * 100
    [InlineData(100, "100% used", "100% utilizado")]
    [InlineData(-3, "0% used", "0% utilizado")]
    [InlineData(double.NaN, "0% used", "0% utilizado")]
    public void Percent_used_is_a_whole_number(double percent, string english, string portuguese)
    {
        Assert.Equal(english, En.FormatPercentUsed(percent));
        Assert.Equal(portuguese, Pt.FormatPercentUsed(percent));
    }

    [Theory]
    [InlineData(0.1134, "11.3%", "11,3%")]
    [InlineData(0.0204, "2.0%", "2,0%")]
    [InlineData(0.0099, "1.0%", "1,0%")]
    [InlineData(0.8164, "81.6%", "81,6%")]
    [InlineData(0, "0.0%", "0,0%")]
    [InlineData(1, "100.0%", "100,0%")]
    [InlineData(-0.5, "0.0%", "0,0%")]
    [InlineData(double.NaN, "0.0%", "0,0%")]
    public void A_share_of_the_window_has_one_decimal(double share, string english, string portuguese)
    {
        Assert.Equal(english, En.FormatSharePercent(share));
        Assert.Equal(portuguese, Pt.FormatSharePercent(share));
    }

    [Fact]
    public void Usage_sheet_texts_in_both_languages()
    {
        Assert.Equal(("Context", "Contexto"), (En.ContextSection, Pt.ContextSection));
        Assert.Equal(("Context window", "Janela de contexto"), (En.ContextWindow, Pt.ContextWindow));
        Assert.Equal(("{0}% used", "{0}% utilizado"), (En.PercentUsed, Pt.PercentUsed));
        Assert.Equal(("{0} of {1} tokens", "{0} de {1} tokens"), (En.TokensOfWindow, Pt.TokensOfWindow));
        Assert.Equal(("Plan usage", "Uso do plano"), (En.PlanUsage, Pt.PlanUsage));
        Assert.Equal(("Refresh", "Atualizar"), (En.Refresh, Pt.Refresh));
        Assert.Equal(("Usage", "Uso"), (En.UsageTitle, Pt.UsageTitle));     // the sheet's title is the /usage panel's

        Assert.Equal("150.6k of 1M tokens", En.FormatTokensOfWindow(150_600, 1_000_000));
        Assert.Equal("150,6k de 1M tokens", Pt.FormatTokensOfWindow(150_600, 1_000_000));
        Assert.Equal("40k of 200k tokens", En.FormatTokensOfWindow(40_000, 200_000));

        // a text of the app's own is filled all the same, and an odd template never throws
        var own = En with { PercentUsed = "used: {0} %", TokensOfWindow = "{1} / {0}" };
        Assert.Equal("used: 15 %", own.FormatPercentUsed(15.2));
        Assert.Equal("1M / 150.6k", own.FormatTokensOfWindow(150_600, 1_000_000));
        var odd = new ChatStrings { PercentUsed = "{x}", TokensOfWindow = "{2} {" };
        Assert.Equal("{x}", odd.FormatPercentUsed(15));
        Assert.Equal("{2} {", odd.FormatTokensOfWindow(1, 2));
    }

    [Theory]
    [InlineData(0, "0s")]
    [InlineData(44_999, "44s")]
    [InlineData(60_000, "1m 0s")]
    [InlineData(434_000, "7m 14s")]
    [InlineData(3_725_000, "62m 5s")]
    [InlineData(-5, "0s")]
    public void Agent_time_is_whole_seconds(long ms, string expected) => Assert.Equal(expected, ChatStrings.FormatAgentTime(ms));

    [Fact]
    public void Agent_map_texts_in_both_languages()
    {
        Assert.Equal("0 agents", En.FormatAgentsChip(0));
        Assert.Equal("1 agent", En.FormatAgentsChip(1));
        Assert.Equal("3 agentes", Pt.FormatAgentsChip(3));
        Assert.Equal("1 agente", Pt.FormatAgentsChip(1));
        Assert.Equal("73 agents · click an agent for details", En.FormatAgentMapSubtitle(73));
        Assert.Equal("73 agentes · clique num agente para ver detalhes", Pt.FormatAgentMapSubtitle(73));
        Assert.Equal(("Agent map", "Mapa de agentes"), (En.AgentMapTitle, Pt.AgentMapTitle));
        Assert.Equal(("Main agent", "Agente principal"), (En.AgentMain, Pt.AgentMain));
        Assert.Equal("Spawned by Review Task 9", En.FormatAgentSpawnedBy("Review Task 9"));
        Assert.Equal("Chamado por Review Task 9", Pt.FormatAgentSpawnedBy("Review Task 9"));
        Assert.Equal(("Spawned by the main agent", "Chamado pelo agente principal"), (En.AgentSpawnedByMain, Pt.AgentSpawnedByMain));
        Assert.Equal(("Running", "Rodando"), (En.AgentStatusLabel(AgentStatus.Running), Pt.AgentStatusLabel(AgentStatus.Running)));
        Assert.Equal(("Finished", "Terminado"), (En.AgentStatusLabel(AgentStatus.Completed), Pt.AgentStatusLabel(AgentStatus.Completed)));
        Assert.Equal(("Failed", "Falhou"), (En.AgentStatusLabel(AgentStatus.Failed), Pt.AgentStatusLabel(AgentStatus.Failed)));
        Assert.Equal(("Stopped", "Parado"), (En.AgentStatusLabel(AgentStatus.Stopped), Pt.AgentStatusLabel(AgentStatus.Stopped)));
        Assert.Equal(("background", "segundo plano"), (En.AgentBackground, Pt.AgentBackground));
        Assert.Equal(("No result yet", "Sem resultado ainda"), (En.AgentNoResult, Pt.AgentNoResult));
        Assert.Equal("168.5k tokens", En.FormatAgentTokens(168_549));
        Assert.Equal("249,5k tokens no contexto", Pt.FormatAgentContextTokens(249_500));
    }

    [Fact]
    public void Cache_minutes()
    {
        Assert.Equal("59m", En.FormatCacheMinutes(59));
        Assert.Equal("5m", Pt.FormatCacheMinutes(5));
    }

    [Fact]
    public void Plan_document_texts()
    {
        Assert.Equal(("Claude’s Plan", "Plano do Claude"), (En.PlanTitle, Pt.PlanTitle));
        Assert.Equal(("Plan Mode", "Modo de plano"), (En.PlanModeTool, Pt.PlanModeTool));
        Assert.Equal(("User approved the plan", "O usuário aprovou o plano"), (En.PlanApproved, Pt.PlanApproved));
        Assert.Equal(("Stayed in plan mode", "Continuou no modo de plano"), (En.PlanStayedInPlanMode, Pt.PlanStayedInPlanMode));
        Assert.Equal(("Ready for review", "Pronto para revisão"), (En.PlanReadyForReview, Pt.PlanReadyForReview));
        Assert.Equal(("Select text to add comments on the plan", "Selecione um trecho para comentar o plano"),
            (En.PlanSelectToComment, Pt.PlanSelectToComment));
    }

    [Fact]
    public void Plan_comment_texts()
    {
        Assert.Equal(("Add Comment", "Comentar"), (En.PlanAddComment, Pt.PlanAddComment));
        Assert.Equal(("Add your feedback.", "Escreva seu comentário."), (En.PlanCommentPlaceholder, Pt.PlanCommentPlaceholder));
        Assert.Equal(("Cancel", "Cancelar"), (En.PlanCommentCancel, Pt.PlanCommentCancel));
        Assert.Equal(("Comments (2)", "Comentários (2)"), (En.FormatPlanComments(2), Pt.FormatPlanComments(2)));
        Assert.Equal(("Remove comment", "Remover comentário"), (En.PlanRemoveComment, Pt.PlanRemoveComment));
        Assert.Equal(("Continue planning", "Continuar planejando"), (En.PlanContinuePlanning, Pt.PlanContinuePlanning));
        Assert.Equal(("1 comment will be included as feedback", "1 comentário irá junto como retorno"),
            (En.FormatPlanCommentsIncluded(1), Pt.FormatPlanCommentsIncluded(1)));
        Assert.Equal(("2 comments will be included as feedback", "2 comentários irão junto como retorno"),
            (En.FormatPlanCommentsIncluded(2), Pt.FormatPlanCommentsIncluded(2)));
        Assert.Equal("1,200 comments will be included as feedback", En.FormatPlanCommentsIncluded(1200));
        Assert.Equal(("Send feedback and keep planning", "Enviar retorno e continuar planejando"), (En.PlanSendFeedback, Pt.PlanSendFeedback));
    }

    [Fact]
    public void Background_agent_line()
    {
        const string Hint = " · type /tasks to see its transcript";
        string Line(AgentStatus status, bool waiting = false, string? activity = null, string? error = null, AgentNesting nested = AgentNesting.None) =>
            En.FormatBackgroundAgent(status, waiting, activity, error, "Read the logs", nested);

        Assert.Equal("Working in the background" + Hint, Line(AgentStatus.Running));
        Assert.Equal("Working in the background: Reading a.txt" + Hint, Line(AgentStatus.Running, activity: "  Reading a.txt "));
        Assert.Equal("Working in the background" + Hint, Line(AgentStatus.Running, activity: "Read the logs")); // the description again
        Assert.Equal("Working in the background: " + new string('a', 120) + Hint, Line(AgentStatus.Running, activity: new string('a', 120)));
        Assert.Equal("Working in the background: " + new string('a', 119) + "…" + Hint, Line(AgentStatus.Running, activity: new string('a', 121)));
        Assert.Equal("Waiting for your permission: see the request below" + Hint, Line(AgentStatus.Running, waiting: true, activity: "Reading"));
        Assert.Equal("Waiting for your permission, and so is an agent it started: see the request below" + Hint,
            Line(AgentStatus.Running, waiting: true, nested: AgentNesting.Waiting | AgentNesting.Working));
        Assert.Equal("Failed: Disk full" + Hint, Line(AgentStatus.Failed, error: "Disk full"));
        Assert.Equal("Failed: " + new string('e', 119) + "…" + Hint, Line(AgentStatus.Failed, error: new string('e', 200)));
        // an error of several lines is one line, and the cut at 120 counts after that
        Assert.Equal("Failed: Disk full at /tmp/a" + Hint, Line(AgentStatus.Failed, error: "Disk full\r\n  at /tmp/a\n"));
        Assert.Equal("Failed: " + new string('x', 60) + " " + new string('y', 58) + "…" + Hint,
            Line(AgentStatus.Failed, error: new string('x', 60) + "\n\n" + new string('y', 80)));
        Assert.Equal("Working in the background: Reading a.txt then b.txt" + Hint, Line(AgentStatus.Running, activity: "Reading a.txt\nthen b.txt"));
        Assert.Equal("Failed" + Hint, Line(AgentStatus.Failed));
        Assert.Equal("Finished" + Hint, Line(AgentStatus.Completed));
        Assert.Equal("Stopped" + Hint, Line(AgentStatus.Stopped));

        // what the agents it started do: waiting first, still working only once it is over, failed and stopped together
        Assert.Equal("Working in the background · an agent it started is waiting for your permission: see the request below" + Hint,
            Line(AgentStatus.Running, nested: AgentNesting.Waiting));
        Assert.Equal("Working in the background" + Hint, Line(AgentStatus.Running, nested: AgentNesting.Working));
        Assert.Equal("Finished · an agent it started is still working" + Hint, Line(AgentStatus.Completed, nested: AgentNesting.Working));
        Assert.Equal("Finished · an agent it started is still working · an agent it started failed · an agent it started was stopped" + Hint,
            Line(AgentStatus.Completed, nested: AgentNesting.Working | AgentNesting.Failed | AgentNesting.Stopped));
        Assert.Equal("Stopped · an agent it started failed" + Hint, Line(AgentStatus.Stopped, nested: AgentNesting.Failed | AgentNesting.Stopped));

        Assert.Equal("Trabalhando em segundo plano: Lendo a.txt · digite /tasks para ver a transcrição dele",
            Pt.FormatBackgroundAgent(AgentStatus.Running, false, "Lendo a.txt", null, "x", AgentNesting.None));
        Assert.Equal("Terminado · um agente que ele iniciou falhou · digite /tasks para ver a transcrição dele",
            Pt.FormatBackgroundAgent(AgentStatus.Completed, false, null, null, "x", AgentNesting.Failed));
        Assert.Equal("Falhou: Disco cheio · digite /tasks para ver a transcrição dele",
            Pt.FormatBackgroundAgent(AgentStatus.Failed, false, null, "Disco cheio", "x", AgentNesting.None));
    }

    [Fact]
    public void The_external_message_texts_in_both_languages()
    {
        Assert.Equal("Message from @maria:", En.FormatMessageFrom("cross-session", "maria", true));
        Assert.Equal("Message from @maria", En.FormatMessageFrom("agent", "maria", false));
        Assert.Equal("Message from channel @slack:", En.FormatMessageFrom("channel", "slack", true));
        Assert.Equal("Mensagem de @maria:", Pt.FormatMessageFrom("cross-session", "maria", true));
        Assert.Equal("Mensagem do canal @slack:", Pt.FormatMessageFrom("channel", "slack", true));
        Assert.Equal("From Maria Souza", En.FormatMessageFromSender("Maria Souza"));
        Assert.Equal("De Maria Souza", Pt.FormatMessageFromSender("Maria Souza"));
        Assert.Equal("Sender not recorded: hello", En.FormatSenderNotRecorded("hello"));
        Assert.Equal("Sender not recorded", En.FormatSenderNotRecorded(""));
        Assert.Equal("Remetente não registrado: olá", Pt.FormatSenderNotRecorded("olá"));
        Assert.Equal("(none)", En.TaggedNothing);
        Assert.Equal("(nenhum)", Pt.TaggedNothing);

        Assert.Equal("Task ID", En.TaggedLabel("task-id"));
        Assert.Equal("ID da tarefa", Pt.TaggedLabel("task-id"));
        Assert.Equal("Duration (ms)", En.TaggedLabel("duration_ms"));
        Assert.Equal("Teammate message", En.TaggedLabel("teammate-message"));
        Assert.Equal("Terminal command output", En.TaggedLabel("bash-stdout"));
        Assert.Equal("Mensagem de colega", Pt.TaggedLabel("teammate-message"));
        Assert.Null(En.TaggedLabel("something-else"));
        Assert.Null(En.TaggedLabel(null));
        // every section name a tagged message gives has a text
        foreach (var key in new[]
        {
            "task-id", "tool-use-id", "task-type", "output-file", "status", "summary", "note", "event", "result", "usage", "worktree",
            "subagent_tokens", "tool_uses", "duration_ms", "worktreePath", "worktreeBranch", "other-text", "after-notification",
            "output", "error-output", "exit-code", "teammate-message", "bash-stdout", "bash-stderr", "tick", "mcp-resource-update",
            "mcp-polling-update", "local-command-caveat",
        })
        {
            Assert.False(string.IsNullOrEmpty(En.TaggedLabel(key)), key);
            Assert.False(string.IsNullOrEmpty(Pt.TaggedLabel(key)), key);
        }
    }

    [Fact]
    public void The_invisible_characters_notice_in_both_languages()
    {
        Assert.Equal("Removed 1 invisible character from the pasted text", En.FormatInvisibleNotice(1, InvisibleNoticeKind.Pasted, false));
        Assert.Equal("Removed 12 invisible characters from the pasted text", En.FormatInvisibleNotice(12, InvisibleNoticeKind.Pasted, false));
        Assert.Equal("Removed 3 invisible characters · review and press Enter to send", En.FormatInvisibleNotice(3, InvisibleNoticeKind.Review, false));
        Assert.Equal("Removed 3 invisible characters · review and press Ctrl/Cmd+Enter to send", En.FormatInvisibleNotice(3, InvisibleNoticeKind.Review, true));
        Assert.Equal("Removed 1 invisible character · nothing left to send", En.FormatInvisibleNotice(1, InvisibleNoticeKind.Empty, true));
        Assert.Equal("Removido 1 caractere invisível do texto colado", Pt.FormatInvisibleNotice(1, InvisibleNoticeKind.Pasted, false));
        Assert.Equal("Removidos 3 caracteres invisíveis · revise e pressione Enter para enviar", Pt.FormatInvisibleNotice(3, InvisibleNoticeKind.Review, false));
        Assert.Equal("Removidos 3 caracteres invisíveis · revise e pressione Ctrl/Cmd+Enter para enviar", Pt.FormatInvisibleNotice(3, InvisibleNoticeKind.Review, true));
        Assert.Equal("Removidos 2 caracteres invisíveis · nada restou para enviar", Pt.FormatInvisibleNotice(2, InvisibleNoticeKind.Empty, false));
        Assert.Equal("Removed 1,234 invisible characters from the pasted text", En.FormatInvisibleNotice(1234, InvisibleNoticeKind.Pasted, false));
        Assert.Equal("Dismiss notice", En.DismissNotice);
        Assert.Equal("Dispensar aviso", Pt.DismissNotice);
        Assert.Equal("Message input", En.MessageInput);
        Assert.Equal("Campo de mensagem", Pt.MessageInput);
    }
}
