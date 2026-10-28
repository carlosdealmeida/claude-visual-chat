using System;

namespace ClaudeVisualChat.Controls;

/// <summary>The groups of the command palette, in the order they show; <see cref="PaletteAction.Section"/> is one of these.</summary>
internal static class PaletteSection
{
    internal const string Context = "Context";
    internal const string Model = "Model";
    internal const string Customize = "Customize";
    internal const string Settings = "Settings";
    internal const string SlashCommands = "Slash Commands";
    internal const string Support = "Support";

    /// <summary>The groups in the order they show.</summary>
    internal static readonly string[] Order = { Context, Model, Customize, Settings, SlashCommands, Support };
}

/// <summary>
/// A row of the command palette that is not a slash command: it acts on the view (which gives it the conversation and the reply
/// box). <see cref="Label"/> is the row's name, in English; <see cref="Text"/> gives it in the language of the strings. A row with
/// <see cref="IsOn"/> is a switch, and one that <see cref="KeepsOpen"/> leaves the palette open; the others close it.
/// </summary>
internal sealed record PaletteAction(string Section, string Label, Action<ClaudeChatView> Run, Func<ClaudeChatView, bool>? IsOn = null)
{
    /// <summary>The row's name in the strings' language; the <see cref="Label"/> when null.</summary>
    internal Func<ChatStrings, string>? Text { get; init; }

    /// <summary>Whether the row shows now; always, when null.</summary>
    internal Func<ClaudeChatView, bool>? IsShown { get; init; }

    /// <summary>What shows at the right of the row (the model, the level); nothing when null or when it gives null.</summary>
    internal Func<ClaudeChatView, ChatStrings, string?>? Value { get; init; }

    /// <summary>The palette stays open after the row acts (a switch always does).</summary>
    internal bool KeepsOpen { get; init; }

    /// <summary>The row's tooltip in the strings' language; none when null.</summary>
    internal Func<ChatStrings, string>? Tip { get; init; }
}
