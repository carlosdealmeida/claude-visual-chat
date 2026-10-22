# Claude Visual Chat

[Português](https://github.com/carlosdealmeida/claude-visual-chat/blob/main/README.pt-BR.md)

An Avalonia control that draws [Claude Code](https://github.com/anthropics/claude-code) conversations the way the
Claude Code extension for VS Code draws them. Give it the lines `claude -p` writes in stream-json, or a session file
Claude Code keeps on disk; it shows the conversation and hands back what the user did (a message, a yes or no to a
permission request, a mode, model or effort change, a slash command, an image, stop) as .NET events.

> Community project, not affiliated with or endorsed by Anthropic.

![The sample app playing a recorded session: an edit and a new file, each allowed by the user](https://raw.githubusercontent.com/carlosdealmeida/claude-visual-chat/main/docs/screenshot.png)

- **Looks like the extension.** The layout follows the Claude Code extension for VS Code: the timeline with its dots,
  the tool calls with their subtitles, the side-by-side diff, the permission request and the reply box, with the same
  fonts, sizes, spacing and colors.
- **Colors of today's VS Code.** By default the colors are those of "Dark 2026", the default dark theme of current
  VS Code. "Dark Modern" ships too, ready to switch on (see [Colors](#colors)).
- **Streams or history.** Live output from `claude -p --output-format stream-json`, or a whole session file
  (`~/.claude/projects/<folder>/<session id>.jsonl`).
- **English and Brazilian Portuguese** texts built in; every text can be replaced.
- **Nothing hidden.** The library opens no links, downloads no images, runs no process and touches no file. Your app
  starts `claude` and passes the lines in and out.

Targets `netstandard2.0` with Avalonia 11.0.10. Its only dependencies are `Avalonia` 11.0.10,
`Markdown.Avalonia.Tight` 11.0.2, `SkiaSharp` 2.88.7 (to shrink and decode attached images) and
`System.Text.Json` 8.0.5.

## Install

```sh
dotnet add package ClaudeVisualChat.Avalonia
```

The control draws its own parts, but uses Avalonia's standard controls (text box, combo box, scroll bars) inside, so
**your app needs an Avalonia theme**: Fluent or Simple.

```xml
<Application xmlns="https://github.com/avaloniaui" RequestedThemeVariant="Dark">
  <Application.Styles>
    <FluentTheme />
  </Application.Styles>
</Application>
```

Then put the view where you want the chat:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:cvc="using:ClaudeVisualChat.Controls">
  <cvc:ClaudeChatView Name="Chat" />
</Window>
```

## Quick start

Your app starts `claude -p` with stream-json in and out, sends each line of its output to the conversation on the UI
thread, and writes to its input what the user does:

```csharp
using System.Diagnostics;
using System.Text;
using Avalonia.Threading;
using ClaudeVisualChat;
using ClaudeVisualChat.Protocol;

var conversation = new ChatConversation();
Chat.Conversation = conversation; // the ClaudeChatView above

var claude = Process.Start(new ProcessStartInfo("claude",
    "-p --input-format stream-json --output-format stream-json --verbose " +
    "--include-partial-messages --permission-prompt-tool stdio")
{
    WorkingDirectory = projectFolder, // the folder Claude works in
    RedirectStandardInput = true,
    RedirectStandardOutput = true,
    StandardOutputEncoding = Encoding.UTF8,
    UseShellExecute = false,
})!;

// claude -> view: one JSON line per event, applied on the UI thread
claude.OutputDataReceived += (_, e) =>
{
    if (e.Data != null) Dispatcher.UIThread.Post(() => conversation.AppendLine(e.Data));
};
claude.BeginOutputReadLine();
// first, the control handshake the official SDKs send; the control requests below were checked after it
Send(StdinMessages.Initialize("init_1"));

// view -> claude: each thing the user does is one line on claude's stdin
var ids = 0;
void Send(string line) { claude.StandardInput.Write(line + "\n"); claude.StandardInput.Flush(); }
conversation.MessageSubmitted += (_, e) => Send(StdinMessages.UserMessage(e.Text, e.Images));
conversation.PermissionAnswered += (_, e) => Send(StdinMessages.PermissionResponse(e));
conversation.PermissionModeRequested += (_, e) => Send(StdinMessages.SetPermissionMode("req_" + ++ids, e.Mode));
conversation.ModelRequested += (_, e) => Send(StdinMessages.SetModel("req_" + ++ids, e.Model));
conversation.EffortRequested += (_, e) => Send(StdinMessages.ApplyEffort("req_" + ++ids, e.Effort));
conversation.StopRequested += (_, _) => Send(StdinMessages.Interrupt("req_" + ++ids));
```

On Windows, a `claude` installed with npm is `claude.cmd`: start it with `cmd /c claude ...`, or give its full path.

`ChatConversation` is not thread-safe: call it on the UI thread, as above. Lines it does not understand (blank, not
JSON, an unknown type) are skipped. `ClaudeChatView.AvailableModes` sets the permission modes offered (`plan`,
`default`, `acceptEdits`, `auto` by default); the models are `ChatConversation.AvailableModels` (none by default,
which hides the model chip; see [Model and effort](#model-and-effort)). `PermissionModeRequested` and
`ModelRequested` are raised when the user asks for a change, before the claude confirms it: the pickers show the new
mode or model only once the claude reports it.

One view can show several conversations in turn: set `Conversation` to another one. What the user had written and not
sent stays with its conversation and comes back with it.

A message that cannot reach the claude (the connection dropped, a relay refused it) can be taken back with the event's
arguments, at once inside the handler or later:

```csharp
conversation.MessageSubmitted += (_, e) =>
{
    if (!TrySend(StdinMessages.UserMessage(e.Text))) conversation.UndoSubmit(e);
};
```

The message leaves the list, "working" goes away when nothing else keeps it, and the text returns to the reply box
when the box is empty (`SubmitUndone` carries it, for a host that wants it elsewhere).

## Attachments

The reply box takes images: the **+** button opens a file picker, Ctrl+V pastes an image (when the clipboard also has
text, the text wins), and files can be dragged onto the box. Each one shows as a pill with a thumbnail, name, size and
a ×; a click on the thumbnail opens a full preview. Images are shrunk to fit (`MaxImageSide`, 1568 px by default, and
`ImageBudgetBytes`, 3,750,000 bytes for the whole message), at most `MaxImages` (10) per message; a refused image says
why (`ImageTooBig`, `TooManyImages`, `NotAnImage` in `ChatStrings`). A message can be only images. The host sends them
along with the text, and a user's message shows them as thumbnails under it:

```csharp
conversation.MessageSubmitted += (_, e) => Send(StdinMessages.UserMessage(e.Text, e.Images));
```

`ChatConversation.AddImageAsync(bytes)` attaches an image from your own code. The shrinking and decoding use
SkiaSharp 2.88.7, which the package now depends on; Avalonia brings its native libraries. On Windows the clipboard is
read with the system calls (PNG, then DIB and DIBV5); elsewhere with Avalonia's `image/png`.

## Commands

The `/` button in the bar, or typing `/` in an empty box, opens the command palette: a filter field, then the commands
by exact name, prefix, name containing and description. Arrows move, Enter runs, Tab inserts the command in the box
and Esc closes. The commands are what the claude lists in its `initialize` answer (the control request your app sent
in the quick start): feed that answer to `AppendLine`, or set them yourself.

```csharp
conversation.Commands = new[] { new ChatCommand("review", "Review a pull request", "<pr>") };
```

A command that is not local goes to the claude as the text of a message, like any other. The ones the conversation
handles itself (`ChatConversation.LocalCommands`: `context`, `usage`, `remote-control`, `rc` and `chrome`) raise
`CommandInvoked` instead, and send nothing:

```csharp
conversation.CommandInvoked += (_, e) =>
{
    switch (e.Name)
    {
        case "context": Send(StdinMessages.GetContextUsage("req_" + ++ids)); break;  // its answer fills the panel
        case "chrome": Send(StdinMessages.GetChromeStatus("req_" + ++ids)); break;
        case "remote-control": case "rc": ToggleRemoteControl(); break;               // see below
    }
};
```

For `/chrome`, the claude answers the `GetChromeStatus` request with a `ControlAcknowledged` whose `Response`
says `connected` or `paired_browser`; the component does not read it for you: your app reads
`ControlAcknowledged.Response` and sets `conversation.BrowserStatus`.

`/context` and `/usage` also open a panel over the reply box (`ChatConversation.ShownPanel`) with the numbers the
claude last reported: `ContextUsage` comes from the answer to `get_context_usage`; `UsageLimits` from the
`rate_limit_event` lines the claude writes, so your app only has to pass every line through `AppendLine`.
(`conversation.Apply(new UsageLimitsReported(limits))` is an optional override.) With no data yet, a panel shows
"…" while the conversation is busy and "No usage data yet" otherwise. Esc closes a panel.

## Model and effort

The bar shows a chip with the model and its effort (**Opus 5.5 High**). It opens a menu with the models and a slider
for the effort levels the chosen model takes. The models come from the conversation (not from the view):

```csharp
conversation.AvailableModels = new[]
{
    new ChatModel("opus", "claude-opus-5-5", "Opus 5.5", "For complex work and everyday tasks",
        new[] { "low", "medium", "high", "xhigh", "max" }),
    new ChatModel("haiku", "claude-haiku-4-5-20251001", "Haiku 4.5", "Fastest for quick answers",
        Array.Empty<string>()), // takes no effort
};
```

The `initialize` answer brings the same list, and `AppendLine` fills it in. Like the model and the mode, the effort is
requested first and shown once the claude confirms: `EffortRequested` comes with the level (`null` for the model's
own), and the answer to `get_settings`, or a `SettingsApplied`, moves the chip. A model id with a suffix
(`claude-opus-5-5[1m]`) finds its model, and the chip shows it (**Opus 5.5 [1m] High**).

```csharp
conversation.EffortRequested += (_, e) =>
{
    Send(StdinMessages.ApplyEffort("req_" + ++ids, e.Effort));
    Send(StdinMessages.GetSettings("req_" + ++ids)); // its answer says the level now in force
};
```

A typed `/model x` or `/effort x` changes the claude too, but the chip follows only after the next `system/init` or
a `GetSettings` answer, so send `GetSettings` after them.

## Prompt cache, browser and Remote Control

- **Prompt cache.** A clock in the bar counts the minutes the claude's prompt cache has left, from the cache usage of
  the last reply. It turns red when the cache has likely expired. `Clock` is the time source it reads (the sample plays
  the recording's own).
- **Browser.** Your app reports the state of the browser extension: `BrowserStatus` (`Off`, `Connecting`, `Connected`,
  `NoBrowser`, `Error`, with `BrowserError`). A banner over the box tells it, and its disconnect button raises
  `BrowserDisconnectRequested`; the state changes only when you set it.
- **Remote Control.** `RemoteControlStatus` (`Off`, `Connecting`, `Connected`, `Error`), `RemoteControlUrl` and
  `RemoteControlError` show a pill in the bar. A click on it raises `RemoteControlLinkRequested` (`RemoteControlLinkRequestedEventArgs`) with the link, which
  your app opens (the library opens nothing). The claude's `bridge_state` lines move the status by themselves.

```csharp
conversation.BrowserStatus = ChatBrowserStatus.Connected;
conversation.BrowserDisconnectRequested += (_, _) =>
{
    DisconnectBrowser(); // yours
    conversation.BrowserStatus = ChatBrowserStatus.Off;
};

conversation.RemoteControlStatus = ChatRemoteControlStatus.Connecting;
Send(StdinMessages.RemoteControl("req_" + ++ids, true)); // the answer brings the link
conversation.RemoteControlLinkRequested += (_, e) =>
    Process.Start(new ProcessStartInfo(e.Url) { UseShellExecute = true });
```

## Open a saved session

Claude Code keeps each session in `~/.claude/projects/<folder>/<session id>.jsonl`. To show one:

```csharp
conversation.WorkingDirectory = @"C:\work\my-app"; // paths under this folder show relative
conversation.LoadTranscript(File.ReadLines(sessionFile));
```

`LoadTranscript` replaces what the conversation had. Lines that are not part of the conversation (attachments, file
history and the like) are skipped.

## Colors

Every color, both fonts, the text sizes and a few places come from resources named `ClaudeChat.*` (the keys are in
the table below). The rest is fixed in the theme: the Markdown headings (26, 19.5 and 15.2 px), a few line heights,
and the spacing, corners and heights measured from the extension. Redefine any key in the view's resources:

```xml
<cvc:ClaudeChatView>
  <cvc:ClaudeChatView.Resources>
    <SolidColorBrush x:Key="ClaudeChat.Background" Color="#101010" />
  </cvc:ClaudeChatView.Resources>
</cvc:ClaudeChatView>
```

The default colors are those of VS Code's "Dark 2026" theme. For those of "Dark Modern", merge its dictionary into the
view's resources:

```xml
<cvc:ClaudeChatView>
  <cvc:ClaudeChatView.Resources>
    <ResourceDictionary>
      <ResourceDictionary.MergedDictionaries>
        <ResourceInclude Source="avares://ClaudeVisualChat.Avalonia/Themes/DarkModern.axaml" />
      </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
  </cvc:ClaudeChatView.Resources>
</cvc:ClaudeChatView>
```

The keys, all prefixed with `ClaudeChat.`:

| Group | Keys |
| --- | --- |
| Theme colors (change with the VS Code theme) | `Background`, `Surface`, `Border`, `BorderStrong`, `Foreground`, `ForegroundMuted`, `Accent`, `AccentFill`, `AccentForeground`, `FocusBorder`, `Warning`, `Error`, `Success`, `InputBackground`, `InputBorder`, `InputActiveBorder`, `PermissionActiveBorder`, `InputPlaceholder`, `HoverBackground`, `OptionBorder`, `TimelineLine`, `Dot`, `ToolBackground`, `CodeBackground`, `CodeForeground`, `CodeBlockBackground`, `PillBackground`, `PillHoverBackground` |
| The extension's own colors | `DotSucceeded`, `DotFailed`, `DotRejected`, `SendBackground`, `SendForeground`, `FocusRing`, `Spinner` |
| Reply box shadows (`BoxShadows`) | `ComposerShadow`, `ComposerFocusShadow`, `ComposerFocusShadowAcceptEdits`, `ComposerFocusShadowPlan`, `ComposerFocusShadowAuto` |
| Diff | `DiffBackground`, `DiffForeground`, `DiffRemoved`, `DiffAdded`, `DiffRemovedText`, `DiffAddedText`, `DiffHatch`, `DiffGuide`, `DiffCurrentLine`, `DiffSign`, `DiffShadow`, `DiffFade` |
| Fonts | `FontFamily` (`Segoe UI, Segoe WPC, Inter, sans-serif`), `MonoFontFamily` (`Consolas, Courier New, monospace`) |
| Sizes | `FontSize`, `LineHeight`, `SmallFontSize`, `SmallLineHeight`, `CodeFontSize`, `EditorFontSize`, `EditorLineHeight`, `PromptFontSize`, `PromptLineHeight`, `PromptCodeFontSize` |
| Places | `ListPadding`, `ComposerMaxWidth`, `ScrollBarGutter` |
| Icons (`StreamGeometry`) | `Icon.ModeDefault`, `Icon.ModeAcceptEdits`, `Icon.ModePlan`, `Icon.ModeAuto`, `Icon.Send`, `Icon.Chevron`, `Icon.Add`, `Icon.Slash`, `Icon.Check`, `Icon.Clock`, `Icon.Close`, `Icon.Remove` |
| Menus and the command palette | `MenuBackground`, `MenuForeground`, `ListHoverBackground`, `ListActiveBackground`, `ListActiveForeground`, `EffortThumbShadow` |
| Attachments | `AttachmentBackground`, `AttachmentHoverBorder`, `AttachmentRemoveFade`, `PreviewOverlay`, `PreviewShadow` |
| Banners and Remote Control | `BannerBackground`, `BannerForeground`, `BannerTint`, `BannerErrorBackground`, `BannerErrorForeground`, `RemoteControlForeground`, `RemoteControlHoverBackground`, `RemoteControlHoverForeground` |
| `/context` and `/usage` panels | `ContextColor1` to `ContextColor6` (the chart colors of the context bar), `PanelShadow` |

`ScrollBarGutter` is the room the list leaves on its right for a scroll bar drawn over it, like Fluent's. A scroll bar
with a column of its own, like Simple's, already has its room, and the list keeps all of its width.

## Texts

The texts come from `ChatStrings`: `ChatStrings.English` (the default) or `ChatStrings.PortugueseBrazil`. It is a
record, so change only what you need by copy:

```csharp
Chat.Strings = ChatStrings.PortugueseBrazil;
Chat.Strings = ChatStrings.English with { InputPlaceholder = "Ask Claude…", TimeFormat = "HH:mm" };
```

Numbers use the record's own separators (`ThousandsSeparator`, `DecimalSeparator`), never the system culture.

## Read-only

Set `IsReadOnly` to show a conversation without letting the user act on it: a closed session, an offline machine, a
viewer.

```xml
<controls:ClaudeChatView Conversation="{Binding Chat}" IsReadOnly="True" />
```

The reply box and the permission request are hidden; the list still draws, scrolls and lets the user select text.
Pending requests stay in `ChatConversation.PendingPermissions`, and the reply box keeps its draft, so setting
`IsReadOnly` back to `false` shows everything again.

## Limitations

- The stream-json format is not a stable API, and it can change between versions of `claude`. The control was built
  against `claude` 2.1.286 and ignores what it does not know, so a new kind of line is left out rather than breaking
  the view.
- Some buttons of the extension are not here, because they need features of the host: attaching files other than
  images, and the microphone.

## Sample app

`samples/ClaudeVisualChat.Sample` plays the recorded sessions of `tests/fixtures` (an edit, Bash commands, a subagent,
an error, a denied request, an interruption, mode and model changes, an image, slash commands and an effort change) as a live stream or as a saved session, in
English or Portuguese. The panel on the right shows each event the view sends back and the stdin line your app would
write for it.

```sh
dotnet run --project samples/ClaudeVisualChat.Sample
```

It also makes the screenshots of this page without opening anything on the screen: it plays a recording to the end,
says yes to every request, saves the 1280×800 window and exits.

```sh
dotnet run --project samples/ClaudeVisualChat.Sample -- --screenshot docs/screenshot.png --scenario edit
dotnet run --project samples/ClaudeVisualChat.Sample -- --screenshot docs/screenshot-pt.png --scenario edit --lang pt
```

With `--history`, the screenshot shows the recording's session file instead of its stream.

## Recording new sessions

`tools/gravar.py` (Python 3, standard library only) records real `claude -p` sessions and cleans them up for the
repository. The scenarios are in `tools/cenarios.json`.

```sh
python -I tools/gravar.py edit      # one scenario
python -I tools/gravar.py --todos   # all of them
```

Each scenario runs in a new temporary folder with the `haiku` model, `--setting-sources project` and
`--strict-mcp-config`, so your own settings, MCP servers, skills and plugins stay out. It costs a few cents. The
recorder then keeps only the fields the tests use, replaces the folder with `C:\demo\app` and your user name with
`demo`, and writes `<scenario>.stream.ndjson` and `<scenario>.transcript.jsonl` to `tests/fixtures`.
`FixturePrivacyTests` checks the files for user folders other than `demo`'s and, when you write them one per line in
`tests/private-words.txt` (git ignores it), for your own words: your user name, company, clients. Read the files
before you commit all the same. The recorder's own tests: `python -I tools/test_gravar.py`.

## Building

```sh
dotnet build -c Release
dotnet test
```

The UI tests run headless with Skia and the system fonts, so on Linux install a font first (for example
`sudo apt-get install fonts-dejavu-core`). Where Segoe UI and Consolas are missing, the view falls back to the
system's sans-serif and monospace fonts.

## License

[MIT](https://github.com/carlosdealmeida/claude-visual-chat/blob/main/LICENSE). Copyright (c) 2026 carlosdealmeida.
