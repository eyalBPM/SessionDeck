# SessionDeck Connector

חלק משלב ד' של SessionDeck (ראה `../SPEC.md` §7). ה-extension:

- שולח ל-SessionDeck ‏(named pipe ‏`\\.\pipe\sessiondeck`) snapshot של נתיב ה-workspace, ה-branch הנוכחי וטאבי ה-Claude Code הפתוחים — בהפעלה ועל כל שינוי.
- מקבל מ-SessionDeck פקודות `openSession` ומפעיל/פותח מחדש את הטאב של הסשן דרך `claude-vscode.editor.open` של Claude Code (עם fallback ל-`claude --resume` בטרמינל).

## בנייה והתקנה

```powershell
cd vscode-extension
npm install
npx tsc -p ./
npx vsce package --allow-missing-repository
code --install-extension sessiondeck-connector-<version>.vsix
```

אחרי התקנה יש לבצע **Reload Window** בכל חלונות ה-VSCode. לוג: ‏Output → ערוץ "SessionDeck". סנכרון ידני: ‏Command Palette → "SessionDeck: Sync Now".
