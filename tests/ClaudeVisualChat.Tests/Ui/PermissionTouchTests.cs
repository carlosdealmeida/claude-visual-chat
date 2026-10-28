using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using ClaudeVisualChat.Protocol;
using Xunit;

namespace ClaudeVisualChat.Tests.Ui;

/// <summary>
/// The permission request of the touch layout (<see cref="ChatLayout.Touch"/>): a short question for a title, the answers across
/// the block at 48 DIPs without their numbers, a send button under the field of "No", and the sheets giving way to a request.
/// </summary>
public class PermissionTouchTests
{
    private const string Suggestions = "[{\"type\":\"setMode\",\"mode\":\"acceptEdits\",\"destination\":\"session\"}]";

    // the answers, in the language of the views these tests open (Portuguese, like the touch view of UiHelpers)
    private const string Yes = "Sim";
    private const string YesAlways = "Sim, e não perguntar de novo nesta conversa";
    private const string No = "Não, e dizer o que fazer";
    private const string Send = "Enviar";

    private static JsonElement J(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static PermissionRequested Request(
        string id, string tool, string input, string? suggestions = null, string? displayName = null) =>
        new PermissionRequested(id, tool, displayName ?? tool, null, "tool-" + id, J(input),
            suggestions == null ? (JsonElement?)null : J(suggestions));

    /// <summary>An edit of a file, with the suggestions that make the second answer exist (they stay in the session).</summary>
    private static PermissionRequested Edit(string id = "r1", string? suggestions = Suggestions) =>
        Request(id, "Edit",
            "{\"file_path\":\"C:/repo/src/OrderService.cs\",\"old_string\":\".OrderBy(o => o.Date)\",\"new_string\":\".OrderByDescending(o => o.Date)\"}",
            suggestions);

    private static PermissionRequested Bash(string id, string command, string? suggestions = null) =>
        Request(id, "Bash", JsonSerializer.Serialize(new { command }), suggestions);

    /// <summary>A clock that moves only when the test says.</summary>
    private sealed class ManualClock : IPromptClock
    {
        public TimeSpan Now { get; private set; } = TimeSpan.FromHours(1);

        public void Advance(int milliseconds) => Now += TimeSpan.FromMilliseconds(milliseconds);
    }

    /// <summary>A clock where a second goes by between any two readings, so no answer ever comes too soon: for the tests that are not about the pause.</summary>
    private sealed class SlowClock : IPromptClock
    {
        private TimeSpan _now = TimeSpan.FromHours(1);

        public TimeSpan Now => _now += TimeSpan.FromSeconds(1);
    }

    private sealed class Opened
    {
        public Opened(ChatConversation conversation, IPromptClock? clock = null, ChatLayout layout = ChatLayout.Touch,
            double height = UiHelpers.PhoneHeight)
        {
            Conversation = conversation;
            conversation.PermissionAnswered += (_, e) => Answers.Add(e);
            conversation.CommandInvoked += (_, e) => Commands.Add(e.Name);
            View = new ClaudeChatView { Conversation = conversation, Layout = layout, Strings = ChatStrings.PortugueseBrazil };
            View.Prompt.Clock = clock ?? new SlowClock();
            Window = UiHelpers.Show(View, UiHelpers.PhoneWidth, height);
            Prompt = UiHelpers.Prompt(View);
        }

        public ChatConversation Conversation { get; }
        public ClaudeChatView View { get; }
        public Window Window { get; }
        public PermissionPrompt Prompt { get; }
        public List<PermissionAnsweredEventArgs> Answers { get; } = new List<PermissionAnsweredEventArgs>();

        /// <summary>The commands the host heard of (<see cref="ChatConversation.InvokeCommand"/>).</summary>
        public List<string> Commands { get; } = new List<string>();

        public List<string> Texts => UiHelpers.Texts(Prompt);

        public string Title => Prompt.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Classes.Contains("ClaudeChatPermissionTitle")).Text ?? "";

        public Button Option(string text) => UiHelpers.ButtonWithText(Prompt, text);

        /// <summary>The field of "No", when it is open.</summary>
        public TextBox? DenyField =>
            Prompt.GetVisualDescendants().OfType<TextBox>().SingleOrDefault(t => t.IsEffectivelyVisible);

        /// <summary>The frame of the request.</summary>
        public Border Frame => Prompt.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("ClaudeChatPermission"));

        /// <summary>The width inside the frame, which an answer takes whole.</summary>
        public double Inside => Frame.Bounds.Width - Frame.Padding.Left - Frame.Padding.Right
                                - Frame.BorderThickness.Left - Frame.BorderThickness.Right;

        /// <summary>The area of the command or the diff, which scrolls when it is long.</summary>
        public ScrollViewer Detail => Prompt.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "DetailScroller");

        public ModelSheet ModelSheet => View.GetVisualDescendants().OfType<ModelSheet>().Single();
        public AddSheet AddSheet => View.GetVisualDescendants().OfType<AddSheet>().Single();
        public UsageSheet UsageSheet => View.GetVisualDescendants().OfType<UsageSheet>().Single();

        /// <summary>The model pill of the touch bar.</summary>
        public ModelChip Pill => UiHelpers.Composer(View).GetVisualDescendants().OfType<ModelChip>().Single(c => c.Name == "TouchModelChip");

        /// <summary>The + of the touch bar.</summary>
        public Button Plus => UiHelpers.Composer(View).GetVisualDescendants().OfType<Button>().Single(b => b.Name == "TouchAddButton");

        /// <summary>The ring of the touch bar (the desktop bar's, hidden, is in the tree too).</summary>
        public ContextRing Ring => UiHelpers.Composer(View).GetVisualDescendants().OfType<ContextRing>().Single(r => r.Name == "TouchRing");

        /// <summary>The numbers in front of the answers that the user can see: 1, 2 and 3 on the desktop, none in the touch layout.</summary>
        public List<string> Numbers => Prompt.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Classes.Contains("ClaudeChatOptionNumber") && t.IsEffectivelyVisible).Select(t => t.Text ?? "").ToList();

        public void Tap(string option) => UiHelpers.Click(Window, Option(option));

        /// <summary>What lies outside the view's bounds, by how much (0 when the control is all inside it): below, then above.</summary>
        public (double Below, double Above) Outside(Control control)
        {
            var top = control.TranslatePoint(default, View)!.Value.Y;
            var bottom = control.TranslatePoint(new Point(0, control.Bounds.Height), View)!.Value.Y;
            return (Math.Max(0, bottom - View.Bounds.Height), Math.Max(0, -top));
        }
    }

    private static object? Resource(Control view, string key) => view.TryFindResource(key, out var value) ? value : null;

    /// <summary>The title of the desktop's card, whose file name is a run of its own (in the editor's font).</summary>
    private static string DesktopTitle(Opened ui)
    {
        var title = ui.Prompt.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("ClaudeChatPermissionTitle"));
        return string.IsNullOrEmpty(title.Text) && title.Inlines is { Count: > 0 } inlines ? inlines.Text ?? "" : title.Text ?? "";
    }

    private static Color ColorOf(Control view, string key) => Assert.IsAssignableFrom<ISolidColorBrush>(Resource(view, key)).Color;

    /// <summary>
    /// Every button the user can see in the request is a target of 44 DIPs at least (the arrows of a scroll bar are not buttons to
    /// touch: a finger scrolls by dragging the content).
    /// </summary>
    private static void AssertTargetsOf44(Opened ui) =>
        Assert.All(ui.Prompt.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b is not RepeatButton),
            b => Assert.True(b.Bounds.Width >= 44 && b.Bounds.Height >= 44, UiHelpers.Texts(b).FirstOrDefault() + " is " + b.Bounds.Size));

    // ---- the brief ----

    [AvaloniaFact]
    public void Touch_edit_request_asks_with_the_file_name()
    {
        TestLog.Instance.Clear();
        var c = new ChatConversation();
        c.Apply(Edit());
        var ui = new Opened(c);

        Assert.True(ui.Prompt.IsEffectivelyVisible);
        Assert.Equal("Editar OrderService.cs?", ui.Title);
        Assert.DoesNotContain("O Claude quer editar este arquivo:", ui.Texts);        // the question is the title: the file's name is in it
        foreach (var text in new[] { Yes, YesAlways, No }) Assert.Contains(text, ui.Texts);

        // the answers have no numbers, and each one takes the width of the block, at a finger's height
        Assert.Empty(ui.Numbers);
        foreach (var text in new[] { Yes, YesAlways, No })
        {
            var option = ui.Option(text);
            Assert.True(option.Bounds.Height >= 48, text + " is " + option.Bounds.Height + " high");
            Assert.Equal(ui.Inside, option.Bounds.Width, 1);
        }

        // what it will do, whole: the diff of the edit
        var diff = Assert.Single(ui.Prompt.GetVisualDescendants().OfType<DiffView>());
        Assert.True(diff.IsEffectivelyVisible);
        Assert.Contains(".OrderBy(o => o.Date)", ui.Texts);
        Assert.Contains(".OrderByDescending(o => o.Date)", ui.Texts);
        AssertTargetsOf44(ui);
        Assert.Empty(TestLog.Instance.Entries);                                         // no binding or property warnings
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Touch_command_request_shows_the_command()
    {
        var c = new ChatConversation();
        c.Apply(Bash("r1", "dotnet test"));
        var ui = new Opened(c);

        Assert.Equal("Rodar este comando?", ui.Title);
        Assert.DoesNotContain("O Claude quer rodar este comando:", ui.Texts);
        var box = ui.Prompt.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "SummaryBox");
        Assert.True(box.IsEffectivelyVisible);
        Assert.Equal(new[] { "dotnet test" }, UiHelpers.Texts(box));                     // the command is in the box, not in the title
        Assert.Contains(Yes, ui.Texts);
        Assert.DoesNotContain(YesAlways, ui.Texts);                                      // no suggestions, no second answer
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Touch_options_answer_after_the_pause()
    {
        var clock = new ManualClock();
        var c = new ChatConversation();
        c.Apply(Edit());
        var ui = new Opened(c, clock);

        // it just came under the finger: no answer reaches a request the user has not seen
        ui.Tap(Yes);
        ui.Tap(YesAlways);
        ui.Tap(No);
        Assert.Empty(ui.Answers);
        Assert.Null(ui.DenyField);
        Assert.True(ui.Prompt.IsEffectivelyVisible);

        clock.Advance(299);
        ui.Tap(Yes);
        Assert.Empty(ui.Answers);

        clock.Advance(1);                                                                // 300 ms after it came on screen
        ui.Tap(Yes);
        var answer = Assert.Single(ui.Answers);
        Assert.Equal(PermissionDecision.Allow, answer.Decision);
        Assert.Equal("r1", answer.Request.RequestId);
        Assert.False(ui.Prompt.IsEffectivelyVisible);

        // the next request starts the pause over
        c.Apply(Edit("r2"));
        UiHelpers.Pump();
        ui.Tap(YesAlways);
        Assert.Single(ui.Answers);
        clock.Advance(300);
        ui.Tap(YesAlways);
        Assert.Equal(PermissionDecision.AllowAlways, ui.Answers[1].Decision);
        Assert.Equal("r2", ui.Answers[1].Request.RequestId);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Touch_no_opens_the_field_with_a_send_button()
    {
        var clock = new ManualClock();
        var c = new ChatConversation();
        c.Apply(Edit());
        var ui = new Opened(c, clock);
        clock.Advance(300);
        Assert.Null(ui.DenyField);
        Assert.DoesNotContain(Send, ui.Texts);                                           // no field, no button

        ui.Tap(No);
        var field = ui.DenyField;
        Assert.NotNull(field);
        Assert.True(field!.IsFocused);
        var send = ui.Option(Send);
        Assert.True(send.Bounds.Height >= 48, "the button is " + send.Bounds.Height + " high");
        Assert.Equal(ui.Inside, send.Bounds.Width, 1);                                   // across the block, like the answers
        Assert.True(send.TranslatePoint(default, ui.Prompt)!.Value.Y
                    >= field.TranslatePoint(new Point(0, field.Bounds.Height), ui.Prompt)!.Value.Y - 0.5, "under the field");
        Assert.True(field.Bounds.Height >= 44, "the field is " + field.Bounds.Height + " high");
        AssertTargetsOf44(ui);

        UiHelpers.Type(ui.Window, "use descending");
        Assert.Empty(ui.Answers);
        UiHelpers.Click(ui.Window, send);

        var answer = Assert.Single(ui.Answers);
        Assert.Equal(PermissionDecision.Deny, answer.Decision);
        Assert.Equal("use descending", answer.Message);
        Assert.False(ui.Prompt.IsEffectivelyVisible);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Touch_request_closes_the_sheets_and_waits_for_the_pause()
    {
        var clock = new ManualClock();
        var c = TouchSamples.Opus();
        var ui = new Opened(c, clock);

        // the model sheet is open: the request comes first, the sheet would cover it
        UiHelpers.Click(ui.Window, ui.Pill);
        Assert.True(ui.ModelSheet.IsOpen);
        c.Apply(Edit());
        UiHelpers.Pump();
        Assert.False(ui.ModelSheet.IsOpen);
        Assert.True(ui.Prompt.IsEffectivelyVisible);
        Assert.Equal("r1", ui.Prompt.Request!.RequestId);
        ui.Tap(Yes);                                                                     // the finger that was on the sheet, at the same instant
        Assert.Empty(ui.Answers);
        clock.Advance(300);
        ui.Tap(Yes);
        Assert.Equal("r1", Assert.Single(ui.Answers).Request.RequestId);

        // the same for the "+" sheet
        UiHelpers.Click(ui.Window, ui.Plus);
        Assert.True(ui.AddSheet.IsOpen);
        c.Apply(Edit("r2"));
        UiHelpers.Pump();
        Assert.False(ui.AddSheet.IsOpen);
        Assert.Equal("r2", ui.Prompt.Request!.RequestId);
        ui.Tap(Yes);
        Assert.Single(ui.Answers);
        clock.Advance(300);
        ui.Tap(Yes);
        Assert.Equal("r2", ui.Answers[1].Request.RequestId);

        // the usage sheet lives from the panel: the request ends the panel too, and answering it brings nothing back
        c.InvokeCommand("context", "");
        UiHelpers.Pump();
        Assert.True(ui.UsageSheet.IsOpen);
        Assert.Equal(ChatPanel.Context, c.ShownPanel);
        c.Apply(Edit("r3"));
        UiHelpers.Pump();
        Assert.Equal(ChatPanel.None, c.ShownPanel);
        Assert.False(ui.UsageSheet.IsOpen);
        Assert.True(ui.Prompt.IsEffectivelyVisible);
        Assert.Equal("r3", ui.Prompt.Request!.RequestId);
        ui.Tap(Yes);
        Assert.Equal(2, ui.Answers.Count);
        clock.Advance(300);
        ui.Tap(Yes);
        Assert.Equal("r3", ui.Answers[2].Request.RequestId);
        UiHelpers.Pump();
        Assert.False(ui.UsageSheet.IsOpen);
        ui.Window.Close();
    }

    // ---- a panel asked for while a request waits ----

    [AvaloniaFact]
    public void Touch_a_panel_asked_for_while_a_request_waits_is_closed_at_once_and_the_ring_does_not_ask()
    {
        var clock = new ManualClock();
        var c = TouchSamples.Opus();
        c.Apply(new ContextUsageReported(TouchSamples.Context));
        var ui = new Opened(c, clock);
        c.Apply(Edit());
        UiHelpers.Pump();
        Assert.NotNull(c.CurrentPermission);
        Assert.True(ui.Ring.IsEffectivelyVisible);

        // the ring is still on screen under the request, and a tap on it asks for nothing
        UiHelpers.Click(ui.Window, ui.Ring);
        Assert.Empty(ui.Commands);
        Assert.Equal(ChatPanel.None, c.ShownPanel);
        Assert.False(ui.UsageSheet.IsOpen);

        // a typed /context, or the host, asks anyway: the panel is closed on the spot (the host still hears the command)
        c.Submit("/context");
        Assert.Equal(ChatPanel.None, c.ShownPanel);
        c.InvokeCommand("usage", "");
        Assert.Equal(ChatPanel.None, c.ShownPanel);
        Assert.Equal(new[] { "context", "usage" }, ui.Commands);
        UiHelpers.Pump();
        Assert.False(ui.UsageSheet.IsOpen);
        Assert.True(ui.Prompt.IsEffectivelyVisible);

        // answering the request: the sheet does not come up by itself
        clock.Advance(300);
        ui.Tap(Yes);
        Assert.Single(ui.Answers);
        UiHelpers.Pump();
        Assert.Equal(ChatPanel.None, c.ShownPanel);
        Assert.False(ui.UsageSheet.IsOpen);

        // with nothing waiting, the ring asks, as it always did
        UiHelpers.Click(ui.Window, ui.Ring);
        Assert.Equal(new[] { "context", "usage", "context" }, ui.Commands);
        Assert.True(ui.UsageSheet.IsOpen);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Touch_a_panel_asked_for_while_the_view_is_out_of_the_window_is_closed_when_it_is_back_with_a_request_waiting()
    {
        var c = TouchSamples.Opus();
        var view = UiHelpers.TouchView(c);
        var host = new ContentControl { Content = view };                                // a shell that swaps its page
        var window = UiHelpers.ShowPhone(host);
        var sheet = view.GetVisualDescendants().OfType<UsageSheet>().Single();

        host.Content = new TextBlock { Text = "Tokens" };
        UiHelpers.Pump();
        c.Apply(Bash("r1", "ls"));
        c.InvokeCommand("context", "");                                                  // nobody listens, and a request waits
        host.Content = view;
        UiHelpers.Pump();
        Assert.Equal(ChatPanel.None, c.ShownPanel);
        Assert.False(sheet.IsOpen);
        var title = UiHelpers.Prompt(view).GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("ClaudeChatPermissionTitle"));
        Assert.Equal("Rodar este comando?", title.Text);                                 // and the request asks as it did, back in the window

        c.DismissPermission("r1");                                                       // the request goes: the sheet does not come up
        UiHelpers.Pump();
        Assert.False(sheet.IsOpen);
        Assert.Equal(ChatPanel.None, c.ShownPanel);
        window.Close();
    }

    [AvaloniaFact]
    public void A_panel_of_the_desktop_with_a_request_waiting_is_closed_when_the_layout_becomes_touch()
    {
        var c = TouchSamples.Opus();
        c.Apply(Bash("r1", "ls"));
        var ui = new Opened(c, layout: ChatLayout.Desktop);
        c.InvokeCommand("context", "");                                                  // the desktop's panel shows over the list, beside the request
        UiHelpers.Pump();
        Assert.Equal(ChatPanel.Context, c.ShownPanel);

        ui.View.Layout = ChatLayout.Touch;                                               // where the panel is a sheet, which a request keeps from opening
        UiHelpers.Pump();
        Assert.Equal(ChatPanel.None, c.ShownPanel);
        Assert.False(ui.UsageSheet.IsOpen);
        c.DismissPermission("r1");
        UiHelpers.Pump();
        Assert.False(ui.UsageSheet.IsOpen);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void The_desktop_keeps_its_panels_beside_a_request_and_the_agent_map_stays_in_touch()
    {
        var desktop = TouchSamples.Opus();
        desktop.Apply(Bash("r1", "ls"));
        var ui = new Opened(desktop, layout: ChatLayout.Desktop);
        desktop.InvokeCommand("context", "");
        UiHelpers.Pump();
        Assert.Equal(ChatPanel.Context, desktop.ShownPanel);                             // the panel does not cover the request there
        Assert.True(ui.Prompt.IsEffectivelyVisible);
        ui.Window.Close();

        var touch = UiHelpers.Recording("subagent", "stream");
        var phone = new Opened(touch);
        touch.ShowAgents();
        UiHelpers.Pump();
        touch.Apply(Bash("r2", "ls"));
        UiHelpers.Pump();
        Assert.Equal(ChatPanel.Agents, touch.ShownPanel);                                // as on the desktop, the map stays
        Assert.True(phone.View.GetVisualDescendants().OfType<AgentMapPanel>().Single().IsEffectivelyVisible);
        phone.Window.Close();
    }

    // ---- the questions ----

    [AvaloniaFact]
    public void Touch_questions_name_the_file_the_tool_or_the_command_whatever_the_path_looks_like()
    {
        var c = new ChatConversation();
        c.Apply(Request("r1", "Edit", "{\"file_path\":\"C:\\\\demo\\\\app\\\\notes.md\",\"old_string\":\"a\",\"new_string\":\"b\"}"));
        c.Apply(Request("r2", "Write", "{\"file_path\":\"todo.md\",\"content\":\"- Item 1\\n\"}"));
        c.Apply(Request("r3", "MultiEdit", "{\"file_path\":\"/home/dev/api/Orders.cs\",\"edits\":[{\"old_string\":\"a\",\"new_string\":\"b\"}]}"));
        c.Apply(Request("r4", "NotebookEdit", "{\"notebook_path\":\"C:/work/analysis.ipynb\",\"new_source\":\"x = 1\"}"));
        c.Apply(Request("r5", "WebFetch", "{\"url\":\"https://example.com/docs\"}", displayName: "Fetch"));
        c.Apply(Request("r6", "PowerShell", JsonSerializer.Serialize(new { command = "Get-ChildItem" })));
        c.Apply(Request("r7", "Edit", "{\"old_string\":\"a\",\"new_string\":\"b\"}"));       // no path to name
        var ui = new Opened(c);

        var titles = new List<string>();
        for (var i = 1; i <= 7; i++)
        {
            titles.Add(ui.Title);
            c.DismissPermission("r" + i);
            UiHelpers.Pump();
        }
        Assert.Equal(new[]
        {
            "Editar notes.md?", "Editar todo.md?", "Editar Orders.cs?", "Editar analysis.ipynb?",
            "Usar Fetch?", "Rodar este comando?", "Usar Edit?",
        }, titles);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Touch_option_two_says_this_session_only_when_every_rule_stays_in_it()
    {
        // the 0.5.0 rule, kept by the touch layout: a rule that outlives the conversation (or does not say where it goes) cannot say "this session"
        const string Saved = "[{\"type\":\"addRules\",\"rules\":[{\"toolName\":\"Bash\",\"ruleContent\":\"npm test\"}],\"behavior\":\"allow\",\"destination\":\"localSettings\"}]";
        const string Mixed = "[{\"type\":\"setMode\",\"mode\":\"acceptEdits\",\"destination\":\"session\"},{\"type\":\"addRules\",\"rules\":[],\"behavior\":\"allow\",\"destination\":\"userSettings\"}]";
        const string Unsaid = "[{\"type\":\"setMode\",\"mode\":\"acceptEdits\"}]";
        var c = new ChatConversation();
        c.Apply(Bash("r0", "npm test", Suggestions));
        c.Apply(Bash("r1", "npm test", Saved));
        c.Apply(Bash("r2", "npm test", Mixed));
        c.Apply(Bash("r3", "npm test", Unsaid));
        var ui = new Opened(c);

        // all in the session: the conversation's label, in both languages
        Assert.Contains(YesAlways, ui.Texts);
        ui.View.Strings = ChatStrings.English;
        UiHelpers.Pump();
        Assert.Contains("Yes, and don't ask again this session", ui.Texts);

        // r1 saved in the settings, r2 one rule out of the session, r3 a mode that does not say where it goes: none says "this session"
        foreach (var id in new[] { "r0", "r1", "r2" })
        {
            c.DismissPermission(id);
            UiHelpers.Pump();
            Assert.Contains("Yes, and don't ask again", ui.Texts);
            Assert.DoesNotContain("Yes, and don't ask again this session", ui.Texts);
            ui.View.Strings = ChatStrings.PortugueseBrazil;
            UiHelpers.Pump();
            Assert.Contains("Sim, e não perguntar de novo", ui.Texts);
            Assert.DoesNotContain(YesAlways, ui.Texts);
            ui.View.Strings = ChatStrings.English;
            UiHelpers.Pump();
        }
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Touch_questions_follow_the_language()
    {
        var c = new ChatConversation();
        c.Apply(Edit());
        c.Apply(Bash("r2", "dotnet test"));
        c.Apply(Request("r3", "WebFetch", "{\"url\":\"https://example.com/docs\"}", displayName: "Fetch"));
        var ui = new Opened(c);
        ui.View.Strings = ChatStrings.English;
        UiHelpers.Pump();

        Assert.Equal("Edit OrderService.cs?", ui.Title);
        Assert.Contains("Yes, and don't ask again this session", ui.Texts);
        c.DismissPermission("r1");
        UiHelpers.Pump();
        Assert.Equal("Run this command?", ui.Title);
        c.DismissPermission("r2");
        UiHelpers.Pump();
        Assert.Equal("Use Fetch?", ui.Title);

        ui.View.Strings = ChatStrings.PortugueseBrazil;                                  // changed while it is on screen
        UiHelpers.Pump();
        Assert.Equal("Usar Fetch?", ui.Title);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Touch_request_shows_the_whole_command_and_the_whole_diff()
    {
        // what the user says yes to is never cut: the touch layout changes the look of the request, not what it shows
        var command = "echo " + new string('a', 6000) + " && rm -rf /tmp/x";
        var c = new ChatConversation();
        c.Apply(Bash("r1", command));
        var oldText = string.Join("\n", Enumerable.Range(1, 40).Select(i => "old line " + i));
        var newText = string.Join("\n", Enumerable.Range(1, 40).Select(i => "new line " + i));
        c.Apply(Request("r2", "Edit", JsonSerializer.Serialize(new { file_path = "a.txt", old_string = oldText, new_string = newText }), Suggestions));
        var ui = new Opened(c);

        Assert.Contains(command, ui.Texts);
        c.DismissPermission("r1");
        UiHelpers.Pump();
        var diff = Assert.Single(ui.Prompt.GetVisualDescendants().OfType<DiffView>());
        Assert.False(diff.IsCut);
        Assert.Equal(80, diff.GetVisualDescendants().OfType<ItemsControl>().Single().ItemCount);   // 40 old and 40 new rows
        Assert.DoesNotContain(ui.Texts, t => t.StartsWith("+", StringComparison.Ordinal) && t.EndsWith(" linhas", StringComparison.Ordinal));
        ui.Window.Close();
    }

    // ---- the look ----

    [AvaloniaFact]
    public void Touch_frame_is_a_warning_ring_on_the_sheets_background_and_the_detail_goes_to_240()
    {
        var oldText = string.Join("\n", Enumerable.Range(1, 40).Select(i => "old line " + i));
        var newText = string.Join("\n", Enumerable.Range(1, 40).Select(i => "new line " + i));
        var c = new ChatConversation();
        c.Apply(Request("r1", "Edit", JsonSerializer.Serialize(new { file_path = "a.txt", old_string = oldText, new_string = newText }), Suggestions));
        var ui = new Opened(c);

        var frame = ui.Frame;
        Assert.Equal(new CornerRadius(14), frame.CornerRadius);
        Assert.Equal(new Thickness(12), frame.Padding);
        Assert.Equal(new Thickness(1), frame.BorderThickness);
        Assert.Same(Resource(ui.View, "ClaudeChat.SheetBackground"), frame.Background);

        // the border is Warning at 60 %, with the keyboard in the request (the desktop's turns to its active border then) and without it
        var warning = ColorOf(ui.View, "ClaudeChat.Warning");
        var ring = Color.FromArgb(153, warning.R, warning.G, warning.B);
        Assert.True(ui.Prompt.IsKeyboardFocusWithin);
        Assert.Equal(ring, Assert.IsAssignableFrom<ISolidColorBrush>(frame.BorderBrush).Color);
        UiHelpers.Input(ui.View).Focus();
        UiHelpers.Pump();
        Assert.False(ui.Prompt.IsKeyboardFocusWithin);
        Assert.Equal(ring, Assert.IsAssignableFrom<ISolidColorBrush>(frame.BorderBrush).Color);
        Assert.Same(Resource(ui.View, "ClaudeChat.PermissionWarningBorder"), frame.BorderBrush);

        // the detail (the command, or the file and its diff) grows to 240 and scrolls inside
        Assert.Equal(240, ui.Detail.MaxHeight);
        Assert.Equal(240, ui.Detail.Bounds.Height, 0);
        Assert.True(ui.Detail.Extent.Height > ui.Detail.Viewport.Height + 100);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Touch_yes_is_the_accent_the_others_are_the_sheets_group_and_the_keyboard_changes_neither()
    {
        var c = new ChatConversation();
        c.Apply(Edit());
        var ui = new Opened(c);
        var accentFill = Resource(ui.View, "ClaudeChat.AccentFill");
        var accentInk = Resource(ui.View, "ClaudeChat.AccentForeground");
        var group = Resource(ui.View, "ClaudeChat.SheetGroupBackground");
        var ink = Resource(ui.View, "ClaudeChat.Foreground");

        void Looks()
        {
            var yes = ui.Option(Yes);
            var yesLabel = yes.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == Yes);
            Assert.Same(accentFill, yes.Background);
            Assert.Same(accentInk, yesLabel.Foreground);
            Assert.Equal(FontWeight.SemiBold, yesLabel.FontWeight);
            foreach (var other in new[] { YesAlways, No })
            {
                var option = ui.Option(other);
                var label = option.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == other);
                Assert.Same(group, option.Background);
                Assert.Same(ink, label.Foreground);
                Assert.NotEqual(FontWeight.SemiBold, label.FontWeight);
            }
            foreach (var text in new[] { Yes, YesAlways, No })
            {
                var option = ui.Option(text);
                Assert.Equal(new CornerRadius(10), option.CornerRadius);
                Assert.True(option.MinHeight >= 48);
                Assert.Equal(15, option.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == text).FontSize);
            }
        }

        Looks();
        ui.Option(No).Focus();                                                           // the keyboard moves: the accent stays where the answer is
        UiHelpers.Pump();
        Looks();
        UiHelpers.Input(ui.View).Focus();
        UiHelpers.Pump();
        Looks();
        ui.Window.Close();
    }

    /// <summary>The face of an answer: the border the button's template draws.</summary>
    private static Border Face(Opened ui, string option) =>
        ui.Option(option).GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_ChatButton");

    [AvaloniaFact]
    public void Touch_the_keyboard_shows_by_a_ring_on_every_answer_but_the_first()
    {
        var c = new ChatConversation();
        c.Apply(Edit());
        var ui = new Opened(c);
        var ring = Resource(ui.View, "ClaudeChat.FocusBorder");

        Assert.True(ui.Option(Yes).IsFocused);                                           // the request took the keyboard when it came
        foreach (var text in new[] { Yes, YesAlways, No }) Assert.Equal(new Thickness(0), Face(ui, text).BorderThickness);   // no ring on the first: it is the accent
        ui.Option(No).Focus(NavigationMethod.Tab);
        UiHelpers.Pump();
        Assert.Equal(new Thickness(1), Face(ui, No).BorderThickness);
        Assert.Same(ring, Face(ui, No).BorderBrush);
        Assert.Equal(new Thickness(0), Face(ui, Yes).BorderThickness);
        Assert.Equal(new Thickness(0), Face(ui, YesAlways).BorderThickness);

        ui.Option(Yes).Focus(NavigationMethod.Tab);                                      // back on the first: the accent says where it is
        UiHelpers.Pump();
        Assert.Equal(new Thickness(0), Face(ui, Yes).BorderThickness);
        Assert.Equal(new Thickness(0), Face(ui, No).BorderThickness);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Touch_a_finger_dims_the_answer_and_the_first_keeps_the_accent_under_the_pointer()
    {
        var clock = new ManualClock();
        var c = new ChatConversation();
        c.Apply(Edit());
        var ui = new Opened(c, clock);
        var accentFill = Resource(ui.View, "ClaudeChat.AccentFill");
        UiHelpers.Input(ui.View).Focus();                                                // the keyboard is elsewhere: nothing about "Yes" is selected
        UiHelpers.Pump();

        var yes = ui.Option(Yes);
        var at = yes.TranslatePoint(new Point(yes.Bounds.Width / 2, yes.Bounds.Height / 2), ui.Window)!.Value;
        ui.Window.MouseMove(at, RawInputModifiers.None);
        UiHelpers.Pump();
        Assert.Same(accentFill, Face(ui, Yes).Background);                               // the desktop's hover is a gray: not here
        Assert.Equal(1, yes.Opacity);

        ui.Window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
        UiHelpers.Pump();
        Assert.Equal(0.7, yes.Opacity, 2);                                               // a finger on it dims it
        ui.Window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
        UiHelpers.Pump();
        Assert.Equal(1, yes.Opacity);
        Assert.Empty(ui.Answers);                                                        // and the pause still holds: it had just come
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Back_to_the_desktop_brings_the_numbers_the_titles_and_the_desktops_sizes_back()
    {
        var oldText = string.Join("\n", Enumerable.Range(1, 40).Select(i => "old line " + i));
        var newText = string.Join("\n", Enumerable.Range(1, 40).Select(i => "new line " + i));
        var c = new ChatConversation();
        c.Apply(Request("r1", "Edit", JsonSerializer.Serialize(new { file_path = "C:/repo/src/OrderService.cs", old_string = oldText, new_string = newText }), Suggestions));
        var ui = new Opened(c);
        ui.Tap(No);                                                                      // with the field and its button open
        Assert.Contains(Send, ui.Texts);

        ui.View.Layout = ChatLayout.Desktop;
        UiHelpers.Pump();
        Assert.Equal("Fazer esta edição em OrderService.cs?", DesktopTitle(ui));          // the desktop's card asks as the extension 2.1.310 does
        Assert.Equal(new[] { "1", "2", "3" }, ui.Numbers);
        Assert.DoesNotContain(Send, ui.Texts);                                           // the button is the touch layout's: Enter sends there
        Assert.Equal(new CornerRadius(8), ui.Frame.CornerRadius);
        Assert.Equal(new Thickness(8), ui.Frame.Padding);
        Assert.Equal(200, ui.Detail.MaxHeight);
        Assert.Equal(200, ui.Detail.Bounds.Height, 0);

        ui.View.Layout = ChatLayout.Touch;                                               // and again
        UiHelpers.Pump();
        Assert.Equal("Editar OrderService.cs?", ui.Title);
        Assert.Empty(ui.Numbers);
        Assert.Contains(Send, ui.Texts);                                                 // the field was left open: its button is back with the layout
        Assert.Equal(56, ui.Detail.Bounds.Height, 0);                                    // and the detail is a peek again, for the keyboard
        ui.Window.Close();
    }

    // ---- the field of "No" ----

    [AvaloniaFact]
    public void The_send_button_does_what_enter_does_in_the_field()
    {
        var clock = new ManualClock();
        var c = new ChatConversation();
        c.Apply(Edit("r1"));
        c.Apply(Edit("r2"));
        c.Apply(Edit("r3"));
        var ui = new Opened(c, clock);
        clock.Advance(300);

        // nothing written: a plain no, and the host sends the claude its default text
        ui.Tap(No);
        UiHelpers.Click(ui.Window, ui.Option(Send));
        Assert.Equal(PermissionDecision.Deny, Assert.Single(ui.Answers).Decision);
        Assert.Null(ui.Answers[0].Message);
        Assert.Equal("r2", ui.Prompt.Request!.RequestId);
        Assert.Null(ui.DenyField);                                                       // the next request starts with the field closed
        Assert.DoesNotContain(Send, ui.Texts);

        // only spaces is nothing written; what is written is trimmed
        clock.Advance(300);
        ui.Tap(No);
        UiHelpers.Type(ui.Window, "   ");
        UiHelpers.Click(ui.Window, ui.Option(Send));
        Assert.Null(ui.Answers[1].Message);

        clock.Advance(300);
        ui.Tap(No);
        UiHelpers.Type(ui.Window, "  use descending  ");
        UiHelpers.Press(ui.Window, Key.Enter);                                           // Enter in the field sends the same as the button
        Assert.Equal(PermissionDecision.Deny, ui.Answers[2].Decision);
        Assert.Equal("use descending", ui.Answers[2].Message);
        Assert.Equal(new[] { "r1", "r2", "r3" }, ui.Answers.Select(a => a.Request.RequestId));
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void The_pause_holds_for_the_send_button_when_the_request_comes_back_on_screen()
    {
        var clock = new ManualClock();
        var c = new ChatConversation();
        c.Apply(Edit());
        var ui = new Opened(c, clock);
        clock.Advance(300);
        ui.Tap(No);
        UiHelpers.Type(ui.Window, "use descending");

        ui.View.IsReadOnly = true;                                                       // the request is hidden, and comes back: nobody has seen it yet
        UiHelpers.Pump();
        ui.View.IsReadOnly = false;
        UiHelpers.Pump();
        Assert.NotNull(ui.DenyField);                                                    // the field is as it was left
        UiHelpers.Click(ui.Window, ui.Option(Send));
        Assert.Empty(ui.Answers);

        clock.Advance(300);
        UiHelpers.Click(ui.Window, ui.Option(Send));
        var answer = Assert.Single(ui.Answers);
        Assert.Equal(PermissionDecision.Deny, answer.Decision);
        Assert.Equal("use descending", answer.Message);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Esc_closes_the_field_and_takes_its_button_with_it()
    {
        var clock = new ManualClock();
        var c = new ChatConversation();
        c.Apply(Edit());
        var ui = new Opened(c, clock);
        clock.Advance(300);
        ui.Tap(No);
        UiHelpers.Type(ui.Window, "half a thought");
        Assert.Contains(Send, ui.Texts);

        UiHelpers.Press(ui.Window, Key.Escape);
        Assert.Null(ui.DenyField);
        Assert.DoesNotContain(Send, ui.Texts);
        Assert.Empty(ui.Answers);

        ui.Tap(No);                                                                      // it opens empty again
        Assert.Equal("", ui.DenyField!.Text ?? "");
        Assert.Contains(Send, ui.Texts);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_digit_with_the_keyboard_on_the_send_button_answers_nothing()
    {
        // the 1, 2 and 3 of a keyboard answer while it is in the request; not in the field of "No", nor on the button under it
        var clock = new ManualClock();
        var c = new ChatConversation();
        c.Apply(Edit());
        var ui = new Opened(c, clock);
        clock.Advance(300);
        ui.Tap(No);
        ui.Option(Send).Focus(NavigationMethod.Tab);
        UiHelpers.Pump();

        UiHelpers.Press(ui.Window, Key.D1);
        UiHelpers.Press(ui.Window, Key.D2);
        UiHelpers.Press(ui.Window, Key.D3);
        Assert.Empty(ui.Answers);
        Assert.NotNull(ui.DenyField);

        UiHelpers.Press(ui.Window, Key.Enter);                                           // Enter on the button is a click
        Assert.Equal(PermissionDecision.Deny, Assert.Single(ui.Answers).Decision);
        ui.Window.Close();
    }

    // ---- the keyboard: the field of "No" above it, and where it goes after an answer ----

    /// <summary>An edit of 40 lines, which makes the detail as tall as it gets.</summary>
    private static PermissionRequested LongEdit(string id = "r1")
    {
        var oldText = string.Join("\n", Enumerable.Range(1, 40).Select(i => "old line " + i));
        var newText = string.Join("\n", Enumerable.Range(1, 40).Select(i => "new line " + i));
        return Request(id, "Edit", JsonSerializer.Serialize(new { file_path = "C:/repo/src/OrderService.cs", old_string = oldText, new_string = newText }), Suggestions);
    }

    private static void AssertInsideTheView(Opened ui, string what, Control control)
    {
        var (below, above) = ui.Outside(control);
        Assert.True(below <= 0.5 && above <= 0.5, what + " is outside the view: " + below + " below, " + above + " above");
    }

    [AvaloniaFact]
    public void Touch_with_the_field_of_no_open_the_detail_is_a_peek_so_the_field_and_its_button_stay_in_a_short_view()
    {
        // the on-screen keyboard is up: what is left of the phone's screen is about 400 DIPs, and the field that is typed in
        // (and the button under it) must not be under the keyboard
        var clock = new ManualClock();
        var c = new ChatConversation();
        c.Apply(LongEdit());
        var ui = new Opened(c, clock, height: 400);
        clock.Advance(300);
        Assert.Equal(240, ui.Detail.Bounds.Height, 0);                                   // the request alone: the detail has its room

        UiHelpers.Press(ui.Window, Key.D3);                                              // "No" by the keyboard: its button is under the edge of a view this short
        var field = ui.DenyField;
        Assert.NotNull(field);
        var peek = Assert.IsType<double>(Resource(ui.View, "ClaudeChat.PromptDetailPeekHeight"));
        Assert.Equal(56, peek);
        Assert.Equal(peek, ui.Detail.Bounds.Height, 0);                                  // the diff is a peek, which scrolls
        Assert.True(ui.Detail.Extent.Height > ui.Detail.Viewport.Height + 100);          // and all of it is still in there

        var title = ui.Prompt.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("ClaudeChatPermissionTitle"));
        AssertInsideTheView(ui, "the title", title);
        foreach (var text in new[] { Yes, YesAlways, No }) AssertInsideTheView(ui, text, ui.Option(text));
        AssertInsideTheView(ui, "the field", field!);
        AssertInsideTheView(ui, "the send button", ui.Option(Send));

        UiHelpers.Press(ui.Window, Key.Escape);                                          // the field closes: the detail has its room back
        Assert.Null(ui.DenyField);
        Assert.Equal(240, ui.Detail.Bounds.Height, 0);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Touch_a_long_command_is_a_peek_too_when_the_keyboard_comes_up_over_the_field()
    {
        // as it goes on a phone: the finger taps "No" with the whole screen there, and the keyboard takes its share after that
        var clock = new ManualClock();
        var c = new ChatConversation();
        c.Apply(Bash("r1", string.Join("\n", Enumerable.Range(1, 30).Select(i => "echo " + i)), Suggestions));
        var ui = new Opened(c, clock);
        clock.Advance(300);
        Assert.Equal(240, ui.Detail.Bounds.Height, 0);

        ui.Tap(No);
        ui.Window.Height = 400;                                                          // the keyboard came up
        UiHelpers.PumpUntil(() => ui.View.Bounds.Height < 401);
        Assert.Equal(56, ui.Detail.Bounds.Height, 0);
        Assert.Contains(ui.Texts, t => t.StartsWith("echo 1\necho 2\n", StringComparison.Ordinal));   // the start of the command is what the peek shows
        AssertInsideTheView(ui, "the field", ui.DenyField!);
        AssertInsideTheView(ui, "the send button", ui.Option(Send));
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Touch_the_detail_has_its_room_back_when_another_request_or_the_desktop_closes_the_field()
    {
        var clock = new ManualClock();
        var c = new ChatConversation();
        c.Apply(LongEdit("r1"));
        c.Apply(LongEdit("r2"));
        var ui = new Opened(c, clock);
        clock.Advance(300);
        ui.Tap(No);
        Assert.Equal(56, ui.Detail.Bounds.Height, 0);

        ui.View.Layout = ChatLayout.Desktop;                                             // the desktop never shrinks it, field or not
        UiHelpers.Pump();
        Assert.NotNull(ui.DenyField);
        Assert.Equal(200, ui.Detail.Bounds.Height, 0);
        ui.View.Layout = ChatLayout.Touch;                                               // and the touch layout does again, the field being open
        UiHelpers.Pump();
        Assert.Equal(56, ui.Detail.Bounds.Height, 0);

        c.DismissPermission("r1");                                                       // the next request comes with the field closed
        UiHelpers.Pump();
        Assert.Equal("r2", ui.Prompt.Request!.RequestId);
        Assert.Null(ui.DenyField);
        Assert.Equal(240, ui.Detail.Bounds.Height, 0);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Desktop_keeps_its_detail_while_the_field_is_open()
    {
        var c = new ChatConversation();
        c.Apply(LongEdit());
        var ui = new Opened(c, layout: ChatLayout.Desktop);
        Assert.Equal(200, ui.Detail.Bounds.Height, 0);

        Assert.NotNull(ui.DenyField);                                                    // the desktop's field is always there (its "No" refuses at once)
        ui.DenyField!.Focus();
        UiHelpers.Pump();
        Assert.Equal(200, ui.Detail.MaxHeight);
        Assert.Equal(200, ui.Detail.Bounds.Height, 0);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Touch_an_answer_by_tap_does_not_give_the_keyboard_to_the_reply_box()
    {
        // on a phone, the reply box with the keyboard is the soft keyboard coming up after every "Yes"
        var clock = new ManualClock();
        var c = new ChatConversation();
        c.Apply(Edit());
        var ui = new Opened(c, clock);
        var input = UiHelpers.Input(ui.View);
        Assert.True(ui.Option(Yes).IsFocused);                                           // the request took the keyboard when it came
        clock.Advance(300);

        ui.Tap(Yes);
        Assert.Equal(PermissionDecision.Allow, Assert.Single(ui.Answers).Decision);
        Assert.False(ui.Prompt.IsEffectivelyVisible);
        Assert.False(input.IsFocused);
        Assert.DoesNotContain(ui.View.GetVisualDescendants().OfType<TextBox>(), t => t.IsFocused);   // nothing that takes text has it
        Assert.Null(UiHelpers.Focused(ui.Window));                                       // it is nowhere: not on the "Yes" that is hidden now either
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Touch_an_answer_with_the_send_button_does_not_either()
    {
        var clock = new ManualClock();
        var c = new ChatConversation();
        c.Apply(Edit());
        var ui = new Opened(c, clock);
        clock.Advance(300);
        ui.Tap(No);
        UiHelpers.Type(ui.Window, "use descending");

        UiHelpers.Click(ui.Window, ui.Option(Send));
        Assert.Equal("use descending", Assert.Single(ui.Answers).Message);
        Assert.False(UiHelpers.Input(ui.View).IsFocused);
        Assert.DoesNotContain(ui.View.GetVisualDescendants().OfType<TextBox>(), t => t.IsFocused);
        Assert.Null(UiHelpers.Focused(ui.Window));
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Touch_an_answer_with_the_keyboards_enter_leaves_no_text_box_with_the_keyboard()
    {
        // the soft keyboard's action key sends from the field itself: the field is hidden then, and must not keep the focus,
        // which would keep the soft keyboard up with nowhere to type
        var clock = new ManualClock();
        var c = new ChatConversation();
        c.Apply(Edit());
        var ui = new Opened(c, clock);
        clock.Advance(300);
        ui.Tap(No);
        var field = ui.DenyField;
        Assert.NotNull(field);
        Assert.True(field!.IsFocused);
        UiHelpers.Type(ui.Window, "use descending");

        UiHelpers.Press(ui.Window, Key.Enter);
        Assert.Equal("use descending", Assert.Single(ui.Answers).Message);
        Assert.False(field.IsFocused);
        Assert.False(UiHelpers.Input(ui.View).IsFocused);
        Assert.Null(UiHelpers.Focused(ui.Window));
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Touch_the_next_request_keeps_the_keyboard_and_only_the_last_answer_stops_short_of_the_reply_box()
    {
        var clock = new ManualClock();
        var c = new ChatConversation();
        c.Apply(Edit("r1"));
        c.Apply(Edit("r2"));
        var ui = new Opened(c, clock);
        var input = UiHelpers.Input(ui.View);
        clock.Advance(300);

        ui.Tap(Yes);                                                                     // another waits: the request has the keyboard, as ever
        Assert.Equal("r2", ui.Prompt.Request!.RequestId);
        Assert.True(ui.Option(Yes).IsFocused);
        Assert.False(input.IsFocused);

        clock.Advance(300);
        ui.Tap(Yes);                                                                     // the last one
        Assert.Equal(new[] { "r1", "r2" }, ui.Answers.Select(a => a.Request.RequestId));
        Assert.False(input.IsFocused);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Desktop_gives_the_keyboard_back_to_the_reply_box_after_the_last_answer()
    {
        var clock = new ManualClock();
        var c = new ChatConversation();
        c.Apply(Edit());
        var ui = new Opened(c, clock, layout: ChatLayout.Desktop);
        var input = UiHelpers.Input(ui.View);
        clock.Advance(500);                                                              // the desktop's card arms in 500 ms (2.1.310)

        ui.Tap(Yes);
        Assert.Single(ui.Answers);
        Assert.True(input.IsFocused);                                                    // as it always did there
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Desktop_field_has_no_send_button()
    {
        var c = new ChatConversation();
        c.Apply(Edit());
        var ui = new Opened(c, layout: ChatLayout.Desktop);

        Assert.NotNull(ui.DenyField);                                                    // always there on the desktop
        Assert.DoesNotContain(Send, ui.Texts);                                           // the desktop's request has no button under it
        Assert.DoesNotContain(Send, UiHelpers.ButtonNames(ui.Prompt));
        ui.Window.Close();
    }

    // ---- the texts ----

    [AvaloniaFact]
    public void The_send_button_follows_the_language()
    {
        var c = new ChatConversation();
        c.Apply(Edit());
        var ui = new Opened(c);
        ui.Tap(No);
        Assert.Contains(Send, ui.Texts);

        ui.View.Strings = ChatStrings.English;                                           // changed while the field is open
        UiHelpers.Pump();
        Assert.Contains("Send", ui.Texts);
        Assert.DoesNotContain(Send, ui.Texts);
        Assert.NotNull(ui.DenyField);                                                    // and the field stays as it is
        ui.Window.Close();
    }
}
