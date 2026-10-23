using System;
using System.Collections.Generic;

namespace ClaudeVisualChat.Protocol;

/// <summary>
/// The line that names a file sent with a message, <c>[attached file: &lt;path on the claude's machine&gt;]</c>: the
/// host adds one per file at the end of the text it sends, and the claude reads the file from there. The conversation
/// takes those lines back off the end of a user message (live or in a session file) and shows chips instead.
/// </summary>
public static class AttachedFileLine
{
    private const string Prefix = "[attached file: ";
    private const string Suffix = "]";

    /// <summary>The marker line for a file at <paramref name="remotePath"/>, without a line break.</summary>
    public static string Format(string remotePath)
    {
        if (remotePath == null) throw new ArgumentNullException(nameof(remotePath));
        return Prefix + remotePath + Suffix;
    }

    /// <summary>
    /// Takes the marker lines off the end of the message: from the last line up (blank lines at the very end left
    /// out), each line that is exactly a marker (nothing before it, only blank space after it, a path not empty), up
    /// to the first line that is not. Returns the text left, with
    /// the line breaks before the markers taken off too, and the paths in their order. A message with no marker at
    /// its end comes back as it is, with no paths; a marker in the middle of the text stays text.
    /// </summary>
    public static (string Text, IReadOnlyList<string> Paths) Split(string message)
    {
        if (message == null) throw new ArgumentNullException(nameof(message));
        var lines = message.Split('\n');
        var last = lines.Length - 1;
        // a line break (or blank space) after the last marker is no line of its own
        while (last >= 0 && string.IsNullOrWhiteSpace(lines[last])) last--;
        var paths = new List<string>();
        var first = last + 1;
        for (var i = last; i >= 0; i--)
        {
            var path = PathOf(lines[i].TrimEnd()); // spaces after a marker do not hide it
            if (path == null) break;
            paths.Add(path);
            first = i;
        }
        if (paths.Count == 0) return (message, Array.Empty<string>());
        paths.Reverse();
        var text = string.Join("\n", lines, 0, first).TrimEnd('\r', '\n');
        return (text, paths);
    }

    private static string? PathOf(string line)
    {
        if (line.Length <= Prefix.Length + Suffix.Length) return null;
        if (!line.StartsWith(Prefix, StringComparison.Ordinal) || !line.EndsWith(Suffix, StringComparison.Ordinal)) return null;
        var path = line.Substring(Prefix.Length, line.Length - Prefix.Length - Suffix.Length);
        return path.Trim().Length == 0 ? null : path;
    }
}
