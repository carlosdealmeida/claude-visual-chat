using System;

namespace ClaudeVisualChat;

/// <summary>The user's answer to a permission request.</summary>
public enum PermissionDecision
{
    Allow,
    AllowAlways,
    Deny,
}

public sealed class PermissionAnsweredEventArgs : EventArgs
{
    public PermissionAnsweredEventArgs(PendingPermission request, PermissionDecision decision, string? message)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        Decision = decision;
        Message = message;
    }

    public PendingPermission Request { get; }

    public PermissionDecision Decision { get; }

    /// <summary>What to tell Claude on a <see cref="PermissionDecision.Deny"/>.</summary>
    public string? Message { get; }
}

/// <summary>
/// A message the user sent; give it back to <see cref="ChatConversation.UndoSubmit(MessageSubmittedEventArgs)"/> when
/// it cannot be delivered.
/// </summary>
public sealed class MessageSubmittedEventArgs : EventArgs
{
    public MessageSubmittedEventArgs(string text)
    {
        Text = text ?? "";
    }

    public string Text { get; }

    /// <summary>The conversation that sent it; null for one made by hand.</summary>
    internal ChatConversation? Owner { get; set; }

    /// <summary>The message as shown; null when the conversation does not echo what is sent.</summary>
    internal UserMessageItem? Item { get; set; }

    internal bool Undone { get; set; }
}

/// <summary>A permission mode the user asked for, not yet confirmed by the claude.</summary>
public sealed class PermissionModeRequestedEventArgs : EventArgs
{
    public PermissionModeRequestedEventArgs(string mode)
    {
        Mode = mode ?? "";
    }

    /// <summary>The mode asked for (<c>plan</c>, <c>default</c>, <c>acceptEdits</c>, <c>auto</c>…).</summary>
    public string Mode { get; }
}

/// <summary>A model the user asked for, not yet confirmed by the claude.</summary>
public sealed class ModelRequestedEventArgs : EventArgs
{
    public ModelRequestedEventArgs(string model)
    {
        Model = model ?? "";
    }

    /// <summary>The model asked for, as the claude takes it (<c>sonnet</c>, <c>opus</c>, a full model id…).</summary>
    public string Model { get; }
}
