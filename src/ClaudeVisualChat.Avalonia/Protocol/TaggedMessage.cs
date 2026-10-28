using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

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
    /// </summary>
    internal sealed class TaggedMessage
    {
        public const string AgentKind = "agent";
        public const string CrossSessionKind = "cross-session";
        public const string ChannelKind = "channel";
        public const string TaskKind = "task-notification";

        private const string TaskTag = "task-notification";
        private const string ArrivedLead = "A message arrived from ";
        private const int PreviewLength = 512;
        private const int HandleLength = 20;
        private const int NameLength = 64;

        private static readonly string[] PeerLeads =
        {
            "Another Claude session sent a message:\n",
            "Another Claude session sent a message while you were working:\n",
            "A peer session sent a message while you were working:\n",
        };

        // The notes the claude appends after a message from a peer: told by how they begin, since the rest is the claude's wording.
        private static readonly string[] PeerNotes =
        {
            "This came from another Claude session",
            "IMPORTANT: This is NOT from your user — it came from a different Claude session",
            "This is from another Claude session, not your user.",
        };

        private const string DescendantNote = "That \"other Claude session\" is an agent working inside this same session";
        private const string ChannelNote = "IMPORTANT: This is NOT from your user — it came from an external channel";

        private static readonly string[] BlockTags = { "agent-message", "cross-session-message", "channel" };

        private static readonly string[] WrappedTags =
        {
            "teammate-message", "bash-stdout", "bash-stderr", "tick", "mcp-resource-update", "mcp-polling-update", "local-command-caveat",
        };

        private static readonly Regex WrappedOpen = new Regex(
            "^<(teammate-message|bash-stdout|bash-stderr|tick|mcp-resource-update|mcp-polling-update|local-command-caveat)" +
            "((?:[ \\t][^>\\r\\n\\v\\f\\u0085\\u2028\\u2029]*)?)>", RegexOptions.Compiled);

        private static readonly Regex BashTail = new Regex(
            "</bash-stdout>(?:<bash-stderr>([^<]*)</bash-stderr>)?(?:<bash-exit-code>([^<]*)</bash-exit-code>)?$", RegexOptions.Compiled);

        private static readonly Regex AgentAttributes = new Regex("^ from=\"([^\"<>]*)\"$", RegexOptions.Compiled);

        private static readonly Regex ChannelAttributes = new Regex(
            "^ source=\"([^\"<>]*)\"((?: [a-zA-Z_][a-zA-Z0-9_]*=\"[^\"<>]*\")*)$", RegexOptions.Compiled);

        private static readonly Regex CrossSessionAttributes = new Regex(
            "^(?: from=\"[A-Za-z0-9%:_/.\\\\-]{1,300}\")?(?: from-session=\"[A-Za-z0-9_-]{1,80}\")?(?: hop-chain=\"[0-9a-f]{24}(?:,[0-9a-f]{24}){0,31}\")?" +
            "(?: from-name=\"([^\"<>]+)\")?(?: from-mode=\"(?:bypass|prompting)\")?$", RegexOptions.Compiled);

        private static readonly Regex Attribute = new Regex("([a-zA-Z_][a-zA-Z0-9_-]*)=\"([^\"]*)\"", RegexOptions.Compiled);

        private static readonly Regex SummaryTag = new Regex("<summary>([\\s\\S]*?)</summary>", RegexOptions.Compiled);

        private static readonly Regex TagName = new Regex("\\G<([A-Za-z_-]+)>", RegexOptions.Compiled);

        private static readonly Regex Entities = new Regex("&(?:amp|lt|gt);", RegexOptions.Compiled);

        private static readonly Regex AllEntities = new Regex("&(?:amp|lt|gt|quot|apos);", RegexOptions.Compiled);

        private static readonly HashSet<string> TaskTags = new HashSet<string>(StringComparer.Ordinal)
        {
            "task-id", "tool-use-id", "task-type", "output-file", "status", "summary", "note", "event", "result", "usage", "worktree",
        };

        // the tags whose text is one plain line, kept as it came
        private static readonly HashSet<string> PlainLine = new HashSet<string>(StringComparer.Ordinal)
        {
            "task-id", "tool-use-id", "task-type", "output-file", "status", "worktreePath", "subagent_tokens", "tool_uses", "duration_ms",
        };

        private static readonly HashSet<string> UsageTags = new HashSet<string>(StringComparer.Ordinal) { "subagent_tokens", "tool_uses", "duration_ms" };

        private static readonly HashSet<string> WorktreeTags = new HashSet<string>(StringComparer.Ordinal) { "worktreePath", "worktreeBranch" };

        /// <summary><see cref="AgentKind"/>, <see cref="CrossSessionKind"/>, <see cref="ChannelKind"/>, <see cref="TaskKind"/>, or the tag of a wrapped notice.</summary>
        public string Kind { get; private init; } = "";

        /// <summary>The sender the tag names (<c>from</c>, <c>from-name</c>, <c>source</c>), with its entities decoded; null when it names none.</summary>
        public string? FromName { get; private init; }

        /// <summary>The message; for the notice of a task, its summary.</summary>
        public string Body { get; private init; } = "";

        /// <summary>The attributes of the tag as written, trimmed.</summary>
        public string Attrs { get; private init; } = "";

        /// <summary>The attributes of the tag, in order.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Attributes { get; private init; } = Array.Empty<KeyValuePair<string, string>>();

        /// <summary>For a channel message: the attributes after its source; else null.</summary>
        public string? Meta { get; private init; }

        /// <summary>The line before the block ("Another Claude session sent a message:\n"), or empty.</summary>
        public string Lead { get; private init; } = "";

        /// <summary>What follows the block (the claude's note to itself), or empty.</summary>
        public string Trailer { get; private init; } = "";

        /// <summary><c>none</c> (nothing follows the block), <c>peer</c> (the note for a message from another session) or <c>descendant</c> (from an agent of this session).</summary>
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
            if (string.IsNullOrEmpty(text) || text.IndexOf('<') < 0) return null; // every block opens with a tag
            return ReadTask(text) ?? ReadBlock(text) ?? ReadWrapped(text);
        }

        // ---- the notice of a task ----

        private static TaggedMessage? ReadTask(string text)
        {
            var j = text.TrimStart();
            if (!j.StartsWith("<" + TaskTag, StringComparison.Ordinal)) return null;
            var close = "</" + TaskTag + ">";
            var end = j.IndexOf(close, StringComparison.Ordinal);
            if (end < 0 || j.IndexOf(close, end + close.Length, StringComparison.Ordinal) >= 0) return null;
            var head = j.Substring(0, end);
            var summary = SummaryTag.Match(head);
            if (summary.Success && summary.Groups[1].Value.IndexOf('<') >= 0) return null;
            if (Count(head, "<summary>") > 1 || Count(head, "</summary>") > 1) return null;
            var body = summary.Success ? Clean(Unescape(summary.Groups[1].Value)).Trim() : "";
            if (body.Length == 0) return null; // a notice with no summary has nothing to say
            IReadOnlyList<TaggedSection>? parts = TaskSections(j, close);
            return new TaggedMessage
            {
                Kind = TaskKind,
                Body = body,
                Sections = parts ?? new[] { new TaggedSection(null, Clean(text)) }, // a list it cannot make is shown as it came
                Columns = parts != null,
            };
        }

        /// <summary>The names and values of the notice; null when it holds a tag, a repeat or a text the list cannot show.</summary>
        private static List<TaggedSection>? TaskSections(string text, string close)
        {
            var open = "<" + TaskTag + ">";
            if (!text.StartsWith(open, StringComparison.Ordinal) || Count(text, close) != 1) return null;
            var end = text.IndexOf(close, StringComparison.Ordinal);
            var pieces = Split(text.Substring(open.Length, end - open.Length), TaskTags);
            if (pieces == null) return null;
            var sections = new List<TaggedSection>();
            foreach (var piece in pieces)
            {
                if (piece.Name == null)
                {
                    sections.Add(new TaggedSection("other-text", Clean(piece.Text)));
                    continue;
                }
                var nested = piece.Name == "usage" ? UsageTags : piece.Name == "worktree" ? WorktreeTags : null;
                if (nested == null)
                {
                    if (piece.Text.IndexOf('<') >= 0 || (PlainLine.Contains(piece.Name) && !IsPlainLine(piece.Text))) return null;
                    sections.Add(new TaggedSection(piece.Name, Clean(Value(piece.Name, piece.Text)).Trim()));
                    continue;
                }
                var inner = Split(piece.Text, nested);
                if (inner == null) return null;
                var lines = new List<TaggedSection>();
                foreach (var line in inner)
                {
                    if (line.Name == null || line.Text.IndexOf('<') >= 0 || !IsPlainLine(line.Text)) return null;
                    if (piece.Name == "usage" && !IsDigits(line.Text)) return null;
                    lines.Add(new TaggedSection(line.Name, Clean(Value(line.Name, line.Text))));
                }
                var joined = new StringBuilder();
                foreach (var line in lines)
                {
                    if (joined.Length > 0) joined.Append('\n');
                    joined.Append(line.Label).Append(": ").Append(line.Text);
                }
                sections.Add(new TaggedSection(piece.Name, joined.ToString()) { Lines = lines });
            }
            var after = text.Substring(end + close.Length).Trim();
            if (after.Length > 0) sections.Add(new TaggedSection("after-notification", Clean(after)));
            return sections;
        }

        /// <summary>A plain line is kept as it came; any other text has its entities decoded.</summary>
        private static string Value(string name, string text) => PlainLine.Contains(name) ? text : Unescape(text);

        private static bool IsDigits(string text)
        {
            if (text.Length == 0) return false;
            foreach (var c in text)
            {
                if (c < '0' || c > '9') return false;
            }
            return true;
        }

        /// <summary>One line: no white space but plain spaces, and no run of two, in the text as it came and as it is shown.</summary>
        private static bool IsPlainLine(string text) => IsOneLine(text) && IsOneLine(Clean(text));

        private static bool IsOneLine(string text)
        {
            for (var i = 0; i < text.Length; i++)
            {
                if (char.IsWhiteSpace(text[i]) && text[i] != ' ') return false;
                if (text[i] == ' ' && i + 1 < text.Length && text[i + 1] == ' ') return false;
            }
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

        /// <summary>The text as pieces: the text between tags (no name) and each tag with its inside; null at a tag that is not allowed, or twice.</summary>
        private static List<Piece>? Split(string text, HashSet<string> allowed)
        {
            var pieces = new List<Piece>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var at = 0;
            while (at < text.Length)
            {
                var lt = text.IndexOf('<', at);
                var between = text.Substring(at, (lt < 0 ? text.Length : lt) - at).Trim();
                if (between.Length > 0) pieces.Add(new Piece(null, between));
                if (lt < 0) break;
                var m = TagName.Match(text, lt);
                if (!m.Success) return null;
                var name = m.Groups[1].Value;
                if (!allowed.Contains(name) || !seen.Add(name)) return null;
                var start = lt + name.Length + 2;
                var closing = text.IndexOf("</" + name + ">", start, StringComparison.Ordinal);
                if (closing < 0) return null;
                pieces.Add(new Piece(name, text.Substring(start, closing - start)));
                at = closing + name.Length + 3;
            }
            return pieces;
        }

        // ---- a message from an agent, another session or a channel ----

        private static TaggedMessage? ReadBlock(string text)
        {
            string? arrival = null;
            if (text.StartsWith(ArrivedLead, StringComparison.Ordinal))
            {
                var line = text.IndexOf('\n');
                if (line < 0) return null;
                arrival = text.Substring(0, line + 1);
            }
            var lead = arrival;
            if (lead == null)
            {
                foreach (var known in PeerLeads)
                {
                    if (text.StartsWith(known, StringComparison.Ordinal))
                    {
                        lead = known;
                        break;
                    }
                }
            }
            var rest = lead == null ? text : text.Substring(lead.Length);

            foreach (var tag in BlockTags)
            {
                var open = "<" + tag;
                if (!rest.StartsWith(open, StringComparison.Ordinal)) continue;
                var gt = rest.IndexOf('>', open.Length);
                if (gt < 0) continue;
                var attrs = rest.Substring(open.Length, gt - open.Length);
                if (!IsOpening(attrs, rest, gt)) continue; // "<channelx>" is no "<channel>"
                var kind = tag == "agent-message" ? AgentKind : tag == "channel" ? ChannelKind : CrossSessionKind;
                var isChannel = kind == ChannelKind;
                if ((arrival != null) != isChannel && lead != null) return null;

                var close = "\n</" + tag + ">";
                var closeAt = rest.IndexOf(close, StringComparison.Ordinal);
                if (closeAt < 0 || Count(rest, "</" + tag + ">") != 1) return null;
                var after = rest.Substring(closeAt + close.Length);
                var tail = TailOf(kind, after);
                if (tail == null) return null;

                string? from;
                string? meta = null;
                if (kind == AgentKind)
                {
                    var m = AgentAttributes.Match(attrs);
                    if (!m.Success) return null;
                    from = m.Groups[1].Value;
                }
                else if (isChannel)
                {
                    var m = ChannelAttributes.Match(attrs);
                    if (!m.Success) return null;
                    from = m.Groups[1].Value;
                    meta = m.Groups[2].Value.Trim();
                    if (meta.Length == 0) meta = null;
                    if (arrival != null && !IsArrivalOf(arrival, AllUnescape(from))) return null;
                    if ((arrival != null) != (tail == "peer")) return null;
                }
                else
                {
                    var m = CrossSessionAttributes.Match(attrs);
                    if (!m.Success) return null;
                    from = m.Groups[1].Success ? m.Groups[1].Value : null;
                    if (from != null && Shorten(from) != from) return null;
                }
                var named = string.IsNullOrEmpty(from) ? null : kind == CrossSessionKind ? from : AllUnescape(from!);
                var bodyAt = gt + 2; // the line break after the opening tag belongs to the tag
                var body = closeAt > bodyAt ? rest.Substring(bodyAt, closeAt - bodyAt) : "";
                return Build(kind, named, body, attrs.Trim(), meta, lead ?? "", after, tail);
            }
            return null;
        }

        /// <summary>The tag opens with its attributes after a space or a tab, or at once, and a line break follows it.</summary>
        private static bool IsOpening(string attrs, string rest, int gt)
        {
            if (gt + 1 >= rest.Length || rest[gt + 1] != '\n') return false;
            if (attrs.Length == 0) return true;
            if (attrs[0] != ' ' && attrs[0] != '\t') return false;
            foreach (var c in attrs)
            {
                if (c == '\r' || c == '\n' || c == '\v' || c == '\f' || c == (char)0x85 || c == (char)0x2028 || c == (char)0x2029) return false;
            }
            return true;
        }

        private static bool IsArrivalOf(string arrival, string source) =>
            arrival == ArrivedLead + source + ":\n" || arrival == ArrivedLead + source + " while you were working:\n";

        /// <summary>What follows the block says who sent it; null when it is text the claude does not write there.</summary>
        private static string? TailOf(string kind, string after)
        {
            if (after.Length == 0) return "none";
            if (!after.StartsWith("\n\n", StringComparison.Ordinal) || after.IndexOf('\n', 2) >= 0) return null;
            var note = after.Substring(2);
            if (kind == ChannelKind) return note.StartsWith(ChannelNote, StringComparison.Ordinal) ? "peer" : null;
            foreach (var known in PeerNotes)
            {
                if (note.StartsWith(known, StringComparison.Ordinal)) return "peer";
            }
            return note.StartsWith(DescendantNote, StringComparison.Ordinal) ? "descendant" : null;
        }

        private static TaggedMessage Build(
            string kind, string? from, string body, string attrs, string? meta, string lead, string trailer, string tail)
        {
            var sections = new List<TaggedSection>();
            if (lead.Trim().Length > 0) sections.Add(new TaggedSection(null, Clean(lead.Trim())));
            sections.Add(new TaggedSection(null, Clean(body)));
            if (trailer.Trim().Length > 0) sections.Add(new TaggedSection(null, Clean(trailer.Trim())));
            return new TaggedMessage
            {
                Kind = kind,
                FromName = from,
                Body = Clean(body),
                Attrs = attrs,
                Attributes = ReadAttributes(attrs),
                Meta = meta,
                Lead = lead,
                Trailer = trailer,
                Tail = tail,
                Sections = sections,
            };
        }

        private static IReadOnlyList<KeyValuePair<string, string>> ReadAttributes(string attrs)
        {
            var list = new List<KeyValuePair<string, string>>();
            foreach (Match m in Attribute.Matches(attrs)) list.Add(new KeyValuePair<string, string>(m.Groups[1].Value, m.Groups[2].Value));
            return list;
        }

        // ---- a wrapped notice ----

        private static TaggedMessage? ReadWrapped(string text)
        {
            var j = text.TrimEnd();
            var open = WrappedOpen.Match(j);
            if (!open.Success) return null;
            var tag = open.Groups[1].Value;
            var close = "</" + tag + ">";
            var bash = tag == "bash-stdout" ? BashTail.Match(j) : null;
            if (tag == "bash-stdout" ? !bash!.Success : !j.EndsWith(close, StringComparison.Ordinal)) return null;
            if (Count(j, close) != 1 || j.Length < open.Length + close.Length) return null;
            var attrs = open.Groups[2].Value.Trim();
            var inner = j.Substring(open.Length, (bash == null ? j.Length - close.Length : bash.Index) - open.Length);
            var sections = new List<TaggedSection>();
            if (bash == null)
            {
                sections.Add(new TaggedSection(null, TrimBreaks(Clean(tag == "bash-stderr" ? Output(inner) : inner))));
            }
            else
            {
                sections.Add(new TaggedSection("output", TrimBreaks(Clean(Output(inner)))));
                if (bash.Groups[1].Success) sections.Add(new TaggedSection("error-output", Clean(Unescape(bash.Groups[1].Value))));
                if (bash.Groups[2].Success) sections.Add(new TaggedSection("exit-code", Clean(bash.Groups[2].Value)));
            }
            return new TaggedMessage
            {
                Kind = tag,
                Body = sections[0].Text,
                Attrs = attrs,
                Attributes = ReadAttributes(attrs),
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
            return Unescape(text);
        }

        // ---- names and previews ----

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
                var tooLong = end - at > PreviewLength * 4;
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
                sb.Append(hidden && c != '‍' && c != '‌' ? ' ' : c);
            }
            return sb.ToString();
        }

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
        /// The text as it is drawn: without terminal escapes, C1 controls, carriage returns, vertical tabs and form feeds, unpaired surrogates, and
        /// format characters (except the joiners that emoji and some scripts need). A text that is cleaned once is not changed by cleaning it again.
        /// </summary>
        public static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            text = AnsiText.Strip(text);
            StringBuilder? sb = null;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                var drop = c == '\u001b' || c == '\r' || c == '\v' || c == '\f' || (c >= '\u0080' && c <= '\u009f');
                if (!drop && char.IsSurrogate(c))
                {
                    var paired = char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]);
                    if (paired)
                    {
                        sb?.Append(c).Append(text[i + 1]);
                        i++;
                        continue;
                    }
                    drop = true;
                }
                if (!drop && c != '‌' && c != '‍')
                {
                    var category = CharUnicodeInfo.GetUnicodeCategory(c);
                    drop = category == UnicodeCategory.Format || category == UnicodeCategory.LineSeparator || category == UnicodeCategory.ParagraphSeparator;
                }
                if (drop)
                {
                    sb ??= new StringBuilder(text, 0, i, text.Length);
                    continue;
                }
                sb?.Append(c);
            }
            return sb == null ? text : sb.ToString();
        }

        private static string Unescape(string text) =>
            Entities.Replace(text, m => m.Value == "&amp;" ? "&" : m.Value == "&lt;" ? "<" : ">");

        private static string AllUnescape(string text) =>
            AllEntities.Replace(text, m => m.Value == "&amp;" ? "&" : m.Value == "&lt;" ? "<" : m.Value == "&gt;" ? ">" : m.Value == "&quot;" ? "\"" : "'");

        private static int Count(string text, string part)
        {
            var count = 0;
            var at = 0;
            while ((at = text.IndexOf(part, at, StringComparison.Ordinal)) >= 0)
            {
                count++;
                at += part.Length;
            }
            return count;
        }
    }
}
