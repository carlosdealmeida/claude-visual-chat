using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using ClaudeVisualChat.Controls;
using Xunit;

namespace ClaudeVisualChat.Tests;

public class ShrinkPanelTests
{
    /// <summary>A child that would like <paramref name="height"/>, giving way at <paramref name="givesWay"/> down to <paramref name="min"/>.</summary>
    private static Border Part(double height, int givesWay = 0, double min = 0, double floor = 0)
    {
        var part = new Border { Child = new Border { Height = height } };
        ShrinkPanel.SetGivesWay(part, givesWay);
        ShrinkPanel.SetMinShare(part, min);
        ShrinkPanel.SetFloor(part, floor);
        return part;
    }

    private static ShrinkPanel Laid(double height, params Control[] parts)
    {
        var panel = new ShrinkPanel();
        foreach (var part in parts) panel.Children.Add(part);
        panel.Measure(new Size(200, height));
        panel.Arrange(new Rect(0, 0, 200, height));
        return panel;
    }

    private static double[] Heights(ShrinkPanel panel) => panel.Children.Select(c => c.Bounds.Height).ToArray();

    private static double[] Tops(ShrinkPanel panel) => panel.Children.Select(c => c.Bounds.Y).ToArray();

    [AvaloniaFact]
    public void Nothing_shrinks_when_it_fits()
    {
        var panel = Laid(300, Part(50, 1, 10), Part(60, 2, 10), Part(70));

        Assert.Equal(new double[] { 50, 60, 70 }, Heights(panel));
        Assert.Equal(new double[] { 0, 50, 110 }, Tops(panel));      // top to bottom
        Assert.Equal(180, panel.DesiredSize.Height);
    }

    [AvaloniaFact]
    public void The_highest_gives_way_first_down_to_its_minimum()
    {
        // 300 wanted, 200 there: the 2 gives first, down to its 40 (60 of the 100), and the 1 the other 40
        var panel = Laid(200, Part(100, 1, 20), Part(100, 2, 40), Part(100));

        Assert.Equal(new double[] { 60, 40, 100 }, Heights(panel));
        Assert.Equal(new double[] { 0, 60, 100 }, Tops(panel));

        // a minimum over what the child wants is what it wants: it never grows
        panel = Laid(150, Part(30, 2, 80), Part(150, 1, 10));
        Assert.Equal(new double[] { 30, 120 }, Heights(panel));

        // two at the same level share the cut by what each can give
        panel = Laid(150, Part(100, 1, 0), Part(50, 1, 0), Part(30));
        Assert.Equal(new double[] { 80, 40, 30 }, Heights(panel));
    }

    [AvaloniaFact]
    public void Zero_never_shrinks()
    {
        // the minimums leave 30 too many: the 0 keeps its whole height, and the others go on giving, the highest first, down to
        // their floor (0 when they have none)
        var panel = Laid(100, Part(60, 1, 20), Part(80), Part(50, 3, 30));
        Assert.Equal(new double[] { 20, 80, 0 }, Heights(panel));
        Assert.Equal(new double[] { 0, 20, 100 }, Tops(panel));

        panel = Laid(100, Part(60, 1, 20), Part(80), Part(50, 3, 30, floor: 10));
        Assert.Equal(new double[] { 10, 80, 10 }, Heights(panel));

        // a hidden child takes no room
        var hidden = Part(500, 0);
        hidden.IsVisible = false;
        panel = Laid(100, Part(40), hidden, Part(40));
        Assert.Equal(new double[] { 0, 40 }, panel.Children.Where(c => c.IsVisible).Select(c => c.Bounds.Y).ToArray());
    }

    [AvaloniaFact]
    public void Inside_a_scroll_it_fits_its_room_and_scrolls_only_what_its_floors_leave()
    {
        // measured with no limit, it wants its whole height; arranged with a room, it gives way to it and is that high
        var panel = new ShrinkPanel { Room = 100, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        panel.Children.Add(Part(80));
        panel.Children.Add(Part(60, 4, 15, floor: 15));
        panel.Measure(Size.Infinity);
        Assert.Equal(140, panel.DesiredSize.Height);
        panel.Arrange(new Rect(0, 0, 200, 140));
        Assert.Equal(new double[] { 80, 20 }, Heights(panel));
        Assert.Equal(100, panel.Bounds.Height);

        panel.Room = 50; // less than the floors: it is as high as they are, and the rest scrolls
        panel.Measure(Size.Infinity);
        Assert.Equal(140, panel.DesiredSize.Height);
        panel.Arrange(new Rect(0, 0, 200, 140));
        Assert.Equal(new double[] { 80, 15 }, Heights(panel));
        Assert.Equal(95, panel.Bounds.Height);
    }
}
