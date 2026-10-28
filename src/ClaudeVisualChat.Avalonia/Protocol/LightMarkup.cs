using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ClaudeVisualChat.Protocol
{
    /// <summary>
    /// One element of a text in light markup: its open tag, the raw text up to its close tag, and where each lies in the source. The
    /// content is never read as markup here; a caller that wants the elements inside asks <see cref="LightMarkup.Nodes"/> for them.
    /// </summary>
    internal sealed class MarkupElement
    {
        public MarkupElement(string source, string name, IReadOnlyList<KeyValuePair<string, string>> attributes, int start, int contentStart)
        {
            Source = source;
            Name = name;
            Attributes = attributes;
            Start = start;
            ContentStart = contentStart;
            ContentEnd = contentStart;
            End = -1;
        }

        /// <summary>Whether its close tag has been found (or it closed itself with <c>/&gt;</c>).</summary>
        public bool IsWhole => End >= 0;

        public string Source { get; }

        public string Name { get; }

        /// <summary>The attributes in the order they were written, their values decoded.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Attributes { get; }

        /// <summary>The offset of the <c>&lt;</c> of the open tag.</summary>
        public int Start { get; }

        /// <summary>The offset right after the open tag.</summary>
        public int ContentStart { get; }

        /// <summary>The offset of the <c>&lt;</c> of the close tag (the same as <see cref="ContentStart"/> for <c>&lt;x/&gt;</c>).</summary>
        public int ContentEnd { get; private set; }

        /// <summary>The offset right after the close tag; -1 while the close tag has not been found.</summary>
        public int End { get; private set; }

        internal void Close(int contentEnd, int end)
        {
            ContentEnd = contentEnd;
            End = end;
        }

        /// <summary>The content as written, entities not decoded.</summary>
        public string RawContent => Source.Substring(ContentStart, ContentEnd - ContentStart);

        /// <summary>The value of an attribute; null when the tag does not have it.</summary>
        public string? Attribute(string name)
        {
            foreach (var pair in Attributes)
            {
                if (pair.Key == name) return pair.Value;
            }
            return null;
        }
    }

    /// <summary>A piece of a run of light markup: an element, or the text between two of them.</summary>
    internal readonly struct MarkupNode
    {
        public MarkupNode(MarkupElement element)
        {
            Element = element;
            Text = null;
        }

        public MarkupNode(string text)
        {
            Element = null;
            Text = text;
        }

        /// <summary>The element; null for a text.</summary>
        public MarkupElement? Element { get; }

        /// <summary>The text as written; null for an element.</summary>
        public string? Text { get; }
    }

    /// <summary>
    /// A small reader of the markup the claude writes around messages: tags with quoted attributes, closed by a tag of the same name.
    /// It is a scanner, not an XML parser, and it is forgiving where the claude is: the content of an element runs to the first close tag
    /// of the same name, so text in it may hold a stray <c>&lt;</c> or a tag of another name. Every step moves forward over the text, so
    /// the cost grows with the length of the text (times the length of a name at most); there are no regular expressions.
    /// </summary>
    internal static class LightMarkup
    {
        /// <summary>The longest name of a tag or an attribute it reads.</summary>
        public const int MaxNameLength = 64;

        /// <summary>The most attributes a tag may have.</summary>
        public const int MaxAttributes = 32;

        /// <summary>
        /// The open tag at <paramref name="at"/> (where <c>&lt;</c> stands), with no content yet; null when what stands there is not
        /// a well-formed open tag before <paramref name="limit"/>. A tag closed by <c>/&gt;</c> comes back already whole (empty).
        /// </summary>
        public static MarkupElement? TagAt(string s, int at, int limit)
        {
            if (at < 0 || at >= limit || s[at] != '<') return null;
            var i = at + 1;
            var name = ReadName(s, ref i, limit, allowUnderscoreFirst: false);
            if (name == null) return null;
            List<KeyValuePair<string, string>>? attributes = null;
            while (true)
            {
                var spaced = SkipSpace(s, ref i, limit);
                if (i >= limit) return null;
                if (s[i] == '>')
                {
                    return new MarkupElement(s, name, Frozen(attributes), at, i + 1);
                }
                if (s[i] == '/')
                {
                    if (i + 1 >= limit || s[i + 1] != '>') return null;
                    var empty = new MarkupElement(s, name, Frozen(attributes), at, i + 2);
                    empty.Close(i + 2, i + 2);
                    return empty;
                }
                // an attribute needs room before it: <a b="1"c="2"> is no tag
                if (!spaced) return null;
                var key = ReadName(s, ref i, limit, allowUnderscoreFirst: true);
                if (key == null) return null;
                SkipSpace(s, ref i, limit);
                if (i >= limit || s[i] != '=') return null;
                i++;
                SkipSpace(s, ref i, limit);
                if (i >= limit || (s[i] != '"' && s[i] != '\'')) return null;
                var quote = s[i];
                var close = s.IndexOf(quote, i + 1, limit - i - 1);
                if (close < 0) return null;
                var value = Unescape(s.Substring(i + 1, close - i - 1));
                i = close + 1;
                attributes ??= new List<KeyValuePair<string, string>>();
                if (attributes.Count >= MaxAttributes) return null;
                foreach (var pair in attributes)
                {
                    if (pair.Key == key) return null; // the same attribute twice
                }
                attributes.Add(new KeyValuePair<string, string>(key, value));
            }
        }

        /// <summary>
        /// The whole element at <paramref name="at"/>: its open tag and its content up to the first close tag of its name before
        /// <paramref name="limit"/>; null when either is missing.
        /// </summary>
        public static MarkupElement? ElementAt(string s, int at, int limit)
        {
            var element = TagAt(s, at, limit);
            if (element == null || element.IsWhole) return element; // <x/> is whole already
            var close = FindClose(s, element.Name, element.ContentStart, limit, out var after);
            if (close < 0) return null;
            element.Close(close, after);
            return element;
        }

        /// <summary>
        /// The elements and the texts between them in <paramref name="from"/>..<paramref name="to"/>, in order. A <c>&lt;</c> that opens
        /// no tag is text. Null when an element is not closed, when a close tag stands with no element to close, or when there would be
        /// more than <paramref name="maxNodes"/> pieces.
        /// </summary>
        public static List<MarkupNode>? Nodes(string s, int from, int to, int maxNodes)
        {
            var nodes = new List<MarkupNode>();
            var text = new StringBuilder();
            var i = from;
            while (i < to)
            {
                var lt = s.IndexOf('<', i, to - i);
                if (lt < 0)
                {
                    text.Append(s, i, to - i);
                    break;
                }
                text.Append(s, i, lt - i);
                if (IsCloseTag(s, lt, to)) return null;
                var element = TagAt(s, lt, to);
                if (element == null)
                {
                    text.Append('<');
                    i = lt + 1;
                    continue;
                }
                element = ElementAt(s, lt, to);
                if (element == null) return null;
                if (text.Length > 0)
                {
                    nodes.Add(new MarkupNode(text.ToString()));
                    text.Clear();
                }
                nodes.Add(new MarkupNode(element));
                if (nodes.Count > maxNodes) return null;
                i = element.End;
            }
            if (text.Length > 0) nodes.Add(new MarkupNode(text.ToString()));
            return nodes.Count > maxNodes ? null : nodes;
        }

        /// <summary>
        /// The text with the five named entities (<c>&amp;amp;</c>, <c>&amp;lt;</c>, <c>&amp;gt;</c>, <c>&amp;quot;</c>, <c>&amp;apos;</c>)
        /// and the numeric ones turned into their characters. Anything else that starts with <c>&amp;</c> stays as written.
        /// </summary>
        public static string Unescape(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('&') < 0) return text ?? "";
            var sb = new StringBuilder(text.Length);
            var i = 0;
            while (i < text.Length)
            {
                var amp = text.IndexOf('&', i);
                if (amp < 0)
                {
                    sb.Append(text, i, text.Length - i);
                    break;
                }
                sb.Append(text, i, amp - i);
                // an entity is short: look for its ';' within a few characters only
                var semi = text.IndexOf(';', amp + 1, Math.Min(12, text.Length - amp - 1));
                var replaced = semi > amp + 1 ? Entity(text.Substring(amp + 1, semi - amp - 1)) : null;
                if (replaced == null)
                {
                    sb.Append('&');
                    i = amp + 1;
                }
                else
                {
                    sb.Append(replaced);
                    i = semi + 1;
                }
            }
            return sb.ToString();
        }

        private static string? Entity(string name)
        {
            switch (name)
            {
                case "amp": return "&";
                case "lt": return "<";
                case "gt": return ">";
                case "quot": return "\"";
                case "apos": return "'";
            }
            if (name.Length < 2 || name[0] != '#') return null;
            int code;
            var ok = name[1] == 'x' || name[1] == 'X'
                ? int.TryParse(name.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out code)
                : int.TryParse(name.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out code);
            if (!ok || code <= 0 || code > 0x10FFFF || (code >= 0xD800 && code <= 0xDFFF)) return null;
            return char.ConvertFromUtf32(code);
        }

        /// <summary>The offset of the first <c>&lt;/name&gt;</c> at or after <paramref name="from"/>, and the offset after it; -1 when none.</summary>
        private static int FindClose(string s, string name, int from, int limit, out int after)
        {
            var i = from;
            while (i < limit)
            {
                var lt = s.IndexOf("</", i, limit - i, StringComparison.Ordinal);
                if (lt < 0) break;
                var j = lt + 2;
                if (limit - j >= name.Length && string.CompareOrdinal(s, j, name, 0, name.Length) == 0)
                {
                    j += name.Length;
                    SkipSpace(s, ref j, limit);
                    if (j < limit && s[j] == '>')
                    {
                        after = j + 1;
                        return lt;
                    }
                }
                i = lt + 2;
            }
            after = -1;
            return -1;
        }

        /// <summary>Whether a close tag (<c>&lt;/name&gt;</c>) stands at <paramref name="at"/>.</summary>
        private static bool IsCloseTag(string s, int at, int limit)
        {
            if (at + 1 >= limit || s[at + 1] != '/') return false;
            var i = at + 2;
            if (ReadName(s, ref i, limit, allowUnderscoreFirst: false) == null) return false;
            SkipSpace(s, ref i, limit);
            return i < limit && s[i] == '>';
        }

        /// <summary>A name: an ASCII letter (or an underscore, for an attribute), then letters, digits, dashes and underscores.</summary>
        private static string? ReadName(string s, ref int i, int limit, bool allowUnderscoreFirst)
        {
            var start = i;
            if (i >= limit || !(IsAsciiLetter(s[i]) || (allowUnderscoreFirst && s[i] == '_'))) return null;
            i++;
            while (i < limit && (IsAsciiLetter(s[i]) || (s[i] >= '0' && s[i] <= '9') || s[i] == '-' || s[i] == '_'))
            {
                i++;
                if (i - start > MaxNameLength) return null;
            }
            return s.Substring(start, i - start);
        }

        private static bool IsAsciiLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

        private static bool SkipSpace(string s, ref int i, int limit)
        {
            var start = i;
            while (i < limit && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
            return i > start;
        }

        private static IReadOnlyList<KeyValuePair<string, string>> Frozen(List<KeyValuePair<string, string>>? attributes) =>
            attributes == null ? Array.Empty<KeyValuePair<string, string>>() : (IReadOnlyList<KeyValuePair<string, string>>)attributes.AsReadOnly();
    }
}
