using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using Xunit;

namespace ClaudeVisualChat.Tests.Ui;

/// <summary>
/// The Agent map in the touch layout: on a phone the tree goes down, the agents under the main card and within the map's width;
/// on a screen where it fits (a tablet) it is the desktop's. The subtitle speaks of a tap and wraps.
/// </summary>
public class AgentMapTouchTests
{
    /// <summary>A large phone held upright, in device-independent pixels.</summary>
    private const double PhoneWidth = 384, PhoneHeight = 832;

    private static string Call(string id, string description, string? parent) =>
        "{\"type\":\"assistant\",\"message\":{\"id\":\"msg_" + id + "\",\"type\":\"message\",\"role\":\"assistant\"," +
        "\"content\":[{\"type\":\"tool_use\",\"id\":\"" + id + "\",\"name\":\"Agent\",\"input\":{\"description\":\"" + description + "\"," +
        "\"subagent_type\":\"general-purpose\"}}]},\"parent_tool_use_id\":" + (parent == null ? "null" : "\"" + parent + "\"") +
        ",\"session_id\":\"s1\"}";

    /// <summary>Two agents of the main one and, under the first, an agent it called: the map shows the newest first, the called one under its caller.</summary>
    private static ChatConversation ThreeAgents()
    {
        var c = TouchSamples.Opus();
        c.AppendLine(Call("toolu_a", "Look around the repository", null));
        c.AppendLine(Call("toolu_b", "Write the missing tests", null));
        c.AppendLine(Call("toolu_c", "Read one file", "toolu_a"));
        Assert.Equal(3, c.Agents.Count);
        return c;
    }

    private sealed class Opened
    {
        public Opened(ChatConversation conversation, double width, ChatLayout layout = ChatLayout.Touch)
        {
            Conversation = conversation;
            View = new ClaudeChatView { Conversation = conversation, Layout = layout, Strings = ChatStrings.PortugueseBrazil };
            Window = UiHelpers.Show(View, width, PhoneHeight);
            conversation.ShowAgents();
            UiHelpers.Pump();
        }

        public ChatConversation Conversation { get; }
        public ClaudeChatView View { get; }
        public Window Window { get; }

        public AgentMapPanel Map => View.GetVisualDescendants().OfType<AgentMapPanel>().Single();

        public ScrollViewer Scroller => Map.GetVisualDescendants().OfType<ScrollViewer>().First();

        public Border Main => Map.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatAgentRow") && b.Classes.Contains("main"));

        public TextBlock Subtitle => Map.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("ClaudeChatAgentMapSubtitle"));

        /// <summary>The agents' rows, top to bottom.</summary>
        public List<Button> Rows => Map.GetVisualDescendants().OfType<Button>()
            .Where(b => b.IsEffectivelyVisible && b.Classes.Contains("ClaudeChatAgentRow"))
            .OrderBy(b => b.TranslatePoint(default, Map)!.Value.Y)
            .ToList();

        public Rect InMap(Visual control) => new Rect(control.TranslatePoint(default, Map)!.Value, control.Bounds.Size);
    }

    /// <summary>Within a pixel: the dialog is centered, so the layout's rounding may put an edge a pixel either way.</summary>
    private static void Near(double expected, double actual) =>
        Assert.True(System.Math.Abs(expected - actual) <= 1.01, "expected " + expected + ", got " + actual);

    private static List<string> Titles(IEnumerable<Button> rows) =>
        rows.Select(r => r.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("ClaudeChatAgentRowTitle")).Text ?? "").ToList();

    [AvaloniaFact]
    public void On_a_phone_the_tree_goes_down_and_every_agent_is_in_the_map()
    {
        var ui = new Opened(ThreeAgents(), PhoneWidth);
        Assert.True(ui.Map.IsNarrow);
        UiHelpers.DrawFrames();

        // nothing to scroll sideways: the tree is as wide as the map
        var scroller = ui.Scroller;
        Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, scroller.HorizontalScrollBarVisibility);
        Assert.True(scroller.Extent.Width <= scroller.Viewport.Width + 0.5, "the tree is " + scroller.Extent.Width + " in " + scroller.Viewport.Width);
        var visor = ui.InMap(scroller);

        // the order of today, the newest first and the called one under its caller, 32 further in
        var rows = ui.Rows;
        Assert.Equal(new[] { "Write the missing tests", "Look around the repository", "Read one file" }, Titles(rows));
        foreach (var row in rows)
        {
            var place = ui.InMap(row);
            Assert.True(place.X >= visor.X - 0.5 && place.Right <= visor.Right + 0.5, Titles(new[] { row })[0] + " is at " + place + " in " + visor);
            Assert.True(place.Width >= 200, "a row of " + place.Width);   // the description has room to be read
        }
        Near(ui.InMap(rows[0]).X, ui.InMap(rows[1]).X);
        Near(ui.InMap(rows[1]).X + 32, ui.InMap(rows[2]).X);
        Near(ui.InMap(rows[1]).Right, ui.InMap(rows[2]).Right);     // both reach the map's right

        // the main card on top, as wide as the map, and the agents under it, 32 in
        var main = ui.InMap(ui.Main);
        Assert.True(main.Bottom < ui.InMap(rows[0]).Y, "the main card is over the agents");
        Near(main.X, visor.X);
        Near(main.X + 32, ui.InMap(rows[0]).X);
        Assert.True(main.Right >= ui.InMap(rows[0]).Right - 0.5, "the main card is as wide as the map");
        // the line goes down from the card's left (16 px in), and not out of its right
        var lines = ui.Map.GetVisualDescendants().OfType<Border>().Where(b => b.IsEffectivelyVisible && b.Classes.Contains("ClaudeChatAgentLine")).ToList();
        Assert.DoesNotContain(lines, l => l.Classes.Contains("main"));
        Assert.Contains(lines, l => l.Classes.Contains("spine") && ui.InMap(l).Y <= main.Bottom + 0.5 && System.Math.Abs(ui.InMap(l).X - (main.X + 16)) < 0.6);

        // the subtitle wraps, and speaks of a tap
        Assert.Equal(TextWrapping.Wrap, ui.Subtitle.TextWrapping);
        Assert.Equal("3 agentes · toque num agente para ver os detalhes", ui.Subtitle.Text);
        Assert.True(ui.InMap(ui.Subtitle).Right <= visor.Right + 0.5);

        // the rows' lines are tall enough for the phone's text: the descenders are not cut
        foreach (var text in ui.Map.GetVisualDescendants().OfType<TextBlock>()
                     .Where(t => t.IsEffectivelyVisible && (t.Classes.Contains("ClaudeChatAgentRowTitle") || t.Classes.Contains("ClaudeChatAgentRowMeta"))))
            Assert.True(text.LineHeight >= text.FontSize * 1.3, text.Text + ": a line of " + text.LineHeight + " for " + text.FontSize);

        // a tap on the one further down opens its card, as ever
        UiHelpers.Click(ui.Window, rows[2]);
        Assert.True(ui.Map.ShowsCard);
        Assert.Contains("Read one file", UiHelpers.Texts(ui.Map));
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_tree_that_fits_is_the_desktops()
    {
        var ui = new Opened(ThreeAgents(), 1000);
        Assert.False(ui.Map.IsNarrow);
        var rows = ui.Rows;
        Assert.Equal(new[] { "Write the missing tests", "Look around the repository", "Read one file" }, Titles(rows));
        var main = ui.InMap(ui.Main);
        Near(320, main.Width);
        Assert.All(rows, r => Assert.Equal(300, r.Bounds.Width, 1));
        Near(main.Right + 32, ui.InMap(rows[0]).X);                  // beside the card, as on the desktop
        Near(ui.InMap(rows[1]).X + 32, ui.InMap(rows[2]).X);
        Assert.Equal(TextWrapping.Wrap, ui.Subtitle.TextWrapping);            // the touch layout's subtitle all the same
        Assert.Equal("3 agentes · toque num agente para ver os detalhes", ui.Subtitle.Text);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void The_tree_follows_the_width_and_the_layout()
    {
        var ui = new Opened(ThreeAgents(), PhoneWidth);
        Assert.True(ui.Map.IsNarrow);

        ui.Window.Width = 1000;                                                 // the phone turned into a tablet
        UiHelpers.Pump();
        Assert.False(ui.Map.IsNarrow);
        Near(300, ui.Rows[0].Bounds.Width);
        ui.Window.Width = PhoneWidth;
        UiHelpers.Pump();
        Assert.True(ui.Map.IsNarrow);

        ui.View.Layout = ChatLayout.Desktop;                                    // the desktop's never goes down, however narrow
        UiHelpers.Pump();
        Assert.False(ui.Map.IsNarrow);
        Near(320, ui.Main.Bounds.Width);
        Near(300, ui.Rows[0].Bounds.Width);
        Assert.Equal(TextWrapping.NoWrap, ui.Subtitle.TextWrapping);
        Assert.Equal("3 agentes · clique num agente para ver detalhes", ui.Subtitle.Text);
        Assert.True(ui.Scroller.Extent.Width > ui.Scroller.Viewport.Width);     // it scrolls sideways, as it did
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_desktop_view_on_a_narrow_window_is_as_it_was()
    {
        var ui = new Opened(ThreeAgents(), PhoneWidth, ChatLayout.Desktop);
        Assert.False(ui.Map.IsNarrow);
        Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, ui.Scroller.HorizontalScrollBarVisibility);
        Near(ui.InMap(ui.Main).Right + 32, ui.InMap(ui.Rows[0]).X);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void The_touch_subtitle_in_both_languages()
    {
        Assert.Equal("2 agents · tap an agent for details", ChatStrings.English.FormatAgentMapSubtitle(2, touch: true));
        Assert.Equal("1 agent · tap an agent for details", ChatStrings.English.FormatAgentMapSubtitle(1, touch: true));
        Assert.Equal("2 agentes · toque num agente para ver os detalhes", ChatStrings.PortugueseBrazil.FormatAgentMapSubtitle(2, touch: true));
        Assert.Equal("1 agente · toque num agente para ver os detalhes", ChatStrings.PortugueseBrazil.FormatAgentMapSubtitle(1, touch: true));
        Assert.Equal("1.200 agentes · toque num agente para ver os detalhes", ChatStrings.PortugueseBrazil.FormatAgentMapSubtitle(1200, touch: true));
        Assert.Equal("2 agents · click an agent for details", ChatStrings.English.FormatAgentMapSubtitle(2));   // the desktop's, as it was

        // and the map shows the one agent's
        var c = TouchSamples.Opus();
        c.AppendLine(Call("toolu_a", "Look around the repository", null));
        var ui = new Opened(c, PhoneWidth);
        Assert.Equal("1 agente · toque num agente para ver os detalhes", ui.Subtitle.Text);
        ui.Window.Close();
    }
}
