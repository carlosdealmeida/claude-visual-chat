using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClaudeVisualChat.Protocol
{
    /// <summary>Turns one line of a Claude Code session file (<c>.jsonl</c>) into events. Stateless and tolerant.</summary>
    public static class TranscriptParser
    {
        private static readonly IReadOnlyList<ClaudeEvent> None = Array.Empty<ClaudeEvent>();

        public static IReadOnlyList<ClaudeEvent> Parse(string line)
        {
            try
            {
                var json = LineText.Prepare(line);
                if (json == null) return None;
                using (var doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;
                    switch (JsonHelpers.Str(root, "type"))
                    {
                        case "user":
                            return EventReader.ReadUser(root, "toolUseResult", null);
                        case "assistant":
                            return EventReader.ReadAssistant(root, null);
                        case "system":
                            return EventReader.ReadNotice(root);
                        default:
                            return None;
                    }
                }
            }
            catch (Exception)
            {
                return None;
            }
        }
    }

    internal static class LineText
    {
        private static readonly Regex Ansi = new Regex("^\u001b\\[[0-9;?]*[A-Za-z]", RegexOptions.Compiled);

        /// <summary>Trims the line and drops leading ANSI escapes; null when nothing JSON-like is left.</summary>
        public static string? Prepare(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            var text = line!.Trim();
            while (true)
            {
                var m = Ansi.Match(text);
                if (!m.Success) break;
                text = text.Substring(m.Length);
            }
            text = text.Trim();
            return text.Length > 0 && text[0] == '{' ? text : null;
        }
    }

    internal static class EventReader
    {
        private static readonly IReadOnlyList<ClaudeEvent> None = Array.Empty<ClaudeEvent>();

        public static IReadOnlyList<ClaudeEvent> ReadUser(JsonElement root, string toolUseResultName, string? parent)
        {
            var message = JsonHelpers.Obj(root, "message");
            if (message == null) return None;
            var blocks = BlockReader.ReadContent(JsonHelpers.Prop(message.Value, "content"));
            if (blocks.Count == 0) return None;

            var result = JsonHelpers.CloneNonNull(root, toolUseResultName);
            var isMeta = JsonHelpers.Bool(root, "isMeta");
            var isCompact = JsonHelpers.Bool(root, "isCompactSummary");
            var meta = JsonHelpers.Prop(root, "tool_result_meta"); // stream
            var denialKind = JsonHelpers.Str(root, "toolDenialKind"); // session file
            var timestamp = JsonHelpers.Time(root, "timestamp");
            var events = new List<ClaudeEvent>(blocks.Count);
            foreach (var block in blocks)
            {
                var b = block;
                if (b is ToolResultBlock toolResult
                    && (NonExecutionKind(meta, toolResult.ToolUseId) ?? denialKind) is { } kind)
                    b = toolResult with { NonExecutionKind = kind };
                events.Add(new UserBlock(parent, b, result, isMeta, isCompact) { Timestamp = timestamp });
            }
            return events;
        }

        /// <summary>The <c>non_execution_kind</c> of the <c>tool_result_meta</c> entry for that tool call.</summary>
        private static string? NonExecutionKind(JsonElement? meta, string toolUseId)
        {
            if (meta == null || meta.Value.ValueKind != JsonValueKind.Array) return null;
            foreach (var entry in meta.Value.EnumerateArray())
            {
                if (JsonHelpers.Str(entry, "id") == toolUseId)
                    return JsonHelpers.Str(entry, "non_execution_kind");
            }
            return null;
        }

        public static IReadOnlyList<ClaudeEvent> ReadAssistant(JsonElement root, string? parent)
        {
            var message = JsonHelpers.Obj(root, "message");
            if (message == null) return None;
            var content = JsonHelpers.Prop(message.Value, "content");
            if (content == null || content.Value.ValueKind != JsonValueKind.Array) return None;

            var id = JsonHelpers.Str(message.Value, "id") ?? "";
            var timestamp = JsonHelpers.Time(root, "timestamp");
            var thinking = JsonHelpers.Long(root, "thinkingDurationMs");
            var events = new List<ClaudeEvent>();
            foreach (var block in BlockReader.ReadContent(content))
                events.Add(new AssistantBlock(id, parent, block) { Timestamp = timestamp, ThinkingDurationMs = thinking });
            return events;
        }

        public static IReadOnlyList<ClaudeEvent> ReadNotice(JsonElement root)
        {
            var subtype = JsonHelpers.Str(root, "subtype");
            return subtype == "compact_boundary" || subtype == "api_retry"
                ? new ClaudeEvent[] { new SystemNotice(subtype!) }
                : None;
        }
    }
}
