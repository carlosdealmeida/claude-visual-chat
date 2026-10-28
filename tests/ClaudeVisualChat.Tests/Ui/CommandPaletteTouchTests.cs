using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using Xunit;

namespace ClaudeVisualChat.Tests.Ui;

/// <summary>
/// What a press on a row of the command palette does. The desktop layout runs the command at the press, as the extension's menu
/// does. The touch layout runs it on a tap (the pointer released where it was pressed, which is Avalonia's <c>Tapped</c>), never at
/// the press: a finger that starts to scroll the list, which has a ceiling of 300 px, must not run the command under it.
/// </summary>
public class CommandPaletteTouchTests
{
    private sealed class Opened
    {
        public Opened(bool touch)
        {
            Conversation = TouchSamples.Opus();
            Conversation.Commands = Enumerable.Range(1, 20).Select(i => new ChatCommand("cmd" + i, "Command number " + i, "")).ToArray();
            Conversation.MessageSubmitted += (_, e) => Sent.Add(e.Text);
            Conversation.CommandInvoked += (_, e) => Invoked.Add(e.Name);
            View = touch ? UiHelpers.TouchView(Conversation) : new ClaudeChatView { Conversation = Conversation };
            Window = touch ? UiHelpers.ShowPhone(View) : UiHelpers.Show(View);
            Composer = UiHelpers.Composer(View);
            Palette = Composer.GetLogicalDescendants().OfType<CommandPalette>().Single();
        }

        public ChatConversation Conversation { get; }
        public ClaudeChatView View { get; }
        public Window Window { get; }
        public ChatComposer Composer { get; }
        public CommandPalette Palette { get; }
        public List<string> Sent { get; } = new List<string>();
        public List<string> Invoked { get; } = new List<string>();

        /// <summary>A "/" typed in the empty box opens the palette without a filter field: scrolling is the only way to the later commands.</summary>
        public void OpenTyped()
        {
            UiHelpers.Input(View).Focus();
            UiHelpers.Type(Window, "/");
            Assert.True(Palette.IsOpen);
        }

        public List<Border> Rows => Palette.Child!.GetVisualDescendants().OfType<Border>()
            .Where(b => b.IsEffectivelyVisible && b.Classes.Contains("ClaudeChatMenuItem")).ToList();

        public ScrollViewer List => Palette.Child!.GetVisualDescendants().OfType<ScrollViewer>().First();

        public Border Row(string label) => Rows.Single(r => UiHelpers.Texts(r).Single() == label);

        /// <summary>A point of the row, <paramref name="down"/> px under its middle, in the window's coordinates.</summary>
        public Point At(Border row, double down = 0) =>
            row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2 + down), Window)!.Value;

        /// <summary>
        /// The middle of the row, once the screen shows the list where it is, for after the list has moved. What is under a point is
        /// found in the last frame drawn, and the headless window draws one only when asked (a phone draws them all the time): frames
        /// are drawn until the row is under its own middle, and was in the same place a frame before. The middle is read again at each
        /// frame, so a list that is still settling is followed, where a point read once would be left behind.
        /// </summary>
        public Point DrawFramesUntilUnder(Border row)
        {
            Point? before = null;
            var at = default(Point);
            UiHelpers.PumpUntil(() =>
            {
                UiHelpers.DrawFrames(1);
                at = At(row);
                var still = before == at;
                before = at;
                return still && Window.InputHitTest(at) is Visual hit && (ReferenceEquals(hit, row) || row.IsVisualAncestorOf(hit));
            });
            return at;
        }

        public void Press(Point at)
        {
            Window.MouseMove(at, RawInputModifiers.None);
            Window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
            UiHelpers.Pump();
        }

        public void Move(Point to)
        {
            Window.MouseMove(to, RawInputModifiers.LeftMouseButton);
            UiHelpers.Pump();
        }

        public void Release(Point at)
        {
            Window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
            UiHelpers.Pump();
        }
    }

    /// <summary>
    /// A finger on the window, through the raw touch input of the headless platform (the pointer of a phone: a tap size of 10 px, a
    /// list that starts to scroll after 30). Avalonia 11's reference assembly hides the types that carry it, which are there at run
    /// time, so they are reached by reflection (those of 11.0.10: another version may move them).
    /// <para>
    /// Its events are stamped by a clock of its own, 16 ms apart (a screen reports about as often), and not by the machine's: a drag
    /// stamped by that clock gets a few distinct times, which depend on how fast the machine ran it, and Avalonia reads a velocity
    /// from them when it has three. A finger that lifts while it moves at a speed is a fling, and the list goes on by itself after
    /// it, under the point where a tap was meant to land. A finger that rests a moment before it lifts (<see cref="Rest"/>) is not.
    /// </para>
    /// </summary>
    private sealed class Finger
    {
        private readonly Window _window;
        private readonly object _device = Activator.CreateInstance(typeof(TouchDevice))!;
        private ulong _now = 1_000;

        public Finger(Window window) => _window = window;

        /// <summary>The finger lands on the screen as it is drawn: what is under it is found in the last frame.</summary>
        public void Down(Point at)
        {
            UiHelpers.DrawFrames();
            Send(RawPointerEventType.TouchBegin, at);
        }

        public void Move(Point to) => Send(RawPointerEventType.TouchUpdate, to);

        /// <summary>The finger stays where it is for a moment (more than the 200 ms Avalonia takes for a lift in motion): it does not fling.</summary>
        public void Rest() => _now += 250;

        public void Up(Point at) => Send(RawPointerEventType.TouchEnd, at);

        private void Send(RawPointerEventType type, Point at)
        {
            _now += 16;
            var window = _window.PlatformImpl!;
            var input = (Action<RawInputEventArgs>)window.GetType().GetProperty("Input")!.GetValue(window)!;
            input((RawInputEventArgs)Activator.CreateInstance(
                typeof(RawTouchEventArgs), _device, _now, _window, type, at, RawInputModifiers.None, 1L)!);
            UiHelpers.Pump();
        }
    }

    [AvaloniaFact]
    public void Touch_a_press_on_a_row_runs_nothing_and_the_tap_that_ends_it_runs_the_command()
    {
        var ui = new Opened(touch: true);
        ui.OpenTyped();
        var at = ui.At(ui.Row("/cmd3"));

        ui.Press(at);
        Assert.Empty(ui.Sent);                                           // never on the press
        Assert.True(ui.Palette.IsOpen);
        ui.Release(at);
        Assert.Equal(new[] { "/cmd3" }, ui.Sent);                        // on the tap
        Assert.False(ui.Palette.IsOpen);
        Assert.Equal("", UiHelpers.Input(ui.View).Text ?? "");           // what was typed to find it goes
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Touch_the_palette_with_its_filter_field_and_a_command_of_the_conversation_run_on_the_tap_too()
    {
        var ui = new Opened(touch: true);
        ui.Conversation.Commands = new[] { new ChatCommand("cmd2", "Command number 2", "") };   // a short list: every row in sight
        ui.Composer.OpenCommands();                                      // the "Commands" row of the "+" sheet opens it with a filter field
        UiHelpers.Pump();
        UiHelpers.Type(ui.Window, "cmd2");                              // the slash commands come once something is typed in the field
        var at = ui.DrawFramesUntilUnder(ui.Row("/cmd2"));
        ui.Press(at);
        Assert.Empty(ui.Sent);
        Assert.True(ui.Palette.IsOpen);
        ui.Release(at);
        Assert.Equal(new[] { "/cmd2" }, ui.Sent);
        Assert.False(ui.Palette.IsOpen);

        ui.Composer.OpenCommands();                                      // the conversation's own commands are run, not sent
        UiHelpers.Pump();
        UiHelpers.Type(ui.Window, "context");
        ui.DrawFramesUntilUnder(ui.Row("/context"));
        var usage = ui.At(ui.Row("/context"), 12);                       // a little lower than the tap before: at the same point so soon it would be a double click
        ui.Press(usage);
        Assert.Empty(ui.Invoked);
        ui.Release(usage);
        Assert.Equal(new[] { "context" }, ui.Invoked);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Touch_a_press_that_moves_past_the_tap_threshold_runs_nothing()
    {
        var ui = new Opened(touch: true);
        ui.OpenTyped();
        var row = ui.Row("/cmd3");
        var (start, end) = (ui.At(row, -9), ui.At(row, 9));
        var (from, to, tap) = (ui.At(row), ui.At(ui.Row("/cmd5")), ui.At(ui.Row("/cmd4")));

        // still on the same row, but 18 px from where it was pressed: a drag, not a tap
        ui.Press(start);
        Assert.Empty(ui.Sent);
        ui.Move(end);
        ui.Release(end);
        Assert.Empty(ui.Sent);
        Assert.True(ui.Palette.IsOpen);

        // dragged on to another row
        ui.Press(from);
        ui.Move(to);
        ui.Release(to);
        Assert.Empty(ui.Sent);
        Assert.Empty(ui.Invoked);
        Assert.True(ui.Palette.IsOpen);

        // and a tap right after still runs its command: the palette was left as it was
        ui.Press(tap);
        ui.Release(tap);
        Assert.Equal(new[] { "/cmd4" }, ui.Sent);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Touch_scrolling_the_list_under_a_press_runs_nothing()
    {
        var ui = new Opened(touch: true);
        ui.OpenTyped();
        var list = ui.List;
        Assert.True(list.Extent.Height > list.Viewport.Height);          // 25 rows of 44 px under a ceiling of 300
        Assert.Equal(0, list.Offset.Y);
        var at = ui.At(ui.Row("/cmd2"));

        ui.Press(at);
        Assert.Empty(ui.Sent);
        ui.Window.MouseWheel(at, new Vector(0, -3), RawInputModifiers.None);   // the list goes up under the pointer
        UiHelpers.Pump();
        Assert.True(list.Offset.Y > 0);
        ui.Release(at);
        Assert.Empty(ui.Sent);
        Assert.Empty(ui.Invoked);
        Assert.True(ui.Palette.IsOpen);                                  // still there to pick from, in its new place
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Touch_a_finger_that_scrolls_the_list_runs_nothing_and_a_tap_of_the_finger_runs_its_command()
    {
        var ui = new Opened(touch: true);
        ui.OpenTyped();
        var finger = new Finger(ui.Window);
        var list = ui.List;
        var at = ui.At(ui.Row("/cmd3"));

        // a drag of 20 px is past the tap threshold (10 px) and short of the scroll (30 px): neither a tap nor a scroll
        finger.Down(at);
        Assert.Empty(ui.Sent);                                           // never on the press
        for (var step = 1; step <= 4; step++) finger.Move(new Point(at.X, at.Y - 5 * step));
        finger.Up(new Point(at.X, at.Y - 20));
        Assert.Empty(ui.Sent);
        Assert.Equal(0, list.Offset.Y);
        Assert.True(ui.Palette.IsOpen);

        // a drag of 80 px scrolls the list, as a finger does on a phone, and the finger lifted from it taps nothing (it rests a
        // moment before it lifts: a lift in motion is a fling, which keeps the list going and takes the rows from under a later tap)
        finger.Down(at);
        for (var step = 1; step <= 8; step++) finger.Move(new Point(at.X, at.Y - 10 * step));
        Assert.True(list.Offset.Y > 0);                                  // the list went with the finger
        Assert.Empty(ui.Sent);
        finger.Rest();
        finger.Up(new Point(at.X, at.Y - 80));
        Assert.Empty(ui.Sent);
        Assert.Empty(ui.Invoked);
        Assert.True(ui.Palette.IsOpen);

        // a tap, with the list where the scroll left it: the command under the finger, on the lift
        var tap = ui.DrawFramesUntilUnder(ui.Row("/cmd6"));
        finger.Down(tap);
        Assert.Empty(ui.Sent);
        Assert.True(ui.Palette.IsOpen);
        finger.Up(tap);
        Assert.Equal(new[] { "/cmd6" }, ui.Sent);
        Assert.False(ui.Palette.IsOpen);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Desktop_a_row_runs_its_command_at_the_press_as_before()
    {
        var ui = new Opened(touch: false);
        ui.OpenTyped();
        var at = ui.At(ui.Row("/cmd3"));

        ui.Press(at);
        Assert.Equal(new[] { "/cmd3" }, ui.Sent);                        // at the press, before the button is let go
        Assert.False(ui.Palette.IsOpen);
        ui.Release(at);
        Assert.Equal(new[] { "/cmd3" }, ui.Sent);                        // and only once
        ui.Window.Close();
    }
}
