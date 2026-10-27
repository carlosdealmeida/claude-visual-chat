using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using ClaudeVisualChat.Protocol;
using Xunit;

namespace ClaudeVisualChat.Tests.Ui;

/// <summary>The dictation (<see cref="IChatDictation"/>) the host offers and the microphone of the touch layout's reply box.</summary>
public class DictationTests
{
    private sealed class FakeDictation : IChatDictation
    {
        private EventHandler? _changed;

        public bool IsAvailable { get; set; } = true;

        /// <summary>How many handlers listen: the box lets go of the host's object when it leaves the window.</summary>
        public int Subscribers { get; private set; }

        public event EventHandler? AvailabilityChanged
        {
            add { _changed += value; Subscribers++; }
            remove { _changed -= value; Subscribers--; }
        }

        public IProgress<string>? Partial { get; private set; }
        public CancellationToken Stop { get; private set; }
        public TaskCompletionSource<string?> Result { get; private set; } = new();
        public int Calls { get; private set; }
        public void Change(bool available) { IsAvailable = available; _changed?.Invoke(this, EventArgs.Empty); }
        public Task<string?> ListenAsync(IProgress<string>? partial, CancellationToken stop)
        { Calls++; Partial = partial; Stop = stop; Result = new(); return Result.Task; }
    }

    /// <summary>A host whose dictation fails the moment it is asked, instead of in the task it returns.</summary>
    private sealed class ThrowingDictation : IChatDictation
    {
        public bool IsAvailable => true;
        public event EventHandler? AvailabilityChanged { add { } remove { } }
        public Task<string?> ListenAsync(IProgress<string>? partial, CancellationToken stop) => throw new InvalidOperationException("no microphone");
    }

    private sealed class Opened
    {
        public Opened(IChatDictation? dictation, ChatConversation? conversation)
        {
            Conversation = conversation ?? TouchSamples.Opus();
            Conversation.MessageSubmitted += (_, e) => Sent.Add(e.Text);
            View = UiHelpers.TouchView(Conversation);
            if (dictation != null) View.Dictation = dictation;
            Window = UiHelpers.ShowPhone(View);
            Composer = UiHelpers.Composer(View);
            Input = UiHelpers.Input(View);
        }

        public ChatConversation Conversation { get; }
        public ClaudeChatView View { get; }
        public Window Window { get; }
        public ChatComposer Composer { get; }
        public TextBox Input { get; }
        public List<string> Sent { get; } = new List<string>();

        public List<string> Names => UiHelpers.ButtonNames(Composer);

        /// <summary>The microphone by the name it has now: "Ditar", or "Parar o ditado" while it listens.</summary>
        public Button Mic(string name = "Ditar") => UiHelpers.ButtonWithText(Composer, name);

        /// <summary>A finger on the microphone.</summary>
        public void Tap(string name = "Ditar") => UiHelpers.Click(Window, Mic(name));
    }

    private static Opened Open(IChatDictation? dictation = null, ChatConversation? conversation = null) => new Opened(dictation, conversation);

    private static T Part<T>(ChatComposer composer, string name) where T : Control =>
        composer.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    // ---- the brief ----

    [AvaloniaFact]
    public void Mic_shows_only_with_an_available_dictation_in_touch()
    {
        var ui = Open();                                                  // no dictation: no microphone
        Assert.DoesNotContain("Ditar", ui.Names);

        var fake = new FakeDictation { IsAvailable = false };
        ui.View.Dictation = fake;                                         // one that cannot start yet
        UiHelpers.Pump();
        Assert.DoesNotContain("Ditar", ui.Names);

        fake.Change(true);                                                // and then it can
        UiHelpers.Pump();
        Assert.Contains("Ditar", ui.Names);

        ui.View.Layout = ChatLayout.Desktop;                              // the desktop layout has no microphone, not even out of sight
        UiHelpers.Pump();
        Assert.DoesNotContain("Ditar", ui.Names);
        Assert.DoesNotContain(ui.Composer.GetVisualDescendants().OfType<Button>(), b => b.Name == "MicButton");

        ui.View.Layout = ChatLayout.Touch;
        ui.View.IsReadOnly = true;                                        // nor does a view that only shows the conversation
        UiHelpers.Pump();
        Assert.DoesNotContain("Ditar", ui.Names);
        Assert.False(Part<Button>(ui.Composer, "MicButton").IsVisible);   // it is the microphone that goes, not only the box around it
    }

    [AvaloniaFact]
    public void The_mic_needs_a_conversation_to_write_to()
    {
        var fake = new FakeDictation();
        var view = new ClaudeChatView { Layout = ChatLayout.Touch, Strings = ChatStrings.PortugueseBrazil, Dictation = fake };
        var window = UiHelpers.ShowPhone(view);
        var composer = UiHelpers.Composer(view);
        Assert.DoesNotContain("Ditar", UiHelpers.ButtonNames(composer));  // the box is not even enabled

        view.Conversation = TouchSamples.Opus();
        UiHelpers.Pump();
        Assert.Contains("Ditar", UiHelpers.ButtonNames(composer));
        UiHelpers.Click(window, UiHelpers.ButtonWithText(composer, "Ditar"));
        Assert.Equal(1, fake.Calls);

        view.Conversation = null;                                         // and it goes with the conversation, with a dictation under way
        UiHelpers.Pump();
        Assert.True(fake.Stop.IsCancellationRequested);
        Assert.False(UiHelpers.Input(view).IsReadOnly);
        Assert.DoesNotContain("Ditar", UiHelpers.ButtonNames(composer));
        Assert.DoesNotContain("Parar o ditado", UiHelpers.ButtonNames(composer));
    }

    [AvaloniaFact]
    public void Dictation_puts_the_words_at_the_caret_and_sends_nothing()
    {
        var fake = new FakeDictation();
        var ui = Open(fake);
        ui.Input.Text = "Run the tests";
        ui.Input.CaretIndex = 13;
        UiHelpers.Pump();

        ui.Tap();
        Assert.Equal(1, fake.Calls);
        Assert.True(ui.Input.IsReadOnly);                                 // the words come in; nothing else does
        Assert.Contains("Parar o ditado", ui.Names);
        Assert.DoesNotContain("Ditar", ui.Names);

        fake.Partial!.Report("and tell me");                              // what is heard so far replaces itself
        UiHelpers.Pump();
        Assert.Equal("Run the tests and tell me", ui.Input.Text);
        fake.Partial.Report("and tell me what");
        UiHelpers.Pump();
        Assert.Equal("Run the tests and tell me what", ui.Input.Text);

        fake.Result.SetResult("and tell me what failed");
        UiHelpers.Pump();
        Assert.Equal("Run the tests and tell me what failed", ui.Input.Text);
        Assert.False(ui.Input.IsReadOnly);
        Assert.Contains("Ditar", ui.Names);
        Assert.Empty(ui.Sent);                                            // the person sends it, when they want to
    }

    [AvaloniaFact]
    public void Second_tap_stops_and_keeps_what_was_heard()
    {
        var fake = new FakeDictation();
        var ui = Open(fake);
        ui.Tap();
        fake.Partial!.Report("hello");
        UiHelpers.Pump();
        Assert.False(fake.Stop.IsCancellationRequested);

        ui.Tap("Parar o ditado");
        Assert.True(fake.Stop.IsCancellationRequested);
        Assert.True(ui.Input.IsReadOnly);                                 // the host still has the last words to give

        fake.Result.SetResult("hello world");
        UiHelpers.Pump();
        Assert.Equal("hello world", ui.Input.Text);
        Assert.False(ui.Input.IsReadOnly);
        Assert.Contains("Ditar", ui.Names);
    }

    [AvaloniaFact]
    public void Silence_or_failure_inserts_nothing_and_unlocks_the_box()
    {
        var fake = new FakeDictation();
        var ui = Open(fake);
        ui.Input.Text = "keep this";
        UiHelpers.Pump();

        ui.Tap();
        Assert.True(ui.Input.IsReadOnly);
        fake.Result.SetResult(null);                                      // nothing was heard
        UiHelpers.Pump();
        Assert.Equal("keep this", ui.Input.Text);
        Assert.False(ui.Input.IsReadOnly);

        ui.Tap();                                                         // and again, with a host that fails
        Assert.Equal(2, fake.Calls);
        Assert.True(ui.Input.IsReadOnly);
        fake.Result.SetException(new InvalidOperationException());
        UiHelpers.Pump();
        Assert.Equal("keep this", ui.Input.Text);
        Assert.False(ui.Input.IsReadOnly);
        Assert.Contains("Ditar", ui.Names);
    }

    [AvaloniaFact]
    public void Switching_conversation_stops_the_dictation()
    {
        var a = TouchSamples.Opus();
        var b = new ChatConversation();
        var fake = new FakeDictation();
        var ui = Open(fake, a);
        ui.Input.Text = "first";
        UiHelpers.Pump();
        ui.Tap();
        fake.Partial!.Report("hello");
        UiHelpers.Pump();
        Assert.Equal("first hello", ui.Input.Text);

        ui.View.Conversation = b;                                         // the other conversation's box is free to write in
        UiHelpers.Pump();
        Assert.True(fake.Stop.IsCancellationRequested);
        Assert.False(ui.Input.IsReadOnly);
        Assert.Equal("", ui.Input.Text ?? "");
        Assert.Contains("Ditar", ui.Names);                               // and the microphone is ready for it

        fake.Partial.Report("hello world");                               // what the host heard last has nowhere to go, as a report…
        UiHelpers.Pump();
        Assert.Equal("", ui.Input.Text ?? "");
        fake.Result.SetResult("hello world");                             // …or as the result
        UiHelpers.Pump();
        Assert.Equal("", ui.Input.Text ?? "");
        Assert.Empty(ui.Sent);

        ui.View.Conversation = a;                                         // the words that were in are the draft of the first one
        UiHelpers.Pump();
        Assert.Equal("first hello", ui.Input.Text);
        Assert.False(ui.Input.IsReadOnly);
    }

    // ---- the controller's ruling: the caret, and the keyboard only if the box had it ----

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Dictation_gives_the_keyboard_back_to_the_box_only_if_the_box_had_it(bool boxHadIt)
    {
        var fake = new FakeDictation();
        var ui = Open(fake);
        ui.Input.Text = "Run the tests";
        ui.Input.CaretIndex = 13;
        if (boxHadIt) ui.Input.Focus();
        UiHelpers.Pump();
        Assert.Equal(boxHadIt, ui.Input.IsFocused);

        ui.Tap();                                                         // a finger on a button takes the keyboard from the box: the soft one goes down
        Assert.False(ui.Input.IsFocused);
        fake.Result.SetResult("and tell me what failed");
        UiHelpers.Pump();

        Assert.Equal("Run the tests and tell me what failed", ui.Input.Text);
        Assert.Equal(ui.Input.Text!.Length, ui.Input.CaretIndex);         // the caret after the words, either way
        Assert.Equal(boxHadIt, ui.Input.IsFocused);                       // the keyboard comes back if it was there, and does not come up if it was not
        if (!boxHadIt) Assert.NotSame(ui.Input, UiHelpers.Focused(ui.Window));
        else Assert.Same(ui.Input, UiHelpers.Focused(ui.Window));
    }

    [AvaloniaFact]
    public void Dictation_goes_at_the_caret_when_the_box_has_the_keyboard_and_at_the_end_when_it_has_not()
    {
        // the tap takes the keyboard from the box before it lands, yet the box had it: the words go at its caret, which ends after them
        var fake = new FakeDictation();
        var ui = Open(fake);
        ui.Input.Text = "Run the tests";
        ui.Input.Focus();
        ui.Input.CaretIndex = 3;
        UiHelpers.Pump();
        ui.Tap();
        fake.Partial!.Report("and tell me");
        UiHelpers.Pump();
        Assert.Equal("Run and tell me the tests", ui.Input.Text);
        fake.Result.SetResult("and tell me");
        UiHelpers.Pump();
        Assert.Equal("Run and tell me the tests", ui.Input.Text);
        Assert.Equal(15, ui.Input.CaretIndex);
        ui.Window.Close();

        // without it the caret is wherever it was left, which says nothing: the words go at the end
        var other = new FakeDictation();
        var second = Open(other);
        second.Input.Text = "Run the tests";
        second.Input.CaretIndex = 3;
        UiHelpers.Pump();
        second.Tap();
        other.Partial!.Report("and tell me");
        UiHelpers.Pump();
        Assert.Equal("Run the tests and tell me", second.Input.Text);
        second.Window.Close();
    }

    [AvaloniaFact]
    public void Dictation_does_not_take_the_keyboard_from_what_has_it_by_then()
    {
        var host = new TextBox();                                         // the host's own field, beside the view
        var fake = new FakeDictation();
        var view = UiHelpers.TouchView(TouchSamples.Opus());
        view.Dictation = fake;
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        Grid.SetRow(view, 1);
        grid.Children.Add(host);
        grid.Children.Add(view);
        var window = UiHelpers.ShowPhone(grid);
        var composer = UiHelpers.Composer(view);
        var input = UiHelpers.Input(view);
        input.Focus();
        UiHelpers.Pump();

        UiHelpers.Click(window, UiHelpers.ButtonWithText(composer, "Ditar"));
        host.Focus();                                                     // while it listens, the person goes elsewhere
        UiHelpers.Pump();
        fake.Result.SetResult("hello");
        UiHelpers.Pump();

        Assert.Equal("hello", input.Text);
        Assert.Same(host, UiHelpers.Focused(window));                     // and the end of the dictation leaves the keyboard where it went
        window.Close();
    }

    [AvaloniaFact]
    public void A_press_that_is_not_a_tap_leaves_nothing_for_the_next_activation()
    {
        var fake = new FakeDictation();
        var ui = Open(fake);
        ui.Input.Text = "Run the tests";
        ui.Input.Focus();
        UiHelpers.Pump();

        // a finger comes down on the microphone, which takes the keyboard, and slides off before it lets go: no tap
        var mic = ui.Mic();
        var onIt = mic.TranslatePoint(new Point(mic.Bounds.Width / 2, mic.Bounds.Height / 2), ui.Window)!.Value;
        var away = new Point(onIt.X, onIt.Y - 200);
        ui.Window.MouseMove(onIt, RawInputModifiers.None);
        ui.Window.MouseDown(onIt, MouseButton.Left, RawInputModifiers.None);
        ui.Window.MouseMove(away, RawInputModifiers.None);
        ui.Window.MouseUp(away, MouseButton.Left, RawInputModifiers.None);
        UiHelpers.Pump();
        Assert.Equal(0, fake.Calls);
        Assert.Same(mic, UiHelpers.Focused(ui.Window));

        UiHelpers.Press(ui.Window, Key.Space);                            // the microphone, pressed with the keyboard: the box did not have it then
        Assert.Equal(1, fake.Calls);
        fake.Result.SetResult("and tell me");
        UiHelpers.Pump();
        Assert.Equal("Run the tests and tell me", ui.Input.Text);
        Assert.False(ui.Input.IsFocused);
    }

    // ---- what the box does while it listens ----

    [AvaloniaFact]
    public void Nothing_is_sent_while_it_listens_and_the_end_of_the_dictation_sends_nothing()
    {
        var fake = new FakeDictation();
        var ui = Open(fake);
        ui.Input.Text = "Run the tests";
        UiHelpers.Pump();
        var send = Part<Button>(ui.Composer, "TouchSend");
        Assert.True(send.IsEnabled);

        ui.Tap();
        Assert.False(send.IsEnabled);                                     // what is in the box is not all of the message yet
        ui.Input.Focus();
        UiHelpers.Press(ui.Window, Key.Enter, RawInputModifiers.Control); // nor does the keyboard's key send it
        Assert.Empty(ui.Sent);
        Assert.Equal("Run the tests", ui.Input.Text);

        fake.Result.SetResult("and tell me");
        UiHelpers.Pump();
        Assert.Empty(ui.Sent);
        Assert.True(send.IsEnabled);
        Assert.Equal("Run the tests and tell me", ui.Input.Text);

        UiHelpers.Click(ui.Window, send);                                 // the person sends it, when they want to
        Assert.Equal(new[] { "Run the tests and tell me" }, ui.Sent);
    }

    [AvaloniaFact]
    public void A_failure_or_silence_after_some_words_keeps_the_last_ones()
    {
        var fake = new FakeDictation();
        var ui = Open(fake);
        ui.Input.Text = "Run";
        UiHelpers.Pump();

        ui.Tap();                                                         // the result is nothing, after the words were reported
        fake.Partial!.Report("the tests");
        UiHelpers.Pump();
        fake.Result.SetResult(null);
        UiHelpers.Pump();
        Assert.Equal("Run the tests", ui.Input.Text);
        Assert.False(ui.Input.IsReadOnly);

        ui.Tap();                                                         // the host fails
        fake.Partial!.Report("again");
        UiHelpers.Pump();
        fake.Result.SetException(new InvalidOperationException());
        UiHelpers.Pump();
        Assert.Equal("Run the tests again", ui.Input.Text);
        Assert.False(ui.Input.IsReadOnly);

        ui.Tap();                                                         // or gives back only blanks
        fake.Partial!.Report("please");
        UiHelpers.Pump();
        fake.Result.SetResult("  ");
        UiHelpers.Pump();
        Assert.Equal("Run the tests again please", ui.Input.Text);
        Assert.False(ui.Input.IsReadOnly);
        Assert.Empty(ui.Sent);
    }

    [AvaloniaFact]
    public void A_dictation_that_throws_when_asked_leaves_the_box_as_it_was()
    {
        var ui = Open(new ThrowingDictation());
        ui.Input.Text = "keep this";
        UiHelpers.Pump();

        ui.Tap();                                                         // no exception reaches the caller of the tap

        Assert.Equal("keep this", ui.Input.Text);
        Assert.False(ui.Input.IsReadOnly);
        Assert.Contains("Ditar", ui.Names);
    }

    /// <summary>
    /// Runs <paramref name="action"/> on a thread of its own and waits for it, as a host's callback comes (Task.Run(...).Wait() would
    /// run it on the calling thread, which is waiting: the task has not started yet, so the wait runs it itself).
    /// </summary>
    private static void OnAnotherThread(Action action)
    {
        Exception? failed = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                failed = e;
            }
        });
        thread.Start();
        thread.Join();
        if (failed != null) throw new InvalidOperationException("the host's thread failed: " + failed.Message, failed);
    }

    [AvaloniaFact]
    public void What_a_host_reports_from_another_thread_reaches_the_box_on_the_ui_thread()
    {
        var fake = new FakeDictation { IsAvailable = false };
        var ui = Open(fake);
        var uiThread = Environment.CurrentManagedThreadId;
        var offThread = new List<string>();                               // what the box and the microphone changed on, anywhere but the UI thread
        ui.Input.PropertyChanged += (_, e) => { if (Environment.CurrentManagedThreadId != uiThread) offThread.Add("box " + e.Property.Name); };
        Part<Button>(ui.Composer, "MicButton").PropertyChanged += (_, e) => { if (Environment.CurrentManagedThreadId != uiThread) offThread.Add("mic " + e.Property.Name); };

        OnAnotherThread(() => fake.Change(true));                         // a host's callbacks come from a thread of its own
        UiHelpers.PumpUntil(() => ui.Names.Contains("Ditar"));

        ui.Tap();
        OnAnotherThread(() => fake.Partial!.Report("from afar"));
        UiHelpers.PumpUntil(() => ui.Input.Text == "from afar");
        OnAnotherThread(() => fake.Result.SetResult("from afar, and then some"));
        UiHelpers.PumpUntil(() => !ui.Input.IsReadOnly);

        Assert.Equal("from afar, and then some", ui.Input.Text);
        Assert.Empty(offThread);
    }

    // ---- text that someone else puts in the box while it listens ----

    [AvaloniaFact]
    public void A_message_given_back_while_it_listens_stays_when_the_dictation_ends()
    {
        var fake = new FakeDictation();
        var c = TouchSamples.Opus();
        MessageSubmittedEventArgs? submitted = null;
        c.MessageSubmitted += (_, e) => submitted = e;
        var ui = Open(fake, c);
        ui.Input.Text = "first message";
        UiHelpers.Pump();
        UiHelpers.Click(ui.Window, Part<Button>(ui.Composer, "TouchSend"));
        Assert.Equal(new[] { "first message" }, ui.Sent);
        Assert.Equal("", ui.Input.Text ?? "");

        ui.Tap();                                                         // the person dictates the next one…
        Assert.True(c.UndoSubmit(submitted!));                            // …and the host gives the first one back: it did not get through
        UiHelpers.Pump();
        Assert.Equal("first message", ui.Input.Text);
        fake.Result.SetResult(null);                                      // the silence put the text of the tap back: an empty box
        UiHelpers.Pump();
        Assert.Equal("first message", ui.Input.Text);
        Assert.False(ui.Input.IsReadOnly);

        UiHelpers.Click(ui.Window, Part<Button>(ui.Composer, "TouchSend")); // and again, this time with words heard after it
        Assert.Equal("", ui.Input.Text ?? "");
        ui.Tap();
        Assert.True(c.UndoSubmit(submitted!));
        UiHelpers.Pump();
        fake.Partial!.Report("and this");
        UiHelpers.Pump();
        Assert.Equal("first message and this", ui.Input.Text);
        fake.Result.SetResult("and this too");
        UiHelpers.Pump();
        Assert.Equal("first message and this too", ui.Input.Text);
        Assert.Equal("first message and this too".Length, ui.Input.CaretIndex);
    }

    [AvaloniaFact]
    public void A_command_the_palette_puts_in_while_it_listens_stays_and_the_words_go_after_it()
    {
        var fake = new FakeDictation();
        var c = TouchSamples.Opus();
        c.Commands = new[] { new ChatCommand("review", "Review the changes", "") };
        var ui = Open(fake, c);
        ui.Tap();
        fake.Partial!.Report("the parser");
        UiHelpers.Pump();
        Assert.Equal("the parser", ui.Input.Text);

        ui.Composer.OpenCommands();                                       // the "Commands" row of the + sheet, and Tab puts the one found in
        UiHelpers.Pump();
        UiHelpers.Type(ui.Window, "rev");
        UiHelpers.Press(ui.Window, Key.Tab);
        Assert.Equal("/review ", ui.Input.Text);

        fake.Partial.Report("the parser please");                         // what was heard, and what comes, goes after the command
        UiHelpers.Pump();
        Assert.Equal("/review the parser please", ui.Input.Text);
        fake.Result.SetResult(null);
        UiHelpers.Pump();
        Assert.Equal("/review the parser please", ui.Input.Text);
        Assert.Empty(ui.Sent);
    }

    /// <summary>
    /// Whether <paramref name="target"/> can be reached from <paramref name="root"/> through the fields of the objects on the way (a
    /// delegate through its target), a few steps deep: what a host's object keeps alive.
    /// </summary>
    private static bool Reaches(object root, object target, int depth = 6)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        bool Walk(object? o, int left)
        {
            if (o == null) return false;
            if (ReferenceEquals(o, target)) return true;
            var type = o.GetType();
            if (left == 0 || type.IsPrimitive || type.IsEnum || o is string || o is Type || o is System.Reflection.MemberInfo || !seen.Add(o)) return false;
            if (o is Delegate d) return d.GetInvocationList().Any(x => Walk(x.Target, left - 1));
            for (var t = type; t != null; t = t.BaseType)
            {
                foreach (var f in t.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                {
                    if (f.FieldType.IsPointer || f.FieldType.IsByRefLike) continue;
                    if (Walk(f.GetValue(o), left - 1)) return true;
                }
            }
            return false;
        }
        return Walk(root, depth);
    }

    [AvaloniaFact]
    public void What_the_host_keeps_to_report_through_lets_go_of_the_view_when_the_listening_ends()
    {
        var fake = new FakeDictation();
        var ui = Open(fake);
        ui.Tap();
        var kept = fake.Partial!;
        Assert.True(Reaches(kept, ui.Composer));                          // while it listens, the reports reach the box
        fake.Result.SetResult("hello");
        UiHelpers.Pump();
        Assert.False(Reaches(kept, ui.Composer));                         // the host may keep it after the end: it holds nothing of the view's
        kept.Report("too late");
        UiHelpers.Pump();
        Assert.Equal("hello", ui.Input.Text);

        ui.Tap();                                                         // and the same when it is cut short, before the host gives back
        var cut = fake.Partial!;
        Assert.True(Reaches(cut, ui.Composer));
        ui.View.Layout = ChatLayout.Desktop;
        UiHelpers.Pump();
        Assert.False(Reaches(cut, ui.Composer));
    }

    // ---- what stops it ----

    [AvaloniaFact]
    public void Going_back_to_the_desktop_layout_stops_the_dictation()
    {
        var fake = new FakeDictation();
        var ui = Open(fake);
        ui.Tap();
        fake.Partial!.Report("hello");
        UiHelpers.Pump();

        ui.View.Layout = ChatLayout.Desktop;                              // the microphone that would stop it is gone
        UiHelpers.Pump();
        Assert.True(fake.Stop.IsCancellationRequested);
        Assert.False(ui.Input.IsReadOnly);
        Assert.Equal("hello", ui.Input.Text);

        ui.View.Layout = ChatLayout.Touch;
        UiHelpers.Pump();
        Assert.Contains("Ditar", ui.Names);                               // back to the phone's, ready, not listening
        fake.Result.SetResult("hello world");
        UiHelpers.Pump();
        Assert.Equal("hello", ui.Input.Text);
    }

    [AvaloniaFact]
    public void A_view_that_turns_read_only_stops_the_dictation_and_keeps_the_words()
    {
        var fake = new FakeDictation();
        var ui = Open(fake);
        ui.Input.Text = "first";
        UiHelpers.Pump();
        ui.Tap();
        fake.Partial!.Report("hello");
        UiHelpers.Pump();

        ui.View.IsReadOnly = true;
        UiHelpers.Pump();
        Assert.True(fake.Stop.IsCancellationRequested);
        Assert.False(ui.Input.IsReadOnly);
        fake.Result.SetResult("hello world");                             // the host's last words come too late
        UiHelpers.Pump();
        Assert.Equal("first hello", ui.Input.Text);

        ui.View.IsReadOnly = false;                                       // the box comes back with them, and the microphone is ready
        UiHelpers.Pump();
        Assert.Equal("first hello", ui.Input.Text);
        Assert.Contains("Ditar", ui.Names);
        Assert.Empty(ui.Sent);
    }

    [AvaloniaFact]
    public void A_hidden_reply_box_stops_the_dictation()
    {
        var fake = new FakeDictation();
        var ui = Open(fake);
        ui.Tap();
        fake.Partial!.Report("hello");
        UiHelpers.Pump();

        ui.Composer.IsVisible = false;
        UiHelpers.Pump();

        Assert.True(fake.Stop.IsCancellationRequested);
        Assert.False(ui.Input.IsReadOnly);
        Assert.Equal("hello", ui.Input.Text);
    }

    [AvaloniaFact]
    public void A_dictation_that_becomes_unavailable_or_goes_away_stops()
    {
        var fake = new FakeDictation();
        var ui = Open(fake);
        ui.Tap();
        fake.Partial!.Report("hello");
        UiHelpers.Pump();

        fake.Change(false);                                               // the language's model went away
        UiHelpers.Pump();
        Assert.True(fake.Stop.IsCancellationRequested);
        Assert.False(ui.Input.IsReadOnly);
        Assert.Equal("hello", ui.Input.Text);
        Assert.DoesNotContain("Ditar", ui.Names);
        Assert.DoesNotContain("Parar o ditado", ui.Names);

        fake.Change(true);
        UiHelpers.Pump();
        ui.Tap();
        var second = fake.Stop;
        Assert.False(second.IsCancellationRequested);
        ui.View.Dictation = null;                                         // or the host takes it back
        UiHelpers.Pump();
        Assert.True(second.IsCancellationRequested);
        Assert.False(ui.Input.IsReadOnly);
        Assert.DoesNotContain("Ditar", ui.Names);
        Assert.DoesNotContain("Parar o ditado", ui.Names);
    }

    [AvaloniaFact]
    public void A_dictation_that_is_replaced_is_let_go_of_and_the_new_one_is_followed()
    {
        var first = new FakeDictation();
        var second = new FakeDictation { IsAvailable = false };
        var ui = Open(first);
        Assert.Equal((1, 0), (first.Subscribers, second.Subscribers));

        ui.View.Dictation = second;                                       // the host swaps it for another, which cannot start yet
        UiHelpers.Pump();
        Assert.Equal((0, 1), (first.Subscribers, second.Subscribers));
        Assert.DoesNotContain("Ditar", ui.Names);

        second.Change(true);
        UiHelpers.Pump();
        ui.Tap();
        Assert.Equal((0, 1), (first.Calls, second.Calls));                // and it is the new one that listens
    }

    [AvaloniaFact]
    public void A_view_taken_out_of_the_window_stops_the_dictation_and_lets_go_of_the_host()
    {
        var fake = new FakeDictation();
        var ui = Open(fake);
        Assert.Equal(1, fake.Subscribers);
        ui.Tap();
        fake.Partial!.Report("hello");
        UiHelpers.Pump();

        ui.Window.Content = null;                                         // an object of the host's outlives the view: it must not hold it
        UiHelpers.Pump();
        Assert.True(fake.Stop.IsCancellationRequested);
        Assert.Equal(0, fake.Subscribers);
        fake.Change(false);                                               // the host goes on while the box is away

        ui.Window.Content = ui.View;
        UiHelpers.Pump();
        Assert.Equal(1, fake.Subscribers);
        Assert.False(ui.Input.IsReadOnly);
        Assert.Equal("hello", ui.Input.Text);
        Assert.DoesNotContain("Ditar", ui.Names);                         // back in the window, it knows the dictation cannot start now…
        fake.Change(true);
        UiHelpers.Pump();
        Assert.Contains("Ditar", ui.Names);                               // …and hears when it can
    }

    [AvaloniaFact]
    public void A_callback_of_the_host_that_throws_when_it_is_stopped_does_not_reach_the_tap()
    {
        var fake = new FakeDictation();
        var ui = Open(fake);
        ui.Tap();
        fake.Stop.Register(() => throw new InvalidOperationException("the recognizer is gone"));

        ui.Tap("Parar o ditado");                                         // the callback runs inside the cancel: no exception comes out of the tap

        Assert.True(fake.Stop.IsCancellationRequested);
        fake.Result.SetResult("hello");
        UiHelpers.Pump();
        Assert.Equal("hello", ui.Input.Text);
        Assert.False(ui.Input.IsReadOnly);
    }

    // ---- how it looks and reads ----

    [AvaloniaFact]
    public void The_mic_is_a_target_of_44_and_the_accent_fills_it_while_it_listens()
    {
        var fake = new FakeDictation();
        var ui = Open(fake);
        Border Face(Button b) => b.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "PART_TouchFace");

        var mic = ui.Mic();
        Assert.True(mic.Bounds.Width >= 44 && mic.Bounds.Height >= 44, mic.Bounds.ToString());
        Assert.Equal(new Rect(3, 3, 38, 38), Face(mic).Bounds);           // the face of the other buttons of the bar
        Assert.Same(ui.View.FindResource("ClaudeChat.SheetGroupBackground"), Face(mic).Background);
        Assert.DoesNotContain("listening", mic.Classes);

        ui.Tap();
        var listening = ui.Mic("Parar o ditado");
        Assert.Same(mic, listening);
        Assert.Contains("listening", listening.Classes);
        Assert.True(listening.Bounds.Width >= 44 && listening.Bounds.Height >= 44);
        Assert.Same(ui.View.FindResource("ClaudeChat.AccentFill"), Face(listening).Background);
        Assert.Same(ui.View.FindResource("ClaudeChat.AccentForeground"), listening.Foreground);

        fake.Result.SetResult(null);                                      // and the end puts it back as it was
        UiHelpers.Pump();
        Assert.DoesNotContain("listening", mic.Classes);
        Assert.Same(ui.View.FindResource("ClaudeChat.SheetGroupBackground"), Face(mic).Background);
    }

    [AvaloniaFact]
    public void The_mic_is_named_in_the_language_of_the_view()
    {
        var ui = Open(new FakeDictation());
        Assert.Contains("Ditar", ui.Names);

        ui.View.Strings = ChatStrings.English;
        UiHelpers.Pump();
        Assert.Contains("Dictate", ui.Names);
        ui.Tap("Dictate");
        Assert.Contains("Stop dictation", ui.Names);

        ui.View.Strings = ChatStrings.PortugueseBrazil;                   // the name follows the language while it listens too
        UiHelpers.Pump();
        Assert.Contains("Parar o ditado", ui.Names);
    }

    [Fact]
    public void The_mic_texts_in_both_languages()
    {
        Assert.Equal(("Dictate", "Stop dictation"), (ChatStrings.English.Dictate, ChatStrings.English.StopDictation));
        Assert.Equal(("Ditar", "Parar o ditado"), (ChatStrings.PortugueseBrazil.Dictate, ChatStrings.PortugueseBrazil.StopDictation));
    }

    [AvaloniaFact]
    public void The_bar_fits_the_phone_with_the_mic_and_everything_else_on()
    {
        var c = TouchSamples.Opus();
        c.Apply(new SessionStarted(null, "claude-fable-5-1", null, null, null));   // a longer name, and a longer level
        c.Apply(new SettingsApplied(null, "xhigh", true));
        c.Apply(new ContextUsageReported(TouchSamples.Context));
        c.Submit("x");                                                    // busy, with something written: ring, microphone, send and stop
        var ui = Open(new FakeDictation(), c);
        ui.Input.Text = "and this";
        UiHelpers.Pump();
        var bar = Part<Border>(ui.Composer, "TouchBar");
        var buttons = bar.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).ToList();
        Assert.Equal(new[] { "TouchAddButton", "TouchModelChip", "TouchRing", "MicButton", "TouchSend", "TouchStop" }, buttons.Select(b => b.Name));
        Assert.True(buttons.Last().TranslatePoint(new Point(buttons.Last().Bounds.Width, 0), bar)!.Value.X <= bar.Bounds.Width + 0.5);
        for (var i = 1; i < buttons.Count; i++)                           // none on another
        {
            var before = buttons[i - 1].TranslatePoint(new Point(buttons[i - 1].Bounds.Width, 0), bar)!.Value.X;
            Assert.True(before <= buttons[i].TranslatePoint(default, bar)!.Value.X + 0.5, buttons[i].Name);
        }
        Assert.All(buttons, b => Assert.True(b.Bounds.Width >= 44 && b.Bounds.Height >= 44, b.Name));
    }
}
