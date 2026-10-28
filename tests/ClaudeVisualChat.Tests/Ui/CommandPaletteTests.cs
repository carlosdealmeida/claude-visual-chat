using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using System.Text.Json;
using ClaudeVisualChat.Controls;
using ClaudeVisualChat.Imaging;
using ClaudeVisualChat.Protocol;
using Xunit;

namespace ClaudeVisualChat.Tests.Ui;

public class CommandPaletteTests
{
    private sealed class Opened
    {
        public Opened(ChatConversation conversation)
        {
            Conversation = conversation;
            conversation.MessageSubmitted += (_, e) => Sent.Add(e.Text);
            conversation.CommandInvoked += (_, e) => Invoked.Add(e.Name);
            conversation.StopRequested += (_, _) => Stops++;
            View = new ClaudeChatView { Conversation = conversation };
            Window = UiHelpers.Show(View);
            Composer = UiHelpers.Composer(View);
            Input = UiHelpers.Input(View);
            Palette = Composer.GetLogicalDescendants().OfType<CommandPalette>().Single();
            Unhandled = UiHelpers.UnhandledKeys(Window);
        }

        public ChatConversation Conversation { get; }
        public ClaudeChatView View { get; }
        public Window Window { get; }
        public ChatComposer Composer { get; }
        public TextBox Input { get; }
        public CommandPalette Palette { get; }
        public List<Key> Unhandled { get; }
        public List<string> Sent { get; } = new List<string>();
        public List<string> Invoked { get; } = new List<string>();
        public int Stops { get; set; }

        public Button SlashButton => UiHelpers.ButtonWithText(Composer, "Show command menu (/)");

        /// <summary>The palette's content, while it is open.</summary>
        public Control Menu => Palette.Child!;

        /// <summary>The commands listed, top to bottom.</summary>
        public List<string> Labels => Menu.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && t.Classes.Contains("ClaudeChatMenuLabel")).Select(t => t.Text ?? "").ToList();

        /// <summary>The row the keys act on, or null.</summary>
        public string? Active => Menu.GetVisualDescendants().OfType<Border>()
            .Where(b => b.IsEffectivelyVisible && b.Classes.Contains("ClaudeChatMenuItem") && b.Classes.Contains("active"))
            .Select(b => b.GetVisualDescendants().OfType<TextBlock>().First().Text).SingleOrDefault();

        public TextBox? Filter => Menu.GetVisualDescendants().OfType<TextBox>().SingleOrDefault(t => t.IsEffectivelyVisible);

        public void Type(string text)
        {
            Input.Focus();
            UiHelpers.Type(Window, text);
        }

        public void Press(Key key) => UiHelpers.Press(Window, key);

        /// <summary>A "/" typed in the box, then the rest, as the keyboard sends them.</summary>
        public void Slash(string rest)
        {
            Type("/");
            if (rest.Length > 0) UiHelpers.Type(Window, rest);
        }
    }

    private static ChatConversation WithCommands(params ChatCommand[] commands) =>
        new ChatConversation { Commands = commands };

    private static Opened Open(ChatConversation? conversation = null) => new Opened(conversation ?? WithCommands(
        new ChatCommand("compact", "Clear the history but keep a summary", ""),
        new ChatCommand("hello", "Say hello", "[name]"),
        new ChatCommand("help", "Show help", "")));

    // ---- the brief ----

    [AvaloniaFact]
    public void Slash_button_and_typed_slash_open_the_palette()
    {
        var ui = Open();
        Assert.False(ui.Palette.IsOpen);

        // the button opens it with a filter field that takes the keyboard, and closes it
        UiHelpers.Click(ui.Window, ui.SlashButton);
        Assert.True(ui.Palette.IsOpen);
        Assert.NotNull(ui.Filter);
        Assert.Equal("Filter actions…", ui.Filter!.Watermark);
        Assert.True(ui.Filter.IsFocused);
        Assert.Contains("Slash Commands", UiHelpers.Texts(ui.Menu));
        // all of them, in the claude's order, then the conversation's own with their descriptions
        Assert.Equal(new[] { "/compact", "/hello", "/help", "/context", "/usage", "/remote-control", "/rc", "/chrome", "/copy" }, ui.Labels);
        Assert.Equal("Show context usage", ToolTip.GetTip(Row(ui, "/context")));
        Assert.Equal("Say hello", ToolTip.GetTip(Row(ui, "/hello")));
        Assert.Equal("Copy Claude's last response to clipboard (or /copy N for the Nth-latest)", ToolTip.GetTip(Row(ui, "/copy")));
        UiHelpers.Click(ui.Window, ui.SlashButton);
        Assert.False(ui.Palette.IsOpen);

        // a "/" typed in the empty box opens it, filtered by what follows; the box keeps the keyboard
        ui.Type("/");
        Assert.True(ui.Palette.IsOpen);
        Assert.Null(ui.Filter);
        Assert.True(ui.Input.IsFocused);
        Assert.Equal(9, ui.Labels.Count);
        UiHelpers.Type(ui.Window, "hel");
        Assert.Equal("/hello", ui.Labels[0]);
        Assert.Equal(new[] { "/hello", "/help" }, ui.Labels);
        Assert.Equal("/hello", ui.Active);
        UiHelpers.Type(ui.Window, "zzz");
        Assert.Empty(ui.Labels);
        Assert.Contains("No matching commands", UiHelpers.Texts(ui.Menu));

        // Esc closes it and the box keeps the keyboard and the text; the Esc is not the app's
        ui.Press(Key.Escape);
        Assert.False(ui.Palette.IsOpen);
        Assert.True(ui.Input.IsFocused);
        Assert.Equal("/helzzz", ui.Input.Text);
        Assert.DoesNotContain(Key.Escape, ui.Unhandled);

        // only a "/" at the start of an empty box opens it
        ui.Input.Clear();
        ui.Type("a/");
        Assert.False(ui.Palette.IsOpen);

        // Backspace taking the "/" away closes it
        ui.Input.Clear();
        ui.Type("/");
        Assert.True(ui.Palette.IsOpen);
        ui.Press(Key.Back);
        Assert.Equal("", ui.Input.Text ?? "");
        Assert.False(ui.Palette.IsOpen);

        // Esc in the filter field gives the keyboard back to the box
        UiHelpers.Click(ui.Window, ui.SlashButton);
        Assert.True(ui.Filter!.IsFocused);
        ui.Press(Key.Escape);
        Assert.False(ui.Palette.IsOpen);
        Assert.True(ui.Input.IsFocused);
        Assert.DoesNotContain(Key.Escape, ui.Unhandled);
        Assert.Empty(ui.Sent);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Enter_runs_and_tab_inserts()
    {
        var ui = Open();

        ui.Slash("hel");
        Assert.Equal("/hello", ui.Active);
        ui.Press(Key.Enter);
        Assert.Equal(new[] { "/hello" }, ui.Sent);
        Assert.Equal("", ui.Input.Text ?? "");
        Assert.False(ui.Palette.IsOpen);
        Assert.DoesNotContain(Key.Enter, ui.Unhandled);

        ui.Slash("hel");
        ui.Press(Key.Tab);
        Assert.Equal("/hello ", ui.Input.Text);
        Assert.Equal(7, ui.Input.CaretIndex);
        Assert.False(ui.Palette.IsOpen);
        Assert.Single(ui.Sent);
        Assert.True(ui.Input.IsFocused);
        // what follows the command is its arguments: the box sends it as written
        UiHelpers.Type(ui.Window, "Carlos");
        Assert.False(ui.Palette.IsOpen);
        ui.Press(Key.Enter);
        Assert.Equal(new[] { "/hello", "/hello Carlos" }, ui.Sent);

        // a command of the conversation's own is not sent: it is invoked, and context opens its panel
        ui.Slash("context");
        ui.Press(Key.Enter);
        Assert.Equal(new[] { "context" }, ui.Invoked);
        Assert.Equal(ChatPanel.Context, ui.Conversation.ShownPanel);
        Assert.Equal(2, ui.Sent.Count);
        Assert.Equal("", ui.Input.Text ?? "");

        // the arrows move the mark, round the ends; Enter with no row marked sends what was typed
        ui.Slash("he");
        Assert.Equal(new[] { "/hello", "/help", "/compact", "/chrome", "/copy" }, ui.Labels); // "the" in their descriptions
        Assert.Equal("/hello", ui.Active);
        ui.Press(Key.Up);
        Assert.Equal("/copy", ui.Active);
        ui.Press(Key.Down);
        Assert.Equal("/hello", ui.Active);
        ui.Press(Key.Down);
        Assert.Equal("/help", ui.Active);
        Assert.DoesNotContain(Key.Down, ui.Unhandled);
        Assert.DoesNotContain(Key.Up, ui.Unhandled);
        ui.Press(Key.Enter);
        Assert.Equal("/help", ui.Sent.Last());

        // from the button: the filter field's keys; the command goes, and the draft stays in the box
        ui.Type("draft");
        UiHelpers.Click(ui.Window, ui.SlashButton);
        UiHelpers.Type(ui.Window, "hel");
        Assert.Equal(new[] { "/hello", "/help" }, ui.Labels);
        ui.Press(Key.Enter);
        Assert.Equal("/hello", ui.Sent.Last());
        Assert.False(ui.Palette.IsOpen);
        Assert.Equal("draft", ui.Input.Text);
        Assert.True(ui.Input.IsFocused);

        // Tab from the button puts the command in the box, in place of the draft
        UiHelpers.Click(ui.Window, ui.SlashButton);
        UiHelpers.Type(ui.Window, "comp");
        ui.Press(Key.Tab);
        Assert.Equal("/compact ", ui.Input.Text);
        Assert.False(ui.Palette.IsOpen);
        Assert.True(ui.Input.IsFocused);

        // a click on a row runs it
        ui.Input.Clear();
        UiHelpers.Click(ui.Window, ui.SlashButton);
        UiHelpers.Click(ui.Window, Row(ui, "/usage"));
        Assert.Equal(new[] { "context", "usage" }, ui.Invoked);
        Assert.False(ui.Palette.IsOpen);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Filter_ranks_exact_then_prefix_then_contains()
    {
        var ui = Open(WithCommands(
            new ChatCommand("review", "Review the code of a pull request", ""),
            new ChatCommand("account", "Show the account", ""),
            new ChatCommand("compact", "Free the context at a low cost", ""),
            new ChatCommand("context", "Show the context window", ""),
            new ChatCommand("cost", "Show the cost of the session", "")));

        ui.Slash("co");
        // the names that start with it, in the claude's order; then the names that have it; then the descriptions
        // (the conversation's own commands come after the claude's, rc and chrome by their texts)
        Assert.Equal(new[] { "/compact", "/context", "/cost", "/copy", "/account", "/remote-control", "/review", "/rc", "/chrome" }, ui.Labels);

        UiHelpers.Type(ui.Window, "st");
        // the exact name first, then the one whose description has the word
        Assert.Equal(new[] { "/cost", "/compact" }, ui.Labels);
        Assert.Equal("/cost", ui.Active);

        // the case does not matter, and the conversation's own commands are found by their texts
        ui.Input.Clear();
        ui.Slash("BROWSER");
        Assert.Equal(new[] { "/chrome" }, ui.Labels);
        ui.Window.Close();
    }

    // ---- the look ----

    [AvaloniaFact]
    public void The_palette_has_the_extension_measures()
    {
        var ui = Open();
        ui.Slash("he");
        var frame = (Border)ui.Menu;
        var box = ui.Composer.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("ClaudeChatComposer"));

        // over the reply box, as wide as it, 8 px above it
        Assert.Equal(box.Bounds.Width, frame.Bounds.Width, 1);
        var frameBottom = frame.TranslatePoint(new Point(0, frame.Bounds.Height), ui.Window)!.Value;
        var boxTop = box.TranslatePoint(default, ui.Window)!.Value;
        Assert.Equal(boxTop.X, frameBottom.X, 1);
        Assert.Equal(boxTop.Y - 8, frameBottom.Y, 1);
        Assert.Equal(new CornerRadius(8), frame.CornerRadius);
        Assert.Equal(new Thickness(1), frame.BorderThickness);
        Assert.Same(Resource(ui.View, "ClaudeChat.MenuBackground"), frame.Background);
        Assert.Same(Resource(ui.View, "ClaudeChat.InputBorder"), frame.BorderBrush);

        // a row: 4 px by 8 px inside, 4 px from the sides, 4 px corners; the marked one in the list's selection colors
        var row = Row(ui, "/hello");
        Assert.Equal(new Thickness(8, 4), row.Padding);
        Assert.Equal(new Thickness(4, 0), row.Margin);
        Assert.Equal(new CornerRadius(4), row.CornerRadius);
        Assert.Same(Resource(ui.View, "ClaudeChat.ListActiveBackground"), row.Background);
        Assert.Same(Resource(ui.View, "ClaudeChat.ListActiveForeground"), Label(row).Foreground);
        var other = Row(ui, "/help");
        Assert.Same(Resource(ui.View, "ClaudeChat.Foreground"), Label(other).Foreground);
        Assert.Equal(13, Label(other).FontSize);

        // the section's title: .9em, at half opacity, 4 px by 12 px around it
        var header = ui.Menu.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Slash Commands");
        Assert.Equal(11.7, header.FontSize, 2);
        Assert.Equal(0.5, header.Opacity);
        Assert.Equal(new Thickness(12, 4), header.Margin);

        // the filter field: .9em, 8 px by 12 px inside, 4 px around, 4 px corners
        ui.Press(Key.Escape);
        UiHelpers.Click(ui.Window, ui.SlashButton);
        var filter = ui.Filter!;
        Assert.Equal(11.7, filter.FontSize, 2);
        Assert.Equal(new Thickness(12, 8), filter.Padding);
        Assert.Equal(new Thickness(4), filter.Margin);
        Assert.Equal(new CornerRadius(4), filter.CornerRadius);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Texts_follow_the_strings()
    {
        var c = WithCommands(new ChatCommand("hello", "", ""));
        var ui = Open(c);
        ui.View.Strings = ChatStrings.PortugueseBrazil;
        UiHelpers.Pump();
        UiHelpers.Click(ui.Window, UiHelpers.ButtonWithText(ui.Composer, "Mostrar o menu de comandos (/)"));
        Assert.Equal("Filtrar comandos…", ui.Filter!.Watermark);
        Assert.Contains("Comandos com barra", UiHelpers.Texts(ui.Menu));
        Assert.Equal("Mostrar o uso do contexto", ToolTip.GetTip(Row(ui, "/context")));
        UiHelpers.Type(ui.Window, "zzz");
        Assert.Contains("Nenhum comando encontrado", UiHelpers.Texts(ui.Menu));
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Another_conversation_closes_the_palette()
    {
        var ui = Open();
        ui.Slash("");
        Assert.True(ui.Palette.IsOpen);
        ui.View.Conversation = new ChatConversation();
        UiHelpers.Pump();
        Assert.False(ui.Palette.IsOpen);
        ui.Window.Close();
    }

    // ---- when the palette must not act ----

    private static PermissionRequested Request(string id) =>
        new PermissionRequested(id, "Bash", "Bash", null, "t-" + id, JsonDocument.Parse("{\"command\":\"ls\"}").RootElement.Clone(), null);

    [AvaloniaFact]
    public void Read_only_hidden_or_a_permission_request_close_the_palette()
    {
        var ui = Open();

        // read-only hides the reply box: the palette goes with it, and nothing pressed after sends anything
        ui.Slash("hel");
        Assert.True(ui.Palette.IsOpen);
        ui.View.IsReadOnly = true;
        UiHelpers.Pump();
        Assert.False(ui.Palette.IsOpen);
        ui.Press(Key.Enter);
        Assert.Empty(ui.Sent);
        ui.View.IsReadOnly = false;
        UiHelpers.Pump();
        Assert.False(ui.Palette.IsOpen);

        // the host hides an ancestor (Avalonia 11.0 does not tell the reply box): the palette stays drawn, but a row
        // or Enter in its filter field sends nothing, and closes it
        ui.Input.Clear();
        UiHelpers.Click(ui.Window, ui.SlashButton);
        var row = Row(ui, "/hello");
        ui.View.IsVisible = false;
        UiHelpers.Pump();
        UiHelpers.Click(ui.Window, row);
        Assert.Empty(ui.Sent);
        Assert.False(ui.Palette.IsOpen);
        ui.View.IsVisible = true;
        UiHelpers.Pump();
        UiHelpers.Click(ui.Window, ui.SlashButton);
        UiHelpers.Type(ui.Window, "hel");
        ui.View.IsVisible = false;
        UiHelpers.Pump();
        ui.Press(Key.Enter);
        Assert.Empty(ui.Sent);
        Assert.Empty(ui.Invoked);
        Assert.False(ui.Palette.IsOpen);
        ui.View.IsVisible = true;
        UiHelpers.Pump();

        // a permission request comes first: the palette closes, and does not open while the claude waits on it
        ui.Input.Clear();
        ui.Slash("");
        Assert.True(ui.Palette.IsOpen);
        ui.Conversation.Apply(Request("r1"));
        UiHelpers.Pump();
        Assert.False(ui.Palette.IsOpen);
        UiHelpers.Click(ui.Window, ui.SlashButton);
        Assert.False(ui.Palette.IsOpen);
        ui.Conversation.DismissPermission("r1");
        UiHelpers.Pump();
        UiHelpers.Click(ui.Window, ui.SlashButton);
        Assert.True(ui.Palette.IsOpen);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Esc_with_the_palette_open_does_not_stop_claude()
    {
        var ui = Open();
        ui.Conversation.Submit("go");
        UiHelpers.Pump();
        Assert.True(ui.Conversation.IsBusy);

        ui.Slash("");
        ui.Press(Key.Escape);
        Assert.False(ui.Palette.IsOpen);
        Assert.Equal(0, ui.Stops);

        UiHelpers.Click(ui.Window, ui.SlashButton);
        ui.Press(Key.Escape);
        Assert.False(ui.Palette.IsOpen);
        Assert.Equal(0, ui.Stops);

        ui.Press(Key.Escape); // closed: the Esc stops Claude again
        Assert.Equal(1, ui.Stops);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Pasting_a_slash_does_not_open_the_palette()
    {
        TopLevel? top = null;
        try
        {
            ClipboardImages.HasTextOverride = () => true;
            ClipboardImages.HasImageOverride = () => false;
            var ui = Open();
            top = ui.Window;
            ui.Window.Clipboard!.SetTextAsync("/").Wait();
            ui.Input.Focus();
            UiHelpers.Press(ui.Window, Key.V, RawInputModifiers.Control);
            // the paste is asynchronous: pump until the text arrives (bounded by PumpUntil), then look at the palette
            UiHelpers.PumpUntil(() => ui.Input.Text == "/");
            for (var i = 0; i < 10; i++) UiHelpers.Pump(); // and let a palette that would wrongly open show up
            Assert.Equal("/", ui.Input.Text);
            Assert.False(ui.Palette.IsOpen);
            ui.Window.Close();
        }
        finally
        {
            try { top?.Clipboard?.ClearAsync().Wait(); } catch (Exception) { /* nothing left to clear */ }
            ClipboardImages.HasTextOverride = null;
            ClipboardImages.HasImageOverride = null;
        }
    }

    [AvaloniaFact]
    public void Leaving_the_window_closes_the_palette()
    {
        var ui = Open();
        UiHelpers.Click(ui.Window, ui.SlashButton);
        Assert.True(ui.Palette.IsOpen);
        ui.Window.Content = null;
        UiHelpers.Pump();
        Assert.False(ui.Palette.IsOpen);
        ui.Window.Close();
    }

    private static Border Row(Opened ui, string label) =>
        ui.Menu.GetVisualDescendants().OfType<Border>()
            .Single(b => b.IsEffectivelyVisible && b.Classes.Contains("ClaudeChatMenuItem") && Label(b).Text == label);

    private static TextBlock Label(Border row) => row.GetVisualDescendants().OfType<TextBlock>().First();

    private static object? Resource(Control view, string key) =>
        view.TryFindResource(key, out var value) ? value : null;
}
