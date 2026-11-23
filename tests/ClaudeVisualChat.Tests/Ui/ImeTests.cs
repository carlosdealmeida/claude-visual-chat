using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using ClaudeVisualChat.Protocol;
using Xunit;

namespace ClaudeVisualChat.Tests.Ui;

/// <summary>
/// No key acts while an input method composes (Japanese, Chinese, Korean…): the Enter that picks the word, the arrows that walk its
/// candidates and the Esc that drops them are the input method's, and the box, the palette, the plan's comment box, the permission
/// card, "What went wrong?" and the view's panels leave them alone. Each test runs in the two ways a composing key arrives: as
/// <see cref="Key.ImeProcessed"/> (what Windows sends), and as the plain key while the box still holds the composed text.
/// </summary>
public class ImeTests
{
    /// <summary>How the input method's key reaches the control.</summary>
    public enum Composing
    {
        /// <summary>The window's keyboard says <see cref="Key.ImeProcessed"/>, with the key pressed as its physical key.</summary>
        ImeProcessedKey,

        /// <summary>The plain key, while the box shows text being composed (its presenter's preedit text).</summary>
        PreeditInTheBox,
    }

    private const string Preedit = "にほ";

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

    /// <summary>The composition starts in <paramref name="box"/> (only the way that composes in the box has one): shown, not yet its text.</summary>
    private static void Start(Composing how, TextBox box)
    {
        if (how != Composing.PreeditInTheBox) return;
        var text = box.Text;
        UiHelpers.Compose(box, Preedit);
        Assert.Equal(Preedit, box.GetVisualDescendants().OfType<TextPresenter>().First().PreeditText);
        Assert.Equal(text, box.Text);
    }

    /// <summary>The composition is over: the next key is a plain one.</summary>
    private static void End(Composing how, TextBox box)
    {
        if (how == Composing.PreeditInTheBox) UiHelpers.Compose(box, null);
    }

    /// <summary>A key going down while the input method composes, the one way or the other.</summary>
    private static void ComposingKey(Window window, Composing how, Key key, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        if (how == Composing.ImeProcessedKey) UiHelpers.ImeKey(window, physical, modifiers);
        else UiHelpers.KeyDown(window, key, modifiers);
    }

    private static void ComposingEnter(Window window, Composing how, RawInputModifiers modifiers = RawInputModifiers.None) =>
        ComposingKey(window, how, Key.Enter, PhysicalKey.Enter, modifiers);

    private static void ComposingEscape(Window window, Composing how) => ComposingKey(window, how, Key.Escape, PhysicalKey.Escape);

    // ---- the two signs ----

    [AvaloniaFact]
    public void The_platform_saying_ime_processed_is_composing_on_its_own()
    {
        // a box with no text being composed, and a control that is no box: only the key says it is the input method's
        var box = new TextBox { Text = "oi" };
        var window = UiHelpers.Show(box);
        Assert.True(string.IsNullOrEmpty(box.GetVisualDescendants().OfType<TextPresenter>().First().PreeditText));
        foreach (var source in new object[] { box, new Border() })
        {
            Assert.True(ImeKeys.IsComposing(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.ImeProcessed, Source = source }));
            Assert.False(ImeKeys.IsComposing(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = source }));
        }
        window.Close();
    }

    // ---- the reply box ----

    [AvaloniaTheory]
    [InlineData(Composing.ImeProcessedKey)]
    [InlineData(Composing.PreeditInTheBox)]
    public void An_ime_enter_sends_nothing_and_the_plain_enter_after_it_sends(Composing how)
    {
        var c = new ChatConversation();
        var sent = new List<string>();
        c.MessageSubmitted += (_, e) => sent.Add(e.Text);
        var view = new ClaudeChatView { Conversation = c };
        var window = UiHelpers.Show(view);
        var input = UiHelpers.Input(view);
        var unhandled = UiHelpers.UnhandledKeys(window);
        input.Focus();
        UiHelpers.Type(window, "oi");
        Start(how, input);

        ComposingEnter(window, how);
        Assert.Empty(sent);
        Assert.Contains(how == Composing.ImeProcessedKey ? Key.ImeProcessed : Key.Enter, unhandled); // the input method's key: nobody takes it
        UiHelpers.KeyUp(window, Key.Enter);                                    // the key comes up as a plain Enter
        Assert.Empty(sent);
        Assert.Equal("oi", input.Text);

        End(how, input);
        UiHelpers.Press(window, Key.Enter);
        Assert.Equal(new[] { "oi" }, sent);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(Composing.ImeProcessedKey)]
    [InlineData(Composing.PreeditInTheBox)]
    public void An_ime_up_arrow_recalls_no_prompt(Composing how)
    {
        var c = new ChatConversation();
        c.Apply(new UserBlock(null, new TextContentBlock("one"), null, false, false));
        var view = new ClaudeChatView { Conversation = c };
        var window = UiHelpers.Show(view);
        var input = UiHelpers.Input(view);
        input.Focus();
        UiHelpers.Pump();
        Start(how, input);

        ComposingKey(window, how, Key.Up, PhysicalKey.ArrowUp);
        UiHelpers.KeyUp(window, Key.Up);
        Assert.Equal("", input.Text ?? "");

        End(how, input);
        UiHelpers.Press(window, Key.Up);                                       // a plain Up recalls it
        Assert.Equal("one", input.Text);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(Composing.ImeProcessedKey)]
    [InlineData(Composing.PreeditInTheBox)]
    public void An_ime_escape_does_not_stop_claude_and_the_next_escape_does(Composing how)
    {
        var c = new ChatConversation();
        var stops = 0;
        c.StopRequested += (_, _) => stops++;
        var view = new ClaudeChatView { Conversation = c };
        var window = UiHelpers.Show(view);
        var input = UiHelpers.Input(view);
        input.Focus();
        UiHelpers.Type(window, "go");
        UiHelpers.Press(window, Key.Enter);
        Assert.True(c.IsBusy);
        Start(how, input);

        ComposingEscape(window, how);
        UiHelpers.KeyUp(window, Key.Escape);
        Assert.Equal(0, stops);

        End(how, input);
        UiHelpers.Press(window, Key.Escape);
        Assert.Equal(1, stops);
        window.Close();
    }

    // ---- the command palette's filter ----

    [AvaloniaTheory]
    [InlineData(Composing.ImeProcessedKey)]
    [InlineData(Composing.PreeditInTheBox)]
    public void An_ime_key_in_the_palette_filter_runs_and_closes_nothing(Composing how)
    {
        var c = new ChatConversation { Commands = new[] { new ChatCommand("hello", "Say hello", "") } };
        var sent = new List<string>();
        var invoked = new List<string>();
        c.MessageSubmitted += (_, e) => sent.Add(e.Text);
        c.CommandInvoked += (_, e) => invoked.Add(e.Name);
        var view = new ClaudeChatView { Conversation = c };
        var window = UiHelpers.Show(view);
        var composer = UiHelpers.Composer(view);
        var palette = composer.GetLogicalDescendants().OfType<CommandPalette>().Single();
        UiHelpers.Click(window, UiHelpers.ButtonWithText(composer, "Show command menu (/)"));
        Assert.True(palette.IsOpen);
        var filter = palette.Child!.GetVisualDescendants().OfType<TextBox>().Single(t => t.IsEffectivelyVisible);
        Assert.True(filter.IsFocused);
        UiHelpers.Type(window, "hel");
        Start(how, filter);

        ComposingEnter(window, how);
        UiHelpers.KeyUp(window, Key.Enter);
        ComposingEscape(window, how);
        UiHelpers.KeyUp(window, Key.Escape);
        Assert.True(palette.IsOpen);
        Assert.True(filter.IsFocused);
        Assert.Empty(sent);
        Assert.Empty(invoked);

        End(how, filter);
        UiHelpers.Press(window, Key.Escape);                                   // a plain Esc closes it
        Assert.False(palette.IsOpen);
        window.Close();
    }

    // ---- the plan's comment box ----

    /// <summary>Drags the mouse over the characters <paramref name="from"/> to <paramref name="to"/> (not included) of the block.</summary>
    private static void Select(Window window, PlanTextBlock block, int from, int to)
    {
        block.BringIntoView();
        UiHelpers.Pump();
        UiHelpers.DrawFrames(); // what is under the pointer is found in the last frame drawn
        Point At(int index, bool right)
        {
            var rect = block.TextLayout.HitTestTextRange(index, 1).First();
            var x = right ? rect.Right - rect.Width * 0.4 : rect.Left + rect.Width * 0.4;
            return block.TranslatePoint(new Point(block.Padding.Left + x, block.Padding.Top + rect.Top + rect.Height / 2), window)!.Value;
        }
        var start = At(from, right: false);
        var end = At(to - 1, right: true);
        window.MouseMove(start, RawInputModifiers.None);
        window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        window.MouseUp(end, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        UiHelpers.Pump();
    }

    [AvaloniaTheory]
    [InlineData(Composing.ImeProcessedKey)]
    [InlineData(Composing.PreeditInTheBox)]
    public void An_ime_enter_adds_no_plan_comment_and_an_ime_escape_keeps_the_box(Composing how)
    {
        var c = new ChatConversation();
        c.Apply(new StatusChanged(null, "plan"));
        c.Apply(new AssistantBlock("m-r1", null, new ToolUseBlock("t-r1", "ExitPlanMode", J("{}"))));
        c.Apply(new PermissionRequested("r1", "ExitPlanMode", "ExitPlanMode", null, "t-r1",
            J("{\"plan\":" + JsonSerializer.Serialize("# Rewrite the parser\n\nIntro line.") + "}"), null));
        var view = new ClaudeChatView { Conversation = c };
        view.Prompt.Clock = new SlowClock();
        var window = UiHelpers.Show(view);
        var document = view.GetVisualDescendants().OfType<PlanDocumentPanel>().Single();
        Assert.True(document.IsOpen);
        Select(window, document.Blocks.First(b => b.Words == "Intro line."), 0, 5);
        var add = document.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("ClaudeChatPlanAddComment") && b.IsEffectivelyVisible);
        UiHelpers.Click(window, add);
        var box = view.GetVisualDescendants().OfType<PlanCommentBox>().Single();
        Assert.True(box.IsOpen);
        Assert.True(box.Field.IsFocused);
        UiHelpers.Type(window, "Say more");
        var plan = c.CurrentPlan!;
        Start(how, box.Field);

        ComposingEnter(window, how);
        UiHelpers.KeyUp(window, Key.Enter);
        Assert.Empty(plan.Comments);
        Assert.True(box.IsOpen);
        ComposingEscape(window, how);
        UiHelpers.KeyUp(window, Key.Escape);
        Assert.True(box.IsOpen);
        Assert.True(document.IsOpen);

        End(how, box.Field);
        UiHelpers.Press(window, Key.Enter);                                    // a plain Enter adds it
        Assert.Equal("Say more", Assert.Single(plan.Comments).Text.Trim());
        Assert.False(box.IsOpen);
        window.Close();
    }

    // ---- the permission card ----

    [AvaloniaTheory]
    [InlineData(Composing.ImeProcessedKey)]
    [InlineData(Composing.PreeditInTheBox)]
    public void An_ime_key_answers_no_permission(Composing how)
    {
        var c = new ChatConversation();
        var answers = new List<PermissionAnsweredEventArgs>();
        c.PermissionAnswered += (_, e) => answers.Add(e);
        c.Apply(new PermissionRequested("r1", "Bash", "Bash", null, "tool-r1", J("{\"command\":\"ls\"}"), null));
        var view = new ClaudeChatView { Conversation = c };
        view.Prompt.Clock = new SlowClock();
        var window = UiHelpers.Show(view);
        var prompt = UiHelpers.Prompt(view);
        Assert.True(prompt.IsEffectivelyVisible);

        // on the card the keyboard is on a button, where no text is composed: only the input method's own key can come
        if (how == Composing.ImeProcessedKey)
        {
            UiHelpers.ButtonWithText(prompt, "Yes").Focus();
            UiHelpers.Pump();
            UiHelpers.ImeKey(window, PhysicalKey.Digit1);
            UiHelpers.KeyUp(window, Key.D1);
            UiHelpers.ImeKey(window, PhysicalKey.Escape);
            UiHelpers.KeyUp(window, Key.Escape);
            Assert.Empty(answers);
        }

        // in the field of "No": Enter, Esc and the arrows
        var field = prompt.GetVisualDescendants().OfType<TextBox>().Single(t => t.IsEffectivelyVisible);
        field.Focus();
        UiHelpers.Pump();
        UiHelpers.Type(window, "use git diff");
        Start(how, field);
        ComposingEnter(window, how);
        UiHelpers.KeyUp(window, Key.Enter);
        ComposingEscape(window, how);
        UiHelpers.KeyUp(window, Key.Escape);
        ComposingKey(window, how, Key.Up, PhysicalKey.ArrowUp);
        UiHelpers.KeyUp(window, Key.Up);
        ComposingKey(window, how, Key.Down, PhysicalKey.ArrowDown);
        UiHelpers.KeyUp(window, Key.Down);
        Assert.Empty(answers);
        Assert.True(field.IsFocused);                                          // the arrows went round no option

        End(how, field);
        UiHelpers.Press(window, Key.Enter);                                    // a plain Enter answers "No" with the text
        var answer = Assert.Single(answers);
        Assert.Equal(PermissionDecision.Deny, answer.Decision);
        Assert.Equal("use git diff", answer.Message);
        window.Close();
    }

    // ---- "What went wrong?" ----

    [AvaloniaTheory]
    [InlineData(Composing.ImeProcessedKey)]
    [InlineData(Composing.PreeditInTheBox)]
    public void An_ime_key_sends_and_closes_no_feedback(Composing how)
    {
        var c = new ChatConversation();
        c.Items.Add(new UserMessageItem("Fix the build"));
        c.Items.Add(new AssistantTextItem { Text = "It builds now.", Uuid = "u-1" });
        c.Items.Add(new TurnFooterItem(1200, 10, 20, 0.01, false));
        var asked = new List<FeedbackRequestedEventArgs>();
        c.FeedbackRequested += (_, e) => asked.Add(e);
        var view = new ClaudeChatView { Conversation = c };
        var window = UiHelpers.Show(view);
        var actions = view.GetVisualDescendants().OfType<ResponseActions>().Single();
        var dialog = view.GetVisualDescendants().OfType<FeedbackDialog>().Single();
        UiHelpers.Click(window, actions.BadButton);
        Assert.True(dialog.IsOpen);
        Assert.Same(dialog.Field, UiHelpers.Focused(window));
        UiHelpers.Type(window, "It broke the tests");
        Start(how, dialog.Field);

        ComposingEnter(window, how, RawInputModifiers.Control);
        UiHelpers.KeyUp(window, Key.Enter, RawInputModifiers.Control);
        ComposingEscape(window, how);
        UiHelpers.KeyUp(window, Key.Escape);
        Assert.True(dialog.IsOpen);
        Assert.Single(asked);                                                  // the rating only, no details

        End(how, dialog.Field);
        UiHelpers.Press(window, Key.Escape);                                   // a plain Esc closes it
        Assert.False(dialog.IsOpen);
        Assert.Single(asked);
        window.Close();
    }

    // ---- the view's panels ----

    [AvaloniaTheory]
    [InlineData(Composing.ImeProcessedKey)]
    [InlineData(Composing.PreeditInTheBox)]
    public void An_ime_escape_closes_no_panel(Composing how)
    {
        var c = new ChatConversation();
        var view = new ClaudeChatView { Conversation = c };
        var window = UiHelpers.Show(view);
        var input = UiHelpers.Input(view);
        c.InvokeCommand("context", "");
        UiHelpers.Pump();
        Assert.Equal(ChatPanel.Context, c.ShownPanel);
        input.Focus();
        UiHelpers.Pump();
        Assert.True(input.IsFocused);
        Start(how, input);

        ComposingEscape(window, how);
        UiHelpers.KeyUp(window, Key.Escape);
        Assert.Equal(ChatPanel.Context, c.ShownPanel);

        End(how, input);
        UiHelpers.Press(window, Key.Escape);                                   // a plain Esc closes it
        Assert.Equal(ChatPanel.None, c.ShownPanel);
        window.Close();
    }
}
