using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using ClaudeVisualChat.Protocol;
using Xunit;

namespace ClaudeVisualChat.Tests.Ui;

/// <summary>
/// The reply box's bar at every width: wide, everything on one line as it always was; narrower, the pieces that do not fit go to
/// the lines under it in a fixed order instead of being drawn over one another; and narrower still, nothing throws. Every check
/// compares the pieces with one another, the bar and their own sizes, never with a width of text: the fonts differ between
/// machines, and so do the widths at which a piece goes down.
/// </summary>
public class ComposerWidthTests
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.Zero);

    private static string Call(string id, string description) =>
        "{\"type\":\"assistant\",\"message\":{\"id\":\"msg_" + id + "\",\"type\":\"message\",\"role\":\"assistant\"," +
        "\"content\":[{\"type\":\"tool_use\",\"id\":\"" + id + "\",\"name\":\"Agent\",\"input\":{\"description\":\"" + description + "\"," +
        "\"subagent_type\":\"general-purpose\"}}]},\"parent_tool_use_id\":null,\"session_id\":\"s1\"}";

    /// <summary>A model with a long name, so that the chip is wide: "Opus 5.5 Extended Preview High".</summary>
    private static readonly ChatModel LongModel =
        new ChatModel("opus-preview", "claude-opus-5-5-preview", "Opus 5.5 Extended Preview", "", new[] { "low", "high" });

    /// <summary>A longer name for the mode the box is in.</summary>
    private static readonly ChatStrings LongMode = ChatStrings.English with { ModeAcceptEdits = "Edit files automatically" };

    /// <summary>The order in which the pieces leave the first line: what is needed least goes first.</summary>
    private static readonly string[] Order = { "clock", "remote", "agents", "ring", "model", "mode" };

    /// <summary>
    /// The view at <paramref name="width"/> with every piece of the bar on screen (Remote Control only when
    /// <paramref name="remote"/>): the context ring, the cache clock of an hour's cache ("60m"), Remote Control, the agents chip,
    /// the model with a long name, the mode with a long name, and send and stop side by side. The conversation's time is
    /// <see cref="Now"/>.
    /// </summary>
    private sealed class Crowded
    {
        public Crowded(double width, bool remote = true, double? composerMaxWidth = null, string? font = null, double? smallFontSize = null)
        {
            Conversation = new ChatConversation { Clock = () => Now, AvailableModels = new[] { LongModel } };
            Conversation.Apply(new SessionStarted("s1", LongModel.Value, null, "acceptEdits", "2.1.310"));
            Conversation.Apply(new SettingsApplied(null, "high", true));
            Conversation.Apply(new PromptCacheUsed(TimeSpan.FromHours(1), null));
            Conversation.Apply(new ContextUsageReported(new ContextUsage(800, 1000, 80, Array.Empty<ContextCategory>())));
            if (remote) Conversation.RemoteControlStatus = ChatRemoteControlStatus.Connected;
            Conversation.AppendLine(Call("toolu_a", "Look around the repository"));
            Conversation.AppendLine(Call("toolu_b", "Write the missing tests"));
            View = new ClaudeChatView { Conversation = Conversation, Strings = LongMode };
            if (composerMaxWidth is double most) View.Resources["ClaudeChat.ComposerMaxWidth"] = most; // room for any font
            if (font != null) View.Resources["ClaudeChat.FontFamily"] = new Avalonia.Media.FontFamily(font); // another machine's font
            if (smallFontSize is double size) View.Resources["ClaudeChat.SmallFontSize"] = size; // the bar's texts, the clock's minutes
            Window = UiHelpers.Show(View, width, UiHelpers.Height);
            Composer = UiHelpers.Composer(View);
            Conversation.Submit("go"); // busy: stop shows
            UiHelpers.Input(View).Text = "and then this"; // and send next to it
            UiHelpers.Pump();
        }

        public DateTimeOffset Now { get; set; } = Start;
        public ChatConversation Conversation { get; }
        public ClaudeChatView View { get; }
        public Window Window { get; }
        public ChatComposer Composer { get; }

        public Border Box => Composer.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("ClaudeChatComposer"));

        public ComposerBarPanel Bar => Composer.GetVisualDescendants().OfType<ComposerBarPanel>().Single();

        public PromptCacheClock Clock => Composer.GetVisualDescendants().OfType<PromptCacheClock>().Single();

        public Button Add => Named<Button>("AddButton");
        public Button Commands => Named<Button>("CommandsButton");
        public Button Send => Named<Button>("SendButton");
        public Button Stop => Named<Button>("StopButton");

        private T Named<T>(string name) where T : Control =>
            Composer.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

        /// <summary>Every piece of the desktop bar, by name, in the bar's order from left to right.</summary>
        public IReadOnlyList<(string Name, Control Control)> Pieces => new (string, Control)[]
        {
            ("add", Add),
            ("commands", Commands),
            ("ring", Composer.GetVisualDescendants().OfType<ContextRing>().Single(r => r.Name == "ContextRing")),
            ("clock", Clock),
            ("remote", Composer.GetVisualDescendants().OfType<RemoteControlChip>().Single()),
            ("agents", Composer.GetVisualDescendants().OfType<AgentsChip>().Single()),
            ("model", Composer.GetVisualDescendants().OfType<ModelChip>().Single(m => m.Name == "ModelChip")),
            ("mode", Composer.GetVisualDescendants().OfType<ComboBox>().Single()),
            ("send", Send),
            ("stop", Stop),
        };

        public Control Piece(string name) => Pieces.Single(p => p.Name == name).Control;

        /// <summary>The pieces on screen, with where they are in the reply box (their margins left out).</summary>
        public List<(string Name, Rect Bounds)> Shown() =>
            Pieces.Where(p => p.Control.IsEffectivelyVisible && p.Control.Bounds.Width > 0)
                .Select(p => (p.Name, BoundsIn(p.Control, Composer)))
                .ToList();

        /// <summary>The pieces under the first line (the line of +), in the bar's order.</summary>
        public List<string> Below()
        {
            var first = BoundsIn(Add, Composer);
            return Shown().Where(s => s.Bounds.Top >= first.Bottom).Select(s => s.Name).ToList();
        }

        public void Resize(double width)
        {
            Window.Width = width;
            UiHelpers.Pump();
        }

        /// <summary>The time moves and the clock's second goes by.</summary>
        public void At(TimeSpan sinceStart)
        {
            Now = Start + sinceStart;
            Clock.Tick();
            UiHelpers.Pump();
        }
    }

    private static Rect BoundsIn(Control control, Visual root) =>
        new Rect(control.TranslatePoint(default, root)!.Value, control.Bounds.Size);

    /// <summary>No two pieces of the bar on screen share a pixel.</summary>
    private static void AssertNoOverlap(List<(string Name, Rect Bounds)> shown)
    {
        for (var i = 0; i < shown.Count; i++)
        {
            for (var j = i + 1; j < shown.Count; j++)
            {
                var (a, b) = (shown[i], shown[j]);
                Assert.False(a.Bounds.Intersects(b.Bounds), a.Name + " " + a.Bounds + " overlaps " + b.Name + " " + b.Bounds);
            }
        }
    }

    private static void AssertInside(Crowded ui, string name, Rect bounds)
    {
        var box = BoundsIn(ui.Box, ui.Composer).Deflate(1); // inside the frame
        Assert.True(box.Contains(bounds), name + " " + bounds + " is not inside the box " + box);
    }

    /// <summary>A click in the middle of the button lands on it, not on something drawn over it.</summary>
    private static void AssertClickable(Crowded ui, Button button)
    {
        var center = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), ui.Window)!.Value;
        var hit = UiHelpers.HitTest(ui.Window, center) as Visual;
        Assert.NotNull(hit);
        Assert.True(ReferenceEquals(hit, button) || hit!.GetVisualAncestors().Contains(button), button.Name + " is covered by " + hit);
    }

    /// <summary>The piece is given at least the width it asked for.</summary>
    private static void AssertWhole(string name, Control control) =>
        Assert.True(control.Bounds.Width >= control.DesiredSize.Width - control.Margin.Left - control.Margin.Right - 0.5, name + " is squeezed");

    [AvaloniaFact]
    public void Where_everything_fits_the_bar_is_one_line_laid_out_as_the_dock_panel_it_was()
    {
        // a box wide enough for any font: every piece on the first line, where the dock panel and its stack panel put it
        var ui = new Crowded(1600, composerMaxWidth: 1500);
        Assert.Empty(ui.Below());
        var bar = BoundsIn(ui.Bar, ui.Composer);
        var pieces = ui.Pieces.Select(p => p.Control).ToList();
        foreach (var (name, control) in ui.Pieces)
        {
            Assert.True(control.IsEffectivelyVisible, name + " is not on screen");
            AssertWhole(name, control);
        }

        // one line: as high as its highest piece, and every piece centered on it
        var height = pieces.Max(p => p.DesiredSize.Height);
        Assert.Equal(height, bar.Height, 1);
        foreach (var (name, control) in ui.Pieces)
        {
            var bounds = BoundsIn(control, ui.Composer);
            Assert.True(Math.Abs(bar.Y + (height - bounds.Height) / 2 - bounds.Y) <= 0.5, name + " is not centered on the line: " + bounds);
        }

        // at the left, + at the bar's start and each piece 2 px after the one before it, with their margins
        var x = bar.X;
        foreach (var name in new[] { "add", "commands", "ring", "clock", "remote", "agents", "model" })
        {
            var control = ui.Piece(name);
            var bounds = BoundsIn(control, ui.Composer);
            Assert.True(Math.Abs(x + control.Margin.Left - bounds.X) <= 0.5, name + " at " + bounds.X + ", expected " + (x + control.Margin.Left));
            x = bounds.Right + control.Margin.Right + 2;
        }

        // at the right, stop against the bar's end, send 2 px before it, and the mode against the 4 px before send
        var stack = (StackPanel)ui.Send.Parent!;
        var stop = BoundsIn(ui.Stop, ui.Composer);
        var send = BoundsIn(ui.Send, ui.Composer);
        var mode = BoundsIn(ui.Piece("mode"), ui.Composer);
        Assert.Equal(bar.Right, stop.Right, 1);
        Assert.Equal(send.Right + 2, stop.X, 1);
        Assert.Equal(4, stack.Margin.Left);
        Assert.Equal(send.X - stack.Margin.Left, mode.Right + ui.Piece("mode").Margin.Right, 1);
        Assert.True(mode.X > x, "the mode " + mode + " runs into the model, which ends at " + x);
        AssertNoOverlap(ui.Shown());
        ui.Window.Close();
    }

    /// <summary>The widths of the narrow chat, each with the theme's font and with fonts much wider and narrower.</summary>
    public static IEnumerable<object?[]> NarrowWidths =>
        from width in new[] { 350.0, 320.0 }
        from font in new[] { null, "Verdana", "Arial Narrow, Calibri", "Courier New" }
        select new object?[] { width, font };

    [AvaloniaTheory]
    [MemberData(nameof(NarrowWidths))]
    public void At_a_narrow_width_nothing_in_the_bar_overlaps_and_everything_is_still_there(double width, string? font)
    {
        var ui = new Crowded(width, font: font);
        var shown = ui.Shown();

        // every piece is still on screen, each one whole and inside the box
        Assert.Equal(ui.Pieces.Select(p => p.Name), shown.Select(s => s.Name));
        foreach (var (name, control) in ui.Pieces) AssertWhole(name, control);
        AssertNoOverlap(shown);
        foreach (var (name, bounds) in shown) AssertInside(ui, name, bounds);
        foreach (var button in new[] { ui.Add, ui.Commands, ui.Send, ui.Stop }) AssertClickable(ui, button);

        // +, / and send and stop stay on the first line, at its ends
        var bar = BoundsIn(ui.Bar, ui.Composer);
        var add = BoundsIn(ui.Add, ui.Composer);
        Assert.Equal(bar.X, add.X, 1);
        foreach (var name in new[] { "commands", "send", "stop" }) Assert.Equal(add.Center.Y, BoundsIn(ui.Piece(name), ui.Composer).Center.Y, 1);
        Assert.Equal(bar.Right, BoundsIn(ui.Stop, ui.Composer).Right, 1);
        ui.Window.Close();
    }

    [AvaloniaTheory]
    [InlineData(null)]
    [InlineData("Verdana")]
    [InlineData("Courier New")]
    public void The_pieces_go_down_in_the_order_of_what_matters_least_and_never_come_back_as_the_chat_narrows(string? font)
    {
        // at every width, the pieces under the first line are the first ones of the order, and narrowing never brings one back up
        var ui = new Crowded(1000, font: font);
        var before = new List<string>();
        for (var width = 1000; width >= 200; width -= 5)
        {
            ui.Resize(width);
            var shown = ui.Shown();
            AssertNoOverlap(shown);
            var below = ui.Below();
            foreach (var essential in new[] { "add", "commands", "send", "stop" }) Assert.DoesNotContain(essential, below);
            Assert.Equal(Order.Take(below.Count), Order.Where(below.Contains)); // a prefix of the order
            Assert.True(before.All(below.Contains), "at " + width + " px " + string.Join(", ", before.Except(below)) + " came back up");
            before = below;
        }
        // at 200 px the bar has 156 px inside it: +, / and send and stop take 112, which leaves less than the mode's icon and padding
        // with any text after them, so every piece has gone down
        Assert.Equal(Order, Order.Where(before.Contains));
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_piece_that_comes_and_goes_in_a_narrow_chat_moves_the_others_and_leaves_the_bar_as_it_was()
    {
        var ui = new Crowded(320, remote: false);
        var remote = ui.Piece("remote");
        Assert.False(remote.IsVisible);
        AssertNoOverlap(ui.Shown());
        var height = ui.Composer.Bounds.Height;
        var below = ui.Below();

        ui.Conversation.RemoteControlStatus = ChatRemoteControlStatus.Connecting; // the chip comes
        UiHelpers.Pump();
        Assert.True(remote.IsEffectivelyVisible);
        AssertNoOverlap(ui.Shown());
        foreach (var (name, bounds) in ui.Shown()) AssertInside(ui, name, bounds);

        ui.Conversation.RemoteControlStatus = ChatRemoteControlStatus.Connected;
        UiHelpers.Pump();
        AssertNoOverlap(ui.Shown());

        ui.Conversation.RemoteControlStatus = ChatRemoteControlStatus.Off; // and goes
        UiHelpers.Pump();
        Assert.False(remote.IsVisible);
        AssertNoOverlap(ui.Shown());
        Assert.Equal(height, ui.Composer.Bounds.Height);
        Assert.Equal(below, ui.Below());
        ui.Window.Close();
    }

    /// <summary>
    /// The fonts of other machines, and sizes of the bar's texts with fractions in them: the sans-serif of a Linux runner falls
    /// back from the theme's Segoe UI to a font of other widths, and the line breaks must hold with any of them.
    /// </summary>
    public static IEnumerable<object?[]> Fonts => new[]
    {
        new object?[] { null, null },               // the theme's own
        new object?[] { "Verdana", null },          // much wider
        new object?[] { "Arial Narrow, Calibri", null }, // much narrower
        new object?[] { "Courier New", null },      // every character as wide
        new object?[] { null, 11.7 },               // a size with a fraction
        new object?[] { "Verdana", 12.35 },
        new object?[] { "Times New Roman", 10.4 },
    };

    [AvaloniaTheory]
    [MemberData(nameof(Fonts))]
    public void The_minutes_of_the_cache_clock_going_by_never_move_the_line_breaks(string? font, double? size)
    {
        // an hour's cache shows "60m", then "9m", then, cold, only its icon: the clock's width changes each time, and at no width
        // may that move a piece between the lines (the box would change height under the person's eyes every minute)
        var ui = new Crowded(700, font: font, smallFontSize: size);
        for (var width = 700; width >= 300; width -= 1)
        {
            ui.At(TimeSpan.Zero);
            ui.Resize(width);
            Assert.Equal("60m", string.Join("", UiHelpers.Texts(ui.Clock)));
            var height = ui.Composer.Bounds.Height;
            var below = ui.Below();
            var reserved = ui.Clock.ReservedWidth;
            foreach (var (state, at) in new[] { ("9m", TimeSpan.FromMinutes(51)), ("cold", TimeSpan.FromMinutes(61)), ("60m again", TimeSpan.Zero) })
            {
                ui.At(at);
                var where = "at " + width + " px, " + (font ?? "the theme's font") + " " + (size?.ToString() ?? "") + ", 60m -> " + state;
                // the room the bar keeps for the clock is the same whatever it shows, and never less than it takes
                Assert.True(reserved == ui.Clock.ReservedWidth, where + ": the clock's room went from " + reserved + " to " + ui.Clock.ReservedWidth);
                Assert.True(ui.Clock.DesiredSize.Width <= reserved, where + ": the clock is " + ui.Clock.DesiredSize.Width + " wide, its room " + reserved);
                Assert.True(height == ui.Composer.Bounds.Height, where + ": the box went from " + height + " to " + ui.Composer.Bounds.Height
                    + " px high; under the first line " + string.Join(",", below) + " -> " + string.Join(",", ui.Below()));
                Assert.True(below.SequenceEqual(ui.Below()), where + ": under the first line " + string.Join(",", below) + " -> " + string.Join(",", ui.Below()));
                AssertNoOverlap(ui.Shown());
            }
        }
        ui.Window.Close();
    }

    [AvaloniaTheory]
    [InlineData(200)]
    [InlineData(120)]
    [InlineData(90)]
    [InlineData(40)]
    public void Narrower_than_that_nothing_throws_and_nothing_overlaps(double width)
    {
        var ui = new Crowded(width);
        AssertNoOverlap(ui.Shown());
        ui.Resize(width - 10); // and on the way down
        AssertNoOverlap(ui.Shown());
        ui.Window.Close();
    }
}
