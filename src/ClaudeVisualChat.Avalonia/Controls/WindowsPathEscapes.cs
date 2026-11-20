using System.Text;
using System.Text.RegularExpressions;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The Markdown engine takes only <c>\_</c>, <c>\*</c> and <c>\\</c> as escapes, so in <c>C:\a\_b</c> the backslash before
/// the underscore would vanish. <see cref="Keep"/> doubles that backslash inside a Windows path, outside code, and
/// escapes the path's own underscores and asterisks so that they are not read as emphasis.
/// </summary>
internal static class WindowsPathEscapes
{
    // a drive path (C:\...) or a server path (\\name\...), up to the next white space
    private static readonly Regex PathRun = new(
        @"(?:(?<![A-Za-z0-9])[A-Za-z]:\\|(?<!\\)\\\\(?=[A-Za-z0-9_.$-]))\S*", RegexOptions.Compiled);

    // a fenced block (``` or ~~~, to its closing fence or the end) or an inline code span
    private static readonly Regex Code = new(
        @"^[ ]{0,3}(`{3,}|~{3,}).*?(?:^[ ]{0,3}\1[`~]*[ \t]*$|\z)|(`+)(?:(?!\2).)+?\2",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.Singleline);

    public static string Keep(string markdown)
    {
        if (string.IsNullOrEmpty(markdown) || markdown.IndexOf((char)92) < 0) return markdown;

        var result = new StringBuilder();
        var last = 0;
        var changed = false;
        foreach (Match code in Code.Matches(markdown))
        {
            changed |= Fix(markdown, last, code.Index, result);
            result.Append(code.Value);
            last = code.Index + code.Length;
        }
        changed |= Fix(markdown, last, markdown.Length, result);
        return changed ? result.ToString() : markdown;
    }

    private static bool Fix(string text, int start, int end, StringBuilder result)
    {
        var slice = text.Substring(start, end - start);
        var from = 0;
        var changed = false;
        foreach (Match path in PathRun.Matches(slice))
        {
            var fixedRun = Protect(slice, path);
            if (fixedRun == null) continue;
            result.Append(slice, from, path.Index - from).Append(fixedRun);
            from = path.Index + path.Length;
            changed = true;
        }
        result.Append(slice, from, slice.Length - from);
        return changed;
    }

    /// <summary>
    /// Null when the path has no backslash before an underscore or an asterisk. Otherwise the path with each of those
    /// backslashes doubled and each underscore and asterisk escaped, so the engine shows the path letter for letter. The
    /// delimiters that close an emphasis opened just before the path (<c>**C:\a\_b**</c>) stay as they are.
    /// </summary>
    private static string? Protect(string slice, Match path)
    {
        var run = path.Value;
        var last = run.Length;
        var opening = 0;
        while (path.Index - opening > 0 && slice[path.Index - opening - 1] is '*' or '_') opening++;
        if (opening > 0)
        {
            // the closer of that emphasis, wherever the run puts it: punctuation may follow it, as in a bold path then a colon
            var closer = run.LastIndexOf(slice.Substring(path.Index - opening, opening), System.StringComparison.Ordinal);
            if (closer > 0 && run[closer - 1] != (char)92) last = closer;
        }

        var found = false;
        for (var i = 1; i < last; i++)
            if (run[i - 1] == (char)92 && run[i] is '_' or '*') { found = true; break; }
        if (!found) return null;

        var built = new StringBuilder();
        var begin = 0;
        if (run[0] == (char)92) { built.Append((char)92, 4); begin = 2; } // the engine takes a leading pair of backslashes for one
        for (var i = begin; i < last; i++)
        {
            var c = run[i];
            if (c == (char)92 && i + 1 < last && run[i + 1] is '_' or '*') built.Append((char)92, 2);
            else if (c is '_' or '*') built.Append((char)92).Append(c);
            else built.Append(c);
        }
        return built.Append(run, last, run.Length - last).ToString();
    }
}
