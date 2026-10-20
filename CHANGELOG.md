# Changelog

All notable changes to this project are written here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

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
