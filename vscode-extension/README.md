# SessionDeck Connector

The companion extension for [SessionDeck](https://github.com/eyalBPM/SessionDeck). It:

- Sends SessionDeck (named pipe `\\.\pipe\sessiondeck`) a snapshot of the workspace path, the current branch and the open Claude Code tabs — on startup and on every change.
- Receives `openSession` commands from SessionDeck and activates/reopens the session's tab through Claude Code's `claude-vscode.editor.open` (with a fallback to `claude --resume` in the terminal).

## Build and install

```powershell
cd vscode-extension
npm install
npx tsc -p ./
npx vsce package --allow-missing-repository
code --install-extension sessiondeck-connector-<version>.vsix
```

After installing, run **Reload Window** in every VSCode window. Log: Output → the "SessionDeck" channel. Manual sync: Command Palette → "SessionDeck: Sync Now".
