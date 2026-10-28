using System.Text.Json;

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
