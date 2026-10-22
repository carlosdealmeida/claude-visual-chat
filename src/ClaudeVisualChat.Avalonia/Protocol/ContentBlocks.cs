using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace ClaudeVisualChat.Protocol
{
    /// <summary>One block of a message, as the Claude API describes it.</summary>
    public abstract record ContentBlock;

    /// <summary>A text block (<c>"type": "text"</c>); named so it never clashes with Avalonia's <c>TextBlock</c>.</summary>
    public sealed record TextContentBlock(string Text) : ContentBlock;

    public sealed record ThinkingBlock(string Text) : ContentBlock;

    /// <summary>A tool call. <paramref name="Input"/> is an independent clone.</summary>
    public sealed record ToolUseBlock(string Id, string Name, JsonElement Input) : ContentBlock;

    /// <summary>A tool result; <paramref name="Text"/> joins the text blocks of an array content with a line feed.</summary>
    public sealed record ToolResultBlock(string ToolUseId, string Text, bool IsError) : ContentBlock
    {
        /// <summary>
        /// Why the tool never ran: <c>permission-rule</c> (denied) or <c>user-rejected</c> (interrupted). Comes from
        /// the stream's <c>tool_result_meta</c> or the session file's <c>toolDenialKind</c>; null when it ran.
        /// </summary>
        public string? NonExecutionKind { get; init; }
    }

    /// <summary>An image; <paramref name="Data"/> is base64 as it came, empty when the source is not base64.</summary>
    public sealed record ImageBlock(string MediaType, string Data) : ContentBlock;

    internal static class JsonHelpers
    {
        private static readonly JsonElement EmptyObject = Parse("{}");

        public static JsonElement EmptyInput => EmptyObject;

        private static JsonElement Parse(string json)
        {
            using (var doc = JsonDocument.Parse(json))
                return doc.RootElement.Clone();
        }

        public static JsonElement? Prop(JsonElement el, string name)
        {
            if (el.ValueKind != JsonValueKind.Object) return null;
            return el.TryGetProperty(name, out var value) ? value : (JsonElement?)null;
        }

        public static JsonElement? Obj(JsonElement el, string name)
        {
            var p = Prop(el, name);
            return p != null && p.Value.ValueKind == JsonValueKind.Object ? p : null;
        }

        public static string? Str(JsonElement el, string name)
        {
            var p = Prop(el, name);
            return p != null && p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null;
        }

        public static bool Bool(JsonElement el, string name)
        {
            var p = Prop(el, name);
            return p != null && p.Value.ValueKind == JsonValueKind.True;
        }

        public static long? Long(JsonElement el, string name)
        {
            var p = Prop(el, name);
            if (p == null || p.Value.ValueKind != JsonValueKind.Number) return null;
            return p.Value.TryGetInt64(out var l) ? l : (long)p.Value.GetDouble();
        }

        public static double? Double(JsonElement el, string name)
        {
            var p = Prop(el, name);
            return p != null && p.Value.ValueKind == JsonValueKind.Number ? p.Value.GetDouble() : (double?)null;
        }

        public static JsonElement? Arr(JsonElement el, string name)
        {
            var p = Prop(el, name);
            return p != null && p.Value.ValueKind == JsonValueKind.Array ? p : null;
        }

        /// <summary>The string items of an array property; empty when missing.</summary>
        public static IReadOnlyList<string> Strings(JsonElement el, string name)
        {
            var list = new List<string>();
            var array = Arr(el, name);
            if (array == null) return list;
            foreach (var item in array.Value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String) list.Add(item.GetString() ?? "");
            }
            return list;
        }

        /// <summary>An ISO 8601 time (<c>2026-10-19T21:31:35.778Z</c>); null when missing or not a time.</summary>
        public static DateTimeOffset? Time(JsonElement el, string name)
        {
            var text = Str(el, name);
            return text != null
                   && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time)
                ? time
                : (DateTimeOffset?)null;
        }

        /// <summary>Returns a clone of a property that is present and not null.</summary>
        public static JsonElement? CloneNonNull(JsonElement el, string name)
        {
            var p = Prop(el, name);
            return p != null && p.Value.ValueKind != JsonValueKind.Null ? p.Value.Clone() : (JsonElement?)null;
        }
    }

    /// <summary>Reads the content blocks of API messages. Unknown block types are skipped.</summary>
    internal static class BlockReader
    {
        public static ContentBlock? Read(JsonElement block)
        {
            switch (JsonHelpers.Str(block, "type"))
            {
                case "text":
                    return new TextContentBlock(JsonHelpers.Str(block, "text") ?? "");
                case "thinking":
                    return new ThinkingBlock(JsonHelpers.Str(block, "thinking") ?? "");
                case "tool_use":
                    return new ToolUseBlock(
                        JsonHelpers.Str(block, "id") ?? "",
                        JsonHelpers.Str(block, "name") ?? "",
                        JsonHelpers.CloneNonNull(block, "input") ?? JsonHelpers.EmptyInput);
                case "tool_result":
                    return new ToolResultBlock(
                        JsonHelpers.Str(block, "tool_use_id") ?? "",
                        ReadResultText(JsonHelpers.Prop(block, "content")),
                        JsonHelpers.Bool(block, "is_error"));
                case "image":
                    return ReadImage(block);
                default:
                    return null;
            }
        }

        private static ImageBlock ReadImage(JsonElement block)
        {
            var source = JsonHelpers.Obj(block, "source");
            if (source == null) return new ImageBlock("", "");
            var mediaType = JsonHelpers.Str(source.Value, "media_type") ?? "";
            var data = JsonHelpers.Str(source.Value, "type") == "base64" ? JsonHelpers.Str(source.Value, "data") ?? "" : "";
            return new ImageBlock(mediaType, data);
        }

        /// <summary>Blocks of <c>message.content</c>; a plain string becomes one text block.</summary>
        public static IReadOnlyList<ContentBlock> ReadContent(JsonElement? content)
        {
            var list = new List<ContentBlock>();
            if (content == null) return list;
            var c = content.Value;
            if (c.ValueKind == JsonValueKind.String)
            {
                list.Add(new TextContentBlock(c.GetString() ?? ""));
            }
            else if (c.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in c.EnumerateArray())
                {
                    var block = Read(item);
                    if (block != null) list.Add(block);
                }
            }
            return list;
        }

        private static string ReadResultText(JsonElement? content)
        {
            if (content == null) return "";
            var c = content.Value;
            if (c.ValueKind == JsonValueKind.String) return c.GetString() ?? "";
            if (c.ValueKind != JsonValueKind.Array) return "";
            var parts = new List<string>();
            foreach (var item in c.EnumerateArray())
            {
                if (JsonHelpers.Str(item, "type") == "text")
                    parts.Add(JsonHelpers.Str(item, "text") ?? "");
            }
            return string.Join("\n", parts);
        }
    }
}
