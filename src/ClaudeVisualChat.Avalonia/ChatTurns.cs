using System;
using System.Collections.Generic;

namespace ClaudeVisualChat;

/// <summary>Reads the turns of a conversation's list: a turn is what Claude did after a message, up to its <see cref="TurnFooterItem"/>.</summary>
internal static class ChatTurns
{
    /// <summary>
    /// What Claude wrote in the turn that ends with the footer at <paramref name="footerIndex"/>: the text of each
    /// <see cref="AssistantTextItem"/> between that footer and the closest <see cref="UserMessageItem"/> or
    /// <see cref="TurnFooterItem"/> before it, in the order of the list, joined by a blank line. Tool calls (what a
    /// subagent wrote is inside them, not in the list), thoughts and notices are not part of it. Empty when the turn
    /// wrote nothing.
    /// </summary>
    internal static string AnswerText(IList<ChatItem> items, int footerIndex)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));
        var answers = new List<string>();
        for (var i = footerIndex - 1; i >= 0; i--)
        {
            var item = items[i];
            if (item is UserMessageItem || item is TurnFooterItem) break;
            if (item is AssistantTextItem answer && answer.Text.Length > 0) answers.Add(answer.Text);
        }
        answers.Reverse();
        return string.Join("\n\n", answers);
    }
}
