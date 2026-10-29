using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
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
        /// <summary>Trims the line and drops leading ANSI escapes; null when nothing JSON-like is left.</summary>
        public static string? Prepare(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            var text = AnsiText.TrimStart(line!.Trim()).Trim();
            return text.Length > 0 && text[0] == '{' ? text : null;
        }
    }

    /// <summary>The slash command markup the CLI writes into user text.</summary>
    internal static class CommandText
    {
        private static readonly Regex Output = new Regex(
            "<local-command-stdout>(.*?)</local-command-stdout>", RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>A <see cref="CommandEcho"/> or <see cref="CommandOutput"/> when the text is command markup; else null.</summary>
        public static ClaudeEvent? Read(string text)
        {
            var t = text.TrimStart();
            if (t.StartsWith("<command-message>", StringComparison.Ordinal)
                || t.StartsWith("<command-name>", StringComparison.Ordinal))
            {
                var name = Tag(t, "command-name") ?? Tag(t, "command-message") ?? "";
                return new CommandEcho(name.Trim().TrimStart('/'), Tag(t, "command-args") ?? "");
            }
            return t.StartsWith("<local-command-stdout>", StringComparison.Ordinal) ? ReadOutput(t) : null;
        }

        /// <summary>The inside of <c>local-command-stdout</c>; the whole text when it has no such tags; null when empty.</summary>
        public static CommandOutput? ReadOutput(string? text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var m = Output.Match(text!);
            return new CommandOutput(m.Success ? m.Groups[1].Value : text!);
        }

        private static string? Tag(string text, string tag)
        {
            var open = "<" + tag + ">";
            var start = text.IndexOf(open, StringComparison.Ordinal);
            if (start < 0) return null;
            start += open.Length;
            var end = text.IndexOf("</" + tag + ">", start, StringComparison.Ordinal);
            return end < 0 ? null : text.Substring(start, end - start);
        }
    }

    /// <summary>
    /// Reads the wrapper the claude puts around each long paste in a user message: a line
    /// <c>&lt;pasted_content id="abcd"&gt;</c> (four lowercase hex digits), the pasted text, and a line
    /// <c>&lt;/pasted_content id="abcd"&gt;</c> with the same id, with up to two line breaks set it off from the text around.
    /// The result is the message as the person saw it in the box: the pasted text in place of each wrapper.
    /// </summary>
    internal static class PastedContent
    {
        private const string OpenStart = "<pasted_content id=\"";
        private const string CloseStart = "\n</pasted_content id=\"";
        private const int IdLength = 4;
        private const int MaxBreaks = 2;

        /// <summary>
        /// Takes the wrappers out of <paramref name="text"/>. Returns the very same instance when there is none that is
        /// well formed. One pass: each wrapper is found once and skipped whole, and at the first one with no closing
        /// line the rest of the text stays as it is.
        /// </summary>
        internal static string Strip(string text)
        {
            System.Text.StringBuilder? result = null;
            var copied = 0; // everything before this index is already in result
            var search = 0;
            while (search < text.Length)
            {
                var open = text.IndexOf(OpenStart, search, StringComparison.Ordinal);
                if (open < 0) break;

                var idAt = open + OpenStart.Length;
                var bodyAt = idAt + IdLength + 3; // the id, the quote, the bracket and the line break
                if (!IsHead(text, idAt))
                {
                    search = open + 1;
                    continue;
                }

                var closing = CloseStart + text.Substring(idAt, IdLength) + "\">";
                var close = text.IndexOf(closing, bodyAt, StringComparison.Ordinal);
                if (close < 0) break;

                result ??= new System.Text.StringBuilder(text.Length);

                // what came before: without the line breaks the claude put in front of the wrapper
                var before = open;
                for (var cut = 0; cut < MaxBreaks && before > copied && text[before - 1] == '\n'; cut++) before--;
                result.Append(text, copied, before - copied);
                if (result.Length > 0 && !char.IsWhiteSpace(result[result.Length - 1])) result.Append('\n');

                result.Append(text, bodyAt, close - bodyAt);

                // what comes after: without the line breaks that follow, and one of its own when it starts a word
                var after = close + closing.Length;
                for (var cut = 0; cut < MaxBreaks && after < text.Length && text[after] == '\n'; cut++) after++;
                if (after < text.Length && !char.IsWhiteSpace(text[after])) result.Append('\n');

                copied = after;
                search = after;
            }

            if (result == null) return text;
            result.Append(text, copied, text.Length - copied);
            return result.ToString();
        }

        /// <summary>True when, from <paramref name="idAt"/>, the text reads <c>abcd"&gt;</c> and a line break.</summary>
        private static bool IsHead(string text, int idAt)
        {
            if (idAt + IdLength + 3 > text.Length) return false;
            for (var i = idAt; i < idAt + IdLength; i++)
            {
                var c = text[i];
                if (!(c >= '0' && c <= '9' || c >= 'a' && c <= 'f')) return false;
            }
            return text[idAt + IdLength] == '"' && text[idAt + IdLength + 1] == '>' && text[idAt + IdLength + 2] == '\n';
        }
    }

    internal static class EventReader
    {
        private static readonly IReadOnlyList<ClaudeEvent> None = Array.Empty<ClaudeEvent>();

        public static IReadOnlyList<ClaudeEvent> ReadUser(JsonElement root, string toolUseResultName, string? parent)
        {
            var message = JsonHelpers.Obj(root, "message");
            if (message == null) return None;
            var blocks = new List<ContentBlock>();
            var images = new List<ImageBlock>();
            foreach (var block in BlockReader.ReadContent(JsonHelpers.Prop(message.Value, "content")))
            {
                if (block is ImageBlock image) images.Add(image);
                else blocks.Add(block);
            }
            if (blocks.Count == 0 && images.Count == 0) return None;
            // Images ride with the first text; an image on its own gets an empty text to ride with.
            var carrier = blocks.FindIndex(b => b is TextContentBlock);
            if (carrier < 0 && images.Count > 0)
            {
                blocks.Insert(0, new TextContentBlock(""));
                carrier = 0;
            }

            var result = JsonHelpers.CloneNonNull(root, toolUseResultName);
            var isMeta = JsonHelpers.Bool(root, "isMeta");
            var isCompact = JsonHelpers.Bool(root, "isCompactSummary");
            var isSynthetic = JsonHelpers.Bool(root, "isSynthetic");
            var meta = JsonHelpers.Prop(root, "tool_result_meta"); // stream
            var denialKind = JsonHelpers.Str(root, "toolDenialKind"); // session file
            var timestamp = JsonHelpers.Time(root, "timestamp");
            var origin = ReadOrigin(root);
            var events = new List<ClaudeEvent>(blocks.Count);
            for (var i = 0; i < blocks.Count; i++)
            {
                var b = blocks[i];
                // the claude may wrap a long paste in a tag of its own; the message is shown as the person wrote it
                if (b is TextContentBlock pasted && PastedContent.Strip(pasted.Text) is var unwrapped && !ReferenceEquals(unwrapped, pasted.Text))
                    b = new TextContentBlock(unwrapped);
                if (b is TextContentBlock text && CommandText.Read(text.Text) is { } command)
                {
                    events.Add(command);
                    continue;
                }
                if (b is ToolResultBlock toolResult
                    && (NonExecutionKind(meta, toolResult.ToolUseId) ?? denialKind) is { } kind)
                    b = toolResult with { NonExecutionKind = kind };
                var user = new UserBlock(parent, b, result, isMeta, isCompact) { Timestamp = timestamp, IsSynthetic = isSynthetic, Origin = origin };
                events.Add(i == carrier && images.Count > 0 ? user with { Images = images } : user);
            }
            return events;
        }

        /// <summary>Where the line says it came from (<c>origin</c>); null when it says nothing, or no kind.</summary>
        private static MessageOrigin? ReadOrigin(JsonElement root)
        {
            var origin = JsonHelpers.Obj(root, "origin");
            if (origin == null || !(JsonHelpers.Str(origin.Value, "kind") is { Length: > 0 } kind)) return null;
            return new MessageOrigin(kind)
            {
                Name = JsonHelpers.Str(origin.Value, "name"),
                SenderTaskId = JsonHelpers.Str(origin.Value, "senderTaskId") ?? JsonHelpers.Str(origin.Value, "sender_task_id"),
                Subkind = JsonHelpers.Str(origin.Value, "subkind"),
            };
        }

        /// <summary>
        /// The <c>response</c> of a control reply, told by its shape: <c>commands</c>/<c>models</c> (initialize),
        /// <c>applied</c> (settings), <c>categories</c> with <c>totalTokens</c> (context usage), <c>session_url</c>.
        /// </summary>
        public static void ReadControlBody(JsonElement body, List<ClaudeEvent> events)
        {
            if (JsonHelpers.Arr(body, "commands") is { } commands)
                events.Add(new CommandsAvailable(ReadCommands(commands)));
            if (JsonHelpers.Arr(body, "models") is { } models)
                events.Add(new ModelsAvailable(ReadModels(models)));

            if (JsonHelpers.Obj(body, "applied") is { } applied)
            {
                var hasEffort = applied.TryGetProperty("effort", out _);
                events.Add(new SettingsApplied(JsonHelpers.Str(applied, "model"), JsonHelpers.Str(applied, "effort"), hasEffort));
            }

            if (JsonHelpers.Arr(body, "categories") is { } categories && JsonHelpers.Long(body, "totalTokens") is { } total)
            {
                var list = new List<ContextCategory>();
                foreach (var c in categories.EnumerateArray())
                {
                    list.Add(new ContextCategory(
                        JsonHelpers.Str(c, "name") ?? "", JsonHelpers.Long(c, "tokens") ?? 0, JsonHelpers.Str(c, "kind") ?? ""));
                }
                var percentage = JsonHelpers.Double(body, "percentage") ?? 0;
                events.Add(new ContextUsageReported(new ContextUsage(
                    total, JsonHelpers.Long(body, "maxTokens") ?? 0, (int)Math.Round(percentage), list)
                {
                    Model = JsonHelpers.Str(body, "model"),
                    MemoryFiles = ReadContextFiles(JsonHelpers.Arr(body, "memoryFiles")),
                    Agents = ReadContextAgents(JsonHelpers.Arr(body, "agents")),
                }));
            }

            if (JsonHelpers.Obj(body, "account") is { } account)
            {
                var read = new AccountInfo(
                    LoginOf(account),
                    JsonHelpers.Str(account, "email"),
                    JsonHelpers.Str(account, "organization"),
                    PlanOf(JsonHelpers.Str(account, "subscriptionType")));
                if (read.AuthMethod != null || read.Email != null || read.Organization != null || read.Plan != null)
                    events.Add(new AccountReported(read));
            }

            // the reply to get_usage, which the host passes on: the limits, 0 to 100, with ISO dates
            if (JsonHelpers.Obj(body, "rate_limits") is { } limits && ReadUsageReply(limits) is { Count: > 0 } windows)
                events.Add(new UsageLimitsReported(new UsageLimits(windows)));

            if (JsonHelpers.Str(body, "session_url") is { } url)
                events.Add(new RemoteControlChanged(null, url, null));
        }

        /// <summary>
        /// The login, as a code the strings write out (<c>claudeai</c>, <c>api-key</c>, <c>bedrock</c>, <c>vertex</c>, <c>foundry</c>), from
        /// the account's <c>apiProvider</c>, <c>tokenSource</c> and <c>apiKeySource</c>; null for none or for a value it does not know,
        /// so that a raw identifier is never shown.
        /// </summary>
        private static string? LoginOf(JsonElement account)
        {
            switch (JsonHelpers.Str(account, "apiProvider"))
            {
                case "bedrock": return "bedrock";
                case "vertex": return "vertex";
                case "foundry": return "foundry";
            }
            var token = JsonHelpers.Str(account, "tokenSource");
            if (token != null && (token == "claude.ai" || token.StartsWith("CLAUDE_CODE_OAUTH_TOKEN", StringComparison.Ordinal) || token == "CCR_OAUTH_TOKEN_FILE"))
                return "claudeai";
            var key = JsonHelpers.Str(account, "apiKeySource");
            return !string.IsNullOrEmpty(key) && key != "none" ? "api-key" : null;
        }

        /// <summary>The plan as the extension writes it: "max" is "Max", "enterprise_plus" is "Enterprise Plus"; none is null.</summary>
        private static string? PlanOf(string? subscription)
        {
            if (string.IsNullOrWhiteSpace(subscription) || subscription == "none") return null;
            var words = subscription!.Split(new[] { '_', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", words.Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));
        }

        private static List<UsageWindow> ReadUsageReply(JsonElement limits)
        {
            var list = new List<UsageWindow>();
            foreach (var name in new[] { "five_hour", "seven_day", "seven_day_sonnet" })
            {
                if (limits.TryGetProperty(name, out var window) && window.ValueKind == JsonValueKind.Object && ReadUsageWindow(name, window, null) is { } read)
                    list.Add(read);
            }
            if (JsonHelpers.Arr(limits, "model_scoped") is { } scoped)
            {
                foreach (var entry in scoped.EnumerateArray())
                {
                    var model = JsonHelpers.Str(entry, "display_name");
                    if (!string.IsNullOrEmpty(model) && ReadUsageWindow("model_scoped", entry, model) is { } read) list.Add(read);
                }
            }
            return list;
        }

        private static UsageWindow? ReadUsageWindow(string name, JsonElement window, string? model)
        {
            var utilization = JsonHelpers.Double(window, "utilization");
            if (utilization == null) return null;
            DateTimeOffset? resets = null;
            if (JsonHelpers.Str(window, "resets_at") is { } text
                && DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var at))
                resets = at;
            return new UsageWindow(name, utilization.Value / 100, resets) { Model = model };
        }

        private static IReadOnlyList<ContextFile> ReadContextFiles(JsonElement? array)
        {
            var list = new List<ContextFile>();
            if (array == null) return list;
            foreach (var f in array.Value.EnumerateArray())
            {
                if (JsonHelpers.Str(f, "path") is { Length: > 0 } path) list.Add(new ContextFile(path, JsonHelpers.Long(f, "tokens") ?? 0));
            }
            return list;
        }

        private static IReadOnlyList<ContextAgent> ReadContextAgents(JsonElement? array)
        {
            var list = new List<ContextAgent>();
            if (array == null) return list;
            foreach (var a in array.Value.EnumerateArray())
            {
                if ((JsonHelpers.Str(a, "agentType") ?? JsonHelpers.Str(a, "name")) is { Length: > 0 } name)
                    list.Add(new ContextAgent(name, JsonHelpers.Long(a, "tokens") ?? 0));
            }
            return list;
        }

        private static IReadOnlyList<ChatCommand> ReadCommands(JsonElement array)
        {
            var list = new List<ChatCommand>();
            foreach (var c in array.EnumerateArray())
            {
                var name = JsonHelpers.Str(c, "name");
                if (name == null) continue;
                list.Add(new ChatCommand(
                    name.TrimStart('/'), JsonHelpers.Str(c, "description") ?? "", JsonHelpers.Str(c, "argumentHint") ?? ""));
            }
            return list;
        }

        private static IReadOnlyList<ChatModel> ReadModels(JsonElement array)
        {
            var list = new List<ChatModel>();
            foreach (var m in array.EnumerateArray())
            {
                var value = JsonHelpers.Str(m, "value");
                if (value == null) continue;
                list.Add(new ChatModel(
                    value,
                    JsonHelpers.Str(m, "resolvedModel") ?? value,
                    JsonHelpers.Str(m, "displayName") ?? value,
                    JsonHelpers.Str(m, "description") ?? "",
                    JsonHelpers.Strings(m, "supportedEffortLevels")));
            }
            return list;
        }

        public static IReadOnlyList<ClaudeEvent> ReadRateLimit(JsonElement root)
        {
            var info = JsonHelpers.Obj(root, "rate_limit_info");
            var windows = info == null ? null : JsonHelpers.Obj(info.Value, "unifiedWindows");
            if (windows == null) return None;
            var list = new List<UsageWindow>();
            foreach (var window in windows.Value.EnumerateObject())
            {
                if (ReadWindow(window.Name, window.Value) is { } read) list.Add(read);
            }
            return list.Count == 0 ? None : new ClaudeEvent[] { new UsageLimitsReported(new UsageLimits(list)) };
        }

        private static UsageWindow? ReadWindow(string name, JsonElement window)
        {
            var utilization = JsonHelpers.Double(window, "utilization");
            if (utilization == null) return null;
            var resets = JsonHelpers.Long(window, "resetsAt");
            return new UsageWindow(name, utilization.Value, resets == null ? (DateTimeOffset?)null : DateTimeOffset.FromUnixTimeSeconds(resets.Value))
;
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
            var uuid = JsonHelpers.Str(root, "uuid");
            var events = new List<ClaudeEvent>();
            foreach (var block in BlockReader.ReadContent(content))
                events.Add(new AssistantBlock(id, parent, block) { Timestamp = timestamp, ThinkingDurationMs = thinking, Uuid = uuid });
            if (parent == null && JsonHelpers.Str(message.Value, "model") != "<synthetic>"
                && ReadCache(message.Value, timestamp) is { } cache)
                events.Add(cache);
            return events;
        }

        /// <summary>The cache a reply used: 1 h or 5 min from <c>cache_creation</c>, no lifetime when only read.</summary>
        private static PromptCacheUsed? ReadCache(JsonElement message, DateTimeOffset? at)
        {
            var usage = JsonHelpers.Obj(message, "usage");
            if (usage == null) return null;
            var u = usage.Value;
            var creation = JsonHelpers.Obj(u, "cache_creation");
            if (creation != null)
            {
                if ((JsonHelpers.Long(creation.Value, "ephemeral_1h_input_tokens") ?? 0) > 0)
                    return new PromptCacheUsed(TimeSpan.FromHours(1), at);
                if ((JsonHelpers.Long(creation.Value, "ephemeral_5m_input_tokens") ?? 0) > 0)
                    return new PromptCacheUsed(TimeSpan.FromMinutes(5), at);
            }
            return (JsonHelpers.Long(u, "cache_read_input_tokens") ?? 0) > 0
                   || (JsonHelpers.Long(u, "cache_creation_input_tokens") ?? 0) > 0
                ? new PromptCacheUsed(null, at)
                : null;
        }

        public static IReadOnlyList<ClaudeEvent> ReadNotice(JsonElement root)
        {
            var subtype = JsonHelpers.Str(root, "subtype");
            switch (subtype)
            {
                case "api_retry":
                    return new ClaudeEvent[] { new SystemNotice(subtype!) };
                case "hook_started":
                    return JsonHelpers.Str(root, "hook_id") is { } startedId && JsonHelpers.Str(root, "hook_event") is { } hookEvent
                        ? new ClaudeEvent[] { new HookStarted(startedId, hookEvent) }
                        : None;
                case "hook_response":
                    return JsonHelpers.Str(root, "hook_id") is { } finishedId
                        ? new ClaudeEvent[] { new HookFinished(finishedId) }
                        : None;
                case "compact_boundary":
                    {
                        // the stream says compact_metadata; a session file may say compactMetadata, or nothing
                        var meta = JsonHelpers.Obj(root, "compact_metadata") ?? JsonHelpers.Obj(root, "compactMetadata");
                        if (meta == null) return new ClaudeEvent[] { new SystemNotice(subtype!) };
                        var m = meta.Value;
                        return new ClaudeEvent[]
                        {
                            new SystemNotice(subtype!)
                            {
                                Trigger = JsonHelpers.Str(m, "trigger"),
                                PreTokens = JsonHelpers.Long(m, "pre_tokens") ?? JsonHelpers.Long(m, "preTokens"),
                                PostTokens = JsonHelpers.Long(m, "post_tokens") ?? JsonHelpers.Long(m, "postTokens"),
                            },
                        };
                    }
                case "thinking_tokens":
                    return JsonHelpers.Long(root, "estimated_tokens") is { } estimated
                        ? new ClaudeEvent[] { new ThinkingTokens(estimated) }
                        : None;
                case "commands_changed":
                    return JsonHelpers.Arr(root, "commands") is { } commands
                        ? new ClaudeEvent[] { new CommandsAvailable(ReadCommands(commands)) }
                        : None;
                case "bridge_state":
                    return new ClaudeEvent[]
                    {
                        new RemoteControlChanged(JsonHelpers.Str(root, "state"), null, JsonHelpers.Str(root, "detail")),
                    };
                case "local_command":
                    return CommandText.ReadOutput(JsonHelpers.Str(root, "content")) is { } output
                        ? new ClaudeEvent[] { output }
                        : None;
                case "informational":
                    // a session file keeps the line without its text: nothing to show
                    return JsonHelpers.Str(root, "content") is { Length: > 0 } content
                        ? new ClaudeEvent[] { new Informational(JsonHelpers.Str(root, "level") ?? "info", content) }
                        : None;
                case "task_started":
                case "task_progress":
                case "task_updated":
                case "task_notification":
                    return ReadTask(subtype!, root);
                default:
                    return None;
            }
        }

        /// <summary>A <c>system/task_*</c> line; none without a <c>task_id</c>.</summary>
        private static IReadOnlyList<ClaudeEvent> ReadTask(string subtype, JsonElement root)
        {
            var taskId = JsonHelpers.Str(root, "task_id");
            if (string.IsNullOrEmpty(taskId)) return None;
            var toolUseId = JsonHelpers.Str(root, "tool_use_id") ?? "";
            var usage = JsonHelpers.Obj(root, "usage");
            long? tokens = usage == null ? null : JsonHelpers.Long(usage.Value, "total_tokens");
            var toolUses = usage == null ? null : JsonHelpers.Long(usage.Value, "tool_uses");
            long? duration = usage == null ? null : JsonHelpers.Long(usage.Value, "duration_ms");
            ClaudeEvent e;
            switch (subtype)
            {
                case "task_started":
                    e = new AgentTaskStarted(
                        taskId!, toolUseId, JsonHelpers.Str(root, "description") ?? "", JsonHelpers.Str(root, "subagent_type"),
                        JsonHelpers.Bool(root, "is_backgrounded"), (int)(JsonHelpers.Long(root, "spawn_depth") ?? 1),
                        JsonHelpers.Str(root, "task_type") ?? "")
                    {
                        Prompt = JsonHelpers.Str(root, "prompt"),
                    };
                    break;
                case "task_progress":
                    e = new AgentTaskProgress(
                        taskId!, toolUseId, JsonHelpers.Str(root, "description"), tokens, (int?)toolUses, duration);
                    break;
                case "task_updated":
                    var patch = JsonHelpers.Obj(root, "patch");
                    e = new AgentTaskUpdated(taskId!, patch == null ? null : JsonHelpers.Str(patch.Value, "status"));
                    break;
                default:
                    e = new AgentTaskFinished(
                        taskId!, toolUseId, JsonHelpers.Str(root, "status") ?? "", JsonHelpers.Str(root, "summary"),
                        tokens, (int?)toolUses, duration);
                    break;
            }
            return new[] { e };
        }
    }

    /// <summary>
    /// The <c>&lt;task-notification&gt;</c> text a session file keeps as a user message when a background task ends:
    /// tags <c>task-id</c>, <c>tool-use-id</c>, <c>status</c>, <c>summary</c>, <c>result</c> and <c>usage</c> (with
    /// <c>subagent_tokens</c>, <c>tool_uses</c> and <c>duration_ms</c>).
    /// </summary>
    internal static class TaskNotificationText
    {
        public const string Prefix = "<task-notification>";

        /// <summary>
        /// The task that ended, with the <c>result</c> (else the <c>summary</c>) as its summary; null when the text is
        /// not a notification or has no task id.
        /// </summary>
        public static AgentTaskFinished? Read(string text)
        {
            if (text == null || !text.TrimStart().StartsWith(Prefix, StringComparison.Ordinal)) return null;
            // the result is the agent's own text and may hold anything: the tags before it are read from the start,
            // the result up to its last closing tag, and the usage that follows it from the end
            var taskId = Tag(text, "task-id", false)?.Trim();
            if (string.IsNullOrEmpty(taskId)) return null;
            var usage = Tag(text, "usage", true) ?? "";
            return new AgentTaskFinished(
                taskId!,
                Tag(text, "tool-use-id", false)?.Trim() ?? "",
                Tag(text, "status", false)?.Trim() ?? "",
                Tag(text, "result", true) ?? Tag(text, "summary", false),
                Number(usage, "subagent_tokens"),
                (int?)Number(usage, "tool_uses"),
                Number(usage, "duration_ms"));
        }

        private static long? Number(string text, string tag) =>
            long.TryParse(Tag(text, tag, false)?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                ? n
                : (long?)null;

        /// <summary>
        /// The inside of a tag: from its first opening to the next closing, or, with <paramref name="toLastClose"/>,
        /// to the last closing (for the result, whose text may hold the same tag). The usage, after the result, opens
        /// at its last opening.
        /// </summary>
        private static string? Tag(string text, string tag, bool toLastClose)
        {
            var open = "<" + tag + ">";
            var close = "</" + tag + ">";
            var start = tag == "usage"
                ? text.LastIndexOf(open, StringComparison.Ordinal)
                : text.IndexOf(open, StringComparison.Ordinal);
            if (start < 0) return null;
            start += open.Length;
            var end = toLastClose
                ? text.LastIndexOf(close, StringComparison.Ordinal)
                : text.IndexOf(close, start, StringComparison.Ordinal);
            return end < start ? null : text.Substring(start, end - start);
        }
    }
}
