# SessionDeck — חיבור ל-hooks של Claude Code (שלב ג')

הסקריפט `sessiondeck-hook.ps1` מתרגם אירועי hook של Claude Code לפקודות `sessiondeck session ...`, ומעביר ל-SessionDeck **את כל המידע שה-payload מספק**:

| Hook | פקודה | סטטוס | מידע נוסף שמועבר |
|------|--------|--------|--------------------|
| `SessionStart` | `session start` | idle (אפור) | ‏`cwd` (יוצר workspace אם צריך), `source` ‏(startup/resume/clear/compact) |
| `UserPromptSubmit` | `session status --state working` | כחול קבוע | ה-**prompt** עצמו (`--detail`, מקוצר ל-400 תווים) |
| `Notification` | `session status --state waiting` | כתום מהבהב | הודעת ההמתנה (`--detail` — למשל "needs your permission to use Bash") |
| `PermissionRequest` | `session status --state waiting --permission-dialog` | כתום מהבהב | הכלי והארגומנט שלו (`--detail` — למשל `Write: C:\Windows\Temp\x.txt`) |
| `Stop` | `session status --state done` | ירוק מהבהב → קבוע בלחיצה | |
| `StopFailure` | `session status --state error` | אדום | הודעת השגיאה שהפילה את התור |
| `PreToolUse` ‏(AskUserQuestion / ExitPlanMode) | `session status --state waiting` | כתום מהבהב | טקסט השאלה / "ממתין לאישור התוכנית" — טפסי שאלות אינם בקשת הרשאה ולכן לא מפעילים `PermissionRequest` |
| `PostToolUse` ‏(אותם כלים) | `session status --state working` | כחול קבוע | המשתמש ענה — Claude ממשיך לעבוד |
| `Elicitation` | `session status --state waiting` | כתום מהבהב | בקשת קלט משרת MCP — חסימה אמיתית שלא מייצרת `tool_use`, ולכן הסורק עיוור אליה |
| `ElicitationResult` | `session status --state working` | כחול קבוע | המשתמש ענה לשרת ה-MCP |
| `SessionEnd` | `session end` | הכרטיס נסגר | ‏`reason` ‏(clear/logout/prompt_input_exit/other) |

בנוסף, בכל אירוע מועברים (כשקיימים): `transcript_path` ו-`permission_mode`.

מתוך **31** אירועי ה-hook שקיימים ב-Claude Code (הרשימה המחייבת היא ה-JSON schema של `settings.json` עצמו) SessionDeck רושם את 11 האלה. השאר אינם רלוונטיים למצב הסשן: או שאינם משנים אותו (`InstructionsLoaded`, ‏`MessageDisplay`, ‏`FileChanged`, ‏`ConfigChange`), או שהם מכוסים כבר בעקיפין (`PreCompact`/`PostCompact` — `SessionStart` מגיע עם `source: compact`), או שהם שייכים לזרימות שאינן בשימוש כאן (`WorktreeCreate`, ‏`TeammateIdle`, ‏`TaskCreated`).

### זיהוי "ממתין" מה-transcript — מה עוד נדרש מעבר ל-hooks

ב-**UI המובנה של תוסף Claude Code ב-VSCode** (להבדיל מהטרמינל) `Notification` **לא נורה**, ולפי אנתרופיק זו החלטה מכוונת ולא באג: הסמנטיקה שלו קשורה ל-TUI, ובמקומו ניתן `PermissionRequest`. התוצאה בזמנו הייתה שכשקלוד עצר והמתין, הכרטיס נשאר כחול "working" (תקלה 2026-07-20), ומכאן נולד סורק ה-transcript.

**עדכון 2026-08-04 (T-0318, נבדק אמפירית מול Claude Code 2.1.220):**

- `PermissionRequest` **כן נורה ב-VSCode**, ברגע שהדיאלוג נפתח, עם `tool_name` ו-`tool_input` מלאים. הוא **לא** נורה על קריאות שאושרו אוטומטית — כלומר אין ממנו התראות שווא.
- `PostToolUse` **כן נורה ב-VSCode**. הקביעה ההפוכה מ-v0.6.17 כבר אינה נכונה; היא תוקנה יחד עם `PermissionRequest`.
- ל-`PermissionRequest` **אין אירוע "resolved" מקביל** — הוא מודיע שהדיאלוג נפתח ולא שנסגר. לכן הוא נרשם עם `--permission-dialog`, ומסירת ה-`waiting` מוחזרת לסורק.
- ⚠️ **הדגל לא מסמן `WaitingFromTranscript` ישירות** (ניסיון כזה ב-v0.8.0 יצר הבהוב כתום→כחול→כתום). ה-`tool_use` אמנם נכתב ל-**קובץ** כ-0.5 שנייה לפני שה-hook נורה, אבל מה שקובע הוא מתי SessionDeck **סרק** אותו — והסריקה מונעת מ-mtime של ה-transcript, שמפסיק לגדול בדיוק כל עוד הדיאלוג פתוח. לכן קיים `PermissionDialogOpen`: הוא מחזיק את ה-`waiting` עד שהסורק באמת רואה את הקריאה, ורק אז מוסר לו בעלות. קריאה של סוכן-משנה מסוננת (`isSidechain`) ולעולם לא תיראה — המתנה כזו נשארת עד ה-`Stop`.

**מה זה משנה בחלוקת העבודה:** ה-hook נותן את הקצה הנכנס — מיידי וּודאי, לכל כלי, כולל כאלה שאינם בטבלת הכיול. הסורק נותן את הקצה היוצא — הוא היחיד שרואה את ה-`tool_result` מגיע. הספים למטה ירדו מתפקיד האיתור הראשי לתפקיד **רשת ביטחון** (תוסף ישן, טרמינל, hook מכובה); כיול מחדש שלהם לאור זה טרם נעשה.

הסורק (שרץ ממילא כל 10 שניות) מחפש `tool_use` **שאין לו `tool_result` תואם** — סימן בלתי-תלוי-hooks שקלוד עצר. יש שתי רמות ודאות, כי ב-transcript דיאלוג הרשאה פתוח נראה **זהה** לכלי שפשוט עדיין רץ:

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
- מצב `waiting` שהגיע מ-hook לא מנוקה על ידי הסורק — למעט `PermissionRequest`, שמבקש זאת במפורש דרך `--permission-dialog` כי אין לו hook שסוגר אותו.
- הספירה רצה מול הקריאה השמורה בזיכרון ולא מול הקובץ, כי **ה-transcript קופא כל עוד הדיאלוג פתוח** — קריאה חוזרת שלו לעולם לא הייתה מבחינה בחלוף הזמן.
- כיול ב-`%APPDATA%\SessionDeck\config.json` דרך `PermissionWaitToolSeconds` — מפה של `כלי → שניות`. רק כלים שמופיעים בה נבדקים; מפה ריקה מכבה את ההיסק לגמרי (שאלות עדיין יזוהו). להוסיף `Agent` על אחריותך.

ה-hooks עדיין מותקנים ומועילים: בטרמינל הם עובדים מלא, והם מספקים זיהוי מיידי (בלי המתנה לסריקה).

**איפה רואים את זה:** שורת המשנה של כרטיס הסשן מציגה את ה-detail האחרון (prompt/הודעה) כשאין description ידני; ה-tooltip מציג הכל — id, ‏detail, ‏source, ‏permission mode, ‏transcript, זמנים ו-reason.

## התקנה

**הדרך המומלצת (v0.6.29+):** `sessiondeck install-hooks` — ממזג את 11 ה-hooks לתוך `~/.claude/settings.json` עם הנתיב האמיתי של ההתקנה, אחרי גיבוי. אידמפוטנטי; `sessiondeck uninstall-hooks` מסיר. תומך ב-`--settings <path>` (למשל settings של פרויקט ספציפי) וב-`--dry-run`.

**התקנה ידנית (reference):** הוסף ל-`~/.claude/settings.json` — החלף את `D:\Eyal\SessionDeck\hooks` בנתיב האמיתי של הסקריפט אצלך:

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
    "PermissionRequest": [
      { "hooks": [ { "type": "command", "command": "powershell -NoProfile -ExecutionPolicy Bypass -File \"D:\\Eyal\\SessionDeck\\hooks\\sessiondeck-hook.ps1\" PermissionRequest" } ] }
    ],
    "Stop": [
      { "hooks": [ { "type": "command", "command": "powershell -NoProfile -ExecutionPolicy Bypass -File \"D:\\Eyal\\SessionDeck\\hooks\\sessiondeck-hook.ps1\" Stop" } ] }
    ],
    "StopFailure": [
      { "hooks": [ { "type": "command", "command": "powershell -NoProfile -ExecutionPolicy Bypass -File \"D:\\Eyal\\SessionDeck\\hooks\\sessiondeck-hook.ps1\" StopFailure" } ] }
    ],
    "SessionEnd": [
      { "hooks": [ { "type": "command", "command": "powershell -NoProfile -ExecutionPolicy Bypass -File \"D:\\Eyal\\SessionDeck\\hooks\\sessiondeck-hook.ps1\" SessionEnd" } ] }
    ],
    "PreToolUse": [
      { "matcher": "AskUserQuestion|ExitPlanMode", "hooks": [ { "type": "command", "command": "powershell -NoProfile -ExecutionPolicy Bypass -File \"D:\\Eyal\\SessionDeck\\hooks\\sessiondeck-hook.ps1\" PreToolUse" } ] }
    ],
    "PostToolUse": [
      { "matcher": "AskUserQuestion|ExitPlanMode", "hooks": [ { "type": "command", "command": "powershell -NoProfile -ExecutionPolicy Bypass -File \"D:\\Eyal\\SessionDeck\\hooks\\sessiondeck-hook.ps1\" PostToolUse" } ] }
    ],
    "Elicitation": [
      { "hooks": [ { "type": "command", "command": "powershell -NoProfile -ExecutionPolicy Bypass -File \"D:\\Eyal\\SessionDeck\\hooks\\sessiondeck-hook.ps1\" Elicitation" } ] }
    ],
    "ElicitationResult": [
      { "hooks": [ { "type": "command", "command": "powershell -NoProfile -ExecutionPolicy Bypass -File \"D:\\Eyal\\SessionDeck\\hooks\\sessiondeck-hook.ps1\" ElicitationResult" } ] }
    ]
  }
}
```

> **למה לא `PostToolUse` על כל הכלים?** הוא היה סוגר את ה-`waiting` של `PermissionRequest` ישירות, אבל במחיר הרצת תהליך PowerShell **בכל קריאת כלי** — עלות קבועה על כל סשן, גם כשאין שום דיאלוג. הסורק כבר עושה את אותה עבודה בעלות אפס, וההשהיה (עד 10 שניות) נופלת על הקצה הלא-מזיק: חזרה לכחול, לא ההתראה עצמה.

## מתגים (Flags) — שליטה בתהליכים חיצוניים מה-toolbar

SessionDeck מאפשר להגדיר מתגים שמשמשים כ-**flags לתהליכים חיצוניים**. הוא לא יודע ולא מתעניין במה שהמתג מפעיל — הוא רק מנהל את הדגל: מציג כפתור ב-toolbar וכותב את מצבו לקובץ שכל תהליך יכול לקרוא. ה-hook של Claude Code הוא רק דוגמה אחת לצרכן כזה.

1. הגדר מתג דרך ⚙ ← **"מתגים (Flags)..."**: אייקון, **מזהה (id)**, שם וברירת מחדל.
   - ה-**id** הוא שם קובץ ה-flag ולכן **נעול אחרי היצירה** — שינוי שם התצוגה לא מזיז את הנתיב שתהליכים חיצוניים כבר מסתמכים עליו.
2. כל לחיצה על הכפתור כותבת `1` (דלוק) או `0` (כבוי) ל-`%APPDATA%\SessionDeck\toggles\<id>`. הקובץ שורד הפעלות מחדש ונקרא גם כשהאפליקציה סגורה. קובץ חסר = דלוק.
3. כפתור **ℹ** בשורת המתג פותח עמוד פרטים עם כל מה שצריך כדי לחבר תהליך — id, נתיב מלא, מצב נוכחי, פקודות CLI, קטע בדיקה ב-PowerShell, ופרומפט מוכן להדבקה אצל סוכן AI. לכל שדה יש כפתור העתקה.

בדיקת הדגל בתהליך חיצוני (PowerShell):

```powershell
$flag = "$env:APPDATA\SessionDeck\toggles\<id>"
if ((Test-Path $flag) -and ((Get-Content $flag -Raw).Trim() -eq '0')) { exit 0 }
```

- שליטה גם מ-CLI: ‏`sessiondeck toggle list` / `toggle get <id>` / `toggle set <id> off`.
- ברירת המחדל נקבעת רק בפעם הראשונה (כשאין עדיין קובץ flag).

## הערות

- הסקריפט הוא fire-and-forget: כל כשל נבלע (`exit 0`) כדי לא להפריע ל-session; תואם PowerShell 5.1.
- הקובץ שמור **UTF-8 עם BOM** — חובה בגלל מחרוזות העברית (PS 5.1 קורא ‎.ps1 בלי BOM כ-ANSI ונשבר). אם עורכים — לשמור באותו encoding.
- מצב `error`: מאז `StopFailure` יש לו hook ייעודי (עדכון ל-SPEC §9.2, שנכתב כשלא היה).
  המצב עדיין זמין גם ב-CLI (`--state error`) לסקריפטים אחרים; `reason` של SessionEnd נשמר ומוצג.
- בדיקה ידנית בלי Claude Code:
  ```powershell
  $exe = "D:\Eyal\SessionDeck\bin\Debug\net10.0-windows\SessionDeck.exe"
  & $exe session start  --id test1 --workspace "D:\Eyal\SessionDeck" --source startup
  & $exe session status --id test1 --state working --detail "בדיקת prompt"
  & $exe session status --id test1 --state waiting --detail "Claude needs your permission"
  & $exe session status --id test1 --state done
  & $exe session end    --id test1 --reason other
  ```
