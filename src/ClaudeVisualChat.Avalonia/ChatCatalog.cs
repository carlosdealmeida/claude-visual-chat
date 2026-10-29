using System;
using System.Collections.Generic;

namespace ClaudeVisualChat
{
    /// <summary>A slash command the session offers. <paramref name="Name"/> has no leading slash.</summary>
    public sealed record ChatCommand(string Name, string Description, string ArgumentHint);

    /// <summary>A model the session offers. <paramref name="EffortLevels"/> is empty when the model takes no effort.</summary>
    public sealed record ChatModel(
        string Value, string ResolvedModel, string DisplayName, string Description, IReadOnlyList<string> EffortLevels);

    /// <summary>One slice of the context window. <paramref name="Kind"/> is <c>used</c>, <c>deferred</c> or <c>free</c>.</summary>
    public sealed record ContextCategory(string Name, long Tokens, string Kind);

    /// <summary>A memory file the claude loaded into the context; <paramref name="Path"/> is as the claude wrote it.</summary>
    public sealed record ContextFile(string Path, long Tokens);

    /// <summary>A custom agent whose description is in the context.</summary>
    public sealed record ContextAgent(string Name, long Tokens);

    public sealed record ContextUsage(
        long TotalTokens, long MaxTokens, int Percentage, IReadOnlyList<ContextCategory> Categories)
    {
        /// <summary>The model the window is for, when the claude says; null otherwise.</summary>
        public string? Model { get; init; }

        /// <summary>The memory files in the context, when the claude lists them; empty otherwise.</summary>
        public IReadOnlyList<ContextFile> MemoryFiles { get; init; } = Array.Empty<ContextFile>();

        /// <summary>The custom agents in the context, when the claude lists them; empty otherwise.</summary>
        public IReadOnlyList<ContextAgent> Agents { get; init; } = Array.Empty<ContextAgent>();
    }

    /// <summary>A rate limit window. <paramref name="Name"/> is its key (<c>five_hour</c>, <c>seven_day</c>); <paramref name="Utilization"/> runs from 0 to 1.</summary>
    public sealed record UsageWindow(string Name, double Utilization, DateTimeOffset? ResetsAt)
    {
        /// <summary>The model a per-model window (<c>model_scoped</c>) is for; null for the others.</summary>
        public string? Model { get; init; }
    }

    /// <summary>The limit windows. A reply to <c>get_usage</c> is a whole snapshot and replaces every window; a rate limit event only replaces the windows it names.</summary>
    public sealed record UsageLimits(IReadOnlyList<UsageWindow> Windows)
    {
        /// <summary>True for a <c>get_usage</c> reply: the conversation then drops windows the reply no longer lists.</summary>
        internal bool IsSnapshot { get; init; }
    }

    /// <summary>
    /// The login the claude reported when it started. Each part is null when the claude does not say. <paramref name="AuthMethod"/> is a
    /// code (<c>claudeai</c>, <c>api-key</c>, <c>bedrock</c>, <c>vertex</c>, <c>foundry</c>, <c>gateway</c> or <c>third-party</c>), null for none.
    /// The list of codes may grow: a host should treat a code it does not know as none (the panel shows no row for it).
    /// <paramref name="Plan"/> is the plan's product name, ready to show ("Max", "Enterprise Plus") and not translated; the claude's
    /// <c>subscriptionType</c> is turned into it when the account is read, with each word in capitals, and "none" gives null.
    /// </summary>
    public sealed record AccountInfo(string? AuthMethod, string? Email, string? Organization, string? Plan);
}
