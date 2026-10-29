# Changelog

All notable changes to this project are written here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

## [0.6.0] - 2026-10-29

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
- The boxes of the tool rows, as the Claude Code extension 2.1.310 draws them. The text in an IN or OUT box is cut at
  60 px with a fade; one past 250 characters or 3 lines opens whole, in place, with a click, Enter or Space (at most
  100,000 characters), and cuts again the same way. An output is no longer wrapped or painted red on an error: it sits on
  the code background and scrolls sideways. The IN box of a Bash or PowerShell command has a copy button at its top
  right, shown while the pointer is over the box (always, as a target of 44 px, in the touch layout) and turned into a
  check for 2 seconds after the copy.
- A refused Edit, Write, Bash or PowerShell shows "Reason:" and what the person typed, on a line of its own (a refused
  Edit or Write also reads "Edit failed" or "Write failed"; any other refused tool shows what the claude answered). A
  refusal that comes as the claude's standard sentence followed by "The user provided the following reason for the
  rejection: " and a text (a session opened in the extension) has that text as its reason; the standard sentence alone
  has none.
- The Diff View. A click on a diff in the list, or Enter or Space on it, opens the change over the whole view, side by
  side and scrolling, under the file's whole path (or "Diff View" when the call names no file). Esc, its x or a click on
  its backdrop closes it, and the keyboard goes back to where it was. It is a layer of `CloseTopLayer()`, right under
  the image preview, and it opens in a read-only view too. Back in the window with the Diff View open, the keyboard goes
  to its x, unless a control of the host has it. A layer over the diff in the list darkens under the pointer and shows
  "Click to expand" while the pointer or the keyboard is on it (always shown in the touch layout, where the diff is at
  least 44 px tall). A diff goes side by side above 900 px of width and back to one column at 876 px or less.
- A Write whose `content` is not a text reads "Write succeeded" and shows the content as indented JSON.
- Glob and Grep rows read "Found 3 files" and "12 lines of output" (or "No files found" and "No matches found") and
  open the whole output in place from that line. Web Fetch reads "Fetched from <url>", and Web Search lists the domains
  allowed and blocked ("Allowed: a.com · Blocked: c.com").
- Skill rows read the skill's name in bold and "skill" after it, with no body; TaskOutput rows read `task: "<id>"` and
  have an OUT box without the terminal codes.
- MCP tool rows read "Server [tool]" in bold, with the server made readable (`claude_ai_Gmail` is "Claude AI Gmail"),
  and, under it, the first known field of the input (query, message, channel, repo, url, path, title, search or text).
  Claude in Chrome rows read "Claude in Chrome [action]" with a line per action ("Navigation completed", "Tab
  created"...), an OUT box without the "Tab Context:" section for the actions that read something, and, for the
  `computer` action, a "Screenshot" thumbnail (80 px tall, at most 160 px wide) when the result carried an image; a
  click on it opens the image preview. An error and the `javascript_tool` action show the generic body, with the input
  as JSON.
- Agent rows. An Agent (or Task) call reads "Agent:" and its description, and its box holds only the prompt (IN), never
  an output, not even when the call fails. A subagent in the background has under its row the line the extension draws:
  a dot of 8 px (green at work, blue waiting for a permission, red failed or stopped, gray otherwise), what it is doing
  ("Working in the background: Reading a.txt"), "Waiting for your permission: see the request below", "Failed: ...",
  "Finished" or "Stopped", what the agents it started are doing, and always "type /tasks to see its transcript" at the
  end (`AgentTasksHint`, for a host with no `/tasks`).
- `ToolCallItem.FullResultText`: the whole result of a call (at most `ToolPresentation.MaxKeptResultChars`, 500,000
  characters), without terminal codes for Bash, PowerShell and TaskOutput; `ResultText` and `HiddenResultLines` stay.
  `ToolCallItem.RejectionReason`: what the person typed when refusing the call. `ToolCallItem.IsOutputExpanded` and
  `IsInputExpanded`: whether the output or the command is open whole (in the item, because the list reuses its rows).
  `ToolCallItem.ResultImages` (read-only) and `ToolResultBlock.Images` (`init`): the images a result carried.
  `AnsiText.Strip(string)`, in `ClaudeVisualChat.Protocol`, takes the terminal codes out of a text.
- `AgentInfo.Prompt`, what the agent was asked, and `AgentInfo.IsWaitingForPermission`, true while it runs and a request
  of its own is in the queue (the request's `AgentId` is the agent's `TaskId`; without one, the request belongs to the
  agent whose step asked, to none when the main agent's own step asked, and only for a step the conversation has not
  seen to the agent that started running last). `AgentTaskStarted.Prompt` (`init`, read from `task_started.prompt`).
- `NoticeKind.ToolInterrupted`, added at the end of the enum, for "[Request interrupted by user for tool use]".
- New `ChatStrings` texts, in English and Portuguese: `ToolReason`, `ToolShowFullText`, `ToolCutText`, `ToolInterrupted`,
  `WriteSucceeded`, `ClickToExpand`, `DiffViewTitle`, `CopyCode`, `CopyCodeToClipboard`, `GlobNone`, `GlobOne`,
  `GlobMany`, `GrepNone`, `GrepOne`, `GrepMany`, `FetchedFrom`, `WebAllowed`, `WebBlocked`, `AgentWorkingInBackground`,
  `AgentWorkingInBackgroundOn`, `AgentWaitingForPermission`, `AgentWaitingAlongWithNested`, `AgentFailedWith`,
  `AgentNestedWaiting`, `AgentNestedWorking`, `AgentNestedFailed`, `AgentNestedStopped`, `AgentTasksHint`,
  `ChromeActionCompleted`, `ChromeScreenshot`, `ChromeNavigated`, `ChromeTabCreated`, `ChromeTabsRead`,
  `ChromeInputCompleted`, `ChromeWindowResized`, `ChromeSearchCompleted`, `ChromeGifCompleted`,
  `ChromeConsoleRetrieved`, `ChromeNetworkRetrieved`, `ChromeScriptExecuted`, `ChromePageRead`, `ChromeTextRetrieved`,
  `ChromeImageUploaded` and `ChromePlanUpdated`; and the names of tools `AgentToolTitle`, `WebFetchToolTitle`,
  `WebSearchToolTitle` and `ChromeToolTitle`, which are the same in both languages, like `AgentTokens`.
- New theme resources: `ClaudeChat.AgentWaiting`, `ClaudeChat.ToolContentFocusShadow` (also in "Dark Modern"),
  `ClaudeChat.DiffExpandHover`, `ClaudeChat.DiffExpandPillHoverBorder` and `ClaudeChat.DiffViewShadow`.
- Under a finished turn, a row like the extension's: "Copy response" (the turn's text, with a check for 2 s) and
  "Bookmark response" / "Remove bookmark" (`AssistantTextItem.IsBookmarked`), shown while the pointer or the keyboard is
  on the end of the turn; a bookmarked reply keeps its icon in sight. In the touch layout the bookmark sits beside the
  turn's copy button, always shown, as a target of 44 px, also in a read-only view.
  `IsBookmarked` can be set by code (a host restoring bookmarks) on a reply of `Items`: the reply joins or leaves
  `ChatConversation.Bookmarks` as a click on the button does.
- `ChatConversation.FeedbackRequested` (`FeedbackRequestedEventArgs`: `Item`, `Positive`, `Details`). The row has "Good
  response" and "Bad response" only while the host listens to it, only under a reply that has its line's `uuid`, and
  never in a read-only view. The thumb down opens "What went wrong?" (Ctrl+Enter sends, Esc or "Skip" closes, at most
  4,000 characters), a layer of `CloseTopLayer()` right under the Diff View. `Positive` is null when the person takes
  the rating back. In the touch layout the thumbs sit beside the turn's copy button, at 44 px.
- `ChatConversation.LinkRequested` (`LinkRequestedEventArgs.Url`): the host opens the view's web links, the component
  never does. "Learn how we use your feedback" shows only while the host listens to it. As with the thumbs, a part that
  needs the host shows only while the host subscribes to its event, and the tests prove that nothing shows without a
  subscriber.
- `AssistantTextItem.Uuid` and `AssistantBlock.Uuid` (`init`), from the `uuid` of the assistant line.
- "Copy code" on the code blocks of a reply (fenced and indented): at the top right, shown while the pointer is over the
  block (always, as a target of 44 px, in the touch layout); its icon turns into a check for 2 s.
- `/copy` and `/copy N` copy Claude's last reply (or the Nth-latest of the last 20; a turn's whole text, as the copy
  button takes it) to the clipboard and say how much was copied ("Copied to clipboard (N characters, M lines)"). Typed
  while Claude works, the copy waits for the turn to end; Stop, or the work ending without a turn's end (a message taken
  back with `UndoSubmit`), drops it, with the line "Copy cancelled". `copy` joins `ChatConversation.LocalCommands`: it
  raises `CommandInvoked` too, which the host may ignore.
- `MetaLineItem`: a line the conversation says about its own commands and events (what `/copy` did, a hook that blocked
  the prompt, a background agent that finished), in italics and the secondary color, selectable, outside the timeline.
- A prompt taller than 60 px folds behind "Show more" (inside the bubble, at the bottom right: under the pointer or the
  keyboard on the desktop, always and at 44 px in the touch layout) and opens with "Show less" under it; the height
  animates over 0.3 s. The state is in the item: `UserMessageItem.IsTextExpanded`.
- The prompt of the turn on screen stays pinned at the top of the list, in a band of the side bar's color, until the
  next prompt pushes it out. It spans the list's width and is at most 40 % of the view tall, with a scroll of its own. A
  click on it with no text selected brings the start of its turn to the top after 300 ms; a press that moves more than 4
  px (10 px in the touch layout) is a drag, not a click. The wheel over it, and a finger in the touch layout, scroll the
  list. Prompts without text pin nothing.
- `ClaudeChatView.ScrollToEndOnSend` (true by default; the extension's `scrollToBottomOnSend`): sending a message brings
  the list back to its end and follows it again, even after scrolling up.
- `ClaudeChatView.ShowTimestamps` (true by default, inherited; the extension's `showMessageTimestamps`): false hides the
  messages' times and the date lines. A message's time has the whole date and time in its tooltip ("2026-10-25 15:45:12
  GMT-3").
- A date line ("Sat, Oct 25", with ", 2025" outside the current year) between wavy rules, before the first message and
  wherever the day changes, in both layouts. It belongs to the first item of each day, so it moves when a message of
  that day comes before it or when that item goes.
- A fade of 150 px dims the end of the list into the background over the reply box (not in a read-only view).
- A message sent while Claude works waits at the end of the list, under the working line, in the usual bubble, with no
  time and no actions, until the claude takes it: `ChatConversation.QueuedMessages`. It moves into `Items` when the
  claude says so for its uuid (`command_lifecycle`), when it repeats the message, or else at the first request to the
  API after it was sent (`system/status` `requesting`), or at the turn's end. A screen reader hears "Message queued.".
- `MessageSubmittedEventArgs.Uuid` and the overload `StdinMessages.UserMessage(MessageSubmittedEventArgs)`, which writes
  the text, the images, the message's `uuid` and a marker line for each file that landed. A host that uses it should use
  it for every message: once the claude has told of one message by its uuid, a request to the API no longer releases the
  held ones, so a message written without its uuid stays held until the turn's end. The event `CommandLifecycle` reads
  `command_lifecycle`.
- "Ran at once, while Claude was working." under a command the claude ran at once (`ran_ahead`); only with a host that
  sends the uuid.
- A live token estimate on the thinking line ("Thinking... · 1.2k tokens"), counting up while Claude thinks:
  `ThinkingItem.EstimatedTokens`, the event `ThinkingTokens` and `BlockDelta.EstimatedTokens` (`init`).
- `CompactionItem`: a card "Compacted chat · manual · 36k tokens freed" that opens to the summary the claude wrote.
  `SystemNotice.Trigger`, `PreTokens` and `PostTokens`, and `UserBlock.IsSynthetic` (all `init`).
- The working line at the end of the list: a star that turns and a verb that changes (after 2 s, 3 s, then every 5 s),
  written from left to right and followed by three dots, "Compacting..." while the claude makes room and "Running
  start-up hooks..." while a `SessionStart` or `Setup` hook runs; it hides while a permission request waits.
  `ClaudeChatView.SpinnerVerbs` and `SpinnerVerbsReplace` (the extension's `claudeCode.spinnerVerbs`: verbs added to the
  line's own, or used alone; the host reads its setting, the view saves nothing) and `ReduceMotion` (the star still and
  the verbs swapped whole; Avalonia 11.0 cannot ask the system, so the host that knows sets it), with their `*Property`
  fields. `ChatConversation.Status` (what `system/status` says) and `IsRunningStartupHooks`; the events `HookStarted`
  and `HookFinished`.
- `DividerItem`: what a command printed in one line is a divider, its text between the date line's wavy rules; "Set
  model to X" reads "Switched to X". A label cut short, or any label in a panel of 330 px or less, shows whole with a
  tap.
- The event `Informational` (`system/informational`, with `Level` and `Text`): a warning or an error, such as a hook
  that blocked the prompt, is a meta line; plain information is not shown, as in the extension.
- `ExternalMessageItem`: a message from another agent, another session or a channel, the notice of a task and the
  wrapped notices of the claude (a teammate's message, terminal output, a tick, MCP updates) are a line in italics,
  "Message from @maria: the first words…", that opens to the sender, the extra attributes and the body; a task's notice
  opens to a list of its parts. When its line has an `origin`, a message is a card only if that `origin` is the sender
  the message claims (a channel for a channel, a peer with a task for an agent, a peer without one for another session);
  any other `origin`, such as `human`, leaves it the user's text. A message whose line has no `origin` is a card that
  reads "Sender not recorded", and a meta line with none (the agent message the CLI writes) stays out, as before. The
  claude writes `origin` live only on the lines it replays (session files write it), so through a relay that does not
  replay them, such as the Kairos one, these cards come only from a history. `MessageOrigin` and `UserBlock.Origin`
  (`init`). A background agent that ends live adds a meta line with the first line of its summary.
- New `ChatStrings` texts, in English and Portuguese: `CopyResponse`, `CopyResponseToClipboard`, `BookmarkResponse`,
  `RemoveBookmark`, `GoodResponse`, `BadResponse`, `FeedbackLearnHow`, `FeedbackThanks`, `FeedbackLearnHowShort`,
  `FeedbackTitle`, `FeedbackPlaceholder`, `FeedbackSkip`, `FeedbackSend`; `CommandCopyDescription`, `CopyDone`,
  `CopyNothing`, `CopyOnly`, `CopyOnlyOne` (its singular, "Só há 1 resposta..." in Portuguese), `CopyUsage`,
  `CopyFailed`, `CopyWaiting`, `CopyCancelled`; `ShowMore`, `ShowLess`; `MonthNames`, `DateFormat`,
  `DateWithYearFormat`; `YouQueued`, `MessageQueued`, `RanAtOnce`; `ThinkingNow`, `ThinkingTokensEstimate`,
  `CompactedChat`, `CompactedChatShort`, `CompactionNoSummary`; `WorkingVerbs`, `CompactingLine`,
  `CompactingAnnouncement`, `StartupHooksLine`, `WorkingAnnouncement`; `SwitchedTo`, `SwitchedModel`; `MessageFromPeer`,
  `MessageFromChannel`, `MessageFromSender`, `SenderNotRecorded`, `TaggedNothing` and the names of a task's parts and of
  the wrapped notices (`TaggedTaskId` to `TaggedCommandCaveat`; `TaggedTokens` is the same in both languages).
- New theme resources: `ClaudeChat.DisabledForeground` (the date line; also in "Dark Modern"),
  `ClaudeChat.WorkingGlyphFontSize`, `ClaudeChat.WorkingRowHeight`, `ClaudeChat.FeedbackFieldHeight`, the icons
  `ClaudeChat.Icon.Bookmark`, `ClaudeChat.Icon.ThumbUp` and `ClaudeChat.Icon.ThumbDown`, and the template
  `ClaudeChat.QueuedMessageTemplate`.
- Prompts of the conversation come back in the reply box with the up and down arrows, as in the extension: up with the
  caret at the start of the text (an empty box counts) brings the previous prompt and keeps what was being written, down
  with the caret at the end goes to the next one and, past the newest, gives the draft back. They are this
  conversation's prompts, the queued ones included, without the empty ones, the synthetic lines and the subagents',
  trimmed, with the repeats; the list is the one at the first up. A recalled `/command` does not open the palette
  until it is edited. Clearing the box ends the walk. It works while Claude is busy, and not with the palette or the
  model menu open.
- Reply box shortcuts: Ctrl+J breaks the line; Shift+Tab walks the permission modes `default`, `acceptEdits`, `plan`,
  `auto` and back to `default`, only among `ClaudeChatView.AvailableModes`, raising `PermissionModeRequested`;
  Ctrl+Alt+F toggles the Focus view (below), which the view hears wherever the keyboard is inside it. A read-only view
  acts on none of them, and Ctrl+J and Shift+Tab do nothing while the palette or the model menu is open.
- `ClaudeChatView.UseCtrlEnterToSend` (false by default; the extension's `useCtrlEnterToSend`): true makes only Ctrl or
  Cmd+Enter send and Enter break the line. The touch layout keeps its own rule (Enter breaks the line, Ctrl+Enter
  sends) and now takes Cmd+Enter too.
- A text pasted into the reply box, and a message when it is sent, lose the characters that cannot be seen (zero-width
  characters, the tags block, the direction controls, the variation selectors, the control codes), and their line breaks
  become line feeds. A notice over the box says how many went ("Removed 1 invisible character from the pasted text"). On
  send the box is filled again with the clean text, the notice asks to look at it ("... review and press Enter to send",
  Ctrl/Cmd+Enter with `UseCtrlEnterToSend`) and nothing is sent; the next send goes. When nothing is left of the
  message, the notice says "nothing left to send" and the box stays empty. The notice ends with its ×, with any edit of
  the text or with another conversation, and a permission request hides it, as it hides the other banners. What the text
  needs stays: a joiner inside a word of a script that joins letters or between emoji, a space of zero width between
  letters of Thai, Lao, Khmer or Myanmar, the selectors after an emoji (after a digit, `#` or `*` only with the keycap
  U+20E3), one direction mark for each sequence on a line with right-to-left letters, and the flags of England, Scotland
  and Wales. A message of more than 50,000 characters is cut there and ends with "[Message truncated - exceeded 50,000
  character limit]".
- `MessageSubmittedEventArgs.InlinePastes` (`init`, never null): the long texts pasted into the box (more than 800
  characters or more than two line breaks, and 20 characters or more once trimmed) that are still in the message, so
  the host can tell them from what was typed. `StdinMessages.UserMessage(e)` writes them as `inline_pastes`, after the
  `uuid`, only when there are some. The box remembers the last 100 for each conversation, and a prompt recalled with up
  goes with them again.
- The history takes the `<pasted_content id="abcd">` wrapper that the claude puts around a long paste off a user
  message, in a session file and live, and shows the pasted text where the wrapper was.
- The command palette opened by the / button is in sections with the rows the component runs: Context (attach a file),
  Model (switch model and effort, when the host gives models; account and usage), Slash Commands and, where the host
  opens links, Support (the help docs, the extension's VS Code page). Customize and Settings are in the order of the
  sections but have no rows yet, and a section with no rows is not shown. A row can be a switch, and one that keeps the
  palette open redraws in place. The slash commands are in alphabetical order; with the / button they show once
  something is typed in the field, and a "/" typed in the empty box opens the palette with every group, filtered by what
  follows the "/". Typing `/model` and pressing Enter marks "Switch model..." before a `/model` command, when the host
  gives models (`ChatConversation.AvailableModels` is not empty), because that is the order of the sections in the
  extension 2.1.310.
- The hint of a command's arguments shows as ghost text after `/name ` in the reply box (`/copy` shows `[N]`).
- Dragging files over the chat shows "Drop to attach as context" over the whole view, which takes the drop wherever it
  falls and not only on the reply box; a file of a type that cannot be attached names the types ("Unsupported file
  type(s): .zip"). A read-only view shows no overlay and takes no drop.
- New `ChatStrings` texts, in English and Portuguese: `MessageInput`, `InvisibleRemovedOne`, `InvisibleRemoved`,
  `InvisibleReview`, `InvisibleReviewCtrl`, `InvisibleEmpty`, `InvisiblePasted`, `DismissNotice`; `PaletteContext`,
  `PaletteCustomize`, `PaletteSettings`, `PaletteSupport`, `PaletteAttachFile`, `PaletteSwitchModel`,
  `PaletteAccountUsage`, `PaletteHelpDocs`; `DropToAttach`, `UnsupportedFileTypes`.
- New theme resources: `ClaudeChat.BannerWarningForeground` and `ClaudeChat.BannerWarningBackground` (the notice over
  the reply box, in both themes), `ClaudeChat.DropFill`, `ClaudeChat.DropBorder` and `ClaudeChat.DropLabelShadow`.
- The Focus view, as the extension's: `ClaudeChatView.FocusView` (false by default; a host that wants it on by default
  sets true) folds each round's run of tool calls and thoughts into one row that opens to the steps and ends in
  "Collapse". Ctrl+Alt+F and the command palette's "Focus view" switch (tooltip "Show only your prompts and Claude's
  responses") toggle it; a read-only view does not hear the shortcut. The fold reads "3 tool calls · 1 failed",
  "Thought for 4s" for thoughts only and, while the session works, "Running Bash…", "Thinking…", "Waiting for
  permission…" or "Waiting for your answer…" with the count after it. A refused or interrupted call counts as failed, and
  so does one left without a result once the session is idle, so a history read back never blinks. Prompts and Claude's
  text stay in sight; the latest `TodoWrite` list shows right after the fold of its run; a step that waits for
  permission keeps its fold open, even when the request comes after the turn ended (a subagent in the background), and
  closing it does nothing until the request is answered. The live subagents of a fold show as rows under it, open or
  closed, in the order of the list: up to 4, or 3 and "+N more agents" with the others' numbers added, each with its
  description, what it does, and "1.2k tokens · 3 tools · 1m 5s", the time running (a clock that ticks only while the
  view is attached). The folded list follows the conversation row by row, with no reset, so a long conversation stays
  fast. The turns are still counted in `ChatConversation.Items` (`/copy N` copies the same replies).
- New `ChatStrings` texts, in English and Portuguese: `PaletteFocusView`, `PaletteFocusViewTip`, `FocusToolCalls`,
  `FocusToolCallsOne`, `FocusFailed`, `FocusHiddenSteps`, `FocusHiddenStepsOne`, `FocusThinkingNow`, `FocusRunning`,
  `FocusWaitingForPermission`, `FocusWaitingForAnswer`, `FocusCollapse`, `FocusMoreAgents`, `FocusAgentTools`,
  `FocusAgentToolsOne` and `FocusAgentsCombined`.
- The Agent map and the agents chip, as the extension's. The chip's dot turns blue while an agent waits for your
  permission, green while agents work, red only while nothing works and an agent failed, and grey otherwise, with a
  tooltip for each state. `/tasks` and `/bashes` are local commands: they open the Agent map (and still raise
  `CommandInvoked`, which needs nothing from the host). New `ChatConversation.Title` (`string?`, set by the host,
  observable, kept by `Clear`): the map's main card says it, or "This session"; a click on the card opens it to
  "Working" or "Idle" and how many agents are in each state.
- "Context usage" and "Account & Usage": `/context` and `/usage` open modals like the extension's. "Context usage" has
  the model, the bar and its table, and the five biggest memory files and custom agents, by tokens. "Account & Usage"
  has the account, a bar for each limit window (the session, the week, the week's Sonnet and each model's week),
  "Resets in 45m" under each, the session's cost and times, and "Manage usage on claude.ai" where the host listens to
  `LinkRequested`. The per-model and Sonnet bars show only when the host passes the reply to `get_usage` to the
  conversation (the component never sends it, as it never sends `get_context_usage`); a rate limit event alone gives
  the session and week bars. The touch layout's usage sheet is unchanged.
- New public API for them: `ChatConversation.Account` (`AccountInfo`: `AuthMethod`, `Email`, `Organization`, `Plan`),
  `SessionCostUsd`, `SessionDurationMs` (the turns' wall time) and `SessionApiDurationMs`; `ContextUsage.Model`,
  `MemoryFiles` (`ContextFile`) and `Agents` (`ContextAgent`); `UsageWindow.Model`; and, in `Protocol`,
  `AccountReported`, `ControlAcknowledged.Error` (the text of a failed control reply), `TurnCompleted.ApiDurationMs`
  and `TurnCompleted.ErrorMessage`. `AccountInfo.AuthMethod` is a code (`claudeai`, `api-key`, `bedrock`, `vertex`,
  `foundry`, `gateway`, `third-party`); the list may grow, and a code a host does not know is none. `Plan` is the
  plan's name, ready to show ("Max"), and is not translated.
- The bookmarks panel: command menu > Bookmarks, `ClaudeChatView.ShowBookmarks()` and `HideBookmarks()`, and
  `ChatConversation.Bookmarks` now public (observable, in the order marked, emptied with the items). It shows your
  bookmarked replies beside the conversation, or below it in a narrow view and in the touch layout, with a bar to
  resize it (a double click restores the size), the age of each reply (rewritten every minute while it shows), "Show in
  conversation" and "Remove bookmark". Esc closes it only when the keyboard is in it. It is a side panel: the modals,
  the Diff View, the plan's document and the image preview cover it.
- Insight blocks (a star and "Insight" header, and a rule of 20 or more box-drawing dashes, in inline code) show on
  their own lines in the text color with no background; the Artifact tool row reads "Created", "Opened" or "Published"
  with an "Open artifact" link that goes through the host's `LinkRequested` (without a subscriber only the word
  shows); the notebook cell edit row reads "Edit Notebook Cell" with its state ("Pending", "Success", "Failed") and
  the whole new source.
- Context lines under a prompt: the output of a terminal, a browser tab, the browser's instructions and the code
  selected in the editor that the IDE sent along with it are lines under the message, open to their content and hidden
  in the Focus view, instead of text in the message. `UserMessageItem.Context` (`IReadOnlyList<MessageContext>`,
  `init`) and the record `MessageContext(Kind, Label, Content)`; the kinds are `terminal`, `browser-tab`,
  `browser-instructions` and `selection`. A message read back from a session file has them.
- For screen readers: messages are named "You", "You, queued", "Claude" and by their tool, a hidden "You: ..." text
  comes before each prompt, and a polite live region announces working, ready, compacting, permission requests,
  questions, plan reviews, errors and each finished reply. It holds up to 5 sentences, says one every 400 ms, waits
  while a modal is open, and drops the oldest when the queue is full. "Error: {0}" carries the claude's own message:
  `TurnCompleted.ErrorMessage`, `SystemNotice.ErrorMessage` and the new `NoticeItem.Message` (`init`).
- The "Auto mode is enabled" notice on an empty conversation when the mode is `auto`, closed by its x or by
  `ChatConversation.AutoModeNoticeSeen = true` (the host keeps the flag between sessions; "Learn more" shows only
  where the host listens to `LinkRequested`). The "Switched to {model}" band, with "Why?" and "Hide", when the claude's
  safeguards switch models: the `Protocol.ModelRefusalFallback` event, `ChatConversation.LastModelFallback` and
  `DismissModelFallback()`. A reopened history never shows the band.
- New `ChatStrings` texts, in English and Portuguese: `CommandTasksDescription`, `AgentsChip*Tip`, `AgentThisSession`,
  `AgentMainWorking`, `AgentMainIdle`, `AgentCount*`; `ContextHeader`, `ContextColumn*`, `ContextShareTiny`,
  `ContextMemoryFiles`, `ContextCustomAgents`, `ContextLoading`, `ContextFailed`, `AccountUsageTitle`,
  `AccountSection`, `AccountUsageSection`, `AccountAuthMethod`, `AccountEmail`, `AccountOrganization`, `AccountPlan`,
  `Auth*`, `UsageBar*`, `UsageResetsIn`, `UsageResetsSoon`, `UsageTotal*`, `UsageNoPlanLimits`, `UsageManage`,
  `UsageLoading`, `UsageFailed`, `UsageRetry`; `PaletteBookmarks`, `PaletteBookmarksTip`, `BookmarksTitle`,
  `BookmarksEmpty`, `BookmarksSelectHint`, `BookmarkShowInConversation`, `BookmarkNotShown`, `BookmarkResize`,
  `AgoJustNow`, `AgoMinutes`, `AgoHours`, `AgoDays`; `ArtifactToolTitle`, `ArtifactCreated`, `ArtifactOpened`,
  `ArtifactPublished`, `ArtifactOpenLink`, `NotebookEditTitle`, `NotebookPending`, `NotebookSuccess`, `NotebookFailed`,
  `NotebookNoContent`; `ContextTerminal`, `ContextBrowserTab`, `ContextBrowserTabBare`, `ContextBrowserInstructions`,
  `ContextSelection`, `ContextNone`, `ContextCaptionTerminal`, `ContextCaptionBrowserTab`,
  `ContextCaptionBrowserInstructions`, `ContextCaptionSelection`; `You`, `YouSaid`, `Claude`, `ClaudeThinking`,
  `ClaudeTool`, `AnnounceWorking`, `AnnounceReady`, `AnnounceCompacting`, `AnnounceStartupHooks`, `AnnouncePermission`,
  `AnnounceQuestion`, `AnnouncePlan`, `AnnounceError`, `AnnounceReply`, `AnnounceCodeBlock`, `AnnounceCodeBlockOne`;
  `AutoModeEnabled`, `AutoModeNoticeBody`, `LearnMore`, `ModelFallbackWhy`, `ModelFallbackHide` and
  `ModelFallbackWhyBody`.
- New theme resources: `ClaudeChat.ContextColor7` and `ContextColor8` (the context bar has eight colors, in the
  extension's order), `ClaudeChat.AutoNoticeShadow` and `ClaudeChat.Icon.Bolt`. The chip's blue dot is the existing
  `ClaudeChat.AgentWaiting`.
- **The sandbox's network access request** (`SandboxNetworkAccess`, an ordinary permission request with the host in its
  input). The tool line reads "Network Access" in bold and the host in the editor's font; the card asks "Allow network
  connection to this host?" with the host in a box under it, and option 2 names the WebFetch `domain:<host>` rule the
  claude suggests, with the label every request has. Without a host the row reads only the name and the card has no box.
  New texts: `NetworkAccessToolTitle` and `PermissionNetworkAccess`.
- **The fast mode bolt** on the reply box: a 12 px bolt on the box's top border, 12 px from the right, shown only while
  the box has the keyboard and `ChatConversation.FastModeState` is `on` (amber, `ClaudeChat.FastModeBolt`) or `cooldown`
  (gray, `ClaudeChat.DisabledForeground`). Its tooltip is "Fast mode enabled" or "Fast mode cooling down", then
  " · Effort: High" when the effort is known; the touch layout shows the bolt with no tooltip. `FastModeState` is the last
  `fast_mode_state` seen on `system/init` (`SessionStarted.FastModeState`) or on a `result`
  (`TurnCompleted.FastModeState`): a line without the field leaves it as it was, a value that is not `on`, `cooldown`
  or `off` is kept and shows no bolt, and a history read back never sets it. New texts: `FastModeEnabled` and
  `FastModeCoolingDown`. The "Ultracode" suffix of the model chip is not here: it comes only from `get_settings`.

### Breaking

Nothing public was removed or renamed. These changes of 0.6.0 may still need a change in a host that reads `Items`,
calls `StdinMessages.UserMessage`, styles the view or compares its texts:

- `ChatConversation.Submit` while the conversation is busy (`IsBusy`) no longer adds the message to `Items`: it goes to
  `QueuedMessages` until the claude takes it. A host that looked for the message in `Items` right after `Submit` must
  look in the queue.
- `StdinMessages.UserMessage(null)` with a literal `null` no longer compiles: the call is ambiguous between the two
  overloads. Write `(string)null`.
- `typeof(StdinMessages).GetMethod("UserMessage")`, by reflection without the parameter types, throws
  `AmbiguousMatchException`. Pass the types.
- A compaction (`compact_boundary`) is a `CompactionItem`, no longer a `NoticeItem` of `NoticeKind.Compacted`: a host
  that looked for those notices must look for the card. `NoticeKind.Compacted` stays, not used.
- What a command printed in one line (the `Compacted` of `/compact`, say) is a `DividerItem`, no longer a
  `CommandOutputItem`; an output of several lines is still a `CommandOutputItem`.
- A message from an agent, another session or a channel, and a wrapped notice, is an `ExternalMessageItem`, no longer a
  `UserMessageItem` with the raw markup; the notice of a task in a session file, which did not show, is one too.
  `Items.OfType<UserMessageItem>()` no longer sees these messages. A host with templates of its own needs one for
  `ExternalMessageItem`, and for `MetaLineItem`, `CompactionItem` and `DividerItem`.
- Style: the working line at the end of the list has the style key `Grid`, and Avalonia's type selector matches the
  exact style key, so a selector `Panel.ClaudeChatWorking` no longer reaches it (a test confirms it; use
  `Grid.ClaudeChatWorking`). `Ellipse.ClaudeChatWorkingDot` and the named parts `WorkingDot` and `WorkingText` are gone;
  in their place are `TextBlock.ClaudeChatWorkingGlyph` and `TextBlock.ClaudeChatWorkingText`.
- Text: `ChatStrings.Interrupted` reads "Interrupted", where it read "Interrupted by the user" (in Portuguese
  "Interrompido", where it read "Interrompido por você"). A host that compared the text sees the new one.
- Text: on the desktop layout the reply box reads "Ask Claude to edit..." when idle (`ChatStrings.InputPlaceholder`;
  in Portuguese "Peça ao Claude para editar..."), where it read "Reply to Claude..." ("Responder ao Claude..."), as
  the extension's does. The touch layout keeps "Type / for commands" (`InputPlaceholderTouch`). A host that compared
  the text sees the new one.
- Text: a file of a type that cannot be attached, dropped, picked with the **+** or pasted, says "Unsupported file
  type(s): {0}" (`ChatStrings.UnsupportedFileTypes`, with the types), where it said "Only PDF, text and CSV files".
  `ChatStrings.FileNotSupported` stays for a file of an accepted type that cannot be read.
- Text: the Agent map's main card reads "This session" (`ChatStrings.AgentThisSession`), where it read "Main agent"
  (`AgentMain` stays, not shown). A limit's renewal in the usage panel reads "Resets in 45m" (`UsageResetsIn`), where it
  read "Resets 3:30 PM". A host that compared the texts sees the new ones.
- `ChatConversation.UsageLimits` merges what the claude reports instead of replacing it: a `rate_limit_event` replaces
  only the windows it names and keeps the others, and the reply to `get_usage`, which is a whole snapshot, replaces
  every window. A host that read `UsageLimits` after each event used to see only the last report's windows.

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
- The plan's document opens by itself only when the request carries the plan's text, and the one that opened by itself
  closes when the view turns read-only (one opened from the plan's row in the list stays). A plan that was cancelled or got no answer leaves the document open, as only an approval closes it.
- `ToolPresentation.Summary("ExitPlanMode", ...)` returns the plan file's name, or "", where it returned the first
  string of the input (the plan's text). An `ExitPlanMode` call that comes without the plan (`input: {}`) takes the
  `input` of its permission request.
- `ChatStrings.PermissionRunCommand`, `PermissionEditFile` and `PermissionUseTool` are not used on the desktop since
  0.6.0 (the touch layout still uses them). Nothing public was removed or renamed.
- A tool row's text is cut at 60 px, where it was cut at 12 lines or 4,000 characters with a line "+N lines"; there is no
  "+N lines" any more. A Bash or PowerShell row has no line count either (the extension has none). In the touch layout
  the cut text is a target of 44 px.
- A call the person refused shows the red dot of a failed one and no "Rejected" label, in both layouts.
  `ChatStrings.ToolRejected` and the `ClaudeChat.DotRejected` brush stay, not used.
- A tool call appears in `ChatConversation.Items` when its input is complete (the assistant's line, or the end of its
  block), no longer as an empty row when its block starts; "Working..." covers the wait. A host that read the item
  earlier now sees it a little later. The calls of `ToolSearch`, and their results, no longer appear in the conversation.
- The interruption lines read "Interrupted" and "Tool interrupted" (`ChatStrings.Interrupted` was "Interrupted by the
  user"; the Portuguese text changed the same way). Only the claude's two exact texts, "[Request interrupted by user]"
  and "[Request interrupted by user for tool use]", make them (`NoticeKind.Interrupted` and the new
  `NoticeKind.ToolInterrupted`); any other text that begins "[Request interrupted" is the user's message, as written.
  Before, any text with that beginning was taken for an interruption.
- An Agent (or Task) row has no OUT line, with a result or an error, and the steps of a subagent that runs in the
  background no longer appear in the conversation, only in the Agent map (they stay in the call's `Children`). The steps
  of a subagent in the foreground are still nested behind the arrow of its row, where the extension shows them as plain
  rows.
- `AgentTaskStarted` compares its `Prompt` in the equality of the record: a host that compares these events by value sees
  the difference. The positional constructor did not change.
- `ToolPresentation.Summary` (the `Summary` of a call and of a `PendingPermission`): for an MCP tool it is the first
  known field of the input (query, message, channel, repo, url, path, title, search or text; a channel reads "#name", a
  url its host, the rest at most 40 characters), and "" when none is there, where it was the first text of the input; for
  a Claude in Chrome tool, the line of its action. Glob reads `pattern: "..."` and Grep `"..." (in src, glob: *.cs,
  type: cs)`, Skill drops the leading "/", and TaskOutput reads `task: "<id>"`.
- In the touch layout, the request of Glob and Grep shows that new summary; that of an MCP tool still shows the first
  text of its input, whole, as in 0.5.0.
- The Edit and Write boxes are left out when the view is 500 px wide or less (the line under the name and the reason
  stay), like the extension; this is the desktop layout's only, and the touch layout keeps them. A diff in the list is
  its lines plus 20 px tall, at most 200 px, and fades only when its lines are taller than 200 px (it used to fade past
  180).
- Where the group differs from the extension 2.1.310 on purpose: a Write is told to have succeeded ("Write succeeded")
  only when its input has a `content` that is not a text, and a Write with no `content` has no such line; a request
  that comes without `agent_id` belongs to the agent whose step asked (to none when the main agent's own step asked),
  and only for a step the conversation has not seen to the agent that started running last, so such a request from an
  unseen step may mark the wrong agent; the diff and the commands have no syntax highlighting, as in the extension
  2.1.310; and the fields of the Claude in Chrome input that the rows read (`query`, `urlPattern`, `pattern`,
  `onlyErrors`, `text`, `scroll_direction`, `ref`, `coordinate`, `duration`) come from the schema of the tool, not from a
  recording of a real session, because none was made: a real session may show a summary or a thumbnail less. The Kairos
  relay sends only text, so there a `computer` action reads "Action completed" with no thumbnail. The Claude in Chrome
  rows have no "[Show Tab]" link.
- A code block still open while a reply arrives is drawn as code up to its end, where it was plain text until its
  closing fence came.
- "At the end" of the list means within 50 px, where it meant within 4. Coming back down that close by the user's own
  move makes the list follow its end again from there, without a jump, even over rows of unlike heights whose estimate
  the move changes; the next rows take it to the end. Reaching the very end the list estimated before (the End key, the
  scroll bar at its bottom) goes on to the real end. Any scroll up from the very end stops following. While the list
  does not follow, the first row on screen stays in place when rows change above it, and the move that keeps it there is
  not taken for the user scrolling down.
- The thinking line reads "Thinking..." (with dots) while Claude thinks; "Thinking" stays for a finished thought with no
  known duration.
- The working line has the star and the verbs where it had a dot and "Working...": `ChatStrings.Working` is no longer
  drawn (it stays, not used).
- A palette that lists `LocalCommands` shows `/copy` after `/chrome`.
- The touch layout has the date lines too, where 0.5.0 showed no time at all, and each divider there is 44 px tall.
- Where the group differs from the extension 2.1.310 or from the first design notes on purpose: the code blocks have no
  syntax highlighting, as in the extension 2.1.310 (so there is no tokenizer, and code is in the text color); the old
  tool rows are not pruned, as the extension 2.1.310 does not prune them; while Claude works the reply box reads "Queue
  another message…", as the extension's, which also shows a suggested prompt or its Ctrl+Esc hints (shortcuts of VS
  Code) in that place, and those are left out; the row of actions sits at the end of the turn, under its last reply, and
  copies the whole turn, where the extension has one under each assistant message, and it shows with the pointer over
  the end of the turn, without the extension's fade of 0.18 s; the scroll on send jumps to the end at once, with no
  glide of 2 s; Ctrl+wheel scrolls the list (it zooms in the extension), so it stops following; a still finger on the
  list does not stop following; the time format is `ChatStrings.TimeFormat`, not the claude's; the tooltips are plain
  text; the working verbs are the component's own, in English and Portuguese; and the messages of agents, sessions and
  channels are recognized by the component's own reading of their format, so a message of a shape the claude does not
  write may be read a little differently from the extension.
- The reply box on the desktop has the extension's line height (19.5 px, `ClaudeChat.LineHeight`), where it had the
  font's own, and its placeholder is one line with an ellipsis. The field's name for a screen reader is "Message
  input" (`ChatStrings.MessageInput`).
- A text pasted into the reply box is read from the clipboard and put in by the component on every platform (it used to
  be the text box's own paste), which is what lets it be cleaned; it is still one step of undo, replaces the
  selection and leaves the caret after it.
- The reply box no longer takes the drop by itself: the view takes it wherever it falls, so a file dropped on the list
  is attached once, as one dropped on the box. The palette's "View help docs" opens
  `https://code.claude.com/docs/en/vs-code`.
- Where this group differs from the extension 2.1.310 on purpose: a paste cut by the 50,000 limit is not sent as an
  `inline_pastes` entry (only the pastes that fit whole); the direction of the text follows the control
  (`FlowDirection`), not each paragraph, so a mixed text has its runs ordered but its base direction is the control's;
  the "Effort" row of the palette steps through the levels where the extension has a slider; the palette has no
  Customize or Settings rows yet (nor Rewind, Bookmarks, Clear conversation, Export or the footer); and a file dropped
  while a permission request waits is attached, where the extension ignores the drop.
- Where the Focus view differs from the extension 2.1.310 on purpose: there is no "Run in background" action, which is
  the VS Code host's; there is no tail of live agents whose call is in no fold (an agent started by a subagent, or a
  task with no tool use id): its parent's row still shows it works, and the Agent map lists it; and in a history read
  back, a call without a result counts as failed in the fold, while its own row shows the gray dot.
- The `/context` and `/usage` panels are modals: they take the keyboard while open and give it back to the one that had
  it, and a permission request that arrives meanwhile waits until the modal closes.
- The layers (the modals, the Agent map, the comment box, the sheets, the Diff View, the preview) cover the bookmarks
  panel, and the plan's comment box stays inside the chat area, so it does not cover the panel.
- Where group 6 differs from the extension 2.1.310 on purpose:
  - Left out: LaTeX (the extension draws it with KaTeX, and this release takes no new dependency); the "Session paused"
    card, which needs the `request_user_dialog` that the Kairos machine refuses; the suggestion to make auto mode the
    default, which needs `auto_default_nudge`; the "Why?" text by category, where there is one generic text; and the
    agent card's prompt, tool calls, "Open transcript" and "Stop agent".
  - The Agent map has no tail row for a live agent outside any fold (an agent started by a subagent, or a task with no
    tool use id): the map lists it, as before.
  - The usage modal keeps "Resets in ..."; it shows the account section only with `Account`, the Sonnet bar whenever
    there is data (no plan gate), and "Manage usage on claude.ai" whenever the host listens to `LinkRequested`; its
    bar uses `maxTokens`, not `rawMaxTokens`. A host that does not pass the reply to `get_usage` has no Sonnet or
    per-model bar.
  - The context lines keep the labels in the language the item was built in (they do not follow a later change of
    `Strings`), the chips of the open file and of the selection are not shown, and a browser tab shows its host and
    path without the port, or only "Browser tab" for an empty address or one that is not http or https.
  - The live event of Win32 screen readers in Avalonia 11.0.10 was not confirmed with a real screen reader.
  - The "Switched to" band is not shown in a reopened history, and only one banner shows at a time: it gives way to
    the browser notice and to a Remote Control error.

- The sample app has two made-up scenarios for what no recording shows: `tour` (a plan to approve and comment on, a compaction, a
  message from another session, the "Switched to" band, a bookmarked reply, the fast mode bolt) and `auto-mode` (an empty
  conversation with the "Auto mode is enabled" notice). Its log shows `SubmitUndone`, `LinkRequested` and `FeedbackRequested`.

### Removed

- Style only: the classes `TextBlock.ClaudeChatRejected` (the "Rejected" label),
  `SelectableTextBlock.ClaudeChatOutput.failed` (the red output) and `TextBlock.ClaudeChatMoreLines` (the "+N lines"
  line). A host that styled them loses the effect; nothing public was removed.

### Fixed

- The fast mode bolt was cut in half at the top edge of the reply box: the box's control clipped what overflowed it, and the bolt
  sits 6 px above. It shows whole now.
- The summary of a live compaction (a `user` line with `isSynthetic`) no longer shows as a message from the user: it is
  the compaction card's. A subagent's synthetic line is not taken for it.

### Deliberate differences from the extension 2.1.310

The appearance and behaviour are those of the Claude Code extension 2.1.310, measured and described, with these
exceptions, each kept on purpose (the sections above say each one in its place):

- **What the host cannot do.** The contract with the Kairos machine did not change: the answer to a permission request
  carries only the decision and, on a refusal, the message (4,000 characters at most); the modes are `plan`, `default`,
  `acceptEdits` and `auto`; an unknown local command is ignored. So "Yes, and manually approve edits" asks for the mode
  `default` through `PermissionModeRequested`, where the extension sends `updatedPermissions` with `setMode`; a refusal
  has no refusal sentence of the extension's; the command of a Bash request cannot be edited; and each quoted passage of a
  plan comment is shortened to fit the 4,000, where the extension sends it whole.
- **What needs the host.** The links ("Learn more", "Manage usage on claude.ai"), the thumbs and the feedback box show
  only while the host listens to `LinkRequested` or `FeedbackRequested`; the component opens nothing and sends nothing
  to Anthropic by itself. The "Ultracode" suffix of the model chip comes only from `get_settings`.
- **The plan.** The plan's document is a panel over the list, where the extension opens an editor tab beside the chat; a
  passage to comment lies inside one paragraph or heading and does not cross blocks; the card's list of comments is 160 px
  high (`ClaudeChat.PlanCommentsMaxHeight`), where the extension's depends on the window; "No, keep planning" with an
  empty field sends "User chose to stay in plan mode and continue planning"; and in the touch layout the plan card has no
  line "Select text in the preview to add comments".
- **Tool rows and requests.** Edit and Write keep their diff in the card, where the extension opens it in a tab; a Write is
  told to have succeeded only when its input has a `content` that is not a text; a request that comes without `agent_id`
  belongs to the agent whose step asked, so one from an unseen step may mark the wrong agent; the diff and the commands have
  no syntax highlighting, as in the extension 2.1.310, so there is no tokenizer; the old tool rows are not pruned; the
  Claude in Chrome rows read the fields of the tool's schema, have no "[Show Tab]" link, and on the Kairos relay a
  `computer` action has no thumbnail; and the Focus view has no "Run in background" (the VS Code host's).
- **The list and the reply box.** The row of actions sits at the end of the turn and copies the whole turn, where the
  extension has one under each assistant message, with no fade of 0.18 s; the scroll on send jumps at once, with no glide of
  2 s; Ctrl+wheel scrolls the list, where it zooms in the extension; the tooltips are plain text; the time format is
  `ChatStrings.TimeFormat`, not the claude's; the working verbs are the component's own; and the reply box shows no suggested
  prompt and none of the extension's Ctrl+Esc hints.
- **Pastes, drops and the palette.** A paste cut by the 50,000 limit is not sent as an `inline_pastes` entry; the direction of
  a text follows the control (`FlowDirection`), not each paragraph; the "Effort" row of the palette steps through the levels
  where the extension has a slider; the palette has no Customize or Settings rows yet (nor Rewind, Clear conversation,
  Export or the footer); and a file dropped while a permission request waits is attached, where the extension ignores it.
- **Messages from elsewhere.** The messages of agents, sessions and channels are recognized by the component's own reading
  of their format, so one of a shape the claude does not write may be read a little differently.
- **Group 6.** The "Why?" text of the "Switched to" band is one generic text, not one by category; the agent card has no
  prompt, tool calls, "Open transcript" or "Stop agent"; the Agent map has no tail row for a live agent outside any fold;
  and the "Switched to" band is not shown in a reopened history, and gives way to the browser notice and to a Remote Control
  error (one banner at a time).
- **Not confirmed.** The live event of Win32 screen readers in Avalonia 11.0.10 was not tried with a real screen reader.

### Waiting

What stays out of 0.6.0, and why. Each item is waiting for something the component does not have:

- **LaTeX.** The extension draws it with KaTeX; this release takes no new dependency, so a formula shows as its source.
- **The "Session paused" card and the billed auto-mode card.** Both need `request_user_dialog`, which the Kairos machine
  refuses.
- **The suggestion to make auto mode the default.** It needs `auto_default_nudge`, which does not reach the component.
- **Prompt suggestions.** They need `--prompt-suggestions`, which the machine does not pass to the claude.
- **Ultracode.** It needs `get_settings` to say so, and the machine cuts that reply down to the model and the effort.

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
