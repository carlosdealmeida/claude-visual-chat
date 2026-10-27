using System;
using System.Collections.Generic;
using Avalonia.Controls;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The "+" sheet of the touch layout, in the style of the sheets of Anthropic's Claude app; the + of the reply box's bar opens it
/// (<see cref="ChatComposer.AddSheetRequested"/>). One block of rows, each with its icon of 18 px at the left, its name and, at the
/// right, its state in the secondary color:
/// "Photo or file" and "Commands" close the sheet and ask the view for the reply box's file dialog
/// (<see cref="AttachRequested"/>) and its command palette (<see cref="CommandsRequested"/>); "Mode" shows the permission mode the
/// claude reports and opens a page with the modes the view offers (<see cref="Modes"/>), the current one checked, where a pick
/// asks for it (<see cref="ChatConversation.SetPermissionMode"/>) and goes back; "Browser" and "Remote Control" show the state the
/// host reported and, with a tap, close the sheet and run the command of the same name (<see cref="ChatConversation.InvokeCommand"/>:
/// <c>chrome</c> and <c>remote-control</c>); and, while Remote Control is connected and has a link, "Open claude.ai/code" closes the
/// sheet and asks the host to open it (<see cref="ChatConversation.OpenRemoteControl"/>). It shows what the claude and the host
/// report, so it is drawn again as that changes.
/// </summary>
internal sealed class AddSheet : ChatSheet
{
    /// <summary>The page with the permission modes.</summary>
    private const string ModePage = "mode";

    private IReadOnlyList<string> _modes = Array.Empty<string>();

    /// <summary>The user tapped "Photo or file": the sheet has closed, and the view opens the reply box's file dialog.</summary>
    internal event EventHandler? AttachRequested;

    /// <summary>The user tapped "Commands": the sheet has closed, and the view opens the reply box's command palette.</summary>
    internal event EventHandler? CommandsRequested;

    /// <summary>
    /// The permission modes the sheet offers, in order (the view's <see cref="ClaudeChatView.AvailableModes"/>). With none, the
    /// "Mode" row is left out, since there is nothing to pick.
    /// </summary>
    internal IReadOnlyList<string> Modes
    {
        get => _modes;
        set
        {
            _modes = value ?? Array.Empty<string>();
            Rebuild();
        }
    }

    protected override bool HasPage(ChatConversation conversation, string page) => page != ModePage || _modes.Count > 0;

    protected override bool Shows(string? propertyName) =>
        propertyName is nameof(ChatConversation.PermissionMode) or nameof(ChatConversation.BrowserStatus)
            or nameof(ChatConversation.RemoteControlStatus) or nameof(ChatConversation.RemoteControlUrl);

    protected override string Title(ChatStrings strings) => Page == ModePage ? strings.ModeTitle : strings.AddAttachment;

    protected override void Fill(Panel body, ChatConversation conversation, ChatStrings strings)
    {
        if (Page == ModePage) FillModes(body, conversation, strings);
        else FillRows(body, conversation, strings);
    }

    private void FillRows(Panel body, ChatConversation conversation, ChatStrings strings)
    {
        var rows = new List<Control>
        {
            Row(strings.AttachPhotoOrFile, () => Choose(AttachRequested), icon: "ClaudeChat.Icon.Photo"),
            Row(strings.Commands, () => Choose(CommandsRequested), icon: "ClaudeChat.Icon.Slash"),
        };
        if (_modes.Count > 0)
        {
            var mode = conversation.PermissionMode;
            rows.Add(Row(strings.ModeTitle, () => ShowPage(ModePage), value: strings.ModeLabel(mode), opens: true,
                icon: ChatComposer.IconKey(mode)));
        }
        rows.Add(Row(strings.Browser, () => Run(conversation, "chrome"), value: strings.BrowserStatusLabel(conversation.BrowserStatus),
            icon: "ClaudeChat.Icon.Globe"));
        rows.Add(Row(strings.RemoteControl, () => Run(conversation, "remote-control"),
            value: strings.RemoteControlStatusLabel(conversation.RemoteControlStatus), icon: "ClaudeChat.Icon.Remote"));
        if (conversation.RemoteControlStatus == ChatRemoteControlStatus.Connected && !string.IsNullOrEmpty(conversation.RemoteControlUrl))
        {
            rows.Add(Row(strings.OpenRemoteControlLink, () =>
            {
                Close();
                conversation.OpenRemoteControl();
            }, icon: "ClaudeChat.Icon.External"));
        }
        body.Children.Add(Group(rows));
    }

    private void FillModes(Panel body, ChatConversation conversation, ChatStrings strings)
    {
        var rows = new List<Control>();
        foreach (var mode in _modes)
            rows.Add(Row(strings.ModeLabel(mode), () => PickMode(conversation, mode), check: mode == conversation.PermissionMode));
        body.Children.Add(Group(rows));
    }

    /// <summary>A row that hands over to the reply box: the sheet closes first, so the box has the screen and the keyboard.</summary>
    private void Choose(EventHandler? requested)
    {
        Close();
        requested?.Invoke(this, EventArgs.Empty);
    }

    private void Run(ChatConversation conversation, string command)
    {
        Close();
        conversation.InvokeCommand(command, "");
    }

    private void PickMode(ChatConversation conversation, string mode)
    {
        conversation.SetPermissionMode(mode);
        ShowPage(null);
    }
}
