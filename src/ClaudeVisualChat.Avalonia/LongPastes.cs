using System;
using System.Collections.Generic;

namespace ClaudeVisualChat;

/// <summary>
/// The long pastes of the reply box, like the extension's. A paste is long when it is over <see cref="LongCharacters"/> characters or
/// has more than <see cref="LongLineFeeds"/> line feeds. It stays plain text in the box; the box only remembers it (trimmed, when
/// that leaves at least <see cref="MinimumLength"/> characters), the last <see cref="Capacity"/> of them, and when the message is
/// sent, the ones still in its text go with it as <see cref="MessageSubmittedEventArgs.InlinePastes"/>. A prompt recalled with the
/// up arrow brings back the pastes that were sent with it, since the box still remembers them.
/// </summary>
internal sealed class LongPastes
{
    internal const int LongCharacters = 800;
    internal const int LongLineFeeds = 2;
    internal const int MinimumLength = 20;
    internal const int Capacity = 100;

    /// <summary>What a message over the limit ends with: the text goes to Claude, so it is not translated.</summary>
    internal const string TruncationNotice = "\n\n[Message truncated - exceeded 50,000 character limit]";

    private readonly List<string> _pastes = new List<string>();

    /// <summary>The pastes remembered, the oldest first.</summary>
    internal IReadOnlyList<string> Remembered => _pastes;

    /// <summary>True when the text is a long paste: over 800 characters, or over 2 line feeds (line breaks of any kind count).</summary>
    internal static bool IsLong(string text)
    {
        if (text.Length > LongCharacters) return true;
        var feeds = 0;
        foreach (var c in text)
        {
            if (c == '\n' && ++feeds > LongLineFeeds) return true;
        }
        return false;
    }

    /// <summary>
    /// Remembers <paramref name="pasted"/> (already clean, with its line breaks as line feeds) when it is a long paste. One already
    /// remembered moves to the end. True when it was remembered.
    /// </summary>
    internal bool Remember(string pasted)
    {
        if (!IsLong(pasted)) return false;
        var trimmed = Truncate(pasted.Trim(), InvisibleCharacters.PasteLimit);
        if (trimmed.Length < MinimumLength) return false;
        _pastes.Remove(trimmed);
        _pastes.Add(trimmed);
        if (_pastes.Count > Capacity) _pastes.RemoveAt(0);
        return true;
    }

    /// <summary>
    /// The pastes that are in <paramref name="text"/> whole, once each, within its first <paramref name="limit"/> characters (what is
    /// sent): one that the limit cuts in two is not one any more.
    /// </summary>
    internal List<string> In(string text, int limit)
    {
        var found = new List<string>();
        foreach (var paste in _pastes)
        {
            var at = text.IndexOf(paste, StringComparison.Ordinal);
            if (at >= 0 && at + paste.Length <= limit && !found.Contains(paste)) found.Add(paste);
        }
        return found;
    }

    /// <summary>The first <paramref name="length"/> characters of the text, without cutting a surrogate pair in two.</summary>
    internal static string Truncate(string text, int length)
    {
        if (text.Length <= length) return text;
        if (length > 0 && char.IsHighSurrogate(text[length - 1])) length--;
        return text.Substring(0, length);
    }
}
