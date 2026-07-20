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

### ⚠️ ה-hooks לא מספיקים ב-VSCode — זיהוי "ממתין" מה-transcript ‏(v0.6.17)

ב-**UI המובנה של תוסף Claude Code ב-VSCode** (להבדיל מהטרמינל) האירועים `Notification` ו-`PostToolUse` **לא נורים בפועל**. התוצאה: כשקלוד עצר והמתין לתשובה על שאלה, הכרטיס נשאר כחול "working" במקום כתום מהבהב (תקלה 2026-07-20).

לכן SessionDeck **לא מסתמך על hooks בלבד** לזיהוי המצב הזה. סורק ה-transcript (שרץ ממילא כל 10 שניות) מחפש `tool_use` **שאין לו `tool_result` תואם** — סימן בלתי-תלוי-hooks שקלוד עצר. יש שתי רמות ודאות, כי ב-transcript דיאלוג הרשאה פתוח נראה **זהה** לכלי שפשוט עדיין רץ:

| מה נמצא | ודאות | מתי נצבע כתום |
|---|---|---|
| `AskUserQuestion` / `ExitPlanMode` ללא תוצאה | ודאי — הכלי *הוא* ההמתנה | מיד |
| `Read` / `Edit` / `Write` / `Grep` / `Glob` … | היסק חזק | אחרי 15 שניות |
| `Bash` / `PowerShell` | היסק סביר | אחרי 120 שניות |
| `Agent` וכל כלי שלא ברשימה | לא ניתן להסיק | לעולם לא |

הספים נקבעו ממדידה על 11,000+ קריאות כלי אמיתיות. העמודה הקובעת היא **אחוז הקריאות הלגיטימיות שחורגות מהסף** — כלומר שיעור ההתראות השווא:

| כלי | סף | התראות שווא |
|---|---:|---:|
| `Read` / `Edit` / `Write` | 15ש' | 0.04% / 0.08% / 0.12% |
| `Bash` | 120ש' | 1.03% |
| `PowerShell` | 120ש' | 0.53% |
| `Agent` | — | 37% ב-120ש' → **מוחרג** |

‏`Agent` מוחרג בכוונה: 65% מהרצות סוכן-משנה חורגות מ-30 שניות ו-37% מ-120, כך שאין סף שהוא גם מספיק קצר כדי להועיל וגם מספיק שקט כדי לסמוך עליו. התראת שווא מתקנת את עצמה — הכרטיס חוזר לכחול ברגע שהכלי מסיים.

- הופיעה התשובה → חזרה ל-`working`; ה-`Stop` hook ‏(שכן נורה ב-VSCode) ייקח משם ל-`done`.
- שורות של סוכני-משנה (`isSidechain`) מסוננות — רק השיחה הראשית יכולה לחסום את המשתמש.
- מצב `waiting` שהגיע מ-hook אמיתי לא מנוקה על ידי הסורק — רק מצב שהסורק עצמו קבע.
- הספירה רצה מול הקריאה השמורה בזיכרון ולא מול הקובץ, כי **ה-transcript קופא כל עוד הדיאלוג פתוח** — קריאה חוזרת שלו לעולם לא הייתה מבחינה בחלוף הזמן.
- כיול ב-`%APPDATA%\SessionDeck\config.json` דרך `PermissionWaitToolSeconds` — מפה של `כלי → שניות`. רק כלים שמופיעים בה נבדקים; מפה ריקה מכבה את ההיסק לגמרי (שאלות עדיין יזוהו). להוסיף `Agent` על אחריותך.

ה-hooks עדיין מותקנים ומועילים: בטרמינל הם עובדים מלא, והם מספקים זיהוי מיידי (בלי המתנה לסריקה).

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

## מתגים מותאמים (Custom Toggles) — שליטה ב-hooks אישיים מה-toolbar

SessionDeck יכול להציג כפתורי toggle שאתה מגדיר, והמצב שלהם נכתב ל**קובץ דגל** שסקריפטים חיצוניים קוראים. כך אפשר להדליק/לכבות hook אישי (למשל התראות ntfy לטלפון) בלי שום קוד ייעודי בתוסף.

1. הגדר מתג דרך ⚙ ← **"עריכת מתגים אישיים..."** (אייקון, מזהה, תיאור, ברירת מחדל). לחלופין, ידנית ב-`%APPDATA%\SessionDeck\config.json`:

```json
"CustomToggles": [
  { "Id": "ntfy", "Icon": "🔔", "Tooltip": "התראות לטלפון", "DefaultOn": true }
]
```

2. יופיע כפתור 🔔 ב-toolbar (ליד ה-📌). כל לחיצה כותבת `1`/`0` ל-`%APPDATA%\SessionDeck\toggles\ntfy`. הקובץ שורד הפעלות מחדש ונקרא גם כשהאפליקציה סגורה (המצב האחרון).

3. חבר את ה-hook שלך למתג: הדרך הקלה — כפתור **📋** בשורת המתג (בדיאלוג העריכה) מעתיק ללוח פרומפט באנגלית; הדבק אותו אצל הסוכן שלך (Claude Code) והוא יוסיף את הבדיקה לסקריפט הנכון. ידנית — הוסף בתחילת הסקריפט:

```powershell
$flag = "$env:APPDATA\SessionDeck\toggles\ntfy"
if ((Test-Path $flag) -and ((Get-Content $flag -Raw).Trim() -eq '0')) { exit 0 }
```

- שליטה גם מ-CLI: ‏`sessiondeck toggle list` / `toggle get ntfy` / `toggle set ntfy off`.
- ‏`DefaultOn` נקבע רק בפעם הראשונה (כשאין עדיין קובץ דגל).

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
