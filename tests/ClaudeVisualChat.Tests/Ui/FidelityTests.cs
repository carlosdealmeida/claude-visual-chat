using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using Xunit;
using AssistantBlock = ClaudeVisualChat.Protocol.AssistantBlock;
using ToolResultBlock = ClaudeVisualChat.Protocol.ToolResultBlock;
using ToolUseBlock = ClaudeVisualChat.Protocol.ToolUseBlock;
using UserBlock = ClaudeVisualChat.Protocol.UserBlock;

namespace ClaudeVisualChat.Tests.Ui;

/// <summary>
/// The measures of the Claude Code extension for VS Code (2.1.301, Dark Modern, 100 %) that must not regress: fonts,
/// colors, the user box, the timeline rows, the tool boxes and the reply box. The positions come from the CSS of the
/// extension's webview and from a print of it; the reference scene has the same parts as the print, with neutral texts.
/// Text is drawn by another engine than Chromium's, so positions are checked within 1 px.
/// </summary>
public class FidelityTests
{
    /// <summary>The print's panel: 1562 px wide (the print shows the first 1555), so the reply box sits at x = 441.</summary>
    private const double PanelWidth = 1562;

    private const double Tolerance = 1;

    private static JsonElement J(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static object? Resource(Control view, string key) =>
        view.TryFindResource(key, out var value) ? value : null;

    private static Color ColorOf(Control view, string key) =>
        Assert.IsAssignableFrom<ISolidColorBrush>(Resource(view, key)).Color;

    private static Rect BoundsIn(Visual visual, Visual root)
    {
        var origin = visual.TranslatePoint(default, root) ?? throw new InvalidOperationException("not in the tree");
        return new Rect(origin, visual.Bounds.Size);
    }

    private static void Near(double expected, double actual, string what) =>
        Assert.True(Math.Abs(expected - actual) <= Tolerance, $"{what}: expected {expected} ±{Tolerance}, got {actual}");

    private static T Part<T>(Visual root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    // ---- the reference scene: the parts of the print, top to bottom ----

    private const string Code = "            ";

    /// <summary>A time of the print's day in the machine's own time zone, so it shows as written on any machine.</summary>
    private static DateTimeOffset Local(int hour, int minute)
    {
        var time = new DateTime(2026, 10, 19, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(time, TimeZoneInfo.Local.GetUtcOffset(time));
    }

    private static ChatConversation ReferenceScene()
    {
        var c = new ChatConversation { Clock = () => Local(22, 27) };
        // an earlier turn, without times, so the list scrolls like the print's
        c.Items.Add(new UserMessageItem("earlier"));
        for (var i = 0; i < 12; i++) c.Items.Add(new AssistantTextItem { Text = "Earlier answer " + i });
        c.Submit("Can you take the screenshot");
        c.Apply(new AssistantBlock("m1", null, new ClaudeVisualChat.Protocol.TextContentBlock(
            "It passes: the behavior came from Task 5 and the test guards it. I change the `Offset` of the prompt's scroller to `SetCurrentValue`, as Task 5 does."))
        { Timestamp = Local(22, 23) });
        var file = "C:\\demo\\app\\src\\ClaudeVisualChat.Avalonia\\Controls\\PermissionPrompt.axaml.cs";
        c.Apply(new AssistantBlock("m2", null, new ToolUseBlock("t1", "Edit", J(JsonSerializer.Serialize(new
        {
            file_path = file,
            old_string = Code + "DetailScroller.Offset = default;",
            new_string = Code + "DetailScroller.SetCurrentValue(ScrollViewer.OffsetProperty, default(Vector)); // a new request starts at its top",
        })))));
        c.Apply(new UserBlock(null, new ToolResultBlock("t1", "ok", false), null, false, false));
        c.Apply(new AssistantBlock("m3", null, new ToolUseBlock("t2", "Read", J(JsonSerializer.Serialize(new { file_path = file, offset = 1, limit = 60 })))));
        c.Apply(new UserBlock(null, new ToolResultBlock("t2", "lines", false), null, false, false));
        c.Apply(new AssistantBlock("m4", null, new ClaudeVisualChat.Protocol.ThinkingBlock("")));
        c.Apply(new AssistantBlock("m5", null, new ToolUseBlock("t3", "Bash", J(JsonSerializer.Serialize(new
        {
            command = "dotnet build 2>&1 | grep -E \" error | warning \" | sort -u; dotnet build -c Release 2>&1 | grep -E \"error\"; for i in 1 2 3; do dotnet test --no-build 2>&1 | grep -E \"Passed|Failed\"; done",
            description = "Final Debug and Release build and three full test runs",
        })))));
        c.Apply(new AssistantBlock("m6", null, new ClaudeVisualChat.Protocol.ThinkingBlock("")));
        var lines = string.Join("\n", Enumerable.Range(1, 47).Select(i => i == 1 ? "param([string]$OutDir, [string]$Match = \"\")" : "# line " + i));
        c.Apply(new AssistantBlock("m7", null, new ToolUseBlock("t4", "Write", J(JsonSerializer.Serialize(new { file_path = "C:\\demo\\app\\print.ps1", content = lines })))));
        c.Apply(new UserBlock(null, new ToolResultBlock("t4", "written", false), null, false, false));
        c.Apply(new AssistantBlock("m8", null, new ToolUseBlock("t5", "PowerShell", J(JsonSerializer.Serialize(new
        {
            command = "$W = \"C:\\demo\\app\\ref\"; New-Item -ItemType Directory -Force $W | Out-Null; & \"C:\\demo\\app\\print.ps1\" -OutDir $W",
            description = "Takes the screenshot of the VS Code windows without clicking",
        })))));
        return c;
    }

    /// <summary>The scene at the end of its list, like the print.</summary>
    private static (ClaudeChatView View, Window Window) OpenScene(ChatConversation c, double width = PanelWidth, double height = 940)
    {
        var view = new ClaudeChatView { Conversation = c };
        var window = UiHelpers.Show(view, width, height);
        UiHelpers.Scroller(view).ScrollToEnd();
        UiHelpers.Pump();
        return (view, window);
    }

    /// <summary>The rows of the last turn, in order (the list reuses its controls, so they are found by item).</summary>
    private static List<ChatTimelineRow> Rows(ClaudeChatView view)
    {
        var items = view.Conversation!.Items;
        var start = 0;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] is UserMessageItem) start = i + 1;
        }
        var rows = view.GetVisualDescendants().OfType<ChatTimelineRow>().ToList();
        var result = new List<ChatTimelineRow>();
        for (var i = start; i < items.Count; i++)
        {
            if (ChatTimelineRow.IsTimeline(items[i])) result.Add(rows.First(r => ReferenceEquals(r.DataContext, items[i])));
        }
        return result;
    }

    // ---- fonts ----

    [AvaloniaFact]
    public void Fonts_are_the_ones_of_vscode_on_windows()
    {
        var c = ReferenceScene();
        var (view, window) = OpenScene(c);

        Assert.Equal("Segoe UI", Assert.IsType<FontFamily>(Resource(view, "ClaudeChat.FontFamily")).FamilyNames[0]);
        Assert.Equal("Consolas", Assert.IsType<FontFamily>(Resource(view, "ClaudeChat.MonoFontFamily")).FamilyNames[0]);
        Assert.Equal(13d, Resource(view, "ClaudeChat.FontSize"));
        Assert.Equal(19.5, Resource(view, "ClaudeChat.LineHeight")); // line-height 1.5
        Assert.Equal(11.05, Resource(view, "ClaudeChat.SmallFontSize")); // .85em: times, paths, the line under a tool
        Assert.Equal(11.7, Resource(view, "ClaudeChat.CodeFontSize")); // .9em: inline code
        Assert.Equal(14d, Resource(view, "ClaudeChat.EditorFontSize")); // the diff, like the editor
        Assert.Equal(14.3, Resource(view, "ClaudeChat.PromptFontSize")); // 1.1em: the permission request

        var user = view.GetVisualDescendants().OfType<SelectableTextBlock>().Single(t => t.Text == "Can you take the screenshot");
        Assert.Equal(13, user.FontSize);
        Assert.Equal(19.5, user.LineHeight);
        var path = view.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "C:\\demo\\app\\print.ps1");
        Assert.Equal(11.05, path.FontSize);
        Assert.Equal("Consolas", path.FontFamily.FamilyNames[0]);
        var name = view.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Write");
        Assert.Equal(FontWeight.Bold, name.FontWeight);
        window.Close();
    }

    // ---- colors ----

    /// <summary>The colors that come from VS Code's theme, in "Dark 2026" (the default, the print's) and "Dark Modern".</summary>
    private static readonly (string Key, string Dark2026, string DarkModern)[] ThemeColors =
    {
        ("ClaudeChat.Background", "#191A1B", "#1F1F1F"),          // sideBar.background / editor.background
        ("ClaudeChat.Foreground", "#BFBFBF", "#CCCCCC"),          // foreground
        ("ClaudeChat.ForegroundMuted", "#8C8C8C", "#9D9D9D"),     // descriptionForeground
        ("ClaudeChat.Accent", "#48A0C7", "#4DAAFC"),              // textLink.foreground
        ("ClaudeChat.AccentFill", "#297AA0", "#0078D4"),          // button.background
        ("ClaudeChat.FocusBorder", "#B33994BC", "#0078D4"),       // focusBorder
        ("ClaudeChat.Error", "#F48771", "#F85149"),               // errorForeground
        ("ClaudeChat.InputBackground", "#191A1B", "#313131"),     // input.background
        ("ClaudeChat.InputBorder", "#2A2B2C", "#33CCCCCC"),       // inlineChatInput.border = editorWidget.border
        ("ClaudeChat.InputActiveBorder", "#2A2B2C", "#2488DB"),   // inputOption.activeBorder
        ("ClaudeChat.PermissionActiveBorder", "#A62A2B2C", "#A62488DB"),
        ("ClaudeChat.InputPlaceholder", "#555555", "#989898"),    // input.placeholderForeground
        ("ClaudeChat.TimelineLine", "#2A2B2C", "#2B2B2B"),        // sideBarActivityBarTop.border
        ("ClaudeChat.Dot", "#8C8C8C", "#9D9D9D"),
        ("ClaudeChat.ToolBackground", "#121314", "#1F1F1F"),      // editor.background
        ("ClaudeChat.CodeBackground", "#262626", "#3C3C3C"),      // textPreformat.background
        ("ClaudeChat.CodeForeground", "#8C8C8C", "#D0D0D0"),      // textPreformat.foreground
        ("ClaudeChat.CodeBlockBackground", "#242526", "#2B2B2B"), // textCodeBlock.background
        ("ClaudeChat.PillBackground", "#1ABFBFBF", "#1ACCCCCC"),
        ("ClaudeChat.PillHoverBackground", "#2EBFBFBF", "#2ECCCCCC"),
    };

    /// <summary>The extension's own colors, the same in both.</summary>
    private static readonly (string Key, string Color)[] OwnColors =
    {
        ("ClaudeChat.DotSucceeded", "#74C991"),
        ("ClaudeChat.DotFailed", "#C74E39"),
        ("ClaudeChat.DotRejected", "#E1C08D"),
        ("ClaudeChat.DiffBackground", "#1E1E1E"),   // the diff's editor (vs-dark)
        ("ClaudeChat.DiffForeground", "#D4D4D4"),
        ("ClaudeChat.DiffRemoved", "#33FF0000"),
        ("ClaudeChat.DiffAdded", "#339BB955"),
        ("ClaudeChat.DiffHatch", "#33CCCCCC"),      // diffEditor.diagonalFill
        ("ClaudeChat.SendBackground", "#C6613F"),
        ("ClaudeChat.SendForeground", "#FAF9F5"),
        ("ClaudeChat.FocusRing", "#D97757"),
        ("ClaudeChat.Spinner", "#D97757"),
    };

    [AvaloniaFact]
    public void Colors_are_the_dark_2026_values_of_the_extension_variables()
    {
        var view = new ClaudeChatView { Conversation = new ChatConversation() };
        var window = UiHelpers.Show(view);
        foreach (var (key, color, _) in ThemeColors) Assert.True(Color.Parse(color) == ColorOf(view, key), key + " is " + ColorOf(view, key));
        foreach (var (key, color) in OwnColors) Assert.True(Color.Parse(color) == ColorOf(view, key), key + " is " + ColorOf(view, key));
        Assert.Equal(Color.Parse("#191A1B"), Assert.IsAssignableFrom<ISolidColorBrush>(Assert.IsAssignableFrom<Border>(view.Content).Background).Color);
        Assert.Equal(BoxShadows.Parse("0 0 0 3 #153994BC"), Resource(view, "ClaudeChat.ComposerFocusShadowPlan"));
        window.Close();
    }

    [AvaloniaFact]
    public void The_dark_modern_palette_swaps_the_theme_colors()
    {
        var view = new ClaudeChatView { Conversation = new ChatConversation() };
        view.Resources.MergedDictionaries.Add(new Avalonia.Markup.Xaml.Styling.ResourceInclude(new Uri("avares://ClaudeVisualChat.Avalonia/"))
        {
            Source = new Uri("avares://ClaudeVisualChat.Avalonia/Themes/DarkModern.axaml"),
        });
        var window = UiHelpers.Show(view);
        foreach (var (key, _, color) in ThemeColors) Assert.True(Color.Parse(color) == ColorOf(view, key), key + " is " + ColorOf(view, key));
        foreach (var (key, color) in OwnColors) Assert.True(Color.Parse(color) == ColorOf(view, key), key + " is " + ColorOf(view, key));
        // what is drawn follows: the background, the user's box
        Assert.Equal(Color.Parse("#1F1F1F"), Assert.IsAssignableFrom<ISolidColorBrush>(Assert.IsAssignableFrom<Border>(view.Content).Background).Color);
        Assert.Equal(BoxShadows.Parse("0 0 0 3 #1F0078D4"), Resource(view, "ClaudeChat.ComposerFocusShadowPlan"));
        view.Conversation!.Submit("hello");
        UiHelpers.Pump();
        var box = view.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatUser"));
        Assert.Equal(Color.Parse("#313131"), Assert.IsAssignableFrom<ISolidColorBrush>(box.Background).Color);
        window.Close();
    }

    // ---- the user box ----

    [AvaloniaFact]
    public void User_message_is_a_full_width_box_with_its_time_above()
    {
        var c = ReferenceScene();
        var (view, window) = OpenScene(c);
        var box = view.GetVisualDescendants().OfType<Border>()
            .Single(b => b.Classes.Contains("ClaudeChatUser") && ReferenceEquals(b.DataContext, c.Items.OfType<UserMessageItem>().Last()));

        Assert.Equal(new Thickness(6, 4), box.Padding);
        Assert.Equal(new Thickness(1), box.BorderThickness);
        Assert.Equal(new CornerRadius(6), box.CornerRadius);
        Assert.Same(Resource(view, "ClaudeChat.InputBackground"), box.Background);
        Assert.Same(Resource(view, "ClaudeChat.InputBorder"), box.BorderBrush);

        var bounds = BoundsIn(box, view);
        Near(20, bounds.Left, "box left");
        Near(PanelWidth - 20 - 16, bounds.Right, "box right (16 px kept for the scroll bar)");
        Near(30, bounds.Height, "box height");
        var turnPanel = box.GetVisualAncestors().OfType<StackPanel>().First(s => s.Classes.Contains("ClaudeChatUserTurn"));
        var turn = BoundsIn(turnPanel, view);
        Near(16 + 2, bounds.Top - turn.Top, "box under the time: a 16 px line and 2 px");
        Assert.Equal(new Thickness(0, 14, 0, 12), turnPanel.Margin); // the turn's header: 14 px over, 12 under
        Near(turn.Bottom + 12, BoundsIn(Rows(view)[0], view).Top, "the answer 12 px under the user's turn");

        var stamp = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("ClaudeChatStamp") && t.Text == "10:27 PM");
        Assert.Equal(11.05, stamp.FontSize);
        Near(bounds.Right, BoundsIn(stamp, view).Right, "time right edge");
        window.Close();
    }

    // ---- the timeline ----

    [AvaloniaFact]
    public void Rows_follow_one_another_like_in_the_print()
    {
        var c = ReferenceScene();
        var (view, window) = OpenScene(c);
        var rows = Rows(view);
        Assert.Equal(8, rows.Count); // text, Edit, Read, thinking, Bash, thinking, Write, PowerShell

        // the print: dots at y = 142, 181, 316, 352, 390, 476, 513, 657 (a dot is 15 px under its row's top, 31 under a timed one)
        var dots = rows.Select(r => BoundsIn(Part<Ellipse>(r, "PART_Dot"), view)).ToList();
        var printDots = new[] { 142, 181, 316, 352, 390, 476, 513, 657 };
        for (var i = 1; i < dots.Count; i++)
            Near(printDots[i] - printDots[0], dots[i].Top - dots[0].Top, $"dot {i} from the first");
        Assert.All(dots, d => Assert.Equal(new Size(7, 7), d.Size));
        Assert.All(dots, d => Near(20 + 9, d.Left, "dot left"));

        var firstRow = BoundsIn(rows[0], view);
        Near(31, dots[0].Top - firstRow.Top, "dot of a timed answer");
        var stamp = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("ClaudeChatStamp") && t.Text == "10:23 PM");
        Near(6, BoundsIn(stamp, view).Top - firstRow.Top, "answer time");
        window.Close();
    }

    [AvaloniaFact]
    public void The_line_joins_the_dots_of_a_run()
    {
        var c = ReferenceScene();
        var (view, window) = OpenScene(c);
        var rows = Rows(view);
        var lines = rows.Select(r => Part<Rectangle>(r, "PART_Line")).ToList();
        Assert.Same(Resource(view, "ClaudeChat.TimelineLine"), lines[0].Fill);
        Assert.All(lines, l => Near(20 + 12, BoundsIn(l, view).Left, "line left"));
        Assert.All(lines, l => Assert.Equal(1, l.Bounds.Width));

        // the first row of a run starts the line under its dot; the last one stops at 18 px
        Near(34, BoundsIn(lines[0], view).Top - BoundsIn(rows[0], view).Top, "line start under a timed dot");
        Near(rows[0].Bounds.Height - 34, lines[0].Bounds.Height, "line to the end of the first row");
        Near(0, BoundsIn(lines[3], view).Top - BoundsIn(rows[3], view).Top, "line through a middle row");
        Near(rows[3].Bounds.Height, lines[3].Bounds.Height, "line through a middle row");
        Near(18, lines[7].Bounds.Height, "line of the last row");

        window.Close();

        // a lone row has no line; a row that gets a neighbor gets it
        var lone = new ChatConversation();
        lone.Submit("hi");
        lone.Apply(new AssistantBlock("m1", null, new ClaudeVisualChat.Protocol.TextContentBlock("hello")));
        lone.Submit("again");
        (view, window) = OpenScene(lone);
        var hello = view.GetVisualDescendants().OfType<ChatTimelineRow>().Single(r => r.DataContext is AssistantTextItem);
        Assert.False(Part<Rectangle>(hello, "PART_Line").IsEffectivelyVisible);
        lone.Items.Insert(2, new ThinkingItem());
        UiHelpers.Pump();
        Assert.True(Part<Rectangle>(hello, "PART_Line").IsEffectivelyVisible);
        Near(8 + 10, Part<Rectangle>(hello, "PART_Line").Margin.Top, "the run starts 3 px under the dot of its first entry");
        window.Close();
    }

    [AvaloniaFact]
    public void Tool_rows_have_the_extension_measures()
    {
        var c = ReferenceScene();
        var (view, window) = OpenScene(c);
        var rows = Rows(view);
        Rect Box(int row) => BoundsIn(rows[row].GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("ClaudeChatToolBody")), view);
        double Top(int row) => BoundsIn(rows[row], view).Top;

        // a tool with only its header line: 8 + 19.5 + 8
        Near(36, rows[2].Bounds.Height, "Read row");

        // Edit: header, "Modified", the diff box (1 + 58 + 1)
        Near(225 - 166, Box(1).Top - Top(1), "Edit box top");
        Near(60, Box(1).Height, "Edit box height");
        Assert.Contains("Modified", UiHelpers.Texts(rows[1]));

        // Bash: the IN box with one line of command (1 + 4 + 4 + 16.575 + 4 + 4 + 1)
        Near(410 - 375, Box(4).Top - Top(4), "Bash box top");
        Near(35, Box(4).Height, "Bash box height");
        Near(50, Box(4).Left, "box left");
        Near(PanelWidth - 20 - 16, Box(4).Right, "box right");

        // Write: "47 lines" and its box, clipped at 60 px of content (1 + 4 + 60 + 4 + 1)
        Assert.Contains("47 lines", UiHelpers.Texts(rows[6]));
        Near(556 - 498, Box(6).Top - Top(6), "Write box top");
        Near(70, Box(6).Height, "Write box height");

        var box = rows[4].GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("ClaudeChatToolBody"));
        Assert.Equal(new CornerRadius(5), box.CornerRadius);
        Assert.Same(Resource(view, "ClaudeChat.InputBorder"), box.BorderBrush);
        Assert.Same(Resource(view, "ClaudeChat.ToolBackground"), box.Background);
        window.Close();
    }

    [AvaloniaFact]
    public void Tool_header_uses_bold_name_link_colored_path_and_plain_details()
    {
        var c = ReferenceScene();
        var (view, window) = OpenScene(c);
        var rows = Rows(view);

        var path = rows[2].GetVisualDescendants().OfType<TextBlock>().First(t => (t.Text ?? "").EndsWith("PermissionPrompt.axaml.cs", StringComparison.Ordinal));
        Assert.Same(Resource(view, "ClaudeChat.Accent"), path.Foreground);
        var range = rows[2].GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "(lines 2-61)");
        Assert.Equal(13, range.FontSize);
        var description = rows[4].GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Final Debug and Release build and three full test runs");
        Assert.Same(Resource(view, "ClaudeChat.ForegroundMuted"), description.Foreground);
        Assert.Equal(13, description.FontSize);
        var label = rows[4].GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "IN");
        Assert.Equal(11.05, label.FontSize);
        Near(60 - 1, BoundsIn(label, view).Left, "IN label");
        window.Close();
    }

    // ---- the diff ----

    [AvaloniaFact]
    public void A_wide_diff_is_side_by_side_with_a_hatched_filler()
    {
        var c = ReferenceScene();
        var (view, window) = OpenScene(c);
        var edit = Rows(view)[1];
        var diff = edit.GetVisualDescendants().OfType<DiffView>().Single();
        Assert.True(diff.IsSideBySide);

        var removed = edit.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatDiffLine") && b.Classes.Contains("removed"));
        var added = edit.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatDiffLine") && b.Classes.Contains("added"));
        Assert.Same(Resource(view, "ClaudeChat.DiffRemoved"), removed.Background);
        Assert.Same(Resource(view, "ClaudeChat.DiffAdded"), added.Background);
        var r = BoundsIn(removed, view);
        var a = BoundsIn(added, view);
        Near(80, r.Left, "removed text column"); // box 50 + border 1 + gutter 29
        Near(19, r.Height, "one line of 19 px");
        Near(38, a.Height, "the added line wraps to two lines");
        Near(r.Top, a.Top, "both sides start together");
        Near(788 + 10, a.Left, "added text column"); // half of the box, then a 10 px gutter

        var hatch = edit.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatDiffHatch") && b.Bounds.Height > 0);
        var h = BoundsIn(hatch, view);
        Near(r.Bottom, h.Top, "the hatch fills under the shorter side");
        Near(19, h.Height, "hatch height");
        Assert.Same(Resource(view, "ClaudeChat.DiffHatch"), hatch.Background);
        Assert.DoesNotContain(UiHelpers.Texts(edit), t => t.StartsWith("- ", StringComparison.Ordinal) || t.StartsWith("+ ", StringComparison.Ordinal));
        window.Close();
    }

    [AvaloniaFact]
    public void A_narrow_diff_is_inline()
    {
        var c = ReferenceScene();
        var (view, window) = OpenScene(c, width: 700);
        var diff = Rows(view)[1].GetVisualDescendants().OfType<DiffView>().Single();
        Assert.False(diff.IsSideBySide); // the extension goes side by side only above 900 px

        var removed = BoundsIn(diff.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatDiffLine") && b.Classes.Contains("removed")), view);
        var added = BoundsIn(diff.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatDiffLine") && b.Classes.Contains("added")), view);
        Near(removed.Bottom, added.Top, "the new line comes under the old one");
        Near(removed.Left, added.Left, "same column");
        window.Close();
    }

    // ---- the reply box ----

    [AvaloniaFact]
    public void Reply_box_is_680_wide_centered_with_the_extension_bar()
    {
        var c = ReferenceScene();
        c.SetPermissionMode("acceptEdits");
        c.Apply(new ClaudeVisualChat.Protocol.StatusChanged(null, "acceptEdits"));
        var (view, window) = OpenScene(c);
        var composer = UiHelpers.Composer(view);
        var box = BoundsIn(composer.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("ClaudeChatComposer")), view);

        Near(680, box.Width, "width");
        Near(441, box.Left, "left");
        Near(window.Height - 16, box.Bottom, "16 px over the bottom");
        Near(1 + 39.5 + 1 + 36 + 1, box.Height, "text, line, bar");
        Assert.Equal(8, composer.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("ClaudeChatComposer")).CornerRadius.TopLeft);

        Assert.True(c.IsBusy);
        Assert.Equal("Queue another message…", UiHelpers.Input(view).Watermark);
        var stop = composer.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("ClaudeChatStop"));
        Assert.True(stop.IsEffectivelyVisible);
        var s = BoundsIn(stop, view);
        Assert.Equal(new Size(26, 26), s.Size);
        Near(box.Right - 1 - 5, s.Right, "stop at the right of the bar");
        Assert.Same(Resource(view, "ClaudeChat.Foreground"), stop.Background); // "Edit automatically": light button

        var mode = composer.GetVisualDescendants().OfType<ComboBox>().Single(b => b.Classes.Contains("ClaudeChatModePicker"));
        var m = BoundsIn(mode, view);
        Near(26, m.Height, "mode button height");
        Near(s.Left - 4, m.Right, "mode button next to stop: 2 px of gap and 2 of margin");
        Assert.Contains("Edit automatically", UiHelpers.Texts(mode));
        window.Close();
    }

    [AvaloniaFact]
    public void Plan_mode_paints_the_send_button_with_the_accent()
    {
        var c = new ChatConversation();
        c.Apply(new ClaudeVisualChat.Protocol.StatusChanged(null, "plan"));
        var (view, window) = OpenScene(c, height: 600);
        var composer = UiHelpers.Composer(view);
        UiHelpers.Input(view).Text = "go";
        UiHelpers.Pump();
        var send = composer.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("ClaudeChatSend"));
        Assert.Same(Resource(view, "ClaudeChat.AccentFill"), send.Background);

        c.Apply(new ClaudeVisualChat.Protocol.StatusChanged(null, "default"));
        UiHelpers.Pump();
        Assert.Same(Resource(view, "ClaudeChat.SendBackground"), send.Background);
        window.Close();
    }
}
