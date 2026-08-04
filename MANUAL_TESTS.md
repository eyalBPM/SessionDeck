# SessionDeck — manual test checklist (v0.6.0, stages C–D)

> Purpose: to check physically what can't be checked from a distance. Tick ✔ next to whatever passed.
> **Running it:** `bin\Debug\net10.0-windows\SessionDeck.exe` (or F5 from Visual Studio).
> **CLI from a terminal:** `SessionDeck.exe <command>` from that same folder (`SessionDeck.exe help` for the full list).
> **The config file:** `%APPDATA%\SessionDeck\config.json` — it also holds the old tiles from stages A–B (a legacy field, not displayed).

## 1. Workspace cards — adding and binding

- [ ] "+ Add workspace" → pick a project folder → a card is created, named after the folder.
- [ ] If a VSCode window is already open on that workspace — the card binds to it immediately (live thumbnail).
- [ ] `SessionDeck.exe add "D:\path\to\project"` — adding from the CLI, same behavior.
- [ ] Adding the same folder twice — blocked with a message.
- [ ] A git project's card shows the current branch (⎇); switching branch in VSCode updates within ~10 seconds.
- [ ] A project with Peacock (`.vscode/settings.json`) — the card border and title take the Peacock color.
- [ ] Closing the VSCode window — the card stays, showing "No VSCode window open"; reopening — it binds again automatically.
- [ ] Dragging a VSCode window and dropping it onto SessionDeck — a card is created/bound; a non-VSCode window — blocked with a message.

## 2. Session cards — statuses from the hooks

Install the hooks per `hooks/README.md`, then open a Claude Code session in some project:

- [ ] Opening a session — a grey (idle) sub-card appears under the workspace (the workspace is created if it didn't exist).
- [ ] Submitting a prompt — the border turns steady blue (working).
- [ ] Claude asks for a permission — blinking orange (waiting).
- [ ] Turn ends — blinking green (done); clicking the card — steady green + focus to the window.
- [ ] Closing the session — the card disappears; the ▼ button on the main card shows it as closed (dimmed).
- [ ] Manual check without Claude Code — run the commands at the end of `hooks/README.md`.
- [ ] `SessionDeck.exe session status --id <sid> --state error` — blinking red until clicked.

## 3. Managing the deck

- [ ] Clicking a card (not a button) — focuses the VSCode window in place.
- [ ] Clicking a card **with no open window** — VSCode opens on the project folder and the card binds to it.
- [ ] ▶ — the window moves to the Stage area configured in the toolbar.
- [ ] ✏ — edit title/description/color (including going back to "Automatic color").
- [ ] 🗕 — the card disappears; the "👁 hidden" toggle brings it back (dimmed, and the menu item changes to "Show on the deck again").
- [ ] Active cards (open window / live session) jump to the top of the deck.
- [ ] Many cards — vertical scrolling works, cards wrap into rows by window width.
- [ ] 🔍 (v0.6.9) — opens/closes the search row; both ✕ and Escape close it. Closing clears the filter.
- [ ] Text search — filters cards: a workspace shows if its own fields (name/path/branch/description) match, or if it has a matching session (title/detail/id). A matching session shows even when closed.
- [ ] "Search content too" — when ticked, sessions whose transcript file contains the text are included as well; the status bar reports how many were found.

## 4. Zone / Stage

- [ ] Zone "Right half" — the SessionDeck window docks and the work area shrinks; "Off" releases it.
- [ ] Zone "Left quarter" / "Right quarter" (v0.6.30) — the window takes a quarter of the screen width on the matching side; a maximized window doesn't enter the quarter.
- [ ] Custom zone (v0.6.30) — picking "Custom left…" opens a size dialog; `2/7` gives ~28.6% width, `40%` and `0.4` work, invalid input disables "OK". Cancel restores the previous selection. Clicking the already-active item reopens the dialog. The size is shown in the combo item and survives a restart.
- [ ] Zone lock (v0.6.30) — while the zone is active: dragging the title bar doesn't move it, no resize from the frame (the cursor doesn't change), double-clicking the title doesn't maximize, and Win+Shift+Arrow doesn't move it to another screen. Minimize and restore work, and the window returns exactly to the zone. Turning the zone off restores normal drag/resize.
- [ ] "Stage" — screen/half + monitor affect ▶.

## 5. Persistence + startup

- [ ] Close and reopen — every workspace, session (closed ones included), status and acknowledgement comes back.
- [ ] `%APPDATA%\SessionDeck\config.json` is readable; changing a color in `StatusStyles` (working→purple, say) is picked up after a restart.
- [ ] The "Start with Windows" toggle in ⚙ creates/removes the registry value (`HKCU\...\Run\SessionDeck`).

## 6. Stage D — the VSCode extension (SessionDeck Connector)

Prerequisite: the VSIX is installed (`code --install-extension vscode-extension\sessiondeck-connector-0.5.0.vsix`) **and every VSCode window has been reloaded** (Ctrl+Shift+P → "Developer: Reload Window") afterwards. The extension's log: Output → the "SessionDeck" channel.

- [ ] After the reload — a 📑 chip with the number of open Claude tabs appears in the card header (the tooltip lists their names); opening/closing a Claude tab updates the number within ~a second.
- [ ] Switching branch in VSCode — the ⎇ on the card updates immediately (not only after ~10 seconds).
- [ ] **Clicking a session card whose tab is open** — the window takes focus and that session's tab is revealed.
- [ ] **Clicking a session card whose tab is closed** (but VSCode is open) — the tab is reopened (resume) with its history.
- [ ] **Clicking a closed session** (expanded view ▼) — same thing: the session comes back to life in a tab.
- [ ] "Maximized" opening: the sidebar, the bottom panel and the secondary sidebar close before the tab opens (disable with `OpenSessionMaximized: false` in config).
- [ ] **Clicking a session while VSCode is fully closed** — VSCode opens, and within ~seconds the session's tab opens by itself (a pending open waiting for the connector).
- [ ] Sessions show a real name within ~10 seconds: the **tab title** (ai-title) as the primary title, with the session title (from the first prompt) beneath it as secondary when they differ.
- [ ] `SessionDeck.exe session open --id <sid>` — same behavior from the CLI.
- [ ] Closing SessionDeck and reopening it — the extension reconnects on its own within ~5 seconds ("connected" appears in the log).
- [ ] **auto-acknowledge (v0.6.0)**: a session blinking green → switch to its tab directly in VSCode (without touching the app) — the blink stops within ~a second. If the tab is already active and the window focused when the session finishes — no blink starts at all.
- [ ] **"waiting" status in the VSCode UI (v0.6.17)**: Claude asks a question (AskUserQuestion) or presents a plan for approval (ExitPlanMode) in a VSCode tab — the card turns blinking orange within ~10 seconds **even though `Notification`/`PostToolUse` don't fire there**, and the sub-line shows the question text. After answering — back to blue "working", and at the end to green "done".
- [ ] **A permission request in the VSCode UI (v0.6.19)**: Claude asks to approve `Edit`/`Write`/`Read` and you don't answer — the card turns orange within ~15-25 seconds (this is the case reported 2026-07-20). After approving — back to blue.
- [ ] **A permission request for `Bash`/`PowerShell` (v0.6.19)**: same, but the threshold is 120 seconds — the card turns orange within ~2-2.5 minutes.
- [ ] **No false alarms (v0.6.19)**: a `Bash`/`PowerShell` call running **less** than 2 minutes (an ordinary build) or an `Agent` running for many minutes — the card **stays blue**.
- [ ] **Card title = tab title (v0.6.25)**: every session card's title matches its VSCode tab label (shown in full on the deck, truncated with `…` in VSCode). When Claude renames the tab — the deck title follows within ~10 seconds. A manually given name (`/rename` or editing on the deck) wins and keeps winning.
- [ ] **auto-acknowledge for a session with no ai-title (v0.6.20)**: an older session whose transcript contains no `ai-title` (VSCode labels its tab from the prompt) — opening its tab directly in VSCode stops the blink. This is the case reported 2026-07-20.
- [ ] **An ambiguous label doesn't ack the wrong session (v0.6.20)**: two open sessions started from the exact same prompt — opening the tab silences neither (a card that keeps blinking beats a real alert that vanishes quietly).
- [ ] **auto-acknowledge after a title change (v0.6.15)**: mid-turn Claude renames the tab (a new ai-title) → the session finishes while the tab is focused — the blink stops by itself within ≤10 seconds even if SessionDeck's title hasn't updated yet (drift between the tab label and TabTitle).
- [ ] **auto-acknowledge across split groups (connector v0.6.6)**: two Claude tabs in two editor groups (split) — only the session in the focused group gets acknowledged, not the active tab in the other group.
- [ ] **Leaving the tab restores the blink (v0.6.28)**: you're sitting on session A's tab → you switch to another tab (a file, or another session) in the same VSCode window → session A moves to waiting/done — **the card blinks**. This is the case reported 2026-07-20: before, the blink stopped instantly because the deck still "remembered" A's tab as active.
- [ ] **Focus state ages out (v0.6.28 + connector v0.6.7)**: kill the VSCode window by force (Task Manager, no clean close) while a Claude tab is active → within ~6 seconds the deck stops believing that tab is being watched, and a session that moves to waiting afterwards **does** blink.
- [ ] **Expanded view (▼, v0.6.0)**: also shows historical sessions from the transcripts folder (the 15 most recent), with titles and timestamps; clicking them resumes.
- [ ] **An old session that can't be resumed** (from before a folder rename, for instance) — clicking shows a message in the status bar and doesn't open an empty new tab.
- [ ] **RTL (v0.6.0)**: titles/descriptions that start with Hebrew are right-aligned in RTL direction. Since v0.9.0 the UI chrome itself is English and LTR — this check is about data coming from outside the app (workspace/session/task names, descriptions, tooltips, search text and the git branch), which must still follow its own language.
- [ ] Long titles wrap to another line (no "..."); hovering a session card lightens its background.

## 6a. A Windows notification when the deck may be hidden (v0.6.26)

The condition for a notification: **"Windows notifications" is ticked in ⚙ (the default), and 📌 (always on top) is off, and the Zone is "Off"** — only then is the deck an ordinary window that something can cover. In every other state the behavior is unchanged.

- [ ] 📌 off + Zone "Off" + the deck **not** focused → a session moves to waiting/done/error: a Windows balloon appears with the workspace and session name, the taskbar icon gets a **colored dot** (orange/green/red by status), and the button flashes orange once.
- [ ] The dot stays as long as some session needs attention, even after the balloon is gone.
- [ ] Clicking the balloon or the tray icon → the deck window comes to the front (and restores from minimized).
- [ ] **Focused = no notification**: the deck is focused and a session finishes — no balloon, no dot, no flash (the card's blink is visible anyway).
- [ ] **The gate works**: turn 📌 on (or Zone ≠ Off) → the dot and the tray icon disappear immediately; a new session moving to waiting raises no balloon.
- [ ] **No repeats**: a session that stays orange for a long time raises a balloon **once** only, even as other updates happen on the deck.
- [ ] **Re-arming**: click the card (acknowledge) and let the session return to waiting later → a new balloon does appear.
- [ ] **No balloon on startup**: close SessionDeck while sessions are blinking and reopen it (without 📌 and without a Zone) — no burst of balloons for the restored sessions.
- [ ] Several sessions need attention at once → the balloon names the most severe + "and N more"; the dot's color is the most severe one (error > waiting > done).
- [ ] No stuck tray icon: after acknowledging every session, or after closing the app, the icon disappears.
- [ ] **The "Windows notifications" switch in ⚙**: ticked by default (including in an old config that lacks the field); hovering shows an explanation that includes the 📌 + Zone condition.
- [ ] Turning the switch off → the dot and the tray icon disappear immediately, and a new session moving to waiting produces nothing. Turning it back on → the badge returns, but **without** a balloon for sessions that were already orange.
- [ ] The state persists: turn the switch off, restart the app — it is still off in ⚙ and `WindowsNotifications: false` appears in `config.json`.
- [ ] **Withdrawing a notification handled outside the deck (v0.6.27)**: a blinking session raises a balloon → open its tab **directly in VSCode** (without touching the deck) → the blink stops, **and so do** the dot, the tray icon and the notification itself (including its entry in the Windows notification center).
- [ ] **Two sessions, one handled**: both blink and the balloon says "and 1 more" → answer one in VSCode → the notification **stays** (a session is still waiting), the dot stays, and no new notification pops. Only when the second is handled too does everything disappear.

## 6b. A tasks panel from an external JSON file (v0.7.0, T-0116)

Turning it on: ⚙ → "Tasks file (JSON)..." → pick a file (the file contract is documented in task T-0116, and behind the dialog's "📋 Copy spec" button). An empty field = fully off (the default — no strip, no watcher).

- [ ] **Opt-in**: with no path configured — no UI change at all (no strip, no 🗒 button on the cards).
- [ ] Setting a path to a valid file — a collapsed strip on the right: a colored square per task (color from `statusColors`), pinned ones first with a white border + a separator line; hovering shows name/status/description.
- [ ] **Live updates**: editing the file while running — the strip updates within ~a second (debounce + retry on a locked file).
- [ ] **File errors are visible**: broken JSON / a deleted file / a version other than 1 — the strip shows ⚠ with the reason (and on the page — an error state instead of the list). Fix the file — the list returns.
- [ ] **Record errors**: a record with no `id` or `name` — skipped; the rest of the list loads and a small ⚠ appears with the details.
- [ ] 📋 at the top of the strip — switches to the **tasks page**: the full list (name, description, status, 🔗/▶ buttons), and on the left, live session squares grouped by workspace (border color = status, updating live including the blink). ✕ / Esc return to the deck.
- [ ] **Mutual exclusion with search**: on the tasks page the 🔍 button is disabled; while the search row is open the 📋 button is disabled.
- [ ] Clicking a task square **without** `sessions` — a new session opens in its workspace (if `newSessionPrompt` is set in the file — the text waits in the input box with the id and name filled in).
- [ ] Clicking a task square **with** `sessions` — a dropdown: "+ New session" and then the sessions; a live session (🟢) — focus; a session that isn't running (▶) — a real resume (including launching VSCode if needed).
- [ ] 🔗 "Open task" — opens the `url` (obsidian://, for instance).
- [ ] **Workspace cards**: a 🗒 button with the number of linked tasks (matched by `workspace` path); 0 — shown and disabled; clicking — the task list inside the card.
- [ ] Turning it off (clearing the path in the dialog) — all of the tasks UI disappears, the watcher stops.

## 7. Known / limitations

- The 📑 tag on a single session card (open as a tab) is best-effort — based on comparing the tab name to the session title.
- `claude-vscode.editor.open` is an internal Claude Code command; if it disappears in a future version — the extension falls back automatically to a terminal with `claude --resume`.
- Claude Code's `Notification` covers permission/input; there is no dedicated error event (SPEC §9.2).

*The internal command `SessionDeck.exe snapshot <path>.png` renders the UI to a PNG (without the thumbnails) — useful for remote debugging.*
