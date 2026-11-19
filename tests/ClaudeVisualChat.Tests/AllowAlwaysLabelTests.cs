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
    public void A_rule_is_never_cut_in_the_middle_of_a_surrogate_pair()
    {
        // 16 letters and an emoji (two chars): the cut would fall between the halves
        var label = Of(Rules("session", new string('a', 16) + "😀 and the rest"));
        Assert.Equal("Yes, allow " + new string('a', 16) + "… for this session", label.Text);
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
        Assert.Null(label.ModeText); // the sentence is the mode's own
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

    private static string SetMode(string mode, string destination = "session") =>
        "{\"type\":\"setMode\",\"mode\":\"" + mode + "\",\"destination\":\"" + destination + "\"}";

    [Fact]
    public void A_rule_with_a_mode_change_to_accept_edits_also_says_all_edits_are_allowed()
    {
        // a command that touches a file: a rule kept in the local settings and acceptEdits for the session; answering
        // "always" applies both, so the sentence names both
        var label = Of(Rules("localSettings", "touch a.txt"), SetMode("acceptEdits"));
        Assert.Equal("Yes, allow touch a.txt for this project (just you), and all edits for this session", label.Text);
        Assert.Equal("this project (just you)", label.Destination);
        Assert.Equal("Saves to .claude/settings.local.json (gitignored)", label.DestinationTip);
        Assert.Equal(", and all edits for this session", label.ModeText);
        // the place is followed only by the mode's words
        Assert.EndsWith(label.Destination + label.ModeText, label.Text);
        Assert.Empty(label.More);
        Assert.Null(label.FullText);
    }

    [Fact]
    public void Directories_with_a_mode_change_also_say_what_the_mode_does()
    {
        const string Directory = "{\"type\":\"addDirectories\",\"directories\":[\"C:\\\\demo\\\\data\"],\"destination\":\"session\"}";
        var label = Of(Directory, SetMode("acceptEdits"));
        Assert.Equal("Yes, allow access to data/ for this session, and all edits for this session", label.Text);
        Assert.Equal("this session", label.Destination);
        Assert.Equal(", and all edits for this session", label.ModeText);
        Assert.Equal("Yes, allow access to 2 directories for this session, and return to normal mode for this session",
            Of("{\"type\":\"addDirectories\",\"directories\":[\"C:\\\\a\",\"C:\\\\b\"],\"destination\":\"session\"}", SetMode("default")).Text);
    }

    [Fact]
    public void A_rule_with_a_mode_change_to_default_says_it_returns_to_normal_mode()
    {
        var label = Of(Rules("session", "npm:*"), SetMode("default"));
        Assert.Equal("Yes, allow npm for this session, and return to normal mode for this session", label.Text);
        Assert.Equal(", and return to normal mode for this session", label.ModeText);
    }

    [Fact]
    public void A_rule_with_any_other_mode_change_names_the_mode()
    {
        Assert.Equal("Yes, allow npm for this session, and switch to Plan mode for this session",
            Of(Rules("session", "npm:*"), SetMode("plan")).Text);
        Assert.Equal("Yes, allow npm for this session, and switch to Auto mode for this session",
            Of(Rules("session", "npm:*"), SetMode("auto")).Text);
        // a mode the library has no name for shows as the claude wrote it
        Assert.Equal("Yes, allow npm for this session, and switch to bypassPermissions mode for this session",
            Of(Rules("session", "npm:*"), SetMode("bypassPermissions")).Text);
        Assert.Equal("Yes, allow npm for this session, and switch to a⏎b mode for this session",
            Of(Rules("session", "npm:*"), "{\"type\":\"setMode\",\"mode\":\"a\\nb\",\"destination\":\"session\"}").Text);
    }

    [Fact]
    public void A_long_mode_name_is_cut_as_a_long_rule_is_and_shows_whole_in_the_full_text()
    {
        const string Mode = "aVeryLongCustomPermissionMode";
        var label = Of(Rules("session", "npm:*"), SetMode(Mode));
        Assert.Equal("Yes, allow npm for this session, and switch to aVeryLongCustomPe… mode for this session", label.Text);
        Assert.Equal(", and switch to aVeryLongCustomPe… mode for this session", label.ModeText);
        Assert.Equal("Yes, allow npm for this session, and switch to " + Mode + " mode for this session", label.FullText);

        // twenty characters still fit, and nothing was cut: no full text
        var fits = Of(Rules("session", "npm:*"), SetMode("12345678901234567890"));
        Assert.Equal("Yes, allow npm for this session, and switch to 12345678901234567890 mode for this session", fits.Text);
        Assert.Null(fits.FullText);

        // a cut rule and a cut mode: both whole in the full text
        const string Command = "sleep 5 && echo done > done.txt";
        var both = Of(Rules("localSettings", Command), SetMode(Mode));
        Assert.Equal("Yes, allow sleep 5 && echo d… for this project (just you), and switch to aVeryLongCustomPe… mode for this session",
            both.Text);
        Assert.Equal("Yes, allow " + Command + " for this project (just you), and switch to " + Mode + " mode for this session", both.FullText);

        // the name the host gave a mode is cut too
        var strings = ChatStrings.English with { ModePlan = "Plan carefully before acting" };
        Assert.Equal("Yes, allow npm for this session, and switch to Plan carefully be… mode for this session",
            AllowAlwaysLabels.Of(J("[" + Rules("session", "npm:*") + "," + SetMode("plan") + "]"), strings).Text);
    }

    [Fact]
    public void A_brace_in_a_mode_name_is_kept_as_it_is()
    {
        var label = Of(Rules("session", "npm:*"), SetMode("{1}{0}"));
        Assert.Equal("Yes, allow npm for this session, and switch to {1}{0} mode for this session", label.Text);
        Assert.Equal(", and switch to {1}{0} mode for this session", label.ModeText);
    }

    [Fact]
    public void The_mode_change_says_its_own_place_and_does_not_move_the_rule()
    {
        // the mode kept in the local settings: the rule stays in the session, and each part says where it goes
        var label = Of(Rules("session", "npm:*"), SetMode("acceptEdits", "localSettings"));
        Assert.Equal("Yes, allow npm for this session, and all edits for this project (just you)", label.Text);
        Assert.Equal("this session", label.Destination);
        Assert.Equal("Only for this session (not saved)", label.DestinationTip);
        // a mode change with no place, or one that is not offered, lasts the session
        Assert.Equal(", and all edits for this session",
            Of(Rules("userSettings", "npm:*"), "{\"type\":\"setMode\",\"mode\":\"acceptEdits\"}").ModeText);
        Assert.Equal(", and all edits for this session", Of(Rules("userSettings", "npm:*"), SetMode("acceptEdits", "cliArg")).ModeText);
    }

    [Fact]
    public void Of_several_mode_changes_the_last_valid_one_is_named()
    {
        // the claude applies them in order: it ends in the mode of the last one
        Assert.Equal(", and return to normal mode for this session",
            Of(Rules("session", "npm:*"), SetMode("acceptEdits"), SetMode("default")).ModeText);
        Assert.Equal(", and all edits for this project (just you)",
            Of(SetMode("default"), Rules("session", "npm:*"), SetMode("acceptEdits", "localSettings")).ModeText);
        // one without a mode is no mode change, and does not hide the one before it
        Assert.Equal(", and all edits for this session",
            Of(Rules("session", "npm:*"), SetMode("acceptEdits"), "{\"type\":\"setMode\",\"destination\":\"session\"}",
                "{\"type\":\"setMode\",\"mode\":7,\"destination\":\"session\"}").ModeText);
        // only mode changes: the sentence of the last one
        Assert.Equal("Yes, return to normal mode", Of(SetMode("acceptEdits"), SetMode("default")).Text);
        Assert.Equal("Yes, allow all edits this session", Of(SetMode("default"), SetMode("acceptEdits")).Text);
    }

    [Fact]
    public void A_mode_change_that_cannot_be_read_adds_nothing_and_never_throws()
    {
        foreach (var odd in new[]
        {
            "{\"type\":\"setMode\",\"destination\":\"session\"}",                  // no mode
            "{\"type\":\"setMode\",\"mode\":null,\"destination\":\"session\"}",    // not a string
            "{\"type\":\"setMode\",\"mode\":\"\",\"destination\":\"session\"}",    // empty
            "{\"type\":\"setMode\",\"mode\":[\"acceptEdits\"]}",
            "{\"type\":\"setModes\",\"mode\":\"acceptEdits\",\"destination\":\"session\"}", // a type the library does not know
            "\"setMode\"", "7", "null",                                           // not an object
        })
        {
            var label = Of(Rules("localSettings", "npm:*"), odd);
            Assert.Equal("Yes, allow npm for this project (just you)", label.Text);
            Assert.Null(label.ModeText);
        }
    }

    [Fact]
    public void A_cut_rule_and_more_rules_keep_their_meaning_beside_a_mode_change()
    {
        const string Command = "sleep 5 && echo done > done.txt";
        var cut = Of(Rules("localSettings", Command), SetMode("acceptEdits"));
        Assert.Equal("Yes, allow sleep 5 && echo d… for this project (just you), and all edits for this session", cut.Text);
        Assert.Equal("Yes, allow " + Command + " for this project (just you), and all edits for this session", cut.FullText);

        var many = Of(Rules("localSettings", "npm:*", "git status", "ls"), SetMode("acceptEdits"));
        Assert.Equal("Yes, allow npm and 2 more for this project (just you), and all edits for this session", many.Text);
        Assert.Equal("2 more", many.MoreText);
        Assert.Equal(new[] { "git status", "ls" }, many.More);
    }

    [Fact]
    public void A_host_that_changed_the_sentences_keeps_them_and_can_change_the_mode_words()
    {
        var strings = ChatStrings.English with { AllowAlwaysOneRule = "Always {0} ({1})" };
        var label = AllowAlwaysLabels.Of(J("[" + Rules("session", "npm:*") + "," + SetMode("acceptEdits") + "]"), strings);
        Assert.Equal("Always npm (this session), and all edits for this session", label.Text);
        Assert.Equal("this session", label.Destination);

        strings = ChatStrings.English with
        {
            AllowAlwaysAndAllEdits = " + every edit ({0})", AllowAlwaysAndNormalMode = " + manual ({0})", AllowAlwaysAndMode = " + {0} ({1})",
        };
        Assert.Equal("Yes, allow npm for this session + every edit (this session)",
            AllowAlwaysLabels.Of(J("[" + Rules("session", "npm:*") + "," + SetMode("acceptEdits") + "]"), strings).Text);
        Assert.Equal("Yes, allow npm for this session + manual (this session)",
            AllowAlwaysLabels.Of(J("[" + Rules("session", "npm:*") + "," + SetMode("default") + "]"), strings).Text);
        Assert.Equal("Yes, allow npm for this session + Plan (this session)",
            AllowAlwaysLabels.Of(J("[" + Rules("session", "npm:*") + "," + SetMode("plan") + "]"), strings).Text);
    }

    [Fact]
    public void The_mode_words_follow_the_language()
    {
        var pt = ChatStrings.PortugueseBrazil;
        Assert.Equal("Sim, permitir touch a.txt para este projeto (só você), e todas as edições para esta sessão",
            AllowAlwaysLabels.Of(J("[" + Rules("localSettings", "touch a.txt") + "," + SetMode("acceptEdits") + "]"), pt).Text);
        Assert.Equal("Sim, permitir npm para esta sessão, e voltar ao modo normal para esta sessão",
            AllowAlwaysLabels.Of(J("[" + Rules("session", "npm:*") + "," + SetMode("default") + "]"), pt).Text);
        Assert.Equal("Sim, permitir npm para esta sessão, e usar o modo Plano para esta sessão",
            AllowAlwaysLabels.Of(J("[" + Rules("session", "npm:*") + "," + SetMode("plan") + "]"), pt).Text);
    }

    [Fact]
    public void The_recorded_request_of_the_claude_2_1_310_reads_the_rule()
    {
        // permission_suggestions as the claude 2.1.310 sent them (gravacoes §1): a rule kept in the local settings, and a folder for the session
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
