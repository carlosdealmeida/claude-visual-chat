using System;
using System.Text.RegularExpressions;

namespace ClaudeVisualChat.Protocol
{
    /// <summary>
    /// One of the text blocks the IDE puts before the person's own text in a message: the output of a terminal, a browser tab, the
    /// browser's instructions, or the code selected in the editor. <paramref name="Subject"/> is what the line names (a terminal's
    /// name, a tab's host and path, a file and its lines; empty when there is none) and <paramref name="Content"/> what it holds.
    /// </summary>
    internal sealed record ContextBlock(string Kind, string Subject, string Content);

    /// <summary>
    /// Tells the context blocks apart, by the formats measured in the Claude Code extension 2.1.312's webview (they are the same ones the
    /// claude receives; described here in our own words):
    /// <list type="bullet">
    /// <item>terminal: the opening tag <c>terminal name="NAME"</c>, a line feed, the output, a line feed, and the closing tag; the line is named by NAME;</item>
    /// <item>browser tab: the whole block is a <c>browser</c> tag with a <c>tabGroupId</c> (up to 256 characters, no quote or space) and a
    /// <c>tabId</c> (up to 16 digits) around a URL with no space in it; named by the host and the path of the URL (the path is left out
    /// when it is just "/"), or by the text itself when it is not a URL, and by nothing when it is empty;</item>
    /// <item>browser instructions: a <c>browser_instruction</c> tag around the text;</item>
    /// <item>selection: an <c>ide_selection</c> tag around "The user selected [the lines A to B ]from PATH:", a line feed, and the code;
    /// named by the file name of PATH (the whole path when it is a network share), plus <c>#A-B</c> when the lines are told.</item>
    /// </list>
    /// A name or path of more than 4,096 characters, or with a lone surrogate, a control character, or the closing tag in it, makes the block
    /// no context. The "opened file" block is not read here: its chip is not part of this release.
    /// </summary>
    internal static class ContextBlockReader
    {
        public const string Terminal = "terminal";
        public const string BrowserTab = "browser-tab";
        public const string BrowserInstructions = "browser-instructions";
        public const string Selection = "selection";

        private const int MaxName = 4096;
        private const int MaxGroup = 256;
        private const int MaxUrl = 2048;
        private const string InstructionOpen = "<browser_instruction>";
        private const string InstructionClose = "</browser_instruction>";
        private const string SelectionOpen = "<ide_selection>";

        private static readonly Regex TerminalStart = new Regex("^<terminal name=\"([^\"]+)\">", RegexOptions.Compiled);
        private static readonly Regex BrowserTag = new Regex(
            "^<browser tabGroupId=\"([^\":\\s]+)\" tabId=\"(\\d{1,16})\">(\\S*)</browser>\\s*$", RegexOptions.Compiled);
        private static readonly Regex SelectionStart = new Regex(
            "^<ide_selection>The user selected (?:the lines (\\d+) to (\\d+) )?from ", RegexOptions.Compiled);

        /// <summary>The block <paramref name="text"/> is, or null when it is not one.</summary>
        public static ContextBlock? Read(string? text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var trimmed = text!.TrimEnd();
            if (trimmed.StartsWith("<terminal name=\"", StringComparison.Ordinal)) return ReadTerminal(trimmed);
            if (trimmed.StartsWith("<browser tabGroupId=\"", StringComparison.Ordinal)) return ReadBrowser(trimmed);
            if (trimmed.StartsWith(InstructionOpen, StringComparison.Ordinal)
                && trimmed.EndsWith(InstructionClose, StringComparison.Ordinal)
                && trimmed.Length >= InstructionOpen.Length + InstructionClose.Length)
                return new ContextBlock(BrowserInstructions, "",
                    trimmed.Substring(InstructionOpen.Length, trimmed.Length - InstructionOpen.Length - InstructionClose.Length));
            if (trimmed.StartsWith(SelectionOpen, StringComparison.Ordinal)) return ReadSelection(trimmed);
            return null;
        }

        private static ContextBlock? ReadTerminal(string text)
        {
            const string close = "</terminal>";
            var start = TerminalStart.Match(text);
            if (!start.Success || !text.EndsWith(close, StringComparison.Ordinal) || text.Length < start.Length + close.Length) return null;
            var name = start.Groups[1].Value;
            if (!IsSafe(name, close)) return null;
            var content = text.Substring(start.Length, text.Length - start.Length - close.Length);
            if (content.StartsWith("\n", StringComparison.Ordinal)) content = content.Substring(1);
            if (content.EndsWith("\n", StringComparison.Ordinal)) content = content.Substring(0, content.Length - 1);
            return new ContextBlock(Terminal, name, content);
        }

        private static ContextBlock? ReadBrowser(string text)
        {
            const string close = "</browser>";
            var match = BrowserTag.Match(text);
            if (!match.Success) return null;
            var group = match.Groups[1].Value;
            var url = match.Groups[3].Value.Trim();
            if (group.Length > MaxGroup || !IsSafe(group, close)) return null;
            if (url.Length > 0 && (url.Length > MaxUrl || !IsSafe(url, close))) return null;
            var subject = url;
            if (Uri.TryCreate(url, UriKind.Absolute, out var parsed) && parsed.Host.Length > 0)
                subject = parsed.Host + (parsed.AbsolutePath != "/" ? parsed.AbsolutePath : "");
            return new ContextBlock(BrowserTab, subject, url);
        }

        private static ContextBlock? ReadSelection(string text)
        {
            const string close = "</ide_selection>";
            if (!text.EndsWith(close, StringComparison.Ordinal)) return null;
            var start = SelectionStart.Match(text);
            if (!start.Success) return null;
            var lineEnd = text.IndexOf('\n', start.Length);
            var pathEnd = lineEnd < 0 ? text.Length - close.Length : lineEnd;
            // the line of the path ends with a colon
            if (pathEnd <= start.Length + 1 || text[pathEnd - 1] != ':') return null;
            var path = text.Substring(start.Length, pathEnd - 1 - start.Length);
            if (!IsSafe(path, close)) return null;
            var subject = FileName(path);
            if (start.Groups[1].Success) subject += "#" + start.Groups[1].Value + "-" + start.Groups[2].Value;
            return new ContextBlock(Selection, subject, text.Substring(SelectionOpen.Length, text.Length - SelectionOpen.Length - close.Length));
        }

        /// <summary>The last part of a path (split at either slash for a drive path, at "/" only for any other); a network share is kept whole.</summary>
        private static string FileName(string path)
        {
            if (path.StartsWith("\\\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal)) return path;
            var isDrive = path.Length > 2 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/');
            var cut = isDrive ? path.LastIndexOfAny(new[] { '\\', '/' }) : path.LastIndexOf('/');
            var name = cut < 0 ? path : path.Substring(cut + 1);
            return name.Length > 0 ? name : path;
        }

        private static bool IsSafe(string value, string closing)
        {
            if (value.Length == 0 || value.Length > MaxName || value.Contains(closing)) return false;
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (char.IsControl(c)) return false;
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1])) return false;
                    i++;
                }
                else if (char.IsLowSurrogate(c)) return false;
            }
            return true;
        }
    }
}
