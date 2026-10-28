using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using SkiaSharp;

namespace ClaudeVisualChat.Tests.Ui;

internal static class UiHelpers
{
    public const double Width = 1000;
    public const double Height = 700;

    /// <summary>A phone's screen in device-independent pixels (a 412 × 892 window).</summary>
    public const double PhoneWidth = 412, PhoneHeight = 892;

    /// <summary>Shows the control in a 1000×700 window and lets layout and bindings settle.</summary>
    public static Window Show(Control content, double width = Width, double height = Height)
    {
        var window = new Window { Width = width, Height = height, Content = content };
        window.Show();
        Pump();
        return window;
    }

    /// <summary>Shows the control in a window the size of a phone's screen.</summary>
    public static Window ShowPhone(Control content) => Show(content, PhoneWidth, PhoneHeight);

    /// <summary>A view in the touch layout, with the Portuguese texts.</summary>
    public static ClaudeChatView TouchView(ChatConversation? c) =>
        new ClaudeChatView { Conversation = c, Layout = ChatLayout.Touch, Strings = ChatStrings.PortugueseBrazil };

    public static void Pump() => Dispatcher.UIThread.RunJobs();

    /// <summary>
    /// Draws the frames that bring the screen up to the tree. What is under a point is found in the last frame drawn, and the
    /// headless window draws one only when its render timer ticks: a timer of 60 Hz that the dispatcher fires between the jobs it
    /// runs, so a point asked about right after a layer opened or closed (or a list moved) is often still in the frame before it, and
    /// how often depends on how fast the machine runs. Each tick is followed by the jobs it leaves: the first sends what changed to the
    /// renderer, the next draws it.
    /// </summary>
    public static void DrawFrames(int count = 3)
    {
        for (var frame = 0; frame < count; frame++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Pump();
        }
    }

    /// <summary>What is under <paramref name="point"/> of the window, in a frame drawn after everything that happened before the call.</summary>
    public static IInputElement? HitTest(Window window, Point point)
    {
        DrawFrames();
        return window.InputHitTest(point);
    }

    /// <summary>A PNG of <paramref name="w"/> by <paramref name="h"/> pixels in one color: what a picked or pasted image is to the conversation.</summary>
    public static byte[] Png(int w, int h)
    {
        using var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        bmp.Erase(new SKColor(30, 120, 200));
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Runs the UI jobs until <paramref name="done"/>, for work that ends on another thread (10 s at most).</summary>
    public static void PumpUntil(System.Func<bool> done)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && clock.Elapsed < System.TimeSpan.FromSeconds(10))
        {
            Dispatcher.UIThread.RunJobs();
            System.Threading.Thread.Sleep(5);
        }
        Dispatcher.UIThread.RunJobs();
        Xunit.Assert.True(done());
    }

    /// <summary>Lets work on other threads run, for the checks that something did not happen.</summary>
    public static void Settle()
    {
        for (var i = 0; i < 20; i++)
        {
            Dispatcher.UIThread.RunJobs();
            System.Threading.Thread.Sleep(5);
        }
    }

    public static ChatConversation Recording(string scenario, string source)
    {
        var conversation = new ChatConversation();
        if (source == "stream")
        {
            foreach (var line in FixtureFiles.StreamLines(scenario)) conversation.AppendLine(line);
        }
        else
        {
            conversation.LoadTranscript(FixtureFiles.TranscriptLines(scenario));
        }
        return conversation;
    }

    /// <summary>The conversation list (the first one, before the lists of subagents).</summary>
    public static ItemsControl ItemList(ClaudeChatView view) =>
        view.GetLogicalDescendants().OfType<ItemsControl>().First();

    public static ScrollViewer Scroller(ClaudeChatView view) =>
        view.GetLogicalDescendants().OfType<ScrollViewer>().First();

    /// <summary>The text of every TextBlock the user can see under <paramref name="root"/> (a text in pieces, joined).</summary>
    public static List<string> Texts(Visual root) =>
        root.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible)
            .Select(t => string.IsNullOrEmpty(t.Text) && t.Inlines is { Count: > 0 } inlines ? inlines.Text ?? "" : t.Text ?? "")
            .ToList();

    /// <summary>The text of the Markdown drawn under <paramref name="root"/>: its paragraphs, joined by a space.</summary>
    public static string MarkdownText(Visual root) =>
        string.Join(" ", root.GetVisualDescendants().OfType<ColorTextBlock.Avalonia.CTextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text));

    public static bool AtEnd(ScrollViewer scroller) =>
        scroller.Offset.Y >= scroller.Extent.Height - scroller.Viewport.Height - 1;

    // ---- the permission prompt and the reply box ----

    public static PermissionPrompt Prompt(ClaudeChatView view) =>
        view.GetVisualDescendants().OfType<PermissionPrompt>().Single();

    public static ChatComposer Composer(ClaudeChatView view) =>
        view.GetVisualDescendants().OfType<ChatComposer>().Single();

    /// <summary>The text box of the reply box.</summary>
    public static TextBox Input(ClaudeChatView view) =>
        Composer(view).GetVisualDescendants().OfType<TextBox>().Single();

    /// <summary>The one visible button under <paramref name="root"/> that shows <paramref name="text"/>, or is named so (an icon button).</summary>
    public static Button ButtonWithText(Visual root, string text) =>
        root.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && (Texts(b).Contains(text) || AutomationProperties.GetName(b) == text));

    /// <summary>The names of the visible icon buttons under <paramref name="root"/>: what a screen reader tells.</summary>
    public static List<string> ButtonNames(Visual root) =>
        root.GetVisualDescendants()
            .OfType<Button>()
            .Where(b => b.IsEffectivelyVisible && !string.IsNullOrEmpty(AutomationProperties.GetName(b)))
            .Select(b => AutomationProperties.GetName(b)!)
            .ToList();

    /// <summary>A left click in the middle of the control, through the window's mouse.</summary>
    public static void Click(Window window, Control control)
    {
        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window);
        if (center == null) throw new System.InvalidOperationException("the control is not in the window");
        window.MouseMove(center.Value, RawInputModifiers.None);
        window.MouseDown(center.Value, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(center.Value, MouseButton.Left, RawInputModifiers.None);
        Pump();
    }

    /// <summary>A key pressed and released on the focused control, through the window's keyboard.</summary>
    public static void Press(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        KeyDown(window, key, modifiers);
        KeyUp(window, key, modifiers);
    }

    /// <summary>A key going down; sent again without <see cref="KeyUp"/>, it is the keyboard's repeat.</summary>
    public static void KeyDown(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, PhysicalKeyOf(key), null);
        Pump();
    }

    public static void KeyUp(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyRelease(key, modifiers, PhysicalKeyOf(key), null);
        Pump();
    }

    private static PhysicalKey PhysicalKeyOf(Key key)
    {
        if (key >= Key.D0 && key <= Key.D9) return PhysicalKey.Digit0 + (key - Key.D0);
        return System.Enum.TryParse<PhysicalKey>(key.ToString(), out var physical) ? physical : PhysicalKey.None;
    }

    /// <summary>Text typed into the focused control.</summary>
    public static void Type(Window window, string text)
    {
        window.KeyTextInput(text);
        Pump();
    }

    /// <summary>The keys that reach the window without anyone handling them (what the app would get).</summary>
    public static List<Key> UnhandledKeys(Window window)
    {
        var keys = new List<Key>();
        window.AddHandler(InputElement.KeyDownEvent, (_, e) => keys.Add(e.Key), RoutingStrategies.Bubble);
        return keys;
    }

    public static IInputElement? Focused(Window window) => window.FocusManager?.GetFocusedElement();
}
