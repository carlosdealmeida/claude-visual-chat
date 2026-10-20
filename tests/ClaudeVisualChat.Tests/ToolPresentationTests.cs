using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using ClaudeVisualChat;
using Xunit;

namespace ClaudeVisualChat.Tests;

public class ToolPresentationTests
{
    private const string Wd = @"C:\demo\app";

    private static JsonElement J(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    [Theory]
    [InlineData("Read", "{\"file_path\":\"C:\\\\demo\\\\app\\\\src\\\\a.cs\"}", "src\\a.cs")]
    [InlineData("Read", "{\"file_path\":\"c:/DEMO/app/src/a.cs\"}", "src/a.cs")]
    [InlineData("Read", "{\"file_path\":\"D:\\\\other\\\\a.cs\"}", "D:\\other\\a.cs")]
    [InlineData("Read", "{\"file_path\":\"C:\\\\demo\\\\application\\\\a.cs\"}", "C:\\demo\\application\\a.cs")]
    [InlineData("Edit", "{\"file_path\":\"C:\\\\demo\\\\app\\\\b.cs\"}", "b.cs")]
    [InlineData("Write", "{\"file_path\":\"C:\\\\demo\\\\app\\\\b.cs\"}", "b.cs")]
    [InlineData("MultiEdit", "{\"file_path\":\"C:\\\\demo\\\\app\\\\b.cs\"}", "b.cs")]
    [InlineData("NotebookEdit", "{\"notebook_path\":\"C:\\\\demo\\\\app\\\\n.ipynb\"}", "n.ipynb")]
    [InlineData("Bash", "{\"command\":\"git status\"}", "git status")]
    [InlineData("PowerShell", "{\"command\":\"git status\"}", "git status")]
    [InlineData("Bash", "{\"command\":\"echo a\\necho b\"}", "echo a…")]
    [InlineData("Grep", "{\"pattern\":\"Foo\",\"path\":\"src\"}", "Foo · src")]
    [InlineData("Grep", "{\"pattern\":\"Foo\"}", "Foo")]
    [InlineData("Glob", "{\"pattern\":\"**/*.cs\"}", "**/*.cs")]
    [InlineData("WebFetch", "{\"url\":\"https://x.y\"}", "https://x.y")]
    [InlineData("WebSearch", "{\"query\":\"q\"}", "q")]
    [InlineData("Agent", "{\"description\":\"Resumir\"}", "Resumir")]
    [InlineData("Task", "{\"description\":\"Resumir\"}", "Resumir")]
    [InlineData("Skill", "{\"skill\":\"pdf\"}", "pdf")]
    [InlineData("TodoWrite", "{\"todos\":[]}", "")]
    [InlineData("mcp__srv__tool", "{\"n\":1,\"a\":\"b\"}", "b")]
    [InlineData("Mystery", "{\"x\":true,\"y\":\"first\",\"z\":\"second\"}", "first")]
    [InlineData("Mystery", "{\"x\":1}", "")]
    public void Summary_per_tool(string tool, string json, string expected)
        => Assert.Equal(expected, ToolPresentation.Summary(tool, J(json), Wd));

    [Fact]
    public void Summary_without_working_directory_keeps_the_path()
        => Assert.Equal(@"C:\demo\app\a.cs",
            ToolPresentation.Summary("Read", J("{\"file_path\":\"C:\\\\demo\\\\app\\\\a.cs\"}"), null));

    [Fact]
    public void McpName_splits_server_and_tool()
    {
        Assert.Equal(("claude_ai_Gmail", "search"), ToolPresentation.McpName("mcp__claude_ai_Gmail__search"));
        Assert.Null(ToolPresentation.McpName("Read"));
        Assert.Null(ToolPresentation.McpName("mcp__srv"));
    }

    [Fact]
    public void Edit_diff_keeps_three_context_lines()
    {
        var old = string.Join("\n", Enumerable.Range(1, 10).Select(i => i.ToString()));
        var neu = string.Join("\n", Enumerable.Range(1, 10).Select(i => i == 5 ? "five" : i.ToString()));
        var input = J(JsonSerializer.Serialize(new { file_path = "a", old_string = old, new_string = neu }));
        var diff = ToolPresentation.Diff("Edit", input)!;

        Assert.Equal(new[] { "2", "3", "4", "6", "7", "8" }, diff.Where(d => d.Kind == DiffKind.Context).Select(d => d.Text));
        Assert.Equal(new[] { "5" }, diff.Where(d => d.Kind == DiffKind.Removed).Select(d => d.Text));
        Assert.Equal(new[] { "five" }, diff.Where(d => d.Kind == DiffKind.Added).Select(d => d.Text));
        Assert.DoesNotContain(diff, d => d.Kind != DiffKind.Gap && (d.Text == "1" || d.Text == "9" || d.Text == "10"));
        Assert.Equal(DiffKind.Gap, diff.First().Kind);
        Assert.Equal(DiffKind.Gap, diff.Last().Kind);
    }

    [Fact]
    public void Diff_algorithm_middle_is_removed_then_added_without_gaps_when_nothing_is_left_over()
    {
        var input = J("{\"old_string\":\"a\\nb\\nc\",\"new_string\":\"a\\nX\\nY\\nc\"}");
        var diff = ToolPresentation.Diff("Edit", input)!;
        Assert.Equal(
            new[]
            {
                new DiffLine(DiffKind.Context, "a"),
                new DiffLine(DiffKind.Removed, "b"),
                new DiffLine(DiffKind.Added, "X"),
                new DiffLine(DiffKind.Added, "Y"),
                new DiffLine(DiffKind.Context, "c"),
            }, diff);
    }

    [Fact]
    public void Diff_with_only_additions_or_only_removals()
    {
        var add = ToolPresentation.Diff("Edit", J("{\"old_string\":\"a\",\"new_string\":\"a\\nb\"}"))!;
        Assert.Equal(new[] { new DiffLine(DiffKind.Context, "a"), new DiffLine(DiffKind.Added, "b") }, add);
        var del = ToolPresentation.Diff("Edit", J("{\"old_string\":\"a\\nb\",\"new_string\":\"a\"}"))!;
        Assert.Equal(new[] { new DiffLine(DiffKind.Context, "a"), new DiffLine(DiffKind.Removed, "b") }, del);
    }

    [Fact]
    public void MultiEdit_joins_edits_with_a_gap_and_replace_all_behaves_like_edit()
    {
        var multi = ToolPresentation.Diff("MultiEdit",
            J("{\"edits\":[{\"old_string\":\"a\",\"new_string\":\"b\"},{\"old_string\":\"c\",\"new_string\":\"d\"}]}"))!;
        Assert.Equal(
            new[]
            {
                new DiffLine(DiffKind.Removed, "a"), new DiffLine(DiffKind.Added, "b"),
                new DiffLine(DiffKind.Gap, ""),
                new DiffLine(DiffKind.Removed, "c"), new DiffLine(DiffKind.Added, "d"),
            }, multi);

        var all = ToolPresentation.Diff("Edit", J("{\"old_string\":\"a\",\"new_string\":\"b\",\"replace_all\":true}"))!;
        Assert.Equal(new[] { new DiffLine(DiffKind.Removed, "a"), new DiffLine(DiffKind.Added, "b") }, all);
    }

    [Fact]
    public void Write_diff_is_all_added_and_capped()
    {
        var content = string.Join("\n", Enumerable.Range(1, 50).Select(i => "l" + i));
        var diff = ToolPresentation.Diff("Write", J(JsonSerializer.Serialize(new { file_path = "a", content })))!;
        Assert.Equal(21, diff.Count);
        Assert.Equal(20, diff.Count(d => d.Kind == DiffKind.Added));
        Assert.Equal(new DiffLine(DiffKind.Gap, "+30"), diff.Last());
    }

    [Fact]
    public void Edit_diff_caps_each_side_of_a_huge_change()
    {
        var old = string.Join("\n", Enumerable.Range(1, 5000).Select(i => "old " + i));
        var neu = string.Join("\n", Enumerable.Range(1, 5000).Select(i => "new " + i));
        var input = J(JsonSerializer.Serialize(new { file_path = "a", old_string = old, new_string = neu }));

        var diff = ToolPresentation.Diff("Edit", input)!;

        var max = 2 * ToolPresentation.WriteLines + 2 + 2 * ToolPresentation.ContextLines;
        Assert.True(diff.Count <= max, $"{diff.Count} lines, more than {max}");
        Assert.Equal(Enumerable.Range(1, 20).Select(i => "old " + i),
            diff.Where(d => d.Kind == DiffKind.Removed).Select(d => d.Text));
        Assert.Equal(Enumerable.Range(1, 20).Select(i => "new " + i),
            diff.Where(d => d.Kind == DiffKind.Added).Select(d => d.Text));
        // each cut side is followed by its own "+N"
        Assert.Equal(new DiffLine(DiffKind.Gap, "+4980"), diff[20]);
        Assert.Equal(new DiffLine(DiffKind.Gap, "+4980"), diff[41]);
        Assert.Equal(42, diff.Count);
    }

    [Fact]
    public void Diff_without_a_limit_has_every_line_of_each_side()
    {
        // for the permission request: the user says yes to all of it (Ruling 30)
        var old = string.Join("\n", Enumerable.Range(1, 40).Select(i => "old " + i));
        var neu = string.Join("\n", Enumerable.Range(1, 40).Select(i => "new " + i));
        var edit = J(JsonSerializer.Serialize(new { file_path = "a", old_string = "keep\n" + old, new_string = "keep\n" + neu }));

        var whole = ToolPresentation.Diff("Edit", edit, maxLinesPerSide: null)!;
        Assert.Equal(Enumerable.Range(1, 40).Select(i => "old " + i), whole.Where(d => d.Kind == DiffKind.Removed).Select(d => d.Text));
        Assert.Equal(Enumerable.Range(1, 40).Select(i => "new " + i), whole.Where(d => d.Kind == DiffKind.Added).Select(d => d.Text));
        Assert.DoesNotContain(whole, d => d.Kind == DiffKind.Gap);
        Assert.Equal(new DiffLine(DiffKind.Context, "keep"), whole[0]); // the context rules stay

        var content = string.Join("\n", Enumerable.Range(1, 50).Select(i => "l" + i));
        var write = ToolPresentation.Diff("Write", J(JsonSerializer.Serialize(new { file_path = "a", content })), null)!;
        Assert.Equal(50, write.Count);
        Assert.All(write, d => Assert.Equal(DiffKind.Added, d.Kind));

        var multi = J(JsonSerializer.Serialize(new { edits = new[] { new { old_string = old, new_string = "short" } } }));
        Assert.Equal(40, ToolPresentation.Diff("MultiEdit", multi, null)!.Count(d => d.Kind == DiffKind.Removed));

        // a limit given works like the list's, and the two-argument call is the list's 20
        var five = ToolPresentation.Diff("Edit", edit, 5)!;
        Assert.Equal(5, five.Count(d => d.Kind == DiffKind.Removed));
        Assert.Equal(2, five.Count(d => d.Kind == DiffKind.Gap && d.Text == "+35"));
        Assert.Equal(ToolPresentation.Diff("Edit", edit), ToolPresentation.Diff("Edit", edit, ToolPresentation.WriteLines));
        Assert.Null(ToolPresentation.Diff("Read", edit, null));
    }

    [Fact]
    public void MultiEdit_caps_every_edit_and_keeps_context()
    {
        var big = string.Join("\n", Enumerable.Range(1, 30).Select(i => "x" + i));
        var input = J(JsonSerializer.Serialize(new
        {
            edits = new[]
            {
                new { old_string = "keep\n" + big, new_string = "keep\nshort" },
                new { old_string = "a", new_string = "b" },
            },
        }));

        var diff = ToolPresentation.Diff("MultiEdit", input)!;

        Assert.Equal(new DiffLine(DiffKind.Context, "keep"), diff[0]);
        Assert.Equal(20, diff.Count(d => d.Kind == DiffKind.Removed && d.Text.StartsWith("x")));
        Assert.Equal(new DiffLine(DiffKind.Gap, "+10"), diff[21]);
        Assert.Equal(new DiffLine(DiffKind.Added, "short"), diff[22]);
        Assert.Equal(new DiffLine(DiffKind.Gap, ""), diff[23]);
        Assert.Equal(new[] { new DiffLine(DiffKind.Removed, "a"), new DiffLine(DiffKind.Added, "b") }, diff.Skip(24));
    }

    [Fact]
    public void Diff_of_unrelated_tool_is_null()
        => Assert.Null(ToolPresentation.Diff("Read", J("{\"file_path\":\"a\"}")));

    [Fact]
    public void Todos_from_TodoWrite()
    {
        var todos = ToolPresentation.Todos("TodoWrite", J(
            "{\"todos\":[{\"content\":\"a\",\"status\":\"pending\"},{\"content\":\"b\",\"status\":\"in_progress\"},{\"content\":\"c\",\"status\":\"completed\"}]}"))!;
        Assert.Equal(
            new[]
            {
                new TodoEntry("a", TodoStatus.Pending),
                new TodoEntry("b", TodoStatus.InProgress),
                new TodoEntry("c", TodoStatus.Completed),
            }, todos);
        Assert.Null(ToolPresentation.Todos("Read", J("{\"todos\":[]}")));
    }

    [Fact]
    public void Truncate_cuts_and_counts_hidden_lines()
    {
        var text = string.Join("\n", Enumerable.Range(1, 30).Select(i => "l" + i));
        var t = ToolPresentation.Truncate(text, 12);
        Assert.Equal(18, t.HiddenLines);
        Assert.Equal(12, t.Text.Split('\n').Length);
        Assert.EndsWith("l12", t.Text);

        var small = ToolPresentation.Truncate("a\r\nb", 12);
        Assert.Equal(0, small.HiddenLines);
        Assert.Equal("a\r\nb", small.Text);
        Assert.Equal(1, ToolPresentation.Truncate("a\r\nb", 1).HiddenLines);

        var empty = ToolPresentation.Truncate("", 12);
        Assert.Equal("", empty.Text);
        Assert.Equal(0, empty.HiddenLines);
    }

    [Fact]
    public void Truncate_ten_thousand_lines_is_fast()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < 10000; i++) sb.Append("line ").Append(i).Append('\n');
        var text = sb.ToString();
        ToolPresentation.Truncate(text, 12); // warm up
        var sw = Stopwatch.StartNew();
        var t = ToolPresentation.Truncate(text, 12);
        sw.Stop();
        Assert.Equal(9988, t.HiddenLines);
        Assert.True(sw.ElapsedMilliseconds < 50, $"took {sw.ElapsedMilliseconds} ms");
    }

    // ---- the line under the tool name, like the extension ----

    [Theory]
    [InlineData("a", "b", 0, 0)]                 // same count: "Modified"
    [InlineData("a", "a\nb\nc", 2, 0)]
    [InlineData("a\nb\nc", "a", 0, 2)]
    [InlineData("", "a\n", 1, 0)]               // counted like the extension: "a\n" has 2 lines
    public void Edit_line_change_counts_lines_like_the_extension(string oldText, string newText, int added, int removed)
    {
        var input = J(System.Text.Json.JsonSerializer.Serialize(new { file_path = "a.cs", old_string = oldText, new_string = newText }));
        Assert.Equal(new LineChange(added, removed), ToolPresentation.EditLineChange("Edit", input));
    }

    [Fact]
    public void Edit_line_change_only_for_edits()
    {
        Assert.Null(ToolPresentation.EditLineChange("Write", J("{\"content\":\"a\"}")));
        Assert.Null(ToolPresentation.EditLineChange("Edit", J("{\"file_path\":\"a.cs\"}")));
        // MultiEdit: the edits add up
        var multi = J("{\"edits\":[{\"old_string\":\"a\",\"new_string\":\"a\\nb\"},{\"old_string\":\"c\\nd\\ne\",\"new_string\":\"c\"}]}");
        Assert.Equal(new LineChange(1, 2), ToolPresentation.EditLineChange("MultiEdit", multi));
    }

    [Theory]
    [InlineData("a", 1)]
    [InlineData("a\nb", 2)]
    [InlineData("a\nb\n", 3)] // like the extension's split on line feeds
    [InlineData("", 1)]
    public void Write_line_count(string content, int lines)
    {
        var input = J(System.Text.Json.JsonSerializer.Serialize(new { file_path = "a.cs", content }));
        Assert.Equal(lines, ToolPresentation.WriteLineCount("Write", input));
    }

    [Fact]
    public void Write_line_count_only_for_writes_with_content()
    {
        Assert.Null(ToolPresentation.WriteLineCount("Edit", J("{\"content\":\"a\"}")));
        Assert.Null(ToolPresentation.WriteLineCount("Write", J("{\"file_path\":\"a.cs\"}")));
    }

    [Theory]
    [InlineData("{\"file_path\":\"a.cs\",\"offset\":1,\"limit\":60}", 2, 61)]
    [InlineData("{\"file_path\":\"a.cs\",\"offset\":0,\"limit\":10}", 1, 10)]
    [InlineData("{\"file_path\":\"a.cs\",\"offset\":1000}", 1001, null)]
    public void Read_range(string json, int from, int? to)
    {
        Assert.Equal(new LineRange(from, to), ToolPresentation.ReadRange("Read", J(json)));
    }

    [Theory]
    [InlineData("Read", "{\"file_path\":\"a.cs\"}")]
    [InlineData("Read", "{\"file_path\":\"a.cs\",\"limit\":10}")]
    [InlineData("Read", "{\"file_path\":\"a.cs\",\"offset\":\"1\"}")]
    [InlineData("Edit", "{\"file_path\":\"a.cs\",\"offset\":1,\"limit\":2}")]
    public void Read_range_only_with_an_offset(string tool, string json)
    {
        Assert.Null(ToolPresentation.ReadRange(tool, J(json)));
    }

    [Theory]
    [InlineData("[1,2]")]
    [InlineData("\"text\"")]
    [InlineData("null")]
    [InlineData("{\"file_path\":5,\"command\":[],\"pattern\":{},\"old_string\":1,\"new_string\":2,\"edits\":4,\"content\":7,\"todos\":3}")]
    [InlineData("{}")]
    public void Bad_input_never_throws(string json)
    {
        var input = J(json);
        foreach (var tool in new[] { "Read", "Edit", "Write", "MultiEdit", "NotebookEdit", "Bash", "PowerShell", "Grep", "Glob", "WebFetch", "WebSearch", "Agent", "Task", "Skill", "TodoWrite", "mcp__a__b", "Other" })
        {
            Assert.Equal("", ToolPresentation.Summary(tool, input, Wd));
            Assert.Null(ToolPresentation.Diff(tool, input));
            Assert.Null(ToolPresentation.Todos(tool, input));
            Assert.Null(ToolPresentation.EditLineChange(tool, input));
            Assert.Null(ToolPresentation.WriteLineCount(tool, input));
            Assert.Null(ToolPresentation.ReadRange(tool, input));
        }
    }
}
