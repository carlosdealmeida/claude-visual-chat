using System;
using System.Collections.Generic;
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

    /// <summary>The extensions that make a name with no folder a file ("main.css"; "console.log" is a call, though "log" is one too).</summary>
    private static readonly HashSet<string> KnownExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "cs", "csx", "csproj", "sln", "axaml", "xaml", "razor", "cshtml", "fs", "vb", "js", "mjs", "cjs", "jsx", "ts", "tsx", "json",
        "jsonl", "ndjson", "css", "scss", "less", "html", "htm", "xml", "svg", "md", "mdx", "txt", "csv", "tsv", "yml", "yaml", "toml",
        "ini", "cfg", "conf", "env", "py", "ipynb", "rb", "php", "go", "rs", "java", "kt", "kts", "gradle", "swift", "c", "h", "cpp",
        "hpp", "cc", "sh", "bash", "ps1", "psm1", "bat", "cmd", "sql", "proto", "vue", "svelte", "dart", "lua", "pdf",
        "png", "jpg", "jpeg", "gif", "webp", "rs", "asp", "asa", "aspx", "ascx", "ashx", "asmx", "asax", "master", "inc", "config",
        "resx", "props", "targets", "vbs",
    };

    /// <summary>
    /// The extensions that are also the name of a member or a call ("res.json", "obj.cs", "foo.h"): with one of these a short
    /// lowercase stem is a variable; with any other ("lib.rs", "web.config", "pom.xml", "log.txt") it is a file.
    /// </summary>
    private static readonly HashSet<string> MemberLikeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "json", "js", "ts", "cs", "h", "c", "env", "css", "html", "md", "sh",
    };

    /// <summary>The last part before the extension of a name like "index.d.ts" or "appsettings.Development.json": a dotted name is a file only with one of these.</summary>
    private static readonly HashSet<string> MultiDotParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "d", "test", "spec", "min", "config", "stories", "e2e", "module", "bundle", "local", "development", "production", "staging",
        "dev", "prod", "designer", "generated", "g", "axaml", "xaml", "razor", "cshtml", "aspx", "ascx", "asax", "master",
        "override", "app", "server", "client", "service", "controller", "component", "routes", "release", "debug", "tests",
    };

    /// <summary>The dot files people open (".gitignore", ".env.local", ".eslintrc.json"), by the name after the first dot.</summary>
    private static readonly HashSet<string> DotFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "gitignore", "env", "npmrc", "nvmrc", "eslintrc", "prettierrc", "babelrc", "htaccess", "stylelintrc",
    };

    /// <summary>Whole names whose extension alone says nothing ("yarn.lock"; "mutex.lock" is a call).</summary>
    private static readonly HashSet<string> KnownNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "yarn.lock", "Cargo.lock", "poetry.lock", "composer.lock", "Gemfile.lock", "flake.lock",
    };

    /// <summary>Libraries and words that end like a file ("Node.js", "Avalonia.Xaml") and are not one.</summary>
    private static readonly HashSet<string> ProseStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "node", "vue", "react", "next", "nuxt", "express", "angular", "ember", "backbone", "three", "deno", "bun", "avalonia",
        "newtonsoft", "system", "microsoft", "process", "import", "window", "document", "console", "this", "self", "example",
    };

    /// <summary>Short lowercase stems that are files ("app.json") even with a member-like extension; any other ("res.json", "obj.cs", "foo.h") is a variable.</summary>
    private static readonly HashSet<string> ShortStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "app", "run", "api", "cli", "ci", "db", "ui", "vm",
    };

    private static readonly Regex LineSuffix = new Regex(@":(\d+)(?::\d+)?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LineFragment = new Regex(@"^L(\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>A scheme of two letters or more ("https:", "mailto:"); one letter is a drive ("C:\").</summary>
    private static readonly Regex Scheme = new Regex(@"^[A-Za-z][A-Za-z0-9+.\-]+:(?!\d+(?::\d+)?$)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Whether the text of a code span is a path: its last part is a file name with an extension, it has a <c>/</c> or <c>\</c> or else
    /// is a bare name of a known extension (see <see cref="IsBareFileName"/>), and it is not a URL. An optional <c>:line</c> or
    /// <c>:line:col</c> at the end gives <paramref name="line"/> (null without one); <paramref name="path"/> is the text as written, without that suffix.
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
        if (!HasFileName(body) || HasHostPrefix(body)) return false;
        if (body.IndexOfAny(Separators) < 0 && !IsBareFileName(body)) return false;
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

        // the fragment first (it may follow a query, and holds the line), then the query
        int? found = null;
        var anchor = text.IndexOf('#');
        if (anchor >= 0)
        {
            var fragment = LineFragment.Match(text.Substring(anchor + 1));
            if (fragment.Success) found = ParseLine(fragment.Groups[1].Value);
            text = text.Substring(0, anchor); // any other fragment names a place the host cannot open
        }
        var query = text.IndexOf('?');
        if (query >= 0) text = text.Substring(0, query);
        if (found == null)
        {
            var suffix = LineSuffix.Match(text);
            if (suffix.Success)
            {
                text = text.Substring(0, suffix.Index);
                found = ParseLine(suffix.Groups[1].Value);
            }
        }
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

    /// <summary>
    /// A name with no folder has nothing to tell it from a member or a library ("res.json", "Node.js", "System.Text.Json"), so it
    /// leans to "not a path": the extension must be one people open, a dotted stem must end in a known part ("index.d.ts"), and the
    /// stem is not a library's name or a short variable.
    /// </summary>
    private static bool IsBareFileName(string name)
    {
        if (KnownNames.Contains(name)) return true;
        if (name[0] == '.')
        {
            // a dot file goes by the name after its first dot: ".env.local", ".eslintrc.json"; ".then" or ".NET" is not one
            var end = name.IndexOf('.', 1);
            return DotFiles.Contains(end < 0 ? name.Substring(1) : name.Substring(1, end - 1));
        }
        var dot = name.LastIndexOf('.');
        var extension = name.Substring(dot + 1);
        if (!KnownExtensions.Contains(extension)) return false;
        var stem = name.Substring(0, dot);
        var inner = stem.LastIndexOf('.');
        if (inner >= 0) return MultiDotParts.Contains(stem.Substring(inner + 1));
        if (ProseStems.Contains(stem)) return false;
        var shortLower = stem.Length <= 3;
        foreach (var c in stem) shortLower &= char.IsLower(c);
        return !shortLower || !MemberLikeExtensions.Contains(extension) || ShortStems.Contains(stem);
    }

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
