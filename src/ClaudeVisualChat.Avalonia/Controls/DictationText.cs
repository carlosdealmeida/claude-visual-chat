using System;

namespace ClaudeVisualChat.Controls;

/// <summary>Where the words of a dictation go in the reply box's text.</summary>
internal static class DictationText
{
    /// <summary>
    /// Puts <paramref name="spoken"/>, trimmed, into <paramref name="text"/> at <paramref name="caret"/> (a caret outside the text
    /// is taken to its start or its end), with one space between the words and what is next to them, unless a space (or a line
    /// break) is there already or the text starts or ends there. The caret that comes back is at the end of the words: the space
    /// after them does not count. Nothing spoken (null, empty, blanks) gives back <paramref name="text"/> and <paramref name="caret"/>
    /// as they came.
    /// </summary>
    internal static (string Text, int Caret) Insert(string text, int caret, string? spoken)
    {
        var words = spoken?.Trim();
        if (string.IsNullOrEmpty(words)) return (text, caret);
        var at = Math.Max(0, Math.Min(caret, text.Length));
        var before = at > 0 && !char.IsWhiteSpace(text[at - 1]) ? " " : "";
        var after = at < text.Length && !char.IsWhiteSpace(text[at]) ? " " : "";
        var inserted = before + words;
        return (text.Substring(0, at) + inserted + after + text.Substring(at), at + inserted.Length);
    }
}
