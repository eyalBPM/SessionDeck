# SessionDeck

**A Windows control deck for your running Claude Code sessions.**

SessionDeck tiles every VSCode window into a live grid and shows each Claude Code session inside it as a status card — grey when idle, blue while working, blinking orange when Claude is waiting for you, green when it's done. One click focuses the window, activates the right tab, and clears the alert.

Under the hood it's a general-purpose window deck (any top-level window can be tiled, pinned and driven from a CLI), but the UI and workflows are deliberately scoped to VSCode + Claude Code.

> Status: actively developed, `v0.6.27`. Windows-only by design.

---

## Why

When you run several Claude Code sessions at once, the expensive thing isn't the work — it's noticing that a session stopped and is waiting for you. SessionDeck turns that into a glance: a blinking orange border means *some* session needs an answer, and clicking it takes you straight there.

## Features

- **Live window grid** — real DWM thumbnails (`DwmRegisterThumbnail`), rendered by the Windows compositor. No screen capture, no code injection, near-zero CPU.
- **Workspace cards** — one card per VSCode workspace. Workspaces are persistent: a card survives closing the window and re-binds automatically when a matching window reappears.
- **Session cards** — one sub-card per Claude Code session, with a status-coloured border driven by Claude Code hooks (`idle` / `working` / `waiting` / `done` / `error`). Status → colour/blink mapping lives in config, not in code.
- **Click to resume** — clicking a session card focuses the VSCode window, activates that session's tab (via the companion extension), and acknowledges the blink.
- **Windows notifications** — when the whole deck is quiet and something needs attention, it escalates to a native notification, and withdraws it when the cause is gone.
- **Reserved Zone** — SessionDeck can claim half or all of a monitor as an AppBar, so maximized windows and snap never cover it.
- **Stage / Pin** — define a target rectangle once, then pin any window to it from the UI or the CLI.
- **Full CLI** — everything is scriptable over a named pipe, with a <100ms round trip so hooks stay cheap.
- **Starts with Windows** and restores the complete layout, zone and stage.

## How it works

```
Claude Code hooks ──> sessiondeck-hook.ps1 ──> sessiondeck session status --id ... --state ...
                                                        │
                          named pipe  \\.\pipe\sessiondeck
                                                        ▼
VSCode windows ──DWM thumbnails──>  SessionDeck (WPF)  <──── VSCode extension (tab activate / tab labels)
                                                        │
                              transcript scanner (fallback "waiting" detection)
```

Hooks alone aren't enough: inside the **VSCode Claude Code UI** the `Notification` and `PostToolUse` hooks never actually fire, so a session waiting on a permission prompt would stay blue forever. SessionDeck therefore also scans the session transcript for a `tool_use` with no matching `tool_result` — an independent signal that Claude has stopped. Per-tool thresholds were calibrated on 11,000+ real tool calls and chosen for false-alarm rate (`Read`/`Edit` at 15s ≈ 0.1%, `Bash` at 120s ≈ 1%, `Agent` excluded entirely at 37%). See [`hooks/README.md`](hooks/README.md) for the full table.

## Architecture

| Component | Role | Tech |
|---|---|---|
| UI shell | Cards grid, editing, window picker | WPF, .NET 10 |
| Thumbnail host | Live window previews | DWM Thumbnail API via `HwndHost` |
| Window tracker | Title change / create / destroy → re-bind | `SetWinEventHook` (no polling) |
| AppBar service | Reserved Zone | `SHAppBarMessage` |
| Pipe server | CLI command intake | `NamedPipeServerStream`, JSON |
| Transcript reader | Hook-independent "waiting" detection | Polls session `.jsonl` |
| Config store | Persistence | JSON in `%APPDATA%\SessionDeck` |

No admin rights, no injection into foreign processes, per-monitor DPI aware (v2).

## Getting started

**Requirements:** Windows 10/11, [.NET 10 SDK](https://dotnet.microsoft.com/download), VSCode with the Claude Code extension.

```powershell
git clone https://github.com/eyalBPM/SessionDeck.git
cd SessionDeck
dotnet build -c Release
.\bin\Release\net10.0-windows\SessionDeck.exe
```

The first launch starts the UI and the pipe server. Any later invocation with arguments acts as a CLI client against it.

**Wire up the hooks** — point your Claude Code `settings.json` hooks at [`hooks/sessiondeck-hook.ps1`](hooks/sessiondeck-hook.ps1); see [`hooks/README.md`](hooks/README.md).

**Install the VSCode extension** (enables tab activation and live tab labels). The `.vsix` is not checked in, so build it first:

```powershell
cd vscode-extension
npm install
npx @vscode/vsce package
code --install-extension .\sessiondeck-connector-*.vsix
```

**Add SessionDeck.exe to your PATH.** The hook bridge resolves the executable from PATH before falling back to a build directory, so this is what lets the hooks work from any workspace.

## CLI

```
sessiondeck list [--all]                 # workspaces + sessions
sessiondeck add <folder path>            # add a workspace
sessiondeck remove <target>
sessiondeck set <target> [--title "..."] [--desc "..."] [--color <c>]
sessiondeck focus <target>               # activate the window in place
sessiondeck pin <target>                 # move it to the Stage, then activate
sessiondeck stage --monitor <n> --half left|right | --full | --rect x,y,w,h
sessiondeck zone  --monitor <n> --half left|right | --full | --off
sessiondeck status

sessiondeck session start  --id <session_id> --workspace <name> [--title "..."]
sessiondeck session status --id <session_id> --state working|waiting|done|error|idle
sessiondeck session end    --id <session_id>
sessiondeck session list   [--workspace <name>] [--all]
```

`<target>` is a stable numeric workspace id or `--match "<regex>"`. Exit code 0 on success, non-zero with a message on stderr otherwise.

## Documentation

- [`SPEC.md`](SPEC.md) — full development spec, decisions log and roadmap (Hebrew).
- [`hooks/README.md`](hooks/README.md) — hook wiring and the waiting-detection heuristics (Hebrew).
- [`MANUAL_TESTS.md`](MANUAL_TESTS.md) — manual test checklist.
- [`vscode-extension/README.md`](vscode-extension/README.md) — the companion extension.

## Known limitations

- A **minimized** window freezes its DWM thumbnail on the last frame — keep tracked windows restored (being covered by other windows is fine).
- **Inactive VSCode tabs have no thumbnail.** VSCode/DWM don't render them, so session cards are text + border only. This is a hard platform limit, not a missing feature.
- Claude Code exposes no dedicated `error` hook; the `error` state exists in the model and CLI but is mapped conservatively.
- Installation is still three manual steps (build, extension, hooks) and the hook JSON needs absolute paths. A packaged release with a single installer is planned.

## License

[MIT](LICENSE) © BPM Ltd.
