using System;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClaudeVisualChat;

/// <summary>A tool call waiting for the user's yes or no.</summary>
public sealed class PendingPermission
{
    public PendingPermission(
        string requestId, string toolName, string displayName, string? description, string? toolUseId,
        string summary, JsonElement input, JsonElement? suggestions)
    {
        RequestId = requestId ?? "";
        ToolName = toolName ?? "";
        DisplayName = string.IsNullOrEmpty(displayName) ? ToolName : displayName;
        Description = description;
        ToolUseId = toolUseId;
        Summary = summary ?? "";
        Input = input;
        Suggestions = suggestions;
    }

    public string RequestId { get; }

    public string ToolName { get; }

    public string DisplayName { get; }

    public string? Description { get; }

    public string? ToolUseId { get; }

    public string Summary { get; }

    public JsonElement Input { get; }

    /// <summary>The claude's <c>permission_suggestions</c>, sent back on "allow always".</summary>
    public JsonElement? Suggestions { get; }

    /// <summary>The subagent that asks (the claude's <c>agent_id</c>, the agent's task id); null when the main conversation asks.</summary>
    public string? AgentId { get; init; }

    /// <summary>
    /// The claude asks to start on "No" (<c>default_to_no</c>): the keyboard goes to "No", and the digits do not answer.
    /// False when not told.
    /// </summary>
    public bool DefaultToNo { get; init; }

    /// <summary>
    /// The claude offers no rule to remember (<c>suppress_always_allow_rule</c>), so there is no "allow always"
    /// (<see cref="CanAllowAlways"/> is false), whatever the suggestions. False when not told.
    /// </summary>
    public bool SuppressAlwaysAllowRule { get; init; }

    /// <summary>Why the claude asks (<c>decision_reason</c>), as it came; null when not told. See <see cref="AskReason"/> and <see cref="HookReason"/>.</summary>
    public string? DecisionReason { get; init; }

    /// <summary>What decided to ask (<c>decision_reason_type</c>): <c>hook</c>, <c>safetyCheck</c>, <c>subcommandResults</c>, <c>other</c>…; null when not told.</summary>
    public string? DecisionReasonType { get; init; }

    /// <summary>A code for the reason (<c>decision_reason_code</c>), such as <c>classifier_transcript_too_long</c>; null when not told.</summary>
    public string? DecisionReasonCode { get; init; }

    /// <summary>Where the MCP server of the tool comes from (<c>mcp_server.source</c>), <c>claudeai</c> for a claude.ai connector; null when not told.</summary>
    public string? McpServerSource { get; init; }

    /// <summary>
    /// Why the claude asks, ready to show under the desktop card's title, as the extension shows it; null when there is
    /// nothing to show. It is the reason of a safety check (<see cref="DecisionReasonType"/> <c>safetyCheck</c>) or of a
    /// compound command (<c>subcommandResults</c>), and that of a claude.ai connector (type <c>other</c>, <see cref="McpServerSource"/>
    /// <c>claudeai</c>, an <c>mcp__</c> tool, a code other than <c>classifier_transcript_too_long</c>, and a reason that is a
    /// sentence, not a word followed by a colon), whose line breaks become one space first. The text has no control or invisible
    /// characters, is trimmed, and each line break is a ⏎.
    /// </summary>
    public string? AskReason
    {
        get
        {
            var reason = DecisionReason;
            if (reason == null) return null;
            var type = DecisionReasonType;
            if (type != "safetyCheck" && type != "subcommandResults")
            {
                if (!FromClaudeAiConnector) return null;
                reason = LineBreaksAround.Replace(reason, " ");
            }
            return Shown(reason);
        }
    }

    /// <summary>
    /// What a hook said when it asked (<see cref="DecisionReasonType"/> <c>hook</c>), ready to show under "A hook says:"; null when
    /// there is nothing to show. Trimmed, cut at <see cref="MaxHookReasonLength"/> characters with "… [+N chars]", without control or
    /// invisible characters, each line break a ⏎.
    /// </summary>
    public string? HookReason =>
        DecisionReasonType == "hook" && DecisionReason is { } reason ? Shown(Cut(reason.Trim(), MaxHookReasonLength)) : null;

    /// <summary>The most of a hook's reason the card shows (the extension's 2000).</summary>
    internal const int MaxHookReasonLength = 2000;

    /// <summary>A line break and the spaces around it: one space in a connector's reason.</summary>
    private static readonly Regex LineBreaksAround = new Regex(@"\s*\n\s*", RegexOptions.CultureInvariant);

    /// <summary>A reason that is a code (a word, then a colon or nothing): not shown for a connector.</summary>
    private static readonly Regex CodeLike = new Regex(@"^[A-Za-z0-9_-]+(:|\z)", RegexOptions.CultureInvariant);

    /// <summary>The reason is a claude.ai connector's, worth showing (see <see cref="AskReason"/>).</summary>
    private bool FromClaudeAiConnector =>
        DecisionReasonType == "other"
        && McpServerSource == "claudeai"
        && ToolName.StartsWith("mcp__", StringComparison.Ordinal)
        && DecisionReasonCode != "classifier_transcript_too_long"
        && !CodeLike.IsMatch(DecisionReason ?? "");

    /// <summary>The text as the card shows it: no invisible characters, trimmed, a ⏎ for each line break; null when nothing is left.</summary>
    private static string? Shown(string text)
    {
        var clean = InvisibleCharacters.Clean(text).Text.Trim(); // its line breaks are line feeds now
        return clean.Length == 0 ? null : clean.Replace('\n', '⏎');
    }

    /// <summary>The text cut at <paramref name="max"/> characters, not in the middle of a pair, with how much was left out.</summary>
    private static string Cut(string text, int max)
    {
        if (text.Length <= max) return text;
        var length = char.IsHighSurrogate(text[max - 1]) ? max - 1 : max;
        return text.Substring(0, length) + "… [+" + (text.Length - length).ToString(CultureInfo.InvariantCulture) + " chars]";
    }

    /// <summary>"Allow always" is offered: the claude sent suggestions and did not suppress the rule.</summary>
    public bool CanAllowAlways =>
        !SuppressAlwaysAllowRule
        && Suggestions is { ValueKind: JsonValueKind.Array } suggestions && suggestions.GetArrayLength() > 0;

    /// <summary>
    /// True when every suggestion sent back on "allow always" lasts only for this conversation
    /// (<c>"destination": "session"</c>); a rule kept in the settings (<c>localSettings</c>, <c>userSettings</c>…), or
    /// one that does not say where it goes, outlives it.
    /// </summary>
    internal bool AllowAlwaysStaysInSession
    {
        get
        {
            if (!CanAllowAlways) return false;
            foreach (var suggestion in Suggestions!.Value.EnumerateArray())
            {
                if (suggestion.ValueKind != JsonValueKind.Object
                    || !suggestion.TryGetProperty("destination", out var destination)
                    || destination.ValueKind != JsonValueKind.String
                    || destination.GetString() != "session")
                    return false;
            }
            return true;
        }
    }
}
