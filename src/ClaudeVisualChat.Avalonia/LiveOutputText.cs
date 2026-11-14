using System.Text;
using ClaudeVisualChat.Protocol;

namespace ClaudeVisualChat;

/// <summary>
/// What the claude says a running command has printed so far, made ready to show (<see cref="ToolCallItem.LiveOutput"/>): a cut start
/// and the terminal codes go, each line keeps what a terminal would leave of it, and a long text keeps its end.
/// </summary>
internal static class LiveOutputText
{
    /// <summary>The most characters the text keeps.</summary>
    internal const int MaxChars = 100_000;

    /// <summary>
    /// The text of <paramref name="output"/>. When the claude kept only its end (<paramref name="truncated"/>), its first line is a piece:
    /// it goes up to its line break, or, when that break is the text's last character or there is none, only the broken characters
    /// (U+FFFD) of its start go. Then the terminal codes go; each line loses its final carriage return and keeps what follows its last
    /// other one (a progress bar drawn over itself); and over <see cref="MaxChars"/> characters, the text keeps its end, from the first
    /// line break within its last <see cref="MaxChars"/> characters.
    /// </summary>
    internal static string Prepare(string output, bool truncated)
    {
        var text = output ?? "";
        if (truncated)
        {
            var feed = text.IndexOf('\n');
            text = feed >= 0 && feed < text.Length - 1 ? text.Substring(feed + 1) : text.TrimStart('�');
        }
        text = AnsiText.Strip(text);
        if (text.IndexOf('\r') >= 0) text = Overwritten(text);
        if (text.Length > MaxChars)
        {
            var start = text.Length - MaxChars;
            var feed = text.IndexOf('\n', start);
            text = feed >= 0 ? text.Substring(feed + 1) : text.Substring(start);
        }
        return text;
    }

    /// <summary>Each line without its final <c>\r</c>, and from what follows its last other <c>\r</c>.</summary>
    private static string Overwritten(string text)
    {
        var result = new StringBuilder(text.Length);
        var start = 0;
        while (true)
        {
            var feed = text.IndexOf('\n', start);
            var end = feed < 0 ? text.Length : feed;
            var last = end;
            if (last > start && text[last - 1] == '\r') last--;
            var from = last > start ? text.LastIndexOf('\r', last - 1, last - start) : -1;
            var lineStart = from >= 0 ? from + 1 : start;
            result.Append(text, lineStart, last - lineStart);
            if (feed < 0) break;
            result.Append('\n');
            start = feed + 1;
        }
        return result.ToString();
    }
}
