# SessionDeck (formerly WinGrid) — development spec (v0.6)

> Last updated: 2026-07-17
> Status: **stages A–D implemented** (v0.5.0, 2026-07-19; pending manual testing — `MANUAL_TESTS.md`); the rename to SessionDeck is done (v0.3.0); auto-acknowledge from a tab (stage D, last item) — still open.
> Name: **SessionDeck** (chosen 2026-07-17 — decision 20). The rename was completed in full: the code on 2026-07-17 (csproj/slnx, namespaces, the `sessiondeck` pipe, the mutex, `%APPDATA%\SessionDeck` + migration, the Run value + migration), and the folder name `D:\Eyal\SessionDeck` (2026-07-19, by Eyal).
> Spec author: Claude Code together with Eyal, sessions 2026-07-16 – 2026-07-17.

---

## 1. Background and goal

A general-purpose Windows app for monitoring and driving windows: a grid of "tiles", each showing a **live view** of a chosen OS window, with a title, a description and a colored border. Clicking one raises/activates the real window. The app is also driven from a CLI.

**The overarching goal (kept in mind, though the spec stays generic):** control over every Claude Code session running on the machine (each session in a VSCode window). In phase 2, Claude Code hooks will call the CLI to update a border color per session's work status (working = green, waiting for input = blinking orange/black, for instance).

**Agenda update (2026-07-17, v0.5 — decision 15):** the app is now **explicitly focused on Claude Code sessions in VSCode** — a control deck showing every VSCode window as a card, and the sessions (tabs) inside it as sub-cards with live status from the hooks. The generic capability (monitoring any OS window) is kept in the engine under the hood, but the UI and the flows are filtered to VSCode only (decision 13). The full model — §2b. Stages A–B (already implemented) are the foundation: thumbnails, focus/pin, zone, CLI, persistence, blink — all of it serves the new structure.

---

## 2. Core concepts

| Concept | Definition |
|---------|------------|
| **Tile** | A square in the grid representing one OS window (a top-level HWND): live view + title + description + colored border. |
| **Stage** | A predefined global target area (full screen / half screen / rectangle) that a window "jumps" to on a **Pin** action. |
| **Reserved Zone** | The screen area the app itself claims (full or half screen). It is subtracted from Windows' work area — other windows (maximized ones included) can't enter it; the mouse moves there freely. |
| **Matcher** | A persistent rule binding a tile to a window: process name + title pattern (regex), for re-binding after a restart. |
| **Window Card** | (v0.5) A card per VSCode window: live view + window details + a Focus button + an expand button. The evolution of the existing Tile. |
| **Session Card** | (v0.5) A sub-card inside a Window Card, representing a Claude Code session (tab): name, status, border color by status. No thumbnail. |

---

## 2b. The cards and statuses model (v0.5 — the new agenda)

### UI structure
- A **Window Card** per VSCode window: a live view (DWM thumbnail) at the top, the window's details (workspace, process), an open (Focus) button, and an expand button.
- **Session Cards** below the thumbnail: one per live Claude Code session in that window — the session name, a textual status, and a **colored border by status** (the colors and the blink live here, not at the window level). No thumbnail — an inactive tab isn't rendered by VSCode/DWM, so there are no pixels to show (an absolute technical limit).
- **Expanding**: clicking the expand button also shows **closed** sessions of that workspace, with the option to reopen them (resume — implemented in stage D). Retention for closed sessions: the ~20 most recent per workspace (config).

### Statuses (decision 11)
| Status | Border | Transitions |
|--------|--------|-------------|
| `idle` | steady grey | a session exists and isn't working (after SessionStart, before the first prompt) |
| `working` | steady blue | until the next event |
| `waiting` | blinking orange | waiting for permission/input; until a click or the next event |
| `done` | blinking green → steady | blinks until **acknowledge** (a user click) → steady green |
| `error` | blinking red → steady | same — blinks until acknowledge → steady red |

- **Clicking a Session Card** = Focus the window + acknowledge (stop the done/error blink) + activate/open that specific tab in VSCode (stage D, v0.5.0). Auto-acknowledge when the user opens the tab directly in VSCode — still open (question 3 in §9).
- **The status→color/blink mapping lives in config** — changeable without touching the hooks or the code.
- Note: Claude Code's hooks have no dedicated error event; the `error` state exists in the model and in the CLI, and the mapping from real events will be settled in stage C based on what the hooks actually provide.

### A session's lifecycle (decision 12)
- SessionStart (hook) → a Session Card is created under the workspace's Window Card (creating the Window Card if there isn't one).
- SessionEnd (hook) → the card disappears from the normal view; it is kept and shown in the expanded view.

### Managing workspaces (added 2026-07-17 — decisions 16–18)
- **A workspace is a persistent entity**: it is remembered even with no VSCode window open (the equivalent of today's disconnected tile). The Window Card effectively becomes a **Workspace Card** — the window is only its live binding.
- **Sorting**: active workspaces (open window / live session) rise to the top; old ones can be **hidden** and are shown through a "show hidden" button/filter.
- **Layout**: wide cards, wrapping by width, a **minimum size** per card, all inside a vertical **scroll area** (the grid no longer has to fit the screen in full).
- **The main card's content**: the project name, git's **current branch**, the live view, and the session sub-cards. **Custom title + description** are supported on the main card and on the sub-cards alike.
- **The main card's color**: taken automatically from VSCode's workspace settings (`.vscode/settings.json` — `peacock.color` or `workbench.colorCustomizations.titleBar.activeBackground`) when present — a natural integration for anyone working with Peacock; otherwise a manual/default color.
- **Adding a workspace to the deck (decision 21)** — in priority order:
  1. **Picking a folder** (primary, stage C): a folder-picker dialog, like opening a project in VSCode. The path is known immediately → the Peacock color + branch are available before any window opens or session runs.
  2. **Reported by the VSCode extension** (stage D): the extension reports the open workspaces and adds them automatically.
  3. **Dragging a window in (drag-in)** — kept as a secondary channel: blocked if the window isn't VSCode or if the workspace is already on the deck (and with the extension active it will already be there anyway).
  4. **The hook's `cwd`** — a safety net: a session reporting a workspace that isn't on the deck creates it automatically, with the path from the cwd.
- **Technical note**: reading settings.json and the branch (`.git/HEAD`) requires the workspace **path** — guaranteed by flows 1/2/4 (folder pick, extension, hook cwd). The path is stored in config permanently.

---

## 3. Functional requirements

### F1 — a live grid
- A live view of each window through the **DWM Thumbnail API** (`DwmRegisterThumbnail`) — rendered by Windows' compositor, zero CPU cost, with no injection into or capture of the source windows.
- **Automatic** layout (decided 2026-07-16): the column count is computed from the number of tiles and the size of the display area; all tiles the same size; reordering by dragging within the grid.
- Each thumbnail's aspect ratio follows the source window (letterboxing inside the tile).

### F2 — anatomy of a tile
- **Title**: defaults to the window title from Windows (`GetWindowText`); manually editable. As long as it hasn't been edited by hand — it updates automatically when the window changes its title.
- **Description**: empty by default; editable (UI + CLI).
- **Colored border**: a static color, or a **blinking mode** — alternating between two colors (orange/black, say) at a configured rate (default 500ms).
- Additional indication on the tile: process name + the window's icon (nice-to-have).

### F3 — activation and pinning (decided 2026-07-16: two modes)
- **Focus (activate in place)**: an ordinary click on a tile → the real window is brought to the front and activated (`SetForegroundWindow`) **where it currently is**.
- **Pin (pin to the Stage)**: a separate action (a button on the tile / double-click / CLI) → the real window is moved to the Stage (`SetWindowPos`) and activated.
- Defining the Stage: through the UI ("set Stage") and through the CLI — monitor + half (left/right) / full / a custom rectangle.

### F4 — Reserved Zone
- The app can "claim" a full or half screen through the **AppBar API** (`SHAppBarMessage` — ABM_NEW / ABM_SETPOS), per monitor.
- While the zone is active the work area shrinks: maximized windows and even snap don't enter the area; the mouse still moves freely.
- Modes: `off` (an ordinary window) / `quarter-left` / `half-left` / `half-right` / `quarter-right` / `full` / `custom-left` / `custom-right`, per screen.
- Custom mode: a free width as a fraction of the screen width — input "2/7", "40%" or "0.4" (range 5%–100%); chosen in a dialog from the UI or from the CLI with `--size`. The value is stored in config (`Zone.Size`) exactly as typed.
- While the zone is active the window is **locked in place**: dragging the title bar, resizing, maximizing and Win+Shift+Arrow are all swallowed; only turning the zone off releases it. Minimize/restore stay available.

### F5 — adding windows (decided 2026-07-16: picker + drag)
1. **Picker** (MVP): a crosshair-style "pick a window" button (like Spy++) — drag the cursor onto the window you want and release.
2. **Drag-in** (stage B): drag a real window's title bar and drop it over the app → a tile is added. Implementation: a global low-level mouse hook detecting the end of a foreign window's move-drag over the app's area.
3. **CLI**: `sessiondeck add ...` (see §4).
- Windows filtered out of the picker: SessionDeck's own windows, windows with no title, tool windows.

### F6 — managing tiles (updated 2026-07-16)
- Removal (UI + CLI), editing title/description/color (UI + CLI), reordering by drag.
- Every tile has a **state** field: `connected` / `disconnected`. The state is shown as a status indication on the tile (an icon/badge + a placeholder instead of the thumbnail, say) — **the title, description and color do not change because of a disconnect** (decided 2026-07-16). The state also appears in `sessiondeck list`.
- **A window that closes**: the tile moves to `disconnected` and is not deleted; when a new window matching the Matcher opens — an automatic re-bind and the tile returns to `connected`. Automatic removal = a setting (off by default).

### F7 — persistence + auto-save (updated 2026-07-16)
- A JSON profile at `%APPDATA%\SessionDeck\config.json` (with a one-time automatic migration from the old `%APPDATA%\WinGrid`): the tile list (Matcher, manual title, description, color/blink), zone state, Stage setting, order, and the main window's position/size.
- **Always auto-save**: every change (add/remove/edit/color/order/zone/stage) is saved immediately (~1s debounce, atomic write: temp file + rename). There is no "save" button and no unsaved state.
- On startup: load the profile, enumerate existing windows + re-bind by Matchers; tiles whose window hasn't opened yet are shown "disconnected" (grey) until a matching window appears.

### F8 — CLI (see §4)
- Every management capability is available from the CLI, for automation (and in particular for Claude Code's hooks in phase 2).

### F9 — starting with Windows (added 2026-07-16)
- SessionDeck registers in the user's startup (registry `HKCU\...\Run`, no admin; can be turned off in the settings).
- At startup the **full state** is restored from the profile: every tile (through Matchers — after a restart every window gets a new HWND, so restoration is re-bind based), the Reserved Zone state, the Stage setting, and the grid order.
- Tiles whose window hasn't opened yet after the restart appear disconnected and bind automatically the moment the matching window opens.

---

## 4. The CLI — in detail

### Execution model
- **Singleton**: the first run raises the UI + a **named pipe** server (`\\.\pipe\sessiondeck`).
- Any later run with arguments is a CLI client: it passes the command over the pipe, prints the reply, and returns an exit code (0 = success, ≠0 = an error + a message on stderr). Target runtime: <100ms (critical for the hooks).
- With no instance running: CLI commands fail with a clear message (a possible future option: `--start`, which raises the UI).

### Commands (updated in v0.4.0 — the workspaces model; the old tile commands `border`/`add --match` were removed)

```
sessiondeck list [--all]                  # workspaces + sessions (--all includes closed ones)
sessiondeck add <folder path>             # add a workspace by folder (the equivalent of the UI picker)
sessiondeck remove <target>
sessiondeck set <target> [--title "..."] [--desc "..."] [--color <c>]   # an empty value = auto
sessiondeck focus <target>                # activate the workspace's window in place
sessiondeck pin <target>                  # move to the Stage + activate
sessiondeck stage --monitor <n> --half left|right | --full | --rect x,y,w,h
sessiondeck zone --monitor <n> --half left|right | --quarter left|right | --custom left|right [--size 2/7|40%|0.4] | --full | --off
sessiondeck status                        # state: version, zone, stage, workspace/session counts
```

- **`<target>`**: a workspace's stable numeric id, or `--match "<regex>"` against the name/title.
- **Colors**: names (`red`, `green`, `orange`, `blue`, `gray`, ...) or hex `#RRGGBB`.
- Session border colors are derived from the status (the `StatusStyles` map in config) — there is no `border` command any more.

### 4b. The sessions CLI (implemented in v0.4.0 — stage C)

```
sessiondeck session start  --id <session_id> --workspace <name> [--title "..."]
sessiondeck session status --id <session_id> --state working|waiting|done|error|idle
sessiondeck session end    --id <session_id>
sessiondeck session list   [--workspace <name>] [--all]     # --all includes closed ones
```

- `session_id` comes from Claude Code's hook; the workspace is used to map to a window (by title).
- These commands are called from the hooks script (stage C) — so they must stay fast (<100ms) and atomic.

---

## 5. Architecture (decided 2026-07-16: C# / .NET + WPF)

| Component | Role | Technology |
|-----------|------|------------|
| **UI Shell** | Main window, grid, tile editing, picker | WPF (.NET 10 LTS) |
| **Thumbnail Host** | Hosting DWM thumbnails inside the grid | `DwmRegisterThumbnail` / `DwmUpdateThumbnailProperties`, interop through HwndHost |
| **Window Tracker** | Tracking windows: title change, destroy, create (for re-bind) | `SetWinEventHook` (EVENT_OBJECT_NAMECHANGE / DESTROY / CREATE) — no polling |
| **AppBar Module** | Reserved Zone | `SHAppBarMessage` |
| **Pipe Server** | Receiving CLI commands | NamedPipeServerStream, a simple JSON protocol |
| **CLI Client** | The same exe with args → the pipe | System.CommandLine (or hand-rolled parsing) |
| **Config Store** | Persistence | JSON in %APPDATA% |
| **Blink Engine** | One shared timer for every blinking border | A single DispatcherTimer (not a timer per tile) |

- No admin rights; no code injection into foreign windows.
- Per-monitor DPI awareness (v2) from day one — a multi-monitor environment with mixed DPI is the target scenario.

---

## 6. Technical constraints and risks

1. **A minimized window** — the DWM thumbnail freezes on the last frame. Recommendation: keep tracked windows restored (being behind other windows is fine — the thumbnail stays live even when the window is covered). A possible future option: preventing minimize for tracked windows.
2. **SetForegroundWindow restrictions** — when the user clicks in our UI we are the foreground process and are allowed to move focus. But a `sessiondeck focus` coming from a background process (a hook) may be blocked by Windows (anti focus-stealing). Not critical: phase 2 only changes colors. If it becomes necessary — there are well-known workarounds (AttachThreadInput and friends).
3. **Granularity = an OS window** — several Claude Code sessions in the same VSCode window can't be told apart. Working convention: **one session = one VSCode window**.
4. **Drag-in requires a global low-level mouse hook** — a sensitive component (performance/stability); hence the Picker is the primary mechanism and drag-in was deferred to stage B.
5. **Elevated (admin) windows** — the thumbnail will work, but move/activate will be blocked unless SessionDeck is elevated too. Documented as a known limitation.
6. **Identification by title** — VSCode's title changes with the open file (`file — workspace — Visual Studio Code`). The Matcher must be a regex on the workspace name, not a full comparison.

---

## 7. Development stages

### Stage A — MVP
- An automatic grid + live DWM thumbnails
- **Reserved Zone (AppBar) — full / half, per monitor** (brought forward from stage B by Eyal's decision 2026-07-16 — an essential part of the app)
- Adding through the Picker + removal
- Title (auto from Windows) + a static border color
- Focus (click) + a basic Pin to Stage
- CLI: `list`, `add --match`, `remove`, `border` (static), `focus`, `pin`, `zone`
- Persistence with auto-save (F7)

### Stage B — completing the generic tool
- A blinking border + the Blink Engine
- Drag-in (mouse hook)
- Descriptions + full editing in the UI
- Disconnected tiles + automatic re-bind
- Starting with Windows + full state restoration (F9 — depends on re-bind)
- `stage` / `set` / `status` in the CLI

### Stage C — the cards UI + hook integration (updated in v0.5; no extension) — **implemented in v0.4.0 (2026-07-17)**
- A new UI structure: Window Cards + Session Cards per §2b; the display filtered to VSCode only (the engine stays generic)
- The sessions CLI per §4b; sessions are created and updated **from the hooks only**
- A status engine + acknowledge on click; the status→color mapping in config
- A hooks script (PowerShell) registered in Claude Code's settings:
  - `SessionStart` → `session start` (idle)
  - `UserPromptSubmit` → working
  - `Notification` (waiting for permission/input) → waiting
  - `Stop` (turn finished) → done
  - `SessionEnd` → `session end`
- Clicking a Session Card at this stage: Focus the window only (activating the specific tab — stage D)

**Implementation notes (v0.4.0):** the generic Tile was replaced by WorkspaceCardView (the engine — thumbnails, binding, zone/stage — was kept); the crosshair picker was removed from the toolbar in favour of picking a folder; tile data from stages A–B is kept in config as a legacy field and isn't displayed; branch/Peacock refresh every ~10 seconds; the hooks script lives in `hooks/` with installation instructions.
- session→window mapping by the workspace name in VSCode's title

### Stage D — the VSCode extension (added in v0.5) — **implemented in v0.5.0 (2026-07-19)**
- ~~**A first spike**: correlating `session_id` ↔ tab~~ — **moot**: Claude Code's extension exposes an internal command `claude-vscode.editor.open(sessionId)` that does reveal-or-resume by itself (it holds the sessionPanels map). The fallback if that signature breaks in a future version: a terminal with `claude --resume <id>`.
- Syncing from the extension to SessionDeck (over the existing pipe): open Claude tabs (label+active), the current branch (event-driven, better than the 10-second poll), the workspace path — sent at startup and on every tab/branch change
- Clicking a Session Card → Focus the window + activate/open that specific tab in VSCode (`openSession` on the pipe's reverse channel); with no connector (VSCode still starting) the request waits and is sent on the first sync (TTL 90 seconds)
- Reopening (resuming) a closed session from the expanded view — the same path
- "Maximized" opening (config `OpenSessionMaximized`, default true): collapsing the sidebar/panel/auxiliary bar before opening the tab
- Auto-acknowledge when the user opens the tab directly in VSCode — **implemented (v0.6.0)**: the tab title is the last `ai-title` record in the transcript, which makes the session↔tab correlation reliable; when the matching tab is active and the window focused — an automatic acknowledge (including when the status changes while the tab is already open)

**Implementation notes (v0.5.0):** the extension (`vscode-extension/`, TypeScript, VSIX installed by hand) holds a persistent connection to the pipe (the pipe server was upgraded to multi-instance + a connector mode); automatic session names are derived from the transcript (the last summary or the first prompt, `TranscriptReader`) and are shown instead of "session xxxxxxxx"; Claude tabs are filtered in the extension by the `claudeVSCodePanel` viewType; the 📑 tag on a session open as a tab is best-effort, by comparing the label to the title; a new CLI: `session open --id <sid>`; tab order isn't synced (deliberately dropped — the deck sorts by activity).

---

## 8. Decisions taken (2026-07-16)

| # | Decision | Choice |
|---|----------|--------|
| 1 | Pin target | **Two modes**: Focus in the current position (click) + Pin to a global Stage (a separate action) |
| 2 | Grid layout | Automatic, uniform tiles, reordering by drag |
| 3 | Stack | C# / .NET 10 (LTS) + WPF — updated from .NET 8, whose support ends 11/2026 |
| 4 | Adding a window | Picker (MVP) + drag-in (stage B) + CLI |
| 5 | Name | WinGrid — final |
| 6 | Auto-save | Always, with no save button (F7) |
| 7 | Startup | Starting with Windows + full state restoration through re-bind (F9) |
| 8 | A window that closes | The tile stays with its title/desc/color unchanged; a **state** field was added (connected/disconnected) + automatic re-bind |
| 9 | Repo location | **`D:\Eyal\SessionDeck`** (originally `D:\Eyal\WinGrid`; the folder was renamed 2026-07-19 along with the rename) — a standalone git repo, outside the OneDrive tree. Note: everything under `D:\Eyal\` is classified as a "personal project" in Eyal's settings — the BPM protocols don't apply, the general Git/safety rules do |
| 10 | Reserved Zone | **In stage A (MVP)** — an essential part of the app (Eyal's decision 2026-07-16) |
| 11 | Status scheme (2026-07-17, updated) | **working=steady blue** (settled — orange is reserved exclusively for waiting), waiting=blinking orange, done=blinking green→steady on acknowledge, error=blinking red→steady, idle=grey; the mapping in config |
| 12 | A session that closes (2026-07-17) | The Session Card disappears; available in the Window Card's expanded view with a resume option (stage D); retention ~20 |
| 13 | Display scope (2026-07-17) | VSCode only in the UI; the engine stays generic. Support for terminals — maybe in future, not now |
| 14 | Name (2026-07-17) | ~~Staying WinGrid for now~~ → superseded by decision 19 |
| 15 | The v0.5 agenda (2026-07-17) | The app = a control deck for Claude Code sessions in VSCode; the cards structure per §2b; stages C–D redefined |
| 16 | Managing workspaces (2026-07-17) | A workspace is a persistent entity; active ones on top; old ones hideable; wide cards with a min-size in a scroll area |
| 17 | Main card content (2026-07-17) | Project name + current branch; custom title/description on both card levels |
| 18 | Card color from VSCode (2026-07-17) | Taken from the workspace's settings.json (Peacock / titleBar.activeBackground) when present |
| 19 | Renaming — early (2026-07-17) | To be done before implementing stage C, by **rename-in-place** (not a new project) |
| 20 | The name (2026-07-17) | **SessionDeck** |
| 21 | Adding a workspace (2026-07-17) | Primary channel: picking a folder; the extension (stage D); drag-in stays but is blocked for non-VSCode/duplicates; the hook cwd as a safety net |

## 9. Open questions

1. ~~**session↔tab correlation (stage D)**~~ — **settled (2026-07-19)**: opening/activating a tab goes through Claude Code's own `claude-vscode.editor.open(sessionId)`, so the correlation isn't needed on our side. Only the reverse direction remains open (tab→session for auto-acknowledge) — currently best-effort by label.
2. **The source of the error state (stage C)** — there is no dedicated hook; to be checked against what the hooks actually provide (SessionEnd's reason, for instance).
3. ~~**auto-acknowledge from opening a tab directly in VSCode (stage D)**~~ — **settled and implemented (2026-07-19, v0.6.0)**: it turned out the tab title is stored in the transcript as an `ai-title` record — a reliable correlation by session_id. v0.6.0 also adds: the tab title as the primary title (the session title secondary), a list of historical sessions from the transcripts folder in the expanded view (up to 15, not stored in config), blocking resume for a session whose transcript doesn't exist under the current slug (preventing an accidental new conversation after a folder rename), sorting sessions by activity, RTL for text starting with Hebrew, and hover on session cards.

**The scope of the rename to SessionDeck** (decisions 19–20): **done 2026-07-17 (v0.3.0)** — the csproj/slnx file names, RootNamespace/AssemblyName, the namespaces in code, the pipe name (`sessiondeck`), the mutex, the config folder (`%APPDATA%\SessionDeck` + an automatic migration of an existing config), the HKCU Run value (+migration), and the references in SPEC/MANUAL_TESTS. Git history was preserved. The folder was renamed to `D:\Eyal\SessionDeck` (2026-07-19) — the rename is complete.

The remaining spec questions are settled (see §8); implementation questions will be settled as development goes.

---

## 10. Kickoff instructions — for the implementation session (Claude Code in `D:\Eyal\WinGrid`)

This document is the full brief for the implementation. The agreed order of work:

1. **Done (2026-07-16):** Eyal created the project in Visual Studio — WPF Application, C#, .NET 10 LTS, solution+project in the same folder (`WinGrid.slnx` + `WinGrid.csproj` in `D:\Eyal\WinGrid`).
2. The first session in `D:\Eyal\WinGrid` is asked to "execute the brief" — that is, to implement **stage A (MVP)** per §7, in line with every requirement (§3–§6) and decision (§8).

Instructions for the implementation session:

- **Git**: `git init` if it hasn't been done; create a `.gitignore` for .NET/VS (bin/, obj/, .vs/) **before** any commit; work by Eyal's Git rules — a branch before working, never on main, commit only on explicit approval.
- **Version**: manage the version in the csproj (`<Version>`) — bump it on every code change, starting from 0.1.0.
- **One project only**: the UI and the CLI are the same exe (see §4). Note: WPF is `OutputType=WinExe` — for the CLI to print to the parent's console you must use `AttachConsole(ATTACH_PARENT_PROCESS)` in the CLI branch before writing to stdout.
- **Recommended implementation order inside the MVP**: (1) grid + DWM thumbnails with the picker; (2) focus/pin + stage; (3) Reserved Zone (AppBar); (4) pipe server + CLI; (5) persistence/auto-save.
- **Manual checks** at the end of every milestone: run it, attach 2–3 real VSCode windows, confirm the live view, focus, and a zone that pushes maximized windows out.
- Anything not defined here is a free implementation decision; if a contradiction or a gap in the spec turns up — ask Eyal and update this document.
