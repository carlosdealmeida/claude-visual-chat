using System;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using Xunit;

namespace ClaudeVisualChat.Tests.Ui;

/// <summary>
/// A long prompt folded at 60 px (the extension's long message): a text taller than that is cut, fading out over its last 50 px, with
/// "Show more" inside the bubble at the bottom right (under the pointer or the keyboard on the desktop, always in the touch layout); open,
/// "Show less" sits under it at the right. The height animates over 0.3 s, the state lives in the item, and a narrower view folds a text
/// that fit before.
/// </summary>
public class FoldedTextTests
{
    private static string Lines(int count) => string.Join("\n", Enumerable.Range(1, count).Select(i => "line " + i));

    private static (ClaudeChatView View, Window Window) Open(ChatConversation c, bool touch = false)
    {
        var view = touch ? UiHelpers.TouchView(c) : new ClaudeChatView { Conversation = c, Strings = ChatStrings.English };
        return (view, touch ? UiHelpers.ShowPhone(view) : UiHelpers.Show(view));
    }

    /// <summary>The fold of <paramref name="item"/>'s row in the list (not the one pinned at the top).</summary>
    private static FoldedText Fold(Visual root, UserMessageItem item) =>
        root.GetVisualDescendants().OfType<FoldedText>()
            .Single(f => ReferenceEquals(f.DataContext, item) && f.FindAncestorOfType<StickyPrompt>() == null);

    private static ContentPresenter Text(FoldedText fold) =>
        fold.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");

    /// <summary>Draws frames until <paramref name="done"/> (an animation of the render clock), 5 s at most.</summary>
    private static void FrameUntil(Func<bool> done)
    {
        var clock = Stopwatch.StartNew();
        while (!done() && clock.Elapsed < TimeSpan.FromSeconds(5))
        {
            System.Threading.Thread.Sleep(20);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            UiHelpers.Pump();
        }
        Assert.True(done());
    }

    private static Rect BoundsIn(Visual control, Visual root)
    {
        var at = control.TranslatePoint(default, root)!.Value;
        return new Rect(at, control.Bounds.Size);
    }

    [AvaloniaFact]
    public void A_prompt_taller_than_sixty_pixels_folds_with_show_more()
    {
        var c = new ChatConversation();
        var prompt = new UserMessageItem(Lines(10));
        var command = new UserMessageItem("/review\n" + Lines(9)); // a slash command folds too
        var brief = new UserMessageItem("Short question");
        c.Items.Add(prompt);
        c.Items.Add(command);
        c.Items.Add(brief);
        var (view, window) = Open(c);

        foreach (var item in new[] { prompt, command })
        {
            var fold = Fold(view, item);
            Assert.True(fold.IsFolded);
            Assert.Equal(60, fold.Bounds.Height, 0.5);                    // cut at 60 px, however long it is
            Assert.True(Text(fold).Bounds.Height > 150);                  // the text keeps its height under the cut
            Assert.True(fold.ShowMoreButton.IsVisible);
            Assert.False(fold.ShowLessButton.IsVisible);
            Assert.Contains("Show more", UiHelpers.Texts(fold.ShowMoreButton));
            // inside the bubble at the bottom right, 4 px from the corner
            var more = BoundsIn(fold.ShowMoreButton, fold);
            Assert.Equal(fold.Bounds.Width - 4, more.Right, 0.5);
            Assert.Equal(60 - 4, more.Bottom, 0.5);
            // the inverted colors of a menu, 4 px corners, 8 px inside, .85em
            Assert.Same(view.FindResource("ClaudeChat.MenuForeground"), fold.ShowMoreButton.Background);
            Assert.Same(view.FindResource("ClaudeChat.MenuBackground"), fold.ShowMoreButton.Foreground);
            Assert.Equal(new CornerRadius(4), fold.ShowMoreButton.CornerRadius);
            Assert.Equal(new Thickness(8), fold.ShowMoreButton.Padding);
            Assert.Equal((double)view.FindResource("ClaudeChat.SmallFontSize")!, fold.ShowMoreButton.FontSize, 3);
            // the text fades out over its last 50 px
            var mask = Assert.IsType<LinearGradientBrush>(Text(fold).OpacityMask);
            Assert.Equal(RelativeUnit.Absolute, mask.StartPoint.Unit);
            Assert.Equal(10, mask.StartPoint.Point.Y, 0.5);
            Assert.Equal(60, mask.EndPoint.Point.Y, 0.5);
            Assert.Equal(Colors.Black, mask.GradientStops[0].Color);
            Assert.Equal(Colors.Transparent, mask.GradientStops[^1].Color);
        }

        var plain = Fold(view, brief);                                    // one line: nothing to fold
        Assert.False(plain.IsFolded);
        Assert.True(plain.Bounds.Height < 60);
        Assert.False(plain.ShowMoreButton.IsVisible);
        Assert.False(plain.ShowLessButton.IsVisible);
        Assert.Null(Text(plain).OpacityMask);
        window.Close();
    }

    [AvaloniaFact]
    public void Show_more_opens_it_and_show_less_folds_it_again()
    {
        var c = new ChatConversation();
        var prompt = new UserMessageItem(Lines(10));
        c.Items.Add(prompt);
        var (view, window) = Open(c);
        var fold = Fold(view, prompt);
        var whole = Text(fold).Bounds.Height;

        UiHelpers.Click(window, fold.ShowMoreButton);
        Assert.True(prompt.IsTextExpanded);                               // the state is the item's
        Assert.True(fold.IsAnimatingHeight);                                    // the height grows over 0.3 s
        Assert.Equal(TimeSpan.FromSeconds(0.3), FoldedText.AnimationDuration);
        FrameUntil(() => !fold.IsAnimatingHeight);
        Assert.False(fold.ShowMoreButton.IsVisible);
        Assert.True(fold.ShowLessButton.IsVisible);
        Assert.Contains("Show less", UiHelpers.Texts(fold.ShowLessButton));
        Assert.Null(Text(fold).OpacityMask);                              // nothing fades once it is whole
        // "Show less" under the whole text, at the right
        var less = BoundsIn(fold.ShowLessButton, fold);
        Assert.True(less.Top >= whole - 0.5, $"{less.Top} under {whole}");
        Assert.Equal(fold.Bounds.Width - 4, less.Right, 0.5);
        Assert.Equal(whole + less.Height + 8, fold.Bounds.Height, 0.5);   // its 4 px margins under the text

        UiHelpers.Click(window, fold.ShowLessButton);
        Assert.False(prompt.IsTextExpanded);
        Assert.True(fold.IsAnimatingHeight);
        FrameUntil(() => !fold.IsAnimatingHeight);
        Assert.Equal(60, fold.Bounds.Height, 0.5);
        Assert.True(fold.ShowMoreButton.IsVisible);
        Assert.False(fold.ShowLessButton.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void The_open_state_lives_in_the_item_and_a_row_drawn_for_it_is_open_at_once()
    {
        var c = new ChatConversation();
        var prompt = new UserMessageItem(Lines(10)) { IsTextExpanded = true };
        c.Items.Add(prompt);
        var (view, window) = Open(c);
        var fold = Fold(view, prompt);
        Assert.False(fold.IsAnimatingHeight);                                   // only the user's click animates
        Assert.True(fold.Bounds.Height > 150);
        Assert.True(fold.ShowLessButton.IsVisible);

        prompt.IsTextExpanded = false;                                    // the host folds it
        UiHelpers.Pump();
        Assert.False(fold.IsAnimatingHeight);
        Assert.Equal(60, fold.Bounds.Height, 0.5);
        window.Close();
    }

    [AvaloniaFact]
    public void A_narrower_view_folds_a_prompt_that_fit_before()
    {
        var c = new ChatConversation();
        var prompt = new UserMessageItem(string.Join(" ", Enumerable.Repeat("words that wrap", 14))); // two lines at 1000 px
        c.Items.Add(prompt);
        var (view, window) = Open(c);
        var fold = Fold(view, prompt);
        Assert.False(fold.IsFolded);
        Assert.True(fold.Bounds.Height < 60);
        Assert.False(fold.ShowMoreButton.IsVisible);

        window.Width = 300;                                               // narrower: the same text takes more lines
        UiHelpers.Pump();
        Assert.True(fold.IsFolded);
        Assert.Equal(60, fold.Bounds.Height, 0.5);
        Assert.True(fold.ShowMoreButton.IsVisible);

        window.Width = 1000;                                              // and wider again it fits
        UiHelpers.Pump();
        Assert.False(fold.IsFolded);
        Assert.False(fold.ShowMoreButton.IsVisible);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Show_more_shows_under_the_pointer_or_the_keyboard_on_the_desktop_and_always_in_the_touch_layout(bool touch)
    {
        var c = new ChatConversation();
        var prompt = new UserMessageItem(Lines(10));
        c.Items.Add(prompt);
        c.Items.Add(new AssistantTextItem { Text = "An answer." });
        var (view, window) = Open(c, touch);
        var fold = Fold(view, prompt);
        var more = fold.ShowMoreButton;
        var bubble = fold.FindAncestorOfType<Border>()!;
        Assert.Contains("ClaudeChatUser", bubble.Classes);
        Assert.Contains(touch ? "Mostrar mais" : "Show more", UiHelpers.Texts(more));
        if (touch)
        {
            Assert.Equal(1, more.Opacity);                                // no pointer to hover with: always shown
            Assert.True(more.Bounds.Width >= 44 && more.Bounds.Height >= 44, more.Bounds.ToString());
            var target = BoundsIn(more, fold);
            Assert.True(target.Bottom <= fold.Bounds.Height + 0.5 && target.Right <= fold.Bounds.Width + 0.5, "inside the bubble");
            var corner = more.TranslatePoint(new Point(2, 2), window)!.Value; // a tap near its corner, which the face does not reach
            window.MouseDown(corner, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(corner, MouseButton.Left, RawInputModifiers.None);
            UiHelpers.Pump();
            Assert.True(prompt.IsTextExpanded);
            FrameUntil(() => !fold.IsAnimatingHeight);
            Assert.True(fold.ShowLessButton.Bounds.Height >= 44);
        }
        else
        {
            Assert.Equal(0, more.Opacity);                                // clear until the pointer comes over the bubble
            Assert.True(more.Bounds.Height < 44);
            var away = bubble.TranslatePoint(new Point(10, 10), window)!.Value;
            window.MouseMove(away, RawInputModifiers.None);
            UiHelpers.Pump();
            Assert.Equal(1, more.Opacity);
            window.MouseMove(new Point(window.Width - 2, window.Height - 2), RawInputModifiers.None);
            UiHelpers.Pump();
            Assert.Equal(0, more.Opacity);
            more.Focus(NavigationMethod.Tab);                             // reached with the keyboard: it shows
            UiHelpers.Pump();
            Assert.Equal(1, more.Opacity);
            UiHelpers.Press(window, Key.Enter);
            Assert.True(prompt.IsTextExpanded);
            FrameUntil(() => !fold.IsAnimatingHeight);
            Assert.Equal(0.9, fold.ShowLessButton.Opacity, 3);            // "Show less" is always shown, at 90 % until the pointer comes
        }
        window.Close();
    }
}
