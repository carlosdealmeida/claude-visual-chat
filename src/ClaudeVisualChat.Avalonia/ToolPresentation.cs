using System;
using System.Collections.Generic;
using System.Text.Json;

namespace ClaudeVisualChat;

/// <summary>Kind of a line in a tool diff.</summary>
public enum DiffKind { Context, Removed, Added, Gap }

/// <summary>One line of a tool diff. For <see cref="DiffKind.Gap"/> the text is "+N" (hidden lines) or empty.</summary>
public record DiffLine(DiffKind Kind, string Text);

/// <summary>State of a to-do entry.</summary>
public enum TodoStatus { Pending, InProgress, Completed }

/// <summary>One entry of a TodoWrite list.</summary>
public record TodoEntry(string Text, TodoStatus Status);

/// <summary>Text cut to its first lines, with the count of lines left out.</summary>
public readonly record struct TruncatedText(string Text, int HiddenLines);

/// <summary>How many lines an edit adds and removes, counted like the extension (new lines minus old ones).</summary>
public readonly record struct LineChange(int Added, int Removed);

/// <summary>The lines a Read asked for: from <see cref="From"/> to <see cref="To"/> (null: to the end), from 1.</summary>
public readonly record struct LineRange(int From, int? To);

/// <summary>Pure helpers that turn a tool call's input into what the chat shows. Never throws on odd input.</summary>
public static class ToolPresentation
{
    /// <summary>Lines of tool output shown before "+N lines".</summary>
    public const int OutputLines = 12;
    /// <summary>Characters of a tool's result a call keeps in <see cref="ToolCallItem.FullResultText"/>.</summary>
    public const int MaxKeptResultChars = 500_000;
    /// <summary>Lines of a Write shown as additions.</summary>
    public const int WriteLines = 20;
    /// <summary>Unchanged lines kept around a change.</summary>
    public const int ContextLines = 3;

    private const string McpPrefix = "mcp__";

    public static string Summary(string toolName, JsonElement input, string? workingDirectory)
    {
        try
        {
            if (input.ValueKind != JsonValueKind.Object) return "";
            switch (toolName)
            {
                case "Read":
                case "Edit":
                case "Write":
                case "MultiEdit":
                    return RelativePath(Str(input, "file_path"), workingDirectory);
                case "NotebookEdit":
                    return RelativePath(Str(input, "notebook_path"), workingDirectory);
                case "Bash":
                case "PowerShell":
                    return FirstLine(Str(input, "command"));
                case "Grep":
                    return GrepSummary(input);
                case "Glob":
                case "Search":
                {
                    var pattern = Str(input, "pattern");
                    return pattern.Length > 0 ? "pattern: \"" + pattern + "\"" : "";
                }
                case "WebFetch": return Str(input, "url");
                case "WebSearch": return Str(input, "query");
                case "Agent":
                case "Task": return Str(input, "description");
                case "Skill": return Str(input, "skill");
                case "TodoWrite": return "";
                case "ExitPlanMode": return PlanFileName(input) ?? ""; // never the plan's text: the document shows it
                default: return FirstString(input);
            }
        }
        catch (Exception)
        {
            return "";
        }
    }

    /// <summary>The pattern in quotes, then the options that are set, in this order: <c> (in src, glob: *.cs, type: cs)</c>.</summary>
    private static string GrepSummary(JsonElement input)
    {
        var pattern = Str(input, "pattern");
        if (pattern.Length == 0) return "";
        var options = new List<string>();
        if (Str(input, "path") is { Length: > 0 } path) options.Add("in " + path);
        if (Str(input, "glob") is { Length: > 0 } glob) options.Add("glob: " + glob);
        if (Str(input, "type") is { Length: > 0 } type) options.Add("type: " + type);
        return "\"" + pattern + "\"" + (options.Count == 0 ? "" : " (" + string.Join(", ", options) + ")");
    }

    /// <summary>
    /// The lines of a Glob's or Grep's output that are not empty (a line break of either kind ends one); an output that is exactly
    /// <paramref name="noneText"/> (what a Glob says when it found nothing) counts none.
    /// </summary>
    internal static int CountOutputLines(string? output, string? noneText = null)
    {
        if (string.IsNullOrEmpty(output)) return 0;
        if (noneText != null && output!.Trim() == noneText) return 0;
        var count = 0;
        var start = 0;
        for (var i = 0; i <= output!.Length; i++)
        {
            if (i < output.Length && output[i] != '\n') continue;
            var end = i > start && output[i - 1] == '\r' ? i - 1 : i;
            if (end > start) count++;
            start = i + 1;
        }
        return count;
    }

    /// <summary>The domains of a WebSearch's <c>allowed_domains</c> or <c>blocked_domains</c>; none when the input has no such list.</summary>
    internal static IReadOnlyList<string> SearchDomains(JsonElement input, string property)
    {
        var domains = new List<string>();
        if (input.ValueKind != JsonValueKind.Object || !input.TryGetProperty(property, out var list) || list.ValueKind != JsonValueKind.Array)
            return domains;
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } domain) domains.Add(domain);
        }
        return domains;
    }

    /// <summary>
    /// The diff of an Edit, MultiEdit or Write; null for other tools. Each changed side of an edit, like a Write,
    /// shows at most <see cref="WriteLines"/> lines followed by a "+N" gap, so a huge change stays small.
    /// </summary>
    public static IReadOnlyList<DiffLine>? Diff(string toolName, JsonElement input) => Diff(toolName, input, WriteLines);

    /// <summary>
    /// The diff of an Edit, MultiEdit or Write, each changed side cut at <paramref name="maxLinesPerSide"/> lines
    /// (followed by a "+N" gap), or whole when null: the permission request shows all that the user says yes to.
    /// The unchanged lines around a change are kept to <see cref="ContextLines"/> either way.
    /// </summary>
    public static IReadOnlyList<DiffLine>? Diff(string toolName, JsonElement input, int? maxLinesPerSide)
    {
        try
        {
            if (input.ValueKind != JsonValueKind.Object) return null;
            var most = maxLinesPerSide is int limit ? Math.Max(0, limit) : int.MaxValue;
            switch (toolName)
            {
                case "Edit":
                    return TryEdit(input, most, out var one) ? one : null;
                case "MultiEdit":
                {
                    if (!input.TryGetProperty("edits", out var edits) || edits.ValueKind != JsonValueKind.Array) return null;
                    var result = new List<DiffLine>();
                    foreach (var edit in edits.EnumerateArray())
                    {
                        if (edit.ValueKind != JsonValueKind.Object || !TryEdit(edit, most, out var part)) continue;
                        if (result.Count > 0) result.Add(new DiffLine(DiffKind.Gap, ""));
                        result.AddRange(part);
                    }
                    return result.Count == 0 ? null : result;
                }
                case "Write":
                {
                    if (!TryStr(input, "content", out var content)) return null;
                    var lines = SplitLines(content);
                    var result = new List<DiffLine>();
                    for (var i = 0; i < lines.Count && i < most; i++)
                        result.Add(new DiffLine(DiffKind.Added, lines[i]));
                    if (lines.Count > most)
                        result.Add(new DiffLine(DiffKind.Gap, "+" + (lines.Count - most)));
                    return result;
                }
                default:
                    return null;
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static IReadOnlyList<TodoEntry>? Todos(string toolName, JsonElement input)
    {
        try
        {
            if (toolName != "TodoWrite" || input.ValueKind != JsonValueKind.Object) return null;
            if (!input.TryGetProperty("todos", out var todos) || todos.ValueKind != JsonValueKind.Array) return null;
            var result = new List<TodoEntry>();
            foreach (var item in todos.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !TryStr(item, "content", out var text)) continue;
                var status = Str(item, "status") switch
                {
                    "in_progress" => TodoStatus.InProgress,
                    "completed" => TodoStatus.Completed,
                    _ => TodoStatus.Pending,
                };
                result.Add(new TodoEntry(text, status));
            }
            return result;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The line under an Edit or MultiEdit: lines added and removed, from the line counts of the old and new texts
    /// (a MultiEdit adds its edits up); null for other tools or without both texts.
    /// </summary>
    public static LineChange? EditLineChange(string toolName, JsonElement input)
    {
        try
        {
            if (input.ValueKind != JsonValueKind.Object) return null;
            switch (toolName)
            {
                case "Edit":
                    return TryCount(input, out var oldLines, out var newLines) ? Change(oldLines, newLines) : null;
                case "MultiEdit":
                {
                    if (!input.TryGetProperty("edits", out var edits) || edits.ValueKind != JsonValueKind.Array) return null;
                    int added = 0, removed = 0, counted = 0;
                    foreach (var edit in edits.EnumerateArray())
                    {
                        if (!TryCount(edit, out var a, out var b)) continue;
                        var change = Change(a, b);
                        added += change.Added;
                        removed += change.Removed;
                        counted++;
                    }
                    return counted == 0 ? null : new LineChange(added, removed);
                }
                default:
                    return null;
            }
        }
        catch (Exception)
        {
            return null;
        }

        static LineChange Change(int oldLines, int newLines) =>
            new LineChange(Math.Max(0, newLines - oldLines), Math.Max(0, oldLines - newLines));

        static bool TryCount(JsonElement edit, out int oldLines, out int newLines)
        {
            oldLines = newLines = 0;
            if (!TryStr(edit, "old_string", out var oldText) || !TryStr(edit, "new_string", out var newText)) return false;
            oldLines = CountLines(oldText);
            newLines = CountLines(newText);
            return true;
        }
    }

    /// <summary>The lines a Write writes, counted like the extension (a final line feed starts one more); null otherwise.</summary>
    public static int? WriteLineCount(string toolName, JsonElement input)
    {
        if (toolName != "Write" || input.ValueKind != JsonValueKind.Object) return null;
        return TryStr(input, "content", out var content) ? CountLines(content) : (int?)null;
    }

    /// <summary>The lines a Read asked for, when it gave an offset; null otherwise (the whole file).</summary>
    public static LineRange? ReadRange(string toolName, JsonElement input)
    {
        if (toolName != "Read" || input.ValueKind != JsonValueKind.Object) return null;
        if (!TryInt(input, "offset", out var offset)) return null;
        return TryInt(input, "limit", out var limit)
            ? new LineRange(offset + 1, offset + limit)
            : new LineRange(offset + 1, null);
    }

    public static TruncatedText Truncate(string text, int maxLines)
    {
        if (string.IsNullOrEmpty(text)) return new TruncatedText("", 0);
        if (maxLines < 0) maxLines = 0;

        // Walk the text once: remember where line number maxLines ends and count all lines.
        var lines = 0;
        var cut = -1;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;
            lines++;
            if (lines == maxLines) cut = i;
        }
        if (text[text.Length - 1] != '\n') lines++; // last line without a terminator

        if (lines <= maxLines) return new TruncatedText(text, 0);
        var shown = maxLines == 0 ? "" : text.Substring(0, cut);
        if (shown.EndsWith("\r", StringComparison.Ordinal)) shown = shown.Substring(0, shown.Length - 1);
        return new TruncatedText(shown, lines - maxLines);
    }

    /// <summary>
    /// The name of an <c>ExitPlanMode</c> plan's file: the last part of its input's <c>planFilePath</c>, after a slash of either
    /// kind; null when the input has none.
    /// </summary>
    internal static string? PlanFileName(JsonElement input)
    {
        if (!TryStr(input, "planFilePath", out var path)) return null;
        var name = path.Substring(Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/')) + 1);
        return name.Length == 0 ? null : name;
    }

    public static (string Server, string Tool)? McpName(string toolName)
    {
        if (toolName == null || !toolName.StartsWith(McpPrefix, StringComparison.Ordinal)) return null;
        var rest = toolName.Substring(McpPrefix.Length);
        var sep = rest.IndexOf("__", StringComparison.Ordinal);
        if (sep <= 0 || sep + 2 >= rest.Length) return null;
        return (rest.Substring(0, sep), rest.Substring(sep + 2));
    }

    // ---- helpers ----

    private static bool TryStr(JsonElement obj, string name, out string value)
    {
        if (obj.ValueKind == JsonValueKind.Object
            && obj.TryGetProperty(name, out var p)
            && p.ValueKind == JsonValueKind.String)
        {
            value = p.GetString() ?? "";
            return true;
        }
        value = "";
        return false;
    }

    private static string Str(JsonElement obj, string name) => TryStr(obj, name, out var v) ? v : "";

    private static bool TryInt(JsonElement obj, string name, out int value)
    {
        value = 0;
        return obj.ValueKind == JsonValueKind.Object
               && obj.TryGetProperty(name, out var p)
               && p.ValueKind == JsonValueKind.Number
               && p.TryGetInt32(out value)
               && value >= 0;
    }

    /// <summary>Pieces between line feeds, like JavaScript's <c>split("\n").length</c>.</summary>
    private static int CountLines(string text)
    {
        var lines = 1;
        foreach (var c in text)
        {
            if (c == '\n') lines++;
        }
        return lines;
    }

    private static string FirstString(JsonElement obj)
    {
        foreach (var p in obj.EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.String) return p.Value.GetString() ?? "";
        return "";
    }

    private static string FirstLine(string text)
    {
        var nl = text.IndexOf('\n');
        if (nl < 0) return text;
        return text.Substring(0, nl).TrimEnd('\r') + "…";
    }

    private static string RelativePath(string path, string? workingDirectory)
    {
        if (path.Length == 0 || string.IsNullOrEmpty(workingDirectory)) return path;
        var wd = workingDirectory!.Replace('/', '\\').TrimEnd('\\');
        if (wd.Length == 0) return path;
        var norm = path.Replace('/', '\\');
        if (norm.Length > wd.Length + 1
            && norm[wd.Length] == '\\'
            && norm.StartsWith(wd, StringComparison.OrdinalIgnoreCase))
            return path.Substring(wd.Length + 1);
        return path;
    }

    private static List<string> SplitLines(string text)
    {
        var result = new List<string>();
        if (text.Length == 0) return result;
        var start = 0;
        while (true)
        {
            var nl = text.IndexOf('\n', start);
            if (nl < 0)
            {
                if (start < text.Length) result.Add(text.Substring(start).TrimEnd('\r'));
                break;
            }
            result.Add(text.Substring(start, nl - start).TrimEnd('\r'));
            start = nl + 1;
        }
        return result;
    }

    private static bool TryEdit(JsonElement edit, int most, out List<DiffLine> diff)
    {
        diff = new List<DiffLine>();
        if (!TryStr(edit, "old_string", out var oldText) || !TryStr(edit, "new_string", out var newText)) return false;
        var a = SplitLines(oldText);
        var b = SplitLines(newText);

        var max = Math.Min(a.Count, b.Count);
        var prefix = 0;
        while (prefix < max && a[prefix] == b[prefix]) prefix++;
        var suffix = 0;
        while (suffix < max - prefix && a[a.Count - 1 - suffix] == b[b.Count - 1 - suffix]) suffix++;

        var before = Math.Min(ContextLines, prefix);
        if (prefix > before) diff.Add(new DiffLine(DiffKind.Gap, "+" + (prefix - before)));
        for (var i = prefix - before; i < prefix; i++) diff.Add(new DiffLine(DiffKind.Context, a[i]));

        AddCapped(diff, DiffKind.Removed, a, prefix, a.Count - suffix, most);
        AddCapped(diff, DiffKind.Added, b, prefix, b.Count - suffix, most);

        var after = Math.Min(ContextLines, suffix);
        for (var i = 0; i < after; i++) diff.Add(new DiffLine(DiffKind.Context, a[a.Count - suffix + i]));
        if (suffix > after) diff.Add(new DiffLine(DiffKind.Gap, "+" + (suffix - after)));
        return true;
    }

    /// <summary>Adds lines [start, end) as <paramref name="kind"/>, at most <paramref name="most"/>, then "+N" for the rest.</summary>
    private static void AddCapped(List<DiffLine> diff, DiffKind kind, List<string> lines, int start, int end, int most)
    {
        var shown = most >= end - start ? end : start + most;
        for (var i = start; i < shown; i++) diff.Add(new DiffLine(kind, lines[i]));
        if (end > shown) diff.Add(new DiffLine(DiffKind.Gap, "+" + (end - shown)));
    }
}
