# Changelog

All notable changes to this project are written here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

## [0.4.0] - 2026-10-23

The Agent map, the context ring and attached files, as the Claude Code extension 2.1.309 has them.

### Added

- The Agent map. `ChatConversation.Agents` (the newest first) and `RunningAgents` follow the subagents: `AgentInfo`
  (`Id`, `TaskId`, `Description`, `AgentType`, `Model`, `IsBackground`, `Depth`, `ParentId`, `Status`, `StartedAt`,
  `DurationMs`, `Tokens`, `ToolUses`, `Activity`, `Result`) and `AgentStatus` (`Running`, `Completed`, `Failed`,
  `Stopped`). They come from the Agent (or Task) calls, from the claude's `system/task_started`, `task_progress`,
  `task_updated` and `task_notification` lines (the new events `AgentTaskStarted`, `AgentTaskProgress`,
  `AgentTaskUpdated` and `AgentTaskFinished`) and from the Agent call's result; `LoadTranscript` builds them from a
  session file's `<task-notification>` messages. A command an agent runs (`local_bash`) is not an agent. An agent with
  no end in a session file is shown stopped; a later live line of it (its task lines, or a line of its own) brings it
  back to running.
- The "N agents" chip in the bar, between the Remote Control chip and the model chip (the running agents, "0 agents"
  once all have ended, its dot green while one is), shown once the conversation has an agent, as the extension's. It
  opens the Agent map (`ChatConversation.ShowAgents()`, `ChatPanel.Agents`):
  the main agent's card, kept in sight while the list scrolls, and the agents as a tree, newest on top, with their time
  (counting from `Clock` while they run) and tokens. A click on an agent opens its card: status, type, model,
  background, who called it, time and tokens, what it is doing while it runs, and its answer as the extension shows it
  (the text as it came, not as markdown). Esc, the back arrow and the × work as in the extension; the dialogs' border
  takes the input's active border with the keyboard in them.
- The context ring in the bar, right after the **/** button (before the cache clock, as the extension's): shown once more than half of the window is used (`ContextUsage`), a 20 px pie in the
  send button's clay that fills in three steps (50, 75 and 99 %), with the extension's tooltip. A click opens the
  `/context` panel; unlike the extension's, it does not compact the conversation.
- Attached files: PDF (`%PDF-`), text (`.txt`, `.log`, `.md`, `.json`) and CSV files, through the **+** button, a drop,
  Ctrl+V of files copied in Explorer (as a drop; text on the clipboard still wins; images and files are told apart;
  with `MaxFiles` at 0 the dialog offers images only) or `ChatConversation.AddFileAsync(path)` and `AddFile(name, bytes)`; `Files`,
  `RemoveFile`, `MaxFiles` (5) and `MaxFileBytes` (25 MB). `ChatFile` (`Name`, `Size`, `MediaType`, `Kind`,
  `LocalPath`, `Content`, and `Progress` and `RemotePath`, which the host writes), `ChatFileKind` and `AddFileResult`.
  The host sends each file and then the message with one `[attached file: <path>]` line per file at its end
  (`Protocol.AttachedFileLine.Format` and `Split`); the conversation takes those lines off the user's messages, live
  and in session files, and shows a chip instead. While a file is being sent its chip says "Sending 42%". A message
  can be only files. `UserMessageItem.Files` holds a message's files.
- New `ChatStrings` texts, in English and Portuguese: `AgentsChip`, `AgentsChipOne`, `AgentMapTitle`,
  `AgentMapSubtitle`, `AgentMapSubtitleOne`, `AgentMain`, `AgentSpawnedByMain`, `AgentSpawnedBy`, `AgentRunning`,
  `AgentFinished`, `AgentFailed`, `AgentStopped`, `AgentBackground`, `AgentNoResult`, `AgentTokens`,
  `AgentContextTokens`, `Back`, `ContextRingTip`, `ContextRingHint`, `FileTooBig`, `FileNotSupported`, `TooManyFiles`,
  `FileUploading` and `AttachableFiles`.
- New resources: `ClaudeChat.ModalOverlay`, `AgentRunning`, `ContextRingTrack`, `ContextRingFill`,
  `ThumbnailHoverBorder` (also in "Dark Modern" where they differ) and the icons `Icon.ArrowLeft`, `Icon.FilePdf`,
  `Icon.FileText` and `Icon.Table`.
- The sample app has a made-up `agents` session (seven agents, one called by another, 71 % of the context used, a file
  in the reply box with a made-up upload), and its screenshot shows the Agent map.

### Changed

- `MessageSubmittedEventArgs` gained `Files` (empty when there are none). A host that sends only `e.Text` and
  `e.Images` drops the files: send them and add their marker lines (see the README), or set `MaxFiles` to 0 to refuse them.
- A user message whose text starts with `<task-notification>` is now read for the Agent map. It still does not show.
- `ChatPanel` gained `Agents`: a `switch` over `ShownPanel` may need a case for it.
- A sent message's images now show above its text, in one row with its files, as the extension's message attachments,
  instead of under it. The thumbnails are the extension's: 48 px high and up to 120 px wide, cut at the sides past that
  (they were 64 by 256 px at most, in proportion), with 7 px corners in a 1 px frame with 8 px corners.
- A file dropped on the reply box that is neither an image nor a PDF, text or CSV file is refused with the reason,
  instead of being ignored.

## [0.3.1] - 2026-10-23

### Fixed

- A view could leave its host blank when removed. When a host swapped the view out (a `ContentControl` whose
  content is drawn by a template), Avalonia threw while the reply box rebuilt its mode list during the detach
  ("Visual is atached to visual tree but root could not be found"), and the host's presenter never showed its new
  content. Controls no longer touch the visual tree while being detached: they only let go of the conversation's
  events and stop the cache clock's timer, and they draw the conversation as it is when they come back.

## [0.3.0] - 2026-10-22

The reply box gets the bar of the Claude Code extension: attach images, a command palette, a model and effort chip,
the prompt cache clock, the browser and Remote Control states, and the `/context` and `/usage` panels.

### Added

- Image attachments. The **+** button (file picker), Ctrl+V (the text wins when the clipboard has both), and dragging
  files onto the box attach images, shown as pills with a thumbnail, name, size and a × and a full-screen preview.
  `ChatConversation.Attachments`, `AddImageAsync` (returns an `AddImageResult`), `RemoveAttachment`, `MaxImages` (10),
  `ImageBudgetBytes` (3,750,000) and `MaxImageSide` (1568 px). Images are shrunk to fit; a refused one says why
  (`ImageTooBig`, `TooManyImages`, `NotAnImage`). The new `ChatImage` carries the media type and the base64 data,
  `MessageSubmittedEventArgs.Images` brings them to the host, and `StdinMessages.UserMessage(text, images)` writes them.
  On Windows the clipboard is read with the system calls (PNG, DIB, then DIBV5).
- The bar, in the extension's order: **+**, **/**, the cache clock, the Remote Control pill, the model chip, the mode
  and send or stop.
- The command palette (the **/** button, or `/` typed in an empty box): a filter, then the commands by exact name,
  prefix, name containing and description; arrows, Enter, Tab and Esc. The palette and the model menu close when the
  view turns read-only, when the box is hidden and when a permission request arrives. `ChatConversation.Commands`,
  `TerminalCommands`, `PaletteCommands`, `LocalCommands`, `InvokeCommand` and the `CommandInvoked` event for the local
  commands (`context`, `usage`, `remote-control`, `rc`, `chrome`), which send nothing to the claude.
- The model chip (**Opus 5.5 High**) and its menu: the models and a slider for the effort levels of the chosen model. A model id with a suffix (`claude-opus-5-5[1m]`) finds its
  model, and the chip shows the suffix (**Opus 5.5 [1m] High**).
  `ChatModel`, `ChatConversation.AvailableModels`, `CurrentModel`, `ModelSuffix`, `Effort`, `RequestedEffort`, `SetEffort` and the
  `EffortRequested` event, raised before the claude confirms, as the mode and model are. `SettingsApplied` and the
  `get_settings` answer move the chip.
- The prompt cache clock (`PromptCacheExpiresAt`, `PromptCacheTtl`, `IsPromptCacheCompacted`, and `Clock` as its time
  source), the browser banner (`BrowserStatus`, `BrowserError`, `DisconnectBrowser` and `BrowserDisconnectRequested`)
  and the Remote Control pill and banner (`RemoteControlStatus`, `RemoteControlUrl`, `RemoteControlError`,
  `OpenRemoteControl` and `RemoteControlLinkRequested`). The claude's `bridge_state` lines move the Remote Control.
- `StdinMessages.ApplyEffort`, `GetSettings`, `GetContextUsage`, `RemoteControl` and `GetChromeStatus`.
  The parser reads the `initialize`, `get_settings`, `get_context_usage` and Remote Control answers, so
  `AppendLine` fills the commands, the models, the effort and the panels.
- The `/context` and `/usage` panels (`ChatConversation.ShownPanel`). The extension shows them in VS Code dialogs,
  which have no place in the webview, so here they are drawn over the end of the list in the frame of the
  extension's menus (the menu's colors, the input's border, 8 px corners, a shadow), as wide as the reply box at
  most, with a title and a ×. Esc closes them instead of stopping Claude; they also show in a read-only view.
  Context: the tokens used of the window, a bar split by the used categories and a row per category. Usage: a bar
  per rate limit window and when it renews. New resources `ClaudeChat.ContextColor1` to `6` (VS Code's chart
  colors) and `ClaudeChat.PanelShadow`. New `ChatStrings` texts: `ContextTotal`, `UsageFiveHour`, `UsageSevenDay`,
  `UsagePercent`, `UsageResetsDay`, `DayNames` (the seven short day names, Sunday first, separated by spaces),
  `PanelLoading` (shown while the conversation is busy and the data has not come) and `UsageUnavailable` and
  `ContextUnavailable` (shown when the conversation is idle and there is none).
- The images of a user's message as thumbnails under its text, 64 px high and 256 px wide at most, in proportion and
  never larger than the image; a click opens the preview. They are decoded off the UI thread, at most twice the size
  they are drawn at.
- What a local command printed (`CommandOutputItem`) in the editor's font, in the secondary color.
- New resources for the bar, in "Dark 2026" and "Dark Modern": `BannerBackground`, `BannerForeground`, `BannerTint`,
  `BannerErrorBackground`, `BannerErrorForeground`, `RemoteControlForeground`, `RemoteControlHoverBackground`,
  `RemoteControlHoverForeground`, `MenuBackground`, `MenuForeground`, `ListHoverBackground`, `ListActiveBackground`,
  `ListActiveForeground`, `EffortThumbShadow`, `AttachmentBackground`, `AttachmentHoverBorder`,
  `AttachmentRemoveFade`, `PreviewOverlay`, `PreviewShadow`, and the icons `Icon.Add`, `Icon.Slash`, `Icon.Check`,
  `Icon.Clock`, `Icon.Close` and `Icon.Remove`. New `ChatStrings` texts for all of it, in English and Portuguese.
- The sample app plays three more recordings (an image, slash commands and an effort change), shows the new events
  in its log with the stdin line for each, answers an effort request with a `SettingsApplied`, and shows the browser
  and the Remote Control connected. `tools/gravar.py` records the new scenarios.
- New dependency: `SkiaSharp` 2.88.7, to shrink and decode the images.

### Breaking

Recompile against 0.3.0; the changes below break binary compatibility.

- `ImageBlock()` became `ImageBlock(string MediaType, string Data)`.
- `StdinMessages.UserMessage`, the `MessageSubmittedEventArgs` constructor and the `UserMessageItem` constructor
  gained an optional `images` parameter.
- `ClaudeChatView.AvailableModels` was removed (see Removed).
- The event arguments of `RemoteControlLinkRequested` are `RemoteControlLinkRequestedEventArgs`.

### Changed

- `ImageBlock` carries the data (`MediaType`, `Data`) instead of being empty, and `UserBlock.Images` brings the
  images of a user's message; `UserMessageItem.Images` holds them, and the message shows them as thumbnails.
- `ChatConversation.Submit` accepts a message that is only images (the text may be empty when something is attached).
- The clipboard is read text first (a paste of both gives the text), then the image: PNG, then DIB, then DIBV5.

### Removed

- `ClaudeChatView.AvailableModels` (and its property field). The models come from
  `ChatConversation.AvailableModels`, a list of `ChatModel`, so each conversation can have its own.

### Fixed

- The End key did not reach the end of a long list when the estimated heights were wrong; it now does.

## [0.2.1] - 2026-10-22

### Added

- `ChatConversation.UndoSubmit(MessageSubmittedEventArgs)` takes back a message the host could not deliver: it leaves
  `Items`, the conversation stops waiting for the claude to repeat it, and `IsBusy` goes back to `false` when only the
  messages sent while idle kept it busy and the claude has not answered since. It works inside `MessageSubmitted`.
  The new `SubmitUndone` event carries the text, and the reply box shows it again when the box is empty.

### Fixed

- A conversation opened in the middle of a reply showed the reply's blocks twice. The host started listening after
  the `message_start`, so the streamed blocks had no message id, and the assistant line that completes each block
  added a new item instead. A block without a message id is now completed by the next assistant line of the same
  kind from the same agent.

## [0.2.0] - 2026-10-21

### Added

- `ClaudeChatView.IsReadOnly` (a styled property, `false` by default): when `true`, the reply box and the permission
  request are hidden, while the list keeps drawing, scrolling and letting the user select text. Pending requests stay
  in `ChatConversation.PendingPermissions`, and the reply box keeps its draft, so setting it back to `false` shows
  everything again. For closed conversations, offline machines and viewers.

## [0.1.0] - 2026-10-20

First release.

### Added

- `ChatConversation`: the conversation as an observable list of items, fed with the stream-json lines of `claude -p`
  (`AppendLine`) or with a Claude Code session file (`LoadTranscript`). Lines it does not understand are skipped.
  What the user does comes back as events: `MessageSubmitted`, `PermissionAnswered`, `PermissionModeRequested`,
  `ModelRequested` and `StopRequested`. The mode and model events tell what the user asked for; the conversation's
  `PermissionMode` and `Model` change when the claude confirms.
- `Protocol`: `StreamJsonParser` and `TranscriptParser` turn lines into typed events (`ClaudeEvent`) and content blocks
  (`TextContentBlock`, `ThinkingBlock`, `ToolUseBlock`, `ToolResultBlock`, `ImageBlock`). The names avoid clashes with
  Avalonia (`TextBlock`) and CommunityToolkit.Mvvm (the base of the observable types is `ChatObservableObject`).
- `StdinMessages`: the lines to write to the claude's stdin for each of those events (`UserMessage`,
  `PermissionResponse`, `SetPermissionMode`, `SetModel`, `Interrupt`, `Initialize`).
- `ClaudeChatView`: the chat in the layout of the Claude Code extension for VS Code. It has the timeline, the user's
  messages with their times, Markdown answers, collapsible thinking, tool calls with subtitles, outputs and
  side-by-side diffs, subagents and the permission request with its queue. The reply box has the mode and model
  pickers and the send and stop buttons.
- Colors of VS Code's "Dark 2026" theme by default, and `Themes/DarkModern.axaml` for "Dark Modern". Every color, both
  fonts, the text sizes and a few places can be redefined through the `ClaudeChat.*` resources.
- `ChatStrings`: texts in English (default) and Brazilian Portuguese.
- A sample app that plays recorded sessions, and a recorder (`tools/gravar.py`) that makes new recordings and removes
  private data from them.

### Known limitations

- No syntax highlighting in diffs and code blocks (planned for 0.2).
- No attach, `/` commands or microphone buttons.

[0.1.0]: https://github.com/carlosdealmeida/claude-visual-chat/releases/tag/v0.1.0
