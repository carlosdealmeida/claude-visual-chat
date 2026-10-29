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

    /// <summary>
    /// A hook of the claude began (<c>system/hook_started</c>): <paramref name="HookId"/> is its id, <paramref name="HookEvent"/> the
    /// event it answers (<c>SessionStart</c>, <c>Setup</c>, <c>PreToolUse</c>...).
    /// </summary>
    public sealed record HookStarted(string HookId, string HookEvent) : ClaudeEvent;

    /// <summary>A hook of the claude answered (<c>system/hook_response</c>); <paramref name="HookId"/> is the one a <see cref="HookStarted"/> gave.</summary>
    public sealed record HookFinished(string HookId) : ClaudeEvent;

    /// <summary>
    /// What became of a message written to the claude's stdin with a <c>uuid</c> (<c>command_lifecycle</c>, 2.1.310):
    /// <paramref name="CommandUuid"/> is that uuid; <paramref name="State"/> is <c>queued</c>, <c>started</c>, <c>completed</c>,
    /// <c>cancelled</c>, <c>discarded</c> or <c>refused</c>; <paramref name="RanAhead"/> is true for a command the claude ran at
    /// once, while it was working (<c>ran_ahead</c>), false when not told.
    /// </summary>
    public sealed record CommandLifecycle(string CommandUuid, string State, bool RanAhead) : ClaudeEvent;

    /// <summary>A complete assistant block (one block per line).</summary>
    public sealed record AssistantBlock(string MessageId, string? ParentToolUseId, ContentBlock Block) : ClaudeEvent
    {
        /// <summary>When the line was written (its <c>timestamp</c>); null when the line has none.</summary>
        public DateTimeOffset? Timestamp { get; init; }

        /// <summary>How long a thought took (the session file's <c>thinkingDurationMs</c>); null when not told.</summary>
        public long? ThinkingDurationMs { get; init; }

        /// <summary>The line's <c>uuid</c>; null when the line has none.</summary>
        public string? Uuid { get; init; }
    }

    public sealed record UserBlock(
        string? ParentToolUseId, ContentBlock Block, JsonElement? ToolUseResult, bool IsMeta, bool IsCompactSummary)
        : ClaudeEvent
    {
        /// <summary>When the line was written (its <c>timestamp</c>); null when the line has none.</summary>
        public DateTimeOffset? Timestamp { get; init; }

        /// <summary>The images that came with the text; empty by default.</summary>
        public IReadOnlyList<ImageBlock> Images { get; init; } = Array.Empty<ImageBlock>();

        /// <summary>The line is the claude's own (<c>isSynthetic</c>), not something the user wrote: after a compaction, the summary comes so.</summary>
        public bool IsSynthetic { get; init; }

        /// <summary>
        /// Where the line says it came from (<c>origin</c>): another session, a channel, a task. A session file writes it on the line of a
        /// task's notice (<c>task-notification</c>), and the claude writes it when it replays a message. Null when the line says nothing.
        /// </summary>
        public MessageOrigin? Origin { get; init; }
    }

    /// <summary>
    /// The <c>origin</c> of a user line: <paramref name="Kind"/> is <c>human</c>, <c>peer</c>, <c>channel</c>, <c>task-notification</c>,
    /// <c>coordinator</c>, <c>plugin</c>…
    /// </summary>
    public sealed record MessageOrigin(string Kind)
    {
        /// <summary>The sender's name (<c>name</c>), for a message from another session; null when not told.</summary>
        public string? Name { get; init; }

        /// <summary>The task of the sender (<c>senderTaskId</c>), when the sender is an agent of this same session; null otherwise.</summary>
        public string? SenderTaskId { get; init; }

        /// <summary>A narrower kind (<c>subkind</c>); null when not told.</summary>
        public string? Subkind { get; init; }
    }

    /// <summary>A streamed block opened. A <c>tool_use</c> starts with an empty <c>{}</c> input.</summary>
    public sealed record BlockStarted(string MessageId, int Index, string? ParentToolUseId, ContentBlock Block) : ClaudeEvent;

    public sealed record BlockDelta(string MessageId, int Index, string? ParentToolUseId, DeltaKind Kind, string Text) : ClaudeEvent
    {
        /// <summary>For a thought: the tokens it has used so far, as the delta tells (<c>estimated_tokens</c>); null when not told.</summary>
        public long? EstimatedTokens { get; init; }
    }

    /// <summary>
    /// How many tokens the claude has thought so far in the block that is arriving (<c>system/thinking_tokens</c>): the
    /// absolute count, which only grows within a block.
    /// </summary>
    public sealed record ThinkingTokens(long EstimatedTokens) : ClaudeEvent;

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

    public sealed record ControlAcknowledged(string RequestId, bool IsError, JsonElement? Response) : ClaudeEvent
    {
        /// <summary>The text of an error reply (<c>error</c>); null for a success, or when the reply has none.</summary>
        public string? Error { get; init; }
    }

    /// <summary><paramref name="InputTokens"/> adds the cache creation and cache read tokens.</summary>
    public sealed record TurnCompleted(
        string Subtype, bool IsError, long? DurationMs, double? CostUsd, long? InputTokens, long? OutputTokens)
        : ClaudeEvent;

    /// <summary>Marker for <c>compact_boundary</c> and <c>api_retry</c>.</summary>
    public sealed record SystemNotice(string Subtype) : ClaudeEvent
    {
        /// <summary>For <c>compact_boundary</c>: what started the compaction, <c>manual</c> or <c>auto</c> (<c>compact_metadata.trigger</c>); null when not told.</summary>
        public string? Trigger { get; init; }

        /// <summary>For <c>compact_boundary</c>: the tokens the conversation held before (<c>pre_tokens</c>); null when not told.</summary>
        public long? PreTokens { get; init; }

        /// <summary>For <c>compact_boundary</c>: the tokens it holds after (<c>post_tokens</c>); null when not told.</summary>
        public long? PostTokens { get; init; }
    }

    /// <summary>
    /// A line the claude says about its own work (<c>system/informational</c>). <paramref name="Level"/> is <c>info</c>, <c>warning</c> or
    /// <c>error</c> (<c>info</c> when the line does not say); a hook that blocked the prompt sends a <c>warning</c> whose text is
    /// "UserPromptSubmit operation blocked by hook:", the reason and "Original prompt: …".
    /// </summary>
    public sealed record Informational(string Level, string Text) : ClaudeEvent;

    /// <summary>The slash commands the session offers (from <c>initialize</c> or <c>commands_changed</c>).</summary>
    public sealed record CommandsAvailable(IReadOnlyList<ChatCommand> Commands) : ClaudeEvent;

    public sealed record ModelsAvailable(IReadOnlyList<ChatModel> Models) : ClaudeEvent;

    /// <summary>The settings in force (from <c>get_settings</c>). <paramref name="HasEffort"/> is false when <c>effort</c> is absent.</summary>
    public sealed record SettingsApplied(string? Model, string? Effort, bool HasEffort) : ClaudeEvent;

    /// <summary>A reply used the prompt cache. <paramref name="Ttl"/> is null when the line does not say how long it lives.</summary>
    public sealed record PromptCacheUsed(TimeSpan? Ttl, DateTimeOffset? At) : ClaudeEvent;

    public sealed record ContextUsageReported(ContextUsage Usage) : ClaudeEvent;

    public sealed record UsageLimitsReported(UsageLimits Limits) : ClaudeEvent;

    /// <summary>The login, from the <c>account</c> of the <c>initialize</c> reply.</summary>
    public sealed record AccountReported(AccountInfo Account) : ClaudeEvent;

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
        : ClaudeEvent
    {
        /// <summary>What the agent was asked to do (the line's <c>prompt</c>); null when the line does not say.</summary>
        public string? Prompt { get; init; }
    }

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
