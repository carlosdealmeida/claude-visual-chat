using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using ClaudeVisualChat.Protocol;
using ColorTextBlock.Avalonia;
using Xunit;

namespace ClaudeVisualChat.Tests.Ui;

/// <summary>
/// The plan of an <c>ExitPlanMode</c> request as a document over the list (<see cref="PlanDocumentPanel"/>), as the extension opens it in
/// a tab beside the chat: it comes by itself with the request, leaves the card under it to be answered, closes when the plan is approved
/// and stays after a refusal; Esc, its × and the host's back button close it, one layer at a time. And the plan's row in the timeline:
/// "Claude’s Plan" and its file, then whether the user approved it, and a click on it that opens the plan again.
/// </summary>
public class PlanDocumentTests
{
    /// <summary>A clock where a second goes by between any two readings, so no answer ever comes too soon: these tests are not about the pause.</summary>
    private sealed class SlowClock : IPromptClock
    {
        private TimeSpan _now = TimeSpan.FromHours(1);

        public TimeSpan Now => _now += TimeSpan.FromSeconds(1);
    }

    private const string RewriteTitle = "Rewrite the parser";
    private const string RewritePlan = "# Rewrite the parser ##\n\nIntro line.\n\n## Steps\n\n1. Read `parser.cs`\n2. Write the new one";
    private const string RecordedTitle = "Plan: add \"Fourth point\" to notes.md";
    private const string RecordedFile = "plan-a-small-change-delegated-dove.md";

    private sealed class Opened
    {
        public Opened(ChatConversation conversation, ChatLayout layout = ChatLayout.Desktop)
        {
            Conversation = conversation;
            View = new ClaudeChatView { Conversation = conversation, Layout = layout };
            if (layout == ChatLayout.Touch) View.Strings = ChatStrings.PortugueseBrazil;
            View.Prompt.Clock = new SlowClock();
            Window = layout == ChatLayout.Touch ? UiHelpers.ShowPhone(View) : UiHelpers.Show(View);
            Sent = new List<string>();
            conversation.PermissionModeRequested += (_, e) => Sent.Add("mode:" + e.Mode);
            conversation.PermissionAnswered += (_, e) => Sent.Add(e.Decision.ToString().ToLowerInvariant() + ":" + e.Request.RequestId
                                                                 + (e.Message == null ? "" : ":" + e.Message));
        }

        public ChatConversation Conversation { get; }
        public ClaudeChatView View { get; }
        public Window Window { get; }
        public List<string> Sent { get; }
        public ChatStrings Strings => View.Strings;

        public PlanDocumentPanel Document => View.GetVisualDescendants().OfType<PlanDocumentPanel>().Single();
        public PermissionPrompt Prompt => UiHelpers.Prompt(View);
        public TextBox Input => UiHelpers.Input(View);
        public Button Option(string text) => UiHelpers.ButtonWithText(Prompt, text);
        public Button Close => UiHelpers.ButtonWithText(Document, Strings.Close);

        /// <summary>The words of the document: its header and banner, and the Markdown's blocks.</summary>
        public List<string> DocumentTexts =>
            UiHelpers.Texts(Document).Concat(Document.GetVisualDescendants().OfType<CTextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "")).ToList();

        /// <summary>The row of the plan in the timeline, brought into sight.</summary>
        public ToolCallView PlanRow(int index = 0)
        {
            var call = Calls()[index];
            UiHelpers.ItemList(View).ScrollIntoView(call);
            UiHelpers.Pump();
            return View.GetVisualDescendants().OfType<ToolCallView>().First(v => ReferenceEquals(v.DataContext, call) && v.IsEffectivelyVisible);
        }

        public List<ToolCallItem> Calls() => Conversation.Items.OfType<ToolCallItem>().Where(t => t.Name == "ExitPlanMode").ToList();
    }

    private static JsonElement J(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static PermissionRequested ExitPlanMode(string id, string plan = RewritePlan, string? path = "C:\\Users\\demo\\.claude\\plans\\rewrite.md") =>
        new PermissionRequested(id, "ExitPlanMode", "ExitPlanMode", null, "t-" + id,
            J("{\"plan\":" + JsonSerializer.Serialize(plan) + (path == null ? "" : ",\"planFilePath\":" + JsonSerializer.Serialize(path)) + "}"), null);

    private static PermissionRequested Bash(string id) =>
        new PermissionRequested(id, "Bash", "Bash", null, "t-" + id, J("{\"command\":\"ls\"}"), null);

    /// <summary>A conversation in plan mode whose claude asks to leave it: the call in the timeline, then its request.</summary>
    private static ChatConversation Planning(string id = "r1", string plan = RewritePlan)
    {
        var c = new ChatConversation();
        c.Apply(new StatusChanged(null, "plan"));
        AskPlan(c, id, plan);
        return c;
    }

    private static void AskPlan(ChatConversation c, string id, string plan = RewritePlan)
    {
        c.Apply(new AssistantBlock("m-" + id, null, new ToolUseBlock("t-" + id, "ExitPlanMode", J("{}"))));
        c.Apply(ExitPlanMode(id, plan));
    }

    private static Rect BoundsIn(Control control, Visual root) =>
        new Rect(control.TranslatePoint(default, root)!.Value, control.Bounds.Size);

    /// <summary>The middle of the control and of each edge, a little inside, belong to it: nothing is drawn over it.</summary>
    private static void AssertReachable(Window window, Control control)
    {
        UiHelpers.DrawFrames();
        var width = control.Bounds.Width;
        var height = control.Bounds.Height;
        foreach (var (x, y) in new[] { (width / 2, 2.0), (width / 2, height - 2), (2.0, height / 2), (width - 2, height / 2), (width / 2, height / 2) })
        {
            var at = control.TranslatePoint(new Point(x, y), window)!.Value;
            var hit = window.InputHitTest(at) as Visual;
            Assert.True(hit != null && (ReferenceEquals(hit, control) || control.IsVisualAncestorOf(hit)),
                "the point (" + x + ", " + y + ") of the control is " + (hit?.GetType().Name ?? "nothing"));
        }
    }

    // ---- it opens by itself, over the list, and the card stays usable ----

    [AvaloniaTheory]
    [InlineData(ChatLayout.Desktop)]
    [InlineData(ChatLayout.Touch)]
    public void The_plan_opens_by_itself_above_the_list_and_leaves_the_card_usable(ChatLayout layout)
    {
        var ui = new Opened(Planning(), layout);
        var strings = ui.Strings;

        Assert.True(ui.Document.IsOpen);
        Assert.Contains(RewriteTitle, UiHelpers.Texts(ui.Document));                       // the header: the plan's title
        Assert.Contains("Intro line.", ui.DocumentTexts);                                  // the Markdown
        Assert.Contains(strings.PlanReadyForReview, ui.DocumentTexts);                     // the banner: comments are on
        Assert.Contains(strings.PlanSelectToComment, ui.DocumentTexts);
        Assert.False(ui.View.IsSheetOpen);                                                 // it is no sheet

        // it takes the list's row, and only it: the card is under it, whole, on the screen and answerable
        var list = BoundsIn(UiHelpers.Scroller(ui.View), ui.View);
        var document = BoundsIn(ui.Document, ui.View);
        Assert.Equal(list, document);
        Assert.True(ui.Prompt.IsEffectivelyVisible);
        Assert.True(ui.Prompt.IsEnabled);
        Assert.True(BoundsIn(ui.Prompt, ui.View).Top >= document.Bottom - 0.5);
        var no = ui.Option(strings.PlanKeepPlanning);
        AssertReachable(ui.Window, no);
        AssertReachable(ui.Window, ui.Close);
        if (layout == ChatLayout.Touch)
            Assert.True(ui.Close.Bounds.Width >= 44 && ui.Close.Bounds.Height >= 44, "the × is " + ui.Close.Bounds.Size);
        else
            Assert.Equal(new Size(28, 28), ui.Close.Bounds.Size);

        UiHelpers.Click(ui.Window, no);                                                    // the card answers under the document
        if (layout == ChatLayout.Touch) UiHelpers.Click(ui.Window, UiHelpers.ButtonWithText(ui.Prompt, strings.Send)); // the touch field's "Send"
        Assert.Equal(new[] { "deny:r1:User chose to stay in plan mode and continue planning" }, ui.Sent);
        Assert.True(ui.Document.IsOpen);                                                   // a refusal leaves it open
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void The_document_takes_the_editor_measures()
    {
        var ui = new Opened(Planning());
        var markdown = ui.Document.GetVisualDescendants().OfType<MarkdownBlock>().Single();
        Assert.Contains("ClaudeChatPlanDocument", markdown.Classes);
        Assert.Equal(14, markdown.FontSize);

        // the blocks the user can select (0.6.0, Task 8): each of the Markdown's text blocks, but the list markers
        var blocks = markdown.GetVisualDescendants().OfType<PlanTextBlock>().ToList();
        Assert.DoesNotContain(markdown.GetVisualDescendants().OfType<CTextBlock>(), t => !t.Classes.Contains("ListMarker"));
        var h1 = blocks.Single(b => b.Classes.Contains("Heading1"));
        var h2 = blocks.Single(b => b.Classes.Contains("Heading2"));
        var paragraph = blocks.First(b => b.Words == "Intro line.");
        Assert.Equal(14 * 1.6, h1.FontSize, 3);
        Assert.Equal(14 * 1.3, h2.FontSize, 3);
        Assert.Equal(Avalonia.Media.FontWeight.SemiBold, h1.FontWeight);
        Assert.Equal(14, paragraph.FontSize);
        Assert.Equal(14 * 1.6, paragraph.LineHeight, 3);

        // H1 and H2 are underlined with a 1 px line, 8 and 6 px under the text
        var rule1 = Assert.IsAssignableFrom<Border>(h1.GetVisualParent());
        var rule2 = Assert.IsAssignableFrom<Border>(h2.GetVisualParent());
        Assert.Equal(new Thickness(0, 0, 0, 1), rule1.BorderThickness);
        Assert.Equal(8, rule1.Padding.Bottom);
        Assert.Equal(6, rule2.Padding.Bottom);
        Assert.Equal(24, rule2.Margin.Top);                                                // headings: 24 px above, 8 below
        Assert.Equal(8, rule2.Margin.Bottom);

        // the page: 16 px over and under the text, 24 px at the sides
        var markdownLeft = markdown.TranslatePoint(default, ui.Document)!.Value.X;
        var banner = ui.Document.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatPlanBanner"));
        var page = Assert.IsAssignableFrom<Control>(banner.GetVisualParent());
        Assert.Equal(new Thickness(24, 16), page.Margin);
        Assert.Equal(24, markdownLeft, 1);
        ui.Window.Close();
    }

    // ---- approval closes it, a refusal keeps it ----

    [AvaloniaFact]
    public void Approval_closes_the_plan_and_a_refusal_keeps_it_open()
    {
        var c = Planning("r1");
        var ui = new Opened(c);
        Assert.True(ui.Document.IsOpen);

        UiHelpers.Click(ui.Window, ui.Option(ui.Strings.PlanKeepPlanning));               // refused
        Assert.True(ui.Document.IsOpen);
        Assert.Contains(RewriteTitle, UiHelpers.Texts(ui.Document));                       // its plan stays
        Assert.DoesNotContain(ui.Strings.PlanReadyForReview, ui.DocumentTexts);            // but nothing waits for comments now

        AskPlan(c, "r2", "# Use a list\n\n1. Write the list");                             // the next plan takes its place
        UiHelpers.Pump();
        Assert.True(ui.Document.IsOpen);
        Assert.Contains("Use a list", UiHelpers.Texts(ui.Document));
        Assert.DoesNotContain(RewriteTitle, UiHelpers.Texts(ui.Document));
        Assert.Contains(ui.Strings.PlanReadyForReview, ui.DocumentTexts);

        UiHelpers.Click(ui.Window, ui.Option(ui.Strings.PlanYesManual));                   // approved
        Assert.Equal("allow:r2", ui.Sent.Last());
        Assert.False(ui.Document.IsOpen);

        // a plan that left unanswered (the turn was stopped) is no approval: the document stays
        AskPlan(c, "r3");
        UiHelpers.Pump();
        Assert.True(ui.Document.IsOpen);
        c.Apply(new PermissionCancelled("r3"));
        UiHelpers.Pump();
        Assert.True(ui.Document.IsOpen);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_read_only_view_does_not_open_the_plan_by_itself()
    {
        var c = new ChatConversation();
        var view = new ClaudeChatView { Conversation = c, IsReadOnly = true };
        var window = UiHelpers.Show(view);
        var document = view.GetVisualDescendants().OfType<PlanDocumentPanel>().Single();

        AskPlan(c, "r1");
        UiHelpers.Pump();
        Assert.False(document.IsOpen);                                                     // the plan cannot be answered here

        view.IsReadOnly = false;                                                           // the editable view: the positive pair
        c.Apply(new PermissionCancelled("r1"));
        AskPlan(c, "r2");
        UiHelpers.Pump();
        Assert.True(document.IsOpen);
        window.Close();
    }

    // ---- the row in the timeline ----

    private static List<string> RowTexts(ToolCallView row) => UiHelpers.Texts(row);

    [AvaloniaFact]
    public void The_timeline_row_reads_claudes_plan_and_user_approved_the_plan()
    {
        // a history: the plan approved, with its file
        var ui = new Opened(UiHelpers.Recording("plan", "transcript"));
        var row = ui.PlanRow();
        var texts = RowTexts(row);
        Assert.Contains("Claude\u2019s Plan", texts);
        Assert.Contains(RecordedFile, texts);
        Assert.Contains("User approved the plan", texts);
        Assert.DoesNotContain(ui.Strings.ToolInput, texts);                                // no IN/OUT box
        Assert.DoesNotContain(ui.Strings.ToolOutput, texts);
        Assert.DoesNotContain(ui.Strings.ToolRejected, texts);
        Assert.DoesNotContain(texts, t => t.Contains("Fourth point") || t.Contains("approved your plan")); // no plan text, no result
        var name = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Claude\u2019s Plan");
        Assert.Equal(Avalonia.Media.FontWeight.Bold, name.FontWeight);
        ui.Window.Close();

        // refused, then approved: the recording plan-feedback, live
        ui = new Opened(UiHelpers.Recording("plan-feedback", "stream"));
        Assert.Contains("Stayed in plan mode", RowTexts(ui.PlanRow(0)));
        Assert.DoesNotContain("User approved the plan", RowTexts(ui.PlanRow(0)));
        Assert.DoesNotContain(ui.Strings.ToolRejected, RowTexts(ui.PlanRow(0)));          // the line says it
        Assert.Contains("User approved the plan", RowTexts(ui.PlanRow(1)));
        ui.Window.Close();

        // a call that came without its plan (plan-auto's history): "Plan Mode"
        ui = new Opened(UiHelpers.Recording("plan-auto", "transcript"));
        texts = RowTexts(ui.PlanRow());
        Assert.Contains("Plan Mode", texts);
        Assert.DoesNotContain("Claude\u2019s Plan", texts);
        ui.Window.Close();

        // while it waits: the plan and its file, and no second line; only the plan: no file
        var c = Planning();
        c.Apply(new AssistantBlock("m9", null, new ToolUseBlock("t-9", "ExitPlanMode", J("{\"plan\":\"# Short\"}"))));
        ui = new Opened(c);
        texts = RowTexts(ui.PlanRow(0));
        Assert.Contains("Claude\u2019s Plan", texts);
        Assert.Contains("rewrite.md", texts);
        Assert.DoesNotContain("User approved the plan", texts);
        Assert.DoesNotContain("Stayed in plan mode", texts);
        texts = RowTexts(ui.PlanRow(1));
        Assert.Contains("Claude\u2019s Plan", texts);
        Assert.DoesNotContain("rewrite.md", texts);

        ui.View.Strings = ChatStrings.PortugueseBrazil;                                    // and in the other language
        UiHelpers.Pump();
        Assert.Contains(ChatStrings.PortugueseBrazil.PlanTitle, RowTexts(ui.PlanRow(0)));
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_click_on_the_row_title_opens_the_plan_again()
    {
        // a history: the row opens the plan to read, without comments
        var ui = new Opened(UiHelpers.Recording("plan", "transcript"));
        Assert.False(ui.Document.IsOpen);
        var row = ui.PlanRow();
        UiHelpers.Click(ui.Window, row.Header);
        Assert.True(ui.Document.IsOpen);
        Assert.Contains(RecordedTitle, UiHelpers.Texts(ui.Document));
        Assert.DoesNotContain(ui.Strings.PlanReadyForReview, ui.DocumentTexts);
        Assert.False(((ToolCallItem)row.DataContext!).IsExpanded);                         // the click opened no subagent
        UiHelpers.Press(ui.Window, Key.Escape);                                            // Esc closes it
        Assert.False(ui.Document.IsOpen);
        ui.Window.Close();

        // live: closed with its ×, the row brings back the plan under review, comments and all
        ui = new Opened(Planning());
        UiHelpers.Click(ui.Window, ui.Close);
        Assert.False(ui.Document.IsOpen);
        Assert.True(ui.Option(ui.Strings.PlanYesAutoMode).IsFocused);                      // the keyboard goes back to the card
        UiHelpers.Click(ui.Window, ui.PlanRow().Header);
        Assert.True(ui.Document.IsOpen);
        Assert.Contains(RewriteTitle, UiHelpers.Texts(ui.Document));
        Assert.Contains(ui.Strings.PlanReadyForReview, ui.DocumentTexts);
        ui.Window.Close();

        // a row without the plan's text opens nothing
        ui = new Opened(UiHelpers.Recording("plan-auto", "transcript"));
        row = ui.PlanRow();
        Assert.False(row.Header.IsHitTestVisible);
        ui.Window.Close();
    }

    // ---- the keyboard: the rule of today, and Esc for the top layer only ----

    [AvaloniaFact]
    public void A_request_that_arrives_with_the_plan_open_keeps_the_existing_focus_rule()
    {
        // the document opened from the row (the keyboard in the view): a request that comes takes the keyboard, as ever
        var c = UiHelpers.Recording("plan", "transcript");
        var ui = new Opened(c);
        UiHelpers.Click(ui.Window, ui.PlanRow().Header);
        Assert.True(ui.Document.IsOpen);
        c.Apply(Bash("b1"));
        UiHelpers.Pump();
        Assert.True(ui.Prompt.IsKeyboardFocusWithin);
        Assert.True(ui.Document.IsOpen);                                                   // it does not cover the card: it stays

        // Esc closes the document only; the request is still there, with the keyboard; the next Esc is the card's "No"
        UiHelpers.Press(ui.Window, Key.Escape);
        Assert.False(ui.Document.IsOpen);
        Assert.Empty(ui.Sent);
        Assert.Same(c.CurrentPermission, ui.Prompt.Request);
        Assert.True(ui.Prompt.IsKeyboardFocusWithin);
        UiHelpers.Press(ui.Window, Key.Escape);
        Assert.Equal(new[] { "deny:b1" }, ui.Sent);
        ui.Window.Close();

        // with something typed in the reply box, a request that comes leaves the keyboard there, document or not
        c = UiHelpers.Recording("plan", "transcript");
        ui = new Opened(c);
        UiHelpers.Click(ui.Window, ui.PlanRow().Header);
        ui.Input.Focus();
        UiHelpers.Type(ui.Window, "half a thought");
        c.Apply(Bash("b2"));
        UiHelpers.Pump();
        Assert.True(ui.Input.IsFocused);
        Assert.False(ui.Prompt.IsKeyboardFocusWithin);
        UiHelpers.Press(ui.Window, Key.Escape);                                            // Esc closes the document, not Claude's turn
        Assert.False(ui.Document.IsOpen);
        Assert.True(ui.Input.IsFocused);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Esc_closes_the_plan_first_and_only_then_says_no()
    {
        var ui = new Opened(Planning());
        Assert.True(ui.Prompt.IsKeyboardFocusWithin);                                      // the card took the keyboard with the plan
        Assert.True(ui.Document.IsOpen);

        UiHelpers.Press(ui.Window, Key.Escape);
        Assert.False(ui.Document.IsOpen);
        Assert.Empty(ui.Sent);                                                             // the plan is still to answer
        Assert.True(ui.Prompt.IsKeyboardFocusWithin);

        UiHelpers.KeyDown(ui.Window, Key.Escape);                                          // the card on top: Esc is "No"
        Assert.Equal(new[] { "deny:r1:User chose to stay in plan mode and continue planning" }, ui.Sent);
        UiHelpers.KeyUp(ui.Window, Key.Escape);

        // an Esc held on the document closes it once, and never goes on to refuse the plan
        AskPlan(ui.Conversation, "r2");
        UiHelpers.Pump();
        Assert.True(ui.Document.IsOpen);
        UiHelpers.KeyDown(ui.Window, Key.Escape);
        UiHelpers.KeyDown(ui.Window, Key.Escape);                                          // the keyboard's repeat
        Assert.False(ui.Document.IsOpen);
        Assert.Single(ui.Sent);
        UiHelpers.KeyUp(ui.Window, Key.Escape);
        ui.Window.Close();
    }

    // ---- the comments on the plan (Task 8) ----

    private static PlanTextBlock Block(Opened ui, string words) => ui.Document.Blocks.First(b => b.Words == words);

    /// <summary>The middle of character <paramref name="index"/> of <paramref name="block"/>, a little to the left or to the right of it.</summary>
    private static Point CharPoint(PlanTextBlock block, int index, bool right, Visual root)
    {
        var rect = block.TextLayout.HitTestTextRange(index, 1).First();
        var x = right ? rect.Right - rect.Width * 0.4 : rect.Left + rect.Width * 0.4; // well inside the glyph: its edge may fall outside the block
        return block.TranslatePoint(new Point(block.Padding.Left + x, block.Padding.Top + rect.Top + rect.Height / 2), root)!.Value;
    }

    /// <summary>Drags the mouse over the characters <paramref name="from"/> to <paramref name="to"/> (not included) of the block.</summary>
    private static void Select(Window window, PlanTextBlock block, int from, int to)
    {
        block.BringIntoView(); // the card grows with each comment, and the document over the list gets shorter
        UiHelpers.Pump();
        UiHelpers.DrawFrames(); // what is under the pointer is found in the last frame drawn
        var start = CharPoint(block, from, right: false, window);
        var end = CharPoint(block, to - 1, right: true, window);
        window.MouseMove(start, RawInputModifiers.None);
        window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        window.MouseUp(end, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        UiHelpers.Pump();
    }

    /// <summary>Within a pixel: the layout rounds to pixels what the text measures.</summary>
    private static void Near(double expected, double actual) =>
        Assert.True(Math.Abs(expected - actual) <= 1, "expected " + expected + ", got " + actual);

    private static Button? AddCommentButton(Opened ui) =>
        ui.Document.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => b.Classes.Contains("ClaudeChatPlanAddComment") && b.IsEffectivelyVisible);

    /// <summary>"Add Comment", brought into sight first: under a passage at the bottom of the document, it scrolls with it.</summary>
    private static void ClickAddComment(Opened ui)
    {
        var add = AddCommentButton(ui) ?? throw new InvalidOperationException("no Add Comment on screen");
        add.BringIntoView();
        UiHelpers.Pump();
        UiHelpers.DrawFrames();
        UiHelpers.Click(ui.Window, add);
    }

    private static PlanCommentBox CommentBox(Opened ui) => ui.View.GetVisualDescendants().OfType<PlanCommentBox>().Single();

    private static Border BoxFrame(Opened ui) =>
        CommentBox(ui).GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatPlanCommentBox"));

    [AvaloniaTheory]
    [InlineData(ChatLayout.Desktop)]
    [InlineData(ChatLayout.Touch)]
    public void Selecting_text_in_the_plan_offers_add_comment(ChatLayout layout)
    {
        var ui = new Opened(Planning(), layout);
        var strings = ui.Strings;
        var plan = ui.Conversation.CurrentPlan!;
        var intro = Block(ui, "Intro line.");
        Assert.Null(AddCommentButton(ui));                                                 // nothing selected: nothing offered

        Select(ui.Window, intro, 0, 5);                                                    // "Intro"
        Assert.Equal((0, 5), intro.SelectedRange);
        var add = AddCommentButton(ui);
        Assert.NotNull(add);
        Assert.Equal(strings.PlanAddComment, UiHelpers.Texts(add!).Single());
        var selection = intro.SelectionBounds!.Value;
        var bottomLeft = intro.TranslatePoint(selection.BottomLeft, ui.Window)!.Value;
        var addAt = add!.TranslatePoint(default, ui.Window)!.Value;
        Near(bottomLeft.X, addAt.X);                                            // under its bottom left corner…
        Near(bottomLeft.Y + 6, addAt.Y);                                        // …6 px down
        if (layout == ChatLayout.Touch) Assert.True(add.Bounds.Height >= 44, "Add Comment is " + add.Bounds.Size);

        // the box: 8 px under the passage, 320 px wide, the passage, the field with the keyboard, "Cancel" and "Add Comment"
        UiHelpers.Click(ui.Window, add);
        var box = CommentBox(ui);
        Assert.True(box.IsOpen);
        Assert.Null(AddCommentButton(ui));
        Assert.Equal("Intro", box.QuoteText);
        Assert.True(box.Field.IsFocused);
        Assert.Equal(strings.PlanCommentPlaceholder, box.Field.Watermark);
        var frame = BoxFrame(ui);
        Near(320, frame.Bounds.Width);
        var frameAt = frame.TranslatePoint(default, ui.Window)!.Value;
        Near(bottomLeft.Y + 8, frameAt.Y);
        Near(bottomLeft.X, frameAt.X);
        var cancel = UiHelpers.ButtonWithText(box, strings.PlanCommentCancel);
        var confirm = UiHelpers.ButtonWithText(box, strings.PlanAddComment);
        Assert.True(cancel.TranslatePoint(default, frame)!.Value.X < confirm.TranslatePoint(default, frame)!.Value.X); // "Cancel", then "Add Comment"
        if (layout == ChatLayout.Touch)
        {
            Assert.True(cancel.Bounds.Height >= 44 && confirm.Bounds.Height >= 44, "the box's buttons are " + cancel.Bounds.Size + " and " + confirm.Bounds.Size);
            UiHelpers.Press(ui.Window, Key.Enter);                                         // the phone's Enter breaks the line
            Assert.True(box.IsOpen);
        }
        UiHelpers.Type(ui.Window, "Say more");
        if (layout == ChatLayout.Desktop) UiHelpers.Press(ui.Window, Key.Enter);          // Enter adds
        else UiHelpers.Click(ui.Window, confirm);
        Assert.False(box.IsOpen);

        var comment = Assert.Single(plan.Comments);
        Assert.Equal("Intro", comment.Quote);
        Assert.Equal(RewriteTitle, comment.Heading);                                       // the nearest heading above it
        Assert.Equal("Say more", comment.Text.Trim());

        // the passage is marked and followed by a circle numbered 1, the comment in its tooltip
        var marked = intro.Inlines!.OfType<Avalonia.Controls.Documents.Run>().Where(r => r.Classes.Contains("ClaudeChatPlanMarked")).ToList();
        Assert.Equal("Intro", string.Concat(marked.Select(r => r.Text)));
        var circle = intro.Inlines!.OfType<Avalonia.Controls.Documents.InlineUIContainer>().Single().Child;
        Assert.Equal("1", UiHelpers.Texts(circle).Single());
        Assert.Equal(comment.Text, ToolTip.GetTip(circle));
        Assert.Equal("Intro line.", intro.Words);                                          // the circle is no word of the block

        // and the card is "Continue planning"
        Assert.Contains(strings.PlanContinuePlanning, UiHelpers.Texts(ui.Prompt));
        Assert.Contains(strings.FormatPlanComments(1), UiHelpers.Texts(ui.Prompt));
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_selection_after_a_comment_and_across_inline_code_quotes_the_words()
    {
        var ui = new Opened(Planning());
        var intro = Block(ui, "Intro line.");
        Select(ui.Window, intro, 0, 5);
        ClickAddComment(ui);
        UiHelpers.Type(ui.Window, "First");
        UiHelpers.Press(ui.Window, Key.Enter);

        // after the circle, the places of the block still give its words: "line" is at 7 to 11 of the drawn text, 6 to 10 of the words
        Select(ui.Window, intro, 7, 11);
        ClickAddComment(ui);
        Assert.Equal("line", CommentBox(ui).QuoteText);
        UiHelpers.Type(ui.Window, "Second");
        UiHelpers.Press(ui.Window, Key.Enter);
        var plan = ui.Conversation.CurrentPlan!;
        Assert.Equal(new[] { "Intro", "line" }, plan.Comments.Select(c => c.Quote).ToArray());
        Assert.Equal(new[] { "1", "2" }, intro.Inlines!.OfType<Avalonia.Controls.Documents.InlineUIContainer>().Select(c => UiHelpers.Texts(c.Child).Single()).ToArray());

        // a block with inline code: the code is a run of its own, and a selection over it quotes the plain words, filed under the
        // heading over the list
        var read = ui.Document.Blocks.First(b => b.Words.StartsWith("Read ", StringComparison.Ordinal));
        Assert.Equal("Read parser.cs", read.Words);
        Assert.Contains(read.Inlines!.OfType<Avalonia.Controls.Documents.Run>(), r => r.Classes.Contains("ClaudeChatPlanCode") && r.Text == "parser.cs");
        Select(ui.Window, read, 0, read.Words.Length);
        ClickAddComment(ui);
        Assert.Equal("Read parser.cs", CommentBox(ui).QuoteText);
        UiHelpers.Press(ui.Window, Key.Enter);                                             // an empty comment adds nothing, and the box stays
        Assert.True(CommentBox(ui).IsOpen);
        Assert.Equal(2, plan.Comments.Count);
        UiHelpers.Type(ui.Window, "Third");
        UiHelpers.Press(ui.Window, Key.Enter);
        Assert.Equal("Steps", plan.Comments[2].Heading);

        // Esc closes the box only: the document stays, and nothing is sent
        Select(ui.Window, read, 0, 4);
        ClickAddComment(ui);
        Assert.True(CommentBox(ui).IsOpen);
        UiHelpers.Press(ui.Window, Key.Escape);
        Assert.False(CommentBox(ui).IsOpen);
        Assert.True(ui.Document.IsOpen);
        Assert.Equal(3, plan.Comments.Count);
        Assert.Empty(ui.Sent);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_plan_that_takes_no_comments_offers_none()
    {
        // a history: the plan opened again from its row is only to read
        var ui = new Opened(UiHelpers.Recording("plan", "transcript"));
        UiHelpers.Click(ui.Window, ui.PlanRow().Header);
        Assert.True(ui.Document.IsOpen);
        var block = ui.Document.Blocks.First(b => b.Words.Length > 8 && !b.IsHeading);
        Select(ui.Window, block, 0, 5);
        Assert.NotNull(block.SelectedRange);                                               // it can be selected (and copied)…
        Assert.Null(AddCommentButton(ui));                                                 // …but not commented on
        ui.Window.Close();

        // refused: the plan stays on screen, without its banner and without comments
        ui = new Opened(Planning());
        UiHelpers.Click(ui.Window, ui.Option(ui.Strings.PlanKeepPlanning));
        Select(ui.Window, Block(ui, "Intro line."), 0, 5);
        Assert.Null(AddCommentButton(ui));
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void The_cross_removes_a_comment_and_its_mark()
    {
        var ui = new Opened(Planning());
        var intro = Block(ui, "Intro line.");
        Select(ui.Window, intro, 0, 5);
        ClickAddComment(ui);
        UiHelpers.Type(ui.Window, "First");
        UiHelpers.Press(ui.Window, Key.Enter);
        var read = ui.Document.Blocks.First(b => b.Words.StartsWith("Read ", StringComparison.Ordinal));
        Select(ui.Window, read, 0, 4);
        ClickAddComment(ui);
        UiHelpers.Type(ui.Window, "Second");
        UiHelpers.Press(ui.Window, Key.Enter);
        Assert.Single(intro.Marks);
        Assert.Equal(2, read.Marks.Single().Number);

        var crosses = ui.Prompt.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("ClaudeChatPlanCommentRemove") && b.IsEffectivelyVisible).ToList();
        Assert.Equal(2, crosses.Count);
        Assert.Equal(ui.Strings.PlanRemoveComment, ToolTip.GetTip(crosses[0]));
        UiHelpers.Click(ui.Window, crosses[0]);
        Assert.Equal("Second", Assert.Single(ui.Conversation.CurrentPlan!.Comments).Text);
        Assert.Empty(intro.Marks);                                                         // its mark goes with it
        Assert.Equal(1, read.Marks.Single().Number);                                       // and the other one is number 1 now
        Assert.Contains(ui.Strings.FormatPlanCommentsIncluded(1), UiHelpers.Texts(ui.Prompt));
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Sending_the_feedback_clears_the_comments_and_their_marks()
    {
        var ui = new Opened(Planning());
        var intro = Block(ui, "Intro line.");
        Select(ui.Window, intro, 0, 5);
        ClickAddComment(ui);
        UiHelpers.Type(ui.Window, "Say more");
        UiHelpers.Press(ui.Window, Key.Enter);

        UiHelpers.Click(ui.Window, ui.Option(ui.Strings.PlanSendFeedback));
        Assert.Equal(new[] { "deny:r1:User chose to stay in plan mode and continue planning\n\nComments on the plan:\n[Re: \"Intro\"] Say more" }, ui.Sent);
        Assert.True(ui.Document.IsOpen);                                                   // a refusal keeps the document
        Assert.Empty(intro.Marks);
        ui.Window.Close();
    }

    // ---- what the review of Task 7 asked for ----

    [AvaloniaFact]
    public void A_request_without_the_plans_text_opens_no_empty_document()
    {
        var c = new ChatConversation();
        c.Apply(new StatusChanged(null, "plan"));
        c.Apply(new PermissionRequested("r1", "ExitPlanMode", "ExitPlanMode", null, "t-r1", J("{}"), null));
        var ui = new Opened(c);
        Assert.NotNull(c.CurrentPlan);                                                     // the card asks about it…
        Assert.Contains(ui.Strings.PlanAccept, UiHelpers.Texts(ui.Prompt));
        Assert.False(ui.Document.IsOpen);                                                  // …but there is nothing to show
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void The_document_that_opened_by_itself_closes_when_the_view_becomes_read_only()
    {
        var ui = new Opened(Planning());
        Assert.True(ui.Document.IsOpen);
        ui.View.IsReadOnly = true;
        UiHelpers.Pump();
        Assert.False(ui.Document.IsOpen);

        // the comment box closes with it
        ui.View.IsReadOnly = false;
        UiHelpers.Click(ui.Window, ui.PlanRow().Header);
        Select(ui.Window, Block(ui, "Intro line."), 0, 5);
        ClickAddComment(ui);
        Assert.True(CommentBox(ui).IsOpen);
        ui.View.IsReadOnly = true;
        UiHelpers.Pump();
        Assert.False(CommentBox(ui).IsOpen);
        Assert.Empty(ui.Conversation.CurrentPlan!.Comments);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Touch_a_tap_on_the_row_title_opens_the_plan_again()
    {
        var ui = new Opened(UiHelpers.Recording("plan", "transcript"), ChatLayout.Touch);
        Assert.False(ui.Document.IsOpen);
        UiHelpers.Click(ui.Window, ui.PlanRow().Header);
        Assert.True(ui.Document.IsOpen);
        Assert.Contains(RecordedTitle, UiHelpers.Texts(ui.Document));
        Assert.True(ui.View.CloseTopLayer());                                              // the host's back button closes it
        Assert.False(ui.Document.IsOpen);
        ui.Window.Close();
    }
}
