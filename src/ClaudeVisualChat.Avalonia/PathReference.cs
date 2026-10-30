using System;
using System.Text.RegularExpressions;

namespace ClaudeVisualChat;

/// <summary>
/// Tells a file path from other text in a reply: a code span (<c>src/app/main.css:42</c>) or the target of a Markdown link
/// (<c>docs/a.md#L42</c>). The view never checks the disk, so this goes by the text alone and leans to "not a path": a span that is
/// not one stays plain code, while a wrong guess would make ordinary code look clickable.
/// </summary>
internal static class PathReference
{
    /// <summary>The longest extension of a file name.</summary>
    private const int MaxExtensionLength = 10;

    private static readonly char[] Separators = { '/', '\\' };

    private static readonly Regex LineSuffix = new Regex(@":(\d+)(?::\d+)?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LineFragment = new Regex(@"#L(\d+)(?:C\d+)?(?:-L?\d+(?:C\d+)?)?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>A scheme of two letters or more ("https:", "mailto:"); one letter is a drive ("C:\").</summary>
    private static readonly Regex Scheme = new Regex(@"^[A-Za-z][A-Za-z0-9+.\-]+:(?!\d+(?::\d+)?$)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Whether the text of a code span is a path: it has a <c>/</c> or <c>\</c>, its last part is a file name with an extension, and
    /// it is not a URL. An optional <c>:line</c> or <c>:line:col</c> at the end gives <paramref name="line"/> (null without one);
    /// <paramref name="path"/> is the text as written, without that suffix.
    /// </summary>
    public static bool TryFromCode(string? text, out string path, out int? line)
    {
        path = "";
        line = null;
        if (text == null) return false;
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.Contains("://")) return false;
        foreach (var c in trimmed)
        {
            if (!IsPathChar(c)) return false;
        }

        var body = trimmed;
        int? found = null;
        var suffix = LineSuffix.Match(trimmed);
        if (suffix.Success)
        {
            body = trimmed.Substring(0, suffix.Index);
            found = ParseLine(suffix.Groups[1].Value);
        }
        if (body.IndexOfAny(Separators) < 0 || !HasFileName(body) || HasHostPrefix(body)) return false;
        path = body;
        line = found;
        return true;
    }

    /// <summary>
    /// Whether the target of a Markdown link is a path: anything that is not a URL (no scheme such as <c>https:</c> or
    /// <c>mailto:</c>, not <c>//host</c>) and not just an anchor. The line comes from <c>#L42</c> (<c>#L42-L51</c> gives 42) or a
    /// <c>:42</c> suffix; the path is unescaped (<c>%20</c>).
    /// </summary>
    public static bool TryFromLink(string? target, out string path, out int? line)
    {
        path = "";
        line = null;
        var text = target?.Trim();
        if (string.IsNullOrEmpty(text) || text![0] == '#' || text.StartsWith("//", StringComparison.Ordinal) || Scheme.IsMatch(text))
            return false;

        var query = text.IndexOf('?');
        if (query >= 0) text = text.Substring(0, query);
        int? found = null;
        var fragment = LineFragment.Match(text);
        if (fragment.Success)
        {
            text = text.Substring(0, fragment.Index);
            found = ParseLine(fragment.Groups[1].Value);
        }
        else
        {
            var suffix = LineSuffix.Match(text);
            if (suffix.Success)
            {
                text = text.Substring(0, suffix.Index);
                found = ParseLine(suffix.Groups[1].Value);
            }
        }
        var anchor = text.IndexOf('#');
        if (anchor >= 0) text = text.Substring(0, anchor); // any other fragment names a place the host cannot open
        if (text.Length == 0) return false;
        try
        {
            text = Uri.UnescapeDataString(text);
        }
        catch (UriFormatException)
        {
            // keep the text as it came
        }
        path = text;
        line = found;
        return true;
    }

    private static int? ParseLine(string digits) => int.TryParse(digits, out var n) && n > 0 ? n : (int?)null;

    /// <summary>Letters, digits and the marks a path uses; no spaces, and none of the signs of code (<c>*</c>, <c>&lt;</c>, <c>=</c>, <c>(</c>...).</summary>
    private static bool IsPathChar(char c) => char.IsLetterOrDigit(c) || c == '\\' || "_-./:~@+%#[]".IndexOf(c) >= 0;

    /// <summary>A colon before the last separator that is not a drive's ("user@host:/etc/x.conf"): a remote file, not one of this machine.</summary>
    private static bool HasHostPrefix(string body)
    {
        var colon = body.IndexOf(':');
        if (colon < 0) return false;
        return !(colon == 1 && char.IsLetter(body[0]));
    }

    /// <summary>The last part has a name, a dot and an extension of letters or digits with at least one letter ("a.cs", not "2.0").</summary>
    private static bool HasFileName(string body)
    {
        var name = body.Substring(body.LastIndexOfAny(Separators) + 1);
        var dot = name.LastIndexOf('.');
        if (dot < 1 && !(dot == 0 && name.Length > 1) || dot == name.Length - 1) return false;
        var extension = name.Substring(dot + 1);
        if (extension.Length == 0 || char.IsDigit(extension[0])) return false; // "2.0f" is arithmetic
        if (extension.Length > MaxExtensionLength) return false;
        var letter = false;
        foreach (var c in extension)
        {
            if (!char.IsLetterOrDigit(c)) return false;
            letter |= char.IsLetter(c);
        }
        return letter;
    }
}
