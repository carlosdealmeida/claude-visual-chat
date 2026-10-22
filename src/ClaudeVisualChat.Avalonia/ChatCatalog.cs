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

    public sealed record ContextUsage(
        long TotalTokens, long MaxTokens, int Percentage, IReadOnlyList<ContextCategory> Categories);

    /// <summary>A rate limit window. <paramref name="Name"/> is its key (<c>five_hour</c>, <c>seven_day</c>); <paramref name="Utilization"/> runs from 0 to 1.</summary>
    public sealed record UsageWindow(string Name, double Utilization, DateTimeOffset? ResetsAt);

    public sealed record UsageLimits(IReadOnlyList<UsageWindow> Windows);
}
