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
using ClaudeVisualChat;
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
        ("ClaudeChat.DisabledForeground", "#555555", "#80CCCCCC"), // disabledForeground: the date line
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
        ("ClaudeChat.AttachmentBackground", "#2A2B2C", "#414141"),     // input.background 85 % + descriptionForeground
        ("ClaudeChat.AttachmentHoverBorder", "#666667", "#85CCCCCC"),  // the input border 60 % + foreground
        ("ClaudeChat.ThumbnailHoverBorder", "#B38C8C8C", "#B39D9D9D"), // descriptionForeground at 70 %
        ("ClaudeChat.ContextRingTrack", "#26BFBFBF", "#26CCCCCC"),     // the context ring's track: foreground at 15 %
        ("ClaudeChat.MenuBackground", "#202122", "#1F1F1F"),           // menu.background
        ("ClaudeChat.MenuForeground", "#BFBFBF", "#CCCCCC"),           // menu.foreground
        ("ClaudeChat.ListHoverBackground", "#14FFFFFF", "#2A2D2E"),    // list.hoverBackground
        // the banners: BannerTint or errorForeground at 50 % over foreground, at 25 % over editor.background
        ("ClaudeChat.BannerForeground", "#8591B7", "#8B98BE"),
        ("ClaudeChat.BannerBackground", "#20273B", "#2A3043"),
        ("ClaudeChat.BannerErrorForeground", "#DAA398", "#E28F8B"),
        ("ClaudeChat.BannerErrorBackground", "#4B302B", "#552C2A"),
        // the warning banner (the invisible characters): #E5A54B at 50 % over foreground, at 25 % over editor.background
        ("ClaudeChat.BannerWarningForeground", "#D2B285", "#D9B98C"),
        ("ClaudeChat.BannerWarningBackground", "#473822", "#51412A"),
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
        ("ClaudeChat.PreviewOverlay", "#D9000000"), // the image preview's backdrop
        ("ClaudeChat.BannerTint", "#4A63AF"),
        ("ClaudeChat.RemoteControlForeground", "#FAF9F5"),
        ("ClaudeChat.RemoteControlHoverBackground", "#516DC1"), // the connected chip's brightness(1.1)
        ("ClaudeChat.RemoteControlHoverForeground", "#FFFFFF"),
        ("ClaudeChat.ModalOverlay", "#BF000000"),   // the Agent map's backdrop (--app-modal-background)
        ("ClaudeChat.AgentRunning", "#89D185"),     // an agent at work: the status-busy green
        ("ClaudeChat.ContextRingFill", "#C6613F"),  // the context ring: the send button's clay
        ("ClaudeChat.DiffExpandHover", "#0D000000"),           // the layer over a diff in the list, under the pointer
        ("ClaudeChat.DiffExpandPillHoverBorder", "#4DFFFFFF"), // "Click to expand" under the pointer
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
        Assert.Equal(BoxShadows.Parse("0 4 12 0 #0D000000"), Resource(view, "ClaudeChat.DiffViewShadow")); // the Diff View's dialog
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
        Assert.Equal(BoxShadows.Parse("inset 0 0 0 1 #0078D4"), Resource(view, "ClaudeChat.ToolContentFocusShadow")); // the focus border of this theme
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

    // ---- the date line and the fade over the reply box ----

    [AvaloniaTheory]
    [InlineData(ChatLayout.Desktop)]
    [InlineData(ChatLayout.Touch)]
    public void The_date_line_has_the_extension_measures(ChatLayout layout)
    {
        var c = ReferenceScene(); // its prompt is the first message with a time: the line is over it
        var view = new ClaudeChatView { Conversation = c, Layout = layout };
        var window = layout == ChatLayout.Touch ? UiHelpers.ShowPhone(view) : UiHelpers.Show(view, PanelWidth, 940);
        UiHelpers.ItemList(view).ScrollIntoView(c.Items.OfType<UserMessageItem>().Last());
        UiHelpers.Pump();
        var line = view.GetVisualDescendants().OfType<DateSeparator>().Single(d => d.IsEffectivelyVisible);
        Assert.Equal("Mon, Oct 19", line.Date);

        // the label: centered, .9em, in disabledForeground, cut with "…"
        var label = line.Label;
        Assert.Equal(Resource(view, "ClaudeChat.CodeFontSize"), label.FontSize);
        Assert.Same(Resource(view, "ClaudeChat.DisabledForeground"), line.Foreground);
        Assert.Same(Resource(view, "ClaudeChat.DisabledForeground"), label.Foreground);
        Assert.Equal(TextTrimming.CharacterEllipsis, label.TextTrimming);
        var box = BoundsIn(line, view);
        var text = BoundsIn(label, view);
        Near(box.Center.X, text.Center.X, "label centered");
        // the rules: 7 px tall, on both sides, 10 px from the label, as far as the line's ends; centered on the label
        Near(0, line.LeftRule.Left, "left rule from the start");
        Near(line.Bounds.Width, line.RightRule.Right, "right rule to the end");
        Near(10, label.Bounds.Left - line.LeftRule.Right, "gap before the label");
        Near(10, line.RightRule.Left - label.Bounds.Right, "gap after the label");
        Assert.Equal(7, line.LeftRule.Height);
        Assert.Equal(7, line.RightRule.Height);
        Assert.True(line.LeftRule.Width >= 32);
        Near(label.Bounds.Center.Y, line.LeftRule.Center.Y, "rule centered on the label");
        // 8 px over and under
        Near(8, label.Bounds.Top, "8 px over");
        Near(8, line.Bounds.Height - label.Bounds.Bottom, "8 px under");
        // over the user's turn, which keeps its own spacing under it
        var turn = view.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Classes.Contains("ClaudeChatUserTurn") && ReferenceEquals(s.DataContext, c.Items.OfType<UserMessageItem>().Last()));
        Near(box.Bottom + turn.Margin.Top, BoundsIn(turn, view).Top, "the turn under the line");
        window.Close();

        // narrow, the label is cut and the rules keep their 32 px
        var scene = ReferenceScene();
        var narrow = new ClaudeChatView { Conversation = scene, Layout = layout };
        window = UiHelpers.Show(narrow, 140, 600);
        UiHelpers.ItemList(narrow).ScrollIntoView(scene.Items.OfType<UserMessageItem>().Last());
        UiHelpers.Pump();
        line = narrow.GetVisualDescendants().OfType<DateSeparator>().Single(d => d.IsEffectivelyVisible);
        Assert.True(line.LeftRule.Width >= 32 - Tolerance, $"left rule {line.LeftRule.Width}");
        Assert.True(line.RightRule.Width >= 32 - Tolerance, $"right rule {line.RightRule.Width}");
        Assert.True(line.Label.Bounds.Width < line.Label.DesiredSize.Width + 0.5 && line.Label.Bounds.Width <= line.Bounds.Width - 2 * (32 + 10) + 0.5);
        window.Close();
    }

    [AvaloniaFact]
    public void A_fade_of_150_px_dims_the_list_over_the_reply_box()
    {
        var c = ReferenceScene();
        var (view, window) = OpenScene(c);
        var fade = view.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatListFade"));
        Assert.True(fade.IsEffectivelyVisible);
        Assert.False(fade.IsHitTestVisible); // the list under it takes the pointer
        var bounds = BoundsIn(fade, view);
        Near(150, bounds.Height, "fade height");
        Near(view.Bounds.Height, bounds.Bottom, "fade to the bottom of the view");
        Near(0, bounds.Left, "fade from the left");
        Near(view.Bounds.Width, bounds.Right, "fade to the right");
        // from nothing at its top to the background at its bottom
        Assert.Same(Resource(view, "ClaudeChat.Background"), fade.Background);
        var mask = Assert.IsAssignableFrom<ILinearGradientBrush>(fade.OpacityMask);
        Assert.Equal(0, mask.GradientStops.First().Color.A);
        Assert.Equal(255, mask.GradientStops.Last().Color.A);
        Assert.True(mask.StartPoint.Point.Y < mask.EndPoint.Point.Y);
        // over the list, under the reply box: drawn after the one, before the other
        var grid = Assert.IsAssignableFrom<Grid>(fade.Parent);
        var composer = view.GetVisualDescendants().OfType<ChatComposer>().Single();
        var bottom = composer.GetVisualAncestors().OfType<StackPanel>().First(s => ReferenceEquals(s.Parent, grid));
        Assert.True(grid.Children.IndexOf(UiHelpers.Scroller(view)) < grid.Children.IndexOf(fade));
        Assert.True(grid.Children.IndexOf(fade) < grid.Children.IndexOf(bottom));
        // the top of the reply box is inside the fade: the fade shows over the list just above it
        Assert.True(BoundsIn(composer, view).Top > bounds.Top);

        view.IsReadOnly = true; // no reply box to fade into
        UiHelpers.Pump();
        Assert.False(fade.IsEffectivelyVisible);
        view.IsReadOnly = false;
        UiHelpers.Pump();
        Assert.True(fade.IsEffectivelyVisible);
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
    public void Tool_contents_are_cut_at_60_px_and_open_to_the_height_of_their_lines()
    {
        var c = ReferenceScene();
        var lines = string.Join("\n", Enumerable.Range(1, 20).Select(i => "line " + i));
        c.Apply(new AssistantBlock("m9", null, new ToolUseBlock("t9", "Bash", J("{\"command\":\"seq 20\"}"))));
        c.Apply(new UserBlock(null, new ToolResultBlock("t9", lines, false), null, false, false));
        var (view, window) = OpenScene(c);
        var rows = Rows(view);
        List<Border> Contents(int row) => rows[row].GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("ClaudeChatToolContent")).ToList();
        double Inner(Border b) => b.Bounds.Height - b.Padding.Top - b.Padding.Bottom;

        // Write's file (47 lines) and the output (20) are cut alike: 60 px of text under 4 px of padding
        var write = Contents(6).Single();
        var output = Contents(rows.Count - 1)[1];
        Near(60, Inner(write), "Write content");
        Near(60, Inner(output), "OUT content");
        Near(write.Bounds.Height, output.Bounds.Height, "both cut alike");

        // a click opens it to the height of its lines (the line height is the theme's, whatever the font)
        var tool = c.Items.OfType<ToolCallItem>().Last();
        var lineHeight = Assert.IsType<double>(Resource(view, "ClaudeChat.SmallLineHeight"));
        var cutTop = BoundsIn(output, rows[rows.Count - 1]).Top;
        tool.IsOutputExpanded = true;
        UiHelpers.Pump();
        var open = Contents(rows.Count - 1)[1];
        Near(20 * lineHeight, Inner(open), "20 lines open");
        Near(cutTop, BoundsIn(open, rows[rows.Count - 1]).Top, "it grows downward");

        // the label column is shared: OUT starts where IN does
        var labels = rows[rows.Count - 1].GetVisualDescendants().OfType<TextBlock>().Where(t => t.Text == "IN" || t.Text == "OUT").ToList();
        Assert.Equal(2, labels.Count);
        Near(BoundsIn(labels[0], view).Left, BoundsIn(labels[1], view).Left, "OUT under IN");
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
    // ---- the new bar (2.1.306) ----
    // Already pinned elsewhere, not repeated here: the 26 px size and 2 px gap of + and / (ComposerTests.The_bar_has_the_extension_order),
    // the clock's 26 px and the Remote Control chip's 18 px / radius 9 / padding 8 (StatusTests.Clock_and_chip_sit_in_the_bar_with_the_extension_measures),
    // the banner's tuck, radius, padding and text (StatusTests.Banner_is_tucked_under_the_reply_box), the model chip's 18 px / radius 9 /
    // padding 8 and the menu's rows and slider (ModelMenuTests), the pill's 24 px, 4 px radius and strip padding (AttachmentTests),
    // the palette's frame, rows and filter (CommandPaletteTests), the panel's frame (UsagePanelTests).

    /// <summary>The scene of the extension's print: every part of the bar on, and the browser's banner over the box.</summary>
    private static (ClaudeChatView View, Window Window, ChatComposer Composer) OpenBar()
    {
        var now = new DateTimeOffset(2026, 10, 22, 10, 0, 0, TimeSpan.Zero);
        var c = new ChatConversation
        {
            Clock = () => now,
            AvailableModels = new[] { new ClaudeVisualChat.ChatModel("opus", "claude-opus-5-5", "Opus 5.5", "", new[] { "low", "high" }) },
        };
        c.Apply(new ClaudeVisualChat.Protocol.SessionStarted("s1", "opus", null, "default", "2.1.306"));
        c.Apply(new ClaudeVisualChat.Protocol.SettingsApplied(null, "high", true));
        c.Apply(new ClaudeVisualChat.Protocol.PromptCacheUsed(TimeSpan.FromMinutes(60), null));
        c.Apply(new ClaudeVisualChat.Protocol.ContextUsageReported(new ContextUsage(700000, 1000000, 70, System.Array.Empty<ContextCategory>())));
        // an agent, so the agents chip shows (the extension's shows only once the map has one)
        c.Apply(new ClaudeVisualChat.Protocol.AgentTaskStarted("t1", "toolu_1", "Look around", "general-purpose", true, 1, "local_agent"));
        c.RemoteControlStatus = ChatRemoteControlStatus.Connected;
        c.RemoteControlUrl = "https://claude.ai/code/session_1";
        c.BrowserStatus = ChatBrowserStatus.Connected;
        var view = new ClaudeChatView { Conversation = c };
        var window = UiHelpers.Show(view, 1000, 400);
        return (view, window, UiHelpers.Composer(view));
    }

    [AvaloniaFact]
    public void The_bar_parts_sit_on_one_row_with_the_extension_gaps()
    {
        var (view, window, composer) = OpenBar();
        var box = BoundsIn(composer.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("ClaudeChatComposer")), view);
        Rect Of(Control c) => BoundsIn(c, view);
        var add = Of(UiHelpers.ButtonWithText(composer, "Add"));
        var slash = Of(UiHelpers.ButtonWithText(composer, "Show command menu (/)"));
        var clock = Of(composer.GetVisualDescendants().OfType<PromptCacheClock>().Single());
        var remote = Of(composer.GetVisualDescendants().OfType<RemoteControlChip>().Single());
        var agents = Of(composer.GetVisualDescendants().OfType<AgentsChip>().Single());
        var ring = Of(composer.GetVisualDescendants().OfType<ContextRing>().Single());
        var model = Of(composer.GetVisualDescendants().OfType<ModelChip>().Single());
        var mode = Of(composer.GetVisualDescendants().OfType<ComboBox>().Single());
        var send = Of(composer.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("ClaudeChatSend")));

        // .inputFooterV2_gGYT1w: footer padding 5, flex gap 2, align-items center; its buttons 26 px high
        // (the footer's .5 px top border is drawn as the 1 px line, so the bar is 36 and not 36.5)
        Near(box.Bottom - 1 - 5, add.Bottom, "the bar's buttons end 5 px over the box's bottom edge");
        Near(26, add.Height, "buttons are 26 high");
        foreach (var (name, part) in new[] { ("slash", slash), ("clock", clock), ("mode", mode), ("send", send) })
            Near(add.Top, part.Top, name + " is on the row of +");
        // the chips (min-height 18, .modelPill_gGYT1w) are centered on the 26 px row
        Near(add.Top + 4, remote.Top, "Remote Control chip centered");
        Near(add.Top + 4, agents.Top, "agents chip centered");
        Near(add.Top + 4, model.Top, "model chip centered");

        // gap 2 between neighbors; the model chip adds margin-left 6 (.modelPill_gGYT1w) to the gap. The order of the
        // extension's 2.1.309 bar: +, /, the context ring (iv0), the clock (kv0), Remote Control (av0), the agents (Iv0),
        // the model (vb1)
        // the context ring (the extension's 20 px pie, shown over 50 % used; 28 x 24) right after /
        Near(slash.Right + 2, ring.Left, "ring after /: gap 2");
        Near(ring.Right + 2, clock.Left, "clock after the ring: gap 2");
        Near(clock.Right + 2, remote.Left, "chip after clock");
        // the "N agents" chip, a .modelPill_gGYT1w too, between the Remote Control chip and the model chip (2.1.309)
        Near(remote.Right + 2 + 6, agents.Left, "agents chip: gap 2 + margin 6");
        Near(agents.Right + 2 + 6, model.Left, "model chip: gap 2 + margin 6");
        Near(add.Top + 1, ring.Top, "ring is centered on the 26 px row");
        Near(28, ring.Width, "ring button is 28 wide (.usage: padding 2px 4px around the 20 px svg)");
        Near(24, ring.Height, "ring button is 24 high");
        var arc = Of(composer.GetVisualDescendants().OfType<ContextRingArc>().Single());
        Near(20, arc.Width, "the pie is 20 wide");
        Near(20, arc.Height, "the pie is 20 high");
        Near(ring.Left + 4, arc.Left, "the pie sits 4 px in from the button's edge");
        Near(ring.Top + 2, arc.Top, "and 2 px down");

        // under 50 % used the ring is hidden and takes no room, like the other chips
        UiHelpers.Pump();
        view.Conversation!.Apply(new ClaudeVisualChat.Protocol.ContextUsageReported(new ContextUsage(100, 1000, 10, System.Array.Empty<ContextCategory>())));
        UiHelpers.Pump();
        Near(slash.Right + 2, Of(composer.GetVisualDescendants().OfType<PromptCacheClock>().Single()).Left, "hidden ring: the clock follows /");
        Near(Of(composer.GetVisualDescendants().OfType<AgentsChip>().Single()).Right + 2 + 6, Of(composer.GetVisualDescendants().OfType<ModelChip>().Single()).Left, "the model chip still follows the agents chip");
        Near(18, agents.Height, "agents chip is 18 high");
        Near(mode.Right + 4, send.Left, "send after mode: gap 2 and the mode button's 2 px margin, as FidelityTests.Reply_box_is_680_wide...");
        Near(box.Right - 1 - 5, send.Right, "send at the right of the bar");
        Assert.Equal(new Size(26, 26), send.Size);

        // the model chip's text: .85em, the effort at margin-left 4 in the secondary color
        var chip = composer.GetVisualDescendants().OfType<ModelChip>().Single();
        Assert.All(chip.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible), t => Assert.Equal(11.05, t.FontSize, 2));
        window.Close();
    }

    [AvaloniaFact]
    public void The_browser_banner_is_as_wide_as_the_box_and_20_px_of_content_high()
    {
        var (view, window, composer) = OpenBar();
        var box = BoundsIn(composer.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("ClaudeChatComposer")), view);
        var banner = view.GetVisualDescendants().OfType<BrowserBanner>().Single();
        var b = BoundsIn(banner, view);

        // .banner_88YE4g: padding 6 6 12 16, border .5, the × (.closeButton_88YE4g: 4 + 12 + 4 = 20) is the tallest part
        // (the text is 11.05 * 1.4 = 15.5); .5 px borders are drawn as 1 px, so 6 + 20 + 12 + 2 x .5 = 39 is 40 here
        Near(40, b.Height, "banner height");
        Near(box.Left, b.Left, "banner left");
        Near(box.Right, b.Right, "banner right");
        Near(box.Top + 8, b.Bottom, "margin-bottom -8: the banner's last 8 px are under the box");
        var close = banner.GetVisualDescendants().OfType<Button>().Single(x => x.IsEffectivelyVisible);
        Assert.Equal(new Size(20, 20), close.Bounds.Size);
        Near(b.Right - 1 - 6, BoundsIn(close, view).Right, "the × sits 6 px from the right");
        window.Close();
    }
}
