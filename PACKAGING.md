# SessionDeck — packaging and distribution plan (target: v0.6.29)

> Written 2026-07-20. Intended for a separate Claude Code implementation session.
> Read this file **in full** before touching any code.

---

## 0. The goal

Today, installing SessionDeck takes five manual steps:

1. Install the .NET 10 SDK
2. `git clone` + `dotnet build`
3. Add `SessionDeck.exe` to PATH by hand
4. `npm install` + package the `.vsix` + `code --install-extension`
5. Open `~/.claude/settings.json`, paste ~40 lines of JSON, **and replace `D:\Eyal\SessionDeck` inside it with your own path — in seven places**

Step 5 is the failure point. It is also fragile on Eyal's own machine: the moment the project moves to another folder, the hooks stop working **silently, with no error** ([`hooks/sessiondeck-hook.ps1:19`](hooks/sessiondeck-hook.ps1#L19) does `exit 0` when it can't find the exe).

**The target:** the user downloads one zip from GitHub Releases, extracts it, runs `install.ps1`, done.

---

## 1. Decisions already made — do not reopen

| Topic | Decided |
|---|---|
| Zip contents | **self-contained single-file** (~150MB). Works on a clean machine with no .NET installed. The reasoning: 150MB in a one-time download is cheaper than supporting "why won't this open". |
| License | MIT, © BPM Ltd. Already on `main`. |
| Repo visibility | **public** since 2026-08-02. (Private until then — the original decision here.) |
| VS Marketplace / winget / Claude Code plugin | **Still not implemented.** The blocker that deferred them — a private repo — was removed on 2026-08-02. |
| `.vsix` in git | **Stays in `.gitignore`.** Its place is in Release artifacts, not in the repo. |

---

## 2. Step 0 — copy `hooks/` into the build folder

**This is a prerequisite for everything else.** As things stand `hooks/sessiondeck-hook.ps1` isn't copied to `bin/`, so the exe can't find it after an install.

In [`SessionDeck.csproj`](SessionDeck.csproj), add a new `ItemGroup`:

```xml
<ItemGroup>
  <Content Include="hooks\sessiondeck-hook.ps1">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

**Verification:** after `dotnet build`, the file `bin\Debug\net10.0-windows\hooks\sessiondeck-hook.ps1` exists, **and still carries its UTF-8 BOM** (check with `Format-Hex -Path <file> -Count 3`; it must start with `EF BB BF`). Without the BOM, PowerShell 5.1 reads the file's non-ASCII characters as ANSI — see [`hooks/README.md`](hooks/README.md).

---

## 3. Step 1 — the `sessiondeck install-hooks` command

This is the main part. Estimate: 2-3 hours.

### 3.1 What the command does

```
sessiondeck install-hooks [--settings <path>] [--dry-run]
sessiondeck uninstall-hooks [--settings <path>]
```

Opens `~/.claude/settings.json`, backs it up, and merges SessionDeck's seven hooks into it — writing **the real path the app was installed to**, instead of the hard-coded `D:\Eyal\SessionDeck`.

### 3.2 Critical design point — the command does not go through the pipe

**Read this before writing a line.**

[`Program.cs:14-15`](Program.cs#L14-L15) makes **any** argument mean "send this through the pipe to the running app":

```csharp
if (args.Length > 0)
    return Cli.CliClient.Run(args);
```

But `install-hooks` has to work **while the app has never run** — which is exactly the situation during an install. So it must be caught in `Main` **before** that line, and run inside the CLI process itself:

```csharp
if (args.Length > 0)
{
    // Install commands run locally — they must work before the app has ever started.
    if (args[0] is "install-hooks" or "uninstall-hooks")
        return Cli.HookInstaller.Run(args);

    return Cli.CliClient.Run(args);
}
```

**Therefore: do not add it to the `switch` in [`CommandExecutor.cs:38-50`](Cli/CommandExecutor.cs#L38-L50).** Everything there runs on a live app's UI thread. Create a new class, `Cli/HookInstaller.cs`.

### 3.3 Resolving paths

| What | How |
|---|---|
| The `.ps1` path | `Path.Combine(AppContext.BaseDirectory, "hooks", "sessiondeck-hook.ps1")` — works under single-file publish too (.NET 6+ returns the exe's folder there). |
| The `settings.json` path | `Path.Combine(Environment.GetFolderPath(SpecialFolder.UserProfile), ".claude", "settings.json")` — overridable with `--settings`. |

If the `.ps1` isn't found — **fail with a clear message**; never write hooks pointing at a file that doesn't exist.

### 3.4 The merge algorithm — the dangerous part

The user's `settings.json` may already have hooks of their own. **They must not be deleted.**

Use `JsonNode` (`System.Text.Json.Nodes`) rather than deserializing into a class — so unknown fields are never lost.

For each of the seven events:

1. Get (or create) the array at `hooks.<Event>`
2. **Remove** every group whose `hooks[]` contains a `command` holding the string `sessiondeck-hook.ps1` — this is what makes the command idempotent, and what handles upgrades where the path changed
3. Remove groups left empty
4. Add our group
5. If the array emptied entirely (relevant to `uninstall`) — delete the key

`uninstall-hooks` = the same thing without step 4.

### 3.5 The shape of the seven hooks

Five without a matcher — `SessionStart`, `UserPromptSubmit`, `Notification`, `Stop`, `SessionEnd`:

```json
{ "hooks": [ { "type": "command", "command": "<CMD> <EventName>" } ] }
```

Two **with** a matcher — `PreToolUse`, `PostToolUse`:

```json
{ "matcher": "AskUserQuestion|ExitPlanMode",
  "hooks": [ { "type": "command", "command": "<CMD> <EventName>" } ] }
```

where `<CMD>` is:

```
powershell -NoProfile -ExecutionPolicy Bypass -File "<resolved .ps1 path>"
```

The full, authoritative source for the hook table is [`hooks/README.md`](hooks/README.md), section "Installation". **Make sure you match it exactly**, matchers included.

### 3.6 Writing safely

1. **Back up before touching anything:** copy to `settings.json.sessiondeck-backup-<yyyyMMdd-HHmmss>`
2. **Atomic write:** the same pattern as [`ConfigStore.cs:78-80`](Services/ConfigStore.cs#L78-L80) — write to `.tmp`, then `File.Move(..., overwrite: true)`
3. **Preserve formatting:** `new JsonSerializerOptions { WriteIndented = true }`
4. **UTF-8 without BOM** — it's a JSON file, not PowerShell

### 3.7 Edge cases that must work

| Situation | Required behavior |
|---|---|
| `~/.claude/settings.json` doesn't exist | Create it (and the folder) with `{ "hooks": { ... } }` |
| The file exists but is empty / `{}` | Add the `hooks` key |
| SessionDeck hooks from an **old** path already exist | Replaced with the new ones. No duplicates. |
| **Another tool's** hooks on the same event | Kept as they are, alongside ours |
| The file is malformed JSON | **Fail without writing.** Tell the user to fix it. Do not overwrite. |
| A second run back to back | No change to the file (other than a fresh backup) |

### 3.8 Tests

Unit tests on the merge algorithm — the table in 3.7 is the list of test cases. Inject the settings path through `--settings` so they run against temporary files.

Manual end-to-end check:
```powershell
Copy-Item ~/.claude/settings.json ~/settings-real-backup.json
sessiondeck install-hooks --dry-run     # look at the output first
sessiondeck install-hooks
sessiondeck install-hooks               # again — confirm zero change
sessiondeck uninstall-hooks             # confirm a clean return to the original state
```

---

## 3b. Step 1b — two fixes the installer depends on

**Do not skip this section.** Without these two fixes the installer will run, look successful, and break two things **silently**. Both are small.

### 3b.1 — the `sessiondeck quit` command

**The problem:** replacing the exe requires stopping the running app, and there is no command for it — see the command list in [`CommandExecutor.cs:38-50`](Cli/CommandExecutor.cs#L38-L50). There's `activate`, `status`, `focus`, but no `quit`.

So `install.ps1` would have to `Stop-Process -Force`. And that destroys something real: [`MainWindow.xaml.cs:213-218`](MainWindow.xaml.cs#L213-L218) releases the **AppBar** only on the `Closing` event:

```csharp
private void OnClosing(object? sender, CancelEventArgs e)
{
    ...
    _appBar.Remove();   // ← SHAppBarMessage(ABM_REMOVE)
}
```

A forced kill skips that, and Windows' work area stays shrunk: **half the screen stays "taken" after the app is already dead**, with no explanation to the user. The kind of fault people don't know how to report.

**The fix:** add `quit` to the `switch` in [`CommandExecutor.cs`](Cli/CommandExecutor.cs) (an ordinary command that does go through the pipe — unlike `install-hooks`). It must close the window through the normal path so `OnClosing` fires. `install.ps1` will call it, and fall back to `Stop-Process -Force` only after a ~5 second timeout.

### 3b.2 — refreshing the startup path

**The problem:** [`StartupService.cs:37-38`](Services/StartupService.cs#L37-L38) writes the exe's **absolute** path into the registry:

```csharp
key.SetValue(ValueName, $"\"{exe}\"");
```

The value is only updated when the setting is toggled by hand. Once the install moves to `%LOCALAPPDATA%\Programs\SessionDeck`, the registry keeps pointing at the old path — and on the next boot Windows will launch the old build, or nothing at all if it was deleted.

**This happens on the very first install, not on an upgrade.** Eyal's current Run value points at `D:\Eyal\SessionDeck\bin\...`.

**The fix:** at startup, if `IsEnabled()` and the stored value doesn't match `Environment.ProcessPath` — rewrite it. The natural place is next to `MigrateLegacyValue()`, which [`Program.cs`](Program.cs) already calls. Add `RefreshPathIfStale()` to `StartupService` and call it from there.

---

## 4. Step 2 — `install.ps1`

A new file at the root. Small. The order of operations:

1. Requires PowerShell; does **not** require admin rights (everything is per-user)
2. Stops a running SessionDeck instance — **through `sessiondeck quit` (§3b.1), not `Stop-Process`.** Fall back to `Stop-Process -Force` only after a ~5 second timeout.
3. Copies the zip's contents to `%LOCALAPPDATA%\Programs\SessionDeck`
4. Adds that folder to the **user PATH** — only if it isn't there already
5. Installs the extension: `code --install-extension .\sessiondeck-connector-*.vsix`
   - If `code` isn't on PATH → **a warning, not a failure.** The app works without the extension; only tab activation and live tab labels won't.
6. Runs `install-hooks` from the installed path
7. Starts the app
8. Prints a summary: where it installed, what was added to PATH, which backup file was created, **and the three versions actually installed — app, extension, hooks.**

Why three versions: the three parts update independently and can drift apart. If step 5 fails quietly (no `code` on PATH), the app will work — only tab activation and tab labels break. Printing the versions turns such a mismatch into something visible instead of a mysterious bug.

**An upgrade = running the same script again.** Every step is idempotent, so there is no separate upgrade path.

A matching `uninstall.ps1` is worth having too.

---

## 5. Step 3 — building the release

```powershell
# 1. bump to 0.6.29 in SessionDeck.csproj

# 2. the app — self-contained, single file
dotnet publish -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

# 3. the extension
cd vscode-extension; npm install; npx @vscode/vsce package; cd ..

# 4. package: the publish output (including hooks\) + the vsix + install.ps1 + uninstall.ps1
#    into SessionDeck-0.6.29-win-x64.zip

# 5. publish
gh release create v0.6.29 SessionDeck-0.6.29-win-x64.zip --title "v0.6.29" --notes "..."
```

**`gh` lives at `C:\Program Files\GitHub CLI\gh.exe`** and is authenticated as `eyalBPM`. If `gh` isn't recognized in the terminal — open a new one (PATH is loaded once, when the window opens).

**Real verification:** extract the zip onto a machine or VM **with no .NET installed**, run `install.ps1`, and check that a fresh Claude Code session produces a card in SessionDeck. That is the only test that proves the packaging works.

---

## 5b. The update process

**There is no separate upgrade path.** Updating = downloading the new zip and running the same `install.ps1`. Every step is idempotent: files are overwritten, the PATH edit is a no-op, the extension updates, and `install-hooks` rewrites the paths.

**What survives an update:** all user settings — `config.json`, the workspaces, the toggles, the stage and the zone. They all live in `%APPDATA%\SessionDeck`, outside the install folder. If a future version changes the config schema, migration is the app's job at startup (there is already a precedent — `MigrateLegacyValue`).

**What won't break, because we handled it:** the AppBar (§3b.1) and the startup registration (§3b.2).

**Hooks firing mid-update** — open Claude Code sessions will keep firing hooks while the exe is being replaced. That is safe: [`sessiondeck-hook.ps1:19`](hooks/sessiondeck-hook.ps1#L19) does `exit 0` when it can't find the exe. The cards simply miss a few status updates and correct themselves on the next scan.

### What isn't solved — telling people about an update

**There is no auto-update and no notification.** Since the repo went public (2026-08-02) there is at least a public page to look at — [the Releases page](https://github.com/eyalBPM/SessionDeck/releases) — but nobody is pushed there on their own. In practice: Eyal still has to tell people by hand.

**That is a conscious decision, not an oversight.** For internal use at the scale of a few people, an update mechanism is unjustified overhead. The one cheap step that is worth it: **have the app show its version number somewhere visible** (a toolbar tooltip or the ⚙ menu), so that when someone reports a bug you can tell which version they're looking at.

Now that the repo is public, two routes have opened and are worth considering: a version check against `releases/latest` in the API (cheap, without full auto-update), and winget, which gives `winget upgrade` for free.

---

## 6. Working rules for the implementation session

- **Branch:** `feat/packaging-installer`. Do not work on `main`.
- **A version bump is mandatory** — `<Version>` in [`SessionDeck.csproj`](SessionDeck.csproj) from `0.6.28` to `0.6.29`.
- **No commit and no push without Eyal's explicit approval.**
- Temporary zip / publish files → add a pattern to `.gitignore` **before** creating them (`*.zip`, `publish/`).
- Update [`README.md`](README.md) at the end: replace the manual install section with the release instructions, and remove the line under "Known limitations" saying installation is manual.

## 7. Definition of done

- [ ] `hooks/` is copied to the build output, BOM intact
- [ ] `install-hooks` merges correctly in all seven edge cases in table 3.7
- [ ] `sessiondeck quit` closes cleanly — **and afterwards Windows' work area is back to full size** (check with an active zone: maximize a window and confirm it fills the screen)
- [ ] After installing to a new folder, the Run value in the registry points at the new exe
- [ ] `install.ps1` prints the three versions at the end
- [ ] Running `install-hooks` twice changes nothing
- [ ] `uninstall-hooks` returns the file to its original state
- [ ] `install.ps1` installs end to end on a clean machine with no .NET
- [ ] Release `v0.6.29` published with the zip
- [ ] README updated
