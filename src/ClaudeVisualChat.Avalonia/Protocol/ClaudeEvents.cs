using System;
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
        : ClaudeEvent;

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
    }

    /// <summary>A streamed block opened. A <c>tool_use</c> starts with an empty <c>{}</c> input.</summary>
    public sealed record BlockStarted(string MessageId, int Index, string? ParentToolUseId, ContentBlock Block) : ClaudeEvent;

    public sealed record BlockDelta(string MessageId, int Index, string? ParentToolUseId, DeltaKind Kind, string Text) : ClaudeEvent;

    public sealed record BlockStopped(string MessageId, int Index, string? ParentToolUseId) : ClaudeEvent;

    public sealed record PermissionRequested(
        string RequestId, string ToolName, string? DisplayName, string? Description, string? ToolUseId,
        JsonElement Input, JsonElement? Suggestions)
        : ClaudeEvent;

    public sealed record PermissionCancelled(string RequestId) : ClaudeEvent;

    public sealed record ControlAcknowledged(string RequestId, bool IsError, JsonElement? Response) : ClaudeEvent;

    /// <summary><paramref name="InputTokens"/> adds the cache creation and cache read tokens.</summary>
    public sealed record TurnCompleted(
        string Subtype, bool IsError, long? DurationMs, double? CostUsd, long? InputTokens, long? OutputTokens)
        : ClaudeEvent;

    /// <summary>Marker for <c>compact_boundary</c> and <c>api_retry</c>.</summary>
    public sealed record SystemNotice(string Subtype) : ClaudeEvent;
}
