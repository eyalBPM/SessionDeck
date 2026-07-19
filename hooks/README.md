# SessionDeck — חיבור ל-hooks של Claude Code (שלב ג')

הסקריפט `sessiondeck-hook.ps1` מתרגם אירועי hook של Claude Code לפקודות `sessiondeck session ...`, ומעביר ל-SessionDeck **את כל המידע שה-payload מספק**:

| Hook | פקודה | סטטוס | מידע נוסף שמועבר |
|------|--------|--------|--------------------|
| `SessionStart` | `session start` | idle (אפור) | ‏`cwd` (יוצר workspace אם צריך), `source` ‏(startup/resume/clear/compact) |
| `UserPromptSubmit` | `session status --state working` | כחול קבוע | ה-**prompt** עצמו (`--detail`, מקוצר ל-400 תווים) |
| `Notification` | `session status --state waiting` | כתום מהבהב | הודעת ההמתנה (`--detail` — למשל "needs your permission to use Bash") |
| `Stop` | `session status --state done` | ירוק מהבהב → קבוע בלחיצה | |
| `PreToolUse` ‏(AskUserQuestion / ExitPlanMode) | `session status --state waiting` | כתום מהבהב | טקסט השאלה / "ממתין לאישור התוכנית" — טפסי שאלות לא מפעילים Notification (תיקון 2026-07-19) |
| `PostToolUse` ‏(אותם כלים) | `session status --state working` | כחול קבוע | המשתמש ענה — Claude ממשיך לעבוד |
| `SessionEnd` | `session end` | הכרטיס נסגר | ‏`reason` ‏(clear/logout/prompt_input_exit/other) |

בנוסף, בכל אירוע מועברים (כשקיימים): `transcript_path` ו-`permission_mode`.

**איפה רואים את זה:** שורת המשנה של כרטיס הסשן מציגה את ה-detail האחרון (prompt/הודעה) כשאין description ידני; ה-tooltip מציג הכל — id, ‏detail, ‏source, ‏permission mode, ‏transcript, זמנים ו-reason.

## התקנה

1. ודא ש-`SessionDeck.exe` רץ (או נגיש ב-PATH; הסקריפט מכיר גם את נתיב ה-build בפרויקט).
2. הוסף ל-`~/.claude/settings.json` (או ל-settings של פרויקט ספציפי):

```json
{
  "hooks": {
    "SessionStart": [
      { "hooks": [ { "type": "command", "command": "powershell -NoProfile -ExecutionPolicy Bypass -File \"D:\\Eyal\\SessionDeck\\hooks\\sessiondeck-hook.ps1\" SessionStart" } ] }
    ],
    "UserPromptSubmit": [
      { "hooks": [ { "type": "command", "command": "powershell -NoProfile -ExecutionPolicy Bypass -File \"D:\\Eyal\\SessionDeck\\hooks\\sessiondeck-hook.ps1\" UserPromptSubmit" } ] }
    ],
    "Notification": [
      { "hooks": [ { "type": "command", "command": "powershell -NoProfile -ExecutionPolicy Bypass -File \"D:\\Eyal\\SessionDeck\\hooks\\sessiondeck-hook.ps1\" Notification" } ] }
    ],
    "Stop": [
      { "hooks": [ { "type": "command", "command": "powershell -NoProfile -ExecutionPolicy Bypass -File \"D:\\Eyal\\SessionDeck\\hooks\\sessiondeck-hook.ps1\" Stop" } ] }
    ],
    "SessionEnd": [
      { "hooks": [ { "type": "command", "command": "powershell -NoProfile -ExecutionPolicy Bypass -File \"D:\\Eyal\\SessionDeck\\hooks\\sessiondeck-hook.ps1\" SessionEnd" } ] }
    ],
    "PreToolUse": [
      { "matcher": "AskUserQuestion|ExitPlanMode", "hooks": [ { "type": "command", "command": "powershell -NoProfile -ExecutionPolicy Bypass -File \"D:\\Eyal\\SessionDeck\\hooks\\sessiondeck-hook.ps1\" PreToolUse" } ] }
    ],
    "PostToolUse": [
      { "matcher": "AskUserQuestion|ExitPlanMode", "hooks": [ { "type": "command", "command": "powershell -NoProfile -ExecutionPolicy Bypass -File \"D:\\Eyal\\SessionDeck\\hooks\\sessiondeck-hook.ps1\" PostToolUse" } ] }
    ]
  }
}
```

## הערות

- הסקריפט הוא fire-and-forget: כל כשל נבלע (`exit 0`) כדי לא להפריע ל-session; תואם PowerShell 5.1.
- הקובץ שמור **UTF-8 עם BOM** — חובה בגלל מחרוזות העברית (PS 5.1 קורא ‎.ps1 בלי BOM כ-ANSI ונשבר). אם עורכים — לשמור באותו encoding.
- מצב `error`: אין ל-Claude Code אירוע hook ייעודי לשגיאה (SPEC §9.2). המצב קיים ב-CLI
  (`--state error`) וניתן להפעלה מסקריפטים אחרים; `reason` של SessionEnd נשמר ומוצג.
- בדיקה ידנית בלי Claude Code:
  ```powershell
  $exe = "D:\Eyal\SessionDeck\bin\Debug\net10.0-windows\SessionDeck.exe"
  & $exe session start  --id test1 --workspace "D:\Eyal\SessionDeck" --source startup
  & $exe session status --id test1 --state working --detail "בדיקת prompt"
  & $exe session status --id test1 --state waiting --detail "Claude needs your permission"
  & $exe session status --id test1 --state done
  & $exe session end    --id test1 --reason other
  ```
