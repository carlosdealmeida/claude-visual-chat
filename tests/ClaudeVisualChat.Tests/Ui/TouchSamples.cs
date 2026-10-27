using System;
using System.Collections.Generic;
using ClaudeVisualChat.Protocol;

namespace ClaudeVisualChat.Tests.Ui;

/// <summary>What the touch layout's tests show: the models and the context usage of a real session, and a conversation with them.</summary>
internal static class TouchSamples
{
    private static readonly string[] AllLevels = { "low", "medium", "high", "xhigh", "max" };

    /// <summary>
    /// The claude's list of tests/fixtures/effort.stream.ndjson, without its <c>default</c> entry, in its order: the five
    /// levels on the models that take an effort, its own (English) descriptions.
    /// </summary>
    public static readonly IReadOnlyList<ChatModel> Models = new[]
    {
        new ChatModel("opus", "claude-opus-5-5", "Opus 5.5", "For complex work and everyday tasks", AllLevels),
        new ChatModel("claude-fable-5-1", "claude-fable-5-1", "Fable 5.1", "For your toughest challenges", AllLevels),
        new ChatModel("sonnet", "claude-sonnet-5-5", "Sonnet 5.5", "Most efficient for simpler tasks", AllLevels),
        new ChatModel("haiku", "claude-haiku-4-5-20251001", "Haiku 4.5", "Fastest for quick answers", Array.Empty<string>()),
        new ChatModel("claude-sonnet-5", "claude-sonnet-5", "Sonnet 5", "Efficient for routine tasks", AllLevels),
        new ChatModel("claude-opus-5", "claude-opus-5", "Opus 5", "Best for everyday, complex tasks", AllLevels),
        new ChatModel("claude-fable-5", "claude-fable-5", "Fable 5", "Most capable for your hardest and longest-running tasks", AllLevels),
        new ChatModel("claude-opus-4-8", "claude-opus-4-8", "Opus 4.8", "Best for everyday, complex tasks", AllLevels),
    };

    /// <summary>The context of a long session: 150,600 of 1,000,000 tokens (15 %), and what the claude counts it from.</summary>
    public static readonly ContextUsage Context = new ContextUsage(150_600, 1_000_000, 15, new[]
    {
        new ContextCategory("Messages", 113_400, "used"),
        new ContextCategory("System tools", 20_400, "used"),
        new ContextCategory("Skills", 9_900, "used"),
        new ContextCategory("Free space", 816_400, "free"),
        new ContextCategory("MCP tools (deferred)", 127_500, "deferred"),
    });

    /// <summary>A conversation on Opus 5.5 with a high effort and the models of <see cref="Models"/> to choose from.</summary>
    public static ChatConversation Opus()
    {
        var conversation = new ChatConversation { AvailableModels = Models };
        conversation.Apply(new SessionStarted("s1", "opus", null, "default", "2.1.310"));
        conversation.Apply(new SettingsApplied(null, "high", true));
        return conversation;
    }
}
