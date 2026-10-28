# Changelog

All notable changes to this project are written here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

## [0.6.0] - unreleased

### Added

- The permission request in the desktop layout asks as the Claude Code extension 2.1.310 does. The title names the tool
  and what it works on: "Allow this bash command?" with the command in a box and, under it, the call's own
  description; "Make this edit to OrderService.cs?" and "Allow write to ..." with the diff in the card; "Allow reading
  from ...", glob, grep, fetch and search with the path, the url or the query (and the domains allowed and blocked);
  "Use skill /x?" with its note, its description and its arguments; any other tool "Do you want to proceed with X?" over
  a "Details" fold with the input as JSON. "No" refuses at once with what is in the field under the answers, which is
  always there. Esc is "No" too, and in the field Enter and Esc send "No" with its text. The digits pick the answers in
  the order they are shown, and the digit after the last one goes to the field; up and down move between the answers
  and the field. The answers stay off for 500 ms after the card appears, so a key that was meant for the reply box
  answers nothing, and a request that comes while the person was typing (less than 1.5 s after the last key) waits
  behind "Waiting for permission..." until the typing stops. A request with `default_to_no` focuses "No" and takes no
  digit; one with `suppress_always_allow_rule` has no option 2. `PermissionRequested` and `PendingPermission` carry
  `AgentId`, `DefaultToNo` and `SuppressAlwaysAllowRule`.
- Option 2 of the card names the rule and where it is kept: "Yes, allow npm for this project (just you)", "Yes, allow
  access to data/ for this session", "Yes, allow all edits this session" or "Yes, return to normal mode". A rule
  longer than 20 characters is cut to 17 and an ellipsis, with its whole text in the tooltip. The place ("this project
  (just you)", "this project", "all projects", "this session") and the "N more" of several rules are underlined and
  have a tooltip; "N more" lists the rules left out.
- The card folds to its title with a button of 20 px at the top right, to read the conversation behind it. Folded, Esc
  does not refuse, the scroll keys move the conversation, and unfolding arms the answers again for 500 ms.
- The plan card for `ExitPlanMode`: "Accept this plan?" with "Yes, and use auto mode", "Yes, and manually approve
  edits" and "No, keep planning", the keys 1 to 3 and 4 for the field. When the view's `AvailableModes` has no `auto`,
  the first answer reads "Yes, and auto-accept" and asks for `acceptEdits`. It exists in the touch layout too, in the
  flow of the 0.5.0 (answers across the block, the first in the accent, "Send" under the field).
- The plan opens by itself as a document over the list, with the card under it still in use: the plan's first heading
  (or "Claude's Plan") as its title, the plan's file, and a "Ready for review" strip that says text can be selected to
  comment. The document closes when the plan is approved and stays after a "No". Esc, the x and
  `ClaudeChatView.CloseTopLayer()` close it, one layer at a time: the first Esc closes the document, and only an Esc
  with the card on top is "No".
- The timeline's row for `ExitPlanMode` reads "Claude's Plan" and the file, or "Plan Mode", with "User approved the
  plan" or "Stayed in plan mode" under it and no IN/OUT box; a click on its title opens the plan again, to read, also
  from a history (without the comments).
- Comments on the plan: a selection in the document offers "Add Comment", which opens a box of 320 px over the passage
  (Enter adds, Shift+Enter breaks the line, Esc cancels; in the touch layout Enter breaks the line and the button
  adds). The passage is highlighted, with a numbered circle whose tooltip shows the comment. With comments the card
  becomes "Continue planning": a list "Comments (N)" over the title, each item with an x, and one answer, "Send
  feedback and keep planning", which refuses with the text and the comments and then clears them.
- New `ChatStrings` texts, in English and Portuguese: `PermissionBash`, `PermissionPowerShell`, `PermissionEdit`,
  `PermissionWrite`, `PermissionRead`, `PermissionGlob`, `PermissionGlobAnywhere`, `PermissionGrep`,
  `PermissionGrepAnywhere`, `PermissionWebFetch`, `PermissionWebSearch`, `PermissionAllowedDomains`,
  `PermissionBlockedDomains`, `PermissionSkill`, `PermissionSkillNote`, `PermissionSkillArguments`,
  `PermissionProceed`, `PermissionDetails`, `PermissionRefuse`, `PermissionEscToCancel`, `PermissionCollapse`,
  `PermissionExpand`, `WaitingForPermission`; `AllowAlwaysOneRule`, `AllowAlwaysTwoRules`, `AllowAlwaysManyRules`,
  `AllowAlwaysMoreRules`, `AllowAlwaysOneDirectory`, `AllowAlwaysManyDirectories`, `AllowAlwaysAllEdits`,
  `AllowAlwaysNormalMode`, `AllowAlwaysThisProjectJustYou`, `AllowAlwaysThisProjectJustYouTip`,
  `AllowAlwaysAllProjects`, `AllowAlwaysAllProjectsTip`, `AllowAlwaysThisProjectShared`,
  `AllowAlwaysThisProjectSharedTip`, `AllowAlwaysThisSession`, `AllowAlwaysThisSessionTip`; `PlanAccept`,
  `PlanCommentHint`, `PlanYesAutoMode`, `PlanYesAutoAccept`, `PlanYesManual`, `PlanKeepPlanning`, `PlanTitle`,
  `PlanModeTool`, `PlanApproved`, `PlanStayedInPlanMode`, `PlanReadyForReview`, `PlanSelectToComment`,
  `PlanAddComment`, `PlanCommentPlaceholder`, `PlanCommentCancel`, `PlanComments`, `PlanRemoveComment`,
  `PlanContinuePlanning`, `PlanOneCommentIncluded`, `PlanCommentsIncluded` and `PlanSendFeedback`.
- New theme resources: `ClaudeChat.PlanCommentHighlight`, `ClaudeChat.SecondaryButtonBackground`,
  `ClaudeChat.PlanCommentsMaxHeight` (160), `ClaudeChat.PromptHintFontSize` and the icons `ClaudeChat.Icon.ChevronDown`
  and `ClaudeChat.Icon.ChevronUp`.

### Changed

- The new permission card is the desktop layout's. In the touch layout the request stays as the 0.5.0 left it (a short
  question, the answers across the block, a field with "Send" under "No", 300 ms), apart from the plan card above. The
  fold of the card is the desktop's too: the touch layout cannot fold a tall request.
- On the desktop, "No" refuses at once, where it used to open the field first, and the answers stay off for 500 ms
  instead of 300 ms. `PermissionYesAlways` is no longer shown there: `PermissionYesAlwaysBeyondSession` is the option
  when the suggestions give nothing to name. The touch layout keeps both as in 0.5.0.
- The plan card's "Yes" raises `PermissionModeRequested` with the mode and, right after and without waiting for the
  claude to confirm it, `PermissionAnswered` (Allow), which is what the extension does: the claude applies the two in
  the order it reads them. "Yes, and manually approve edits" asks for the mode `default`.
- Known limit of the way back from `auto`: if the claude answers a control request with an error after the plan was
  approved with `auto`, the component asks for `acceptEdits`. It does not know the id of the request that failed (the
  host picks the ids), so an error of any other control request in that window, a failed `set_model` for instance, has
  the same effect. The worst case ends in `acceptEdits`, the more restrictive mode.
- A refusal carries at most 4,000 characters (the limit of the Kairos relay): the typed text, the plan comments and the
  quoted passages together. Over it, the quoted passages are shortened first, all to the same length and with an
  ellipsis, then the comments, then the typed text, and only as a last resort the whole message. This holds for the "No"
  of every permission card, and for the "Send" of the touch layout.
- Where the extension and the first design notes differ, the card follows the extension: the request's own description
  is not shown, a Bash command has no line count, and the plan is a document, not a block in the list. The differences
  from the extension that remain are deliberate: the plan's document is a panel over the list, where the extension
  opens an editor tab beside the chat; "No, keep planning" with an empty field sends "User chose to stay in plan mode
  and continue planning", and with text sends that text without the extension's refusal sentence (the host's contract
  has only the message); the extension sends each quoted passage whole, here they are shortened to fit the 4,000; a
  selection to comment lies inside one paragraph or heading, it does not cross blocks; "Yes, and manually approve
  edits" asks for the mode `default` through `PermissionModeRequested`, where the extension sends `updatedPermissions`
  with `setMode`, which the host cannot send; the list of comments in the card is 160 px high
  (`ClaudeChat.PlanCommentsMaxHeight`), where the extension's depends on the window; Edit and Write keep their diff in
  the card, where the extension opens it in a tab; the command of a Bash request cannot be edited; and in the touch
  layout the plan card has no line "Select text in the preview to add comments".
- The plan's document sits below the `/context` and `/usage` panels and the Agent map, which show over it
  and close before it; the comment box sits above all of them.
- The plan's document opens by itself only when the request carries the plan's text, and closes when the view turns
  read-only. A plan that was cancelled or got no answer leaves the document open, as only an approval closes it.
- `ToolPresentation.Summary("ExitPlanMode", ...)` returns the plan file's name, or "", where it returned the first
  string of the input (the plan's text). An `ExitPlanMode` call that comes without the plan (`input: {}`) takes the
  `input` of its permission request.
- `ChatStrings.PermissionRunCommand`, `PermissionEditFile` and `PermissionUseTool` are not used on the desktop since
  0.6.0 (the touch layout still uses them). Nothing public was removed or renamed.

### Fixed

## [0.5.0] - 2026-10-27

The touch layout: the view laid out for a finger on a phone, in the style of Anthropic's Claude app.

### Added

- `ChatLayout` (`Desktop`, the default, and `Touch`) and `ClaudeChatView.Layout`, inherited by everything inside the
  view; the view and each part that changes with it take the pseudo-class `:touch`. In the touch layout: text of 15 px
  on a 22.5 px line and no timeline; the user's message in a bubble at the right, with no time; each tool call in a
  block with a frame and its state dot at the start; a copy button (⧉) under a finished turn, in place of the line of
  duration, tokens and cost; the "Agents · N" chip at the end of the list; a rounded reply box over a bar of targets
  of 44 px (**+**, the model pill, the context ring from the first token, the microphone and send or stop), where Enter
  breaks the line and Ctrl+Enter sends; the model pill ends in an ellipsis, the level first, when the bar leaves it less
  room than its name and level need (on a phone 360 DIPs wide, with the ring, the microphone and send on the bar), where it
  would cut a letter in half; and targets of 44 px in the banners, the image preview, the Agent map and the command
  palette, whose rows run their command on a tap and not on the press, so a finger that scrolls the list runs nothing. The
  desktop layout does not change: its tree has only the hidden, empty layers of the sheets more.
- Sheets that rise from the bottom of the view in the touch layout: the model sheet (the models with their
  descriptions, and the effort levels on a page of their own), the **+** sheet (photo or file, commands, the permission
  mode on a page, browser, Remote Control and the link to open it) and the usage sheet, which takes the place of the
  `/context` and `/usage` panels there (the context window by category and, under it, the host's own content or the
  plan's limits; its ↻ asks for the data again). The ✕, Esc or a tap on the veil closes a sheet. A permission request
  closes the open sheets and keeps new ones from opening while it waits.
- `ClaudeChatView.UsageSheetContent`: what the host shows in the usage sheet under the context window, in place of the
  plan's limits. A control is shown as it is; any other object through the host's data templates.
- The permission request in the touch layout: a short question ("Edit OrderService.cs?", "Run this command?", "Use
  Fetch?"), its answers across the block at 48 px with the first in the accent, a "Send" button under the field of
  "No", and its command or diff scrolling inside a frame of 240 px, which shrinks to a peek of 56 px while that field is
  open, so that the keyboard leaves it in sight. No keyboard is given to the reply box after an answer or when a layer
  closes: on a phone that raises the on-screen keyboard.
- `IChatDictation` and `ClaudeChatView.Dictation`: the host's speech to text. While it is available, the touch layout's
  bar has a microphone: a tap listens, what is heard goes into the box at the caret and is never sent by itself, and a
  second tap stops it. Text put in the box while it listens (a message the host gives back with `UndoSubmit`, a command
  from the palette) stays, and the words go after it.
- `ClaudeChatView.CloseTopLayer()`, for a host's back button, which does not reach the view as Esc: it closes one layer
  per call, the top-most first, and returns whether it closed one.
- `ClaudeChatView.IsSheetOpen` (a read-only `DirectProperty`, for a binding) and `ClaudeChatView.DismissSheet()`, for a host
  that draws its own parts around the view (a top bar): while a sheet is on screen the host dims them with a veil of its own,
  in the color `ClaudeChat.SheetOverlay`, and a tap on it calls `DismissSheet()`, which does what a tap on the sheet's veil
  does and returns false, closing nothing, when no sheet is open.
- The Agent map in the touch layout: when the tree does not fit the map's width (a phone), it goes down, the main agent's
  card on top and the agents under it, as wide as the map, an agent another one called still 32 px in under its caller, and
  the map scrolls only down; where it fits (a tablet), it is the desktop's. Its rows' lines are tall enough for the touch
  layout's text, whose descenders the desktop's lines cut. Its subtitle wraps and speaks of a tap: new
  `ChatStrings` texts `AgentMapSubtitleTouch` and `AgentMapSubtitleTouchOne`, in English and Portuguese.
- `Themes/Touch.axaml`: the touch layout's sizes, the same `ClaudeChat.*` keys as the desktop's with the phone's values,
  merged into the view's resources while the layout is `Touch`. New resources `ClaudeChat.UserBubble`,
  `SheetBackground`, `SheetGroupBackground`, `SheetHandle` and `SheetOverlay` (in "Dark 2026" and "Dark Modern"),
  `PermissionWarningBorder`, `PromptDetailMaxHeight` and `PromptDetailPeekHeight` (the heights of the request's command
  or diff; the desktop's stay at 200), and the icons `Icon.Copy`, `Icon.Mic`, `Icon.Refresh`, `Icon.Photo`,
  `Icon.Globe`, `Icon.Remote` and `Icon.External`.
- New `ChatStrings` texts, in English and Portuguese: `InputPlaceholderTouch`, `Dictate`, `StopDictation`,
  `PermissionRunCommandQuestion`, `PermissionEditFileQuestion`, `PermissionUseToolQuestion`, `AttachPhotoOrFile`,
  `Commands`, `ModeTitle`, `Browser`, `StatusOff`, `StatusConnecting`, `StatusOn`, `StatusError`,
  `OpenRemoteControlLink`, `SelectModelTitle`, `OtherModels`, `ModelDescriptions` (the claude's descriptions of its
  models, in the language of the strings), `ContextSection`, `ContextWindow`, `PercentUsed`, `TokensOfWindow`,
  `PlanUsage`, `Refresh`, `AgentsChipTouch`, `AgentsChipTouchRunning`, `CopyAnswer` and `Copied`.
- The sample app takes `--touch`: a window the size of a phone's screen (412 × 892) with the view alone in it, in the
  touch layout; with `--screenshot` too, which makes the screenshots of the README's new section.

### Changed

- Nothing changes in the desktop layout. Its context ring still shows once more than half of the window is used and
  draws the extension's three steps; the touch layout's shows from the first token and draws the share used as it is
  (15 % used is 15 % of the circle).

### Fixed

- A host that takes the view's data context away (a page drawn by a template, its content set to null) no longer gets a
  binding warning on the panel of the reply box (`StackPanel.IsVisible`): the view hides that panel from code while it is
  read-only, and coming into the window or leaving it logs nothing.

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
