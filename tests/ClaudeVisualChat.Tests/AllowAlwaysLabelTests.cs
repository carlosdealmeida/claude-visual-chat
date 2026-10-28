using System.Text.Json;
using Xunit;

namespace ClaudeVisualChat.Tests;

public class AllowAlwaysLabelTests
{
    private static readonly ChatStrings En = ChatStrings.English;

    private static JsonElement J(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static string Rules(string destination, params string?[] contents)
    {
        var rules = string.Join(",", System.Array.ConvertAll(contents, c =>
            c == null ? "{\"toolName\":\"Bash\"}" : "{\"toolName\":\"Bash\",\"ruleContent\":" + JsonSerializer.Serialize(c) + "}"));
        return "{\"type\":\"addRules\",\"rules\":[" + rules + "],\"behavior\":\"allow\",\"destination\":\"" + destination + "\"}";
    }

    private static AllowAlwaysLabel Of(params string[] suggestions) =>
        AllowAlwaysLabels.Of(J("[" + string.Join(",", suggestions) + "]"), En);

    [Fact]
    public void A_command_prefix_rule_reads_allow_npm_for_this_project_just_you()
    {
        var label = Of(Rules("localSettings", "npm:*"));
        Assert.Equal("Yes, allow npm for this project (just you)", label.Text);
        Assert.Equal("this project (just you)", label.Destination);
        Assert.Equal("Saves to .claude/settings.local.json (gitignored)", label.DestinationTip);
        Assert.Empty(label.More);
        Assert.Null(label.FullText);
    }

    [Fact]
    public void A_long_rule_is_cut_at_seventeen_characters_and_an_ellipsis()
    {
        const string Command = "sleep 5 && echo done > done.txt";
        var label = Of(Rules("localSettings", Command));
        Assert.Equal("Yes, allow sleep 5 && echo d… for this project (just you)", label.Text);
        Assert.Equal("Yes, allow " + Command + " for this project (just you)", label.FullText); // cut: the whole is the tooltip

        // twenty characters still fit
        Assert.Equal("Yes, allow 12345678901234567890 for this session", Of(Rules("session", "12345678901234567890")).Text);
        // a prefix rule names the command and is not cut
        Assert.Equal("Yes, allow dotnet build --configuration for this session",
            Of(Rules("session", "dotnet build --configuration:*")).Text);
    }

    [Fact]
    public void A_rule_without_content_names_its_tool_and_line_breaks_show_as_a_symbol()
    {
        Assert.Equal("Yes, allow Bash for all projects", Of(Rules("userSettings", (string?)null)).Text);
        Assert.Equal("Yes, allow a⏎b for this session", Of(Rules("session", "a\nb")).Text);
        Assert.Equal("Yes, allow a⏎b for this session", Of(Rules("session", "a\r\nb")).Text);
    }

    [Fact]
    public void Two_rules_read_a_and_b()
    {
        var label = Of(Rules("projectSettings", "npm:*", "git status"));
        Assert.Equal("Yes, allow npm and git status for this project (shared)", label.Text);
        Assert.Equal("Saves to .claude/settings.json (shared with team)", label.DestinationTip);
        Assert.Empty(label.More);
    }

    [Fact]
    public void Three_rules_read_and_two_more_with_the_rest_in_the_tooltip()
    {
        var label = Of(Rules("localSettings", "npm:*", "git status", "dotnet build --no-restore"));
        Assert.Equal("Yes, allow npm and 2 more for this project (just you)", label.Text);
        Assert.Equal("2 more", label.MoreText);
        Assert.Equal(new[] { "git status", "dotnet build --no-restore" }, label.More); // whole, not cut
    }

    [Fact]
    public void The_rules_of_several_suggestions_count_together()
    {
        var label = Of(Rules("session", "a"), Rules("session", "b"));
        Assert.Equal("Yes, allow a and b for this session", label.Text);
    }

    [Fact]
    public void A_directory_reads_allow_access_to_its_last_folder()
    {
        const string Directories = "{\"type\":\"addDirectories\",\"directories\":[\"C:\\\\demo\\\\data\"],\"destination\":\"session\"}";
        Assert.Equal("Yes, allow access to data/ for this session", Of(Directories).Text);
        Assert.Equal("Yes, allow access to data/ for this session",
            Of("{\"type\":\"addDirectories\",\"directories\":[\"/home/me/data/\"],\"destination\":\"session\"}").Text);
        Assert.Equal("Yes, allow access to 2 directories for this session",
            Of("{\"type\":\"addDirectories\",\"directories\":[\"C:\\\\a\",\"C:\\\\b\"],\"destination\":\"session\"}").Text);
        Assert.Equal("Yes, allow access to 1,200 directories for this session",
            Of("{\"type\":\"addDirectories\",\"directories\":[" + string.Join(",", System.Linq.Enumerable.Repeat("\"C:\\\\x\"", 1200)) + "],\"destination\":\"session\"}").Text);
    }

    [Fact]
    public void Rules_come_before_directories()
    {
        var label = Of(Rules("localSettings", "sleep 5"), "{\"type\":\"addDirectories\",\"directories\":[\"C:\\\\demo\\\\app\"],\"destination\":\"session\"}");
        Assert.Equal("Yes, allow sleep 5 for this project (just you)", label.Text);
    }

    [Fact]
    public void Only_set_mode_accept_edits_reads_allow_all_edits_this_session()
    {
        var label = Of("{\"type\":\"setMode\",\"mode\":\"acceptEdits\",\"destination\":\"session\"}");
        Assert.Equal("Yes, allow all edits this session", label.Text);
        Assert.Null(label.Destination);
        Assert.Null(label.DestinationTip);
        Assert.Equal("Yes, return to normal mode", Of("{\"type\":\"setMode\",\"mode\":\"default\",\"destination\":\"session\"}").Text);
        Assert.Equal("Yes, and don't ask again", Of("{\"type\":\"setMode\",\"mode\":\"plan\",\"destination\":\"session\"}").Text);
    }

    [Fact]
    public void Without_a_suggestion_to_name_it_reads_and_dont_ask_again()
    {
        Assert.Equal("Yes, and don't ask again", AllowAlwaysLabels.Of(null, En).Text);
        Assert.Equal("Yes, and don't ask again", Of().Text);
        Assert.Equal("Yes, and don't ask again", Of("{\"type\":\"somethingNew\",\"destination\":\"session\"}").Text);
        Assert.Equal("Yes, and don't ask again", AllowAlwaysLabels.Of(J("{\"a\":1}"), En).Text);
        Assert.Equal("Yes, and don't ask again", Of("{\"type\":\"addRules\",\"rules\":[],\"destination\":\"session\"}").Text);
    }

    [Theory]
    [InlineData("localSettings", "this project (just you)", "Saves to .claude/settings.local.json (gitignored)")]
    [InlineData("userSettings", "all projects", "Saves to ~/.claude/settings.json")]
    [InlineData("projectSettings", "this project (shared)", "Saves to .claude/settings.json (shared with team)")]
    [InlineData("session", "this session", "Only for this session (not saved)")]
    public void Each_destination_has_its_words_and_its_tooltip(string destination, string words, string tip)
    {
        var label = Of(Rules(destination, "npm:*"));
        Assert.Equal(words, label.Destination);
        Assert.Equal(tip, label.DestinationTip);
        Assert.EndsWith(" for " + words, label.Text);
    }

    [Fact]
    public void The_highest_destination_wins()
    {
        var label = Of(Rules("session", "a"), Rules("userSettings", "b"));
        Assert.Equal("all projects", label.Destination);
        Assert.Equal("Yes, allow a and b for all projects", label.Text);
        Assert.Equal("this project (shared)", Of(Rules("localSettings", "a"), Rules("projectSettings", "b")).Destination);
        Assert.Equal("this project (just you)", Of(Rules("session", "a"), Rules("localSettings", "b")).Destination);
        // the order they come in does not matter, and a destination that is not offered or missing is ignored
        Assert.Equal("all projects", Of(Rules("userSettings", "a"), Rules("session", "b")).Destination);
        Assert.Equal("this session", Of(Rules("cliArg", "a")).Destination);
        Assert.Equal("this project (just you)", Of(Rules("cliArg", "a"), Rules("localSettings", "b")).Destination);
        Assert.Equal("this session",
            Of("{\"type\":\"addRules\",\"rules\":[{\"toolName\":\"Bash\",\"ruleContent\":\"a\"}]}").Destination);
    }

    [Fact]
    public void The_recorded_request_of_the_claude_2_1_310_reads_the_rule()
    {
        // permission_suggestions as the claude 2.1.310 sent them (gravacoes §1): a rule kept for the user, and a folder for the session
        var label = Of(Rules("localSettings", "sleep 5 && echo done > done.txt"),
            "{\"type\":\"addDirectories\",\"directories\":[\"C:\\\\demo\\\\app\"],\"destination\":\"session\"}");
        Assert.Equal("Yes, allow sleep 5 && echo d… for this project (just you)", label.Text);
    }

    [Fact]
    public void The_words_follow_the_language()
    {
        var label = AllowAlwaysLabels.Of(J("[" + Rules("localSettings", "npm:*", "git status", "ls") + "]"), ChatStrings.PortugueseBrazil);
        Assert.Equal("Sim, permitir npm e mais 2 para este projeto (só você)", label.Text);
        Assert.Equal("mais 2", label.MoreText);
        Assert.Equal("Grava em .claude/settings.local.json (fora do git)", label.DestinationTip);
    }

    [Fact]
    public void A_brace_in_a_rule_is_kept_as_it_is()
    {
        Assert.Equal("Yes, allow echo {1}{0} for this session", Of(Rules("session", "echo {1}{0}")).Text);
    }
}
