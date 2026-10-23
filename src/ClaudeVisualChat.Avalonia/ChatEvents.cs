using System;
using System.Collections.Generic;

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
    public MessageSubmittedEventArgs(string text, IReadOnlyList<ChatImage>? images = null)
        : this(text, images, null)
    {
    }

    internal MessageSubmittedEventArgs(string text, IReadOnlyList<ChatImage>? images, IReadOnlyList<ChatFile>? files)
    {
        Text = text ?? "";
        Images = images ?? Array.Empty<ChatImage>();
        Files = files ?? Array.Empty<ChatFile>();
    }

    /// <summary>What the user wrote; the files are not named in it (see <see cref="Files"/>).</summary>
    public string Text { get; }

    /// <summary>The images sent with the message; empty when there are none.</summary>
    public IReadOnlyList<ChatImage> Images { get; }

    /// <summary>
    /// The files sent with the message; empty when there are none. The host sends each one to the claude's machine,
    /// setting <see cref="ChatFile.Progress"/> as it goes and <see cref="ChatFile.RemotePath"/> when done (both on the
    /// UI thread: post them from an upload worker), then sends
    /// the text followed by one <see cref="Protocol.AttachedFileLine.Format"/> line per file; when that fails, it
    /// gives the message back with <see cref="ChatConversation.UndoSubmit"/>, and the files go back to the reply box.
    /// </summary>
    public IReadOnlyList<ChatFile> Files { get; }

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

/// <summary>A slash command the conversation handles itself instead of sending it as a message.</summary>
public sealed class CommandInvokedEventArgs : EventArgs
{
    public CommandInvokedEventArgs(string name, string arguments)
    {
        Name = name ?? "";
        Arguments = arguments ?? "";
    }

    /// <summary>The command, without the slash (<c>context</c>, <c>usage</c>, <c>rc</c>…).</summary>
    public string Name { get; }

    /// <summary>What followed the name, trimmed; empty when nothing did.</summary>
    public string Arguments { get; }
}

/// <summary>An effort level the user asked for, not yet confirmed by the claude.</summary>
public sealed class EffortRequestedEventArgs : EventArgs
{
    public EffortRequestedEventArgs(string? effort)
    {
        Effort = effort;
    }

    /// <summary>The level asked for (<c>low</c>, <c>medium</c>, <c>high</c>…); null to go back to the model's own.</summary>
    public string? Effort { get; }
}

/// <summary>The panel the conversation asks the view to show over the reply box.</summary>
public enum ChatPanel
{
    None,
    Context,
    Usage,

    /// <summary>The Agent map: the conversation's subagents, over the whole view (<see cref="ChatConversation.ShowAgents"/>).</summary>
    Agents,
}

/// <summary>The state of the browser extension, which the host reports.</summary>
public enum ChatBrowserStatus
{
    Off,
    Connecting,
    Connected,
    NoBrowser,
    Error,
}

/// <summary>The state of the Remote Control.</summary>
public enum ChatRemoteControlStatus
{
    Off,
    Connecting,
    Connected,
    Error,
}

/// <summary>The user asked to open the Remote Control link; the host opens it.</summary>
public sealed class RemoteControlLinkRequestedEventArgs : EventArgs
{
    public RemoteControlLinkRequestedEventArgs(string url)
    {
        Url = url;
    }

    public string Url { get; }
}
