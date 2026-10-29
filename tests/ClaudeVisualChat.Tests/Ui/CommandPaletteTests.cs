using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Layout;
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
            .Select(b => b.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("ClaudeChatMenuLabel")).Text).SingleOrDefault();

        /// <summary>The slash commands listed, top to bottom.</summary>
        public List<string> Commands => Labels.Where(l => l.StartsWith("/", StringComparison.Ordinal)).ToList();

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
        // with no filter the button lists the actions only; the slash commands come once something is typed in the field
        Assert.DoesNotContain("Slash Commands", UiHelpers.Texts(ui.Menu));
        Assert.Equal(new[] { "Attach file…", "Bookmarks", "Account & usage…", "Focus view" }, ui.Labels);
        UiHelpers.Type(ui.Window, "hel");
        Assert.Contains("Slash Commands", UiHelpers.Texts(ui.Menu));
        Assert.Equal(new[] { "/hello", "/help" }, ui.Labels);
        UiHelpers.Click(ui.Window, ui.SlashButton);
        Assert.False(ui.Palette.IsOpen);

        // a "/" typed in the empty box opens it, filtered by what follows; the box keeps the keyboard
        ui.Type("/");
        Assert.True(ui.Palette.IsOpen);
        Assert.Null(ui.Filter);
        Assert.True(ui.Input.IsFocused);
        // every group, unfiltered, as in the extension: the actions, and the slash commands in their place, in alphabetical order
        Assert.Equal(new[] { "Context", "Model", "Settings", "Slash Commands" }, Headers(ui));
        Assert.Equal(new[] { "Attach file…", "Bookmarks", "Account & usage…", "Focus view" }, ui.Labels.Where(l => !l.StartsWith("/", StringComparison.Ordinal)));
        Assert.Equal(new[] { "/bashes", "/chrome", "/compact", "/context", "/copy", "/hello", "/help", "/rc", "/remote-control", "/tasks", "/usage" }, ui.Commands);
        Assert.Equal("View and manage everything running in the background", ToolTip.GetTip(Row(ui, "/tasks")));
        Assert.Equal("View and manage everything running in the background", ToolTip.GetTip(Row(ui, "/bashes")));
        Assert.Equal("Show context usage", ToolTip.GetTip(Row(ui, "/context")));
        Assert.Equal("Say hello", ToolTip.GetTip(Row(ui, "/hello")));
        Assert.Equal("Copy Claude's last response to clipboard (or /copy N for the Nth-latest)", ToolTip.GetTip(Row(ui, "/copy")));
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
        ui.Conversation.ClosePanel(); // the modal took the keyboard; closing it gives it back to the box
        UiHelpers.Pump();
        Assert.True(ui.Input.IsFocused);

        // the arrows move the mark, round the ends; Enter with no row marked sends what was typed
        ui.Slash("he");
        Assert.Equal(new[] { "/hello", "/help", "/bashes", "/chrome", "/compact", "/copy", "/tasks" }, ui.Commands); // "the" in their descriptions, in alphabetical order
        Assert.Equal("/hello", ui.Active);
        ui.Press(Key.Up);
        Assert.Equal("/tasks", ui.Active);
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
        UiHelpers.Type(ui.Window, "usa");
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
        // the names that start with it, alphabetically; then the names that have it; then the descriptions
        // (the conversation's own commands come after the claude's, rc and chrome by their texts)
        Assert.Equal(new[] { "/compact", "/context", "/copy", "/cost", "/account", "/remote-control", "/chrome", "/rc", "/review" }, ui.Commands);

        UiHelpers.Type(ui.Window, "st");
        // the exact name first, then the one whose description has the word
        Assert.Equal(new[] { "/cost", "/compact" }, ui.Commands);
        Assert.Equal("/cost", ui.Active);

        // the case does not matter, and the conversation's own commands are found by their texts
        ui.Input.Clear();
        ui.Slash("BROWSER");
        Assert.Equal(new[] { "/chrome" }, ui.Commands);
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
        Assert.Equal(new[] { "Anexar arquivo…", "Respostas marcadas", "Conta e uso…", "Modo foco" }, ui.Labels);
        Assert.Contains("Contexto", UiHelpers.Texts(ui.Menu));
        Assert.Contains("Modelo", UiHelpers.Texts(ui.Menu));
        UiHelpers.Type(ui.Window, "cont");
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
        UiHelpers.Type(ui.Window, "hel");
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

    // ---- the groups and the rows that are not commands (0.6.0) ----

    private static List<string> Headers(Opened ui) =>
        ui.Menu.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && t.Classes.Contains("ClaudeChatMenuHeader")).Select(t => t.Text ?? "").ToList();

    private static List<Border> Dividers(Opened ui) =>
        ui.Menu.GetVisualDescendants().OfType<Border>().Where(b => b.IsEffectivelyVisible && b.Classes.Contains("ClaudeChatMenuDivider")).ToList();

    /// <summary>A conversation that has models with an effort, and a host that opens links: every group of this task has a row.</summary>
    private static ChatConversation Complete()
    {
        var c = TouchSamples.Opus();
        c.Commands = new[] { new ChatCommand("review", "Review a pull request", "<pr>") };
        c.LinkRequested += (_, _) => { };
        return c;
    }

    private static string? Value(Border row) =>
        row.GetVisualDescendants().OfType<TextBlock>().SingleOrDefault(t => t.Classes.Contains("ClaudeChatMenuValue"))?.Text;

    [AvaloniaFact]
    public void The_palette_lists_context_model_and_slash_commands_sections_in_order()
    {
        var ui = Open(Complete());
        UiHelpers.Click(ui.Window, ui.SlashButton);
        UiHelpers.Type(ui.Window, "e");   // the filter keeps the rows with an "e": a row of each group has one

        // the groups in their order, the empty one (Customize) not there, a divider of 1 px with 4 px around it between them
        Assert.Equal(new[] { "Context", "Model", "Settings", "Slash Commands", "Support" }, Headers(ui));
        var dividers = Dividers(ui);
        Assert.Equal(4, dividers.Count);
        Assert.All(dividers, d =>
        {
            Assert.Equal(1, d.Bounds.Height, 1);
            Assert.Equal(new Thickness(0, 4), d.Margin);
            Assert.Same(Resource(ui.View, "ClaudeChat.InputBorder"), d.Background);
        });
        var labels = ui.Labels;
        Assert.Equal("Attach file…", labels.First());
        Assert.Equal("View help docs", labels.Last());
        Assert.True(labels.IndexOf("Switch model…") < labels.IndexOf("/review"));
        Assert.True(labels.IndexOf("Account & usage…") < labels.IndexOf("/review"));

        // inside a group the order is the filter's: the name that starts with it, then the ones that have it, in the list's order
        Assert.Equal(new[] { "Effort", "Switch model…", "Account & usage…" },
            labels.Where(l => l != "Attach file…" && l != "View help docs" && l != "Focus view" && !l.StartsWith("/", StringComparison.Ordinal)));

        // the model on the right of its row, the level on the right of the effort's
        Assert.Equal("Opus 5.5", Value(Row(ui, "Switch model…")));
        Assert.Equal("High", Value(Row(ui, "Effort")));
        ui.Window.Close();

        // without models neither the model nor the effort has a row; what is left of the group stays
        var bare = Open();
        UiHelpers.Click(bare.Window, bare.SlashButton);
        Assert.Equal(new[] { "Attach file…", "Bookmarks", "Account & usage…", "Focus view" }, bare.Labels);
        Assert.Equal(new[] { "Context", "Model", "Settings" }, Headers(bare));
        Assert.Equal(2, Dividers(bare).Count);
        bare.Window.Close();
    }

    [AvaloniaFact]
    public void Slash_commands_hide_when_opened_by_the_button_without_a_filter()
    {
        var ui = Open(Complete());
        UiHelpers.Click(ui.Window, ui.SlashButton);
        Assert.DoesNotContain("Slash Commands", Headers(ui));
        Assert.DoesNotContain(ui.Labels, l => l.StartsWith("/", StringComparison.Ordinal));

        // they come with the first letter of the filter, and go with the last
        UiHelpers.Type(ui.Window, "r");
        Assert.Contains("Slash Commands", Headers(ui));
        Assert.Contains("/review", ui.Labels);
        ui.Press(Key.Back);
        Assert.DoesNotContain("Slash Commands", Headers(ui));
        Assert.DoesNotContain(ui.Labels, l => l.StartsWith("/", StringComparison.Ordinal));
        ui.Press(Key.Escape);

        // a "/" typed in the box lists every group, Slash Commands in its place, as the extension does; text after it filters them all
        ui.Type("/");
        Assert.Equal(new[] { "Context", "Model", "Settings", "Slash Commands", "Support" }, Headers(ui));
        Assert.Contains("/review", ui.Labels);
        Assert.Contains("Attach file…", ui.Labels);
        UiHelpers.Type(ui.Window, "rev");
        Assert.Equal(new[] { "Slash Commands" }, Headers(ui));
        Assert.Equal(new[] { "/review" }, ui.Labels);
        UiHelpers.Press(ui.Window, Key.Escape);
        ui.Input.Clear();
        ui.Slash("att");
        Assert.Equal(new[] { "Context" }, Headers(ui));       // the filter runs every group, and a group with no match is not there
        Assert.Equal(new[] { "Attach file…" }, ui.Labels);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void View_help_docs_shows_only_with_a_link_subscriber()
    {
        var c = new ChatConversation();
        var ui = Open(c);
        UiHelpers.Click(ui.Window, ui.SlashButton);
        Assert.DoesNotContain("View help docs", ui.Labels);   // no host that opens links: nothing to offer
        Assert.DoesNotContain("Support", Headers(ui));
        UiHelpers.Click(ui.Window, ui.SlashButton);

        var links = new List<string>();
        EventHandler<LinkRequestedEventArgs> onLink = (_, e) => links.Add(e.Url);
        c.LinkRequested += onLink;
        UiHelpers.Click(ui.Window, ui.SlashButton);
        Assert.Equal("Support", Headers(ui).Last());
        UiHelpers.Click(ui.Window, Row(ui, "View help docs"));
        Assert.Equal("https://code.claude.com/docs/en/vs-code", CommandPalette.HelpDocsUrl);   // what the extension opens
        Assert.Equal(new[] { CommandPalette.HelpDocsUrl }, links);
        Assert.False(ui.Palette.IsOpen);
        Assert.True(ui.Input.IsFocused);

        // the host that stops listening takes the row away again
        c.LinkRequested -= onLink;
        UiHelpers.Click(ui.Window, ui.SlashButton);
        Assert.DoesNotContain("View help docs", ui.Labels);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void The_model_row_opens_the_model_menu_and_the_effort_row_changes_the_level_and_stays()
    {
        var c = Complete();
        var asked = new List<string?>();
        c.EffortRequested += (_, e) => asked.Add(e.Effort);
        var ui = Open(c);
        UiHelpers.Click(ui.Window, ui.SlashButton);

        // the effort goes to the next level, round the end, as the model menu's row does; the palette stays and shows the level asked for
        UiHelpers.Click(ui.Window, Row(ui, "Effort"));
        Assert.Equal(new[] { "xhigh" }, asked);
        Assert.True(ui.Palette.IsOpen);
        Assert.Equal("Extra high", Value(Row(ui, "Effort")));

        // "Switch model…" closes the palette and opens the model menu, whose keys the model chip takes
        var menu = ui.Composer.GetLogicalDescendants().OfType<ModelMenu>().Single();
        UiHelpers.Click(ui.Window, Row(ui, "Switch model…"));
        Assert.False(ui.Palette.IsOpen);
        Assert.True(menu.IsOpen);
        ui.Press(Key.Escape);
        Assert.False(menu.IsOpen);

        // "Account & usage…" is the /usage that runs here
        UiHelpers.Click(ui.Window, ui.SlashButton);
        UiHelpers.Click(ui.Window, Row(ui, "Account & usage…"));
        Assert.Equal(new[] { "usage" }, ui.Invoked);
        Assert.Equal(ChatPanel.Usage, c.ShownPanel);
        Assert.False(ui.Palette.IsOpen);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_switch_keeps_the_palette_open()
    {
        var on = false;
        var tested = new PaletteAction(PaletteSection.Settings, "Test switch", _ => on = !on, _ => on);
        CommandPalette.Actions.Add(tested);
        try
        {
            var ui = Open();
            UiHelpers.Click(ui.Window, ui.SlashButton);
            Assert.Contains("Settings", Headers(ui));
            var row = Row(ui, "Test switch");
            Border Track() => row.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatSwitch"));
            Border Thumb() => row.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatSwitchThumb"));

            // 32 x 18 with 9 px corners, in the input's border color; the thumb is 14 px, 2 px from the start
            Assert.Equal(new Size(32, 18), Track().Bounds.Size);
            Assert.Equal(new CornerRadius(9), Track().CornerRadius);
            Assert.Same(Resource(ui.View, "ClaudeChat.InputBorder"), Track().Background);
            Assert.Equal(new Size(14, 14), Thumb().Bounds.Size);
            Assert.Equal(new CornerRadius(7), Thumb().CornerRadius);
            Assert.Equal(2, Thumb().Margin.Left);
            var slide = Assert.IsType<Avalonia.Animation.ThicknessTransition>(Assert.Single(Thumb().Transitions!));
            Assert.Equal(Layoutable.MarginProperty, slide.Property);
            Assert.Equal(TimeSpan.FromSeconds(0.15), slide.Duration);

            // the press turns it on and the palette stays open, with the filter field still holding the keyboard; the track takes the accent
            // and the thumb goes to 16 px
            UiHelpers.Click(ui.Window, row);
            Assert.True(on);
            Assert.True(ui.Palette.IsOpen);
            Assert.True(ui.Filter!.IsFocused);
            Assert.Empty(ui.Sent);
            Assert.Contains("on", Track().Classes);
            Assert.Same(Resource(ui.View, "ClaudeChat.AccentFill"), Track().Background);
            UiHelpers.PumpUntil(() => { UiHelpers.DrawFrames(1); return Thumb().Margin.Left == 16; });   // the transition runs on the frames

            // and off again, by Enter on the marked row
            ui.Press(Key.Down);
            for (var i = 0; i < 20 && ui.Active != "Test switch"; i++) ui.Press(Key.Down);
            ui.Press(Key.Enter);
            Assert.False(on);
            Assert.True(ui.Palette.IsOpen);
            Assert.DoesNotContain("on", Track().Classes);
            ui.Window.Close();
        }
        finally
        {
            CommandPalette.Actions.Remove(tested);
        }
    }

    [AvaloniaFact]
    public void Touch_has_the_groups_and_a_switch_with_a_target_of_44_in_rows_of_the_height_they_had()
    {
        var on = false;
        var tested = new PaletteAction(PaletteSection.Settings, "Test switch", _ => on = !on, _ => on);
        CommandPalette.Actions.Add(tested);
        try
        {
            var view = UiHelpers.TouchView(Complete());
            var window = UiHelpers.ShowPhone(view);
            var composer = UiHelpers.Composer(view);
            var palette = composer.GetLogicalDescendants().OfType<CommandPalette>().Single();
            composer.OpenCommands();
            UiHelpers.Pump();
            var all = palette.Child!.GetVisualDescendants().ToList();
            var headers = all.OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && t.Classes.Contains("ClaudeChatMenuHeader")).Select(t => t.Text).ToList();
            Assert.Equal(new[] { "Contexto", "Modelo", "Configurações", "Suporte" }, headers);
            var rows = all.OfType<Border>().Where(b => b.IsEffectivelyVisible && b.Classes.Contains("ClaudeChatMenuItem")).ToList();
            Assert.All(rows, r => Assert.InRange(r.Bounds.Height, 43.5, 46));   // the rows of the 0.5.0: 44 px, the switch's target inside them

            // the test's switch (the Focus view's is another in the group)
            var testRow = rows.Single(r => r.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Classes.Contains("ClaudeChatMenuLabel") && t.Text == "Test switch"));
            var host = testRow.GetVisualDescendants().OfType<Panel>().Single(p => p.IsEffectivelyVisible && p.Classes.Contains("ClaudeChatSwitchHost"));
            Assert.True(host.Bounds.Width >= 43.5 && host.Bounds.Height >= 43.5, host.Bounds.ToString());
            var track = testRow.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatSwitch"));
            Assert.Equal(new Size(32, 18), track.Bounds.Size);                // the track is the same
            testRow.BringIntoView();                                          // under the Focus view's row: the list may have to scroll to it
            UiHelpers.Pump();
            // taps one after the other on the same switch are a double click to the pointer, and each of them counts: on, off, on
            UiHelpers.Click(window, host);
            Assert.True(on);
            Assert.True(palette.IsOpen);
            Assert.Contains("on", track.Classes);
            UiHelpers.Click(window, host);
            Assert.False(on);
            Assert.DoesNotContain("on", track.Classes);
            UiHelpers.Click(window, host);
            Assert.True(on);
            Assert.Contains("on", track.Classes);
            Assert.True(palette.IsOpen);

            // a press that moves past the tap threshold before it lifts is no tap
            var from = host.TranslatePoint(new Point(host.Bounds.Width / 2, host.Bounds.Height / 2), window)!.Value;
            window.MouseMove(from, RawInputModifiers.None);
            window.MouseDown(from, MouseButton.Left, RawInputModifiers.None);
            window.MouseMove(from + new Vector(0, 16), RawInputModifiers.LeftMouseButton);
            window.MouseUp(from + new Vector(0, 16), MouseButton.Left, RawInputModifiers.None);
            UiHelpers.Pump();
            Assert.True(on);
            window.Close();
        }
        finally
        {
            CommandPalette.Actions.Remove(tested);
        }
    }

    [AvaloniaFact]
    public void Touch_taps_one_after_the_other_on_the_effort_row_go_through_the_levels()
    {
        var c = Complete();
        var asked = new List<string?>();
        c.EffortRequested += (_, e) => asked.Add(e.Effort);
        var view = UiHelpers.TouchView(c);
        var window = UiHelpers.ShowPhone(view);
        var composer = UiHelpers.Composer(view);
        var palette = composer.GetLogicalDescendants().OfType<CommandPalette>().Single();
        composer.OpenCommands();
        UiHelpers.Pump();
        var effort = palette.Child!.GetVisualDescendants().OfType<Border>()
            .Single(b => b.IsEffectivelyVisible && b.Classes.Contains("ClaudeChatMenuItem") && b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Esforço"));
        UiHelpers.Click(window, effort);
        UiHelpers.Click(window, effort);
        UiHelpers.Click(window, effort);
        Assert.Equal(new[] { "xhigh", "max", "low" }, asked);   // from high, round the end; none of the taps is lost
        Assert.True(palette.IsOpen);
        window.Close();
    }

    [AvaloniaFact]
    public void Slash_commands_are_in_alphabetical_order_without_a_filter_and_the_filter_ranks_inside_its_groups()
    {
        var ui = Open(WithCommands(
            new ChatCommand("zeta", "Last", ""), new ChatCommand("Alpha", "First", ""), new ChatCommand("beta", "Second", "")));
        ui.Type("/");
        // no culture of the machine decides: the names, ignoring the case, then as written
        Assert.Equal(new[] { "/Alpha", "/bashes", "/beta", "/chrome", "/context", "/copy", "/rc", "/remote-control", "/tasks", "/usage", "/zeta" }, ui.Commands);
        UiHelpers.Type(ui.Window, "eta");   // both have it inside the name: the filter keeps the alphabetical order inside the group
        Assert.Equal(new[] { "/beta", "/zeta" }, ui.Commands);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_row_run_from_a_typed_slash_takes_the_typed_text_away()
    {
        var ui = Open();
        ui.Slash("usa");
        Assert.Equal("Account & usage…", ui.Active);   // the groups come in their order: the model's before the commands
        ui.Press(Key.Enter);
        Assert.Equal(new[] { "usage" }, ui.Invoked);
        Assert.Equal("", ui.Input.Text ?? "");
        Assert.False(ui.Palette.IsOpen);
        Assert.Empty(ui.Sent);
        ui.Window.Close();
    }

    private static Border Row(Opened ui, string label) =>
        ui.Menu.GetVisualDescendants().OfType<Border>()
            .Single(b => b.IsEffectivelyVisible && b.Classes.Contains("ClaudeChatMenuItem") && Label(b).Text == label);

    private static TextBlock Label(Border row) =>
        row.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("ClaudeChatMenuLabel"));

    private static object? Resource(Control view, string key) =>
        view.TryFindResource(key, out var value) ? value : null;
}
