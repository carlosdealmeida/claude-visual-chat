using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using ClaudeVisualChat.Protocol;
using Xunit;

namespace ClaudeVisualChat.Tests.Ui;

/// <summary>
/// The sheets of the touch layout (<see cref="ChatSheet"/>), through the model sheet: the pill of the reply box opens it
/// with the claude's models and the effort; it closes on the ✕, on the veil, and when the view takes its reason away.
/// </summary>
public class ModelSheetTests
{
    private sealed class Opened
    {
        public Opened(ChatConversation conversation, double height = UiHelpers.PhoneHeight)
        {
            Conversation = conversation;
            conversation.ModelRequested += (_, e) => Requested.Add(e.Model);
            conversation.EffortRequested += (_, e) => Efforts.Add(e.Effort);
            View = UiHelpers.TouchView(conversation);
            Window = UiHelpers.Show(View, UiHelpers.PhoneWidth, height);
            Composer = UiHelpers.Composer(View);
            Sheet = View.GetVisualDescendants().OfType<ModelSheet>().Single();
        }

        public ChatConversation Conversation { get; }
        public ClaudeChatView View { get; }
        public Window Window { get; }
        public ChatComposer Composer { get; }
        public ModelSheet Sheet { get; }
        public List<string> Requested { get; } = new List<string>();
        public List<string?> Efforts { get; } = new List<string?>();

        /// <summary>The model pill of the touch bar.</summary>
        public ModelChip Pill => Composer.GetVisualDescendants().OfType<ModelChip>().Single(c => c.Name == "TouchModelChip");

        public List<string> Texts => UiHelpers.Texts(Sheet);

        /// <summary>The rows the user can tap.</summary>
        public List<Button> Rows => Sheet.GetVisualDescendants().OfType<Button>()
            .Where(b => b.IsEffectivelyVisible && b.Classes.Contains("ClaudeChatSheetRow")).ToList();

        public Button Row(string text) => Rows.Single(r => UiHelpers.Texts(r).Contains(text));

        /// <summary>The checks the user can see.</summary>
        public List<Path> Checks => Sheet.GetVisualDescendants().OfType<Path>()
            .Where(p => p.IsEffectivelyVisible && p.Classes.Contains("ClaudeChatMenuCheck")).ToList();

        /// <summary>The row of the one check there is.</summary>
        public Button CheckedRow => Assert.Single(Checks).FindAncestorOfType<Button>()!;

        public void OpenSheet() => UiHelpers.Click(Window, Pill);

        public Button Header(string name) => UiHelpers.ButtonWithText(Sheet, name);

        public void Tap(Point at)
        {
            Window.MouseMove(at, RawInputModifiers.None);
            Window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
            Window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
            UiHelpers.Pump();
        }
    }

    private static T Part<T>(Visual root, string cssClass) where T : StyledElement =>
        root.GetVisualDescendants().OfType<T>().Single(c => c.Classes.Contains(cssClass));

    private static JsonElement J(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static PermissionRequested Permission() =>
        new PermissionRequested("r1", "Bash", "Bash", null, "t1", J("{\"command\":\"ls\"}"), null);

    private static object? Resource(Opened ui, string key) => ui.View.FindResource(key);

    /// <summary>Every button the user can see in the sheet (the ✕ or the back arrow, the rows) is a target of 44 px at least.</summary>
    private static void AssertTargetsOf44(Opened ui) =>
        Assert.All(ui.Sheet.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible),
            b => Assert.True(b.Bounds.Width >= 44 && b.Bounds.Height >= 44, AutomationProperties.GetName(b)));

    // ---- the brief ----

    [AvaloniaFact]
    public void Model_pill_opens_the_sheet_with_the_apps_title()
    {
        TestLog.Instance.Clear();
        var ui = new Opened(TouchSamples.Opus());
        Assert.False(ui.Sheet.IsOpen);
        Assert.False(ui.Sheet.IsEffectivelyVisible);

        ui.OpenSheet();
        Assert.True(ui.Sheet.IsOpen);
        Assert.Equal(new[]
        {
            "Selecionar modelo",
            "Opus 5.5", "Para trabalhos complexos e tarefas do dia a dia",
            "Fable 5.1", "Para seus desafios mais difíceis",
            "Sonnet 5.5", "Mais eficiente para tarefas mais simples",
            "Haiku 4.5", "Mais rápido para respostas rápidas",
            "Esforço", "Alto ›",
            "Outros modelos",
            "Sonnet 5", "Opus 5", "Fable 5", "Opus 4.8",
        }, ui.Texts);

        // the check, in the accent color, on the current model's row and on no other
        var check = Assert.Single(ui.Checks);
        Assert.Same(ui.Row("Opus 5.5"), check.FindAncestorOfType<Button>());
        Assert.Same(Resource(ui, "ClaudeChat.Accent"), check.Stroke);

        // a finger's rows: 64 px for a model with its description, 52 for the others and the effort
        Assert.All(ui.Rows, r => Assert.True(r.Bounds.Height >= 48, r.Bounds.Height.ToString()));
        foreach (var name in new[] { "Opus 5.5", "Fable 5.1", "Sonnet 5.5", "Haiku 4.5" })
            Assert.True(ui.Row(name).Bounds.Height >= 64, name);
        foreach (var name in new[] { "Esforço", "Sonnet 5", "Opus 5", "Fable 5", "Opus 4.8" })
            Assert.True(ui.Row(name).Bounds.Height >= 52, name);
        AssertTargetsOf44(ui);
        Assert.Empty(TestLog.Instance.Entries); // no binding or property warnings
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Picking_a_model_asks_for_it_and_closes()
    {
        var ui = new Opened(TouchSamples.Opus());
        ui.OpenSheet();
        UiHelpers.Click(ui.Window, ui.Row("Sonnet 5.5"));
        Assert.Equal(new[] { "sonnet" }, ui.Requested);
        Assert.False(ui.Sheet.IsOpen);
        Assert.Empty(ui.Efforts);

        // a model of the others asks for its own value
        ui.OpenSheet();
        UiHelpers.Click(ui.Window, ui.Row("Opus 4.8"));
        Assert.Equal(new[] { "sonnet", "claude-opus-4-8" }, ui.Requested);
        Assert.False(ui.Sheet.IsOpen);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Effort_row_opens_the_levels_and_picks_one()
    {
        var ui = new Opened(TouchSamples.Opus());
        ui.OpenSheet();
        Assert.Contains("Alto ›", ui.Texts);

        UiHelpers.Click(ui.Window, ui.Row("Esforço"));
        Assert.Equal(new[] { "Esforço", "Baixo", "Médio", "Alto", "Muito alto", "Máximo" }, ui.Texts);
        Assert.Same(ui.Row("Alto"), ui.CheckedRow);
        Assert.All(ui.Rows, r => Assert.True(r.Bounds.Height >= 52, r.Bounds.Height.ToString()));
        AssertTargetsOf44(ui);
        Assert.Empty(ui.Efforts);

        UiHelpers.Click(ui.Window, ui.Row("Médio"));
        Assert.Equal(new string?[] { "medium" }, ui.Efforts);
        Assert.True(ui.Sheet.IsOpen);                                    // back on the first page, open
        Assert.Contains("Selecionar modelo", ui.Texts);
        Assert.Contains("Médio ›", ui.Texts);                            // what was asked, while the claude has not confirmed it
        Assert.DoesNotContain("Alto ›", ui.Texts);

        // the claude confirms it: the same
        ui.Conversation.Apply(new SettingsApplied(null, "medium", true));
        UiHelpers.Pump();
        Assert.Contains("Médio ›", ui.Texts);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Sheet_closes_on_the_x_and_on_the_dim()
    {
        var ui = new Opened(TouchSamples.Opus());
        ui.OpenSheet();
        UiHelpers.Click(ui.Window, ui.Header("Fechar"));
        Assert.False(ui.Sheet.IsOpen);

        // the dim is what is above the panel; a tap on the panel itself (its title, its handle, a gap) closes nothing
        ui.OpenSheet();
        var panel = Part<Border>(ui.Sheet, "ClaudeChatSheetPanel");
        UiHelpers.Click(ui.Window, Part<TextBlock>(ui.Sheet, "ClaudeChatSheetTitle"));
        ui.Tap(panel.TranslatePoint(new Point(panel.Bounds.Width / 2, 1), ui.Window)!.Value);
        Assert.True(ui.Sheet.IsOpen);
        Assert.True(panel.Bounds.Top > 20);
        ui.Tap(new Point(UiHelpers.PhoneWidth / 2, panel.Bounds.Top / 2));
        Assert.False(ui.Sheet.IsOpen);
        Assert.Empty(ui.Requested);
        Assert.Empty(ui.Efforts);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void No_effort_row_for_a_model_without_effort()
    {
        var c = TouchSamples.Opus();
        c.Apply(new SessionStarted(null, "haiku", null, null, null));
        var ui = new Opened(c);
        ui.OpenSheet();
        Assert.True(ui.Sheet.IsOpen);
        Assert.DoesNotContain("Esforço", ui.Texts);
        Assert.Same(ui.Row("Haiku 4.5"), ui.CheckedRow);
        Assert.Contains("Outros modelos", ui.Texts);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Desktop_chip_still_opens_the_menu()
    {
        var view = new ClaudeChatView { Conversation = TouchSamples.Opus() };    // the desktop layout
        var window = UiHelpers.Show(view);
        var composer = UiHelpers.Composer(view);
        var chip = composer.GetVisualDescendants().OfType<ModelChip>().Single();
        var menu = composer.GetLogicalDescendants().OfType<ModelMenu>().Single();
        var sheet = view.GetVisualDescendants().OfType<ModelSheet>().Single();

        UiHelpers.Click(window, chip);
        Assert.True(menu.IsOpen);
        Assert.False(sheet.IsOpen);
        Assert.False(sheet.IsEffectivelyVisible);
        window.Close();
    }

    // ---- the layer ----

    [AvaloniaFact]
    public void A_closed_sheet_is_an_empty_hidden_layer_between_the_map_and_the_preview()
    {
        var ui = new Opened(TouchSamples.Opus());
        Assert.Empty(ui.Sheet.GetVisualDescendants());                   // nothing of it in the tree until it opens
        Assert.Null(ui.Sheet.Child);
        Assert.False(ui.Sheet.IsVisible);
        Assert.Equal(2, Grid.GetRowSpan(ui.Sheet));                       // both rows and both columns of the split: over the bookmarks panel too

        var layers = ((Grid)ui.Sheet.Parent!).Children;
        var at = layers.IndexOf(ui.Sheet);
        Assert.IsType<PlanCommentBox>(layers[at - 1]);                   // over the plan's comment box (0.6.0)…
        Assert.IsType<AgentMapPanel>(layers[at - 2]);                    // …and the Agent map under it…
        Assert.Same(layers[0], layers.OfType<Grid>().First(g => g.Name == "ChatArea"));
        Assert.Contains(((Visual)ui.Sheet.Parent!).GetVisualDescendants().OfType<PlanDocumentPanel>(), p => ReferenceEquals(p.Parent, layers[0])); // the plan's document is in the chat area, the split's first child: under all of them
        var preview = layers.IndexOf(layers.OfType<ImagePreview>().Single());
        Assert.All(layers.OfType<ChatSheet>(), sheet => Assert.True(layers.IndexOf(sheet) < preview));   // …and, like every sheet, under the preview of an image

        ui.OpenSheet();
        Assert.NotEmpty(ui.Sheet.GetVisualDescendants());
        Assert.Same(ui.Sheet, UiHelpers.HitTest(ui.Window, new Point(5, 5)));   // the veil takes what is under it
        ui.Sheet.Close();
        Assert.Empty(ui.Sheet.GetVisualDescendants());                   // and is emptied again
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void The_sheet_rises_from_the_bottom_with_the_apps_measures()
    {
        var ui = new Opened(TouchSamples.Opus());
        ui.OpenSheet();
        var panel = Part<Border>(ui.Sheet, "ClaudeChatSheetPanel");
        Assert.Same(Resource(ui, "ClaudeChat.SheetOverlay"), ui.Sheet.Background);
        Assert.Equal(ui.View.Bounds.Width, ui.Sheet.Bounds.Width);                                     // the whole view (a row more or less than it, once the rows are rounded to pixels)
        Assert.InRange(ui.Sheet.Bounds.Height, ui.View.Bounds.Height, ui.View.Bounds.Height + 1);
        Assert.Same(Resource(ui, "ClaudeChat.SheetBackground"), panel.Background);
        Assert.Equal(new CornerRadius(18, 18, 0, 0), panel.CornerRadius);
        Assert.Equal(ui.Sheet.Bounds.Height, panel.TranslatePoint(new Point(0, panel.Bounds.Height), ui.Sheet)!.Value.Y, 1);
        Assert.Equal(UiHelpers.PhoneWidth, panel.Bounds.Width, 1);
        Assert.InRange(panel.Bounds.Height, 300, ui.Sheet.Bounds.Height * 0.92 + 0.5);

        // the handle, the header
        var handle = Part<Border>(panel, "ClaudeChatSheetHandle");
        Assert.Equal(new Size(36, 4), handle.Bounds.Size);
        Assert.Equal(new CornerRadius(2), handle.CornerRadius);
        Assert.Same(Resource(ui, "ClaudeChat.SheetHandle"), handle.Background);
        Assert.Equal(UiHelpers.PhoneWidth / 2, handle.TranslatePoint(new Point(18, 0), panel)!.Value.X, 1);
        var header = Part<Grid>(panel, "ClaudeChatSheetHeader");
        Assert.Equal(48, header.Bounds.Height);
        var close = ui.Header("Fechar");
        Assert.Equal(new Size(44, 44), close.Bounds.Size);
        Assert.Equal(0, close.TranslatePoint(default, header)!.Value.X, 1);
        var title = Part<TextBlock>(header, "ClaudeChatSheetTitle");
        Assert.Equal("Selecionar modelo", title.Text);
        Assert.Equal(16, title.FontSize);
        Assert.Equal(FontWeight.SemiBold, title.FontWeight);
        // centered within a pixel: the text's width, and so its rounding, depends on the platform's fonts
        Assert.InRange(title.TranslatePoint(new Point(title.Bounds.Width / 2, 0), header)!.Value.X, UiHelpers.PhoneWidth / 2 - 1, UiHelpers.PhoneWidth / 2 + 1);

        // the groups: blocks with 14 px corners, a line of the sheet's color between the rows
        var groups = panel.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("ClaudeChatSheetGroup")).ToList();
        Assert.Equal(3, groups.Count);                                   // the main models, the effort, the others
        Assert.All(groups, g =>
        {
            Assert.Equal(new CornerRadius(14), g.CornerRadius);
            Assert.Same(Resource(ui, "ClaudeChat.SheetGroupBackground"), g.Background);
            Assert.Equal(12, g.TranslatePoint(default, panel)!.Value.X, 1);                    // 12 px from the sides
            Assert.Equal(UiHelpers.PhoneWidth - 24, g.Bounds.Width, 1);
        });
        var lines = panel.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("ClaudeChatSheetSeparator")).ToList();
        Assert.Equal(3 + 0 + 3, lines.Count);
        Assert.All(lines, l =>
        {
            Assert.Equal(1, l.Bounds.Height);
            Assert.Same(Resource(ui, "ClaudeChat.SheetBackground"), l.Background);
        });

        // the texts: the name at 16, its description and the group's label at 13 in the muted color
        var muted = Resource(ui, "ClaudeChat.ForegroundMuted");
        var name = Part<TextBlock>(ui.Row("Opus 5.5"), "ClaudeChatSheetName");
        Assert.Equal(16, name.FontSize);
        var description = Part<TextBlock>(ui.Row("Opus 5.5"), "ClaudeChatSheetDescription");
        Assert.Equal(13, description.FontSize);
        Assert.Same(muted, description.Foreground);
        var label = Part<TextBlock>(panel, "ClaudeChatSheetLabel");
        Assert.Equal("Outros modelos", label.Text);
        Assert.Equal(13, label.FontSize);
        Assert.Same(muted, label.Foreground);
        var level = Part<TextBlock>(ui.Row("Esforço"), "ClaudeChatSheetValue");
        Assert.Same(muted, level.Foreground);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_list_taller_than_92_percent_of_the_view_scrolls()
    {
        var ui = new Opened(TouchSamples.Opus(), height: 400);
        ui.OpenSheet();
        var panel = Part<Border>(ui.Sheet, "ClaudeChatSheetPanel");
        var small = ui.Sheet.Bounds.Height;
        Assert.Equal(small * 0.92, panel.Bounds.Height, 0);
        var scroller = panel.GetVisualDescendants().OfType<ScrollViewer>().First();
        Assert.True(scroller.Extent.Height > scroller.Viewport.Height);
        Assert.Equal(0, scroller.Offset.Y);

        // the window grows (the keyboard goes away): the panel follows, and the whole list fits
        ui.Window.Height = UiHelpers.PhoneHeight;
        UiHelpers.PumpUntil(() => panel.Bounds.Height > small * 0.92 + 1);
        Assert.InRange(panel.Bounds.Height, 300, ui.Sheet.Bounds.Height * 0.92 + 0.5);
        Assert.True(scroller.Extent.Height <= scroller.Viewport.Height + 0.5);
        ui.Window.Close();
    }

    // ---- what it shows ----

    [AvaloniaFact]
    public void The_check_follows_the_current_model_and_the_effort_what_the_claude_reports()
    {
        var ui = new Opened(TouchSamples.Opus());
        ui.OpenSheet();
        Assert.Same(ui.Row("Opus 5.5"), ui.CheckedRow);

        ui.Conversation.Apply(new SessionStarted(null, "sonnet", null, null, null));
        UiHelpers.Pump();
        Assert.Same(ui.Row("Sonnet 5.5"), ui.CheckedRow);                // redone while it is open

        // a model of the others is the current one: its row has the check
        ui.Conversation.Apply(new SessionStarted(null, "claude-opus-4-8", null, null, null));
        UiHelpers.Pump();
        Assert.Same(ui.Row("Opus 4.8"), ui.CheckedRow);

        // the effort the claude reports, and the one asked for while it waits
        ui.Conversation.Apply(new SettingsApplied(null, "xhigh", true));
        UiHelpers.Pump();
        Assert.Contains("Muito alto ›", ui.Texts);
        ui.Conversation.Apply(new SettingsApplied(null, null, true));    // none: the model decides
        UiHelpers.Pump();
        Assert.Contains("Automático ›", ui.Texts);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Texts_follow_the_language_and_a_description_not_in_the_list_stays_as_it_came()
    {
        var c = TouchSamples.Opus();
        c.AvailableModels = TouchSamples.Models.Concat(new[]
        {
            new ChatModel("nova", "claude-nova-1", "Nova 1", "Brand new", new[] { "low" }),   // a family of its own
        }).ToList();
        var ui = new Opened(c);
        ui.View.Strings = ChatStrings.English;
        UiHelpers.Pump();
        ui.OpenSheet();
        Assert.Contains("Select model", ui.Texts);
        Assert.Contains("For complex work and everyday tasks", ui.Texts);   // English: the claude's own words
        Assert.Contains("High ›", ui.Texts);
        Assert.Contains("Other models", ui.Texts);
        Assert.Contains("Brand new", ui.Texts);
        Assert.Contains("Close", UiHelpers.ButtonNames(ui.Sheet));

        ui.View.Strings = ChatStrings.PortugueseBrazil;                     // changed while it is open
        UiHelpers.Pump();
        Assert.Contains("Selecionar modelo", ui.Texts);
        Assert.Contains("Para trabalhos complexos e tarefas do dia a dia", ui.Texts);
        Assert.Contains("Alto ›", ui.Texts);
        Assert.Contains("Outros modelos", ui.Texts);
        Assert.Contains("Fechar", UiHelpers.ButtonNames(ui.Sheet));
        Assert.Contains("Brand new", ui.Texts);                             // not among the four it knows: as the claude sent it

        UiHelpers.Click(ui.Window, ui.Row("Esforço"));
        Assert.Contains("Esforço", ui.Texts);
        Assert.Contains("Voltar", UiHelpers.ButtonNames(ui.Sheet));
        ui.View.Strings = ChatStrings.English;
        UiHelpers.Pump();
        Assert.Equal(new[] { "Effort", "Low", "Medium", "High", "Extra high", "Max" }, ui.Texts);
        Assert.Contains("Back", UiHelpers.ButtonNames(ui.Sheet));
        ui.Window.Close();
    }

    [Fact]
    public void The_descriptions_the_app_translates_are_the_four_it_knows()
    {
        Assert.Empty(ChatStrings.English.ModelDescriptions);
        Assert.Equal(new ChatStrings(), ChatStrings.English);                // the empty list is shared: default texts stay equal to each other
        var pt = ChatStrings.PortugueseBrazil.ModelDescriptions;
        Assert.Equal(new[]
        {
            "For complex work and everyday tasks", "For your toughest challenges",
            "Most efficient for simpler tasks", "Fastest for quick answers",
        }.OrderBy(s => s), pt.Keys.OrderBy(s => s));
        Assert.Equal("Para trabalhos complexos e tarefas do dia a dia", pt["For complex work and everyday tasks"]);
        Assert.Equal("Para seus desafios mais difíceis", pt["For your toughest challenges"]);
        Assert.Equal("Mais eficiente para tarefas mais simples", pt["Most efficient for simpler tasks"]);
        Assert.Equal("Mais rápido para respostas rápidas", pt["Fastest for quick answers"]);
        // the shared texts cannot be changed by a cast: a change would reach every view of the process
        foreach (var shared in new[] { ChatStrings.English.ModelDescriptions, pt })
        {
            var asWritable = Assert.IsAssignableFrom<IDictionary<string, string>>(shared);
            Assert.Throws<NotSupportedException>(() => asWritable["Brand new"] = "Novinho");
        }

        Assert.Equal(("Select model", "Selecionar modelo"), (ChatStrings.English.SelectModelTitle, ChatStrings.PortugueseBrazil.SelectModelTitle));
        Assert.Equal(("Other models", "Outros modelos"), (ChatStrings.English.OtherModels, ChatStrings.PortugueseBrazil.OtherModels));
        Assert.Equal("Fastest for quick answers", ChatStrings.English.ModelDescription("Fastest for quick answers"));
        Assert.Equal("Mais rápido para respostas rápidas", ChatStrings.PortugueseBrazil.ModelDescription("Fastest for quick answers"));
        Assert.Equal("Brand new", ChatStrings.PortugueseBrazil.ModelDescription("Brand new"));
        Assert.Equal("", ChatStrings.PortugueseBrazil.ModelDescription(""));
        // a copy that takes the dictionary away, or puts its own
        Assert.Equal("Fastest for quick answers", (ChatStrings.PortugueseBrazil with { ModelDescriptions = null! }).ModelDescription("Fastest for quick answers"));
        var own = ChatStrings.English with { ModelDescriptions = new Dictionary<string, string> { ["Brand new"] = "Novinho" } };
        Assert.Equal("Novinho", own.ModelDescription("Brand new"));
    }

    [AvaloniaFact]
    public void A_model_without_description_shows_its_name_alone()
    {
        var c = new ChatConversation
        {
            AvailableModels = new[]
            {
                new ChatModel("opus", "claude-opus-5-5", "Opus 5.5", "", new[] { "low", "high" }),
                new ChatModel("sonnet", "claude-sonnet-5-5", "Sonnet 5.5", "Efficient", new string[0]),
            },
        };
        c.Apply(new SessionStarted("s1", "opus", null, "default", "2.1.310"));
        var ui = new Opened(c);
        ui.OpenSheet();
        Assert.Equal(new[] { "Selecionar modelo", "Opus 5.5", "Sonnet 5.5", "Efficient", "Esforço", "Automático ›" }, ui.Texts);
        Assert.DoesNotContain("Outros modelos", ui.Texts);                  // nothing is among the others
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void With_no_main_model_the_others_come_alone_without_their_label()
    {
        var c = new ChatConversation { AvailableModels = new[] { new ChatModel("x", "mystery-1", "Mystery", "", new string[0]) } };
        c.Apply(new SessionStarted("s1", "mystery-1", null, "default", "2.1.310"));
        var ui = new Opened(c);
        ui.OpenSheet();
        Assert.Equal(new[] { "Selecionar modelo", "Mystery" }, ui.Texts);
        Assert.Same(ui.Row("Mystery"), ui.CheckedRow);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void The_pages_have_a_back_arrow_in_place_of_the_x()
    {
        var ui = new Opened(TouchSamples.Opus());
        ui.OpenSheet();
        Assert.Contains("Fechar", UiHelpers.ButtonNames(ui.Sheet));
        Assert.DoesNotContain("Voltar", UiHelpers.ButtonNames(ui.Sheet));

        UiHelpers.Click(ui.Window, ui.Row("Esforço"));
        var names = UiHelpers.ButtonNames(ui.Sheet);
        Assert.Contains("Voltar", names);
        Assert.DoesNotContain("Fechar", names);
        Assert.Equal(new Size(44, 44), ui.Header("Voltar").Bounds.Size);
        Assert.Equal("Esforço", Part<TextBlock>(ui.Sheet, "ClaudeChatSheetTitle").Text);

        UiHelpers.Click(ui.Window, ui.Header("Voltar"));                    // back to the models, the sheet stays
        Assert.True(ui.Sheet.IsOpen);
        Assert.Contains("Selecionar modelo", ui.Texts);
        Assert.Contains("Fechar", UiHelpers.ButtonNames(ui.Sheet));
        Assert.Empty(ui.Efforts);

        UiHelpers.Click(ui.Window, ui.Row("Esforço"));                      // and the veil closes the sheet from a page too
        ui.Tap(new Point(UiHelpers.PhoneWidth / 2, 10));
        Assert.False(ui.Sheet.IsOpen);
        ui.OpenSheet();                                                      // opens on the first page again
        Assert.Contains("Selecionar modelo", ui.Texts);
        ui.Window.Close();
    }

    // ---- when it opens, and when it closes by itself ----

    [AvaloniaFact]
    public void Back_goes_one_page_at_a_time_and_says_whether_it_did_something()
    {
        var ui = new Opened(TouchSamples.Opus());
        var closed = 0;
        ui.Sheet.Closed += (_, _) => closed++;
        Assert.False(ui.Sheet.Back());                                       // closed: nothing to go back from
        ui.OpenSheet();
        UiHelpers.Click(ui.Window, ui.Row("Esforço"));

        Assert.True(ui.Sheet.Back());                                        // the page, to the models
        Assert.True(ui.Sheet.IsOpen);
        Assert.Equal(0, closed);
        Assert.Contains("Selecionar modelo", ui.Texts);
        Assert.True(ui.Sheet.Back());                                        // the models, to the view
        Assert.False(ui.Sheet.IsOpen);
        Assert.Equal(1, closed);
        Assert.False(ui.Sheet.Back());
        ui.Sheet.Close();                                                    // closing a closed sheet says nothing
        Assert.Equal(1, closed);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Another_conversation_read_only_and_the_desktop_layout_close_the_sheet()
    {
        var ui = new Opened(TouchSamples.Opus());
        var closed = 0;
        ui.Sheet.Closed += (_, _) => closed++;

        ui.OpenSheet();
        ui.View.Conversation = new ChatConversation { AvailableModels = TouchSamples.Models };
        UiHelpers.Pump();
        Assert.False(ui.Sheet.IsOpen);
        ui.OpenSheet();                                                      // the new conversation has its own: no model reported yet
        Assert.True(ui.Sheet.IsOpen);
        Assert.DoesNotContain(ui.Sheet.GetVisualDescendants().OfType<Path>(), p => p.IsEffectivelyVisible && p.Classes.Contains("ClaudeChatMenuCheck"));
        Assert.DoesNotContain("Esforço", ui.Texts);

        ui.View.IsReadOnly = true;                                           // the reply box goes away
        UiHelpers.Pump();
        Assert.False(ui.Sheet.IsOpen);
        ui.View.IsReadOnly = false;
        UiHelpers.Pump();
        Assert.False(ui.Sheet.IsOpen);                                       // and does not come back by itself

        ui.OpenSheet();
        Assert.True(ui.Sheet.IsOpen);
        ui.View.Layout = ChatLayout.Desktop;
        UiHelpers.Pump();
        Assert.False(ui.Sheet.IsOpen);
        Assert.Equal(3, closed);

        ui.View.Layout = ChatLayout.Touch;                                   // back to touch: closed, as it was left
        UiHelpers.Pump();
        Assert.False(ui.Sheet.IsOpen);
        ui.OpenSheet();
        Assert.True(ui.Sheet.IsOpen);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_permission_request_closes_the_sheet_and_none_opens_while_one_waits()
    {
        var ui = new Opened(TouchSamples.Opus());
        ui.OpenSheet();
        UiHelpers.Click(ui.Window, ui.Row("Esforço"));
        Assert.True(ui.Sheet.IsOpen);

        ui.Conversation.Apply(Permission());                                 // the request comes first: the sheet would cover it
        UiHelpers.Pump();
        Assert.False(ui.Sheet.IsOpen);
        Assert.Empty(ui.Requested);

        ui.OpenSheet();                                                      // the pill opens nothing while the claude waits
        Assert.False(ui.Sheet.IsOpen);
        ui.Conversation.DismissPermission("r1");
        UiHelpers.Pump();
        ui.OpenSheet();
        Assert.True(ui.Sheet.IsOpen);
        Assert.Contains("Selecionar modelo", ui.Texts);                      // on the first page
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Without_a_list_of_models_the_pill_opens_nothing_and_a_list_that_goes_away_closes_the_sheet()
    {
        var c = new ChatConversation();
        c.Apply(new SessionStarted("s1", "claude-opus-5-5", null, "default", "2.1.310"));   // a model, no list
        var ui = new Opened(c);
        Assert.True(ui.Pill.IsEffectivelyVisible);
        ui.OpenSheet();
        Assert.False(ui.Sheet.IsOpen);

        c.AvailableModels = TouchSamples.Models;
        UiHelpers.Pump();
        ui.OpenSheet();
        Assert.True(ui.Sheet.IsOpen);
        c.AvailableModels = new ChatModel[0];                                // nothing to pick any more
        UiHelpers.Pump();
        Assert.False(ui.Sheet.IsOpen);

        // a list with only the default entry, which the sheet leaves out, is nothing to pick either
        c.AvailableModels = new[] { new ChatModel("default", "claude-opus-5-5", "Default (recommended)", "", new string[0]) };
        UiHelpers.Pump();
        ui.OpenSheet();
        Assert.False(ui.Sheet.IsOpen);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_model_with_no_levels_while_on_the_effort_page_goes_back_to_the_models()
    {
        var ui = new Opened(TouchSamples.Opus());
        ui.OpenSheet();
        UiHelpers.Click(ui.Window, ui.Row("Esforço"));
        Assert.Equal("Esforço", Part<TextBlock>(ui.Sheet, "ClaudeChatSheetTitle").Text);

        ui.Conversation.Apply(new SessionStarted(null, "haiku", null, null, null));   // the claude switched to a model without effort
        UiHelpers.Pump();
        Assert.True(ui.Sheet.IsOpen);
        Assert.Equal("Selecionar modelo", Part<TextBlock>(ui.Sheet, "ClaudeChatSheetTitle").Text);
        Assert.DoesNotContain("Esforço", ui.Texts);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_sheet_left_open_while_the_view_is_out_of_the_window_follows_the_conversation_when_it_is_back()
    {
        var c = TouchSamples.Opus();
        var view = UiHelpers.TouchView(c);
        var host = new ContentControl { Content = view };                   // a shell that swaps its page
        var window = UiHelpers.ShowPhone(host);
        var sheet = view.GetVisualDescendants().OfType<ModelSheet>().Single();
        UiHelpers.Click(window, UiHelpers.Composer(view).GetVisualDescendants().OfType<ModelChip>().Single(x => x.Name == "TouchModelChip"));
        Assert.True(sheet.IsOpen);

        host.Content = new TextBlock { Text = "Tokens" };
        UiHelpers.Pump();
        c.Apply(new SessionStarted(null, "sonnet", null, null, null));       // what changes while it is out is drawn when it is back
        host.Content = view;
        UiHelpers.Pump();
        Assert.True(sheet.IsOpen);
        var check = Assert.Single(sheet.GetVisualDescendants().OfType<Path>(), p => p.IsEffectivelyVisible && p.Classes.Contains("ClaudeChatMenuCheck"));
        Assert.Same(UiHelpers.ButtonWithText(sheet, "Sonnet 5.5"), check.FindAncestorOfType<Button>());

        host.Content = new TextBlock { Text = "Tokens" };                    // and a request that came while it was out closes it
        UiHelpers.Pump();
        c.Apply(Permission());
        host.Content = view;
        UiHelpers.Pump();
        Assert.False(sheet.IsOpen);
        window.Close();
    }

    // ---- the keyboard ----

    [AvaloniaFact]
    public void The_keyboard_goes_into_the_sheet_esc_backs_out_and_the_pill_gets_it_back()
    {
        var ui = new Opened(TouchSamples.Opus());
        var unhandled = UiHelpers.UnhandledKeys(ui.Window);
        UiHelpers.Click(ui.Window, ui.Pill);
        Assert.True(ui.Sheet.IsKeyboardFocusWithin);                         // not left on the pill under the veil

        UiHelpers.Click(ui.Window, ui.Row("Esforço"));                       // the row that was pressed is gone: the sheet keeps it
        Assert.True(ui.Sheet.IsKeyboardFocusWithin);
        UiHelpers.Press(ui.Window, Key.Escape);                              // the page, to the models
        Assert.True(ui.Sheet.IsOpen);
        Assert.Contains("Selecionar modelo", ui.Texts);
        Assert.True(ui.Sheet.IsKeyboardFocusWithin);
        UiHelpers.Press(ui.Window, Key.Escape);
        Assert.False(ui.Sheet.IsOpen);
        Assert.True(ui.Pill.IsFocused);                                      // as the model menu gives it back to its chip
        Assert.DoesNotContain(Key.Escape, unhandled);                        // an Esc the sheet took is not the app's

        UiHelpers.Press(ui.Window, Key.Escape);                              // closed: this one is the app's
        Assert.Contains(Key.Escape, unhandled);

        UiHelpers.Click(ui.Window, ui.Pill);
        UiHelpers.Click(ui.Window, ui.Row("Sonnet 5.5"));                    // a pick gives it back too
        Assert.True(ui.Pill.IsFocused);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Tab_goes_round_inside_the_sheet_and_never_to_what_the_veil_covers()
    {
        var ui = new Opened(TouchSamples.Opus());
        ui.OpenSheet();
        var visited = new HashSet<IInputElement>();
        for (var step = 0; step < 24; step++)
        {
            UiHelpers.Press(ui.Window, Key.Tab);
            Assert.True(ui.Sheet.IsKeyboardFocusWithin, "after Tab number " + (step + 1));
            visited.Add(UiHelpers.Focused(ui.Window)!);
        }
        Assert.Equal(1 + ui.Rows.Count, visited.Count);                      // the ✕ and every row, again and again
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Closing_gives_the_keyboard_back_to_where_it_was()
    {
        var ui = new Opened(TouchSamples.Opus());
        UiHelpers.Input(ui.View).Focus();
        ui.Sheet.Open();                                                     // with the text box focused
        Assert.True(ui.Sheet.IsKeyboardFocusWithin);
        ui.Sheet.Close();
        Assert.Same(UiHelpers.Input(ui.View), UiHelpers.Focused(ui.Window)); // it goes back where it was
        ui.Window.Close();
    }

    // ---- the base, through a sheet of its own ----

    /// <summary>A sheet that is only the base: a title, a page, an action at the right, and its own way to close.</summary>
    private sealed class PlainSheet : ChatSheet
    {
        public int Dismissed, Acted;
        public bool Withheld;

        public string? Inner => Page;

        protected override bool CanOpen(ChatConversation conversation) => !Withheld;

        protected override bool Shows(string? propertyName) => propertyName == nameof(ChatConversation.Effort);

        protected override string Title(ChatStrings strings) => Page == null ? "Plain" : "Inner";

        protected override void Fill(Panel body, ChatConversation conversation, ChatStrings strings)
        {
            body.Children.Add(new TextBlock { Text = (Page ?? "first") + " " + conversation.Effort });
            body.Children.Add(Group(new[] { Row("Go", () => ShowPage("inner")), Row("Value", () => { }, value: "v", check: true) }));
        }

        protected override Button? HeaderAction(ChatConversation conversation, ChatStrings strings) =>
            Page == null ? HeaderButton(new Path(), "Act", () => Acted++) : null;

        protected override void Dismiss()
        {
            Dismissed++;
            Close();
        }
    }

    private static PlainSheet Plain(Opened ui)
    {
        var sheet = new PlainSheet();
        Grid.SetRowSpan(sheet, 3);
        ((Grid)ui.Sheet.Parent!).Children.Add(sheet);
        sheet.Conversation = ui.Conversation;
        UiHelpers.Pump();
        return sheet;
    }

    [AvaloniaFact]
    public void A_sheet_decides_how_it_closes_when_the_user_or_a_permission_request_asks_and_not_when_its_reason_stays()
    {
        var ui = new Opened(TouchSamples.Opus());
        var sheet = Plain(ui);

        sheet.Open();
        Assert.True(sheet.IsOpen);
        UiHelpers.Click(ui.Window, UiHelpers.ButtonWithText(sheet, "Fechar"));          // the ✕
        Assert.Equal(1, sheet.Dismissed);
        Assert.False(sheet.IsOpen);

        sheet.Open();
        ui.Tap(new Point(UiHelpers.PhoneWidth / 2, 5));                                  // the veil
        Assert.Equal(2, sheet.Dismissed);

        sheet.Open();
        ui.Conversation.Apply(Permission());                                             // a request
        UiHelpers.Pump();
        Assert.Equal(3, sheet.Dismissed);
        Assert.False(sheet.IsOpen);
        sheet.Open();                                                                    // not while one waits
        Assert.False(sheet.IsOpen);
        ui.Conversation.DismissPermission("r1");
        UiHelpers.Pump();

        sheet.Open();
        UiHelpers.Press(ui.Window, Key.Escape);                                          // Esc, and Back() on the first page
        Assert.Equal(4, sheet.Dismissed);

        sheet.Open();
        Assert.True(sheet.Back());
        Assert.Equal(5, sheet.Dismissed);

        // what takes the sheet's reason away is not a dismissal: the sheet hides and nothing else happens
        sheet.Open();
        ui.View.Layout = ChatLayout.Desktop;
        UiHelpers.Pump();
        Assert.False(sheet.IsOpen);
        ui.View.Layout = ChatLayout.Touch;
        UiHelpers.Pump();
        sheet.Open();
        sheet.Conversation = new ChatConversation();
        Assert.False(sheet.IsOpen);
        Assert.Equal(5, sheet.Dismissed);

        sheet.Withheld = true;                                                           // the sheet's own say in opening
        sheet.Conversation = ui.Conversation;
        sheet.Open();
        Assert.False(sheet.IsOpen);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_sheet_has_its_title_pages_and_an_action_at_the_right_of_the_header()
    {
        var ui = new Opened(TouchSamples.Opus());
        var sheet = Plain(ui);
        sheet.Open();
        UiHelpers.Pump();                                                                // the body is drawn with the first layout
        var header = Part<Grid>(sheet, "ClaudeChatSheetHeader");
        Assert.Equal("Plain", Part<TextBlock>(header, "ClaudeChatSheetTitle").Text);
        Assert.Contains("first high", UiHelpers.Texts(sheet));

        var act = UiHelpers.ButtonWithText(header, "Act");
        Assert.Equal(new Size(44, 44), act.Bounds.Size);
        Assert.Equal(header.Bounds.Width, act.TranslatePoint(new Point(44, 0), header)!.Value.X, 1);   // its right edge is the header's
        UiHelpers.Click(ui.Window, act);
        Assert.Equal(1, sheet.Acted);
        Assert.True(sheet.IsOpen);                                                       // an action closes nothing by itself

        UiHelpers.Click(ui.Window, UiHelpers.ButtonWithText(sheet, "Go"));               // a page: a back arrow, no action
        Assert.Equal("inner", sheet.Inner);
        Assert.Equal("Inner", Part<TextBlock>(header, "ClaudeChatSheetTitle").Text);
        Assert.DoesNotContain("Act", UiHelpers.ButtonNames(sheet));
        Assert.Contains("Voltar", UiHelpers.ButtonNames(sheet));

        ui.Conversation.Apply(new SettingsApplied(null, "low", true));                   // it shows what it says it shows
        UiHelpers.Pump();
        Assert.Contains("inner low", UiHelpers.Texts(sheet));

        Assert.True(sheet.Back());
        Assert.Null(sheet.Inner);
        UiHelpers.Pump();
        Assert.Contains("Act", UiHelpers.ButtonNames(sheet));
        Assert.Single(sheet.GetVisualDescendants().OfType<Path>(), p => p.IsEffectivelyVisible && p.Classes.Contains("ClaudeChatMenuCheck"));   // the row's check
        ui.Window.Close();
    }
}
