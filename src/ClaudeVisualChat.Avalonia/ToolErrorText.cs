using System;
using System.Text.RegularExpressions;

namespace ClaudeVisualChat;

/// <summary>
/// The text of a failed or refused tool call, made ready to show whole: without the notes and wrappers the claude puts around it,
/// without the plain color codes of a command's output, with each carriage return as a line break and what cannot be seen shown as a
/// symbol (<see cref="CommandOutputText.Symbols"/>), and cut at <see cref="MaxChars"/>.
/// </summary>
internal static class ToolErrorText
{
    /// <summary>What a refusal's standard sentence starts with.</summary>
    internal const string RefusalPrefix = "The user doesn't want to proceed";

    /// <summary>What comes between the standard sentence and the text the user typed.</summary>
    internal const string RefusalReasonLead = "The user provided the following reason for the rejection: ";

    /// <summary>The most UTF-16 units the text keeps; past this it ends with an ellipsis.</summary>
    internal const int MaxChars = 2000;

    private const string CorrectionNote = "Note: The user's next message may contain a correction";
    private const string RetryGuidance = "IMPORTANT: You *may* attempt to accomplish this action";
    private const string WrapperOpen = "<tool_use_error>";
    private const string WrapperClose = "</tool_use_error>";
    private static readonly Regex AnyTag = new Regex("</?[A-Za-z][^>]*>", RegexOptions.CultureInvariant);

    /// <summary>
    /// What the user typed when they refused a call, when <paramref name="text"/> is the claude's refusal sentence: the text after
    /// <see cref="RefusalReasonLead"/>, or null when there is none (the sentence alone, or the lead with nothing after it).
    /// </summary>
    internal static string? TypedReasonOf(string text)
    {
        var lead = text.IndexOf(RefusalReasonLead, StringComparison.Ordinal);
        if (lead < 0) return null;
        var typed = text.Substring(lead + RefusalReasonLead.Length).Trim();
        return typed.Length == 0 ? null : typed;
    }

    /// <summary>
    /// The text to show for a failed or refused call of <paramref name="toolName"/>, or null when the text is only one of the claude's
    /// standard refusal sentences (there is nothing to say then). Empty when nothing is left of it.
    /// </summary>
    public static string? Clean(string toolName, string text)
    {
        text ??= "";
        var note = text.LastIndexOf(CorrectionNote, StringComparison.Ordinal);
        if (note >= 0) text = text.Substring(0, note);

        if (text.TrimStart().StartsWith(RefusalPrefix, StringComparison.Ordinal))
        {
            var typed = TypedReasonOf(text);
            if (typed == null) return null;
            text = typed; // the prefix goes: what follows is what the user typed
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith(WrapperOpen, StringComparison.Ordinal) && trimmed.EndsWith(WrapperClose, StringComparison.Ordinal)
            && trimmed.Length >= WrapperOpen.Length + WrapperClose.Length)
        {
            var inner = trimmed.Substring(WrapperOpen.Length, trimmed.Length - WrapperOpen.Length - WrapperClose.Length);
            if (!AnyTag.IsMatch(inner)) text = inner;
        }

        var guidance = text.IndexOf(RetryGuidance, StringComparison.Ordinal);
        if (guidance >= 0) text = text.Substring(0, guidance);

        if (toolName == "Bash" || toolName == "PowerShell" || toolName == "TaskOutput") text = CommandOutputText.WithoutSimpleColors(text);
        text = CommandOutputText.Symbols(text.Replace("\r\n", "\n").Replace('\r', '\n'), keepCarriageReturn: false).Trim();

        var kept = CommandOutputText.CutOutsideSymbols(text, MaxChars);
        return kept.Length < text.Length ? kept + "…" : text;
    }
}
