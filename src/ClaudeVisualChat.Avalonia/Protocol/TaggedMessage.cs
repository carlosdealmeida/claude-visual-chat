using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ClaudeVisualChat.Protocol
{
    /// <summary>A part of a tagged message: its name (null for a part that has none) and its text; a part that is a list holds it in <see cref="Lines"/>.</summary>
    internal sealed class TaggedSection
    {
        public TaggedSection(string? label, string text)
        {
            Label = label;
            Text = text ?? "";
        }

        /// <summary>The name of the part (<c>task-id</c>, <c>output</c>…); null for a part with no name (a lead line, a body, a note).</summary>
        public string? Label { get; }

        /// <summary>The text of the part, entities decoded and line breaks kept.</summary>
        public string Text { get; }

        /// <summary>The names and values inside the part, when it is a list (the usage or the worktree of a task); null otherwise.</summary>
        public IReadOnlyList<TaggedSection>? Lines { get; init; }
    }

    /// <summary>
    /// A user text that is exactly one tagged block: a message from an agent of this session, from another Claude session or from a
    /// channel, the notice of a background task, or a wrapped notice (a teammate's message, a command's output…).
    /// </summary>
    /// <remarks>
    /// <para>How it reads. <see cref="LightMarkup"/> scans the text into elements (an open tag, its attributes, the raw text up to its close
    /// tag). The name of the first tag picks a <see cref="Shape"/> from <see cref="Shapes"/>, a table that says, for each block the claude
    /// writes, its kind, its family, the attributes it may have and must have, which attribute names the sender, the lines that may lead
    /// it and the notes that may follow it. One reader per family then checks what the table says and builds the message:</para>
    /// <list type="bullet">
    /// <item><b>message</b> (<c>agent-message</c>, <c>cross-session-message</c>, <c>channel</c>): the body sits on lines of its own and is
    /// kept raw (the claude does not escape it); a lead line may come before the tag, and then one of the shape's notes must follow, after
    /// a blank line;</item>
    /// <item><b>notice</b> (<c>teammate-message</c>, <c>tick</c>…): any attributes, any content, nothing around it;</item>
    /// <item><b>output</b> (<c>bash-stdout</c>, <c>bash-stderr</c>, <c>bash-exit-code</c>): one to three of them side by side, each once;</item>
    /// <item><b>task</b> (<c>task-notification</c>): parts named in <see cref="TaskParts"/>, its summary required; a part or a shape it does
    /// not know keeps the notice as raw text; its parts are decoded (the claude escapes them).</item>
    /// </list>
    /// <para>Every part the card shows goes through <see cref="Displayable"/>: no escape sequences, no controls, no
    /// <c>persisted-output</c> wrapper.</para>
    /// <para>Limits it sets itself: names of up to 64 characters and up to 32 attributes (<see cref="LightMarkup"/>), up to 64 pieces inside
    /// a task notice and 16 inside one of its lists, numbers in a usage of up to 19 digits.</para>
    /// </remarks>
    internal sealed class TaggedMessage
    {
        /// <summary>The kind of a message from an agent of this session (<c>&lt;agent-message&gt;</c>).</summary>
        public const string AgentKind = "agent";

        /// <summary>The kind of a message from another Claude session (<c>&lt;cross-session-message&gt;</c>).</summary>
        public const string CrossSessionKind = "cross-session";

        /// <summary>The kind of a message from a channel (<c>&lt;channel&gt;</c>).</summary>
        public const string ChannelKind = "channel";

        /// <summary>The kind of the notice of a background task (<c>&lt;task-notification&gt;</c>).</summary>
        public const string TaskKind = "task-notification";

        /// <summary><see cref="Tail"/> when nothing follows the block.</summary>
        public const string NoTail = "none";

        /// <summary><see cref="Tail"/> when the note that says the message is not from the user follows the block.</summary>
        public const string PeerTail = "peer";

        /// <summary><see cref="Tail"/> when the note that says the sender is an agent working inside this same session follows the block.</summary>
        public const string DescendantTail = "descendant";

        /// <summary>The notes that may follow a message, after a blank line: how each starts, and the <see cref="Tail"/> it gives.</summary>
        private static readonly KeyValuePair<string, string>[] Notes =
        {
            new KeyValuePair<string, string>("IMPORTANT: This is NOT from your user", PeerTail),
            new KeyValuePair<string, string>("That \"other Claude session\" is an agent working inside this same session", DescendantTail),
        };

        /// <summary>What a lead line may say before its colon, besides its stem: the message came while the claude was busy.</summary>
        private const string WhileWorking = " while you were working";

        /// <summary>The stems of the lead lines of a message from a peer (an agent of this session or another session).</summary>
        private static readonly string[] PeerLeads = { "Another Claude session sent a message", "A peer session sent a message" };

        /// <summary>A wrapper the claude puts around a long output it kept in a file; a part that is wholly inside one is shown without it.</summary>
        private const string PersistedOutputTag = "persisted-output";

        /// <summary>The section names of a task notice: what text follows it, and text inside it that is none of its parts.</summary>
        private const string AfterLabel = "after-notification";

        private const string OtherTextLabel = "other-text";

        /// <summary>The most pieces read inside a task notice, and inside one of its lists.</summary>
        private const int MaxTaskPieces = 64;

        private const int MaxFacts = 16;

        /// <summary>The longest number in the usage of a task (a long).</summary>
        private const int MaxDigits = 19;

        /// <summary>The longest short name of a sender; a longer one is cut and ends in "…".</summary>
        public const int HandleLength = 20;

        /// <summary>The longest full name of a sender kept; a longer one is cut and ends in "…".</summary>
        public const int PlainNameLength = 100;

        /// <summary>The longest preview; a longer one is cut and ends in "…".</summary>
        public const int PreviewLength = 512;

        private const string Ellipsis = "…";

        private TaggedMessage()
        {
        }

        /// <summary><see cref="AgentKind"/>, <see cref="CrossSessionKind"/>, <see cref="ChannelKind"/>, <see cref="TaskKind"/>, or the tag of a wrapped notice.</summary>
        public string Kind { get; private init; } = "";

        /// <summary>The sender the block names (the name, the session, the source of a channel), decoded; null when it names none.</summary>
        public string? FromName { get; private init; }

        /// <summary>The message itself, line breaks kept; for the notice of a task, its summary; for a wrapped notice, its first part.</summary>
        public string Body { get; private init; } = "";

        /// <summary>The attributes of the tag in the order written, their values decoded.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Attributes { get; private init; } = Array.Empty<KeyValuePair<string, string>>();

        /// <summary>What follows the block: <see cref="NoTail"/>, <see cref="PeerTail"/> or <see cref="DescendantTail"/>.</summary>
        public string Tail { get; private init; } = NoTail;

        /// <summary>The parts, for the card that does not know the sender: lead, body and note of a message; the parts of a notice.</summary>
        public IReadOnlyList<TaggedSection> Sections { get; private init; } = Array.Empty<TaggedSection>();

        /// <summary>Whether the parts read as a list of names and values (the notice of a task, when every part is one it knows).</summary>
        public bool Columns { get; private init; }

        /// <summary>Whether it is a wrapped notice (a teammate's message, a command's output…) rather than a message from a sender.</summary>
        public bool IsWrappedNotice { get; private init; }

        // ---- the table ----

        private enum Family
        {
            Message,
            Notice,
            Output,
            Task,
        }

        /// <summary>What the table says of one tag.</summary>
        private sealed class Shape
        {
            public Shape(Family family, string kind)
            {
                Family = family;
                Kind = kind;
            }

            public Family Family { get; }

            public string Kind { get; }

            /// <summary>The attributes the tag may have; null for any.</summary>
            public string[]? Allowed { get; init; }

            /// <summary>The attribute it must have; null for none.</summary>
            public string? Required { get; init; }

            /// <summary>The attributes that name the sender, the first one present winning.</summary>
            public string[] Sender { get; init; } = Array.Empty<string>();

            /// <summary>
            /// The stems of the lines that may come before the tag; <c>{name}</c> stands for the value of that attribute. A line is the
            /// stem and ":", or the stem, <see cref="WhileWorking"/> and ":".
            /// </summary>
            public string[] Leads { get; init; } = Array.Empty<string>();

            /// <summary>The <see cref="Tail"/> values of the notes that may follow the block.</summary>
            public string[] Tails { get; init; } = Array.Empty<string>();

            /// <summary>For an output: the name of its part.</summary>
            public string? Part { get; init; }
        }

        private static readonly Dictionary<string, Shape> Shapes = new Dictionary<string, Shape>(StringComparer.Ordinal)
        {
            ["agent-message"] = new Shape(Family.Message, AgentKind)
            {
                Allowed = new[] { "from" }, Required = "from", Sender = new[] { "from" },
                Leads = PeerLeads, Tails = new[] { PeerTail, DescendantTail },
            },
            ["cross-session-message"] = new Shape(Family.Message, CrossSessionKind)
            {
                Allowed = new[] { "from", "from-session", "from-name", "from-mode", "hop-chain" },
                Sender = new[] { "from-name", "from" }, Leads = PeerLeads, Tails = new[] { PeerTail, DescendantTail },
            },
            ["channel"] = new Shape(Family.Message, ChannelKind)
            {
                Required = "source", Sender = new[] { "source" }, Leads = new[] { "A message arrived from {source}" },
                Tails = new[] { PeerTail },
            },
            ["teammate-message"] = new Shape(Family.Notice, "teammate-message"),
            ["tick"] = new Shape(Family.Notice, "tick"),
            ["mcp-resource-update"] = new Shape(Family.Notice, "mcp-resource-update"),
            ["mcp-polling-update"] = new Shape(Family.Notice, "mcp-polling-update"),
            ["local-command-caveat"] = new Shape(Family.Notice, "local-command-caveat"),
            ["bash-stdout"] = new Shape(Family.Output, "bash-stdout") { Part = "output" },
            ["bash-stderr"] = new Shape(Family.Output, "bash-stderr") { Part = "error-output" },
            ["bash-exit-code"] = new Shape(Family.Output, "bash-exit-code") { Part = "exit-code" },
            ["task-notification"] = new Shape(Family.Task, TaskKind),
        };

        /// <summary>How the content of a part of a task notice reads.</summary>
        private enum Fill
        {
            /// <summary>Text, line breaks kept.</summary>
            Text,

            /// <summary>A list of named whole numbers.</summary>
            Numbers,

            /// <summary>A list of named one-line texts.</summary>
            Lines,
        }

        /// <summary>The parts a task notice may hold, each at most once, in any order.</summary>
        private static readonly Dictionary<string, Fill> TaskParts = new Dictionary<string, Fill>(StringComparer.Ordinal)
        {
            ["task-id"] = Fill.Text,
            ["tool-use-id"] = Fill.Text,
            ["task-type"] = Fill.Text,
            ["output-file"] = Fill.Text,
            ["status"] = Fill.Text,
            ["summary"] = Fill.Text,
            ["note"] = Fill.Text,
            ["event"] = Fill.Text,
            ["result"] = Fill.Text,
            ["usage"] = Fill.Numbers,
            ["worktree"] = Fill.Lines,
        };

        // ---- reading ----

        /// <summary>The tagged message <paramref name="text"/> is, or null when it is anything else (or holds broken markup).</summary>
        public static TaggedMessage? Read(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var start = 0;
            var end = text.Length;
            while (start < end && char.IsWhiteSpace(text[start])) start++;
            while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
            if (start == end) return null;

            // a block starts the text, or follows the one line that leads it
            string? lead = null;
            var at = start;
            if (text[start] != '<')
            {
                var lineEnd = text.IndexOf('\n', start, end - start);
                if (lineEnd < 0) return null;
                lead = text.Substring(start, lineEnd - start).TrimEnd('\r');
                at = lineEnd + 1;
                if (at >= end || text[at] != '<') return null;
            }

            var tag = LightMarkup.TagAt(text, at, end);
            if (tag == null || !Shapes.TryGetValue(tag.Name, out var shape)) return null;
            if (lead != null && shape.Leads.Length == 0) return null;
            switch (shape.Family)
            {
                case Family.Message: return ReadMessage(text, at, end, lead, shape);
                case Family.Notice: return ReadNotice(text, at, end, shape);
                case Family.Output: return ReadOutput(text, at, end);
                default: return ReadTask(text, at, end);
            }
        }

        private static TaggedMessage? ReadMessage(string text, int at, int end, string? lead, Shape shape)
        {
            var element = LightMarkup.ElementAt(text, at, end);
            if (element == null || !AttributesFit(element, shape)) return null;

            // the body has lines of its own: a line break after the open tag and one before the close tag
            var from = element.ContentStart;
            var to = element.ContentEnd;
            if (!SkipLineBreak(text, ref from, to, forward: true) || !SkipLineBreak(text, ref to, from, forward: false)) return null;
            // the claude writes the body as it came, unescaped: it is kept raw, and checked raw for a block inside the block
            var rawBody = text.Substring(from, to - from);
            if (HoldsOpenTag(rawBody, element.Name)) return null;

            if (lead != null && !LeadFits(lead, shape, element)) return null;

            // after the block: nothing, or one of the shape's notes after a blank line; a lead comes only with a note
            string? note = null;
            var tail = NoTail;
            if (element.End < end)
            {
                var i = element.End;
                if (!SkipLineBreak(text, ref i, end, forward: true)) return null;
                while (i < end && (text[i] == ' ' || text[i] == '\t')) i++;
                if (!SkipLineBreak(text, ref i, end, forward: true)) return null;
                note = text.Substring(i, end - i).Trim();
                tail = TailOfNote(note, shape);
                if (tail == null) return null;
            }
            else if (lead != null)
            {
                return null;
            }

            var body = Displayable(rawBody);
            var sections = new List<TaggedSection>(3);
            if (lead != null) sections.Add(new TaggedSection(null, Displayable(lead)));
            sections.Add(new TaggedSection(null, body));
            if (note != null) sections.Add(new TaggedSection(null, Displayable(note)));
            return new TaggedMessage
            {
                Kind = shape.Kind,
                FromName = SenderNamed(element, shape),
                Body = body,
                Attributes = element.Attributes,
                Tail = tail,
                Sections = sections,
            };
        }

        private static TaggedMessage? ReadNotice(string text, int at, int end, Shape shape)
        {
            var element = LightMarkup.ElementAt(text, at, end);
            if (element == null || element.End != end) return null;
            var content = Displayable(LightMarkup.Unescape(element.RawContent));
            return new TaggedMessage
            {
                Kind = shape.Kind,
                Body = content,
                Attributes = element.Attributes,
                Sections = new[] { new TaggedSection(null, content) },
                IsWrappedNotice = true,
            };
        }

        private static TaggedMessage? ReadOutput(string text, int at, int end)
        {
            var nodes = LightMarkup.Nodes(text, at, end, 8);
            if (nodes == null) return null;
            var sections = new List<TaggedSection>(3);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string? kind = null;
            foreach (var node in nodes)
            {
                if (node.Element is not { } element)
                {
                    if (!IsBlank(node.Text)) return null;
                    continue;
                }
                if (!Shapes.TryGetValue(element.Name, out var shape) || shape.Family != Family.Output || !seen.Add(element.Name)) return null;
                if (element.Attributes.Count > 0) return null;
                kind ??= shape.Kind;
                sections.Add(new TaggedSection(shape.Part, Displayable(LightMarkup.Unescape(element.RawContent))));
            }
            if (kind == null) return null;
            return new TaggedMessage
            {
                Kind = kind,
                Body = sections[0].Text,
                Sections = sections,
                IsWrappedNotice = true,
            };
        }

        private static TaggedMessage? ReadTask(string text, int at, int end)
        {
            var element = LightMarkup.ElementAt(text, at, end);
            if (element == null) return null;
            var nodes = LightMarkup.Nodes(text, element.ContentStart, element.ContentEnd, MaxTaskPieces);
            if (nodes == null) return null;

            var sections = new List<TaggedSection>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string? summary = null;
            var known = true;
            foreach (var node in nodes)
            {
                if (node.Element is not { } part)
                {
                    if (!IsBlank(node.Text)) sections.Add(new TaggedSection(OtherTextLabel, Displayable(node.Text!).Trim()));
                    continue;
                }
                // the claude escapes the parts of a task notice: they are decoded
                var value = Displayable(LightMarkup.Unescape(part.RawContent));
                if (part.Name == "summary" && summary == null) summary = value;
                if (!TaskParts.TryGetValue(part.Name, out var fill) || !seen.Add(part.Name) || part.Attributes.Count > 0)
                {
                    known = false;
                    continue;
                }
                IReadOnlyList<TaggedSection>? lines = null;
                if (fill != Fill.Text)
                {
                    lines = Facts(part, fill);
                    if (lines == null)
                    {
                        known = false;
                        continue;
                    }
                }
                sections.Add(new TaggedSection(part.Name, value) { Lines = lines });
            }
            if (summary == null || IsBlank(summary)) return null;

            // a notice it cannot lay out as a list is shown as it came
            if (!known)
            {
                sections.Clear();
                sections.Add(new TaggedSection(null, Displayable(text.Substring(element.Start, element.End - element.Start))));
            }
            if (element.End < end)
            {
                // what follows may be anything but another notice, or a second close of this one
                var after = text.Substring(element.End, end - element.End);
                if (HoldsOpenTag(after, element.Name) || after.IndexOf("</" + element.Name, StringComparison.Ordinal) >= 0) return null;
                sections.Add(new TaggedSection(AfterLabel, Displayable(after).Trim()));
            }
            return new TaggedMessage
            {
                Kind = TaskKind,
                Body = summary,
                Attributes = element.Attributes,
                Sections = sections,
                Columns = known,
            };
        }

        /// <summary>The named values inside a part of a task notice, every one of them checked; null when any is not what the part holds.</summary>
        private static IReadOnlyList<TaggedSection>? Facts(MarkupElement part, Fill fill)
        {
            var nodes = LightMarkup.Nodes(part.Source, part.ContentStart, part.ContentEnd, MaxFacts);
            if (nodes == null) return null;
            var facts = new List<TaggedSection>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in nodes)
            {
                if (node.Element is not { } fact)
                {
                    if (!IsBlank(node.Text)) return null;
                    continue;
                }
                if (fact.Attributes.Count > 0 || !seen.Add(fact.Name)) return null;
                // every value is decoded, the path and the branch of a worktree alike
                var value = Displayable(LightMarkup.Unescape(fact.RawContent)).Trim();
                if (fill == Fill.Numbers ? !AllDigits(value) : SpansLines(value)) return null;
                facts.Add(new TaggedSection(fact.Name, value));
            }
            return facts;
        }

        private static bool AttributesFit(MarkupElement element, Shape shape)
        {
            if (shape.Required != null && element.Attribute(shape.Required) == null) return false;
            if (shape.Allowed == null) return true;
            foreach (var pair in element.Attributes)
            {
                if (Array.IndexOf(shape.Allowed, pair.Key) < 0) return false;
            }
            return true;
        }

        private static string? SenderNamed(MarkupElement element, Shape shape)
        {
            foreach (var key in shape.Sender)
            {
                var value = element.Attribute(key);
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
            return null;
        }

        /// <summary>Whether the line before the tag is one of the shape's leads: a stem, its <c>{name}</c> filled, then ":" or <see cref="WhileWorking"/> and ":".</summary>
        private static bool LeadFits(string lead, Shape shape, MarkupElement element)
        {
            foreach (var pattern in shape.Leads)
            {
                var stem = Filled(pattern, element);
                if (stem == null || !lead.StartsWith(stem, StringComparison.Ordinal)) continue;
                var rest = lead.Substring(stem.Length);
                if (rest == ":" || rest == WhileWorking + ":") return true;
            }
            return false;
        }

        /// <summary>The stem with its <c>{name}</c> replaced by the value of that attribute; null when the tag lacks it.</summary>
        private static string? Filled(string stem, MarkupElement element)
        {
            var open = stem.IndexOf('{');
            var close = open < 0 ? -1 : stem.IndexOf('}', open + 1);
            if (close < 0) return stem;
            var value = element.Attribute(stem.Substring(open + 1, close - open - 1));
            return value == null ? null : stem.Substring(0, open) + value + stem.Substring(close + 1);
        }

        /// <summary>The <see cref="Tail"/> a note gives when the shape takes it; null when the text is no note the shape takes.</summary>
        private static string? TailOfNote(string note, Shape shape)
        {
            foreach (var known in Notes)
            {
                if (note.StartsWith(known.Key, StringComparison.Ordinal) && Array.IndexOf(shape.Tails, known.Value) >= 0) return known.Value;
            }
            return null;
        }

        /// <summary>
        /// A part as the card shows it: no terminal escape sequences, no control characters but line feeds and tabs (a carriage return
        /// before a line feed goes, a lone one becomes a line feed), without a <c>persisted-output</c> wrapper around the whole of it,
        /// and without line breaks at its ends.
        /// </summary>
        private static string Displayable(string text)
        {
            var stripped = AnsiText.Strip(text ?? "");
            var sb = new StringBuilder(stripped.Length);
            for (var i = 0; i < stripped.Length; i++)
            {
                var c = stripped[i];
                if (c == '\r')
                {
                    if (i + 1 >= stripped.Length || stripped[i + 1] != '\n') sb.Append('\n');
                    continue;
                }
                if (c == '\n' || c == '\t' || !char.IsControl(c)) sb.Append(c);
            }
            return StripOuterBreaks(Unwrapped(sb.ToString(), PersistedOutputTag));
        }

        /// <summary>The inside of the text when the whole of it (white space aside) is one <c>&lt;name&gt;…&lt;/name&gt;</c>; else the text.</summary>
        private static string Unwrapped(string text, string name)
        {
            var open = "<" + name + ">";
            var close = "</" + name + ">";
            var trimmed = text.Trim();
            if (trimmed.Length < open.Length + close.Length
                || !trimmed.StartsWith(open, StringComparison.Ordinal) || !trimmed.EndsWith(close, StringComparison.Ordinal))
                return text;
            var inner = trimmed.Substring(open.Length, trimmed.Length - open.Length - close.Length);
            return inner.IndexOf(open, StringComparison.Ordinal) >= 0 || inner.IndexOf(close, StringComparison.Ordinal) >= 0 ? text : inner;
        }

        /// <summary>Moves <paramref name="i"/> over one line break (<c>\n</c> or <c>\r\n</c>) toward <paramref name="bound"/>; false when none stands there.</summary>
        private static bool SkipLineBreak(string s, ref int i, int bound, bool forward)
        {
            if (forward)
            {
                if (i < bound && s[i] == '\r') i++;
                if (i >= bound || s[i] != '\n') return false;
                i++;
                return true;
            }
            if (i <= bound || s[i - 1] != '\n') return false;
            i--;
            if (i > bound && s[i - 1] == '\r') i--;
            return true;
        }

        /// <summary>Whether the text holds an open tag of the name: <c>&lt;name</c> then a space, <c>&gt;</c> or <c>/</c>.</summary>
        private static bool HoldsOpenTag(string text, string name)
        {
            var probe = "<" + name;
            var i = 0;
            while ((i = text.IndexOf(probe, i, StringComparison.Ordinal)) >= 0)
            {
                var next = i + probe.Length;
                if (next >= text.Length || text[next] == '>' || text[next] == '/' || char.IsWhiteSpace(text[next])) return true;
                i = next;
            }
            return false;
        }

        private static bool AllDigits(string text)
        {
            if (text.Length == 0 || text.Length > MaxDigits) return false;
            foreach (var c in text)
            {
                if (c < '0' || c > '9') return false;
            }
            return true;
        }

        private static bool IsBlank(string? text)
        {
            if (text == null) return true;
            foreach (var c in text)
            {
                if (!char.IsWhiteSpace(c)) return false;
            }
            return true;
        }

        private static bool SpansLines(string text) => text.IndexOf('\n') >= 0 || text.IndexOf('\r') >= 0;

        private static string StripOuterBreaks(string text) => text.Trim('\r', '\n');

        // ---- names and previews ----

        /// <summary>
        /// The short name of a sender: letters, digits, dots and underscores kept; any run of other characters (dashes among them)
        /// one "-"; invisible characters dropped; dashes and dots taken off both ends; cut at <see cref="HandleLength"/> with "…".
        /// Empty when nothing is left.
        /// </summary>
        public static string Handle(string? name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var sb = new StringBuilder(name!.Length);
            var dash = false;
            foreach (var c in name)
            {
                if (TakesNoRoom(c)) continue;
                if (char.IsLetterOrDigit(c) || c == '.' || c == '_')
                {
                    sb.Append(c);
                    dash = false;
                }
                else if (!dash)
                {
                    sb.Append('-');
                    dash = true;
                }
            }
            var first = 0;
            var last = sb.Length;
            while (first < last && (sb[first] == '-' || sb[first] == '.')) first++;
            while (last > first && (sb[last - 1] == '-' || sb[last - 1] == '.')) last--;
            var handle = sb.ToString(first, last - first);
            return Clip(handle, HandleLength);
        }

        /// <summary>
        /// The full name of a sender as one clean line: invisible characters dropped, controls and runs of white space one space,
        /// the ends trimmed, cut at <see cref="PlainNameLength"/> with "…".
        /// </summary>
        public static string PlainName(string? name) => Clip(OneLine(name ?? ""), PlainNameLength);

        /// <summary>
        /// The preview of a message: its first line with text, without terminal escape sequences, invisible characters and controls,
        /// white space collapsed, cut at <see cref="PreviewLength"/> with "…". Empty when no line has text.
        /// </summary>
        public static string Preview(string body)
        {
            if (string.IsNullOrEmpty(body)) return "";
            var text = AnsiText.Strip(body);
            var lineStart = 0;
            while (lineStart < text.Length)
            {
                var lineEnd = text.IndexOf('\n', lineStart);
                if (lineEnd < 0) lineEnd = text.Length;
                var line = OneLine(text.Substring(lineStart, lineEnd - lineStart));
                if (line.Length > 0) return Clip(line, PreviewLength);
                lineStart = lineEnd + 1;
            }
            return ""; // no line with text (an empty text after the escapes went among them)
        }

        /// <summary>The text with invisible characters dropped, every control and run of white space made one space, the ends trimmed.</summary>
        private static string OneLine(string text)
        {
            var sb = new StringBuilder(text.Length);
            var space = true; // true at the start, so leading space goes
            foreach (var c in text)
            {
                if (TakesNoRoom(c)) continue;
                if (char.IsWhiteSpace(c) || char.IsControl(c))
                {
                    if (!space) sb.Append(' ');
                    space = true;
                    continue;
                }
                sb.Append(c);
                space = false;
            }
            if (sb.Length > 0 && sb[sb.Length - 1] == ' ') sb.Length--;
            return sb.ToString();
        }

        /// <summary>A character that takes no room: the format characters (zero-width space and joiners, the byte order mark, soft hyphen…).</summary>
        private static bool TakesNoRoom(char c) => CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format;

        /// <summary>The text cut at <paramref name="length"/> characters with "…", never between the halves of a surrogate pair.</summary>
        private static string Clip(string text, int length)
        {
            if (text.Length <= length) return text;
            var keep = char.IsHighSurrogate(text[length - 1]) ? length - 1 : length;
            return text.Substring(0, keep) + Ellipsis;
        }
    }
}
