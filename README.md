# Claude Visual Chat

[Português](https://github.com/carlosdealmeida/claude-visual-chat/blob/main/README.pt-BR.md)

An Avalonia control that draws [Claude Code](https://github.com/anthropics/claude-code) conversations the way the
Claude Code extension for VS Code draws them. Give it the lines `claude -p` writes in stream-json, or a session file
Claude Code keeps on disk; it shows the conversation and hands back what the user did (a message, a yes or no to a
permission request, a mode, model or effort change, a slash command, an image or a file, stop) as .NET events.

> Community project, not affiliated with or endorsed by Anthropic.

![The sample app playing a recorded session: an edit and a new file, each allowed by the user](https://raw.githubusercontent.com/carlosdealmeida/claude-visual-chat/main/docs/screenshot.png)

- **Looks like the extension.** The layout follows the Claude Code extension for VS Code: the timeline with its dots,
  the tool calls with their subtitles, the side-by-side diff, the permission request and the reply box, with the same
  fonts, sizes, spacing and colors.
- **Colors of today's VS Code.** By default the colors are those of "Dark 2026", the default dark theme of current
  VS Code. "Dark Modern" ships too, ready to switch on (see [Colors](#colors)).
- **Streams or history.** Live output from `claude -p --output-format stream-json`, or a whole session file
  (`~/.claude/projects/<folder>/<session id>.jsonl`).
- **Desktop or phone.** The extension's layout by default, and a touch layout for a phone (`Layout = ChatLayout.Touch`):
  larger text, no timeline, the user's message in a bubble, sheets for the model, the **+** and the usage, and a
  microphone for your own speech to text (see [Touch layout](#touch-layout)).
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
conversation.MessageSubmitted += (_, e) => Send(StdinMessages.UserMessage(e));
conversation.PermissionAnswered += (_, e) => Send(StdinMessages.PermissionResponse(e));
conversation.PermissionModeRequested += (_, e) => Send(StdinMessages.SetPermissionMode("req_" + ++ids, e.Mode));
conversation.ModelRequested += (_, e) => Send(StdinMessages.SetModel("req_" + ++ids, e.Model));
conversation.EffortRequested += (_, e) => Send(StdinMessages.ApplyEffort("req_" + ++ids, e.Effort));
conversation.StopRequested += (_, _) => Send(StdinMessages.Interrupt("req_" + ++ids));
```

`StdinMessages.UserMessage(e)` writes the text, the images, a marker line for each file that landed and the message's
`uuid`, by which the claude tells when it takes a message sent while it works
(see [Replies and the timeline](#replies-and-the-timeline)). Use it for every message.

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
    if (!TrySend(StdinMessages.UserMessage(e))) conversation.UndoSubmit(e);
};
```

The message leaves the list, "working" goes away when nothing else keeps it, and the text returns to the reply box
when the box is empty (`SubmitUndone` carries it, for a host that wants it elsewhere).

## Attachments

The reply box takes images: the **+** button opens a file picker, Ctrl+V pastes an image (when the clipboard also has
text, the text wins), and files can be dragged onto the chat. Each one shows as a pill with a thumbnail, name, size and
a ×; a click on the thumbnail opens a full preview. Images are shrunk to fit (`MaxImageSide`, 1568 px by default, and
`ImageBudgetBytes`, 3,750,000 bytes for the whole message), at most `MaxImages` (10) per message; a refused image says
why (`ImageTooBig`, `TooManyImages`, `NotAnImage` in `ChatStrings`). A message can be only images. The host sends them
along with the text, and a user's message shows them as thumbnails above its text, in one row with its files (48 px
high and up to 120 px wide, as the extension's):

```csharp
conversation.MessageSubmitted += (_, e) => Send(StdinMessages.UserMessage(e)); // the text and the images
```

`ChatConversation.AddImageAsync(bytes)` attaches an image from your own code. The shrinking and decoding use
SkiaSharp 2.88.7, which the package now depends on; Avalonia brings its native libraries. On Windows the clipboard is
read with the system calls (PNG, then DIB and DIBV5); elsewhere with Avalonia's `image/png`.

## Attached files

The reply box also takes PDF, text and CSV files: the **+** button, dragging and Ctrl+V of files copied in Explorer work
for them as for images, and images and files together go each to their place (text on the clipboard still wins over
files). With `MaxFiles` at 0 the **+** dialog offers images only. A file is a `.pdf` that starts with `%PDF-`, or a
`.txt`, `.log`, `.md`, `.json` or `.csv` with no null byte in its first 8 KB; at most `MaxFiles` (5) per message and
`MaxFileBytes` (25 MB) each. A refused file says why (`FileTooBig`, `FileNotSupported` for one it cannot read,
`UnsupportedFileTypes` for a type it does not take, `TooManyFiles` in `ChatStrings`). Each one shows as a chip with its
type's icon, name and size. From your own code: `ChatConversation.AddFileAsync(path)` (reads only the size and the first
8 KB) or `AddFile(name, bytes)`.

**Your app sends the files; the component only shows them.** The claude reads a file from a path on its own machine, so
`MessageSubmitted` brings the files in `e.Files` and the text without them. Your app sends each file to the machine where
the claude runs (from `ChatFile.LocalPath`, in parts, or from `ChatFile.Content`), writes `Progress` (0 to 1) as it goes
and `RemotePath` when the file is there, then sends the message, which `StdinMessages.UserMessage(e)` writes with one
marker line per file at its end:

```csharp
conversation.MessageSubmitted += async (_, e) =>
{
    try
    {
        foreach (var file in e.Files)
        {
            // yours: copy or upload the file; report the progress on the UI thread
            file.RemotePath = await UploadAsync(file, p => Dispatcher.UIThread.Post(() => file.Progress = p));
        }
        Send(StdinMessages.UserMessage(e)); // the text, the images and "[attached file: <path>]" for each file
    }
    catch (Exception) // any failure: this handler is async void, so nothing may escape it
    {
        conversation.UndoSubmit(e); // the message and its files go back to the reply box
    }
};
```

While `Progress` is set and `RemotePath` is not, the chip says "Sending 42%" and has no ×. The marker line is
`[attached file: <path>]`, alone on its line, at the end of the message; only the markers there count, so one the user
writes in the middle of the text stays text. When the claude repeats the message, and in a session file, the
conversation takes those lines off the text and shows a chip for each (named after the path's last part, with no size).
For your own code, `AttachedFileLine.Format(path)` writes such a line and `AttachedFileLine.Split(message)` takes them
off. Set `Progress` and `RemotePath` on the UI thread.

## The reply box

The keys of the box, as in the extension 2.1.310:

- **Up and down** bring back the prompts of this conversation, the queued ones included. Up, with the caret at the start
  of the text (an empty box counts), brings the previous prompt and keeps what was being written; down, with the caret
  at the end, goes to the next one and, past the newest, gives the draft back. Clearing the box ends the walk. A
  recalled `/command` does not open the palette until it is edited.
- **Ctrl+J** breaks the line. **Shift+Tab** walks the permission modes `default`, `acceptEdits`, `plan`, `auto` and
  back, only among `ClaudeChatView.AvailableModes`, and raises `PermissionModeRequested`. **Ctrl+Alt+F** toggles the
  [Focus view](#the-focus-view). A read-only view acts on none of them, and Ctrl+J and Shift+Tab do nothing while the palette or the
  model menu is open.
- `ClaudeChatView.UseCtrlEnterToSend` (false): Enter sends. Set to true, only Ctrl or Cmd+Enter sends and Enter breaks
  the line (the touch layout always breaks the line on Enter and sends on Ctrl or Cmd+Enter).

  ```xml
  <controls:ClaudeChatView Conversation="{Binding Chat}" UseCtrlEnterToSend="True" />
  ```

- **Dropping files** anywhere on the chat attaches them: while a drag is over the view, "Drop to attach as context"
  shows over all of it. A file of a type the box does not take names the types ("Unsupported file type(s): .zip"). A
  read-only view shows nothing and takes no drop.
- **Invisible characters.** A pasted text, and a message when it is sent, lose the zero-width characters, the tags
  block, the direction controls, the variation selectors and the control codes, except where the text needs them (a
  joiner in a word of a script that joins letters or between emoji, the selectors after an emoji, one direction mark on
  a line with right-to-left letters). A notice over the box says how many went; on send it fills the box with the clean
  text, asks to look at it and sends nothing, and the next send goes. When nothing is left of the message the notice
  says so and the box stays empty. The notice ends with its ×, with an edit of the text or with another conversation,
  and a permission request hides it. A message over 50,000 characters is cut there.
- **Long pastes.** The box remembers the long texts pasted into it (more than 800 characters or more than two line
  breaks) and `MessageSubmittedEventArgs.InlinePastes` lists those still in the message. `StdinMessages.UserMessage(e)`
  writes them as `inline_pastes`, only when there are some.
- On the desktop layout the idle placeholder is "Ask Claude to edit…" (`ChatStrings.InputPlaceholder`); while Claude
  works, "Queue another message…". The touch layout keeps "Type / for commands".

## Commands

The `/` button in the bar, or typing `/` in an empty box, opens the command palette, in sections as the extension's:
Context (attach a file), Model (switch model and effort, when your app gives `AvailableModels`; account and usage),
Slash Commands and Support ("View help docs", only where your app handles `LinkRequested`). Customize and Settings come
in their place but have no rows yet, and a section with nothing to show is not there. The `/` button opens a filter
field and lists the actions; the commands show once something is typed in it, and a `/` typed in the empty box lists
every section, filtered by what follows. The commands are in alphabetical order, ranked inside each section by exact
name, prefix, name containing and description. Arrows move, Enter runs, Tab inserts the command in the box and Esc
closes. After `/name ` the hint of the command's arguments shows as ghost text in the box. The commands are what the
claude lists in its `initialize` answer (the control request your app sent in the quick start): feed that answer to
`AppendLine`, or set them yourself.

```csharp
conversation.Commands = new[] { new ChatCommand("review", "Review a pull request", "<pr>") };
```

A command that is not local goes to the claude as the text of a message, like any other. The ones the conversation
handles itself (`ChatConversation.LocalCommands`: `context`, `usage`, `remote-control`, `rc`, `chrome`, `copy`, `tasks` and `bashes`)
raise `CommandInvoked` instead, and send nothing. `/copy` and `/copy N` need nothing of your app: the view copies the
reply to the clipboard and says so in a line under it. `/tasks` and `/bashes` open the [Agent map](#agent-map) by
themselves too, and your app may ignore their `CommandInvoked`.

```csharp
conversation.CommandInvoked += (_, e) =>
{
    switch (e.Name)
    {
        case "context": Send(StdinMessages.GetContextUsage("req_" + ++ids)); break;  // its answer fills the panel
        case "chrome": Send(StdinMessages.GetChromeStatus("req_" + ++ids)); break;
        case "remote-control": case "rc": ToggleRemoteControl(); break;               // see below
        // "copy": the view's own
    }
};
```

For `/chrome`, the claude answers the `GetChromeStatus` request with a `ControlAcknowledged` whose `Response`
says `connected` or `paired_browser`; the component does not read it for you: your app reads
`ControlAcknowledged.Response` and sets `conversation.BrowserStatus`.

`/context` and `/usage` open modals over the whole view, like the extension's (`ChatConversation.ShownPanel`): "Context
usage" and "Account & Usage". They take the keyboard while open and give it back to whoever had it; a permission request
that arrives meanwhile waits until the modal closes. Esc closes one, and both open while the view is read-only. The
numbers are what the claude last reported, and `ContextUsage` comes from the answer to `get_context_usage`:

- **Context usage** shows the model (`ContextUsage.Model`), the bar by category with its table, and the five biggest
  memory files (`ContextUsage.MemoryFiles`) and custom agents (`ContextUsage.Agents`), by tokens.
- **Account & Usage** shows the account (`ChatConversation.Account`: `AuthMethod`, `Email`, `Organization` and `Plan`, from
  the account in the `initialize` answer), a bar for each limit with "Resets in 45m" under it, and the session's cost
  (`SessionCostUsd`) and times (`SessionApiDurationMs`, and `SessionDurationMs` for the wall time). "Manage usage on
  claude.ai" shows only where your app handles `LinkRequested`. `AuthMethod` is a code (`claudeai`, `api-key`,
  `bedrock`, `vertex`, `foundry`, `gateway`, `third-party`); the list may grow, and a code your app does not know is none.
  `Plan` is the plan's name, ready to show ("Max"), and is not translated.
- **The limits.** `UsageLimits` joins two reports: the `rate_limit_event` lines the claude writes (the session and the
  week; your app only passes every line through `AppendLine`) and the reply to `get_usage`. A rate limit event replaces
  the windows it names and keeps the others; the `get_usage` reply is a whole snapshot and replaces every window.
  **The Sonnet bar and the per-model bars (`UsageWindow.Model`) appear only when your app passes the `get_usage` reply
  to the conversation** (the control response, through `AppendLine`): the component never sends that request, as it
  never sends `get_context_usage`. (`conversation.Apply(new UsageLimitsReported(limits))` is an optional override.)
  With no data yet, a modal shows "Loading usage data..." while the conversation is busy and "Failed to load usage
  data" with a retry after a control error (`ControlAcknowledged.Error`; the account comes as `AccountReported`).

The touch layout keeps its usage sheet in their place, unchanged.

## Agent map

![The Agent map of the sample's made-up session: the main agent at the left, its agents at the right, newest on top, three of them at work](https://raw.githubusercontent.com/carlosdealmeida/claude-visual-chat/main/docs/screenshot-agents.png)

Once the conversation has had an agent, the bar shows a chip with how many are at work (**● 3 agents**, **0 agents**
when all have ended, as the extension counts them); its dot turns green while one is. A click on it, or `ChatConversation.ShowAgents()`, opens the
Agent map over the view (`ShownPanel` is `ChatPanel.Agents`): the main agent's card at the left, with its model and the
tokens in its context, and the agents at its right, the newest on top, each with its dot, description, time and
tokens ("7m 14s · 168.5k tokens"); an agent another one called goes under its caller. A click on an agent opens its
card: "Running · general-purpose · Sonnet 5.5 · background", who called it, the time and tokens, what it is doing while
it runs and its answer. Esc goes back from a card and closes the map; `ClosePanel()` closes it too.

Nothing to set up: the map is built from lines `AppendLine` already gets. The Agent calls, the claude's
`system/task_started`, `task_progress`, `task_updated` and `task_notification` lines and the Agent call's result fill
`ChatConversation.Agents` (`AgentInfo`: description, type, model, status, start, time, tokens, tool uses, activity,
result, caller and depth) and `RunningAgents`; a command an agent runs (`local_bash`) is not an agent. `LoadTranscript`
builds the same map from a session file, where each end comes as a `<task-notification>` message (kept out of the
list); an agent with no end there shows as stopped, and a later live line of it (its task lines, or a line of its own)
brings it back to running. The time of a running agent counts from `Clock`. The chip shows as soon as `Agents` has
one, live or from a session file.

The main card says `ChatConversation.Title`, which the host sets, or "This session" without one; a click opens it, for
"Working" or "Idle", the agents and how many are in each state. `/tasks` (and `/bashes`) opens the map. The chip's dot is
blue while an agent waits for a permission, green while agents work, red when nothing works and one failed, else grey.
The extension's card also has
the agent's prompt, its tool calls, "Open transcript" and "Stop agent", which need the agent's own transcript and a way
to stop it; they are not here.

## Bookmarks

The bookmark button under a reply marks it, and the list is `ChatConversation.Bookmarks`, public and observable, in the
order they were marked (it is emptied with the items). The command palette's "Bookmarks" row, or
`ClaudeChatView.ShowBookmarks()` and `HideBookmarks()`, shows them in a panel beside the conversation (below it in a
narrow view and in the touch layout), with a bar to resize it (a double click restores the size), the age of each reply
("5m ago", rewritten every minute while the panel shows), "Show in conversation" and "Remove bookmark". It is a side
panel, not a layer: the modals, the Diff View, the plan's document and the image preview cover it, and Esc closes it only
while the keyboard is in it. It works in a read-only view.

## Context ring

Once more than half of the context window is used, the bar shows the extension's ring: a 20 px pie in the clay orange
of the send button that fills in three steps (50, 75 and 99 %), with the tooltip "29% of context remaining until
auto-compact." It reads `ChatConversation.ContextUsage`, which the answer to `get_context_usage` fills, so ask for it
when you want the ring to move (after each reply, for example):

```csharp
conversation.PropertyChanged += (_, e) =>
{
    if (e.PropertyName == nameof(ChatConversation.IsBusy) && !conversation.IsBusy)
        Send(StdinMessages.GetContextUsage("req_" + ++ids));
};
```

A click on the ring opens the `/context` panel (it raises `CommandInvoked` with `context`, as the command does). The
extension compacts the conversation on that click; here a stray click must not, so it only shows the numbers. Its colors
are `ClaudeChat.ContextRingTrack` and `ClaudeChat.ContextRingFill`.

## Open a saved session

Claude Code keeps each session in `~/.claude/projects/<folder>/<session id>.jsonl`. To show one:

```csharp
conversation.WorkingDirectory = @"C:\work\my-app"; // paths under this folder show relative
conversation.LoadTranscript(File.ReadLines(sessionFile));
```

`LoadTranscript` replaces what the conversation had. Lines that are not part of the conversation (attachments, file
history and the like) are skipped.

## Plan and permission

On the desktop, the permission request asks as the extension 2.1.310 does: a title that names the tool and what it works
on, the command, the diff or the input under it, and the answers numbered in the order they are shown (the digit after
the last one goes to the field; "No" refuses at once with what is written in it). Option 2 names the rule and where it is
kept ("Yes, allow npm for this project (just you)"). The answers stay off for 500 ms after the card appears, and a
request that arrives while the person is typing waits until the typing stops. A button at the top right folds the card to
its title, to read the conversation behind it. The touch layout keeps the request as it was in 0.5.0, apart from the plan.

What your app does is what it already did: send what `PermissionAnswered` and `PermissionModeRequested` carry, and give
the view every line the claude writes, errors included.

```csharp
conversation.PermissionAnswered += (_, e) => Send(StdinMessages.PermissionResponse(e));
conversation.PermissionModeRequested += (_, e) => Send(StdinMessages.SetPermissionMode("req_" + ++ids, e.Mode));
```

An `ExitPlanMode` request is the plan card, "Accept this plan?", and its plan opens as a document over the list. Three
things to know:

- **The first "Yes" depends on `ClaudeChatView.AvailableModes`.** With `auto` among them it reads "Yes, and use auto
  mode" and asks for `auto`; without it, "Yes, and auto-accept", and asks for `acceptEdits`. "Yes, and manually approve
  edits" asks for `default`.
- **A "Yes" raises two events, in this order and without a wait between them:** `PermissionModeRequested` with the mode,
  then `PermissionAnswered` with Allow. Send them to the claude in the order they come, as the claude applies them in the
  order it reads them. If the claude answers a control request with an error after an `auto` was asked for, the component
  asks for `acceptEdits`. It cannot tell which request failed (the ids are yours), so an error from any other control
  request in that window has the same effect; the worst case is `acceptEdits`, the more restrictive mode.
- **"No, keep planning" is a refusal with a message:** `PermissionAnsweredEventArgs.Message` carries what the person wrote
  and, when there are comments on the plan, the comments and the passages they were made on. A refusal is never longer
  than 4,000 characters (the passages are shortened first, then the comments, then the typed text).

The document closes when the plan is approved and stays after a "No". Esc, its x and `CloseTopLayer()` close it, one
layer at a time; only an Esc with the card on top is "No". Select a passage to comment on it: the comments wait in the
card ("Continue planning") until the person sends them as feedback. The row of the plan in the list opens it again.

## Tool calls

Each call is a row in the list, drawn as the extension 2.1.310 draws it: Bash and PowerShell with their command in an IN
box (and a copy button), Edit and Write with a line under the name ("Modified", "Added 2 lines") and their diff, Glob,
Grep and the web tools with a line that opens the output, Agent with its prompt, MCP tools as "Server [tool]" and Claude
in Chrome as "Claude in Chrome [action]". Nothing to set up: they come from lines `AppendLine` already gets. What your app
may want to know:

- **A call appears when its input is complete,** not as an empty row when its block starts, so a read of
  `ChatConversation.Items` sees it a little later than before. The calls of `ToolSearch`, and their results, never appear.
- **A click on a diff in the list opens the Diff View** over the whole view, side by side, under the file's path. Esc, its
  x and `CloseTopLayer()` close it (it is a layer, right under the image preview), and it opens in a read-only view too.
  On a view 500 px wide or less (the desktop layout only) the box of an Edit or a Write is left out, as in the extension;
  the touch layout keeps it.
- **`ToolCallItem` carries more:** `FullResultText` (the whole result, at most `ToolPresentation.MaxKeptResultChars`
  characters, without terminal codes for commands), `RejectionReason` (what the person typed when refusing the call),
  `IsOutputExpanded` and `IsInputExpanded` (whether a box is open whole) and `ResultImages`. A refused call shows a red dot
  and, for an Edit, a Write or a command, a "Reason:" line; there is no "Rejected" label any more.
- **A screenshot of Claude in Chrome** (the `computer` action) shows as a thumbnail, which opens the image preview, only
  when the result's `content` carries an image block; a relay that forwards only text shows "Action completed". The
  fields of the Chrome input that the rows read come from the tool's schema, not from a recording.
- **A subagent in the background has a line under its row** that ends in "type /tasks to see its transcript". If your
  claude has no `/tasks`, change the text: `ChatStrings.English with { AgentTasksHint = "..." }`. `AgentInfo.Prompt` and
  `AgentInfo.IsWaitingForPermission` are on the agents of `ChatConversation.Agents`; a request that comes without an
  `agent_id` is taken for the agent whose step asked.
- **Insight, Artifact and notebook rows.** An Insight block (a star and "Insight" header, and a rule of 20 or more
  box-drawing dashes, in inline code) shows on its own lines in the text color, with no background. The Artifact tool reads
  "Created", "Opened" or "Published" with an "Open artifact" link, only where your app handles `LinkRequested`. The
  notebook cell edit reads "Edit Notebook Cell", its state ("Pending", "Success", "Failed") and the whole new source.
- The names of the Agent, Web Fetch, Web Search and Claude in Chrome rows are `ChatStrings` texts
  (`AgentToolTitle`, `WebFetchToolTitle`, `WebSearchToolTitle`, `ChromeToolTitle`), the same in both languages.

## The Focus view

`ClaudeChatView.FocusView` (false by default) hides the work and keeps the conversation, as the extension's Focus view does.
Each round's run of tool calls and thoughts becomes one row that opens to the steps and ends in "Collapse". Prompts and
Claude's text stay where they are. Ctrl+Alt+F toggles it, and so does the "Focus view" switch in the command palette
(tooltip "Show only your prompts and Claude's responses"); a read-only view does not hear the shortcut. An app that wants it
on by default sets it:

```xml
<controls:ClaudeChatView Conversation="{Binding Chat}" FocusView="True" />
```

What a fold shows:

- **Its label:** "3 tool calls" ("1 tool call"), with " · 1 failed" when some failed, and "Thought for 4s" for thoughts
  only. A refused or interrupted call counts as failed, and so does one with no result once the session is idle, so a
  history read back does not blink.
- **Live, only while the session works:** "Running Bash…", "Thinking…", "Waiting for permission…" or "Waiting for your
  answer…", with the count after it, and a blinking dot. A step that waits for permission keeps its fold open until the
  request is answered, also when it comes after the turn ended (a subagent in the background).
- **The latest `TodoWrite` list** right after the fold of its run.
- **Live subagents** as rows under the fold, open or closed, in the order of the list: up to 4, or 3 and "+N more
  agents" with the others added up. Each reads its description and what it does, then "1.2k tokens · 3 tools · 1m 5s"
  with the time running.

Differences from the extension, on purpose: no "Run in background" action (the VS Code host's); no tail of live agents whose
call is in no fold; and in a reopened history a call without a result counts as failed in the fold, while its own row shows
the gray dot.

## Replies and the timeline

Around Claude's reply, a turn shows what the extension 2.1.310 shows: "Copy code" on a code block (with no syntax
highlighting, as in the extension), a row under the finished turn to copy it and bookmark the reply, a long prompt
folded behind "Show more" (`UserMessageItem.IsTextExpanded`), the prompt of the turn on screen pinned at the top of the
list, a date line between days, a thinking line that counts its tokens, a card for a compaction and a working line with
a star and verbs. Nothing to set up for those. What your app may want to know:

- **The thumbs and the links need your app.** The row under a turn has "Good response" and "Bad response" only while
  your app listens to `ChatConversation.FeedbackRequested`, and a link ("Learn how we use your feedback") only while it
  listens to `LinkRequested`. The view never sends a rating or opens a page itself:

  ```csharp
  conversation.FeedbackRequested += (_, e) => SendFeedback(e.Item.Uuid, e.Positive, e.Details); // yours
  conversation.LinkRequested += (_, e) => OpenInBrowser(e.Url);                               // yours
  ```

  The thumbs show only under a reply whose line had a `uuid` (`AssistantTextItem.Uuid`), and never in a read-only view.
  A thumb down opens "What went wrong?", a layer of `CloseTopLayer()`; then `Details` is what the person wrote.
  `Positive` is null when the person takes the rating back.
- **Settings of the view** for the extension's settings your app reads and passes on: `ScrollToEndOnSend` (true: sending
  brings the list back to its end), `ShowTimestamps` (true: the times and the date lines), `UseCtrlEnterToSend` (false:
  only Ctrl or Cmd+Enter sends; see [The reply box](#the-reply-box)), `SpinnerVerbs` and `SpinnerVerbsReplace` (verbs
  of your own for the working line, added to its own or alone) and `ReduceMotion` (false; Avalonia 11.0 cannot ask the
  system, so your app sets it when the user wants less motion).

  ```xml
  <controls:ClaudeChatView Conversation="{Binding Chat}" ShowTimestamps="False" ReduceMotion="True" />
  ```

- **A message sent while Claude works waits** at the end of the list, in `ChatConversation.QueuedMessages`, not in
  `Items`, until the claude takes it. Write every message with `StdinMessages.UserMessage(e)`, which sends its `uuid`:
  the claude then says when it takes it (`command_lifecycle`). Without a uuid, the held messages go into the list at the
  next request to the API or at the turn's end.
- **New items in `Items`:** `MetaLineItem` (a line the conversation says about its own commands, such as what `/copy`
  did), `CompactionItem` (where a `NoticeItem` of `NoticeKind.Compacted` used to be), `DividerItem` (what a command
  printed in one line) and `ExternalMessageItem` (a message from another agent, another session or a channel, or a
  task's notice, where a `UserMessageItem` with the raw markup used to be). The view has a template for each; an app
  with templates of its own needs them too.
- **Who sent an `ExternalMessageItem`** comes from the line's `origin`: when it has one, the message is a card only if
  that `origin` is the sender the message claims (otherwise, `human` say, it stays the user's text); with none, the card
  reads "Sender not recorded", and a meta line with none stays out. The claude writes `origin` live only on the lines it
  replays, and session files write it, so through a relay that does not replay them, such as the Kairos one, these cards
  come only from a history.
- **Context lines under a prompt.** What the IDE sent along with a prompt (the output of a terminal, a browser tab, the
  browser's instructions, the code selected in the editor) shows as lines under the message, open to their content, and
  hidden in the Focus view. They are `UserMessageItem.Context`, a list of `MessageContext(Kind, Label, Content)` with the
  `Kind` `terminal`, `browser-tab`, `browser-instructions` or `selection`, filled when a session file is read back. The
  labels are in the language the item was built in; the extension's chips for the open file and for the selection are
  not shown.
- **Screen readers.** Messages are named "You", "You, queued", "Claude" and by their tool, a hidden "You: ..." text comes
  before each prompt, and a polite live region announces working, ready, compacting, permission requests, questions,
  plan reviews, errors and each finished reply: at most 5 sentences wait, one is said every 400 ms, it pauses under a
  modal, and a full queue drops the oldest. "Error: ..." carries the claude's own message, which is
  `TurnCompleted.ErrorMessage`, `SystemNotice.ErrorMessage` and `NoticeItem.Message`. The live event of Win32 screen
  readers in Avalonia 11.0.10 was not confirmed with a real screen reader.
- **The "Auto mode is enabled" notice** shows on an empty conversation when the mode is `auto`, until the person closes
  it. Keep `ChatConversation.AutoModeNoticeSeen` between sessions and set it back; "Learn more" shows only where your app
  handles `LinkRequested`.
- **The "Switched to {model}" band** shows above the reply box when the claude's safeguards switch models
  (`system/model_refusal_fallback`, the `Protocol.ModelRefusalFallback` event), with "Why?" and "Hide". It is
  `ChatConversation.LastModelFallback`, and `DismissModelFallback()` closes it. A history read back never shows it, and
  it gives way to the browser notice and to a Remote Control error.
- **The working line** follows `ChatConversation.Status` (what `system/status` says: "Compacting...") and
  `IsRunningStartupHooks` ("Running start-up hooks...").
- **Where it differs from the extension:** no syntax highlighting; the old tool rows are not pruned (the extension
  2.1.310 keeps them all); while Claude works the reply box reads "Queue another message…", without the suggested
  prompts and Ctrl+Esc hints the extension shows there; and the row of actions sits at the end of the turn and copies
  the whole turn.

## Colors

Every color, both fonts, the text sizes and a few places come from resources named `ClaudeChat.*` (the keys are in
the table below). The rest is fixed in the theme: the Markdown headings (26, 19.5 and 15.2 px), a few line heights,
and the spacing, corners and heights measured from the extension. In the [touch layout](#touch-layout) the sizes and
places have the phone's values. Redefine any key in the view's resources:

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
| Theme colors (change with the VS Code theme) | `Background`, `Surface`, `Border`, `BorderStrong`, `Foreground`, `ForegroundMuted`, `Accent`, `AccentFill`, `AccentForeground`, `FocusBorder`, `Warning`, `Error`, `Success`, `InputBackground`, `InputBorder`, `InputActiveBorder`, `PermissionActiveBorder`, `InputPlaceholder`, `HoverBackground`, `OptionBorder`, `TimelineLine`, `Dot`, `ToolBackground`, `CodeBackground`, `CodeForeground`, `CodeBlockBackground`, `PillBackground`, `PillHoverBackground`, `DisabledForeground` (the date line) |
| The extension's own colors | `DotSucceeded`, `DotFailed`, `DotRejected` (not used since 0.6.0), `SendBackground`, `SendForeground`, `FocusRing`, `Spinner`, `PlanCommentHighlight`, `SecondaryButtonBackground` |
| Reply box and tool box shadows (`BoxShadows`) | `ComposerShadow`, `ComposerFocusShadow`, `ComposerFocusShadowAcceptEdits`, `ComposerFocusShadowPlan`, `ComposerFocusShadowAuto`, `ToolContentFocusShadow` (the focus of a tool box) |
| Diff | `DiffBackground`, `DiffForeground`, `DiffRemoved`, `DiffAdded`, `DiffRemovedText`, `DiffAddedText`, `DiffHatch`, `DiffGuide`, `DiffCurrentLine`, `DiffSign`, `DiffShadow`, `DiffFade`, `DiffExpandHover`, `DiffExpandPillHoverBorder`, `DiffViewShadow` |
| Fonts | `FontFamily` (`Segoe UI, Segoe WPC, Inter, sans-serif`), `MonoFontFamily` (`Consolas, Courier New, monospace`) |
| Sizes | `FontSize`, `LineHeight`, `SmallFontSize`, `SmallLineHeight`, `CodeFontSize`, `EditorFontSize`, `EditorLineHeight`, `PromptFontSize`, `PromptLineHeight`, `PromptCodeFontSize`, `PromptHintFontSize`, `WorkingGlyphFontSize` (the working line's star), `WorkingRowHeight` |
| Places | `ListPadding`, `ComposerMaxWidth`, `ScrollBarGutter`, `PromptDetailMaxHeight` (the command or the diff of a permission request: a taller one scrolls), `PromptDetailPeekHeight` (the same while the field of "No" is open), `PlanCommentsMaxHeight` (the list of comments in the plan card), `FeedbackFieldHeight` (the field of "What went wrong?") |
| Icons (`StreamGeometry`) | `Icon.ModeDefault`, `Icon.ModeAcceptEdits`, `Icon.ModePlan`, `Icon.ModeAuto`, `Icon.Send`, `Icon.Chevron`, `Icon.ChevronDown`, `Icon.ChevronUp`, `Icon.Add`, `Icon.Slash`, `Icon.Check`, `Icon.Clock`, `Icon.Close`, `Icon.Remove`, `Icon.ArrowLeft`, `Icon.FilePdf`, `Icon.FileText`, `Icon.Table`, `Icon.Copy`, `Icon.Mic`, `Icon.Refresh`, `Icon.Photo`, `Icon.Globe`, `Icon.Remote`, `Icon.External`, `Icon.Bookmark`, `Icon.ThumbUp`, `Icon.ThumbDown` |
| Menus and the command palette | `MenuBackground`, `MenuForeground`, `ListHoverBackground`, `ListActiveBackground`, `ListActiveForeground`, `EffortThumbShadow` |
| Attachments | `AttachmentBackground`, `AttachmentHoverBorder`, `AttachmentRemoveFade`, `ThumbnailHoverBorder`, `PreviewOverlay`, `PreviewShadow` |
| Agent map and context ring | `ModalOverlay`, `AgentRunning`, `AgentWaiting`, `ContextRingTrack`, `ContextRingFill` |
| Banners and Remote Control | `BannerBackground`, `BannerForeground`, `BannerTint`, `BannerErrorBackground`, `BannerErrorForeground`, `RemoteControlForeground`, `RemoteControlHoverBackground`, `RemoteControlHoverForeground` |
| `/context` and `/usage` modals | `ContextColor1` to `ContextColor8` (the chart colors of the context bar), `PanelShadow` |
| Auto mode notice | `AutoNoticeShadow` (and the icon `Icon.Bolt`) |
| Touch layout (see [Touch layout](#touch-layout)) | `UserBubble`, `SheetBackground`, `SheetGroupBackground`, `SheetHandle`, `SheetOverlay`, `PermissionWarningBorder` |

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

The reply box and the permission request are hidden, and the view takes no dropped files; the list still draws, scrolls
and lets the user select text. Pending requests stay in `ChatConversation.PendingPermissions`, and the reply box keeps
its draft, so setting `IsReadOnly` back to `false` shows everything again. The thumbs under a reply and the fade over
the reply box go too; copying and bookmarking a reply still work.

## Touch layout

![The sample app in the touch layout, playing a recorded session: an edit and a new file. The user's message is a bubble at the right, the reply has no timeline, each tool call is a block, and the reply box has its bar](https://raw.githubusercontent.com/carlosdealmeida/claude-visual-chat/main/docs/screenshot-touch.png)

Set `Layout` to `ChatLayout.Touch` to lay the view out for a finger on a phone, in the style of Anthropic's Claude app,
instead of the extension's layout. `Desktop` is the default, and it does not change. Everything inside the view follows
`Layout`; each part that changes with it takes the pseudo-class `:touch`.

```xml
<cvc:ClaudeChatView Conversation="{Binding Chat}"
                    Layout="Touch"
                    Dictation="{Binding Dictation}"
                    UsageSheetContent="{Binding Meters}" />
```

What changes:

- **The list.** Text of 15 px on a 22.5 px line, and no timeline: everything starts at the margin. The user's message is
  a bubble at the right, at most 85 % of the list wide, with no time. A tool call is a block with a frame and its state
  dot at the start of its header. A finished turn ends with a copy button (⧉) that puts what Claude wrote in the turn on
  the clipboard, in place of the line of duration, tokens and cost. The agents chip is at the end of the list ("Agents · 2
  running") and opens the Agent map.
- **The reply box.** A rounded box with its text over a bar of targets of 44 px: **+**, the model pill, the context ring
  (from the first token, its arc the share used rather than the extension's steps), the microphone (see below) and send,
  or stop while Claude works. Enter breaks the line, and Ctrl or Cmd+Enter sends. The placeholder stays "Type / for
  commands". The **/** button, the Remote Control chip and the mode picker are not in the bar: the **+** sheet has them.
  The touch layout does not draw the cache clock. The model pill ends in an ellipsis, the level first, when the bar
  leaves it less room than its name and level need.
- **Sheets.** The model pill, the **+** and the context ring each open a sheet that rises from the bottom of the view.
  The ✕, Esc or a tap on the veil over the rest of the view closes it, and a page inside a sheet has a back arrow.
  - *Select model*: the models, each with its description (`ChatStrings.ModelDescriptions` translates the claude's) and
    the current one checked; for a model that takes an effort, an "Effort" row that opens its levels.
  - *Add* (the **+**): "Photo or file" (the file dialog), "Commands" (the command palette), "Mode" (the permission
    modes), "Browser" and "Remote Control" with their state, and, while Remote Control is connected and has a link,
    "Open claude.ai/code", which raises `RemoteControlLinkRequested`.
  - *Usage* (the ring): the context window by category and, under it, what your app puts in `UsageSheetContent`, or the
    limits of the plan the claude reported. It takes the place of the `/context` and `/usage` panels, which are not
    drawn in this layout; `ShownPanel` and `CommandInvoked` work as before, and its ↻ asks for the data again.
- **The permission request** asks a short question ("Edit OrderService.cs?", "Run this command?") with the command or
  the diff under it, and its answers go across the block at 48 px. While one waits no sheet opens (an open one closes
  when it comes), and the keyboard goes to no one: a reply box that took it back after an answer would raise the
  on-screen keyboard.
- **The Agent map.** Its subtitle wraps and speaks of a tap. When the tree does not fit the map's width (a phone held
  upright), it goes down: the main agent's card on top and the agents under it, all as wide as the map, an agent another
  one called still 32 px in under its caller, so the map scrolls only down. Where it fits (a tablet), it is the desktop's.
- **Targets.** What a finger taps is 44 px at least: the buttons of the bar, the rows of the sheets and of the command
  palette, the ✕ of the banners, of the image preview and of the Agent map, and its back arrow. A row of the palette
  runs its command on a tap, not on the press, so a finger that scrolls the list runs nothing.

### What your app adds to the usage sheet

`UsageSheetContent` is what the usage sheet shows under the context window, in place of the plan's limits: your app's
own meters, say. A control is shown as it is; any other object is made into one by the data templates your app has
(the view's, its window's or the application's), as in a `ContentControl`. It is drawn only in the touch layout, and
only while the sheet is open.

```xml
<cvc:ClaudeChatView Layout="Touch" UsageSheetContent="{Binding Meters}">
  <cvc:ClaudeChatView.DataTemplates>
    <DataTemplate DataType="vm:Meters">
      <ProgressBar Value="{Binding SessionShare}" Maximum="100" />
    </DataTemplate>
  </cvc:ClaudeChatView.DataTemplates>
</cvc:ClaudeChatView>
```

### Dictation

The microphone is your app's: the component has no speech recognizer. Give the view an `IChatDictation`, and the touch
layout's bar has a microphone while its `IsAvailable` is true. The desktop layout never has one.

```csharp
public interface IChatDictation
{
    bool IsAvailable { get; }                  // for example, the language's model is on the device
    event EventHandler? AvailabilityChanged;   // raised when IsAvailable changes; may be from any thread
    Task<string?> ListenAsync(IProgress<string>? partial, CancellationToken stop);
}

Chat.Dictation = new MyDictation(); // yours
```

A tap on the microphone asks for one listening, on the UI thread. The box is locked while it lasts: what is heard so far
(`partial`) goes into it at the caret, with a space on each side of the words, and what `ListenAsync` returns is the
final text. A second tap cancels `stop`: `ListenAsync` ends the listening early and returns what it heard (null for
nothing); it does not throw. What is heard is never sent by itself. The caret ends after the words, and the keyboard
goes back to the box only if the box had it when the finger came down: the component never raises the on-screen
keyboard on its own. The listening is cut short, and what comes back after is ignored, when another conversation is
shown, the layout changes, the view turns read-only or leaves the window, or the dictation goes away or stops being
available; the words already in the box stay. Text put in the box while it listens (a message your app gives back with
`UndoSubmit`, a command from the palette) stays too, and the words go after it.

### The back button

A phone's back button does not reach the view as Esc. Call `CloseTopLayer()` from it: it closes one layer per call, the
top-most first, and returns whether it closed one.

```csharp
void OnBackPressed()
{
    if (!Chat.CloseTopLayer()) GoBack(); // yours: nothing of the view was open
}
```

The layers, top-most first: the image preview; the Diff View; "What went wrong?" of a bad rating; a menu of the reply
box (the commands or the models); a sheet (the usage sheet before the others, and a page inside a sheet goes back to the
first page before the sheet closes); the box of a comment on the plan; an agent's card in the Agent map, which goes back
to the map; the panel that is open (the Agent map, or the `/context` and `/usage` panels of the desktop layout); the
plan's document; and the field where the user writes why a request is refused. A permission request is no layer: it has
to be answered.

### Your own parts under a sheet

A sheet dims the view with its veil, and a tap on the veil closes it. What your app draws outside the view (a top bar with
a back button and a title, say) stays lit unless your app dims it too. `IsSheetOpen` is true while a sheet is on screen
(the model sheet, the **+** sheet or the usage sheet); it is a read-only property that a binding follows, and it follows
every way a sheet closes, the ones the view does by itself included (another conversation, the layout going back to
`Desktop`, the view becoming read-only, a permission request). The usage sheet is information only, so it stays open
when the view becomes read-only, and `IsSheetOpen` stays true with it. `DismissSheet()` does what a tap on the veil of the sheet on
top does and returns true, or returns false and closes nothing when no sheet is open. The veil's color is the resource
`ClaudeChat.SheetOverlay`.

```xml
<Grid RowDefinitions="56,*">
  <local:TopBar />
  <cvc:ClaudeChatView Name="Chat" Grid.Row="1" Layout="Touch" />
  <!-- over the top bar, while a sheet dims the view -->
  <Border Background="{DynamicResource ClaudeChat.SheetOverlay}" Focusable="False"
          IsVisible="{Binding #Chat.IsSheetOpen}" PointerPressed="OnVeilPressed" />
</Grid>
```

```csharp
void OnVeilPressed(object? sender, PointerPressedEventArgs e)
{
    e.Handled = true;          // nothing under the veil gets the tap
    Chat.DismissSheet();
}
```

The view's theme defines `ClaudeChat.SheetOverlay` inside the view, so a part outside it finds the key only where your app
has it too: merge `Themes/DarkModern.axaml` into the resources of the parent of both (or define the key there, with the
color you give the view). Unlike `CloseTopLayer()`, `DismissSheet()` closes the whole sheet from any of its pages, as the veil
does, and touches no other layer.

### Colors and sizes

The touch layout's colors are keys of the theme like the others, prefixed with `ClaudeChat.` too. `UserBubble` (the user's bubble),
`SheetBackground`, `SheetGroupBackground` (the blocks of rows in a sheet, and the buttons of the bar), `SheetHandle` (the
grab handle of a sheet) and `SheetOverlay` (the veil over the rest of the view) are in "Dark 2026" and in "Dark Modern".
`PermissionWarningBorder` (the frame of a request) is in "Dark 2026" only. "Dark Modern" does not define it, so it keeps
the "Dark 2026" color. The touch layout's sizes are the same `ClaudeChat.*` keys as the desktop's, with the phone's
values, in `Themes/Touch.axaml`; the view merges that file into its resources while `Layout` is `Touch` and takes it out
when the layout goes back, so your app does nothing. A key your app redefines in the view's resources still wins:

```xml
<cvc:ClaudeChatView Layout="Touch">
  <cvc:ClaudeChatView.Resources>
    <x:Double x:Key="ClaudeChat.FontSize">16</x:Double>
  </cvc:ClaudeChatView.Resources>
</cvc:ClaudeChatView>
```

The sample plays a recording in a phone-sized window: `dotnet run --project samples/ClaudeVisualChat.Sample -- --touch`
(see [Sample app](#sample-app)).

## Limitations

- The stream-json format is not a stable API, and it can change between versions of `claude`. The control was built
  against `claude` 2.1.286 and ignores what it does not know, so a new kind of line is left out rather than breaking
  the view.
- Some buttons of the extension are not here, because they need features of the host: on an agent's card, its
  transcript and "Stop agent". The microphone is there in the touch layout, once your app gives the view its speech to
  text (see [Dictation](#dictation)); the desktop layout has none.
- Left out of this release, as the extension 2.1.310 has them: LaTeX (the extension draws it with KaTeX, and this release
  takes no new dependency); the "Session paused" card, which needs the `request_user_dialog` that the Kairos machine
  refuses; the suggestion to make auto mode the default (it needs `auto_default_nudge`); and a "Why?" text for each
  category of the model switch (there is one generic text). The Agent map has no tail row for a live agent outside any
  fold: the map lists it.

## Sample app

`samples/ClaudeVisualChat.Sample` plays the recorded sessions of `tests/fixtures` (an edit, Bash commands, a subagent,
an error, a denied request, an interruption, mode and model changes, an image, slash commands and an effort change) as a live stream or as a saved session, in
English or Portuguese. The panel on the right shows each event the view sends back and the stdin line your app would
write for it. One more session, `agents`, is made up in the sample (`DemoSession.cs`), since no short recording has
it: seven agents (one called by another, three still at work), 71 % of the context used, and a file left in the reply
box. Sending it shows a made-up upload, its progress and the message with its marker line.

```sh
dotnet run --project samples/ClaudeVisualChat.Sample
```

With `--touch` the window is a phone's screen (412×892) with the view alone in it, in the touch layout. The sample's
bar and log have no room next to it, so the recording and its language come from the command line (`--scenario`,
`--lang`), and it plays when the window opens.

```sh
dotnet run --project samples/ClaudeVisualChat.Sample -- --touch
```

It also makes the screenshots of this page without opening anything on the screen: it plays a recording to the end,
says yes to every request, saves the 1280×800 window (412×892 with `--touch`) and exits.

```sh
dotnet run --project samples/ClaudeVisualChat.Sample -- --screenshot docs/screenshot.png --scenario edit
dotnet run --project samples/ClaudeVisualChat.Sample -- --screenshot docs/screenshot-pt.png --scenario edit --lang pt
dotnet run --project samples/ClaudeVisualChat.Sample -- --screenshot docs/screenshot-agents.png --scenario agents
dotnet run --project samples/ClaudeVisualChat.Sample -- --screenshot docs/screenshot-agents-pt.png --scenario agents --lang pt
dotnet run --project samples/ClaudeVisualChat.Sample -- --touch --screenshot docs/screenshot-touch.png --scenario edit
dotnet run --project samples/ClaudeVisualChat.Sample -- --touch --screenshot docs/screenshot-touch-pt.png --scenario edit --lang pt
```

With `--history`, the screenshot shows the recording's session file instead of its stream. The `agents` screenshot
also sends the file (stopped at 42 %) with a message and opens the Agent map.

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
