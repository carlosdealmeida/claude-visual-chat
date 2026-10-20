using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ClaudeVisualChat;

/// <summary>
/// Texts of the chat view, in English (<see cref="English"/>, the default) or Brazilian Portuguese
/// (<see cref="PortugueseBrazil"/>). Change one text by copy: <c>ChatStrings.English with { Thinking = "…" }</c>.
/// Numbers use <see cref="ThousandsSeparator"/> and <see cref="DecimalSeparator"/>, never the system culture.
/// </summary>
public sealed record ChatStrings
{
    private const string Separator = " · ";

    public static ChatStrings English { get; } = new ChatStrings();

    public static ChatStrings PortugueseBrazil { get; } = new ChatStrings
    {
        Thinking = "Pensando",
        MoreLines = "+{0} linhas",
        Interrupted = "Interrompido por você",
        Compacted = "Conversa resumida",
        ApiRetry = "Tentando de novo a chamada à API…",
        TurnFailed = "A resposta terminou com erro",
        Footer = "{0} · {1} entrada · {2} saída · US$ {3}",
        ToolRejected = "Recusado",
        InputPlaceholder = "Responder ao Claude…",
        InputPlaceholderBusy = "Pôr outra mensagem na fila…",
        ThoughtFor = "Pensou por {0} s",
        RunInBackground = "Rodando em segundo plano",
        Working = "Trabalhando…",
        Send = "Enviar",
        Stop = "Parar",
        TimeFormat = "HH:mm",
        EditModified = "Modificado",
        EditAdded = "Adicionou {0}",
        EditRemoved = "Removeu {0}",
        EditAddedRemoved = "Adicionou {0}, removeu {1}",
        EditFailed = "A edição falhou",
        WriteFailed = "A gravação falhou",
        OneLine = "1 linha",
        Lines = "{0} linhas",
        ReadLines = "(linhas {0}-{1})",
        ReadFromLine = "(da linha {0})",
        ModePlan = "Plano",
        ModeDefault = "Manual",
        ModeAcceptEdits = "Editar sozinho",
        ModeAuto = "Automático",
        PermissionRunCommand = "O Claude quer rodar este comando:",
        PermissionEditFile = "O Claude quer editar este arquivo:",
        PermissionUseTool = "O Claude quer usar {0}:",
        PermissionYes = "Sim",
        PermissionYesAlways = "Sim, e não perguntar de novo nesta conversa",
        PermissionYesAlwaysBeyondSession = "Sim, e não perguntar de novo",
        PermissionNo = "Não, e dizer o que fazer",
        DenyPlaceholder = "Diga ao Claude o que fazer",
        MorePending = "+{0} na fila",
        ThousandsSeparator = ".",
        DecimalSeparator = ",",
    };

    /// <summary>The collapsed line of Claude's thinking, while it arrives or when its length is unknown.</summary>
    public string Thinking { get; init; } = "Thinking";

    /// <summary>The collapsed line of a whole thought; <c>{0}</c> is how many seconds it took.</summary>
    public string ThoughtFor { get; init; } = "Thought for {0}s";

    /// <summary>Under a command that runs in the background.</summary>
    public string RunInBackground { get; init; } = "Run in background";

    /// <summary>The line at the end of the list while Claude works and nothing is arriving.</summary>
    public string Working { get; init; } = "Working…";

    /// <summary>Lines left out of a tool output or diff; <c>{0}</c> is the count.</summary>
    public string MoreLines { get; init; } = "+{0} lines";

    public string Interrupted { get; init; } = "Interrupted by the user";

    public string Compacted { get; init; } = "Conversation compacted";

    public string ApiRetry { get; init; } = "Retrying the API request…";

    public string TurnFailed { get; init; } = "The turn ended with an error";

    /// <summary>
    /// The line under a finished turn: <c>{0}</c> duration, <c>{1}</c> input tokens, <c>{2}</c> output tokens,
    /// <c>{3}</c> cost in dollars. Parts are separated by " · "; a part whose value is unknown is left out.
    /// </summary>
    public string Footer { get; init; } = "{0} · {1} in · {2} out · ${3}";

    /// <summary>Shown next to a tool call the user said no to.</summary>
    public string ToolRejected { get; init; } = "Rejected";

    /// <summary>The empty reply box.</summary>
    public string InputPlaceholder { get; init; } = "Reply to Claude…";

    /// <summary>The empty reply box while Claude works: what is sent now waits in line.</summary>
    public string InputPlaceholderBusy { get; init; } = "Queue another message…";

    /// <summary>The send button's name (it shows an arrow).</summary>
    public string Send { get; init; } = "Send";

    /// <summary>The name of the button that interrupts Claude while it works (it shows a square).</summary>
    public string Stop { get; init; } = "Stop";

    /// <summary>
    /// The time over a message, in local time, as a .NET custom format read with the invariant culture
    /// (<c>h:mm tt</c>: "10:27 PM").
    /// </summary>
    public string TimeFormat { get; init; } = "h:mm tt";

    /// <summary>Under an edit that kept the line count.</summary>
    public string EditModified { get; init; } = "Modified";

    /// <summary>Under an edit that added lines; <c>{0}</c> is <see cref="Lines"/> or <see cref="OneLine"/>.</summary>
    public string EditAdded { get; init; } = "Added {0}";

    /// <summary>Under an edit that removed lines; <c>{0}</c> is <see cref="Lines"/> or <see cref="OneLine"/>.</summary>
    public string EditRemoved { get; init; } = "Removed {0}";

    /// <summary>Under an edit that added (<c>{0}</c>) and removed (<c>{1}</c>) lines.</summary>
    public string EditAddedRemoved { get; init; } = "Added {0}, removed {1}";

    /// <summary>Under an edit that failed.</summary>
    public string EditFailed { get; init; } = "Edit failed";

    /// <summary>Under a write that failed.</summary>
    public string WriteFailed { get; init; } = "Write failed";

    /// <summary>A count of one line.</summary>
    public string OneLine { get; init; } = "1 line";

    /// <summary>A count of lines (also under a write); <c>{0}</c> is the count.</summary>
    public string Lines { get; init; } = "{0} lines";

    /// <summary>Next to a Read of some lines; <c>{0}</c> the first, <c>{1}</c> the last.</summary>
    public string ReadLines { get; init; } = "(lines {0}-{1})";

    /// <summary>Next to a Read from a line to the end; <c>{0}</c> the first.</summary>
    public string ReadFromLine { get; init; } = "(from line {0})";

    /// <summary>The label of a command in the box under a tool call.</summary>
    public string ToolInput { get; init; } = "IN";

    /// <summary>The label of the output in the box under a tool call.</summary>
    public string ToolOutput { get; init; } = "OUT";

    /// <summary>The <c>plan</c> permission mode.</summary>
    public string ModePlan { get; init; } = "Plan";

    /// <summary>The <c>default</c> permission mode (the extension calls it "Manual").</summary>
    public string ModeDefault { get; init; } = "Manual";

    /// <summary>The <c>acceptEdits</c> permission mode.</summary>
    public string ModeAcceptEdits { get; init; } = "Edit automatically";

    /// <summary>The <c>auto</c> permission mode.</summary>
    public string ModeAuto { get; init; } = "Auto";

    /// <summary>Title of a permission request for Bash or PowerShell.</summary>
    public string PermissionRunCommand { get; init; } = "Claude wants to run this command:";

    /// <summary>Title of a permission request for Edit, Write, MultiEdit or NotebookEdit.</summary>
    public string PermissionEditFile { get; init; } = "Claude wants to edit this file:";

    /// <summary>Title of a permission request for any other tool; <c>{0}</c> is the tool's display name.</summary>
    public string PermissionUseTool { get; init; } = "Claude wants to use {0}:";

    public string PermissionYes { get; init; } = "Yes";

    /// <summary>Option 2 when every rule it saves lasts only for this conversation (destination <c>session</c>).</summary>
    public string PermissionYesAlways { get; init; } = "Yes, and don't ask again this session";

    /// <summary>
    /// Option 2 when a rule it saves outlives the conversation (kept in the settings, such as <c>localSettings</c>):
    /// it says nothing about how long.
    /// </summary>
    public string PermissionYesAlwaysBeyondSession { get; init; } = "Yes, and don't ask again";

    public string PermissionNo { get; init; } = "No, and tell Claude what to do differently";

    /// <summary>The empty field that opens with <see cref="PermissionNo"/>.</summary>
    public string DenyPlaceholder { get; init; } = "Tell Claude what to do instead";

    /// <summary>Requests waiting behind the one shown; <c>{0}</c> is the count.</summary>
    public string MorePending { get; init; } = "+{0} waiting";

    public string ThousandsSeparator { get; init; } = ",";

    public string DecimalSeparator { get; init; } = ".";

    /// <summary>Text for a <see cref="NoticeItem"/>.</summary>
    internal string Notice(NoticeKind kind) => kind switch
    {
        NoticeKind.Interrupted => Interrupted,
        NoticeKind.Compacted => Compacted,
        NoticeKind.ApiRetry => ApiRetry,
        NoticeKind.TurnFailed => TurnFailed,
        _ => "",
    };

    /// <summary>A whole number with <see cref="ThousandsSeparator"/>: 38557 → "38,557".</summary>
    internal string FormatNumber(long value)
    {
        var digits = value < 0
            ? (-(decimal)value).ToString(CultureInfo.InvariantCulture)
            : value.ToString(CultureInfo.InvariantCulture);
        var text = new StringBuilder(digits.Length + digits.Length / 3 + 1);
        if (value < 0) text.Append('-');
        var head = digits.Length % 3;
        if (head == 0) head = 3;
        text.Append(digits, 0, head);
        for (var i = head; i < digits.Length; i += 3)
            text.Append(ThousandsSeparator).Append(digits, i, 3);
        return text.ToString();
    }

    /// <summary>Below 60 s, seconds with one decimal ("4.2 s"); from 60 s on, "m:ss".</summary>
    internal string FormatDuration(long milliseconds)
    {
        if (milliseconds < 0) milliseconds = 0;
        var tenths = (long)Math.Round(milliseconds / 100.0, MidpointRounding.AwayFromZero);
        if (tenths < 600) return FormatNumber(tenths / 10) + DecimalSeparator + (tenths % 10) + " s";
        var seconds = (long)Math.Round(milliseconds / 1000.0, MidpointRounding.AwayFromZero);
        return (seconds / 60).ToString(CultureInfo.InvariantCulture) + ":" +
               (seconds % 60).ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>A number with two decimals: 0.0839 → "0.08".</summary>
    internal string FormatMoney(double value)
    {
        var cents = (long)Math.Round(Math.Abs(value) * 100, MidpointRounding.AwayFromZero);
        var sign = value < 0 && cents > 0 ? "-" : "";
        return sign + FormatNumber(cents / 100) + DecimalSeparator + (cents % 100).ToString("00", CultureInfo.InvariantCulture);
    }

    internal string FormatMoreLines(int lines) => MoreLines.Replace("{0}", FormatNumber(lines));

    /// <summary>A time in local time, with <see cref="TimeFormat"/>; a format .NET refuses gives "HH:mm".</summary>
    internal string FormatTime(DateTimeOffset time)
    {
        var local = time.ToLocalTime();
        try
        {
            return local.ToString(TimeFormat, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return local.ToString("HH:mm", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>"Thought for 3s": the seconds rounded like the extension (half up).</summary>
    internal string FormatThoughtFor(long milliseconds)
    {
        var seconds = (long)Math.Floor(Math.Max(0, milliseconds) / 1000.0 + 0.5);
        return ThoughtFor.Replace("{0}", seconds.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>"47 lines", "1 line".</summary>
    internal string FormatLines(int lines) => lines == 1 ? OneLine : Lines.Replace("{0}", FormatNumber(lines));

    /// <summary>The line under an edit: "Modified", "Added 2 lines", "Added 2 lines, removed 1 line"…</summary>
    internal string FormatEditChange(LineChange change)
    {
        if (change.Added > 0 && change.Removed > 0)
            return EditAddedRemoved.Replace("{0}", FormatLines(change.Added)).Replace("{1}", FormatLines(change.Removed));
        if (change.Added > 0) return EditAdded.Replace("{0}", FormatLines(change.Added));
        if (change.Removed > 0) return EditRemoved.Replace("{0}", FormatLines(change.Removed));
        return EditModified;
    }

    /// <summary>"(lines 2-61)" or "(from line 2)"; line numbers go without separators, like in an editor.</summary>
    internal string FormatReadRange(LineRange range) => range.To is int to
        ? ReadLines.Replace("{0}", Plain(range.From)).Replace("{1}", Plain(to))
        : ReadFromLine.Replace("{0}", Plain(range.From));

    private static string Plain(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>The label of a permission mode; a mode without a label keeps its name.</summary>
    internal string ModeLabel(string mode) => mode switch
    {
        "plan" => ModePlan,
        "default" => ModeDefault,
        "acceptEdits" => ModeAcceptEdits,
        "auto" => ModeAuto,
        _ => mode ?? "",
    };

    internal string FormatUseTool(string toolName) => PermissionUseTool.Replace("{0}", toolName ?? "");

    internal string FormatMorePending(int count) => MorePending.Replace("{0}", FormatNumber(count));

    /// <summary><see cref="Footer"/> with what the turn reported; empty when it reported nothing.</summary>
    internal string FormatFooter(TurnFooterItem footer)
    {
        if (footer == null) throw new ArgumentNullException(nameof(footer));
        var values = new[]
        {
            footer.DurationMs is long ms ? FormatDuration(ms) : null,
            footer.InputTokens is long input ? FormatNumber(input) : null,
            footer.OutputTokens is long output ? FormatNumber(output) : null,
            footer.CostUsd is double cost ? FormatMoney(cost) : null,
        };
        if (Array.TrueForAll(values, v => v == null)) return "";

        var kept = new List<string>();
        foreach (var part in Footer.Split(new[] { Separator }, StringSplitOptions.None))
        {
            var text = part;
            var known = true;
            for (var i = 0; i < values.Length; i++)
            {
                var placeholder = "{" + i + "}";
                if (text.IndexOf(placeholder, StringComparison.Ordinal) < 0) continue;
                if (values[i] == null)
                {
                    known = false;
                    break;
                }
                text = text.Replace(placeholder, values[i]);
            }
            if (known) kept.Add(text);
        }
        return string.Join(Separator, kept);
    }
}
