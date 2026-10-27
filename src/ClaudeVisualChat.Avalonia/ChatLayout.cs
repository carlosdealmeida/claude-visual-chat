namespace ClaudeVisualChat;

/// <summary>
/// How <see cref="Controls.ClaudeChatView"/> lays itself out: for a window and a mouse, as the Claude Code extension
/// for VS Code does, or for a finger on a phone, in the style of Anthropic's Claude app.
/// </summary>
public enum ChatLayout
{
    /// <summary>The extension's layout: the timeline with its dots, the sizes of a desktop editor. The default.</summary>
    Desktop,

    /// <summary>
    /// The phone's layout: larger text, no timeline, the user's message in a bubble at the right, each tool call in a
    /// block of its own, targets of at least 44 pixels.
    /// </summary>
    Touch,
}
