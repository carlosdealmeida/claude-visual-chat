using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using Xunit;
using AssistantBlock = ClaudeVisualChat.Protocol.AssistantBlock;
using PermissionRequested = ClaudeVisualChat.Protocol.PermissionRequested;
using ToolResultBlock = ClaudeVisualChat.Protocol.ToolResultBlock;
using ToolUseBlock = ClaudeVisualChat.Protocol.ToolUseBlock;
using UserBlock = ClaudeVisualChat.Protocol.UserBlock;

namespace ClaudeVisualChat.Tests.Ui;

/// <summary>
/// The Edit and Write rows of 2.1.310 (their line under the name, their box, the 500 px rule) and the diff in the list: 200 px at
/// most with a fade, a layer over it that opens the Diff View, side by side above 900 px and back in one column only at 876 px; and
/// the Diff View over the whole view, which Esc, its × and a click on its backdrop close.
/// </summary>
public class DiffViewTests
{
    /// <summary>A clock where a second goes by between any two readings, so no answer ever comes too soon.</summary>
    private sealed class SlowClock : IPromptClock
    {
        private TimeSpan _now = TimeSpan.FromHours(1);

        public TimeSpan Now => _now += TimeSpan.FromSeconds(1);
    }

    private static JsonElement J(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static string Lines(string prefix, int count) =>
        string.Join("\n", Enumerable.Range(1, count).Select(i => prefix + " " + i));

    /// <summary>A call as the claude makes it, with its result when <paramref name="error"/> is given.</summary>
    private static ToolCallItem Call(ChatConversation c, string id, string tool, object input, bool? error = null)
    {
        c.Apply(new AssistantBlock("m-" + id, null, new ToolUseBlock(id, tool, J(JsonSerializer.Serialize(input)))));
        if (error != null) c.Apply(new UserBlock(null, new ToolResultBlock(id, error.Value ? "boom" : "ok", error.Value), null, false, false));
        return c.Items.OfType<ToolCallItem>().Single(t => t.ToolUseId == id);
    }

    private static (ClaudeChatView View, Window Window) Open(ChatConversation c, double width = UiHelpers.Width, double height = UiHelpers.Height)
    {
        var view = new ClaudeChatView { Conversation = c };
        view.Prompt.Clock = new SlowClock();
        return (view, UiHelpers.Show(view, width, height));
    }

    private static ToolCallView ViewOf(Visual root, ToolCallItem item) =>
        root.GetVisualDescendants().OfType<ToolCallView>().Single(v => ReferenceEquals(v.DataContext, item));

    private static object? Resource(Control view, string key) => view.TryFindResource(key, out var value) ? value : null;

    private static DiffView DiffOf(Visual root, ToolCallItem item) => ViewOf(root, item).GetVisualDescendants().OfType<DiffView>().Single();

    private static Border Overlay(DiffView diff) => diff.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatDiffExpand"));

    private static Border Pill(DiffView diff) => diff.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatDiffExpandPill"));

    private static DiffViewPanel Panel(ClaudeChatView view) => view.GetVisualDescendants().OfType<DiffViewPanel>().Single();

    private static void Near(double expected, double actual, string what, double tolerance = 1) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, what + ": expected " + expected + ", got " + actual);

    private static Rect BoundsIn(Visual visual, Visual relativeTo) =>
        new Rect(visual.TranslatePoint(default, relativeTo)!.Value, visual.Bounds.Size);

    /// <summary>The pointer moved to <paramref name="at"/>, in a frame drawn after what changed before (the window hit-tests the last one).</summary>
    private static void MoveTo(Window window, Point at)
    {
        UiHelpers.DrawFrames();
        window.MouseMove(at, RawInputModifiers.None);
        UiHelpers.Pump();
    }

    /// <summary>A click on <paramref name="control"/>, in a frame drawn after what changed before: a layer that just opened or closed is in it.</summary>
    private static void Tap(Window window, Control control)
    {
        UiHelpers.DrawFrames();
        UiHelpers.Click(window, control);
    }

    // ---- Edit and Write ----

    [AvaloniaFact]
    public void Edit_reads_added_removed_or_modified_by_the_net_line_change()
    {
        var c = new ChatConversation();
        var added = Call(c, "t1", "Edit", new { file_path = "a.cs", old_string = "a", new_string = "a\nb\nc" }, error: false);
        var removed = Call(c, "t2", "Edit", new { file_path = "a.cs", old_string = "a\nb", new_string = "a" }, error: false);
        var modified = Call(c, "t3", "Edit", new { file_path = "a.cs", old_string = "x", new_string = "y" }, error: false);
        var net = Call(c, "t4", "Edit", new { file_path = "a.cs", old_string = "a\nb", new_string = "c\nd\ne" }, error: false);
        var failed = Call(c, "t5", "Edit", new { file_path = "a.cs", old_string = "x", new_string = "y" }, error: true);
        var (view, window) = Open(c, height: 1400); // every row on screen

        Assert.Contains("Added 2 lines", UiHelpers.Texts(ViewOf(view, added)));
        Assert.Contains("Removed 1 line", UiHelpers.Texts(ViewOf(view, removed)));
        Assert.Contains("Modified", UiHelpers.Texts(ViewOf(view, modified)));
        Assert.Contains("Added 1 line", UiHelpers.Texts(ViewOf(view, net)));          // the net change: 3 new lines for 2 old ones
        Assert.DoesNotContain(UiHelpers.Texts(ViewOf(view, net)), t => t.Contains("removed"));
        Assert.Contains("Edit failed", UiHelpers.Texts(ViewOf(view, failed)));
        Assert.DoesNotContain("Modified", UiHelpers.Texts(ViewOf(view, failed)));

        // the line, then the diff under it
        double Y(Visual v) => v.TranslatePoint(default, view)!.Value.Y;
        var line = ViewOf(view, added).GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Added 2 lines");
        Assert.True(Y(line) < Y(DiffOf(view, added)));

        view.Strings = ChatStrings.PortugueseBrazil;
        UiHelpers.Pump();
        Assert.Contains("Adicionou 2 linhas", UiHelpers.Texts(ViewOf(view, added)));
        window.Close();
    }

    [AvaloniaFact]
    public void Write_reads_its_line_count_and_shows_the_content_cut_at_sixty_pixels()
    {
        var c = new ChatConversation();
        var many = Call(c, "t1", "Write", new { file_path = "a.txt", content = Lines("line", 47) }, error: false);
        var one = Call(c, "t2", "Write", new { file_path = "b.txt", content = "hello" }, error: false);
        var notText = Call(c, "t3", "Write", new { file_path = "c.json", content = new { name = "x" } }, error: false);
        var failed = Call(c, "t4", "Write", new { file_path = "d.txt", content = "hello" }, error: true);
        var (view, window) = Open(c, height: 1400); // every row on screen

        Assert.Contains("47 lines", UiHelpers.Texts(ViewOf(view, many)));
        Assert.Contains("1 line", UiHelpers.Texts(ViewOf(view, one)));
        Assert.Contains("Write succeeded", UiHelpers.Texts(ViewOf(view, notText)));
        Assert.Contains(UiHelpers.Texts(ViewOf(view, notText)), t => t.Contains("\"name\": \"x\"")); // the content, as JSON
        Assert.Contains("Write failed", UiHelpers.Texts(ViewOf(view, failed)));
        Assert.NotEqual(ChatStrings.English.WriteSucceeded, ChatStrings.PortugueseBrazil.WriteSucceeded);

        // the content in a box, cut at 60 px like any text of a tool; no diff
        var content = ViewOf(view, many).GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatToolContent"));
        Near(60, content.Bounds.Height - content.Padding.Top - content.Padding.Bottom, "Write content");
        Assert.Empty(ViewOf(view, many).GetVisualDescendants().OfType<DiffView>());
        window.Close();
    }

    // ---- the diff in the list ----

    [AvaloniaFact]
    public void A_tall_diff_is_200_pixels_with_a_fade_and_the_pill_shows_on_hover()
    {
        var c = new ChatConversation();
        var tall = Call(c, "t1", "Edit", new { file_path = "a.cs", old_string = Lines("old", 12), new_string = Lines("new", 12) }, error: false);
        var shortOne = Call(c, "t2", "Edit", new { file_path = "a.cs", old_string = "x", new_string = "y" }, error: false);
        var (view, window) = Open(c, height: 900);
        var lineHeight = Assert.IsType<double>(Resource(view, "ClaudeChat.EditorLineHeight"));

        var diff = DiffOf(view, tall);
        Assert.True(diff.IsCut);
        Near(DiffView.MostHeight, diff.Bounds.Height, "tall diff");
        var fade = diff.GetVisualDescendants().OfType<Rectangle>().Single(r => r.Classes.Contains("ClaudeChatDiffFade"));
        Assert.True(fade.IsEffectivelyVisible);
        Near(30, fade.Bounds.Height, "fade");
        Near(diff.Bounds.Height, fade.Bounds.Bottom, "the fade at the foot");

        var small = DiffOf(view, shortOne);                                    // the content and 20 px, no fade
        Assert.False(small.IsCut);
        Near(lineHeight + DiffView.BottomSpace, small.Bounds.Height, "short diff");
        Assert.False(small.GetVisualDescendants().OfType<Rectangle>().Single(r => r.Classes.Contains("ClaudeChatDiffFade")).IsEffectivelyVisible);

        // the layer covers the whole diff and is named for screen readers
        var overlay = Overlay(diff);
        Assert.Equal(diff.Bounds.Size, overlay.Bounds.Size);
        Assert.Equal("Click to expand", AutomationProperties.GetName(overlay));
        Assert.True(overlay.Focusable);
        var pill = Pill(diff);
        Assert.False(pill.IsEffectivelyVisible);

        // the pointer on it: it darkens and the pill shows, 8 px in from the bottom right
        MoveTo(window, overlay.TranslatePoint(new Point(overlay.Bounds.Width / 2, overlay.Bounds.Height / 2), window)!.Value);
        Assert.True(pill.IsEffectivelyVisible);
        Assert.Same(Resource(view, "ClaudeChat.DiffExpandHover"), overlay.Background);
        Assert.Contains("Click to expand", UiHelpers.Texts(pill));
        var p = BoundsIn(pill, overlay);
        Near(overlay.Bounds.Width - 8, p.Right, "pill right");
        Near(overlay.Bounds.Height - 8, p.Bottom, "pill bottom");
        Assert.Same(Resource(view, "ClaudeChat.MenuBackground"), pill.Background);
        MoveTo(window, new Point(5, 5));                                       // away
        Assert.False(pill.IsEffectivelyVisible);

        // the keyboard's focus shows it too
        overlay.Focus(NavigationMethod.Tab);
        UiHelpers.Pump();
        Assert.True(pill.IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void The_diff_goes_back_inline_only_at_876_pixels()
    {
        var diff = new DiffView(new[] { new DiffLine(DiffKind.Removed, "old"), new DiffLine(DiffKind.Added, "new") }, ChatStrings.English);
        var host = new Border { Width = 901, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, Child = diff };
        var window = UiHelpers.Show(host, 1200, 600);
        bool At(double width)
        {
            host.Width = width;
            UiHelpers.Pump();
            return diff.IsSideBySide;
        }

        Assert.True(At(901));    // side by side above 900
        Assert.True(At(890));    // still side by side under it
        Assert.True(At(877));
        Assert.False(At(876));   // back in one column at 876
        Assert.False(At(890));   // and it stays so until past 900 again
        Assert.False(At(900));
        Assert.True(At(901));
        window.Close();
    }

    [AvaloniaFact]
    public void A_click_opens_the_diff_view_with_the_full_path_and_esc_closes_it()
    {
        const string path = "C:/work/project/src/deep/folder/OrderService.cs";
        var c = new ChatConversation { WorkingDirectory = "C:/work/project" };
        var tool = Call(c, "t1", "Edit", new { file_path = path, old_string = "x", new_string = "y" }, error: false);
        var (view, window) = Open(c, width: 700);
        var panel = Panel(view);
        Assert.False(panel.IsOpen);
        var overlay = Overlay(DiffOf(view, tool));
        Assert.False(DiffOf(view, tool).IsCut);                                // nothing cut, and the click opens it all the same

        Tap(window, overlay);
        Assert.True(panel.IsOpen);
        Assert.Contains(path, UiHelpers.Texts(panel));                         // the whole path, not the list's relative one
        var close = UiHelpers.ButtonWithText(panel, view.Strings.Close);
        Assert.True(close.IsFocused);                                          // the focus goes to the ×
        var inside = panel.GetVisualDescendants().OfType<DiffView>().Single();
        Assert.True(inside.IsSideBySide);                                      // always side by side, at 700 px too
        Assert.DoesNotContain(inside.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("ClaudeChatDiffExpand"));
        Assert.False(view.IsSheetOpen);                                        // no sheet

        // it covers the whole view: opaque, 16 px around, the dialog 40 px less than the view
        Assert.Same(Resource(view, "ClaudeChat.Background"), panel.Background);
        Assert.Equal(new Thickness(16), panel.Padding);
        var dialog = panel.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatDiffViewDialog"));
        Near(view.Bounds.Width - 40, dialog.Bounds.Width, "dialog width");
        Near(view.Bounds.Height - 40, dialog.Bounds.Height, "dialog height");
        Assert.Equal(new CornerRadius(4), dialog.CornerRadius);
        Assert.Equal(new Thickness(1), dialog.BorderThickness);

        UiHelpers.Press(window, Key.Escape);
        Assert.False(panel.IsOpen);
        Assert.True(overlay.IsFocused);                                        // the focus is back where it was

        // the × and a click on the backdrop close it too
        Tap(window, overlay);
        Tap(window, UiHelpers.ButtonWithText(panel, view.Strings.Close));
        Assert.False(panel.IsOpen);
        Tap(window, overlay);
        var corner = panel.TranslatePoint(new Point(4, 4), window)!.Value;
        UiHelpers.DrawFrames();
        window.MouseDown(corner, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(corner, MouseButton.Left, RawInputModifiers.None);
        UiHelpers.Pump();
        Assert.False(panel.IsOpen);

        // Enter on the layer opens it as well
        overlay.Focus(NavigationMethod.Tab);
        UiHelpers.Press(window, Key.Enter);
        Assert.True(panel.IsOpen);
        panel.Hide();
        window.Close();
    }

    [AvaloniaFact]
    public void The_diff_view_shows_the_whole_change_or_diff_view_without_a_path()
    {
        var c = new ChatConversation();
        var tool = Call(c, "t1", "Edit", new { file_path = "a.cs", old_string = Lines("old", 30), new_string = Lines("new", 30) }, error: false);
        var byHand = new ToolCallItem
        {
            Name = "Edit", Summary = "b.cs", Status = ToolStatus.Succeeded,
            Diff = new[] { new DiffLine(DiffKind.Removed, "before"), new DiffLine(DiffKind.Added, "after") },
        };
        c.Items.Add(byHand);
        var (view, window) = Open(c, height: 1000);
        Assert.DoesNotContain("new 30", UiHelpers.Texts(DiffOf(view, tool)));  // the list's diff stops at 20 lines a side

        view.ShowDiff(tool);
        UiHelpers.Pump();
        var panel = Panel(view);
        Assert.Contains("new 30", UiHelpers.Texts(panel));                     // the Diff View has all of it
        Assert.False(panel.GetVisualDescendants().OfType<DiffView>().Single().IsCut);

        view.ShowDiff(byHand);                                                 // no input to read: the item's diff, and no path
        UiHelpers.Pump();
        Assert.Contains("Diff View", UiHelpers.Texts(panel));
        Assert.Contains("after", UiHelpers.Texts(panel));
        Assert.NotEqual(ChatStrings.English.DiffViewTitle, ChatStrings.PortugueseBrazil.DiffViewTitle);
        Assert.NotEqual(ChatStrings.English.ClickToExpand, ChatStrings.PortugueseBrazil.ClickToExpand);
        Assert.NotEqual(ChatStrings.English.TapToExpand, ChatStrings.PortugueseBrazil.TapToExpand);
        window.Close();
    }

    [AvaloniaFact]
    public void Below_500_pixels_edit_and_write_bodies_hide()
    {
        var c = new ChatConversation();
        var edit = Call(c, "t1", "Edit", new { file_path = "a.cs", old_string = "x", new_string = "y" }, error: false);
        var write = Call(c, "t2", "Write", new { file_path = "b.txt", content = "hello" }, error: false);
        var bash = Call(c, "t3", "Bash", new { command = "ls" }, error: false);
        var (view, window) = Open(c, width: 480);

        Assert.Empty(ViewOf(view, edit).GetVisualDescendants().OfType<DiffView>());
        Assert.Contains("Modified", UiHelpers.Texts(ViewOf(view, edit)));     // the line under the name stays
        Assert.DoesNotContain("hello", UiHelpers.Texts(ViewOf(view, write)));
        Assert.Contains("1 line", UiHelpers.Texts(ViewOf(view, write)));
        Assert.Contains("ls", UiHelpers.Texts(ViewOf(view, bash)));           // other tools keep their box

        window.Width = 600;                                                    // wider: back
        UiHelpers.Pump();
        Assert.Single(ViewOf(view, edit).GetVisualDescendants().OfType<DiffView>());
        Assert.Contains("hello", UiHelpers.Texts(ViewOf(view, write)));
        window.Width = 480;
        UiHelpers.Pump();
        Assert.Empty(ViewOf(view, edit).GetVisualDescendants().OfType<DiffView>());

        // "500 px wide or less": 500 hides, 501 shows
        window.Width = 501;
        UiHelpers.Pump();
        Assert.Equal(501, view.Bounds.Width, 1);
        Assert.Single(ViewOf(view, edit).GetVisualDescendants().OfType<DiffView>());
        window.Width = 500;
        UiHelpers.Pump();
        Assert.Equal(500, view.Bounds.Width, 1);
        Assert.Empty(ViewOf(view, edit).GetVisualDescendants().OfType<DiffView>());
        window.Close();

        // the phone is narrower still, and keeps them (R12)
        var touch = UiHelpers.TouchView(c);
        var phone = UiHelpers.ShowPhone(touch);
        Assert.Single(ViewOf(touch, edit).GetVisualDescendants().OfType<DiffView>());
        Assert.Contains("hello", UiHelpers.Texts(ViewOf(touch, write)));
        phone.Close();
    }

    [AvaloniaFact]
    public void In_the_touch_layout_the_pill_always_shows_and_the_diff_is_a_finger_tall()
    {
        var c = new ChatConversation();
        var edit = Call(c, "t1", "Edit", new { file_path = "a.cs", old_string = "x", new_string = "y" }, error: false);
        var view = UiHelpers.TouchView(c);
        var window = UiHelpers.ShowPhone(view);
        var diff = DiffOf(view, edit);
        Assert.True(Pill(diff).IsEffectivelyVisible);                          // no pointer to hover with
        Assert.True(diff.Bounds.Height >= 44, "the diff is " + diff.Bounds.Height);
        Assert.Contains(view.Strings.TapToExpand, UiHelpers.Texts(Pill(diff)));           // "Tap", not "Click"
        Assert.DoesNotContain(view.Strings.ClickToExpand, UiHelpers.Texts(Pill(diff)));
        Assert.Equal(view.Strings.TapToExpand, AutomationProperties.GetName(Overlay(diff)));

        Tap(window, Overlay(diff));                                // a tap opens it
        var panel = Panel(view);
        Assert.True(panel.IsOpen);
        var close = UiHelpers.ButtonWithText(panel, view.Strings.Close);
        Assert.True(close.Bounds.Width >= 44 && close.Bounds.Height >= 44, "the × is " + close.Bounds.Size);
        Assert.True(view.CloseTopLayer());
        Assert.False(panel.IsOpen);
        window.Close();

        // the desktop: only with the pointer
        var desktop = new ClaudeChatView { Conversation = c };
        var big = UiHelpers.Show(desktop);
        Assert.False(Pill(DiffOf(desktop, edit)).IsEffectivelyVisible);
        big.Close();
    }

    // ---- the keyboard ----

    [AvaloniaFact]
    public void A_request_that_arrives_with_the_diff_view_open_does_not_take_the_keyboard()
    {
        var c = new ChatConversation();
        var tool = Call(c, "t1", "Edit", new { file_path = "a.cs", old_string = "x", new_string = "y" }, error: false);
        var (view, window) = Open(c);
        var answers = new List<string>();
        c.PermissionAnswered += (_, e) => answers.Add(e.Request.RequestId);
        var panel = Panel(view);
        Tap(window, Overlay(DiffOf(view, tool)));
        var close = UiHelpers.ButtonWithText(panel, view.Strings.Close);
        Assert.True(close.IsFocused);

        c.Apply(new PermissionRequested("r1", "Bash", "Bash", null, "t2", J("{\"command\":\"ls\"}"), null));
        UiHelpers.Pump();
        var prompt = UiHelpers.Prompt(view);
        Assert.NotNull(prompt.Request);
        Assert.True(close.IsFocused);                                          // the request waits for the keyboard
        Assert.False(prompt.IsKeyboardFocusWithin);

        UiHelpers.Press(window, Key.D1);                                       // a number in the Diff View answers nothing
        Assert.Empty(answers);
        UiHelpers.Press(window, Key.Escape);                                   // the first Esc closes the Diff View, only it
        Assert.False(panel.IsOpen);
        Assert.Empty(answers);
        Assert.NotNull(prompt.Request);
        Assert.True(prompt.IsKeyboardFocusWithin);                             // and the request gets the keyboard then, as ever
        window.Close();
    }

    [AvaloniaFact]
    public void Closing_the_preview_over_the_diff_view_does_not_give_the_keyboard_to_the_card_under_it()
    {
        var c = new ChatConversation();
        var tool = Call(c, "t1", "Edit", new { file_path = "a.cs", old_string = "x", new_string = "y" }, error: false);
        var (view, window) = Open(c);
        var panel = Panel(view);
        view.ShowDiff(tool);
        UiHelpers.Pump();
        c.Apply(new PermissionRequested("r1", "Bash", "Bash", null, "t2", J("{\"command\":\"ls\"}"), null));
        UiHelpers.Pump();
        var prompt = UiHelpers.Prompt(view);
        Assert.NotNull(prompt.Request);

        view.ShowPreview(ChatImage.FromBytes(UiHelpers.Png(30, 20), "image/png"), "image.png");
        UiHelpers.Pump();
        Assert.True(view.CloseTopLayer());                                     // the preview, over the Diff View
        UiHelpers.Pump();
        Assert.True(panel.IsOpen);
        Assert.False(prompt.IsKeyboardFocusWithin);                            // the card is hidden under the Diff View: it waits

        Assert.True(view.CloseTopLayer());                                     // the Diff View closes, and the request gets the keyboard then
        Assert.True(prompt.IsKeyboardFocusWithin);
        window.Close();
    }

    [AvaloniaFact]
    public void Esc_closes_the_diff_view_over_the_plan_document_first()
    {
        var c = new ChatConversation();
        var tool = Call(c, "t1", "Edit", new { file_path = "a.cs", old_string = "x", new_string = "y" }, error: false);
        c.Apply(new PermissionRequested("r1", "ExitPlanMode", "ExitPlanMode", null, "t9", J("{\"plan\":\"# Rewrite the parser\"}"), null));
        var (view, window) = Open(c);
        var document = view.GetVisualDescendants().OfType<PlanDocumentPanel>().Single();
        Assert.True(document.IsOpen);
        var panel = Panel(view);
        view.ShowDiff(tool);
        UiHelpers.Pump();

        UiHelpers.Press(window, Key.Escape);
        Assert.False(panel.IsOpen);
        Assert.True(document.IsOpen);                                          // the document under it stays
        window.Close();
    }

    [AvaloniaFact]
    public void A_read_only_view_still_opens_the_diff_view()
    {
        var c = new ChatConversation();
        var tool = Call(c, "t1", "Edit", new { file_path = "a.cs", old_string = "x", new_string = "y" }, error: false);
        var (view, window) = Open(c);
        view.IsReadOnly = true;
        UiHelpers.Pump();
        Tap(window, Overlay(DiffOf(view, tool)));
        Assert.True(Panel(view).IsOpen);
        UiHelpers.Press(window, Key.Escape);
        Assert.False(Panel(view).IsOpen);
        window.Close();
    }

    [AvaloniaFact]
    public void Another_conversation_closes_the_diff_view()
    {
        var c = new ChatConversation();
        var tool = Call(c, "t1", "Edit", new { file_path = "a.cs", old_string = "x", new_string = "y" }, error: false);
        var (view, window) = Open(c);
        view.ShowDiff(tool);
        UiHelpers.Pump();
        Assert.True(Panel(view).IsOpen);
        view.Conversation = new ChatConversation();
        UiHelpers.Pump();
        Assert.False(Panel(view).IsOpen);
        window.Close();
    }

    // ---- the permission card keeps the whole diff ----

    [AvaloniaFact]
    public void The_permission_card_still_shows_the_whole_diff_without_the_layer()
    {
        var c = new ChatConversation();
        c.Apply(new PermissionRequested("r1", "Edit", "Edit", null, "t1",
            J(JsonSerializer.Serialize(new { file_path = "a.cs", old_string = Lines("old", 30), new_string = Lines("new", 30) })), null));
        var (view, window) = Open(c);
        var diff = UiHelpers.Prompt(view).GetVisualDescendants().OfType<DiffView>().Single();
        Assert.False(diff.IsCut);
        Assert.True(diff.Bounds.Height > DiffView.MostHeight);                 // not cut at 200 px: the card's own area scrolls
        Assert.DoesNotContain(diff.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("ClaudeChatDiffExpand"));
        window.Close();
    }
}
