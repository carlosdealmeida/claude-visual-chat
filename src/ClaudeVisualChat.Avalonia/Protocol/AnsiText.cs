using System.Text.RegularExpressions;

namespace ClaudeVisualChat.Protocol
{
    /// <summary>Takes the terminal escape sequences out of text a command printed.</summary>
    public static class AnsiText
    {
        // In the order they are tried: a control sequence (ESC [, or the 8-bit form), an operating system command ended by
        // BEL or by a string terminator (ESC \, or the 8-bit form), the selection of a character set (ESC ( B), and any
        // other sequence of ESC and one character (ESC 7, ESC =, ESC M, ESC c). An ESC before "{" is no sequence, so a JSON
        // line that starts with one keeps its brace.
        private static readonly Regex Sequence = new Regex(
            "(?:\u001b\\[|\u009b)[0-?]*[ -/]*[@-~]"
            + "|(?:\u001b\\]|\u009d)[^\u0007\u001b\u009c]*(?:\u0007|\u001b\\\\|\u009c)"
            + "|\u001b[()*+][ -~]"
            + "|\u001b[0-z|}~]",
            RegexOptions.Compiled);

        /// <summary>The text without its escape sequences; text with none comes back as it is.</summary>
        public static string Strip(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (text.IndexOf('\u001b') < 0 && text.IndexOf('\u009b') < 0 && text.IndexOf('\u009d') < 0) return text;
            return Sequence.Replace(text, "");
        }

        /// <summary>The text without the escape sequences it starts with.</summary>
        internal static string TrimStart(string text)
        {
            while (text.Length > 0 && (text[0] == '\u001b' || text[0] == '\u009b' || text[0] == '\u009d'))
            {
                var m = Sequence.Match(text);
                if (!m.Success || m.Index != 0) return text;
                text = text.Substring(m.Length);
            }
            return text;
        }
    }
}
