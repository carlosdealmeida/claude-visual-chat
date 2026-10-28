using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ClaudeVisualChat.Protocol
{
    /// <summary>One labelled part of a tagged message; <see cref="Lines"/> is set when the part is a list of its own (the usage of a task).</summary>
    internal sealed class TaggedSection
    {
        public TaggedSection(string? label, string text)
        {
            Label = label;
            Text = text;
        }

        /// <summary>A key (<c>task-id</c>, <c>usage</c>, <c>output</c>…) that the strings give a text to; null for a part with no name.</summary>
        public string? Label { get; }

        public string Text { get; }

        public IReadOnlyList<TaggedSection>? Lines { get; init; }
    }

    /// <summary>
    /// A user text that is exactly one tagged block, as the claude writes them when something other than the user speaks: a message from
    /// an agent (<c>agent-message</c>), from another session (<c>cross-session-message</c>), from a channel (<c>channel</c>), the notice of a
    /// task (<c>task-notification</c>), or a wrapped notice (<c>teammate-message</c>, <c>bash-stdout</c>…). Text with a second block, or
    /// with anything else around the block, is not one: it is the user's.
    ///
    /// <para>
    /// The reader is a small scanner of tags written for this component. A <b>message block</b> is, in this order: an optional line that
    /// introduces it; an opening tag alone on its line, with the attributes its sort allows; the message; the closing tag alone on its line;
    /// and, optionally, a blank line and one line of note for the claude. A <b>task notice</b> is a <c>task-notification</c> tag holding one
    /// tag per fact, each at most once, and then, maybe, a text. A <b>wrapped notice</b> is one known tag around the whole text.
    /// </para>
    /// </summary>
    internal sealed class TaggedMessage
    {
        public const string AgentKind = "agent";
        public const string CrossSessionKind = "cross-session";
        public const string ChannelKind = "channel";
        public const string TaskKind = "task-notification";

        private const string TaskTag = "task-notification";
        private const int PreviewLength = 512;
        private const int HandleLength = 20;
        private const int NameLength = 64;

        // the tag of each sort of message block, and the attributes it may carry, in the order they are written
        private static readonly string[] AgentAttributes = { "from" };
        private static readonly string[] CrossSessionAttributes = { "from", "from-session", "hop-chain", "from-name", "from-mode" };

        private static readonly string[] WrappedTags =
        {
            "teammate-message", "bash-stdout", "bash-stderr", "tick", "mcp-resource-update", "mcp-polling-update", "local-command-caveat",
        };

        // the facts of a task notice, and the ones whose text is one plain line
        private static readonly HashSet<string> TaskFacts = new HashSet<string>(StringComparer.Ordinal)
        {
            "task-id", "tool-use-id", "task-type", "output-file", "status", "summary", "note", "event", "result", "usage", "worktree",
        };

        private static readonly HashSet<string> PlainFacts = new HashSet<string>(StringComparer.Ordinal)
        {
            "task-id", "tool-use-id", "task-type", "output-file", "status",
        };

        private static readonly HashSet<string> UsageFacts = new HashSet<string>(StringComparer.Ordinal) { "subagent_tokens", "tool_uses", "duration_ms" };

        private static readonly HashSet<string> WorktreeFacts = new HashSet<string>(StringComparer.Ordinal) { "worktreePath", "worktreeBranch" };

        /// <summary><see cref="AgentKind"/>, <see cref="CrossSessionKind"/>, <see cref="ChannelKind"/>, <see cref="TaskKind"/>, or the tag of a wrapped notice.</summary>
        public string Kind { get; private init; } = "";

        /// <summary>The sender the tag names (<c>from</c>, <c>from-name</c>, <c>source</c>), with its entities decoded; null when it names none.</summary>
        public string? FromName { get; private init; }

        /// <summary>The message; for the notice of a task, its summary.</summary>
        public string Body { get; private init; } = "";

        /// <summary>The attributes of the opening tag, in the order written.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Attributes { get; private init; } = Array.Empty<KeyValuePair<string, string>>();

        /// <summary>
        /// Who the note after the block says wrote it: <c>none</c> (no note), <c>peer</c> (another session or a channel) or <c>descendant</c>
        /// (an agent of this same session).
        /// </summary>
        public string Tail { get; private init; } = "none";

        /// <summary>The parts of the message, for a card that does not know its sender and for the notice of a task.</summary>
        public IReadOnlyList<TaggedSection> Sections { get; private init; } = Array.Empty<TaggedSection>();

        /// <summary>The <see cref="Sections"/> are a list of names and values.</summary>
        public bool Columns { get; private init; }

        /// <summary>A wrapped notice: its tag says what it is, and nobody needs to be named.</summary>
        public bool IsWrappedNotice => Array.IndexOf(WrappedTags, Kind) >= 0;

        /// <summary>The message the text is, or null when it is not exactly one tagged block.</summary>
        public static TaggedMessage? Read(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('<') < 0) return null; // every block holds a tag
            return ReadTaskNotice(text) ?? ReadMessageBlock(text) ?? ReadWrappedNotice(text);
        }

        // ---- tags ----

        /// <summary>An opening tag: its name, its attributes, and the index after its <c>&gt;</c>.</summary>
        private sealed class Tag
        {
            public Tag(string name, List<KeyValuePair<string, string>> attributes, int end)
            {
                Name = name;
                Attributes = attributes;
                End = end;
            }

            public string Name { get; }

            public List<KeyValuePair<string, string>> Attributes { get; }

            public int End { get; }
        }

        /// <summary>
        /// The opening tag that starts at <paramref name="at"/>: <c>&lt;name&gt;</c>, or the name and then, each after a blank,
        /// <c>key="value"</c>. A value holds no quote, angle bracket or line break. Null when the text there is no such tag.
        /// </summary>
        private static Tag? OpenTag(string s, int at)
        {
            if (at >= s.Length || s[at] != '<') return null;
            var i = at + 1;
            var nameStart = i;
            while (i < s.Length && (char.IsLetter(s[i]) || s[i] == '-' || s[i] == '_')) i++;
            if (i == nameStart) return null;
            var name = s.Substring(nameStart, i - nameStart);
            var attributes = new List<KeyValuePair<string, string>>();
            while (i < s.Length)
            {
                if (s[i] == '>') return new Tag(name, attributes, i + 1);
                if (s[i] != ' ' && s[i] != '\t') return null;
                while (i < s.Length && (s[i] == ' ' || s[i] == '\t')) i++;
                if (i < s.Length && s[i] == '>') continue;
                var keyStart = i;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '-' || s[i] == '_')) i++;
                if (i == keyStart || i + 1 >= s.Length || s[i] != '=' || s[i + 1] != '"') return null;
                var key = s.Substring(keyStart, i - keyStart);
                i += 2;
                var valueStart = i;
                while (i < s.Length && s[i] != '"')
                {
                    if (s[i] == '<' || s[i] == '>' || s[i] == '\n' || s[i] == '\r') return null;
                    i++;
                }
                if (i >= s.Length) return null;
                attributes.Add(new KeyValuePair<string, string>(key, s.Substring(valueStart, i - valueStart)));
                i++;
            }
            return null;
        }

        /// <summary>One <c>&lt;name&gt;inside&lt;/name&gt;</c> with no attributes at <paramref name="at"/>; <paramref name="next"/> is the index after it.</summary>
        private static bool Element(string s, int at, out string name, out string inside, out int next)
        {
            name = inside = "";
            next = at;
            var tag = OpenTag(s, at);
            if (tag == null || tag.Attributes.Count > 0) return false;
            var close = "</" + tag.Name + ">";
            var closeAt = s.IndexOf(close, tag.End, StringComparison.Ordinal);
            if (closeAt < 0) return false;
            name = tag.Name;
            inside = s.Substring(tag.End, closeAt - tag.End);
            next = closeAt + close.Length;
            return true;
        }

        private readonly struct Piece
        {
            public Piece(string? name, string text)
            {
                Name = name;
                Text = text;
            }

            public string? Name { get; }

            public string Text { get; }
        }

        /// <summary>
        /// What is in a run of tags: the loose text between them (no name) and each tag with its inside. Null when a tag is not one of
        /// <paramref name="allowed"/>, comes twice, or is never closed.
        /// </summary>
        private static List<Piece>? Pieces(string text, HashSet<string> allowed)
        {
            var pieces = new List<Piece>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var at = 0;
            while (at < text.Length)
            {
                var lt = text.IndexOf('<', at);
                var loose = text.Substring(at, (lt < 0 ? text.Length : lt) - at).Trim();
                if (loose.Length > 0) pieces.Add(new Piece(null, loose));
                if (lt < 0) break;
                if (!Element(text, lt, out var name, out var inside, out var next) || !allowed.Contains(name) || !seen.Add(name)) return null;
                pieces.Add(new Piece(name, inside));
                at = next;
            }
            return pieces;
        }

        // ---- the notice of a task ----

        private static TaggedMessage? ReadTaskNotice(string text)
        {
            var s = text.TrimStart();
            var open = "<" + TaskTag + ">";
            var close = "</" + TaskTag + ">";
            if (!s.StartsWith(open, StringComparison.Ordinal)) return null;
            var closeAt = s.IndexOf(close, StringComparison.Ordinal);
            if (closeAt < 0 || s.IndexOf(close, closeAt + close.Length, StringComparison.Ordinal) >= 0) return null;
            var inside = s.Substring(open.Length, closeAt - open.Length);
            var after = s.Substring(closeAt + close.Length).Trim();

            var summary = SummaryOf(inside);
            if (summary == null) return null; // a notice with nothing to say is no message
            var parts = TaskParts(inside, after);
            return new TaggedMessage
            {
                Kind = TaskKind,
                Body = summary,
                Sections = parts ?? (IReadOnlyList<TaggedSection>)new[] { new TaggedSection(null, Clean(text)) }, // what no list can hold is shown as it came
                Columns = parts != null,
            };
        }

        /// <summary>The summary, if the notice has exactly one that holds no tag and says something.</summary>
        private static string? SummaryOf(string inside)
        {
            const string open = "<summary>";
            const string close = "</summary>";
            var start = inside.IndexOf(open, StringComparison.Ordinal);
            var end = inside.IndexOf(close, StringComparison.Ordinal);
            if (start < 0 || end < start + open.Length) return null;
            if (inside.IndexOf(open, start + 1, StringComparison.Ordinal) >= 0 || inside.IndexOf(close, end + 1, StringComparison.Ordinal) >= 0) return null;
            var text = inside.Substring(start + open.Length, end - start - open.Length);
            if (text.IndexOf('<') >= 0) return null;
            var summary = Clean(Decode(text)).Trim();
            return summary.Length == 0 ? null : summary;
        }

        /// <summary>The facts of the notice as names and values; null when something in it cannot be shown so.</summary>
        private static List<TaggedSection>? TaskParts(string inside, string after)
        {
            var pieces = Pieces(inside, TaskFacts);
            if (pieces == null) return null;
            var sections = new List<TaggedSection>();
            foreach (var piece in pieces)
            {
                if (piece.Name == null)
                {
                    sections.Add(new TaggedSection("other-text", Clean(piece.Text)));
                    continue;
                }
                if (piece.Name == "usage" || piece.Name == "worktree")
                {
                    var lines = SubFacts(piece.Name, piece.Text);
                    if (lines == null) return null;
                    sections.Add(new TaggedSection(piece.Name, string.Join("\n", lines.ConvertAll(l => l.Label + ": " + l.Text))) { Lines = lines });
                    continue;
                }
                if (piece.Text.IndexOf('<') >= 0) return null;
                var plain = PlainFacts.Contains(piece.Name);
                if (plain && HasLineBreak(piece.Text)) return null;
                sections.Add(new TaggedSection(piece.Name, Clean(plain ? piece.Text : Decode(piece.Text)).Trim()));
            }
            if (after.Length > 0) sections.Add(new TaggedSection("after-notification", Clean(after)));
            return sections;
        }

        /// <summary>The facts inside a usage (numbers) or a worktree (one-line texts); null at anything else.</summary>
        private static List<TaggedSection>? SubFacts(string owner, string text)
        {
            var pieces = Pieces(text, owner == "usage" ? UsageFacts : WorktreeFacts);
            if (pieces == null) return null;
            var lines = new List<TaggedSection>();
            foreach (var piece in pieces)
            {
                if (piece.Name == null || piece.Text.IndexOf('<') >= 0 || HasLineBreak(piece.Text)) return null;
                if (owner == "usage" && !IsNumber(piece.Text)) return null;
                lines.Add(new TaggedSection(piece.Name, Clean(piece.Text)));
            }
            return lines;
        }

        private static bool IsNumber(string text)
        {
            if (text.Length == 0) return false;
            foreach (var c in text)
            {
                if (c < '0' || c > '9') return false;
            }
            return true;
        }

        private static bool HasLineBreak(string text) => text.IndexOf('\n') >= 0 || text.IndexOf('\r') >= 0;

        // ---- a message from an agent, another session or a channel ----

        private static TaggedMessage? ReadMessageBlock(string text)
        {
            // the line that introduces the block, if the text does not start with the tag itself
            string? lead = null;
            var s = text;
            if (text[0] != '<')
            {
                var lineEnd = text.IndexOf('\n');
                if (lineEnd <= 0) return null;
                lead = text.Substring(0, lineEnd);
                s = text.Substring(lineEnd + 1);
            }
            var tag = OpenTag(s, 0);
            if (tag == null || tag.End >= s.Length || s[tag.End] != '\n') return null;
            string kind;
            switch (tag.Name)
            {
                case "agent-message": kind = AgentKind; break;
                case "cross-session-message": kind = CrossSessionKind; break;
                case "channel": kind = ChannelKind; break;
                default: return null;
            }

            // the closing tag alone on its line, and nowhere else
            var close = "</" + tag.Name + ">";
            var closeAt = s.IndexOf("\n" + close, tag.End, StringComparison.Ordinal);
            if (closeAt < 0 || s.IndexOf(close, StringComparison.Ordinal) != closeAt + 1) return null;
            if (s.IndexOf(close, closeAt + 1 + close.Length, StringComparison.Ordinal) >= 0) return null;
            var note = s.Substring(closeAt + 1 + close.Length);
            var tail = TailOf(kind, note);
            if (tail == null) return null;

            var sender = SenderOf(kind, tag.Attributes);
            if (sender == null) return null;
            if (kind == ChannelKind)
            {
                // a channel's introduction and its note come together, and the introduction names the channel the tag names
                if ((lead != null) != (tail == "peer")) return null;
                if (lead != null && !IntroducesChannel(lead, sender)) return null;
            }
            else if (lead != null && !IntroducesSession(lead))
            {
                return null;
            }

            var body = closeAt > tag.End ? s.Substring(tag.End + 1, closeAt - tag.End - 1) : "";
            var sections = new List<TaggedSection>();
            if (lead != null) sections.Add(new TaggedSection(null, Clean(lead.Trim())));
            sections.Add(new TaggedSection(null, Clean(body)));
            if (note.Trim().Length > 0) sections.Add(new TaggedSection(null, Clean(note.Trim())));
            return new TaggedMessage
            {
                Kind = kind,
                FromName = sender.Length == 0 ? null : sender,
                Body = Clean(body),
                Attributes = tag.Attributes,
                Tail = tail,
                Sections = sections,
            };
        }

        /// <summary>
        /// The sender the attributes name (empty when they name none), or null when the attributes are not the ones that sort of message
        /// carries: an agent's has <c>from</c>; another session's has some of <c>from</c>, <c>from-session</c>, <c>hop-chain</c>,
        /// <c>from-name</c> and <c>from-mode</c>, in that order; a channel's starts with <c>source</c> and may carry more.
        /// </summary>
        private static string? SenderOf(string kind, List<KeyValuePair<string, string>> attributes)
        {
            if (kind == ChannelKind)
                return attributes.Count > 0 && attributes[0].Key == "source" ? Decode(attributes[0].Value, true) : null;
            var allowed = kind == AgentKind ? AgentAttributes : CrossSessionAttributes;
            var position = -1;
            string? name = null;
            foreach (var attribute in attributes)
            {
                var index = Array.IndexOf(allowed, attribute.Key);
                if (index <= position) return null; // not allowed here, repeated, or out of order
                position = index;
                if (attribute.Key == "from-mode" && attribute.Value != "bypass" && attribute.Value != "prompting") return null;
                if (attribute.Key == "from-name")
                {
                    if (attribute.Value.Length == 0 || Shorten(attribute.Value) != attribute.Value) return null;
                    name = attribute.Value;
                }
                else if (kind == AgentKind)
                {
                    name = Decode(attribute.Value, true);
                }
            }
            if (kind == AgentKind && attributes.Count == 0) return null;
            return name ?? "";
        }

        /// <summary>The line before a channel's block: it names the channel, and may add that the message came while the claude worked.</summary>
        private static bool IntroducesChannel(string line, string source)
        {
            var head = "A message arrived from " + source;
            return line == head + ":" || line == head + " while you were working:";
        }

        /// <summary>The line before another session's block says a session sent a message.</summary>
        private static bool IntroducesSession(string line) =>
            line.EndsWith(":", StringComparison.Ordinal)
            && (line.StartsWith("Another Claude session sent a message", StringComparison.Ordinal)
                || line.StartsWith("A peer session sent a message", StringComparison.Ordinal));

        /// <summary>
        /// What follows the block: nothing (<c>none</c>), or a blank line and one line the claude adds for itself, which says the message is not
        /// the user's: from another session or channel (<c>peer</c>), or from an agent of this same session (<c>descendant</c>). Null when it
        /// is anything else.
        /// </summary>
        private static string? TailOf(string kind, string after)
        {
            if (after.Length == 0) return "none";
            if (!after.StartsWith("\n\n", StringComparison.Ordinal) || after.IndexOf('\n', 2) >= 0) return null;
            var line = after.Substring(2);
            if (kind == ChannelKind) return line.Contains("external channel") ? "peer" : null;
            if (line.Contains("inside this same session")) return "descendant";
            return line.Contains("Claude session") ? "peer" : null;
        }

        // ---- a wrapped notice ----

        private static TaggedMessage? ReadWrappedNotice(string text)
        {
            var s = text.TrimEnd();
            var tag = OpenTag(s, 0);
            if (tag == null || Array.IndexOf(WrappedTags, tag.Name) < 0) return null;
            var close = "</" + tag.Name + ">";
            var closeAt = s.IndexOf(close, tag.End, StringComparison.Ordinal);
            if (closeAt < 0 || s.IndexOf(close, closeAt + close.Length, StringComparison.Ordinal) >= 0) return null;
            var inside = s.Substring(tag.End, closeAt - tag.End);
            var sections = new List<TaggedSection>();
            if (tag.Name == "bash-stdout")
            {
                // a command's output may be followed by its error output and its exit code, in that order, and nothing else
                sections.Add(new TaggedSection("output", TrimBreaks(Clean(Output(inside)))));
                var at = closeAt + close.Length;
                var expected = new[] { "bash-stderr", "bash-exit-code" };
                var labels = new[] { "error-output", "exit-code" };
                var slot = 0;
                while (at < s.Length)
                {
                    if (!Element(s, at, out var name, out var value, out var next) || value.IndexOf('<') >= 0) return null;
                    while (slot < expected.Length && expected[slot] != name) slot++;
                    if (slot == expected.Length) return null;
                    sections.Add(new TaggedSection(labels[slot], Clean(name == "bash-stderr" ? Decode(value) : value)));
                    slot++;
                    at = next;
                }
            }
            else
            {
                if (closeAt + close.Length != s.Length) return null; // the tag wraps the whole text
                sections.Add(new TaggedSection(null, TrimBreaks(Clean(tag.Name == "bash-stderr" ? Output(inside) : inside))));
            }
            return new TaggedMessage
            {
                Kind = tag.Name,
                Body = sections[0].Text,
                Attributes = tag.Attributes,
                Sections = sections,
            };
        }

        /// <summary>The text without the line breaks at its ends: they are the tag's, not the text's.</summary>
        private static string TrimBreaks(string text) => text.Trim('\n');

        /// <summary>The output of a command: the inside of a persisted output when that is all of it, else the text with its entities decoded.</summary>
        private static string Output(string text)
        {
            const string open = "<persisted-output>";
            const string close = "</persisted-output>";
            var trimmed = text.TrimEnd();
            if (trimmed.StartsWith(open, StringComparison.Ordinal) && trimmed.EndsWith(close, StringComparison.Ordinal)
                && trimmed.Length >= open.Length + close.Length)
                return trimmed.Substring(open.Length, trimmed.Length - open.Length - close.Length);
            return Decode(text);
        }

        // ---- names and previews ----

        private static bool IsLetterOrNumber(UnicodeCategory category)
        {
            switch (category)
            {
                case UnicodeCategory.UppercaseLetter:
                case UnicodeCategory.LowercaseLetter:
                case UnicodeCategory.TitlecaseLetter:
                case UnicodeCategory.ModifierLetter:
                case UnicodeCategory.OtherLetter:
                case UnicodeCategory.DecimalDigitNumber:
                case UnicodeCategory.LetterNumber:
                case UnicodeCategory.OtherNumber:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The short name of a sender: control and invisible characters go, the rest is cut at 64 characters, every run of characters that are
        /// no letter, digit, dot, dash or underscore becomes one "-", dots, dashes and underscores at the ends go, and what is left is cut
        /// at 20 characters with "…". Empty when it holds no letter or digit.
        /// </summary>
        public static string Handle(string? name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var sb = new StringBuilder();
            var gap = false;
            var hasLetter = false;
            var shortened = Shorten(name!);
            for (var i = 0; i < shortened.Length; i++)
            {
                var length = char.IsHighSurrogate(shortened[i]) && i + 1 < shortened.Length && char.IsLowSurrogate(shortened[i + 1]) ? 2 : 1;
                var part = shortened.Substring(i, length);
                var isWord = IsLetterOrNumber(CharUnicodeInfo.GetUnicodeCategory(shortened, i));
                if (isWord || part == "." || part == "_" || part == "-")
                {
                    if (gap && sb.Length > 0) sb.Append('-');
                    gap = false;
                    sb.Append(part);
                    hasLetter |= isWord;
                }
                else
                {
                    gap = true;
                }
                i += length - 1;
            }
            if (!hasLetter) return "";
            var handle = sb.ToString().Trim('.', '_', '-');
            return handle.Length <= HandleLength ? handle : CutAt(handle, HandleLength) + "…";
        }

        /// <summary>The name of a sender in full: without control and format characters, trimmed.</summary>
        public static string PlainName(string? name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var sb = new StringBuilder(name!.Length);
            foreach (var c in name)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (category == UnicodeCategory.Control || category == UnicodeCategory.Format || category == UnicodeCategory.LineSeparator
                    || category == UnicodeCategory.ParagraphSeparator)
                    continue;
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        /// <summary>The name without its control and format characters, trimmed, and at most 64 characters (a longer one is cut and ends in "…").</summary>
        private static string Shorten(string name)
        {
            var trimmed = PlainName(name);
            return trimmed.Length <= NameLength ? trimmed : CutAt(trimmed, NameLength) + "…";
        }

        /// <summary>The first line of the text that has any, white space collapsed, up to 512 characters and then "…".</summary>
        public static string Preview(string body)
        {
            if (string.IsNullOrEmpty(body)) return "";
            var at = 0;
            while (at <= body.Length)
            {
                var lineEnd = body.IndexOf('\n', at);
                var end = lineEnd < 0 ? body.Length : lineEnd;
                var tooLong = end - at > PreviewLength * 4; // a line this long is cut before it is cleaned
                var line = Collapse(Invisible(AnsiText.Strip(body.Substring(at, tooLong ? PreviewLength * 4 : end - at))));
                if (line.Length > 0 || tooLong)
                    return tooLong || line.Length > PreviewLength ? CutAt(line, PreviewLength) + "…" : line;
                if (lineEnd < 0) break;
                at = lineEnd + 1;
            }
            return "";
        }

        /// <summary>Control and invisible characters as spaces, so that two words they separated stay two.</summary>
        private static string Invisible(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (var c in text)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(c);
                var hidden = (category == UnicodeCategory.Control && !char.IsWhiteSpace(c)) || category == UnicodeCategory.Format
                             || category == UnicodeCategory.LineSeparator || category == UnicodeCategory.ParagraphSeparator;
                sb.Append(hidden && !IsJoiner(c) ? ' ' : c);
            }
            return sb.ToString();
        }

        /// <summary>The zero-width joiner and non-joiner, which emoji and some scripts need to be drawn right.</summary>
        private static bool IsJoiner(char c) => c == (char)0x200C || c == (char)0x200D;

        private static string Collapse(string text)
        {
            var sb = new StringBuilder(text.Length);
            var space = false;
            foreach (var c in text)
            {
                if (char.IsWhiteSpace(c))
                {
                    space = sb.Length > 0;
                    continue;
                }
                if (space) sb.Append(' ');
                space = false;
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>The text up to <paramref name="length"/> characters, never between the two halves of a surrogate pair.</summary>
        private static string CutAt(string text, int length)
        {
            if (text.Length <= length) return text;
            if (char.IsHighSurrogate(text[length - 1])) length--;
            return text.Substring(0, length);
        }

        // ---- text ----

        /// <summary>
        /// The text as it is drawn: without terminal escapes, control characters (the line break and the tab stay), unpaired surrogates, and
        /// format characters (the joiners stay). Cleaning a text twice gives what cleaning it once did.
        /// </summary>
        public static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            text = AnsiText.Strip(text);
            var sb = new StringBuilder(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    sb.Append(c).Append(text[i + 1]);
                    i++;
                    continue;
                }
                if (char.IsSurrogate(c)) continue;
                var category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (category == UnicodeCategory.Control && c != '\n' && c != '\t') continue;
                if ((category == UnicodeCategory.Format && !IsJoiner(c)) || category == UnicodeCategory.LineSeparator
                    || category == UnicodeCategory.ParagraphSeparator)
                    continue;
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>The text with <c>&amp;amp;</c>, <c>&amp;lt;</c> and <c>&amp;gt;</c> (and, with <paramref name="quotes"/>, <c>&amp;quot;</c> and <c>&amp;apos;</c>) turned back into what they stand for.</summary>
        private static string Decode(string text, bool quotes = false)
        {
            if (text.IndexOf('&') < 0) return text;
            var sb = new StringBuilder(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '&')
                {
                    var semicolon = text.IndexOf(';', i);
                    if (semicolon > i && semicolon - i <= 5)
                    {
                        string? replacement = null;
                        switch (text.Substring(i + 1, semicolon - i - 1))
                        {
                            case "amp": replacement = "&"; break;
                            case "lt": replacement = "<"; break;
                            case "gt": replacement = ">"; break;
                            case "quot" when quotes: replacement = "\""; break;
                            case "apos" when quotes: replacement = "'"; break;
                        }
                        if (replacement != null)
                        {
                            sb.Append(replacement);
                            i = semicolon;
                            continue;
                        }
                    }
                }
                sb.Append(text[i]);
            }
            return sb.ToString();
        }
    }
}
