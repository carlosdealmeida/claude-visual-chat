using System;
using System.Collections.Generic;
using System.Text.Json;

namespace ClaudeVisualChat.Protocol
{
    /// <summary>
    /// Turns lines of <c>claude -p --output-format stream-json</c> into events. Keeps the <c>message.id</c> of the
    /// last <c>message_start</c> per <c>parent_tool_use_id</c> so the deltas that follow can carry it.
    /// Anything it does not recognise yields no events and never throws.
    /// </summary>
    public sealed class StreamJsonParser
    {
        private static readonly IReadOnlyList<ClaudeEvent> None = Array.Empty<ClaudeEvent>();

        private readonly Dictionary<string, string> _messageIds = new Dictionary<string, string>();

        public IReadOnlyList<ClaudeEvent> Parse(string line)
        {
            try
            {
                var json = LineText.Prepare(line);
                if (json == null) return None;
                using (var doc = JsonDocument.Parse(json))
                    return ParseRoot(doc.RootElement);
            }
            catch (Exception)
            {
                return None;
            }
        }

        private IReadOnlyList<ClaudeEvent> ParseRoot(JsonElement root)
        {
            switch (JsonHelpers.Str(root, "type"))
            {
                case "system":
                    return ParseSystem(root);
                case "stream_event":
                    return ParseStreamEvent(root);
                case "assistant":
                    return EventReader.ReadAssistant(root, JsonHelpers.Str(root, "parent_tool_use_id"));
                case "user":
                    return EventReader.ReadUser(root, "tool_use_result", JsonHelpers.Str(root, "parent_tool_use_id"));
                case "control_request":
                    return ParseControlRequest(root);
                case "control_cancel_request":
                    {
                        var id = JsonHelpers.Str(root, "request_id");
                        return id == null ? None : new ClaudeEvent[] { new PermissionCancelled(id) };
                    }
                case "control_response":
                    return ParseControlResponse(root);
                case "result":
                    return ParseResult(root);
                case "rate_limit_event":
                    return EventReader.ReadRateLimit(root);
                case "command_lifecycle":
                    {
                        var uuid = JsonHelpers.Str(root, "command_uuid");
                        var state = JsonHelpers.Str(root, "state");
                        return uuid == null || state == null
                            ? None
                            : new ClaudeEvent[] { new CommandLifecycle(uuid, state, JsonHelpers.Bool(root, "ran_ahead")) };
                    }
                default:
                    return None;
            }
        }

        private static IReadOnlyList<ClaudeEvent> ParseSystem(JsonElement root)
        {
            switch (JsonHelpers.Str(root, "subtype"))
            {
                case "init":
                    return new ClaudeEvent[]
                    {
                        new SessionStarted(
                            JsonHelpers.Str(root, "session_id"),
                            JsonHelpers.Str(root, "model"),
                            JsonHelpers.Str(root, "cwd"),
                            JsonHelpers.Str(root, "permissionMode"),
                            JsonHelpers.Str(root, "claude_code_version"))
                        {
                            TerminalCommands = JsonHelpers.Strings(root, "terminal_slash_commands"),
                        },
                    };
                case "status":
                    return new ClaudeEvent[]
                    {
                        new StatusChanged(JsonHelpers.Str(root, "status"), JsonHelpers.Str(root, "permissionMode")),
                    };
                default:
                    return EventReader.ReadNotice(root);
            }
        }

        private IReadOnlyList<ClaudeEvent> ParseStreamEvent(JsonElement root)
        {
            var ev = JsonHelpers.Obj(root, "event");
            if (ev == null) return None;
            var e = ev.Value;
            var parent = JsonHelpers.Str(root, "parent_tool_use_id");
            var key = parent ?? "";

            switch (JsonHelpers.Str(e, "type"))
            {
                case "message_start":
                    {
                        var message = JsonHelpers.Obj(e, "message");
                        var id = message == null ? null : JsonHelpers.Str(message.Value, "id");
                        if (id != null) _messageIds[key] = id;
                        return None;
                    }
                case "content_block_start":
                    {
                        var index = JsonHelpers.Long(e, "index");
                        var blockEl = JsonHelpers.Obj(e, "content_block");
                        if (index == null || blockEl == null) return None;
                        var block = BlockReader.Read(blockEl.Value);
                        if (block == null) return None;
                        if (block is ToolUseBlock tool)
                            block = tool with { Input = JsonHelpers.EmptyInput };
                        return new ClaudeEvent[] { new BlockStarted(MessageId(key), (int)index.Value, parent, block) };
                    }
                case "content_block_delta":
                    return ParseDelta(e, parent, key);
                case "content_block_stop":
                    {
                        var index = JsonHelpers.Long(e, "index");
                        return index == null
                            ? None
                            : new ClaudeEvent[] { new BlockStopped(MessageId(key), (int)index.Value, parent) };
                    }
                default:
                    return None;
            }
        }

        private IReadOnlyList<ClaudeEvent> ParseDelta(JsonElement e, string? parent, string key)
        {
            var index = JsonHelpers.Long(e, "index");
            var delta = JsonHelpers.Obj(e, "delta");
            if (index == null || delta == null) return None;

            DeltaKind kind;
            string? text;
            switch (JsonHelpers.Str(delta.Value, "type"))
            {
                case "text_delta":
                    kind = DeltaKind.Text;
                    text = JsonHelpers.Str(delta.Value, "text");
                    break;
                case "thinking_delta":
                    kind = DeltaKind.Thinking;
                    text = JsonHelpers.Str(delta.Value, "thinking");
                    break;
                case "input_json_delta":
                    kind = DeltaKind.InputJson;
                    text = JsonHelpers.Str(delta.Value, "partial_json");
                    break;
                default:
                    return None;
            }
            var estimate = kind == DeltaKind.Thinking ? JsonHelpers.Long(delta.Value, "estimated_tokens") : null;
            return new ClaudeEvent[] { new BlockDelta(MessageId(key), (int)index.Value, parent, kind, text ?? "") { EstimatedTokens = estimate } };
        }

        private string MessageId(string key) => _messageIds.TryGetValue(key, out var id) ? id : "";

        private static IReadOnlyList<ClaudeEvent> ParseControlRequest(JsonElement root)
        {
            var requestId = JsonHelpers.Str(root, "request_id");
            var request = JsonHelpers.Obj(root, "request");
            if (requestId == null || request == null) return None;
            var r = request.Value;
            if (JsonHelpers.Str(r, "subtype") != "can_use_tool") return None;
            var toolName = JsonHelpers.Str(r, "tool_name");
            if (toolName == null) return None;

            return new ClaudeEvent[]
            {
                new PermissionRequested(
                    requestId,
                    toolName,
                    JsonHelpers.Str(r, "display_name"),
                    JsonHelpers.Str(r, "description"),
                    JsonHelpers.Str(r, "tool_use_id"),
                    JsonHelpers.CloneNonNull(r, "input") ?? JsonHelpers.EmptyInput,
                    JsonHelpers.CloneNonNull(r, "permission_suggestions"))
                {
                    AgentId = JsonHelpers.Str(r, "agent_id"),
                    DefaultToNo = JsonHelpers.Bool(r, "default_to_no"),
                    SuppressAlwaysAllowRule = JsonHelpers.Bool(r, "suppress_always_allow_rule"),
                },
            };
        }

        private static IReadOnlyList<ClaudeEvent> ParseControlResponse(JsonElement root)
        {
            var response = JsonHelpers.Obj(root, "response");
            if (response == null) return None;
            var r = response.Value;
            var requestId = JsonHelpers.Str(r, "request_id");
            if (requestId == null) return None;
            var body = JsonHelpers.CloneNonNull(r, "response");
            var events = new List<ClaudeEvent>
            {
                new ControlAcknowledged(requestId, JsonHelpers.Str(r, "subtype") == "error", body),
            };
            if (body != null) EventReader.ReadControlBody(body.Value, events);
            return events;
        }

        private static IReadOnlyList<ClaudeEvent> ParseResult(JsonElement root)
        {
            long? inputTokens = null;
            long? outputTokens = null;
            var usage = JsonHelpers.Obj(root, "usage");
            if (usage != null)
            {
                var u = usage.Value;
                var parts = new[]
                {
                    JsonHelpers.Long(u, "input_tokens"),
                    JsonHelpers.Long(u, "cache_creation_input_tokens"),
                    JsonHelpers.Long(u, "cache_read_input_tokens"),
                };
                long sum = 0;
                var any = false;
                foreach (var part in parts)
                {
                    if (part == null) continue;
                    sum += part.Value;
                    any = true;
                }
                if (any) inputTokens = sum;
                outputTokens = JsonHelpers.Long(u, "output_tokens");
            }

            return new ClaudeEvent[]
            {
                new TurnCompleted(
                    JsonHelpers.Str(root, "subtype") ?? "",
                    JsonHelpers.Bool(root, "is_error"),
                    JsonHelpers.Long(root, "duration_ms"),
                    JsonHelpers.Double(root, "total_cost_usd"),
                    inputTokens,
                    outputTokens),
            };
        }
    }
}
