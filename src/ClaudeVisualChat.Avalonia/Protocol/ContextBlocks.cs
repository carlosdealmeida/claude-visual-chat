using System;

namespace ClaudeVisualChat.Protocol
{
    /// <summary>
    /// One context block the IDE places before a typed prompt. <paramref name="Subject"/> is what the
    /// block names (a terminal name, host and path, <c>file#A-B</c>, or empty); <paramref name="Content"/>
    /// is what the line shows when opened.
    /// </summary>
    internal sealed record ContextBlock(string Kind, string Subject, string Content);

    /// <summary>
    /// Recognizes the four kinds of text item the IDE sends ahead of a prompt: terminal output
    /// (<c>&lt;terminal name="..."&gt;</c>), a browser tab (<c>&lt;browser tabGroupId="..." tabId="..."&gt;</c>),
    /// browser instructions (<c>&lt;browser_instruction&gt;</c>) and an editor selection
    /// (<c>&lt;ide_selection&gt;</c>). Each pass is a single left-to-right scan with no backtracking, so the
    /// cost grows linearly with the text. A text that does not fit its kind exactly is not a block.
    /// </summary>
    internal static class ContextBlockReader
    {
        public const string Terminal = "terminal";
        public const string BrowserTab = "browser-tab";
        public const string BrowserInstructions = "browser-instructions";
        public const string Selection = "selection";

        private const int MaxName = 4096;
        private const int MaxAddress = 2048;
        private const int MaxGroupId = 256;
        private const int MaxTabId = 16;
        private const int MaxLineNumber = 10;

        private const string TerminalOpen = "<terminal name=\"";
        private const string TerminalClose = "</terminal>";
        private const string TabOpen = "<browser tabGroupId=\"";
        private const string TabMiddle = "\" tabId=\"";
        private const string TabClose = "</browser>";
        private const string InstructionOpen = "<browser_instruction>";
        private const string InstructionClose = "</browser_instruction>";
        private const string SelectionOpen = "<ide_selection>The user selected ";
        private const string SelectionClose = "</ide_selection>";

        // The three emoji flag sequences that carry hidden tag characters but are legitimate.
        private static readonly string[] AllowedFlags = { Flag("gbeng"), Flag("gbsct"), Flag("gbwls") };

        public static ContextBlock? Read(string? text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var end = text!.Length;
            while (end > 0 && char.IsWhiteSpace(text[end - 1])) end--;
            if (end == 0) return null;
            var s = end == text.Length ? text : text.Substring(0, end);

            if (s.StartsWith(TerminalOpen, StringComparison.Ordinal)) return ReadTerminal(s);
            if (s.StartsWith(TabOpen, StringComparison.Ordinal)) return ReadTab(s);
            if (s.StartsWith(InstructionOpen, StringComparison.Ordinal)) return ReadInstructions(s);
            if (s.StartsWith(SelectionOpen, StringComparison.Ordinal)) return ReadSelection(s);
            return null;
        }

        // ---- terminal ----

        private static ContextBlock? ReadTerminal(string s)
        {
            if (!s.EndsWith(TerminalClose, StringComparison.Ordinal)) return null;
            var nameStart = TerminalOpen.Length;
            var quote = s.IndexOf('"', nameStart);
            if (quote <= nameStart || quote + 1 >= s.Length || s[quote + 1] != '>') return null;
            var bodyStart = quote + 2;
            var bodyEnd = s.Length - TerminalClose.Length;
            if (bodyStart > bodyEnd) return null;

            var name = s.Substring(nameStart, quote - nameStart);
            if (!IsSafe(name, MaxName, TerminalClose)) return null;

            if (bodyStart < bodyEnd && s[bodyStart] == '\n') bodyStart++;
            if (bodyEnd > bodyStart && s[bodyEnd - 1] == '\n') bodyEnd--;
            return new ContextBlock(Terminal, name, s.Substring(bodyStart, bodyEnd - bodyStart));
        }

        // ---- browser tab ----

        private static ContextBlock? ReadTab(string s)
        {
            if (!s.EndsWith(TabClose, StringComparison.Ordinal)) return null;
            var pos = TabOpen.Length;

            var idEnd = s.IndexOf('"', pos);
            if (idEnd < 0) return null;
            var groupId = s.Substring(pos, idEnd - pos);
            if (groupId.Length == 0 || groupId.Length > MaxGroupId) return null;
            foreach (var ch in groupId) if (ch == ':' || char.IsWhiteSpace(ch)) return null;
            if (!IsSafe(groupId, MaxGroupId, TabClose)) return null;

            if (string.CompareOrdinal(s, idEnd, TabMiddle, 0, TabMiddle.Length) != 0) return null;
            pos = idEnd + TabMiddle.Length;

            var digitsEnd = pos;
            while (digitsEnd < s.Length && s[digitsEnd] >= '0' && s[digitsEnd] <= '9') digitsEnd++;
            var digits = digitsEnd - pos;
            if (digits == 0 || digits > MaxTabId) return null;
            if (digitsEnd + 1 >= s.Length || s[digitsEnd] != '"' || s[digitsEnd + 1] != '>') return null;

            var addressStart = digitsEnd + 2;
            var addressEnd = s.Length - TabClose.Length;
            if (addressStart > addressEnd) return null;
            var address = s.Substring(addressStart, addressEnd - addressStart);
            if (address.Length > 0)
            {
                foreach (var ch in address) if (char.IsWhiteSpace(ch)) return null;
                if (!IsSafe(address, MaxAddress, TabClose)) return null;
            }
            return new ContextBlock(BrowserTab, TabSubject(address), address);
        }

        private static string TabSubject(string address)
        {
            if (address.Length == 0) return "";
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)) return address;
            // Only web addresses name a host; anything else (javascript:, data:, file:) gets the bare label.
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return "";
            var host = uri.Authority;
            if (host.Length == 0) return address;
            var path = uri.AbsolutePath;
            return path == "/" || path.Length == 0 ? host : host + path;
        }

        // ---- browser instructions ----

        private static ContextBlock? ReadInstructions(string s)
        {
            if (s.Length < InstructionOpen.Length + InstructionClose.Length) return null;
            if (!s.EndsWith(InstructionClose, StringComparison.Ordinal)) return null;
            var start = InstructionOpen.Length;
            return new ContextBlock(BrowserInstructions, "", s.Substring(start, s.Length - InstructionClose.Length - start));
        }

        // ---- IDE selection ----

        private static ContextBlock? ReadSelection(string s)
        {
            if (!s.EndsWith(SelectionClose, StringComparison.Ordinal)) return null;
            var start = SelectionOpen.Length;
            var end = s.Length - SelectionClose.Length;
            if (start > end) return null;

            var lineEnd = s.IndexOf('\n', start, end - start);
            var headerEnd = lineEnd < 0 ? end : lineEnd;
            var content = lineEnd < 0 ? "" : s.Substring(lineEnd + 1, end - lineEnd - 1);
            if (headerEnd > start && s[headerEnd - 1] == '\r') headerEnd--;
            if (headerEnd <= start || s[headerEnd - 1] != ':') return null;
            headerEnd--;

            string? first = null, last = null;
            var pos = start;
            if (string.CompareOrdinal(s, pos, "the lines ", 0, 10) == 0)
            {
                pos += 10;
                first = Digits(s, ref pos, headerEnd);
                if (first == null || string.CompareOrdinal(s, pos, " to ", 0, 4) != 0) return null;
                pos += 4;
                last = Digits(s, ref pos, headerEnd);
                if (last == null) return null;
                if (string.CompareOrdinal(s, pos, " from ", 0, 6) != 0) return null;
                pos += 6;
            }
            else if (string.CompareOrdinal(s, pos, "from ", 0, 5) == 0)
            {
                pos += 5;
            }
            else return null;

            if (pos > headerEnd) return null;
            var path = s.Substring(pos, headerEnd - pos);
            if (!IsSafe(path, MaxName, SelectionClose)) return null;

            var subject = FileName(path);
            if (first != null) subject += "#" + first + "-" + last;
            return new ContextBlock(Selection, subject, content);
        }

        private static string? Digits(string s, ref int pos, int limit)
        {
            var begin = pos;
            while (pos < limit && s[pos] >= '0' && s[pos] <= '9') pos++;
            var n = pos - begin;
            return n == 0 || n > MaxLineNumber ? null : s.Substring(begin, n);
        }

        private static string FileName(string path)
        {
            if (path.Length >= 2 && IsSlash(path[0]) && IsSlash(path[1])) return path;
            var drive = path.Length >= 3 && path[1] == ':' && IsSlash(path[2])
                && ((path[0] >= 'a' && path[0] <= 'z') || (path[0] >= 'A' && path[0] <= 'Z'));
            var cut = drive ? path.LastIndexOfAny(new[] { '/', '\\' }) : path.LastIndexOf('/');
            var name = cut < 0 ? path : path.Substring(cut + 1);
            return name.Length == 0 ? path : name;
        }

        private static bool IsSlash(char c) => c == '/' || c == '\\';

        // ---- shared safety rule for anything that reaches a label ----

        private static bool IsSafe(string value, int max, string closing)
        {
            if (value.Length == 0 || value.Length > max) return false;
            if (value.IndexOf(closing, StringComparison.Ordinal) >= 0) return false;
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1])) return false;
                    if (c == '\uDB40') return false;             // hidden "tag" characters, U+E0000..U+E007F
                    if (c == '\uD83C' && value[i + 1] == '\uDFF4')
                    {
                        var flag = AllowedFlagAt(value, i);
                        if (flag > 0) { i += flag - 1; continue; }
                    }
                    i++;
                    continue;
                }
                if (char.IsLowSurrogate(c)) return false;
                if (char.IsControl(c)) return false;
                if (c == (char)0x2028 || c == (char)0x2029) return false;
                if ((c >= (char)0x202A && c <= (char)0x202E) || (c >= (char)0x2066 && c <= (char)0x2069)) return false;
            }
            return true;
        }

        private static int AllowedFlagAt(string value, int index)
        {
            foreach (var flag in AllowedFlags)
                if (string.CompareOrdinal(value, index, flag, 0, flag.Length) == 0) return flag.Length;
            return 0;
        }

        private static string Flag(string letters)
        {
            var sb = new System.Text.StringBuilder().Append((char)0xD83C).Append((char)0xDFF4);
            foreach (var ch in letters) sb.Append('\uDB40').Append((char)(0xDC00 + ch));
            return sb.Append('\uDB40').Append('\uDC7F').ToString();
        }
    }
}
