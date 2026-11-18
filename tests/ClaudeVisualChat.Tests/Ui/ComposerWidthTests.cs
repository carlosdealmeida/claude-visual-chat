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
/// the lines under it in a fixed order instead of being drawn over one another; and narrower still, nothing throws.
/// </summary>
public class ComposerWidthTests
{
    private static string Call(string id, string description) =>
        "{\"type\":\"assistant\",\"message\":{\"id\":\"msg_" + id + "\",\"type\":\"message\",\"role\":\"assistant\"," +
        "\"content\":[{\"type\":\"tool_use\",\"id\":\"" + id + "\",\"name\":\"Agent\",\"input\":{\"description\":\"" + description + "\"," +
        "\"subagent_type\":\"general-purpose\"}}]},\"parent_tool_use_id\":null,\"session_id\":\"s1\"}";

    /// <summary>A model with a long name, so that the chip is wide: "Opus 5.5 Extended Preview High".</summary>
    private static readonly ChatModel LongModel =
        new ChatModel("opus-preview", "claude-opus-5-5-preview", "Opus 5.5 Extended Preview", "", new[] { "low", "high" });

    /// <summary>A longer name for the mode the box is in.</summary>
    private static readonly ChatStrings LongMode = ChatStrings.English with { ModeAcceptEdits = "Edit files automatically" };

    /// <summary>
    /// The view at <paramref name="width"/> with every piece of the bar on screen: the context ring, the cache clock, Remote
    /// Control, the agents chip, the model with a long name, the mode with a long name, and send and stop side by side.
    /// </summary>
    private sealed class Crowded
    {
        public Crowded(double width, ChatStrings? strings = null, ChatModel? model = null)
        {
            model ??= LongModel;
            Conversation = new ChatConversation { AvailableModels = new[] { model } };
            Conversation.Apply(new SessionStarted("s1", model.Value, null, "acceptEdits", "2.1.310"));
            Conversation.Apply(new SettingsApplied(null, "high", true));
            Conversation.Apply(new PromptCacheUsed(TimeSpan.FromHours(1), null));
            Conversation.Apply(new ContextUsageReported(new ContextUsage(800, 1000, 80, Array.Empty<ContextCategory>())));
            Conversation.RemoteControlStatus = ChatRemoteControlStatus.Connected;
            Conversation.AppendLine(Call("toolu_a", "Look around the repository"));
            Conversation.AppendLine(Call("toolu_b", "Write the missing tests"));
            View = new ClaudeChatView { Conversation = Conversation, Strings = strings ?? LongMode };
            Window = UiHelpers.Show(View, width, UiHelpers.Height);
            Composer = UiHelpers.Composer(View);
            Conversation.Submit("go"); // busy: stop shows
            UiHelpers.Input(View).Text = "and then this"; // and send next to it
            UiHelpers.Pump();
        }

        public ChatConversation Conversation { get; }
        public ClaudeChatView View { get; }
        public Window Window { get; }
        public ChatComposer Composer { get; }

        public Border Box => Composer.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("ClaudeChatComposer"));

        public Button Add => Named<Button>("AddButton");
        public Button Commands => Named<Button>("CommandsButton");
        public Button Send => Named<Button>("SendButton");
        public Button Stop => Named<Button>("StopButton");

        private T Named<T>(string name) where T : Control =>
            Composer.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

        /// <summary>Every piece of the desktop bar, by name, in the bar's order.</summary>
        public IReadOnlyList<(string Name, Control Control)> Pieces => new (string, Control)[]
        {
            ("add", Add),
            ("commands", Commands),
            ("ring", Composer.GetVisualDescendants().OfType<ContextRing>().Single(r => r.Name == "ContextRing")),
            ("clock", Composer.GetVisualDescendants().OfType<PromptCacheClock>().Single()),
            ("remote", Composer.GetVisualDescendants().OfType<RemoteControlChip>().Single()),
            ("agents", Composer.GetVisualDescendants().OfType<AgentsChip>().Single()),
            ("model", Composer.GetVisualDescendants().OfType<ModelChip>().Single(m => m.Name == "ModelChip")),
            ("mode", Composer.GetVisualDescendants().OfType<ComboBox>().Single()),
            ("send", Send),
            ("stop", Stop),
        };

        /// <summary>The pieces on screen, with where they are in the reply box (their margins left out).</summary>
        public List<(string Name, Rect Bounds)> Shown() =>
            Pieces.Where(p => p.Control.IsEffectivelyVisible && p.Control.Bounds.Width > 0)
                .Select(p => (p.Name, BoundsIn(p.Control, Composer)))
                .ToList();
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

    [AvaloniaTheory]
    [InlineData(350)]
    [InlineData(320)]
    public void At_a_narrow_width_nothing_in_the_bar_overlaps_and_everything_is_still_there(double width)
    {
        var ui = new Crowded(width);
        var shown = ui.Shown();

        // every piece is still on screen, each one whole and inside the box
        Assert.Equal(ui.Pieces.Select(p => p.Name), shown.Select(s => s.Name));
        foreach (var (name, control) in ui.Pieces)
        {
            Assert.True(control.Bounds.Width >= control.DesiredSize.Width - control.Margin.Left - control.Margin.Right - 0.5, name + " is squeezed");
        }
        AssertNoOverlap(shown);
        foreach (var (name, bounds) in shown) AssertInside(ui, name, bounds);
        foreach (var button in new[] { ui.Add, ui.Commands, ui.Send, ui.Stop }) AssertClickable(ui, button);

        // +, / and send and stop stay on the first line, at its ends
        var first = BoundsIn(ui.Add, ui.Composer).Y;
        Assert.Equal(first, BoundsIn(ui.Commands, ui.Composer).Y, 1);
        Assert.Equal(first, BoundsIn(ui.Send, ui.Composer).Y, 1);
        var box = BoundsIn(ui.Box, ui.Composer);
        Assert.Equal(box.Right - 1 - 5, BoundsIn(ui.Stop, ui.Composer).Right, 1);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void The_pieces_go_down_one_by_one_in_the_order_of_what_matters_least()
    {
        // from wide to narrow, a piece leaves the first line only after every piece that matters less has left it: the clock
        // first, then Remote Control, the agents chip, the context ring, the model, and the mode last (at 320 px it is still
        // there, next to send)
        var order = new[] { "clock", "remote", "agents", "ring", "model", "mode" };
        var left = new List<string>();
        var ui = new Crowded(1000);
        for (var width = 1000; width >= 200; width -= 10)
        {
            ui.Window.Width = width;
            UiHelpers.Pump();
            var shown = ui.Shown();
            AssertNoOverlap(shown);
            var top = BoundsIn(ui.Add, ui.Composer).Y;
            var below = shown.Where(s => s.Bounds.Y > top + 13).Select(s => s.Name).ToList(); // under the first line
            Assert.DoesNotContain("add", below);
            Assert.DoesNotContain("commands", below);
            Assert.DoesNotContain("send", below);
            Assert.DoesNotContain("stop", below);
            Assert.Equal(order.Take(below.Count), order.Where(below.Contains)); // the ones that matter least went first
            foreach (var name in below.Where(n => !left.Contains(n))) left.Add(name);
            if (width == 320) Assert.Equal(order.Take(5), left);
        }
        Assert.Equal(order, left); // and at 200 px every one of them has gone down
        ui.Window.Close();
    }

    [AvaloniaTheory]
    [InlineData(1000, 0)]
    [InlineData(700, -12)]
    public void Where_everything_fits_the_bar_is_one_line_as_it_always_was(double width, double shift)
    {
        // every piece on screen, "Opus 5.5 High" and "Edit automatically": the places the bar had before it learned to wrap
        var ui = new Crowded(width, ChatStrings.English, TouchSamples.Models[0]);
        var expected = new Dictionary<string, Rect>
        {
            ["add"] = new Rect(6, 47, 26, 26),
            ["commands"] = new Rect(34, 47, 26, 26),
            ["ring"] = new Rect(62, 48, 28, 24),
            ["clock"] = new Rect(92, 47, 57, 26),
            ["remote"] = new Rect(151, 51, 93, 18),
            ["agents"] = new Rect(252, 51, 68, 18),
            ["model"] = new Rect(328, 51, 88, 18),
            ["mode"] = new Rect(494 + shift, 47, 122, 26),
            ["send"] = new Rect(620 + shift, 47, 26, 26),
            ["stop"] = new Rect(648 + shift, 47, 26, 26),
        };
        Assert.Equal(expected, ui.Shown().ToDictionary(s => s.Name, s => s.Bounds));
        Assert.Equal(new Rect(0, 0, 680 + shift, 79), BoundsIn(ui.Box, ui.Composer));
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
        ui.Window.Width = width - 10; // and on the way down
        UiHelpers.Pump();
        AssertNoOverlap(ui.Shown());
        ui.Window.Close();
    }
}
