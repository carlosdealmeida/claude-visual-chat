using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace ClaudeVisualChat;

/// <summary>
/// What option 2 of a permission request says, built from the claude's <c>permission_suggestions</c>.
/// </summary>
/// <param name="Text">The whole sentence ("Yes, allow npm for this project (just you)").</param>
/// <param name="Destination">Where the rule is saved, as it ends <paramref name="Text"/> ("this project (just you)"); null when the sentence does not say.</param>
/// <param name="DestinationTip">The tooltip of <paramref name="Destination"/>; null with it.</param>
/// <param name="More">The rules that <paramref name="Text"/> does not name, for a tooltip of one line each; empty when it names them all.</param>
/// <param name="MoreText">The words that stand for <paramref name="More"/> in <paramref name="Text"/> ("2 more"); null when <paramref name="More"/> is empty.</param>
/// <param name="FullText">The sentence with every rule whole, when a rule in <paramref name="Text"/> was cut; null otherwise.</param>
internal readonly record struct AllowAlwaysLabel(
    string Text, string? Destination, string? DestinationTip, IReadOnlyList<string> More, string? MoreText = null, string? FullText = null);

/// <summary>Builds the <see cref="AllowAlwaysLabel"/> of a request.</summary>
internal static class AllowAlwaysLabels
{
    private const int RuleLimit = 20;
    private const int RuleKept = 17;

    /// <summary>The label of the suggestions; the plain "and don't ask again" when there is nothing to name.</summary>
    internal static AllowAlwaysLabel Of(JsonElement? suggestions, ChatStrings strings)
    {
        var rules = new List<Rule>();
        var directories = new List<string>();
        string? mode = null;
        var rank = -1;
        var destination = "session";
        if (suggestions is { ValueKind: JsonValueKind.Array } list)
        {
            foreach (var suggestion in list.EnumerateArray())
            {
                if (suggestion.ValueKind != JsonValueKind.Object) continue;
                var at = RankOf(String(suggestion, "destination"));
                if (at > rank)
                {
                    rank = at;
                    destination = String(suggestion, "destination")!;
                }
                switch (String(suggestion, "type"))
                {
                    case "addRules":
                        AddRules(suggestion, rules);
                        break;
                    case "addDirectories":
                        AddDirectories(suggestion, directories);
                        break;
                    case "setMode":
                        mode ??= String(suggestion, "mode");
                        break;
                }
            }
        }

        var where = Where(destination, strings);
        if (rules.Count > 0) return OfRules(rules, where, strings);
        if (directories.Count > 0) return OfDirectories(directories, where, strings);
        if (mode == "acceptEdits") return Plain(strings.AllowAlwaysAllEdits);
        if (mode == "default") return Plain(strings.AllowAlwaysNormalMode);
        return Plain(strings.PermissionYesAlwaysBeyondSession);
    }

    private static AllowAlwaysLabel Plain(string text) => new AllowAlwaysLabel(text, null, null, Array.Empty<string>());

    private static AllowAlwaysLabel OfRules(List<Rule> rules, (string Text, string Tip) where, ChatStrings strings)
    {
        string Names(int index, bool whole) => whole ? rules[index].Whole : rules[index].Short;
        string Build(bool whole, string? more)
        {
            if (rules.Count == 1) return Fill(strings.AllowAlwaysOneRule, Names(0, whole), where.Text);
            if (rules.Count == 2) return Fill(strings.AllowAlwaysTwoRules, Names(0, whole), Names(1, whole), where.Text);
            return Fill(strings.AllowAlwaysManyRules, Names(0, whole), more!, where.Text);
        }

        // three or more: the first by name, the others behind "N more"
        var moreText = rules.Count > 2 ? strings.FormatAllowAlwaysMore(rules.Count - 1) : null;
        var rest = new List<string>();
        for (var i = 1; i < rules.Count && rules.Count > 2; i++) rest.Add(rules[i].Whole);
        var cut = rules[0].Cut || (rules.Count == 2 && rules[1].Cut);
        return new AllowAlwaysLabel(Build(false, moreText), where.Text, where.Tip, rest, moreText, cut ? Build(true, moreText) : null);
    }

    private static AllowAlwaysLabel OfDirectories(List<string> directories, (string Text, string Tip) where, ChatStrings strings)
    {
        var text = directories.Count == 1
            ? Fill(strings.AllowAlwaysOneDirectory, LastFolder(directories[0]), where.Text)
            : Fill(strings.AllowAlwaysManyDirectories, strings.FormatNumber(directories.Count), where.Text);
        return new AllowAlwaysLabel(text, where.Text, where.Tip, Array.Empty<string>());
    }

    /// <summary>A rule as the label shows it (<see cref="Short"/>, cut when long) and whole.</summary>
    private readonly struct Rule
    {
        public Rule(string whole, bool cuttable)
        {
            Whole = whole;
            Cut = cuttable && whole.Length > RuleLimit;
            Short = Cut ? whole.Substring(0, char.IsHighSurrogate(whole[RuleKept - 1]) ? RuleKept - 1 : RuleKept) + "…" : whole; // not in the middle of a pair
        }

        public string Whole { get; }

        public string Short { get; }

        public bool Cut { get; }
    }

    private static void AddRules(JsonElement suggestion, List<Rule> rules)
    {
        if (!suggestion.TryGetProperty("rules", out var list) || list.ValueKind != JsonValueKind.Array) return;
        foreach (var rule in list.EnumerateArray())
        {
            if (rule.ValueKind != JsonValueKind.Object) continue;
            var content = String(rule, "ruleContent");
            if (string.IsNullOrEmpty(content))
            {
                var tool = String(rule, "toolName"); // a rule without content covers the whole tool
                if (!string.IsNullOrEmpty(tool)) rules.Add(new Rule(OneLine(tool!), cuttable: false));
            }
            else if (content!.Length > 2 && content.EndsWith(":*", StringComparison.Ordinal))
            {
                rules.Add(new Rule(OneLine(content.Substring(0, content.Length - 2)), cuttable: false)); // "npm:*" is npm
            }
            else
            {
                rules.Add(new Rule(OneLine(content), cuttable: true));
            }
        }
    }

    private static void AddDirectories(JsonElement suggestion, List<string> directories)
    {
        if (!suggestion.TryGetProperty("directories", out var list) || list.ValueKind != JsonValueKind.Array) return;
        foreach (var directory in list.EnumerateArray())
        {
            if (directory.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(directory.GetString()))
                directories.Add(directory.GetString()!);
        }
    }

    /// <summary>The last folder of a path and a slash: <c>C:\demo\data</c> gives <c>data/</c>.</summary>
    private static string LastFolder(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var at = trimmed.LastIndexOfAny(new[] { '\\', '/' });
        var name = OneLine(at >= 0 ? trimmed.Substring(at + 1) : trimmed);
        return (name.Length > 0 ? name : OneLine(path)) + "/";
    }

    /// <summary>Line breaks show as "⏎".</summary>
    private static string OneLine(string text) =>
        text.Replace("\r\n", "⏎").Replace('\n', '⏎').Replace('\r', '⏎');

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>user > project > local > session; -1 for a destination that is not offered.</summary>
    private static int RankOf(string? destination) => destination switch
    {
        "userSettings" => 3,
        "projectSettings" => 2,
        "localSettings" => 1,
        "session" => 0,
        _ => -1,
    };

    private static (string Text, string Tip) Where(string destination, ChatStrings strings) => destination switch
    {
        "userSettings" => (strings.AllowAlwaysAllProjects, strings.AllowAlwaysAllProjectsTip),
        "projectSettings" => (strings.AllowAlwaysThisProjectShared, strings.AllowAlwaysThisProjectSharedTip),
        "localSettings" => (strings.AllowAlwaysThisProjectJustYou, strings.AllowAlwaysThisProjectJustYouTip),
        _ => (strings.AllowAlwaysThisSession, strings.AllowAlwaysThisSessionTip),
    };

    /// <summary>The format with <c>{0}</c>, <c>{1}</c>… taken by the arguments, in one pass (an argument may hold a brace).</summary>
    private static string Fill(string format, params string[] arguments)
    {
        var text = new StringBuilder(format.Length + 32);
        for (var i = 0; i < format.Length; i++)
        {
            if (format[i] == '{' && i + 2 < format.Length && format[i + 2] == '}'
                && format[i + 1] >= '0' && format[i + 1] < '0' + arguments.Length)
            {
                text.Append(arguments[format[i + 1] - '0']);
                i += 2;
            }
            else
            {
                text.Append(format[i]);
            }
        }
        return text.ToString();
    }
}
