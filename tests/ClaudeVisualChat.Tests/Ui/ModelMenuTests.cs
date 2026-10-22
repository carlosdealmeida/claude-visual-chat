using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using ClaudeVisualChat.Protocol;
using Xunit;

namespace ClaudeVisualChat.Tests.Ui;

public class ModelMenuTests
{
    private static readonly string[] AllLevels = { "low", "medium", "high", "xhigh", "max" };

    private static readonly IReadOnlyList<ChatModel> Models = new[]
    {
        new ChatModel("opus", "claude-opus-5-5", "Opus 5.5", "For complex work and everyday tasks", AllLevels),
        new ChatModel("sonnet", "claude-sonnet-5-5", "Sonnet 5.5", "Most efficient for simpler tasks", AllLevels),
        new ChatModel("haiku", "claude-haiku-4-5-20251001", "Haiku 4.5", "Fastest for quick answers", new string[0]),
    };

    private sealed class Opened
    {
        public Opened(ChatConversation conversation)
        {
            Conversation = conversation;
            conversation.ModelRequested += (_, e) => Requested.Add(e.Model);
            conversation.EffortRequested += (_, e) => Efforts.Add(e.Effort);
            conversation.MessageSubmitted += (_, e) => Sent.Add(e.Text);
            View = new ClaudeChatView { Conversation = conversation };
            Window = UiHelpers.Show(View);
            Composer = UiHelpers.Composer(View);
            Chip = Composer.GetVisualDescendants().OfType<ModelChip>().Single();
            Menu = Composer.GetLogicalDescendants().OfType<ModelMenu>().Single();
            Unhandled = UiHelpers.UnhandledKeys(Window);
        }

        public ChatConversation Conversation { get; }
        public ClaudeChatView View { get; }
        public Window Window { get; }
        public ChatComposer Composer { get; }
        public ModelChip Chip { get; }
        public ModelMenu Menu { get; }
        public List<Key> Unhandled { get; }
        public List<string> Requested { get; } = new List<string>();
        public List<string?> Efforts { get; } = new List<string?>();
        public List<string> Sent { get; } = new List<string>();

        public List<string> ChipTexts => UiHelpers.Texts(Chip);

        public Control Content => Menu.Child!;

        public List<string> MenuTexts => UiHelpers.Texts(Content);

        public List<Border> Rows => Content.GetVisualDescendants().OfType<Border>()
            .Where(b => b.IsEffectivelyVisible && b.Classes.Contains("ClaudeChatMenuItem")).ToList();

        public Border Row(string text) => Rows.Single(r => UiHelpers.Texts(r).Contains(text));

        public string? Active => Rows.Where(r => r.Classes.Contains("active")).Select(r => UiHelpers.Texts(r).First()).SingleOrDefault();

        public Border EffortRow => Rows.Single(r => r.Classes.Contains("effort"));

        public void Press(Key key) => UiHelpers.Press(Window, key);
    }

    private static Opened Open(string? model = "opus", string? effort = "high")
    {
        var c = new ChatConversation { AvailableModels = Models };
        if (model != null) c.Apply(new SessionStarted("s1", model, null, "default", "2.1.306"));
        if (effort != null) c.Apply(new SettingsApplied(null, effort, true));
        return new Opened(c);
    }

    private static object? Resource(Control view, string key) =>
        view.TryFindResource(key, out var value) ? value : null;

    // ---- the brief ----

    [AvaloniaFact]
    public void Model_chip_shows_model_and_effort()
    {
        TestLog.Instance.Clear();
        var ui = Open();
        Assert.True(ui.Chip.IsEffectivelyVisible);
        Assert.Equal(new[] { "Opus 5.5", "High" }, ui.ChipTexts);
        Assert.Equal("Opus 5.5 High", AutomationProperties.GetName(ui.Chip));
        var level = ui.Chip.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "High");
        Assert.Same(Resource(ui.View, "ClaudeChat.ForegroundMuted"), level.Foreground);
        Assert.Equal(4, level.Margin.Left);

        // no effort reported: the model decides
        ui.Conversation.Apply(new SettingsApplied(null, null, true));
        UiHelpers.Pump();
        Assert.Equal(new[] { "Opus 5.5", "Auto" }, ui.ChipTexts);

        // a model without effort: only its name
        ui.Conversation.Apply(new SessionStarted(null, "claude-haiku-4-5-20251001", null, null, null));
        UiHelpers.Pump();
        Assert.Equal(new[] { "Haiku 4.5" }, ui.ChipTexts);
        Assert.Equal("Haiku 4.5", AutomationProperties.GetName(ui.Chip));

        // an id with a suffix finds its model; the chip shows the suffix and the level
        ui.Conversation.Apply(new SessionStarted(null, "claude-opus-5-5[1m]", null, null, null));
        UiHelpers.Pump();
        Assert.Equal(new[] { "Opus 5.5 [1m]", "Auto" }, ui.ChipTexts);
        Assert.Equal("Opus 5.5 [1m] Auto", AutomationProperties.GetName(ui.Chip));
        Assert.True(ui.Menu.Child != null);

        // a model the list does not have: the raw id
        ui.Conversation.Apply(new SessionStarted(null, "claude-mystery-1", null, null, null));
        UiHelpers.Pump();
        Assert.Equal(new[] { "claude-mystery-1" }, ui.ChipTexts);

        // asked and not confirmed: pending, until the claude reports
        ui.Conversation.Apply(new SessionStarted(null, "opus", null, null, null));
        UiHelpers.Pump();
        Assert.DoesNotContain("pending", ui.Chip.Classes);
        Assert.Equal(FontStyle.Normal, ui.Chip.GetVisualDescendants().OfType<TextBlock>().First().FontStyle);
        ui.Conversation.SetModel("sonnet");
        UiHelpers.Pump();
        Assert.Contains("pending", ui.Chip.Classes);
        Assert.Equal(FontStyle.Italic, ui.Chip.GetVisualDescendants().OfType<TextBlock>().First().FontStyle);
        Assert.Equal("Opus 5.5", ui.ChipTexts[0]); // the chip shows what the claude confirmed
        ui.Conversation.Apply(new SessionStarted(null, "sonnet", null, null, null));
        UiHelpers.Pump();
        Assert.DoesNotContain("pending", ui.Chip.Classes);
        Assert.Equal(new[] { "Sonnet 5.5", "Auto" }, ui.ChipTexts);
        ui.Conversation.SetEffort("low");
        UiHelpers.Pump();
        Assert.Contains("pending", ui.Chip.Classes);
        ui.Conversation.Apply(new SettingsApplied(null, "low", true));
        UiHelpers.Pump();
        Assert.DoesNotContain("pending", ui.Chip.Classes);
        Assert.Equal(new[] { "Sonnet 5.5", "Low" }, ui.ChipTexts);
        Assert.Empty(ui.Requested.Skip(1));
        Assert.Empty(TestLog.Instance.Entries); // no binding or property warnings
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Without_models_and_model_there_is_no_chip()
    {
        var c = new ChatConversation();
        var ui = new Opened(c);
        Assert.False(ui.Chip.IsEffectivelyVisible);

        // a model reported with no list: the raw id, and a click opens nothing
        c.Apply(new SessionStarted("s1", "claude-opus-5-5", null, "default", "2.1.306"));
        UiHelpers.Pump();
        Assert.True(ui.Chip.IsEffectivelyVisible);
        Assert.Equal(new[] { "claude-opus-5-5" }, ui.ChipTexts);
        UiHelpers.Click(ui.Window, ui.Chip);
        Assert.False(ui.Menu.IsOpen);

        // the list arrives: the model takes its name
        c.AvailableModels = Models;
        UiHelpers.Pump();
        Assert.Equal(new[] { "Opus 5.5", "Auto" }, ui.ChipTexts);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Model_menu_lists_models_and_effort_levels()
    {
        var ui = Open();
        UiHelpers.Click(ui.Window, ui.Chip);
        Assert.True(ui.Menu.IsOpen);
        Assert.True(ui.Chip.IsFocused);

        // the title, then each model with its description, the current one checked and marked
        Assert.Equal(new[]
        {
            "Select a model",
            "Opus 5.5", "For complex work and everyday tasks",
            "Sonnet 5.5", "Most efficient for simpler tasks",
            "Haiku 4.5", "Fastest for quick answers",
            "Effort", "(High)",
        }, ui.MenuTexts);
        Assert.True(Check(ui.Row("Opus 5.5")).IsEffectivelyVisible);
        Assert.False(Check(ui.Row("Sonnet 5.5")).IsEffectivelyVisible);
        Assert.Equal("Opus 5.5", ui.Active);

        // the effort row: one notch per level of the model
        Assert.Equal(5, Notches(ui).Count);

        // a click on the row goes to the next level, round the end
        UiHelpers.Click(ui.Window, Label(ui.EffortRow));
        Assert.Equal(new string?[] { "xhigh" }, ui.Efforts);
        Assert.Contains("(Extra high)", ui.MenuTexts); // what was asked, while the claude has not confirmed it
        UiHelpers.Click(ui.Window, Label(ui.EffortRow));
        UiHelpers.Click(ui.Window, Label(ui.EffortRow));
        Assert.Equal(new string?[] { "xhigh", "max", "low" }, ui.Efforts);
        Assert.True(ui.Menu.IsOpen); // the menu stays

        // a click on a notch picks that level
        UiHelpers.Click(ui.Window, Notches(ui)[1]);
        Assert.Equal("medium", ui.Efforts.Last());
        Assert.Contains("(Medium)", ui.MenuTexts);
        Assert.True(ui.Menu.IsOpen);

        // the pointer marks the row it is on; the arrows move the mark (round the ends), Enter picks
        Assert.Equal("Effort", ui.Active);
        ui.Press(Key.Down);
        Assert.Equal("Opus 5.5", ui.Active);
        ui.Press(Key.Down);
        Assert.Equal("Sonnet 5.5", ui.Active);
        ui.Press(Key.Up);
        ui.Press(Key.Up);
        Assert.Equal("Effort", ui.Active);
        ui.Press(Key.Enter);
        Assert.Equal("high", ui.Efforts.Last()); // Enter on the effort row: the next level
        Assert.True(ui.Menu.IsOpen);
        ui.Press(Key.Down);
        Assert.Equal("Opus 5.5", ui.Active);
        ui.Press(Key.Down);
        ui.Press(Key.Enter);
        Assert.Equal(new[] { "sonnet" }, ui.Requested);
        Assert.False(ui.Menu.IsOpen);
        Assert.True(ui.Chip.IsFocused);
        foreach (var key in new[] { Key.Up, Key.Down, Key.Enter }) Assert.DoesNotContain(key, ui.Unhandled);
        Assert.Empty(ui.Sent);

        // Esc closes it; the Esc is not the app's
        ui.Press(Key.Enter); // the chip's own key opens it
        Assert.True(ui.Menu.IsOpen);
        ui.Press(Key.Escape);
        Assert.False(ui.Menu.IsOpen);
        Assert.DoesNotContain(Key.Escape, ui.Unhandled);

        // a click on a model picks it; a click on the chip closes the menu
        UiHelpers.Click(ui.Window, ui.Chip);
        UiHelpers.Click(ui.Window, ui.Row("Haiku 4.5"));
        Assert.Equal(new[] { "sonnet", "haiku" }, ui.Requested);
        Assert.False(ui.Menu.IsOpen);
        UiHelpers.Click(ui.Window, ui.Chip);
        Assert.True(ui.Menu.IsOpen);
        UiHelpers.Click(ui.Window, ui.Chip);
        Assert.False(ui.Menu.IsOpen);

        // a model without effort has no effort row
        ui.Conversation.Apply(new SessionStarted(null, "haiku", null, null, null));
        UiHelpers.Pump();
        UiHelpers.Click(ui.Window, ui.Chip);
        Assert.DoesNotContain("Effort", ui.MenuTexts);
        Assert.True(Check(ui.Row("Haiku 4.5")).IsEffectivelyVisible);
        ui.Window.Close();
    }

    // ---- the look ----

    [AvaloniaFact]
    public void Chip_and_menu_have_the_extension_measures()
    {
        var ui = Open();
        // the chip: an 18 px pill, 8 px at the sides, .85em, 6 px after what comes before it
        Assert.Equal(18, ui.Chip.Bounds.Height, 1);
        Assert.Equal(new CornerRadius(9), ui.Chip.CornerRadius);
        Assert.Equal(new Thickness(8, 0), ui.Chip.Padding);
        Assert.Equal(6, ui.Chip.Margin.Left);
        Assert.Equal(11.05, ui.Chip.FontSize, 2);
        Assert.Same(Resource(ui.View, "ClaudeChat.PillBackground"), ui.Chip.Background);
        Assert.Same(Resource(ui.View, "ClaudeChat.Foreground"), ui.Chip.Foreground);

        UiHelpers.Click(ui.Window, ui.Chip);
        var frame = (Border)ui.Content;
        var box = ui.Composer.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("ClaudeChatComposer"));
        Assert.Equal(box.Bounds.Width, frame.Bounds.Width, 1);
        var frameBottom = frame.TranslatePoint(new Point(0, frame.Bounds.Height), ui.Window)!.Value;
        Assert.Equal(box.TranslatePoint(default, ui.Window)!.Value.Y - 8, frameBottom.Y, 1);
        Assert.Same(Resource(ui.View, "ClaudeChat.MenuBackground"), frame.Background);

        // a model row: the name and the description at half opacity, both in 15.6 px lines
        var row = ui.Row("Opus 5.5");
        Assert.Equal(new Thickness(8, 4), row.Padding);
        Assert.Equal(new Thickness(4, 0), row.Margin);
        var description = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "For complex work and everyday tasks");
        Assert.Equal(0.5, description.Opacity);
        Assert.Equal(11.05, description.FontSize, 2);
        Assert.Equal(15.6, description.LineHeight, 2);
        Assert.InRange(row.Bounds.Height, 8 + 15.6 * 2, 8 + 16 * 2); // each line rounded to the pixel

        // the slider: a 76 × 18 track, a 14 px thumb 2 px inside, 4 px notches; at "high", the thumb on the middle one
        var track = ui.EffortRow.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatEffortTrack"));
        Assert.Equal(new Size(76, 18), track.Bounds.Size);
        Assert.Same(Resource(ui.View, "ClaudeChat.InputBorder"), track.Background);
        var thumb = track.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatEffortThumb"));
        Assert.Equal(new Size(14, 14), thumb.Bounds.Size);
        var thumbAt = thumb.TranslatePoint(default, track)!.Value;
        Assert.Equal(2 + 0.5 * 58, thumbAt.X, 1);
        Assert.Equal(2, thumbAt.Y, 1);
        var fill = track.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatEffortFill"));
        Assert.Equal(18 + 0.5 * 58, fill.Bounds.Width, 1);
        Assert.Same(Resource(ui.View, "ClaudeChat.InputActiveBorder"), fill.Background);
        var notches = Notches(ui);
        Assert.All(notches, n => Assert.Equal(new Size(4, 4), n.Bounds.Size));
        Assert.Equal(9, notches[0].TranslatePoint(new Point(2, 2), track)!.Value.X, 1);
        Assert.Equal(9 + 58, notches[4].TranslatePoint(new Point(2, 2), track)!.Value.X, 1);
        Assert.Equal(9, notches[0].TranslatePoint(new Point(2, 2), track)!.Value.Y, 1);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Texts_follow_the_strings()
    {
        var ui = Open(effort: "xhigh");
        ui.View.Strings = ChatStrings.PortugueseBrazil;
        UiHelpers.Pump();
        Assert.Equal(new[] { "Opus 5.5", "Muito alto" }, ui.ChipTexts);
        Assert.Equal("Trocar o modelo", ToolTip.GetTip(ui.Chip));
        UiHelpers.Click(ui.Window, ui.Chip);
        Assert.Contains("Selecione um modelo", ui.MenuTexts);
        Assert.Contains("Esforço", ui.MenuTexts);
        Assert.Contains("(Muito alto)", ui.MenuTexts);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Another_conversation_closes_the_menu()
    {
        var ui = Open();
        UiHelpers.Click(ui.Window, ui.Chip);
        Assert.True(ui.Menu.IsOpen);
        ui.View.Conversation = new ChatConversation { AvailableModels = Models };
        UiHelpers.Pump();
        Assert.False(ui.Menu.IsOpen);
        // no model reported yet (the claude tells at the first message): "Model", as the extension's chip
        Assert.Equal(new[] { "Model" }, ui.ChipTexts);
        Assert.Equal("Switch model", ToolTip.GetTip(ui.Chip));
        UiHelpers.Click(ui.Window, ui.Chip);
        Assert.True(ui.Menu.IsOpen);
        Assert.Null(ui.Active);
        Assert.DoesNotContain("Effort", ui.MenuTexts);
        ui.Window.Close();
    }

    // ---- when the menu must not act ----

    [AvaloniaFact]
    public void Read_only_hidden_or_a_permission_request_close_the_menu()
    {
        var ui = Open();
        UiHelpers.Click(ui.Window, ui.Chip);
        Assert.True(ui.Menu.IsOpen);
        ui.View.IsReadOnly = true;
        UiHelpers.Pump();
        Assert.False(ui.Menu.IsOpen);
        ui.Press(Key.Enter);
        Assert.Empty(ui.Requested);
        ui.View.IsReadOnly = false;
        UiHelpers.Pump();

        // an ancestor hidden by the host: a row, a notch or the effort row picks nothing, and closes the menu
        foreach (var target in new System.Func<Control>[] { () => ui.Row("Sonnet 5.5"), () => Notches(ui)[1], () => Label(ui.EffortRow) })
        {
            UiHelpers.Click(ui.Window, ui.Chip);
            Assert.True(ui.Menu.IsOpen);
            var control = target();
            ui.View.IsVisible = false;
            UiHelpers.Pump();
            UiHelpers.Click(ui.Window, control);
            Assert.False(ui.Menu.IsOpen);
            ui.View.IsVisible = true;
            UiHelpers.Pump();
        }
        Assert.Empty(ui.Requested);
        Assert.Empty(ui.Efforts);

        // a permission request comes first: the menu closes, and the chip opens nothing while the claude waits on it
        UiHelpers.Click(ui.Window, ui.Chip);
        Assert.True(ui.Menu.IsOpen);
        ui.Conversation.Apply(new PermissionRequested("r1", "Bash", "Bash", null, "t1",
            System.Text.Json.JsonDocument.Parse("{\"command\":\"ls\"}").RootElement.Clone(), null));
        UiHelpers.Pump();
        Assert.False(ui.Menu.IsOpen);
        UiHelpers.Click(ui.Window, ui.Chip);
        Assert.False(ui.Menu.IsOpen);
        ui.Conversation.DismissPermission("r1");
        UiHelpers.Pump();
        UiHelpers.Click(ui.Window, ui.Chip);
        Assert.True(ui.Menu.IsOpen);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Leaving_the_window_closes_the_menu()
    {
        var ui = Open();
        UiHelpers.Click(ui.Window, ui.Chip);
        Assert.True(ui.Menu.IsOpen);
        ui.Window.Content = null;
        UiHelpers.Pump();
        Assert.False(ui.Menu.IsOpen);
        ui.Window.Close();
    }

    private static Control Check(Border row) => row.GetVisualDescendants().OfType<Path>().Single();

    private static TextBlock Label(Border row) => row.GetVisualDescendants().OfType<TextBlock>().First();

    private static List<Ellipse> Notches(Opened ui) =>
        ui.EffortRow.GetVisualDescendants().OfType<Ellipse>().Where(e => e.Classes.Contains("ClaudeChatEffortNotch")).ToList();
}
