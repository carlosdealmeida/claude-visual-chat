using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
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

    private const string RunCommand = "Claude wants to run this command:";
    private const string YesAlways = "Yes, and don't ask again this session";
    private const string No = "No, and tell Claude what to do differently";

    private static JsonElement J(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static PermissionRequested Request(
        string id, string tool, string input, string? suggestions = null, string? displayName = null) =>
        new PermissionRequested(id, tool, displayName ?? tool, null, "tool-" + id, J(input),
            suggestions == null ? (JsonElement?)null : J(suggestions));

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

        /// <summary>The field of option 3, when it is open.</summary>
        public TextBox? DenyField =>
            Prompt.GetVisualDescendants().OfType<TextBox>().SingleOrDefault(t => t.IsEffectivelyVisible);
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
        foreach (var expected in new[] { "1", "Yes", "3", No }) Assert.Contains(expected, ui.Texts);

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
        Assert.DoesNotContain("2", ui.Texts);
        Assert.Contains("3", ui.Texts); // the others keep their numbers
        ui.Option("Yes").Focus();
        UiHelpers.Press(ui.Window, Key.D2);
        Assert.Empty(ui.Answers); // no option 2, no answer
        Assert.True(ui.Prompt.IsEffectivelyVisible);

        UiHelpers.Click(ui.Window, ui.Option("Yes"));
        c.Apply(Request("r2", "Write", "{\"file_path\":\"a.txt\",\"content\":\"one\"}", Suggestions));
        UiHelpers.Pump();
        Assert.Contains(YesAlways, ui.Texts);
        Assert.Contains("2", ui.Texts);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Option_three_asks_what_to_do_and_enter_denies_with_it()
    {
        var c = new ChatConversation();
        c.Apply(Bash("r1", "git diff --stat > out.txt"));
        var ui = Open(c);
        Assert.Null(ui.DenyField);

        UiHelpers.Click(ui.Window, ui.Option(No));

        var field = ui.DenyField;
        Assert.NotNull(field);
        Assert.True(field!.IsFocused);
        Assert.Equal("Tell Claude what to do instead", field.Watermark);
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
        Assert.Contains("Claude wants to edit this file:", ui.Texts);
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
        Assert.Single(ui.Answers); // 3 only opens the field
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
        Assert.Null(ui.DenyField);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Digits_typed_in_the_field_are_text_and_esc_closes_it()
    {
        var c = new ChatConversation();
        c.Apply(Bash("r1", "git push --force", Suggestions)); // all three options exist
        var ui = Open(c);
        var unhandled = UiHelpers.UnhandledKeys(ui.Window);
        UiHelpers.Click(ui.Window, ui.Option(No));

        foreach (var (key, text) in new[] { (Key.D1, "1"), (Key.D2, "2"), (Key.D3, "3") })
        {
            UiHelpers.Press(ui.Window, key);
            UiHelpers.Type(ui.Window, text);
        }
        UiHelpers.Type(ui.Window, " commits only");
        Assert.Empty(ui.Answers);
        Assert.Equal("123 commits only", ui.DenyField!.Text);

        UiHelpers.Press(ui.Window, Key.Escape);

        Assert.Null(ui.DenyField);
        Assert.Empty(ui.Answers);
        Assert.True(ui.Prompt.IsEffectivelyVisible);
        Assert.True(ui.Option(No).IsFocused);
        Assert.DoesNotContain(Key.Escape, unhandled); // the prompt used it

        UiHelpers.Press(ui.Window, Key.Escape);
        Assert.Contains(Key.Escape, unhandled); // nothing open: the app gets it
        Assert.Empty(ui.Answers);

        UiHelpers.Click(ui.Window, ui.Option(No)); // the field opens empty again
        Assert.Equal("", ui.DenyField!.Text ?? "");
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
        UiHelpers.Click(ui.Window, ui.Option(No)); // even with the field open
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
        Assert.Contains("O Claude quer editar este arquivo:", texts);
        Assert.Contains("notes.md", texts);
        Assert.Contains("+2 linhas", texts);
        // the editor's diff, in one column at this width: no "-" or "+" in the text, the colors tell
        Assert.Contains("e", texts);
        Assert.Contains("line three", texts);
        Assert.Contains("line 3 (edited)", texts);
        foreach (var expected in new[] { "Sim", "Sim, e não perguntar de novo nesta conversa", "Não, e dizer o que fazer" })
            Assert.Contains(expected, texts);
        Assert.False(ui.Prompt.GetVisualDescendants().OfType<DiffView>().Single().IsSideBySide);
        var removed = ui.Prompt.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatDiffLine") && b.Classes.Contains("removed"));
        Assert.Same(Resource(ui.View, "ClaudeChat.DiffRemoved"), removed.Background);

        ui.View.Strings = ChatStrings.English; // the texts follow the language
        UiHelpers.Pump();
        Assert.Contains("Claude wants to edit this file:", ui.Texts);
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

    [AvaloniaFact]
    public void Option_two_says_this_session_only_when_every_rule_stays_in_it()
    {
        const string Saved = "[{\"type\":\"addRules\",\"rules\":[{\"toolName\":\"Bash\",\"ruleContent\":\"npm test\"}],\"behavior\":\"allow\",\"destination\":\"localSettings\"}]";
        const string Mixed = "[{\"type\":\"setMode\",\"mode\":\"acceptEdits\",\"destination\":\"session\"},{\"type\":\"addRules\",\"rules\":[],\"behavior\":\"allow\",\"destination\":\"userSettings\"}]";
        const string Unsaid = "[{\"type\":\"setMode\",\"mode\":\"acceptEdits\"}]";
        var c = new ChatConversation();
        c.Apply(Bash("r1", "npm test", Suggestions));
        c.Apply(Bash("r2", "npm test", Saved));
        c.Apply(Bash("r3", "npm test", Mixed));
        c.Apply(Bash("r4", "npm test", Unsaid));
        var ui = Open(c);

        // all in the session: the conversation's label, in both languages
        Assert.Contains(YesAlways, ui.Texts);
        ui.View.Strings = ChatStrings.PortugueseBrazil;
        UiHelpers.Pump();
        Assert.Contains("Sim, e não perguntar de novo nesta conversa", ui.Texts);

        // a rule kept in the settings outlives the conversation: the label says nothing about how long
        c.DismissPermission("r1");
        UiHelpers.Pump();
        Assert.Contains("Sim, e não perguntar de novo", ui.Texts);
        Assert.DoesNotContain("Sim, e não perguntar de novo nesta conversa", ui.Texts);
        ui.View.Strings = ChatStrings.English;
        UiHelpers.Pump();
        Assert.Contains("Yes, and don't ask again", ui.Texts);
        Assert.DoesNotContain(YesAlways, ui.Texts);

        // one rule out of the session is enough, and so is a rule that does not say where it goes
        foreach (var id in new[] { "r2", "r3" })
        {
            c.DismissPermission(id);
            UiHelpers.Pump();
            Assert.Contains("Yes, and don't ask again", ui.Texts);
            Assert.DoesNotContain(YesAlways, ui.Texts);
        }

        // answering still sends the suggestions back as they came
        UiHelpers.Click(ui.Window, ui.Option("Yes, and don't ask again"));
        var answer = Assert.Single(ui.Answers);
        Assert.Equal(PermissionDecision.AllowAlways, answer.Decision);
        Assert.Equal("r4", answer.Request.RequestId);
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

        Assert.Contains("Claude wants to edit this file:", ui.Texts);
        Assert.Contains("todo.md", ui.Texts);
        Assert.Contains("- Item 1", ui.Texts); // added lines, in the added color
        Assert.Contains("- Item 2", ui.Texts);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Other_tools_use_their_display_name()
    {
        var c = new ChatConversation();
        c.Apply(Request("r1", "WebFetch", "{\"url\":\"https://example.com/docs\"}", displayName: "Fetch"));
        var ui = Open(c);

        Assert.Contains("Claude wants to use Fetch:", ui.Texts);
        Assert.Contains("https://example.com/docs", ui.Texts);

        ui.View.Strings = ChatStrings.PortugueseBrazil;
        UiHelpers.Pump();
        Assert.Contains("O Claude quer usar Fetch:", ui.Texts);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_command_of_many_lines_shows_every_line()
    {
        var c = new ChatConversation();
        c.Apply(Request("r1", "PowerShell", JsonSerializer.Serialize(new { command = "cd src\nRemove-Item build -Recurse" })));
        var ui = Open(c);

        Assert.Contains(RunCommand, ui.Texts);
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
    public void A_second_key_within_300_ms_does_not_answer_the_next_request()
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

        clock.Advance(299); // a double tap
        UiHelpers.Press(ui.Window, Key.D1);
        UiHelpers.Press(ui.Window, Key.D3);
        Assert.Single(ui.Answers);
        Assert.Null(ui.DenyField);
        Assert.Contains("git push --force", ui.Texts); // still asking

        clock.Advance(1); // 300 ms after the change: the user has seen it
        UiHelpers.Press(ui.Window, Key.D1);
        Assert.Equal(2, ui.Answers.Count);
        Assert.Equal("r2", ui.Answers[1].Request.RequestId);
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

        clock.Advance(300);
        UiHelpers.Click(ui.Window, ui.Option("Yes"));
        Assert.Equal("r1", Assert.Single(ui.Answers).Request.RequestId);

        clock.Advance(100); // a fast second click lands on the next request
        UiHelpers.Click(ui.Window, ui.Option("Yes"));
        UiHelpers.Click(ui.Window, ui.Option(No));
        Assert.Single(ui.Answers);
        Assert.Null(ui.DenyField);

        clock.Advance(200);
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
        Assert.True(ui.Prompt.Bounds.Height < UiHelpers.Height / 2, $"the prompt is {ui.Prompt.Bounds.Height} high");
        ui.Window.Close();
    }
}
