using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using ClaudeVisualChat.Protocol;
using Xunit;

namespace ClaudeVisualChat.Tests.Ui;

/// <summary>
/// The "+" sheet of the touch layout (<see cref="AddSheet"/>): the + of the reply box's bar opens it with the photo or file, the
/// commands, the permission mode, the browser and Remote Control. Each row does what the desktop's buttons and the banners do.
/// </summary>
public class AddSheetTests
{
    private const string RemoteUrl = "https://claude.ai/code/session_x";

    private sealed class Opened
    {
        public Opened(ChatConversation? conversation = null)
        {
            Conversation = conversation ?? TouchSamples.Opus();
            Conversation.PermissionModeRequested += (_, e) => Modes.Add(e.Mode);
            Conversation.CommandInvoked += (_, e) =>
            {
                Commands.Add(e.Name);
                Arguments.Add(e.Arguments);
            };
            Conversation.RemoteControlLinkRequested += (_, e) => Links.Add(e.Url);
            View = UiHelpers.TouchView(Conversation);
            Window = UiHelpers.ShowPhone(View);
            Composer = UiHelpers.Composer(View);
            Sheet = View.GetVisualDescendants().OfType<AddSheet>().Single();
        }

        public ChatConversation Conversation { get; }
        public ClaudeChatView View { get; }
        public Window Window { get; }
        public ChatComposer Composer { get; }
        public AddSheet Sheet { get; }
        public List<string> Modes { get; } = new List<string>();
        public List<string> Commands { get; } = new List<string>();
        public List<string> Arguments { get; } = new List<string>();
        public List<string> Links { get; } = new List<string>();

        /// <summary>The + of the touch bar.</summary>
        public Button Plus => Composer.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "TouchAddButton");

        public CommandPalette Palette => Composer.GetLogicalDescendants().OfType<CommandPalette>().Single();

        public List<string> Texts => UiHelpers.Texts(Sheet);

        public string Title => Part<TextBlock>(Sheet, "ClaudeChatSheetTitle").Text ?? "";

        /// <summary>The rows the user can tap.</summary>
        public List<Button> Rows => Sheet.GetVisualDescendants().OfType<Button>()
            .Where(b => b.IsEffectivelyVisible && b.Classes.Contains("ClaudeChatSheetRow")).ToList();

        public Button Row(string text) => Rows.Single(r => UiHelpers.Texts(r).Contains(text));

        /// <summary>What a row says at its right: the state, or the mode.</summary>
        public string ValueOf(string row) => Part<TextBlock>(Row(row), "ClaudeChatSheetValue").Text ?? "";

        /// <summary>The checks the user can see.</summary>
        public List<Path> Checks => Sheet.GetVisualDescendants().OfType<Path>()
            .Where(p => p.IsEffectivelyVisible && p.Classes.Contains("ClaudeChatMenuCheck")).ToList();

        /// <summary>The row of the one check there is.</summary>
        public Button CheckedRow => Assert.Single(Checks).FindAncestorOfType<Button>()!;

        public void OpenSheet() => UiHelpers.Click(Window, Plus);

        public void Tap(string row) => UiHelpers.Click(Window, Row(row));
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

    /// <summary>Every button the user can see in the sheet (the ✕ or the back arrow, the rows) is a target of 44 px at least.</summary>
    private static void AssertTargetsOf44(Opened ui) =>
        Assert.All(ui.Sheet.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible),
            b => Assert.True(b.Bounds.Width >= 44 && b.Bounds.Height >= 44, AutomationProperties.GetName(b)));

    // ---- the brief ----

    [AvaloniaFact]
    public void Plus_opens_the_sheet_with_its_rows()
    {
        TestLog.Instance.Clear();
        var ui = new Opened();
        Assert.False(ui.Sheet.IsOpen);
        Assert.False(ui.Sheet.IsEffectivelyVisible);

        ui.OpenSheet();
        Assert.True(ui.Sheet.IsOpen);
        Assert.Equal(new[]
        {
            "Adicionar",
            "Foto ou arquivo",
            "Comandos",
            "Modo", "Manual ›",
            "Navegador", "desligado",
            "Remote Control", "desligado",
        }, ui.Texts);                                                    // and no link to claude.ai/code until Remote Control is on
        Assert.DoesNotContain("Abrir claude.ai/code", ui.Texts);

        // one block of rows, a finger's height each; a screen reader hears the state with the name
        Assert.Single(ui.Sheet.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("ClaudeChatSheetGroup"));
        Assert.Equal(5, ui.Rows.Count);
        Assert.All(ui.Rows, r => Assert.True(r.Bounds.Height >= 52, r.Bounds.Height.ToString()));
        AssertTargetsOf44(ui);
        Assert.Equal("Modo, Manual", AutomationProperties.GetName(ui.Row("Modo")));
        Assert.Equal("Navegador, desligado", AutomationProperties.GetName(ui.Row("Navegador")));
        Assert.Empty(ui.Checks);
        Assert.Empty(TestLog.Instance.Entries);                          // no binding or property warnings
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Photo_row_picks_and_attaches()
    {
        var ui = new Opened();
        var openWhenAsked = true;
        ui.Composer.PickImagesOverride = () =>
        {
            openWhenAsked = ui.Sheet.IsOpen;
            return Task.FromResult<IReadOnlyList<byte[]>>(new[] { UiHelpers.Png(4, 4) });
        };
        ui.OpenSheet();
        ui.Tap("Foto ou arquivo");

        UiHelpers.PumpUntil(() => ui.Conversation.Attachments.Count == 1);
        Assert.Equal(4, Assert.Single(ui.Conversation.Attachments).Width);
        Assert.False(ui.Sheet.IsOpen);
        Assert.False(openWhenAsked);                                     // the sheet is gone when the dialog opens
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Photo_row_attaches_a_file_too_and_a_cancelled_dialog_changes_nothing()
    {
        var ui = new Opened();
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cvc-add-sheet-" + Guid.NewGuid().ToString("N") + ".txt");
        System.IO.File.WriteAllText(path, "hello");
        try
        {
            var asked = 0;
            ui.Composer.PickFilesOverride = () =>
            {
                asked++;
                return Task.FromResult<IReadOnlyList<string>>(asked == 1 ? new string[0] : new[] { path });
            };
            ui.OpenSheet();
            ui.Tap("Foto ou arquivo");                                   // cancelled
            UiHelpers.Settle();
            Assert.Equal(1, asked);
            Assert.False(ui.Sheet.IsOpen);
            Assert.Empty(ui.Conversation.Files);
            Assert.Empty(ui.Conversation.Attachments);

            ui.OpenSheet();
            ui.Tap("Foto ou arquivo");
            UiHelpers.PumpUntil(() => ui.Conversation.Files.Count == 1);
            Assert.Equal(System.IO.Path.GetFileName(path), ui.Conversation.Files[0].Name);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Commands_row_opens_the_palette()
    {
        var ui = new Opened();
        ui.OpenSheet();
        Assert.False(ui.Palette.IsOpen);

        ui.Tap("Comandos");
        Assert.False(ui.Sheet.IsOpen);
        Assert.True(ui.Palette.IsOpen);
        // the "/" button's way: the filter field takes the keyboard
        var filter = ui.Palette.Child!.GetVisualDescendants().OfType<TextBox>().Single(t => t.IsEffectivelyVisible);
        Assert.Equal("Filtrar comandos…", filter.Watermark);
        Assert.True(filter.IsFocused);
        Assert.Empty(ui.Commands);                                       // nothing ran: the user picks
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Mode_page_asks_for_the_mode()
    {
        var ui = new Opened();
        ui.OpenSheet();
        Assert.Equal("Manual ›", ui.ValueOf("Modo"));

        ui.Tap("Modo");
        Assert.Equal(new[] { "Modo", "Plano", "Manual", "Editar sozinho", "Automático" }, ui.Texts);
        Assert.Same(ui.Row("Manual"), ui.CheckedRow);                    // the mode the claude reports
        Assert.Equal(4, ui.Rows.Count);
        Assert.All(ui.Rows, r => Assert.True(r.Bounds.Height >= 52, r.Bounds.Height.ToString()));
        var names = UiHelpers.ButtonNames(ui.Sheet);
        Assert.Contains("Voltar", names);                                // a page: the back arrow in place of the ✕
        Assert.DoesNotContain("Fechar", names);
        AssertTargetsOf44(ui);
        Assert.Empty(ui.Modes);

        ui.Tap("Plano");
        Assert.Equal(new[] { "plan" }, ui.Modes);                        // it asks; the claude confirms
        Assert.True(ui.Sheet.IsOpen);
        Assert.Equal("Adicionar", ui.Title);                             // back on the first page
        Assert.Contains("Manual ›", ui.Texts);

        ui.Conversation.Apply(new StatusChanged(null, "plan"));          // the claude confirms it
        UiHelpers.Pump();
        Assert.Contains("Plano ›", ui.Texts);
        ui.Tap("Modo");
        Assert.Same(ui.Row("Plano"), ui.CheckedRow);                     // and the check moves
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Browser_and_remote_rows_invoke_the_commands()
    {
        var ui = new Opened();
        ui.OpenSheet();
        ui.Tap("Navegador");
        Assert.Equal(new[] { "chrome" }, ui.Commands);
        Assert.False(ui.Sheet.IsOpen);

        ui.OpenSheet();                                                  // reopened
        ui.Tap("Remote Control");
        Assert.Equal(new[] { "chrome", "remote-control" }, ui.Commands);
        Assert.False(ui.Sheet.IsOpen);
        Assert.All(ui.Arguments, a => Assert.Equal("", a));
        Assert.Empty(ui.Links);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Remote_link_row_shows_only_when_connected()
    {
        var ui = new Opened();
        ui.Conversation.RemoteControlStatus = ChatRemoteControlStatus.Connected;
        ui.Conversation.RemoteControlUrl = RemoteUrl;
        ui.OpenSheet();
        Assert.Equal("ligado", ui.ValueOf("Remote Control"));
        Assert.Equal(new[]
        {
            "Adicionar",
            "Foto ou arquivo",
            "Comandos",
            "Modo", "Manual ›",
            "Navegador", "desligado",
            "Remote Control", "ligado",
            "Abrir claude.ai/code",
        }, ui.Texts);
        Assert.Equal(6, ui.Rows.Count);
        Assert.True(ui.Row("Abrir claude.ai/code").Bounds.Height >= 52);

        ui.Tap("Abrir claude.ai/code");
        Assert.Equal(new[] { RemoteUrl }, ui.Links);                     // the host opens it; the sheet has done its part
        Assert.False(ui.Sheet.IsOpen);
        Assert.Empty(ui.Commands);                                       // the link is not a command
        ui.Window.Close();
    }

    // ---- what it shows ----

    [AvaloniaFact]
    public void Each_row_has_its_icon_of_18_before_its_name()
    {
        var c = TouchSamples.Opus();
        c.RemoteControlStatus = ChatRemoteControlStatus.Connected;
        c.RemoteControlUrl = RemoteUrl;
        var ui = new Opened(c);
        ui.OpenSheet();
        var icons = new Dictionary<string, string>
        {
            ["Foto ou arquivo"] = "ClaudeChat.Icon.Photo",
            ["Comandos"] = "ClaudeChat.Icon.Slash",
            ["Modo"] = "ClaudeChat.Icon.ModeDefault",                    // the icon of the mode the claude is in
            ["Navegador"] = "ClaudeChat.Icon.Globe",
            ["Remote Control"] = "ClaudeChat.Icon.Remote",
            ["Abrir claude.ai/code"] = "ClaudeChat.Icon.External",
        };
        Assert.Equal(icons.Count, ui.Rows.Count);
        foreach (var pair in icons)
        {
            var row = ui.Row(pair.Key);
            var icon = Assert.Single(row.GetVisualDescendants().OfType<Path>(), p => p.Classes.Contains("ClaudeChatSheetIcon"));
            Assert.Equal(row.FindResource(pair.Value), icon.Data);
            Assert.Equal(new Size(18, 18), new Size(icon.Width, icon.Height));
            Assert.Same(row.FindResource("ClaudeChat.Foreground"), icon.Stroke);
            var name = Part<TextBlock>(row, "ClaudeChatSheetName");
            var iconRight = icon.TranslatePoint(new Point(icon.Bounds.Width, 0), row)!.Value.X;
            Assert.True(iconRight <= name.TranslatePoint(default, row)!.Value.X, pair.Key);
            // centered within a pixel: the row's text, and so its rounding, depends on the platform's fonts
            Assert.InRange(icon.TranslatePoint(new Point(0, icon.Bounds.Height / 2), row)!.Value.Y, row.Bounds.Height / 2 - 1, row.Bounds.Height / 2 + 1);
        }

        // the mode's icon follows the mode
        foreach (var (mode, key) in new[] { ("plan", "ModePlan"), ("acceptEdits", "ModeAcceptEdits"), ("auto", "ModeAuto"), ("bypassPermissions", "ModeAuto") })
        {
            c.Apply(new StatusChanged(null, mode));
            UiHelpers.Pump();
            var row = ui.Row("Modo");
            Assert.Equal(row.FindResource("ClaudeChat.Icon." + key), Part<Path>(row, "ClaudeChatSheetIcon").Data);
        }
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void The_states_follow_what_the_host_reports_while_the_sheet_is_open()
    {
        var c = TouchSamples.Opus();
        var ui = new Opened(c);
        ui.OpenSheet();

        foreach (var (status, text) in new[]
        {
            (ChatBrowserStatus.Connecting, "conectando…"), (ChatBrowserStatus.Connected, "ligado"), (ChatBrowserStatus.NoBrowser, "erro"),
            (ChatBrowserStatus.Error, "erro"), (ChatBrowserStatus.Off, "desligado"),
        })
        {
            c.BrowserStatus = status;
            UiHelpers.Pump();
            Assert.Equal(text, ui.ValueOf("Navegador"));
        }
        foreach (var (status, text) in new[]
        {
            (ChatRemoteControlStatus.Connecting, "conectando…"), (ChatRemoteControlStatus.Connected, "ligado"),
            (ChatRemoteControlStatus.Error, "erro"), (ChatRemoteControlStatus.Off, "desligado"),
        })
        {
            c.RemoteControlStatus = status;
            UiHelpers.Pump();
            Assert.Equal(text, ui.ValueOf("Remote Control"));
            Assert.DoesNotContain("Abrir claude.ai/code", ui.Texts);     // no link yet
        }

        // the link needs both: connected, and an address
        c.RemoteControlStatus = ChatRemoteControlStatus.Connected;
        UiHelpers.Pump();
        Assert.DoesNotContain("Abrir claude.ai/code", ui.Texts);
        c.RemoteControlUrl = RemoteUrl;
        UiHelpers.Pump();
        Assert.Contains("Abrir claude.ai/code", ui.Texts);
        c.RemoteControlUrl = "";
        UiHelpers.Pump();
        Assert.DoesNotContain("Abrir claude.ai/code", ui.Texts);
        c.RemoteControlUrl = RemoteUrl;
        c.RemoteControlStatus = ChatRemoteControlStatus.Error;
        UiHelpers.Pump();
        Assert.DoesNotContain("Abrir claude.ai/code", ui.Texts);
        Assert.True(ui.Sheet.IsOpen);

        c.Apply(new StatusChanged(null, "acceptEdits"));                 // and the mode, the same way
        UiHelpers.Pump();
        Assert.Contains("Editar sozinho ›", ui.Texts);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void The_mode_rows_are_the_modes_the_view_offers()
    {
        var ui = new Opened();
        ui.View.AvailableModes = new[] { "default", "plan", "auto" };
        UiHelpers.Pump();
        ui.OpenSheet();
        ui.Tap("Modo");
        Assert.Equal(new[] { "Modo", "Manual", "Plano", "Automático" }, ui.Texts);

        ui.View.AvailableModes = new[] { "acceptEdits", "default" };     // changed while the page is open
        UiHelpers.Pump();
        Assert.Equal(new[] { "Modo", "Editar sozinho", "Manual" }, ui.Texts);
        Assert.Same(ui.Row("Manual"), ui.CheckedRow);

        ui.View.AvailableModes = new string[0];                          // none to pick: no row, and the page goes back to the rows
        UiHelpers.Pump();
        Assert.True(ui.Sheet.IsOpen);
        Assert.Equal("Adicionar", ui.Title);
        Assert.DoesNotContain("Modo", ui.Texts);
        Assert.Equal(new[] { "Foto ou arquivo", "Comandos", "Navegador", "Remote Control" },
            ui.Rows.Select(r => UiHelpers.Texts(r).First()));

        // a mode the claude is in that is not among those offered shows by its name, with no check on the page
        ui.View.AvailableModes = new[] { "plan", "default" };
        ui.Conversation.Apply(new StatusChanged(null, "bypassPermissions"));
        UiHelpers.Pump();
        Assert.Contains("bypassPermissions ›", ui.Texts);
        ui.Tap("Modo");
        Assert.Empty(ui.Checks);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Texts_follow_the_language()
    {
        var c = TouchSamples.Opus();
        c.RemoteControlStatus = ChatRemoteControlStatus.Connected;
        c.RemoteControlUrl = RemoteUrl;
        c.BrowserStatus = ChatBrowserStatus.NoBrowser;
        var ui = new Opened(c);
        ui.View.Strings = ChatStrings.English;
        UiHelpers.Pump();
        ui.OpenSheet();
        Assert.Equal(new[]
        {
            "Add", "Photo or file", "Commands", "Mode", "Manual ›", "Browser", "Error", "Remote Control", "On", "Open claude.ai/code",
        }, ui.Texts);
        Assert.Contains("Close", UiHelpers.ButtonNames(ui.Sheet));

        ui.View.Strings = ChatStrings.PortugueseBrazil;                  // changed while it is open
        UiHelpers.Pump();
        Assert.Equal(new[]
        {
            "Adicionar", "Foto ou arquivo", "Comandos", "Modo", "Manual ›", "Navegador", "erro", "Remote Control", "ligado", "Abrir claude.ai/code",
        }, ui.Texts);
        Assert.Contains("Fechar", UiHelpers.ButtonNames(ui.Sheet));

        ui.Tap("Modo");
        ui.View.Strings = ChatStrings.English;
        UiHelpers.Pump();
        Assert.Equal(new[] { "Mode", "Plan", "Manual", "Edit automatically", "Auto" }, ui.Texts);
        Assert.Contains("Back", UiHelpers.ButtonNames(ui.Sheet));
        ui.Window.Close();
    }

    [Fact]
    public void The_texts_and_the_states_are_told_in_both_languages()
    {
        var (en, pt) = (ChatStrings.English, ChatStrings.PortugueseBrazil);
        Assert.Equal(("Photo or file", "Foto ou arquivo"), (en.AttachPhotoOrFile, pt.AttachPhotoOrFile));
        Assert.Equal(("Commands", "Comandos"), (en.Commands, pt.Commands));
        Assert.Equal(("Mode", "Modo"), (en.ModeTitle, pt.ModeTitle));
        Assert.Equal(("Browser", "Navegador"), (en.Browser, pt.Browser));
        Assert.Equal(("Off", "desligado"), (en.StatusOff, pt.StatusOff));
        Assert.Equal(("Connecting…", "conectando…"), (en.StatusConnecting, pt.StatusConnecting));
        Assert.Equal(("On", "ligado"), (en.StatusOn, pt.StatusOn));
        Assert.Equal(("Error", "erro"), (en.StatusError, pt.StatusError));
        Assert.Equal(("Open claude.ai/code", "Abrir claude.ai/code"), (en.OpenRemoteControlLink, pt.OpenRemoteControlLink));
        Assert.Equal(("Add", "Adicionar"), (en.AddAttachment, pt.AddAttachment));   // the sheet's title is the + button's name

        var browser = new[]
        {
            ChatBrowserStatus.Off, ChatBrowserStatus.Connecting, ChatBrowserStatus.Connected, ChatBrowserStatus.NoBrowser, ChatBrowserStatus.Error,
        };
        Assert.Equal(new[] { "Off", "Connecting…", "On", "Error", "Error" }, browser.Select(en.BrowserStatusLabel));
        Assert.Equal(new[] { "desligado", "conectando…", "ligado", "erro", "erro" }, browser.Select(pt.BrowserStatusLabel));
        var remote = new[]
        {
            ChatRemoteControlStatus.Off, ChatRemoteControlStatus.Connecting, ChatRemoteControlStatus.Connected, ChatRemoteControlStatus.Error,
        };
        Assert.Equal(new[] { "Off", "Connecting…", "On", "Error" }, remote.Select(en.RemoteControlStatusLabel));
        Assert.Equal(new[] { "desligado", "conectando…", "ligado", "erro" }, remote.Select(pt.RemoteControlStatusLabel));
        var own = en with { StatusOn = "Live" };                          // a copy changes one text
        Assert.Equal("Live", own.RemoteControlStatusLabel(ChatRemoteControlStatus.Connected));
    }

    // ---- the layer, and when it closes by itself ----

    [AvaloniaFact]
    public void A_closed_sheet_is_an_empty_hidden_layer_next_to_the_model_sheet()
    {
        var ui = new Opened();
        Assert.Empty(ui.Sheet.GetVisualDescendants());                   // nothing of it in the tree until it opens
        Assert.Null(ui.Sheet.Child);
        Assert.False(ui.Sheet.IsVisible);
        Assert.Equal(2, Grid.GetRowSpan(ui.Sheet));                       // both rows and both columns of the split: over the bookmarks panel too

        var layers = ((Grid)ui.Sheet.Parent!).Children;
        var at = layers.IndexOf(ui.Sheet);
        Assert.IsType<ModelSheet>(layers[at - 1]);                       // over the Agent map and the model sheet…
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
    public void Another_conversation_read_only_and_the_desktop_layout_close_the_sheet()
    {
        var ui = new Opened();
        ui.OpenSheet();
        ui.View.Conversation = TouchSamples.Opus();                      // another conversation
        UiHelpers.Pump();
        Assert.False(ui.Sheet.IsOpen);

        ui.OpenSheet();                                                  // the new conversation has its own state
        Assert.True(ui.Sheet.IsOpen);
        ui.View.IsReadOnly = true;                                       // the reply box goes away
        UiHelpers.Pump();
        Assert.False(ui.Sheet.IsOpen);
        ui.View.IsReadOnly = false;
        UiHelpers.Pump();
        Assert.False(ui.Sheet.IsOpen);                                   // and the sheet does not come back by itself

        ui.OpenSheet();
        Assert.True(ui.Sheet.IsOpen);
        ui.View.Layout = ChatLayout.Desktop;
        UiHelpers.Pump();
        Assert.False(ui.Sheet.IsOpen);
        ui.View.Layout = ChatLayout.Touch;                               // back to touch: closed, as it was left
        UiHelpers.Pump();
        Assert.False(ui.Sheet.IsOpen);
        ui.OpenSheet();
        Assert.True(ui.Sheet.IsOpen);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_permission_request_closes_the_sheet_and_none_opens_while_one_waits()
    {
        var ui = new Opened();
        ui.OpenSheet();
        ui.Tap("Modo");                                                  // from a page too
        Assert.True(ui.Sheet.IsOpen);

        ui.Conversation.Apply(Permission());                             // the request comes first: the sheet would cover it
        UiHelpers.Pump();
        Assert.False(ui.Sheet.IsOpen);
        Assert.Empty(ui.Modes);

        ui.OpenSheet();                                                  // the + opens nothing while the claude waits
        Assert.False(ui.Sheet.IsOpen);
        ui.Conversation.DismissPermission("r1");
        UiHelpers.Pump();
        ui.OpenSheet();
        Assert.True(ui.Sheet.IsOpen);
        Assert.Equal("Adicionar", ui.Title);                             // on the first page
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Esc_and_the_back_arrow_leave_the_page_then_the_sheet_and_the_plus_gets_the_keyboard_back()
    {
        var ui = new Opened();
        var unhandled = UiHelpers.UnhandledKeys(ui.Window);
        ui.OpenSheet();
        Assert.True(ui.Sheet.IsKeyboardFocusWithin);                     // not left on the + under the veil

        ui.Tap("Modo");
        Assert.True(ui.Sheet.IsKeyboardFocusWithin);                     // the row that was pressed is gone: the sheet keeps it
        UiHelpers.Press(ui.Window, Key.Escape);                          // the page, to the rows
        Assert.True(ui.Sheet.IsOpen);
        Assert.Equal("Adicionar", ui.Title);
        ui.Tap("Modo");
        UiHelpers.Click(ui.Window, UiHelpers.ButtonWithText(ui.Sheet, "Voltar"));
        Assert.Equal("Adicionar", ui.Title);
        Assert.Empty(ui.Modes);

        UiHelpers.Press(ui.Window, Key.Escape);                          // the rows, to the view
        Assert.False(ui.Sheet.IsOpen);
        Assert.True(ui.Plus.IsFocused);
        Assert.DoesNotContain(Key.Escape, unhandled);                    // an Esc the sheet took is not the app's
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Desktop_plus_and_slash_are_as_they_were()
    {
        var c = TouchSamples.Opus();
        var view = new ClaudeChatView { Conversation = c };              // the desktop layout
        var window = UiHelpers.Show(view);
        var composer = UiHelpers.Composer(view);
        var sheet = view.GetVisualDescendants().OfType<AddSheet>().Single();
        var picked = 0;
        composer.PickImagesOverride = () =>
        {
            picked++;
            return Task.FromResult<IReadOnlyList<byte[]>>(new[] { UiHelpers.Png(4, 4) });
        };

        UiHelpers.Click(window, UiHelpers.ButtonWithText(composer, "Add"));     // the + opens the file dialog…
        UiHelpers.PumpUntil(() => c.Attachments.Count == 1);
        Assert.Equal(1, picked);
        Assert.False(sheet.IsOpen);                                      // …and never the sheet
        Assert.False(sheet.IsEffectivelyVisible);

        UiHelpers.Click(window, UiHelpers.ButtonWithText(composer, "Show command menu (/)"));
        Assert.True(composer.HasOpenPopup);                              // the / opens the palette
        window.Close();
    }
}
