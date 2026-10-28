using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using ClaudeVisualChat.Controls;
using Xunit;
using PermissionCancelled = ClaudeVisualChat.Protocol.PermissionCancelled;
using PermissionRequested = ClaudeVisualChat.Protocol.PermissionRequested;
using ToolResultBlock = ClaudeVisualChat.Protocol.ToolResultBlock;
using TurnCompleted = ClaudeVisualChat.Protocol.TurnCompleted;
using UserBlock = ClaudeVisualChat.Protocol.UserBlock;

namespace ClaudeVisualChat.Tests.Ui;

public class PermissionPromptTests
{
    private const string Suggestions = "[{\"type\":\"setMode\",\"mode\":\"acceptEdits\",\"destination\":\"session\"}]";

    // the desktop's card, as the extension 2.1.310 asks (the touch layout keeps the 0.5.0 request: PermissionTouchTests)
    private const string RunCommand = "Allow this bash command?";
    private const string YesAlways = "Yes, allow all edits this session"; // option 2 of the default suggestions (setMode acceptEdits)
    private const string No = "No";

    private static JsonElement J(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static PermissionRequested Request(
        string id, string tool, string input, string? suggestions = null, string? displayName = null,
        string? description = null, bool defaultToNo = false, bool suppressAlwaysAllowRule = false, string? agentId = null) =>
        new PermissionRequested(id, tool, displayName ?? tool, description, "tool-" + id, J(input),
            suggestions == null ? (JsonElement?)null : J(suggestions))
        {
            DefaultToNo = defaultToNo,
            SuppressAlwaysAllowRule = suppressAlwaysAllowRule,
            AgentId = agentId,
        };

    private static PermissionRequested Bash(string id, string command, string? suggestions = null) =>
        Request(id, "Bash", JsonSerializer.Serialize(new { command }), suggestions);

    /// <summary>A clock that moves only when the test says.</summary>
    private sealed class ManualClock : IPromptClock
    {
        public System.TimeSpan Now { get; private set; } = System.TimeSpan.FromHours(1);

        public void Advance(int milliseconds) => Now += System.TimeSpan.FromMilliseconds(milliseconds);
    }

    /// <summary>
    /// A clock where a second goes by between any two readings, so no answer ever comes too soon: for the tests that
    /// are not about the pause after a new request.
    /// </summary>
    private sealed class SlowClock : IPromptClock
    {
        private System.TimeSpan _now = System.TimeSpan.FromHours(1);

        public System.TimeSpan Now => _now += System.TimeSpan.FromSeconds(1);
    }

    private sealed class Opened
    {
        public Opened(ChatConversation conversation, ChatStrings? strings, IPromptClock? clock)
        {
            Conversation = conversation;
            conversation.PermissionAnswered += (_, e) => Answers.Add(e);
            View = new ClaudeChatView { Conversation = conversation };
            View.Prompt.Clock = clock ?? new SlowClock();
            if (strings != null) View.Strings = strings;
            Window = UiHelpers.Show(View);
            Prompt = UiHelpers.Prompt(View);
        }

        public ChatConversation Conversation { get; }
        public ClaudeChatView View { get; }
        public Window Window { get; }
        public PermissionPrompt Prompt { get; }
        public List<PermissionAnsweredEventArgs> Answers { get; } = new List<PermissionAnsweredEventArgs>();

        public List<string> Texts => UiHelpers.Texts(Prompt);

        public Button Option(string text) => UiHelpers.ButtonWithText(Prompt, text);

        /// <summary>The field of "No", when it is on screen (on the desktop, always, under the options).</summary>
        public TextBox? DenyField =>
            Prompt.GetVisualDescendants().OfType<TextBox>().SingleOrDefault(t => t.IsEffectivelyVisible);

        /// <summary>The numbers in front of the options that the user can see.</summary>
        public List<string> Numbers => Prompt.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Classes.Contains("ClaudeChatOptionNumber") && t.IsEffectivelyVisible).Select(t => t.Text ?? "").ToList();

        /// <summary>The option buttons that are on screen, in order.</summary>
        public List<Button> Options => Prompt.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Classes.Contains("ClaudeChatOption") && b.IsEffectivelyVisible).ToList();

        /// <summary>The frame of the card: hidden while the card waits for the typing in the reply box to pause.</summary>
        public Border Frame => Prompt.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("ClaudeChatPermission"));
    }

    private static Opened Open(ChatConversation conversation, ChatStrings? strings = null, IPromptClock? clock = null) =>
        new Opened(conversation, strings, clock);

    private static object? Resource(Control view, string key) =>
        view.TryFindResource(key, out var value) ? value : null;

    // ---- the brief ----

    [AvaloniaFact]
    public void Bash_request_shows_the_command_and_yes_answers_on_click()
    {
        var c = new ChatConversation();
        var ui = Open(c);
        Assert.False(ui.Prompt.IsEffectivelyVisible); // nothing asked, nothing shown

        c.Apply(Bash("r1", "git push origin master"));
        UiHelpers.Pump();

        Assert.True(ui.Prompt.IsEffectivelyVisible);
        Assert.Contains(RunCommand, ui.Texts);
        Assert.Contains("git push origin master", ui.Texts);
        foreach (var expected in new[] { "1", "Yes", "2", No }) Assert.Contains(expected, ui.Texts); // the visible options, numbered in order

        UiHelpers.Click(ui.Window, ui.Option("Yes"));

        var answer = Assert.Single(ui.Answers);
        Assert.Equal(PermissionDecision.Allow, answer.Decision);
        Assert.Equal("r1", answer.Request.RequestId);
        Assert.False(ui.Prompt.IsEffectivelyVisible);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Option_two_exists_only_with_suggestions()
    {
        var c = new ChatConversation();
        c.Apply(Bash("r1", "ls", "[]"));
        var ui = Open(c);

        Assert.DoesNotContain(YesAlways, ui.Texts);
        Assert.Equal(new[] { "1", "2" }, ui.Numbers); // the visible options, numbered in order: "No" is 2
        ui.Option("Yes").Focus();
        UiHelpers.Press(ui.Window, Key.D3);
        Assert.Empty(ui.Answers); // the digit after the last option goes to the field
        Assert.True(ui.DenyField!.IsFocused);

        UiHelpers.Click(ui.Window, ui.Option("Yes"));
        c.Apply(Request("r2", "Write", "{\"file_path\":\"a.txt\",\"content\":\"one\"}", Suggestions));
        UiHelpers.Pump();
        Assert.Contains(YesAlways, ui.Texts);
        Assert.Equal(new[] { "1", "2", "3" }, ui.Numbers);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void The_field_is_always_under_the_options_and_enter_in_it_denies_with_its_text()
    {
        var c = new ChatConversation();
        c.Apply(Bash("r1", "git diff --stat > out.txt"));
        var ui = Open(c);

        var field = ui.DenyField;
        Assert.NotNull(field);                                                   // no option opens it: it is there
        Assert.Equal("Tell Claude what to do instead", field!.Watermark);
        Assert.True(field.TranslatePoint(default, ui.Prompt)!.Value.Y > ui.Option(No).TranslatePoint(default, ui.Prompt)!.Value.Y);
        Assert.Contains("Esc to cancel", ui.Texts);                              // and the hint under it
        field.Focus();
        UiHelpers.Pump();
        UiHelpers.Type(ui.Window, "use git diff");
        Assert.Empty(ui.Answers);

        UiHelpers.Press(ui.Window, Key.Enter);

        var answer = Assert.Single(ui.Answers);
        Assert.Equal(PermissionDecision.Deny, answer.Decision);
        Assert.Equal("use git diff", answer.Message);
        Assert.False(ui.Prompt.IsEffectivelyVisible);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Two_requests_show_how_many_wait()
    {
        var c = new ChatConversation();
        c.Apply(Bash("r1", "npm test"));
        c.Apply(Request("r2", "Write", "{\"file_path\":\"a.txt\",\"content\":\"one\"}", Suggestions));
        var ui = Open(c);

        Assert.Contains("+1 waiting", ui.Texts);
        Assert.Contains(RunCommand, ui.Texts);

        UiHelpers.Click(ui.Window, ui.Option("Yes"));

        Assert.Equal("r1", Assert.Single(ui.Answers).Request.RequestId);
        Assert.True(ui.Prompt.IsEffectivelyVisible); // the next one
        Assert.Contains("Allow write to a.txt?", ui.Texts);
        Assert.DoesNotContain(ui.Texts, t => t.EndsWith("waiting", System.StringComparison.Ordinal));
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Key_two_with_focus_in_the_prompt_allows_always()
    {
        var c = new ChatConversation();
        var ui = Open(c);
        c.Apply(Request("r1", "Write", "{\"file_path\":\"a.txt\",\"content\":\"one\"}", Suggestions));
        UiHelpers.Pump();
        Assert.True(ui.Prompt.IsKeyboardFocusWithin); // it took the focus: the reply box is empty

        UiHelpers.Press(ui.Window, Key.D2);

        var answer = Assert.Single(ui.Answers);
        Assert.Equal(PermissionDecision.AllowAlways, answer.Decision);
        ui.Window.Close();
    }

    // ---- keys ----

    [AvaloniaFact]
    public void Keys_one_and_three_answer_from_the_prompt()
    {
        var c = new ChatConversation();
        c.Apply(Bash("r1", "dotnet build"));
        c.Apply(Bash("r2", "dotnet test"));
        var ui = Open(c);
        ui.Option("Yes").Focus();

        UiHelpers.Press(ui.Window, Key.D1);
        Assert.Equal(PermissionDecision.Allow, Assert.Single(ui.Answers).Decision);
        Assert.True(ui.Prompt.IsKeyboardFocusWithin); // the next request keeps the focus

        UiHelpers.Press(ui.Window, Key.NumPad3);
        Assert.Single(ui.Answers); // without option 2, "No" is 2 and 3 goes to the field
        Assert.True(ui.DenyField!.IsFocused);
        UiHelpers.Press(ui.Window, Key.Enter);

        Assert.Equal(2, ui.Answers.Count);
        Assert.Equal(PermissionDecision.Deny, ui.Answers[1].Decision);
        Assert.Null(ui.Answers[1].Message); // nothing typed: the claude gets the default text
        Assert.True(UiHelpers.Input(ui.View).IsFocused); // back to the reply box
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Digits_typed_in_the_reply_box_are_text()
    {
        var c = new ChatConversation();
        c.Apply(Bash("r1", "rm -rf build", Suggestions));
        var ui = Open(c);
        var input = UiHelpers.Input(ui.View);
        input.Focus();

        foreach (var (key, text) in new[] { (Key.D1, "1"), (Key.D2, "2"), (Key.D3, "3") })
        {
            UiHelpers.Press(ui.Window, key);
            UiHelpers.Type(ui.Window, text);
        }

        Assert.Empty(ui.Answers);
        Assert.Equal("123", input.Text);
        Assert.True(input.IsFocused);
        Assert.Equal("", ui.DenyField!.Text ?? "");
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Digits_typed_in_the_field_are_text_and_esc_sends_no_with_it()
    {
        var c = new ChatConversation();
        c.Apply(Bash("r1", "git push --force", Suggestions)); // all three options exist
        c.Apply(Bash("r2", "git push"));
        var ui = Open(c);
        var unhandled = UiHelpers.UnhandledKeys(ui.Window);
        UiHelpers.Press(ui.Window, Key.D4);                                      // the digit after the last option: the field
        Assert.True(ui.DenyField!.IsFocused);

        foreach (var (key, text) in new[] { (Key.D1, "1"), (Key.D2, "2"), (Key.D3, "3") })
        {
            UiHelpers.Press(ui.Window, key);
            UiHelpers.Type(ui.Window, text);
        }
        UiHelpers.Type(ui.Window, " commits only");
        Assert.Empty(ui.Answers);
        Assert.Equal("123 commits only", ui.DenyField!.Text);

        UiHelpers.Press(ui.Window, Key.Escape);

        var answer = Assert.Single(ui.Answers);
        Assert.Equal(PermissionDecision.Deny, answer.Decision);
        Assert.Equal("123 commits only", answer.Message);
        Assert.DoesNotContain(Key.Escape, unhandled); // the prompt used it
        Assert.Equal("r2", ui.Prompt.Request!.RequestId);
        Assert.Equal("", ui.DenyField!.Text ?? "");                             // the next request's field starts empty
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Keys_with_modifiers_are_left_alone()
    {
        var c = new ChatConversation();
        c.Apply(Bash("r1", "ls"));
        var ui = Open(c);
        var unhandled = UiHelpers.UnhandledKeys(ui.Window);
        ui.Option("Yes").Focus();

        UiHelpers.Press(ui.Window, Key.D1, RawInputModifiers.Control);

        Assert.Empty(ui.Answers);
        Assert.Contains(Key.D1, unhandled);
        ui.Window.Close();
    }

    // ---- focus ----

    [AvaloniaFact]
    public void Prompt_takes_the_focus_only_when_the_reply_box_is_empty()
    {
        var c = new ChatConversation();
        var ui = Open(c);
        var input = UiHelpers.Input(ui.View);
        input.Focus();
        UiHelpers.Type(ui.Window, "half a thought");

        c.Apply(Bash("r1", "ls"));
        UiHelpers.Pump();

        Assert.True(ui.Prompt.IsEffectivelyVisible);
        Assert.True(input.IsFocused);
        Assert.False(ui.Prompt.IsKeyboardFocusWithin);

        c.DismissPermission("r1");
        input.Clear();
        c.Apply(Bash("r2", "ls -la"));
        UiHelpers.Pump();

        Assert.True(ui.Option("Yes").IsFocused);

        UiHelpers.Press(ui.Window, Key.Enter); // Enter on the focused option is a click
        Assert.Equal(PermissionDecision.Allow, Assert.Single(ui.Answers).Decision);
        Assert.True(input.IsFocused); // back to the reply box
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_view_built_during_layout_still_puts_the_focus_on_a_waiting_request()
    {
        // like a tab whose content is built by a template while the window lays out
        var c = new ChatConversation();
        c.Apply(Bash("r1", "ls", Suggestions));
        var list = new ItemsControl
        {
            ItemsSource = new[] { c },
            ItemTemplate = new FuncDataTemplate<ChatConversation>((conversation, _) =>
                new ClaudeChatView { Conversation = conversation, Height = 600 }),
        };
        var window = UiHelpers.Show(list);
        var view = window.GetVisualDescendants().OfType<ClaudeChatView>().Single();

        Assert.True(UiHelpers.ButtonWithText(UiHelpers.Prompt(view), "Yes").IsFocused);
        window.Close();
    }

    [AvaloniaFact]
    public void The_list_keeps_its_end_in_sight_when_a_request_appears()
    {
        var c = new ChatConversation();
        for (var i = 0; i < 100; i++) c.Items.Add(new UserMessageItem("message " + i));
        var ui = Open(c);
        var scroller = UiHelpers.Scroller(ui.View);
        Assert.True(UiHelpers.AtEnd(scroller));
        var viewport = scroller.Viewport.Height;

        c.Apply(Bash("r1", "git status", Suggestions));
        UiHelpers.Pump();

        Assert.True(scroller.Viewport.Height < viewport); // the prompt took room from the list
        Assert.True(UiHelpers.AtEnd(scroller));
        Assert.Contains("message 99", UiHelpers.Texts(ui.View));
        ui.Window.Close();
    }

    // ---- leaving ----

    [AvaloniaFact]
    public void Prompt_leaves_whatever_takes_the_request_away()
    {
        var c = new ChatConversation();
        var ui = Open(c);

        c.Apply(Bash("r1", "a"));
        UiHelpers.Pump();
        Assert.True(ui.Prompt.IsEffectivelyVisible);
        c.Apply(new PermissionCancelled("r1"));
        UiHelpers.Pump();
        Assert.False(ui.Prompt.IsEffectivelyVisible);

        c.Apply(Bash("r2", "b"));
        UiHelpers.Pump();
        Assert.True(ui.Prompt.IsEffectivelyVisible);
        c.Apply(new UserBlock(null, new ToolResultBlock("tool-r2", "done", false), null, false, false));
        UiHelpers.Pump();
        Assert.False(ui.Prompt.IsEffectivelyVisible);

        c.Apply(Bash("r3", "c"));
        UiHelpers.Pump();
        Assert.True(ui.Prompt.IsEffectivelyVisible);
        c.Apply(new TurnCompleted("success", false, 10, 0, 1, 1));
        UiHelpers.Pump();
        Assert.False(ui.Prompt.IsEffectivelyVisible);

        c.Apply(Bash("r4", "d"));
        UiHelpers.Pump();
        ui.DenyField!.Focus(); // even with something written in the field
        UiHelpers.Type(ui.Window, "half a thought");
        c.DismissPermission("r4");
        UiHelpers.Pump();
        Assert.False(ui.Prompt.IsEffectivelyVisible);
        Assert.Empty(ui.Answers);
        ui.Window.Close();
    }

    // ---- what each tool shows ----

    [AvaloniaFact]
    public void Edit_request_shows_the_file_and_its_diff()
    {
        var c = new ChatConversation { WorkingDirectory = "C:\\demo\\app" };
        var input = JsonSerializer.Serialize(new
        {
            file_path = "C:\\demo\\app\\notes.md",
            old_string = "a\nb\nc\nd\ne\nline three",
            new_string = "a\nb\nc\nd\ne\nline 3 (edited)",
        });
        c.Apply(Request("r1", "Edit", input, Suggestions));
        TestLog.Instance.Clear();
        var ui = Open(c, ChatStrings.PortugueseBrazil);
        Assert.Empty(TestLog.Instance.Entries); // no binding or property warnings

        var texts = ui.Texts;
        Assert.Contains("Fazer esta edição em notes.md?", texts);
        Assert.Contains("+2 linhas", texts);
        // the editor's diff, in one column at this width: no "-" or "+" in the text, the colors tell
        Assert.Contains("e", texts);
        Assert.Contains("line three", texts);
        Assert.Contains("line 3 (edited)", texts);
        foreach (var expected in new[] { "Sim", "Sim, permitir todas as edições nesta sessão", "Não" })
            Assert.Contains(expected, texts);
        Assert.False(ui.Prompt.GetVisualDescendants().OfType<DiffView>().Single().IsSideBySide);
        var removed = ui.Prompt.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatDiffLine") && b.Classes.Contains("removed"));
        Assert.Same(Resource(ui.View, "ClaudeChat.DiffRemoved"), removed.Background);

        ui.View.Strings = ChatStrings.English; // the texts follow the language
        UiHelpers.Pump();
        Assert.Contains("Make this edit to notes.md?", ui.Texts);
        Assert.Contains("+2 lines", ui.Texts);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_long_edit_scrolls_to_its_last_line_in_the_request()
    {
        // the list cuts a diff at 200 px; the request shows all of it, in its own scrolling area
        var oldText = string.Join("\n", Enumerable.Range(1, 40).Select(i => "old line " + i));
        var newText = string.Join("\n", Enumerable.Range(1, 40).Select(i => "new line " + i));
        var c = new ChatConversation();
        c.Apply(Request("r1", "Edit", JsonSerializer.Serialize(new { file_path = "a.txt", old_string = oldText, new_string = newText }), Suggestions));
        var ui = Open(c);

        var diff = ui.Prompt.GetVisualDescendants().OfType<DiffView>().Single();
        Assert.False(diff.IsCut);
        var scroller = ui.Prompt.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "DetailScroller");
        Assert.True(scroller.Extent.Height > scroller.Viewport.Height + 100, $"extent {scroller.Extent.Height}, viewport {scroller.Viewport.Height}");
        Assert.True(diff.Bounds.Height > 40 * 19, $"the diff is {diff.Bounds.Height} high");

        scroller.ScrollToEnd();
        UiHelpers.Pump();
        // all of it, not the list's 20 lines a side (Ruling 30): the very last line is on screen
        // (a line paired with its old one is in pieces: its changed characters are marked)
        var last = ui.Prompt.GetVisualDescendants().OfType<TextBlock>().Single(t => (t.Text ?? t.Inlines?.Text) == "new line 40");
        var lastBottom = last.TranslatePoint(new Point(0, last.Bounds.Height), scroller)!.Value.Y;
        Assert.InRange(lastBottom, 0, scroller.Viewport.Height);
        // every line is there: 40 old and 40 new rows, no "+N lines"; the rows off screen are built when scrolled to
        var rows = diff.GetVisualDescendants().OfType<ItemsControl>().Single();
        Assert.Equal(80, rows.ItemCount);
        rows.ScrollIntoView(39);
        UiHelpers.Pump();
        Assert.Contains("old line 40", ui.Texts);
        Assert.DoesNotContain(ui.Texts, t => t.EndsWith(" lines", System.StringComparison.Ordinal) && t.StartsWith("+", System.StringComparison.Ordinal));

        // the same edit in the list keeps the cut
        c.Apply(new ClaudeVisualChat.Protocol.AssistantBlock("m1", null, new ClaudeVisualChat.Protocol.ToolUseBlock("t1", "Edit",
            J(JsonSerializer.Serialize(new { file_path = "a.txt", old_string = oldText, new_string = newText })))));
        UiHelpers.Pump();
        var row = ui.View.GetVisualDescendants().OfType<ToolCallView>().Single();
        Assert.Contains("+20 lines", UiHelpers.Texts(row));
        Assert.DoesNotContain("new line 40", UiHelpers.Texts(row));
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_long_line_shows_whole_in_the_request_and_cut_in_the_list()
    {
        // the request shows every character the user says yes to; the line wraps, as in the extension's editor
        var longLine = "var x = \"" + new string('a', 1500 - 9 - 3 - "FIM_DA_LINHA".Length) + "\"; FIM_DA_LINHA";
        Assert.Equal(1500, longLine.Length);
        var input = JsonSerializer.Serialize(new { file_path = "a.cs", old_string = "var x = 1;", new_string = longLine });
        var c = new ChatConversation();
        c.Apply(Request("r1", "Edit", input, Suggestions));
        var ui = Open(c);

        static string TextOf(TextBlock t) => t.Text ?? t.Inlines?.Text ?? "";
        var line = ui.Prompt.GetVisualDescendants().OfType<TextBlock>().Single(t => TextOf(t).EndsWith("FIM_DA_LINHA", System.StringComparison.Ordinal));
        Assert.Equal(longLine, TextOf(line)); // nothing cut
        Assert.True(line.Bounds.Height > 5 * 19, $"{line.Bounds.Height} px: it wraps"); // many lines of 19 px
        var scroller = ui.Prompt.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "DetailScroller");
        scroller.ScrollToEnd();
        UiHelpers.Pump();
        var bottom = line.TranslatePoint(new Point(0, line.Bounds.Height), scroller)!.Value.Y;
        Assert.InRange(bottom, 0, scroller.Viewport.Height + 1); // its end is reachable on screen

        // in the list, the same line keeps the cut of Task 5
        c.Apply(new ClaudeVisualChat.Protocol.AssistantBlock("m1", null, new ClaudeVisualChat.Protocol.ToolUseBlock("t1", "Edit", J(input))));
        UiHelpers.Pump();
        var row = ui.View.GetVisualDescendants().OfType<ToolCallView>().Single();
        var cut = row.GetVisualDescendants().OfType<TextBlock>().Single(t => TextOf(t).StartsWith("var x = \"aaa", System.StringComparison.Ordinal));
        Assert.EndsWith("…", TextOf(cut));
        Assert.Equal(ToolCallView.MaxLineChars + 1, TextOf(cut).Length);
        Assert.DoesNotContain(UiHelpers.Texts(row), t => t.Contains("FIM_DA_LINHA"));
        ui.Window.Close();
    }

    private const string NpmLocal = "[{\"type\":\"addRules\",\"rules\":[{\"toolName\":\"Bash\",\"ruleContent\":\"npm:*\"}],\"behavior\":\"allow\",\"destination\":\"localSettings\"}]";

    /// <summary>The sentence of option 2 as the user reads it: the words around the underlined parts, and those parts.</summary>
    private static string AlwaysLabel(Opened ui)
    {
        var text = ui.Options[1].GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("ClaudeChatOptionText"));
        if (text.Inlines is not { Count: > 0 } inlines) return text.Text ?? "";
        var label = new System.Text.StringBuilder();
        foreach (var inline in inlines)
        {
            if (inline is Avalonia.Controls.Documents.Run run) label.Append(run.Text);
            else if (inline is Avalonia.Controls.Documents.InlineUIContainer { Child: TextBlock hint }) label.Append(hint.Text);
        }
        return label.ToString();
    }

    /// <summary>The underlined parts of option 2: where the rule is saved and "N more".</summary>
    private static List<TextBlock> AlwaysHints(Opened ui) =>
        ui.Options[1].GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("ClaudeChatAllowAlwaysHint")).ToList();

    [AvaloniaFact]
    public void Option_two_shows_the_rule_label()
    {
        var c = new ChatConversation();
        c.Apply(Bash("r1", "npm test", NpmLocal));
        var ui = Open(c);

        Assert.Equal("Yes, allow npm for this project (just you)", AlwaysLabel(ui));
        var where = Assert.Single(AlwaysHints(ui));
        Assert.Equal("this project (just you)", where.Text);
        Assert.Equal("Saves to .claude/settings.local.json (gitignored)", ToolTip.GetTip(where));
        // a dotted underline, as the extension draws it, and inside the option (not pushed out of it)
        var underline = Assert.Single(where.TextDecorations!);
        Assert.Equal(TextDecorationLocation.Underline, underline.Location);
        Assert.NotNull(underline.StrokeDashArray);
        Assert.True(where.IsEffectivelyVisible);
        var option = ui.Options[1];
        var box = where.TranslatePoint(new Point(0, 0), option)!.Value;
        Assert.True(where.Bounds.Width > 0 && where.Bounds.Height > 0);
        Assert.InRange(box.X, 0, option.Bounds.Width);
        Assert.InRange(box.Y, 0, option.Bounds.Height);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Option_two_says_where_the_rule_is_saved_in_both_languages_and_still_answers_with_the_suggestions()
    {
        var c = new ChatConversation();
        c.Apply(Bash("r1", "npm test", NpmLocal));
        var ui = Open(c);

        ui.View.Strings = ChatStrings.PortugueseBrazil;
        UiHelpers.Pump();
        Assert.Equal("Sim, permitir npm para este projeto (só você)", AlwaysLabel(ui));
        Assert.Equal("Grava em .claude/settings.local.json (fora do git)", ToolTip.GetTip(Assert.Single(AlwaysHints(ui))));

        // answering sends the decision as before: the suggestions go back as they came
        UiHelpers.Click(ui.Window, ui.Options[1]);
        var answer = Assert.Single(ui.Answers);
        Assert.Equal(PermissionDecision.AllowAlways, answer.Decision);
        Assert.Equal("r1", answer.Request.RequestId);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Option_two_with_many_rules_has_a_tooltip_with_the_rest_and_a_cut_rule_is_whole_in_a_tooltip()
    {
        const string Rules = "[{\"type\":\"addRules\",\"rules\":[{\"toolName\":\"Bash\",\"ruleContent\":\"sleep 5 && echo done > done.txt\"},{\"toolName\":\"Bash\",\"ruleContent\":\"git status\"},{\"toolName\":\"Read\"}],\"behavior\":\"allow\",\"destination\":\"userSettings\"}]";
        var c = new ChatConversation();
        c.Apply(Bash("r1", "sleep 5", Rules));
        var ui = Open(c);

        Assert.Equal("Yes, allow sleep 5 && echo d… and 2 more for all projects", AlwaysLabel(ui));
        var hints = AlwaysHints(ui);
        Assert.Equal(new[] { "2 more", "all projects" }, hints.Select(h => h.Text));
        Assert.Equal("• git status\n• Read", ToolTip.GetTip(hints[0]));
        Assert.Equal("Saves to ~/.claude/settings.json", ToolTip.GetTip(hints[1]));
        // the rule that was cut shows whole when the pointer rests on the option
        var text = ui.Options[1].GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("ClaudeChatOptionText"));
        Assert.Equal("Yes, allow sleep 5 && echo done > done.txt and 2 more for all projects", ToolTip.GetTip(text));
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Option_two_without_a_place_to_name_is_plain_text()
    {
        const string Unsaid = "[{\"type\":\"setMode\",\"mode\":\"plan\"}]";
        var c = new ChatConversation();
        c.Apply(Bash("r1", "npm test", Suggestions));   // all edits, this session
        c.Apply(Bash("r2", "npm test", Unsaid));        // any other mode
        c.Apply(Bash("r3", "npm test", "[{\"type\":\"setMode\",\"mode\":\"default\",\"destination\":\"session\"}]"));
        var ui = Open(c);

        Assert.Equal("Yes, allow all edits this session", AlwaysLabel(ui));
        Assert.Empty(AlwaysHints(ui));
        Assert.Contains("Yes, allow all edits this session", ui.Texts);
        c.DismissPermission("r1");
        UiHelpers.Pump();
        Assert.Equal("Yes, and don't ask again", AlwaysLabel(ui));
        Assert.Contains("Yes, and don't ask again", ui.Texts);
        c.DismissPermission("r2");
        UiHelpers.Pump();
        Assert.Equal("Yes, return to normal mode", AlwaysLabel(ui));
        ui.View.Strings = ChatStrings.PortugueseBrazil;
        UiHelpers.Pump();
        Assert.Equal("Sim, voltar ao modo normal", AlwaysLabel(ui));
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void The_pointer_on_the_place_draws_its_underline_solid()
    {
        var c = new ChatConversation();
        c.Apply(Bash("r1", "npm test", NpmLocal));
        var ui = Open(c);
        var where = Assert.Single(AlwaysHints(ui));
        Assert.NotNull(Assert.Single(where.TextDecorations!).StrokeDashArray);

        var center = where.TranslatePoint(new Point(where.Bounds.Width / 2, where.Bounds.Height / 2), ui.Window)!.Value;
        ui.Window.MouseMove(center, RawInputModifiers.None);
        UiHelpers.Pump();
        Assert.True(where.IsPointerOver);
        Assert.Null(Assert.Single(where.TextDecorations!).StrokeDashArray);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_huge_write_request_still_opens_fast()
    {
        // the whole file goes in the request: it must not freeze the view
        var content = string.Join("\n", Enumerable.Range(1, 3000).Select(i => "line " + i));
        var c = new ChatConversation();
        c.Apply(Request("r1", "Write", JsonSerializer.Serialize(new { file_path = "big.txt", content }), Suggestions));
        Open(new ChatConversation()).Window.Close(); // warm up

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var ui = Open(c);
        watch.Stop();
        Assert.True(watch.Elapsed < System.TimeSpan.FromSeconds(2), $"took {watch.ElapsedMilliseconds} ms"); // built as it scrolls
        var scroller = ui.Prompt.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "DetailScroller");
        scroller.ScrollToEnd();
        UiHelpers.Pump();
        Assert.Contains("line 3000", ui.Texts);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Write_shows_what_it_writes()
    {
        var c = new ChatConversation();
        c.Apply(Request("r1", "Write", "{\"file_path\":\"todo.md\",\"content\":\"- Item 1\\n- Item 2\\n\"}", Suggestions));
        var ui = Open(c);

        Assert.Contains("Allow write to todo.md?", ui.Texts);
        Assert.Contains("- Item 1", ui.Texts); // added lines, in the added color: unlike the extension, which opens a diff tab
        Assert.Contains("- Item 2", ui.Texts);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Web_fetch_asks_about_the_url_in_both_languages()
    {
        var c = new ChatConversation();
        c.Apply(Request("r1", "WebFetch", "{\"url\":\"https://example.com/docs\"}", displayName: "Fetch"));
        var ui = Open(c);

        Assert.Contains("Allow fetching this url?", ui.Texts);
        Assert.Contains("https://example.com/docs", ui.Texts);

        ui.View.Strings = ChatStrings.PortugueseBrazil;
        UiHelpers.Pump();
        Assert.Contains("Permitir abrir esta url?", ui.Texts);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_command_of_many_lines_shows_every_line()
    {
        var c = new ChatConversation();
        c.Apply(Request("r1", "PowerShell", JsonSerializer.Serialize(new { command = "cd src\nRemove-Item build -Recurse" })));
        var ui = Open(c);

        Assert.Contains("Allow this PowerShell command?", ui.Texts);
        Assert.Contains("cd src\nRemove-Item build -Recurse", ui.Texts); // not just the first line
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_long_command_shows_its_end()
    {
        // the dangerous part may come last: nothing of what the user says yes to is cut
        var command = "echo " + new string('a', 6000) + " && rm -rf /tmp/x";
        Assert.True(command.Length > 6000);
        var c = new ChatConversation();
        c.Apply(Request("r1", "Bash", JsonSerializer.Serialize(new { command })));
        var ui = Open(c);

        Assert.Contains(command, ui.Texts);
        Assert.Contains(ui.Texts, t => t.EndsWith("rm -rf /tmp/x", System.StringComparison.Ordinal));
        ui.Window.Close();
    }

    // ---- answers that come too soon ----

    [AvaloniaFact]
    public void A_second_key_within_500_ms_does_not_answer_the_next_request()
    {
        var clock = new ManualClock();
        var c = new ChatConversation();
        c.Apply(Bash("r1", "git status"));
        c.Apply(Bash("r2", "git push --force"));
        var ui = Open(c, clock: clock);
        clock.Advance(1000);
        Assert.True(ui.Prompt.IsKeyboardFocusWithin);

        UiHelpers.Press(ui.Window, Key.D1);
        Assert.Equal("r1", Assert.Single(ui.Answers).Request.RequestId);

        clock.Advance(499); // a double tap
        UiHelpers.Press(ui.Window, Key.D1);
        UiHelpers.Press(ui.Window, Key.D2);
        UiHelpers.Press(ui.Window, Key.D3);
        Assert.Single(ui.Answers);
        Assert.False(ui.DenyField!.IsFocused);
        Assert.Contains("git push --force", ui.Texts); // still asking

        clock.Advance(1); // 500 ms after the change: the user has seen it
        UiHelpers.Press(ui.Window, Key.D1);
        Assert.Equal(2, ui.Answers.Count);
        Assert.Equal("r2", ui.Answers[1].Request.RequestId);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Leaving_read_only_restarts_the_pause_before_a_key_answers()
    {
        var clock = new ManualClock();
        var c = new ChatConversation();
        c.Apply(Bash("r1", "git status"));
        var ui = Open(c, clock: clock);
        clock.Advance(1000);

        ui.View.IsReadOnly = true;
        UiHelpers.Pump();
        clock.Advance(5000); // long hidden: the old time must not count
        ui.View.IsReadOnly = false;
        UiHelpers.Pump();
        ui.Prompt.FocusFirstOption();

        clock.Advance(499);
        UiHelpers.Press(ui.Window, Key.D1);
        Assert.Empty(ui.Answers); // a stray digit right after the prompt came back

        clock.Advance(1);
        UiHelpers.Press(ui.Window, Key.D1);
        Assert.Equal("r1", Assert.Single(ui.Answers).Request.RequestId);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_click_right_after_the_change_is_ignored()
    {
        var clock = new ManualClock();
        var c = new ChatConversation();
        var ui = Open(c, clock: clock);
        c.Apply(Bash("r1", "dotnet build"));
        c.Apply(Bash("r2", "dotnet publish"));
        UiHelpers.Pump();

        UiHelpers.Click(ui.Window, ui.Option("Yes")); // it just appeared under the pointer
        Assert.Empty(ui.Answers);

        clock.Advance(500);
        UiHelpers.Click(ui.Window, ui.Option("Yes"));
        Assert.Equal("r1", Assert.Single(ui.Answers).Request.RequestId);

        clock.Advance(100); // a fast second click lands on the next request
        UiHelpers.Click(ui.Window, ui.Option("Yes"));
        UiHelpers.Click(ui.Window, ui.Option(No));
        Assert.Single(ui.Answers);

        clock.Advance(400);
        UiHelpers.Click(ui.Window, ui.Option("Yes"));
        Assert.Equal("r2", ui.Answers[1].Request.RequestId);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_held_key_answers_only_once()
    {
        var clock = new ManualClock();
        var c = new ChatConversation();
        c.Apply(Bash("r1", "a", Suggestions));
        c.Apply(Bash("r2", "b", Suggestions));
        c.Apply(Bash("r3", "c"));
        var ui = Open(c, clock: clock);
        clock.Advance(1000);

        UiHelpers.KeyDown(ui.Window, Key.D2); // held: the keyboard repeats it
        Assert.Equal(PermissionDecision.AllowAlways, Assert.Single(ui.Answers).Decision);
        clock.Advance(1000);
        UiHelpers.KeyDown(ui.Window, Key.D2);
        UiHelpers.KeyDown(ui.Window, Key.D2);
        Assert.Single(ui.Answers);

        UiHelpers.KeyUp(ui.Window, Key.D2);
        UiHelpers.Press(ui.Window, Key.D2); // pressed again: an answer
        Assert.Equal("r2", ui.Answers[1].Request.RequestId);

        // Enter on an option repeats the same way
        clock.Advance(1000);
        Assert.True(ui.Option("Yes").IsFocused);
        UiHelpers.KeyDown(ui.Window, Key.Enter);
        Assert.Equal("r3", ui.Answers[2].Request.RequestId);
        c.Apply(Bash("r4", "d"));
        UiHelpers.Pump();
        clock.Advance(1000);
        UiHelpers.KeyDown(ui.Window, Key.Enter);
        Assert.Equal(3, ui.Answers.Count);
        UiHelpers.KeyUp(ui.Window, Key.Enter);
        UiHelpers.Press(ui.Window, Key.Enter);
        Assert.Equal("r4", ui.Answers[3].Request.RequestId);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_held_key_that_answered_the_last_request_types_nothing()
    {
        var clock = new ManualClock();
        var c = new ChatConversation();
        c.Apply(Bash("r1", "ls"));
        var ui = Open(c, clock: clock);
        var input = UiHelpers.Input(ui.View);
        var unhandled = UiHelpers.UnhandledKeys(ui.Window);
        clock.Advance(1000);

        UiHelpers.KeyDown(ui.Window, Key.D1);
        Assert.Single(ui.Answers);
        Assert.True(input.IsFocused); // none left: back to the reply box
        UiHelpers.KeyDown(ui.Window, Key.D1); // the repeat reaches the reply box
        Assert.DoesNotContain(Key.D1, unhandled); // taken, so it types no "1"

        UiHelpers.KeyUp(ui.Window, Key.D1); // released in the reply box, not in the prompt
        UiHelpers.Press(ui.Window, Key.D1);
        Assert.Contains(Key.D1, unhandled); // a new press in the reply box is text again

        input.Clear();
        c.Apply(Bash("r2", "ls -la"));
        UiHelpers.Pump();
        clock.Advance(1000);
        Assert.True(ui.Prompt.IsKeyboardFocusWithin);
        UiHelpers.Press(ui.Window, Key.D1); // the release above counted
        Assert.Equal("r2", ui.Answers[1].Request.RequestId);
        ui.Window.Close();
    }

    // ---- look ----

    [AvaloniaFact]
    public void The_option_with_the_keyboard_is_the_accent_and_the_others_are_neutral()
    {
        var c = new ChatConversation();
        c.Apply(Bash("r1", "ls", Suggestions));
        var ui = Open(c);
        var accentFill = Resource(ui.View, "ClaudeChat.AccentFill");
        var accentInk = Resource(ui.View, "ClaudeChat.AccentForeground");

        // like the extension: the selected option is filled with the accent, white and bold on it
        void Selected(string text, bool selected)
        {
            var option = ui.Option(text);
            var label = option.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == text);
            if (selected)
            {
                Assert.Same(accentFill, option.Background);
                Assert.Same(accentInk, label.Foreground);
                Assert.Equal(FontWeight.Bold, label.FontWeight);
            }
            else
            {
                Assert.NotSame(accentFill, option.Background);
                Assert.Same(Resource(ui.View, "ClaudeChat.Foreground"), label.Foreground);
            }
        }

        Assert.True(ui.Option("Yes").IsFocused);
        Selected("Yes", true);
        Selected(YesAlways, false);
        Selected(No, false);

        ui.Option(No).Focus(); // the keyboard moves: so does the accent
        UiHelpers.Pump();
        Selected("Yes", false);
        Selected(No, true);

        UiHelpers.Input(ui.View).Focus(); // with the keyboard elsewhere, option 1 is the one Enter would take
        UiHelpers.Pump();
        Selected("Yes", true);
        Selected(No, false);

        // the reply box's frame: a thin line, 8 px corners
        var frame = ui.Prompt.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("ClaudeChatPermission"));
        Assert.Equal(new Thickness(1), frame.BorderThickness);
        Assert.Equal(new CornerRadius(8), frame.CornerRadius);
        Assert.Same(Resource(ui.View, "ClaudeChat.InputBorder"), frame.BorderBrush);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_huge_request_never_pushes_the_reply_box_away()
    {
        var c = new ChatConversation();
        var command = string.Join("\n", Enumerable.Range(1, 2000).Select(i => "echo " + i));
        c.Apply(Request("r1", "Bash", JsonSerializer.Serialize(new { command })));
        var ui = Open(c);

        var input = UiHelpers.Input(ui.View);
        var bottom = input.TranslatePoint(new Point(0, input.Bounds.Height), ui.Window)!.Value;
        Assert.True(bottom.Y <= UiHelpers.Height, $"the reply box ends at {bottom.Y}");
        // the command scrolls inside its 200 px; the card around it (title, options, the field of "No" and its hint, always there
        // since the card of 2.1.310) takes what it takes whatever the command
        var detail = ui.Prompt.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "DetailScroller");
        Assert.Equal(200, detail.Bounds.Height, 0);
        Assert.True(ui.Prompt.Bounds.Height < UiHelpers.Height * 0.6, $"the prompt is {ui.Prompt.Bounds.Height} high");
        ui.Window.Close();
    }

    // ---- the card of the extension 2.1.310 ----

    /// <summary>The title of the card, its runs joined.</summary>
    private static string Title(Opened ui)
    {
        var title = ui.Prompt.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("ClaudeChatPermissionTitle"));
        return string.IsNullOrEmpty(title.Text) && title.Inlines is { Count: > 0 } inlines ? inlines.Text ?? "" : title.Text ?? "";
    }

    [AvaloniaFact]
    public void Bash_asks_allow_this_bash_command_and_shows_its_description()
    {
        var c = new ChatConversation();
        c.Apply(Request("r1", "Bash", "{\"command\":\"ls -la\",\"description\":\"List files\"}"));
        var ui = Open(c);

        Assert.Equal("Allow this bash command?", Title(ui));
        Assert.Contains("List files", ui.Texts);                                 // the input's description, under the title
        var box = ui.Prompt.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "SummaryBox");
        Assert.Equal(new[] { "ls -la" }, UiHelpers.Texts(box));                   // the command in its box
        var description = ui.Prompt.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "List files");
        Assert.True(description.TranslatePoint(default, ui.Prompt)!.Value.Y < box.TranslatePoint(default, ui.Prompt)!.Value.Y);
        Assert.Same(Resource(ui.View, "ClaudeChat.ForegroundMuted"), description.Foreground);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Edit_asks_make_this_edit_to_the_file_name()
    {
        var c = new ChatConversation { WorkingDirectory = "C:\\demo\\app" };
        c.Apply(Request("r1", "Edit", JsonSerializer.Serialize(new { file_path = "C:\\demo\\app\\src\\a.cs", old_string = "int a;", new_string = "int b;" })));
        var ui = Open(c);

        Assert.Equal("Make this edit to a.cs?", Title(ui));
        var title = ui.Prompt.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("ClaudeChatPermissionTitle"));
        var name = title.Inlines!.OfType<Avalonia.Controls.Documents.Run>().Single(r => r.Text == "a.cs");
        Assert.Same(Resource(ui.View, "ClaudeChat.MonoFontFamily"), name.FontFamily); // the file's name in the editor's font
        Assert.Single(ui.Prompt.GetVisualDescendants().OfType<DiffView>());      // and the diff stays in the card (the extension opens a tab)
        Assert.DoesNotContain("src\\a.cs", ui.Texts);                             // the path is not repeated in a box
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Each_tool_asks_its_own_question()
    {
        var c = new ChatConversation();
        c.Apply(Request("r1", "Write", "{\"file_path\":\"/home/dev/notes.md\",\"content\":\"x\"}"));
        c.Apply(Request("r2", "Read", "{\"file_path\":\"C:\\\\demo\\\\secret.txt\"}"));
        c.Apply(Request("r3", "Glob", "{\"pattern\":\"**/*.cs\",\"path\":\"C:\\\\demo\\\\src\"}"));
        c.Apply(Request("r4", "Glob", "{\"pattern\":\"**/*.cs\"}"));
        c.Apply(Request("r5", "Grep", "{\"pattern\":\"TODO\",\"path\":\"src\"}"));
        c.Apply(Request("r6", "Grep", "{\"pattern\":\"TODO\"}"));
        c.Apply(Request("r7", "PowerShell", "{\"command\":\"Get-ChildItem\"}"));
        c.Apply(Request("r8", "WebSearch", "{\"query\":\"avalonia headless\",\"allowed_domains\":[\"a.com\",\"b.com\"],\"blocked_domains\":[\"c.com\"]}"));
        var ui = Open(c);

        var titles = new List<string>();
        var texts = new List<List<string>>();
        for (var i = 1; i <= 8; i++)
        {
            titles.Add(Title(ui));
            texts.Add(ui.Texts);
            c.DismissPermission("r" + i);
            UiHelpers.Pump();
        }
        Assert.Equal(new[]
        {
            "Allow write to notes.md?", "Allow reading from secret.txt?", "Allow glob search in C:\\demo\\src?", "Allow this glob command",
            "Allow grep in src?", "Allow this grep command", "Allow this PowerShell command?", "Allow searching for this query?",
        }, titles);
        Assert.Contains("Get-ChildItem", texts[6]);
        Assert.Contains("avalonia headless", texts[7]);
        Assert.Contains("Allowed: a.com, b.com · Blocked: c.com", texts[7]);
        Assert.DoesNotContain("C:\\demo\\secret.txt", texts[1]);                   // the name is in the title: no box
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void An_mcp_tool_asks_do_you_want_to_proceed_and_folds_its_input_as_json()
    {
        var c = new ChatConversation();
        c.Apply(Request("r1", "mcp__github__create_issue", "{\"title\":\"Bug\",\"labels\":[\"ui\"]}", displayName: "create_issue"));
        var ui = Open(c);
        const string Json = "{\n  \"title\": \"Bug\",\n  \"labels\": [\n    \"ui\"\n  ]\n}";

        Assert.Equal("Do you want to proceed with mcp__github__create_issue?", Title(ui)); // the raw name
        Assert.Contains("Details", ui.Texts);
        Assert.DoesNotContain(Json, ui.Texts);                                   // folded

        UiHelpers.Click(ui.Window, UiHelpers.ButtonWithText(ui.Prompt, "Details"));
        Assert.Contains(Json, ui.Texts);                                         // the input, indented
        Assert.Empty(ui.Answers);                                                // unfolding answers nothing
        UiHelpers.Click(ui.Window, UiHelpers.ButtonWithText(ui.Prompt, "Details"));
        Assert.DoesNotContain(Json, ui.Texts);

        c.Apply(Request("r2", "mcp__x__ping", "{}"));
        c.DismissPermission("r1");
        UiHelpers.Pump();
        Assert.Equal("Do you want to proceed with mcp__x__ping?", Title(ui));
        Assert.DoesNotContain("Details", ui.Texts);                              // nothing to fold: no disclosure
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void The_skill_card_names_the_skill_its_description_and_arguments()
    {
        var c = new ChatConversation { Commands = new[] { new ChatCommand("review", "Review a PR", "") } };
        c.Apply(Request("r1", "Skill", "{\"skill\":\"review\",\"args\":\"12\"}"));
        c.Apply(Request("r2", "Skill", "{\"skill\":\"/unknown\"}"));
        var ui = Open(c);

        Assert.Equal("Use skill /review?", Title(ui));
        Assert.Contains("Claude may use instructions, code, or files from this skill.", ui.Texts);
        Assert.Contains("Review a PR", ui.Texts);
        Assert.Contains("Arguments: 12", ui.Texts);

        c.DismissPermission("r1");
        UiHelpers.Pump();
        Assert.Equal("Use skill /unknown?", Title(ui));                            // its "/" is not doubled
        Assert.Contains("Claude may use instructions, code, or files from this skill.", ui.Texts);
        Assert.DoesNotContain(ui.Texts, t => t.StartsWith("Arguments", System.StringComparison.Ordinal)); // none given
        Assert.DoesNotContain("Review a PR", ui.Texts);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void The_request_description_is_not_shown()
    {
        var c = new ChatConversation();
        c.Apply(Request("r1", "Read", "{\"file_path\":\"C:\\\\demo\\\\secret.txt\"}", description: "Run a thing"));
        var ui = Open(c);

        Assert.Equal("Allow reading from secret.txt?", Title(ui));               // what the card shows…
        Assert.DoesNotContain(ui.Texts, t => t.Contains("Run a thing"));         // …and not the request's own description
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void No_refuses_at_once_with_the_field_text()
    {
        var c = new ChatConversation();
        c.Apply(Bash("r1", "git push --force"));
        var ui = Open(c);

        ui.DenyField!.Focus();
        UiHelpers.Type(ui.Window, "use git");
        UiHelpers.Click(ui.Window, ui.Option(No));

        var answer = Assert.Single(ui.Answers);
        Assert.Equal(PermissionDecision.Deny, answer.Decision);
        Assert.Equal("use git", answer.Message);
        Assert.False(ui.Prompt.IsEffectivelyVisible);

        c.Apply(Bash("r2", "rm -rf build"));                                     // nothing written: a plain no
        UiHelpers.Pump();
        UiHelpers.Click(ui.Window, ui.Option(No));
        Assert.Equal(PermissionDecision.Deny, ui.Answers[1].Decision);
        Assert.Null(ui.Answers[1].Message);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void The_text_of_no_is_cut_at_4000_characters()
    {
        // the host's contract: the refusal carries 4,000 characters at most
        var c = new ChatConversation();
        c.Apply(Bash("r1", "git push --force"));
        var ui = Open(c);
        ui.DenyField!.Text = new string('x', 3990) + "0123456789ABCDEF";

        UiHelpers.Click(ui.Window, ui.Option(No));

        var message = Assert.Single(ui.Answers).Message!;
        Assert.Equal(4000, message.Length);
        Assert.EndsWith("x0123456789", message, System.StringComparison.Ordinal);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Esc_in_the_field_sends_no_with_the_text()
    {
        var c = new ChatConversation();
        c.Apply(Bash("r1", "git push --force"));
        c.Apply(Bash("r2", "git push"));
        c.Apply(Bash("r3", "git pull"));
        var ui = Open(c);

        ui.DenyField!.Focus();
        UiHelpers.Type(ui.Window, "  only the branch  ");
        UiHelpers.Press(ui.Window, Key.Escape);
        Assert.Equal(PermissionDecision.Deny, Assert.Single(ui.Answers).Decision);
        Assert.Equal("only the branch", ui.Answers[0].Message);

        ui.DenyField!.Focus();                                                   // Enter in the field does the same
        UiHelpers.Type(ui.Window, "pull first");
        UiHelpers.Press(ui.Window, Key.Enter);
        Assert.Equal("pull first", ui.Answers[1].Message);

        ui.Option("Yes").Focus();                                                // and Esc on an option is "No"
        UiHelpers.Press(ui.Window, Key.Escape);
        Assert.Equal(PermissionDecision.Deny, ui.Answers[2].Decision);
        Assert.Null(ui.Answers[2].Message);
        Assert.Equal(new[] { "r1", "r2", "r3" }, ui.Answers.Select(a => a.Request.RequestId));
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Arrows_move_between_the_options_and_the_field()
    {
        var c = new ChatConversation();
        c.Apply(Bash("r1", "npm test", Suggestions));
        var ui = Open(c);
        Assert.True(ui.Option("Yes").IsFocused);

        UiHelpers.Press(ui.Window, Key.Down);
        Assert.True(ui.Option(YesAlways).IsFocused);
        UiHelpers.Press(ui.Window, Key.Down);
        Assert.True(ui.Option(No).IsFocused);
        UiHelpers.Press(ui.Window, Key.Down);
        Assert.True(ui.DenyField!.IsFocused);
        UiHelpers.Press(ui.Window, Key.Down);                                    // round to the first
        Assert.True(ui.Option("Yes").IsFocused);
        UiHelpers.Press(ui.Window, Key.Up);
        Assert.True(ui.DenyField!.IsFocused);
        UiHelpers.Press(ui.Window, Key.Up);
        Assert.True(ui.Option(No).IsFocused);
        Assert.Empty(ui.Answers);

        UiHelpers.Press(ui.Window, Key.Enter);                                   // Enter takes the focused option
        Assert.Equal(PermissionDecision.Deny, Assert.Single(ui.Answers).Decision);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Options_stay_disabled_for_half_a_second()
    {
        var clock = new ManualClock();
        var c = new ChatConversation();
        var ui = Open(c, clock: clock);
        c.Apply(Bash("r1", "dotnet build"));
        UiHelpers.Pump();

        Assert.All(ui.Options, o => Assert.Equal(0.5, o.Opacity, 2));             // drawn disabled while arming
        Assert.True(ui.Prompt.IsTicking);
        clock.Advance(499);
        ui.Prompt.Tick();
        UiHelpers.Click(ui.Window, ui.Option("Yes"));
        Assert.Empty(ui.Answers);
        Assert.All(ui.Options, o => Assert.Equal(0.5, o.Opacity, 2));

        clock.Advance(1);
        ui.Prompt.Tick();
        Assert.All(ui.Options, o => Assert.Equal(1, o.Opacity, 2));               // armed
        Assert.False(ui.Prompt.IsTicking);
        UiHelpers.Click(ui.Window, ui.Option("Yes"));
        Assert.Equal(PermissionDecision.Allow, Assert.Single(ui.Answers).Decision);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Default_to_no_focuses_no_and_hides_the_numbers()
    {
        var c = new ChatConversation();
        c.Apply(Request("r1", "Bash", "{\"command\":\"rm -rf /\"}", Suggestions, defaultToNo: true));
        var ui = Open(c);

        Assert.True(ui.Option(No).IsFocused);
        Assert.Empty(ui.Numbers);
        foreach (var key in new[] { Key.D1, Key.D2, Key.D3, Key.D4 }) UiHelpers.Press(ui.Window, key);
        Assert.Empty(ui.Answers);                                                // the digits answer nothing
        Assert.False(ui.DenyField!.IsFocused);
        Assert.Contains("selected", ui.Option(No).Classes);                       // the accent on "No"
        Assert.DoesNotContain("selected", ui.Option("Yes").Classes);

        UiHelpers.Press(ui.Window, Key.Enter);
        Assert.Equal(PermissionDecision.Deny, Assert.Single(ui.Answers).Decision);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Suppress_always_allow_rule_hides_option_two()
    {
        var c = new ChatConversation();
        c.Apply(Request("r1", "Bash", "{\"command\":\"npm test\"}", Suggestions, suppressAlwaysAllowRule: true, agentId: "a1"));
        var ui = Open(c);

        Assert.False(c.CurrentPermission!.CanAllowAlways);
        Assert.True(c.CurrentPermission.SuppressAlwaysAllowRule);
        Assert.Equal("a1", c.CurrentPermission.AgentId);
        Assert.DoesNotContain(YesAlways, ui.Texts);
        Assert.Equal(new[] { "1", "2" }, ui.Numbers);

        UiHelpers.Press(ui.Window, Key.D2);                                      // "No" is 2
        Assert.Equal(PermissionDecision.Deny, Assert.Single(ui.Answers).Decision);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_request_waits_while_the_person_was_typing()
    {
        var now = new System.DateTimeOffset(2026, 10, 27, 10, 0, 0, System.TimeSpan.Zero);
        var c = new ChatConversation { Clock = () => now };
        var ui = Open(c);
        var input = UiHelpers.Input(ui.View);
        input.Focus();
        UiHelpers.Type(ui.Window, "half a thought");

        now = now.AddMilliseconds(500);
        c.Apply(Bash("r1", "git status"));
        UiHelpers.Pump();

        Assert.Contains("Waiting for permission…", UiHelpers.Texts(ui.View));    // above the reply box
        Assert.False(ui.Frame.IsEffectivelyVisible);                              // and no card yet
        Assert.True(ui.Prompt.IsTicking);
        Assert.True(input.IsFocused);

        now = now.AddMilliseconds(900);                                          // 1.4 s after the typing
        ui.Prompt.Tick();
        Assert.False(ui.Frame.IsEffectivelyVisible);
        now = now.AddMilliseconds(100);                                          // 1.5 s
        ui.Prompt.Tick();
        UiHelpers.Pump();
        Assert.True(ui.Frame.IsEffectivelyVisible);
        Assert.DoesNotContain("Waiting for permission…", UiHelpers.Texts(ui.View));
        Assert.Contains(RunCommand, ui.Texts);
        Assert.True(input.IsFocused);                                            // the keyboard stays with the typing
        Assert.Equal("half a thought", input.Text);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_request_does_not_wait_for_an_empty_reply_box()
    {
        var now = new System.DateTimeOffset(2026, 10, 27, 10, 0, 0, System.TimeSpan.Zero);
        var c = new ChatConversation { Clock = () => now };
        var ui = Open(c);
        var input = UiHelpers.Input(ui.View);
        input.Focus();
        UiHelpers.Type(ui.Window, "x");
        UiHelpers.Press(ui.Window, Key.Back);                                    // edited, but nothing is left in the box

        c.Apply(Bash("r1", "git status"));
        UiHelpers.Pump();
        Assert.True(ui.Frame.IsEffectivelyVisible);
        Assert.DoesNotContain("Waiting for permission…", UiHelpers.Texts(ui.View));
        ui.Window.Close();
    }

    // ---- folding the card to read the conversation behind it ----

    private static Button FoldButton(Control root) => root.GetVisualDescendants().OfType<Button>()
        .Single(b => b.Classes.Contains("ClaudeChatPermissionFold") && b.IsEffectivelyVisible);

    private static PermissionRequested BashWithNote(string id) =>
        Request(id, "Bash", "{\"command\":\"dotnet build\",\"description\":\"Builds the solution\"}", Suggestions);

    [AvaloniaFact]
    public void Collapse_leaves_only_the_title_and_focuses_the_button()
    {
        var c = new ChatConversation();
        c.Apply(BashWithNote("r1"));
        var ui = Open(c);
        UiHelpers.Pump();
        var button = FoldButton(ui.Prompt);
        Assert.Equal("Collapse", AutomationProperties.GetName(button));
        Assert.False(ui.Prompt.IsFolded);
        Assert.Contains("dotnet build", ui.Texts);
        Assert.NotNull(ui.DenyField);
        // 20 px, 4 px from the top and from the right of the card
        Assert.Equal(20, button.Bounds.Width, 1);
        var at = button.TranslatePoint(default, ui.Frame)!.Value;
        Assert.Equal(4, at.Y, 1);
        Assert.Equal(4, ui.Frame.Bounds.Width - (at.X + 20), 1);
        var open = ui.Frame.Bounds.Height;

        UiHelpers.Click(ui.Window, button);

        Assert.True(ui.Prompt.IsFolded);
        Assert.Contains(RunCommand, ui.Texts);                                     // the title stays...
        foreach (var gone in new[] { "dotnet build", "Builds the solution", "Esc to cancel", "Yes", No }) Assert.DoesNotContain(gone, ui.Texts);
        Assert.Empty(ui.Options);                                                   // ...and nothing else: no options,
        Assert.Null(ui.DenyField);                                                  // and the reply box stays hidden
        Assert.True(ui.Frame.Bounds.Height < open / 2);
        Assert.Equal("Expand", AutomationProperties.GetName(button));
        Assert.Same(button, UiHelpers.Focused(ui.Window));                          // the keyboard is on the button
        var title = ui.Prompt.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("ClaudeChatPermissionTitle"));
        Assert.Equal(TextWrapping.NoWrap, title.TextWrapping);                      // one line, with an ellipsis
        Assert.Equal(TextTrimming.CharacterEllipsis, title.TextTrimming);
        var folded = button.TranslatePoint(default, ui.Frame)!.Value;
        Assert.Equal(8, folded.Y, 1);                                               // 8 px from the top, folded

        UiHelpers.Click(ui.Window, button);
        Assert.False(ui.Prompt.IsFolded);
        Assert.Contains("dotnet build", ui.Texts);
        Assert.NotNull(ui.DenyField);
        Assert.Equal("Collapse", AutomationProperties.GetName(button));
        Assert.Empty(ui.Answers);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Folded_esc_does_not_refuse()
    {
        var c = new ChatConversation();
        c.Apply(Bash("r1", "dotnet build", Suggestions));
        var ui = Open(c);
        UiHelpers.Pump();
        UiHelpers.Click(ui.Window, FoldButton(ui.Prompt));
        var unhandled = UiHelpers.UnhandledKeys(ui.Window);

        UiHelpers.Press(ui.Window, Key.Escape);
        UiHelpers.Press(ui.Window, Key.D1);                                         // nor does a digit answer
        UiHelpers.Press(ui.Window, Key.Down);

        Assert.Empty(ui.Answers);
        Assert.NotNull(ui.Conversation.CurrentPermission);
        Assert.Contains(Key.Escape, unhandled);                                     // the keys went on to the page
        Assert.Contains(Key.D1, unhandled);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Folded_keys_scroll_the_conversation_behind_the_card()
    {
        var c = new ChatConversation();
        for (var i = 0; i < 100; i++) c.Items.Add(new UserMessageItem("message " + i));
        c.Apply(Bash("r1", "dotnet build", Suggestions));
        var ui = Open(c);
        UiHelpers.Pump();
        var scroller = UiHelpers.Scroller(ui.View);
        UiHelpers.Click(ui.Window, FoldButton(ui.Prompt));
        scroller.ScrollToEnd();
        UiHelpers.Pump();
        var end = scroller.Offset.Y;
        Assert.True(end > 0);

        UiHelpers.Press(ui.Window, Key.PageUp);
        Assert.True(scroller.Offset.Y < end);
        UiHelpers.Press(ui.Window, Key.Home);
        Assert.Equal(0, scroller.Offset.Y, 1);
        Assert.Empty(ui.Answers);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_new_request_appears_unfolded()
    {
        var c = new ChatConversation();
        c.Apply(Bash("r1", "git status", Suggestions));
        c.Apply(Bash("r2", "git log", Suggestions));
        var ui = Open(c);
        UiHelpers.Pump();
        UiHelpers.Click(ui.Window, FoldButton(ui.Prompt));
        Assert.True(ui.Prompt.IsFolded);

        c.AnswerPermission(c.CurrentPermission!, PermissionDecision.Deny, null);     // the next one comes
        UiHelpers.Pump();

        Assert.Equal("r2", ui.Prompt.Request!.RequestId);
        Assert.False(ui.Prompt.IsFolded);
        Assert.Contains("git log", ui.Texts);
        Assert.Equal(3, ui.Options.Count);
        Assert.Equal("Collapse", AutomationProperties.GetName(FoldButton(ui.Prompt)));
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Expanding_arms_the_options_again_for_half_a_second()
    {
        var clock = new ManualClock();
        var c = new ChatConversation();
        var ui = Open(c, clock: clock);
        c.Apply(Bash("r1", "dotnet build", Suggestions));
        UiHelpers.Pump();
        clock.Advance(1000);
        ui.Prompt.Tick();
        Assert.All(ui.Options, o => Assert.Equal(1, o.Opacity, 2));                 // armed

        var button = FoldButton(ui.Prompt);
        UiHelpers.Click(ui.Window, button);                                         // fold
        clock.Advance(5000);
        UiHelpers.Click(ui.Window, button);                                         // unfold: the options count as new
        Assert.True(ui.Prompt.IsTicking);
        Assert.All(ui.Options, o => Assert.Equal(0.5, o.Opacity, 2));
        clock.Advance(499);
        ui.Prompt.Tick();
        UiHelpers.Click(ui.Window, ui.Option("Yes"));
        Assert.Empty(ui.Answers);

        clock.Advance(1);
        ui.Prompt.Tick();
        Assert.All(ui.Options, o => Assert.Equal(1, o.Opacity, 2));
        UiHelpers.Click(ui.Window, ui.Option("Yes"));
        Assert.Equal(PermissionDecision.Allow, Assert.Single(ui.Answers).Decision);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void The_touch_layout_has_no_fold_button()
    {
        var c = new ChatConversation();
        c.Apply(Bash("r1", "dotnet build", Suggestions));
        var view = UiHelpers.TouchView(c);
        var window = UiHelpers.ShowPhone(view);
        UiHelpers.Pump();
        var prompt = UiHelpers.Prompt(view);

        Assert.True(prompt.IsEffectivelyVisible);
        Assert.DoesNotContain(prompt.GetVisualDescendants().OfType<Button>(),
            b => b.Classes.Contains("ClaudeChatPermissionFold") && b.IsEffectivelyVisible);
        Assert.DoesNotContain("Recolher", UiHelpers.ButtonNames(prompt));
        Assert.DoesNotContain("Collapse", UiHelpers.ButtonNames(prompt));
        Assert.False(prompt.IsFolded);
        window.Close();
    }
}
