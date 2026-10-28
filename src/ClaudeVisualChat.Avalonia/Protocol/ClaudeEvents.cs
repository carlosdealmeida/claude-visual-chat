using System;
using System.Collections.Generic;
using System.Text.Json;

namespace ClaudeVisualChat.Protocol
{
    /// <summary>Which stream of a content block a delta extends.</summary>
    public enum DeltaKind
    {
        Text,
        Thinking,
        InputJson,
    }

    /// <summary>A typed event read from one line of stream-json output or of a session file.</summary>
    public abstract record ClaudeEvent;

    public sealed record SessionStarted(
        string? SessionId, string? Model, string? WorkingDirectory, string? PermissionMode, string? ClaudeVersion)
        : ClaudeEvent
    {
        /// <summary>The commands the terminal handles itself (<c>terminal_slash_commands</c>); empty when not told.</summary>
        public IReadOnlyList<string> TerminalCommands { get; init; } = Array.Empty<string>();
    }

    public sealed record StatusChanged(string? Status, string? PermissionMode) : ClaudeEvent;

    /// <summary>A complete assistant block (one block per line).</summary>
    public sealed record AssistantBlock(string MessageId, string? ParentToolUseId, ContentBlock Block) : ClaudeEvent
    {
        /// <summary>When the line was written (its <c>timestamp</c>); null when the line has none.</summary>
        public DateTimeOffset? Timestamp { get; init; }

        /// <summary>How long a thought took (the session file's <c>thinkingDurationMs</c>); null when not told.</summary>
        public long? ThinkingDurationMs { get; init; }
    }

    public sealed record UserBlock(
        string? ParentToolUseId, ContentBlock Block, JsonElement? ToolUseResult, bool IsMeta, bool IsCompactSummary)
        : ClaudeEvent
    {
        /// <summary>When the line was written (its <c>timestamp</c>); null when the line has none.</summary>
        public DateTimeOffset? Timestamp { get; init; }

        /// <summary>The images that came with the text; empty by default.</summary>
        public IReadOnlyList<ImageBlock> Images { get; init; } = Array.Empty<ImageBlock>();
    }

    /// <summary>A streamed block opened. A <c>tool_use</c> starts with an empty <c>{}</c> input.</summary>
    public sealed record BlockStarted(string MessageId, int Index, string? ParentToolUseId, ContentBlock Block) : ClaudeEvent;

    public sealed record BlockDelta(string MessageId, int Index, string? ParentToolUseId, DeltaKind Kind, string Text) : ClaudeEvent;

    public sealed record BlockStopped(string MessageId, int Index, string? ParentToolUseId) : ClaudeEvent;

    public sealed record PermissionRequested(
        string RequestId, string ToolName, string? DisplayName, string? Description, string? ToolUseId,
        JsonElement Input, JsonElement? Suggestions)
        : ClaudeEvent
    {
        /// <summary>The subagent that asks (<c>agent_id</c>, its <c>task_id</c>); null when the main conversation asks.</summary>
        public string? AgentId { get; init; }

        /// <summary>The claude asks to put the keyboard on "No" (<c>default_to_no</c>); false when not told.</summary>
        public bool DefaultToNo { get; init; }

        /// <summary>The claude offers no rule to remember (<c>suppress_always_allow_rule</c>): no "allow always"; false when not told.</summary>
        public bool SuppressAlwaysAllowRule { get; init; }
    }

    public sealed record PermissionCancelled(string RequestId) : ClaudeEvent;

    public sealed record ControlAcknowledged(string RequestId, bool IsError, JsonElement? Response) : ClaudeEvent;

    /// <summary><paramref name="InputTokens"/> adds the cache creation and cache read tokens.</summary>
    public sealed record TurnCompleted(
        string Subtype, bool IsError, long? DurationMs, double? CostUsd, long? InputTokens, long? OutputTokens)
        : ClaudeEvent;

    /// <summary>Marker for <c>compact_boundary</c> and <c>api_retry</c>.</summary>
    public sealed record SystemNotice(string Subtype) : ClaudeEvent;

    /// <summary>The slash commands the session offers (from <c>initialize</c> or <c>commands_changed</c>).</summary>
    public sealed record CommandsAvailable(IReadOnlyList<ChatCommand> Commands) : ClaudeEvent;

    public sealed record ModelsAvailable(IReadOnlyList<ChatModel> Models) : ClaudeEvent;

    /// <summary>The settings in force (from <c>get_settings</c>). <paramref name="HasEffort"/> is false when <c>effort</c> is absent.</summary>
    public sealed record SettingsApplied(string? Model, string? Effort, bool HasEffort) : ClaudeEvent;

    /// <summary>A reply used the prompt cache. <paramref name="Ttl"/> is null when the line does not say how long it lives.</summary>
    public sealed record PromptCacheUsed(TimeSpan? Ttl, DateTimeOffset? At) : ClaudeEvent;

    public sealed record ContextUsageReported(ContextUsage Usage) : ClaudeEvent;

    public sealed record UsageLimitsReported(UsageLimits Limits) : ClaudeEvent;

    /// <summary>Remote Control moved (<c>bridge_state</c>) or started (<c>session_url</c>).</summary>
    public sealed record RemoteControlChanged(string? State, string? Url, string? Detail) : ClaudeEvent;

    /// <summary>A slash command the user ran; <paramref name="Name"/> has no leading slash.</summary>
    public sealed record CommandEcho(string Name, string Arguments) : ClaudeEvent;

    /// <summary>What a local slash command printed (the inside of <c>local-command-stdout</c>).</summary>
    public sealed record CommandOutput(string Text) : ClaudeEvent;

    /// <summary>
    /// A task started (<c>system/task_started</c>). <paramref name="TaskType"/> is <c>local_agent</c> for a subagent
    /// (<c>local_bash</c> for a command); empty when the line does not say.
    /// </summary>
    public sealed record AgentTaskStarted(
        string TaskId, string ToolUseId, string Description, string? AgentType, bool IsBackground, int Depth, string TaskType)
        : ClaudeEvent;

    /// <summary>A task used a tool (<c>system/task_progress</c>); <paramref name="Activity"/> says what it is doing.</summary>
    public sealed record AgentTaskProgress(
        string TaskId, string ToolUseId, string? Activity, long? Tokens, int? ToolUses, long? DurationMs)
        : ClaudeEvent;

    /// <summary>A task changed (<c>system/task_updated</c>); <paramref name="Status"/> is its patch's status, if any.</summary>
    public sealed record AgentTaskUpdated(string TaskId, string? Status) : ClaudeEvent;

    /// <summary>
    /// A task ended (<c>system/task_notification</c>, or the <c>&lt;task-notification&gt;</c> text of a session file).
    /// <paramref name="Status"/> is <c>completed</c>, <c>failed</c>, <c>killed</c>…; <paramref name="Summary"/> is
    /// what the agent answered.
    /// </summary>
    public sealed record AgentTaskFinished(
        string TaskId, string ToolUseId, string Status, string? Summary, long? Tokens, int? ToolUses, long? DurationMs)
        : ClaudeEvent;
}
