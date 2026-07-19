# SessionDeck (לשעבר WinGrid) — אפיון פיתוח (v0.6)

> Last updated: 2026-07-17
> Status: **שלבים א'–ג' ממומשים** (v0.4.0, 2026-07-17; ממתין לבדיקות ידניות — `MANUAL_TESTS.md`); rename ל-SessionDeck בוצע (v0.3.0); שלב ד' (VSCode extension) באפיון.
> שם: **SessionDeck** (נבחר 2026-07-17 — החלטה 20). ה-rename הושלם במלואו: קוד 2026-07-17 (csproj/slnx, namespaces, pipe `sessiondeck`, mutex, ‏`%APPDATA%\SessionDeck` + מיגרציה, ערך Run + מיגרציה), ושם התיקייה `D:\Eyal\SessionDeck` ‏(2026-07-19, ע"י אייל).
> מחבר האפיון: Claude Code בשיתוף אייל, סשנים 2026-07-16 – 2026-07-17.

---

## 1. רקע ומטרה

אפליקציית Windows כללית לניטור ושליטה בחלונות: לוח (grid) של "אריחים" (tiles), כל אריח מציג **תצוגה חיה** של חלון OS נבחר, עם כותרת, תיאור ומסגרת צבעונית. לחיצה מקפיצה/מפעילה את החלון האמיתי. האפליקציה נשלטת גם מ-CLI.

**מטרת-על (זכורה, אך האפיון נשאר גנרי):** שליטה בכל ה-sessions של Claude Code שרצים במחשב (כל session בחלון VSCode). בשלב 2, hooks של Claude Code יקראו ל-CLI כדי לעדכן צבע מסגרת לפי סטטוס העבודה של כל session (למשל: עובד = ירוק, ממתין לאינפוט = מהבהב כתום/שחור).

**עדכון אג'נדה (2026-07-17, v0.5 — החלטה 15):** האפליקציה ממוקדת מעתה **במפורש בסשני Claude Code ב-VSCode** — לוח בקרה שמציג כל חלון VSCode ככרטיס, ואת הסשנים (טאבים) שבתוכו כתת-כרטיסים עם סטטוס חי מה-hooks. היכולת הגנרית (ניטור כל חלון OS) נשמרת במנוע מתחת למכסה, אבל ה-UI וה-flows מסוננים ל-VSCode בלבד (החלטה 13). המודל המלא — §2ב. שלבים א'–ב' (שכבר ממומשים) הם התשתית: thumbnails, focus/pin, zone, CLI, persistence, blink — הכל משרת את המבנה החדש.

---

## 2. מושגי יסוד

| מושג | הגדרה |
|------|--------|
| **Tile** | ריבוע ב-grid המייצג חלון OS אחד (HWND top-level): תצוגה חיה + כותרת + תיאור + מסגרת צבעונית. |
| **Stage** | אזור יעד גלובלי מוגדר-מראש (מסך שלם / חצי מסך / מלבן), שאליו "קופץ" חלון בפעולת **Pin**. |
| **Reserved Zone** | שטח המסך שהאפליקציה עצמה תופסת (מסך מלא או חצי מסך). מנוכה מה-work area של Windows — חלונות אחרים (כולל maximized) לא נכנסים אליו; העכבר חופשי לנוע לשם. |
| **Matcher** | כלל שיוך persistent של tile לחלון: process name + title pattern (regex), לצורך re-bind אחרי הפעלה מחדש. |
| **Window Card** | (v0.5) כרטיס לכל חלון VSCode: תצוגה חיה + פרטי חלון + כפתור Focus + כפתור הרחבה. גלגול של ה-Tile הקיים. |
| **Session Card** | (v0.5) תת-כרטיס בתוך Window Card, מייצג סשן Claude Code (טאב): שם, סטטוס, מסגרת צבע לפי סטטוס. ללא thumbnail. |

---

## 2ב. מודל Cards וסטטוסים (v0.5 — האג'נדה החדשה)

### מבנה ה-UI
- **Window Card** לכל חלון VSCode: תצוגה חיה (DWM thumbnail) למעלה, פרטי החלון (workspace, process), כפתור פתיחה (Focus), וכפתור הרחבה.
- **Session Cards** מתחת ל-thumbnail: אחד לכל סשן Claude Code פעיל באותו חלון — שם הסשן, סטטוס טקסטואלי, ו**מסגרת צבעונית לפי סטטוס** (הצבעים וההבהוב חיים כאן, לא ברמת החלון). ללא thumbnail — טאב לא-אקטיבי אינו מרונדר ע"י VSCode/DWM, אין פיקסלים להציג (מגבלה טכנית מוחלטת).
- **הרחבה**: לחיצה על כפתור ההרחבה מציגה גם סשנים **סגורים** של אותו workspace, עם אפשרות פתיחה מחדש (resume — המימוש בשלב ד'). retention לסשנים סגורים: ~20 אחרונים ל-workspace (config).

### סטטוסים (החלטה 11)
| סטטוס | מסגרת | מעברים |
|--------|--------|---------|
| `idle` | אפורה קבועה | סשן קיים ולא עובד (אחרי SessionStart, לפני prompt ראשון) |
| `working` | כחולה קבועה | עד האירוע הבא |
| `waiting` | כתומה מהבהבת | ממתין ל-permission/אינפוט; עד לחיצה או אירוע הבא |
| `done` | ירוקה מהבהבת → קבועה | מהבהבת עד **acknowledge** (לחיצת המשתמש) → ירוקה קבועה |
| `error` | אדומה מהבהבת → קבועה | כנ"ל — מהבהבת עד acknowledge → אדומה קבועה |

- **לחיצה על Session Card** = Focus לחלון + acknowledge (עצירת הבהוב done/error). בשלב ד': גם הפעלת הטאב הספציפי, ו-auto-acknowledge כשהמשתמש פותח את הטאב ישירות ב-VSCode.
- **מיפוי סטטוס→צבע/הבהוב יושב ב-config** — ניתן לשינוי בלי לגעת ב-hooks או בקוד.
- הערה: ל-hooks של Claude Code אין אירוע error ייעודי; מצב `error` קיים במודל וב-CLI, והמיפוי מאירועים בפועל ייקבע בשלב ג' לפי מה שה-hooks מספקים.

### מחזור חיים של סשן (החלטה 12)
- SessionStart (hook) → נוצר Session Card תחת ה-Window Card של ה-workspace (נוצר Window Card אם אין).
- SessionEnd (hook) → הכרטיס נעלם מהתצוגה הרגילה; נשמר ומוצג בתצוגה המורחבת.

### ניהול Workspaces (נוסף 2026-07-17 — החלטות 16–18)
- **Workspace = ישות persistent**: נזכר גם כשאין חלון VSCode פתוח (מקביל ל-disconnected tile היום). ה-Window Card הופך בפועל ל-**Workspace Card** — החלון הוא רק ה-binding החי שלו.
- **מיון**: workspaces פעילים (חלון פתוח / סשן חי) עולים למעלה; ישנים ניתנים **להסתרה** ומוצגים דרך כפתור/פילטר "הצג מוסתרים".
- **פריסה**: כרטיסים רחבים, ירידות שורה לפי רוחב, **גודל מינימום** לכרטיס, והכל בתוך **אזור גלילה** אנכי (ה-grid כבר לא חייב להיכנס כולו למסך).
- **תוכן הכרטיס הראשי**: שם הפרויקט, ה-**branch הנוכחי** של git, תצוגה חיה, ותת-כרטיסי הסשנים. **Custom title + description** נתמכים גם בכרטיס הראשי וגם בתת-הכרטיסים.
- **צבע הכרטיס הראשי**: נלקח אוטומטית מהגדרות ה-workspace של VSCode (`.vscode/settings.json` — ‏`peacock.color` או `workbench.colorCustomizations.titleBar.activeBackground`) כשקיים — אינטגרציה טבעית למי שעובד עם Peacock; אחרת צבע ידני/ברירת מחדל.
- **הוספת workspace ל-deck (החלטה 21)** — לפי סדר העדיפות:
  1. **בחירת תיקייה** (primary, שלב ג'): דיאלוג בחירת תיקייה כמו פתיחת פרויקט ב-VSCode. הנתיב ידוע מיידית → צבע Peacock + branch זמינים עוד לפני שנפתח חלון או רץ סשן.
  2. **דיווח מה-VSCode extension** (שלב ד'): ה-extension מדווח על ה-workspaces הפתוחים ומוסיף אוטומטית.
  3. **גרירת חלון (drag-in)** — נשארת כערוץ משני: נחסמת אם החלון אינו VSCode או אם ה-workspace כבר קיים ב-deck (וכשה-extension פעיל הוא ממילא כבר יהיה קיים).
  4. **hook `cwd`** — רשת ביטחון: סשן שמדווח על workspace שלא קיים ב-deck יוצר אותו אוטומטית עם הנתיב מה-cwd.
- **הערה טכנית**: קריאת settings.json וה-branch (`.git/HEAD`) דורשת את **נתיב** ה-workspace — מובטח מזרימות 1/2/4 (בחירת תיקייה, extension, hook cwd). הנתיב נשמר ב-config לתמיד.

---

## 3. דרישות פונקציונליות

### F1 — Grid חי
- תצוגה חיה של כל חלון באמצעות **DWM Thumbnail API** (`DwmRegisterThumbnail`) — רינדור ע"י ה-compositor של Windows, עלות CPU אפסית, בלי הזרקה או צילום של חלונות המקור.
- פריסה **אוטומטית** (הוחלט 2026-07-16): מספר עמודות מחושב לפי כמות ה-tiles וגודל אזור התצוגה; כל ה-tiles באותו גודל; שינוי סדר בגרירה בתוך ה-grid.
- Aspect ratio של כל thumbnail נשמר לפי יחס החלון המקורי (letterboxing בתוך ה-tile).

### F2 — אנטומיית Tile
- **כותרת**: ברירת מחדל = ה-window title מ-Windows (`GetWindowText`); ניתנת לעריכה ידנית. כל עוד לא נערכה ידנית — מתעדכנת אוטומטית כשהחלון משנה title.
- **תיאור**: ריק כברירת מחדל; ניתן לעריכה (UI + CLI).
- **מסגרת צבעונית**: צבע סטטי, או **מצב מהבהב** — התחלפות בין שני צבעים (למשל כתום/שחור) בקצב מוגדר (ברירת מחדל 500ms).
- אינדיקציה נוספת על ה-tile: process name + אייקון החלון (nice-to-have).

### F3 — הפעלה וקיבוע (הוחלט 2026-07-16: שני מצבים)
- **Focus (הפעלה במקום)**: לחיצה רגילה על tile → החלון האמיתי מובא לחזית ומופעל (`SetForegroundWindow`) **במיקומו הנוכחי**.
- **Pin (קיבוע ל-Stage)**: פעולה נפרדת (כפתור על ה-tile / double-click / CLI) → החלון האמיתי מוזז ל-Stage (`SetWindowPos`) + מופעל.
- הגדרת ה-Stage: דרך UI ("קבע Stage") ודרך CLI — מסך + חצי (left/right) / full / מלבן מותאם.

### F4 — Reserved Zone
- האפליקציה יכולה "להשתלט" על מסך מלא או חצי מסך באמצעות **AppBar API** (`SHAppBarMessage` — ABM_NEW / ABM_SETPOS), per-monitor.
- כשה-zone פעיל, ה-work area מצטמצם: חלונות maximized ואף snap לא נכנסים לשטח; מעבר עכבר חופשי.
- מצבים: `off` (חלון רגיל) / `half-left` / `half-right` / `full`, לכל מסך.

### F5 — הוספת חלונות (הוחלט 2026-07-16: Picker + גרירה)
1. **Picker** (MVP): כפתור "בחר חלון" בסגנון crosshair (כמו Spy++) — גוררים את הסמן אל החלון הרצוי ומשחררים.
2. **Drag-in** (שלב ב'): גרירת title bar של חלון אמיתי ושחרורו מעל האפליקציה → נוסף tile. מימוש: low-level mouse hook גלובלי שמזהה סיום move-drag של חלון זר מעל אזור האפליקציה.
3. **CLI**: `sessiondeck add ...` (ראה §4).
- חלונות מסוננים מה-picker: חלונות של SessionDeck עצמו, חלונות ללא title, tool windows.

### F6 — ניהול Tiles (עודכן 2026-07-16)
- הסרה (UI + CLI), עריכת כותרת/תיאור/צבע (UI + CLI), שינוי סדר בגרירה.
- לכל tile יש שדה **state**: `connected` / `disconnected`. ה-state מוצג כאינדיקציית סטטוס על ה-tile (למשל אייקון/תג + placeholder במקום ה-thumbnail) — **הכותרת, התיאור והצבע לא משתנים בגלל ניתוק** (הוחלט 2026-07-16). ה-state מופיע גם ב-`sessiondeck list`.
- **חלון שנסגר**: ה-tile עובר ל-`disconnected` ולא נמחק; כשנפתח חלון חדש שתואם ל-Matcher — re-bind אוטומטי וה-tile חוזר ל-`connected`. הסרה אוטומטית = אופציה בהגדרות (כבויה כברירת מחדל).

### F7 — Persistence + Auto-Save (עודכן 2026-07-16)
- פרופיל JSON ב-`%APPDATA%\SessionDeck\config.json` (מיגרציה אוטומטית חד-פעמית מ-`%APPDATA%\WinGrid` הישן): רשימת tiles (Matcher, כותרת ידנית, תיאור, צבע/הבהוב), מצב zone, הגדרת Stage, סדר, מיקום/גודל החלון הראשי.
- **Auto-save תמידי**: כל שינוי (הוספה/הסרה/עריכה/צבע/סדר/zone/stage) נשמר מיידית (debounce ~1s, כתיבה אטומית: temp file + rename). אין כפתור "שמור" ואין מצב לא-שמור.
- בהפעלה: טעינת הפרופיל, enumerate של חלונות קיימים + re-bind לפי Matchers; tiles שחלונם טרם נפתח מוצגים "מנותקים" (אפור) עד שיופיע חלון תואם.

### F8 — CLI (ראה §4)
- כל יכולות הניהול זמינות מ-CLI, לצורך אוטומציה (ובפרט hooks של Claude Code בשלב 2).

### F9 — עלייה אוטומטית עם Windows (נוסף 2026-07-16)
- SessionDeck נרשם ל-startup של המשתמש (registry `HKCU\...\Run`, ללא admin; ניתן לכיבוי בהגדרות).
- בעלייה משוחזר **המצב המלא** מהפרופיל: כל ה-tiles (דרך Matchers — אחרי ריסטארט כל החלונות מקבלים HWND חדש, ולכן השחזור מבוסס re-bind), מצב ה-Reserved Zone, הגדרת ה-Stage, וסדר ה-grid.
- tiles שחלונם עוד לא נפתח אחרי הריסטארט מופיעים מנותקים ומתחברים אוטומטית ברגע שהחלון התואם נפתח.

---

## 4. CLI — פירוט

### מודל הפעלה
- **Singleton**: ההרצה הראשונה מרימה את ה-UI + שרת **named pipe** (`\\.\pipe\sessiondeck`).
- הרצה חוזרת עם ארגומנטים = CLI client: מעביר את הפקודה ל-pipe, מדפיס תשובה, מחזיר exit code (0 = הצלחה, ≠0 = שגיאה + הודעה ל-stderr). זמן ריצה יעד: <100ms (קריטי ל-hooks).
- אם אין instance רץ: פקודות CLI נכשלות עם הודעה ברורה (אופציה עתידית: `--start` שמרים את ה-UI).

### פקודות (עודכן v0.4.0 — מודל workspaces; פקודות ה-tiles הישנות `border`/`add --match` הוסרו)

```
sessiondeck list [--all]                  # workspaces + sessions (--all כולל סגורים)
sessiondeck add <folder path>             # הוספת workspace לפי תיקייה (המקביל ל-picker ב-UI)
sessiondeck remove <target>
sessiondeck set <target> [--title "..."] [--desc "..."] [--color <c>]   # ערך ריק = auto
sessiondeck focus <target>                # הפעלת חלון ה-workspace במקומו
sessiondeck pin <target>                  # הקפצה ל-Stage + הפעלה
sessiondeck stage --monitor <n> --half left|right | --full | --rect x,y,w,h
sessiondeck zone --monitor <n> --half left|right | --full | --off
sessiondeck status                        # מצב: version, zone, stage, מוני workspaces/sessions
```

- **`<target>`**: id מספרי יציב של workspace, או `--match "<regex>"` על השם/כותרת.
- **צבעים**: שמות (`red`, `green`, `orange`, `blue`, `gray`, ...) או hex `#RRGGBB`.
- צבעי המסגרות של הסשנים נגזרים מהסטטוס (מיפוי `StatusStyles` ב-config) — אין יותר פקודת `border`.

### 4ב. CLI לסשנים (ממומש v0.4.0 — שלב ג')

```
sessiondeck session start  --id <session_id> --workspace <name> [--title "..."]
sessiondeck session status --id <session_id> --state working|waiting|done|error|idle
sessiondeck session end    --id <session_id>
sessiondeck session list   [--workspace <name>] [--all]     # --all כולל סגורים
```

- `session_id` מגיע מה-hook של Claude Code; ה-workspace משמש למיפוי לחלון (לפי title).
- הפקודות נקראות מסקריפט ה-hooks (שלב ג') — ולכן חייבות להישאר מהירות (<100ms) ואטומיות.

---

## 5. ארכיטקטורה (הוחלט 2026-07-16: C# / .NET + WPF)

| רכיב | תפקיד | טכנולוגיה |
|------|-------|-----------|
| **UI Shell** | חלון ראשי, grid, עריכת tiles, picker | WPF (.NET 10 LTS) |
| **Thumbnail Host** | אירוח DWM thumbnails בתוך ה-grid | `DwmRegisterThumbnail` / `DwmUpdateThumbnailProperties`, interop דרך HwndHost |
| **Window Tracker** | מעקב אחרי חלונות: title change, destroy, create (ל-re-bind) | `SetWinEventHook` (EVENT_OBJECT_NAMECHANGE / DESTROY / CREATE) — בלי polling |
| **AppBar Module** | Reserved Zone | `SHAppBarMessage` |
| **Pipe Server** | קבלת פקודות CLI | NamedPipeServerStream, פרוטוקול JSON פשוט |
| **CLI Client** | אותו exe עם args → pipe | System.CommandLine (או פרסור ידני) |
| **Config Store** | persistence | JSON ב-%APPDATA% |
| **Blink Engine** | טיימר משותף לכל המסגרות המהבהבות | DispatcherTimer אחד (לא טיימר פר-tile) |

- ללא הרשאות admin; ללא הזרקת קוד לחלונות זרים.
- Per-Monitor DPI awareness (v2) מהיום הראשון — סביבת multi-monitor עם DPI שונה היא תרחיש היעד.

---

## 6. אילוצים וסיכונים טכניים

1. **חלון ממוזער** — DWM thumbnail מקפיא את הפריים האחרון. המלצה: להשאיר חלונות restored (מאחורי חלונות אחרים זה בסדר — ה-thumbnail חי גם כשהחלון מכוסה). אופציה עתידית: מניעת minimize לחלונות במעקב.
2. **SetForegroundWindow restrictions** — כשמשתמש לוחץ ב-UI שלנו, אנחנו ה-foreground process ומותר להעביר פוקוס. אבל `sessiondeck focus` שמגיע מתהליך רקע (hook) עלול להיחסם ע"י Windows (anti focus-stealing). לא קריטי: שלב 2 משנה רק צבעים. אם יידרש — יש workarounds מוכרים (AttachThreadInput וכו').
3. **גרנולריות = חלון OS** — כמה sessions של Claude Code באותו חלון VSCode לא ניתנים להבחנה. מוסכמת עבודה: **session אחד = חלון VSCode אחד**.
4. **Drag-in דורש global low-level mouse hook** — רכיב רגיש (ביצועים/יציבות); לכן Picker הוא המנגנון העיקרי, drag-in נדחה לשלב ב'.
5. **חלונות elevated (admin)** — thumbnail יעבוד, אבל move/activate ייחסמו אם SessionDeck לא elevated. מתועד כמגבלה ידועה.
6. **זיהוי לפי title** — title של VSCode משתנה עם הקובץ הפתוח (`file — workspace — Visual Studio Code`). ה-Matcher חייב regex על שם ה-workspace, לא השוואה מלאה.

---

## 7. שלבי פיתוח

### שלב א' — MVP
- Grid אוטומטי + DWM thumbnails חיים
- **Reserved Zone (AppBar) — full / half, per-monitor** (הוקדם מ-שלב ב' בהחלטת אייל 2026-07-16 — חלק מהותי מהאפליקציה)
- הוספה ב-Picker + הסרה
- כותרת (auto מ-Windows) + מסגרת בצבע סטטי
- Focus (לחיצה) + Pin ל-Stage בסיסי
- CLI: `list`, `add --match`, `remove`, `border` (סטטי), `focus`, `pin`, `zone`
- Persistence עם auto-save (F7)

### שלב ב' — השלמת הכלי הגנרי
- מסגרת מהבהבת + Blink Engine
- Drag-in (mouse hook)
- תיאורים + עריכה מלאה ב-UI
- Disconnected tiles + re-bind אוטומטי
- עלייה אוטומטית עם Windows + שחזור מצב מלא (F9 — תלוי ב-re-bind)
- `stage` / `set` / `status` ב-CLI

### שלב ג' — Cards UI + אינטגרציית hooks (עודכן v0.5; ללא extension) — **ממומש v0.4.0 (2026-07-17)**
- מבנה UI חדש: Window Cards + Session Cards לפי §2ב; סינון התצוגה ל-VSCode בלבד (המנוע נשאר גנרי)
- CLI סשנים לפי §4ב; הסשנים נוצרים ומתעדכנים **מה-hooks בלבד**
- מנוע סטטוסים + acknowledge בלחיצה; מיפוי סטטוס→צבע ב-config
- סקריפט hooks (PowerShell) שנרשם ב-settings של Claude Code:
  - `SessionStart` → `session start` (idle)
  - `UserPromptSubmit` → working
  - `Notification` (ממתין ל-permission/אינפוט) → waiting
  - `Stop` (סיים turn) → done
  - `SessionEnd` → `session end`
- לחיצה על Session Card בשלב זה: Focus לחלון בלבד (הפעלת הטאב הספציפי — שלב ד')

**הערות מימוש (v0.4.0):** ה-Tile הגנרי הוחלף ב-WorkspaceCardView (המנוע — thumbnails, binding, zone/stage — נשמר); ה-crosshair picker הוסר מהסרגל לטובת בחירת תיקייה; נתוני ה-tiles משלבים א'-ב' נשמרים ב-config כשדה legacy ואינם מוצגים; רענון branch/Peacock כל ~10 שניות; סקריפט ה-hooks ב-`hooks/` עם הוראות התקנה.
- מיפוי session→window לפי workspace name ב-title של VSCode

### שלב ד' — VSCode Extension (נוסף v0.5)
- **Spike ראשון**: קורלציה `session_id` ↔ טאב (tabGroups API) — הסיכון הטכני המרכזי
- סנכרון רשימת טאבים מה-extension ל-SessionDeck (דרך ה-pipe הקיים)
- לחיצה על Session Card → הפעלת הטאב הספציפי ב-VSCode
- auto-acknowledge כשהמשתמש פותח את הטאב ישירות ב-VSCode (בלי לחיצה באפליקציה)
- פתיחה מחדש (resume) של סשן סגור מהתצוגה המורחבת

---

## 8. החלטות שהתקבלו (2026-07-16)

| # | החלטה | בחירה |
|---|--------|-------|
| 1 | יעד קיבוע | **שני מצבים**: Focus במקום הנוכחי (לחיצה) + Pin ל-Stage גלובלי (פעולה נפרדת) |
| 2 | פריסת grid | אוטומטית, tiles אחידים, שינוי סדר בגרירה |
| 3 | סטאק | C# / .NET 10 (LTS) + WPF — עודכן מ-.NET 8 כי תמיכתו מסתיימת 11/2026 |
| 4 | הוספת חלון | Picker (MVP) + Drag-in (שלב ב') + CLI |
| 5 | שם | WinGrid — סופי |
| 6 | Auto-save | תמידי, ללא כפתור שמירה (F7) |
| 7 | Startup | עלייה אוטומטית עם Windows + שחזור מצב מלא דרך re-bind (F9) |
| 8 | חלון שנסגר | tile נשאר עם title/desc/color ללא שינוי; נוסף שדה **state** (connected/disconnected) + re-bind אוטומטי |
| 9 | מיקום ה-repo | **`D:\Eyal\SessionDeck`** (במקור `D:\Eyal\WinGrid`; שם התיקייה שונה 2026-07-19 עם ה-rename) — repo git עצמאי, מחוץ לעץ ה-OneDrive. הערה: תחת `D:\Eyal\` חל סיווג "פרויקט אישי" בהגדרות של אייל — פרוטוקולי BPM לא חלים, כללי Git/safety כלליים כן |
| 10 | Reserved Zone | **בשלב א' (MVP)** — חלק מהותי מהאפליקציה (החלטת אייל 2026-07-16) |
| 11 | סכמת סטטוסים (2026-07-17, עודכן) | **working=כחול קבוע** (הוכרע — כתום שמור בלעדית ל-waiting), waiting=כתום מהבהב, done=ירוק מהבהב→קבוע ב-acknowledge, error=אדום מהבהב→קבוע, idle=אפור; המיפוי ב-config |
| 12 | סשן שנסגר (2026-07-17) | ה-Session Card נעלם; זמין בתצוגה מורחבת של ה-Window Card עם אפשרות resume (שלב ד'); retention ~20 |
| 13 | סקופ תצוגה (2026-07-17) | VSCode בלבד ב-UI; המנוע נשאר גנרי. תמיכה ב-terminals — אולי בעתיד, לא עכשיו |
| 14 | שם (2026-07-17) | ~~נשאר WinGrid בינתיים~~ → הוחלף בהחלטה 19 |
| 15 | אג'נדה v0.5 (2026-07-17) | האפליקציה = לוח בקרה לסשני Claude Code ב-VSCode; מבנה Cards לפי §2ב; שלבים ג'–ד' הוגדרו מחדש |
| 16 | ניהול workspaces (2026-07-17) | workspace = ישות persistent; פעילים למעלה; הסתרת ישנים; כרטיסים רחבים עם min-size באזור גלילה |
| 17 | תוכן כרטיס ראשי (2026-07-17) | שם פרויקט + branch נוכחי; custom title/description בשתי רמות הכרטיסים |
| 18 | צבע כרטיס מ-VSCode (2026-07-17) | נלקח מ-settings.json של ה-workspace ‏(Peacock / titleBar.activeBackground) כשקיים |
| 19 | שינוי שם — מוקדם (2026-07-17) | יבוצע לפני מימוש שלב ג', בשיטת **rename-in-place** (לא פרויקט חדש) |
| 20 | השם (2026-07-17) | **SessionDeck** |
| 21 | הוספת workspace (2026-07-17) | ערוץ ראשי: בחירת תיקייה; extension (שלב ד'); drag-in נשאר אך נחסם ללא-VSCode/כפולים; hook cwd כרשת ביטחון |

## 9. שאלות פתוחות

1. **קורלציה session↔טאב (שלב ד')** — איך מקשרים `session_id` מה-hook לטאב ספציפי ב-tabGroups API. ייבדק ב-spike בתחילת שלב ד'.
2. **מקור מצב error (שלב ג')** — אין hook ייעודי; ייבדק מול מה שה-hooks מספקים בפועל (למשל SessionEnd reason).

**היקף ה-rename ל-SessionDeck** (החלטות 19–20): **בוצע 2026-07-17 (v0.3.0)** — שם קבצי csproj/slnx, ‏RootNamespace/AssemblyName, ‏namespaces בקוד, שם ה-pipe (`sessiondeck`), ה-mutex, תיקיית ה-config (‏`%APPDATA%\SessionDeck` + מיגרציה אוטומטית של config קיים), ערך ה-HKCU Run (+מיגרציה), ואזכורים ב-SPEC/MANUAL_TESTS. ההיסטוריה של git נשמרה. שם התיקייה שונה ל-`D:\Eyal\SessionDeck` ‏(2026-07-19) — ה-rename הושלם במלואו.

שאר שאלות האפיון הוכרעו (ראה §8); שאלות מימוש יוכרעו תוך כדי הפיתוח.

---

## 10. הוראות קיקוף — ל-session המימוש (Claude Code ב-`D:\Eyal\WinGrid`)

מסמך זה הוא ההנחיה המלאה למימוש. סדר העבודה שסוכם:

1. **בוצע (2026-07-16):** אייל יצר את הפרויקט ב-Visual Studio — WPF Application, C#, .NET 10 LTS, solution+project באותה תיקייה (`WinGrid.slnx` + `WinGrid.csproj` ב-`D:\Eyal\WinGrid`).
2. ה-session הראשון ב-`D:\Eyal\WinGrid` מתבקש "לבצע את קובץ ההנחיות" — כלומר לממש את **שלב א' (MVP)** לפי §7, בהתאם לכל הדרישות (§3–§6) וההחלטות (§8).

הנחיות ל-session המימוש:

- **Git**: `git init` אם טרם בוצע; ליצור `.gitignore` ל-.NET/VS (bin/, obj/, .vs/) **לפני** כל commit; לעבוד לפי כללי ה-Git של אייל — branch לפני עבודה, אף פעם לא על main, commit רק באישור מפורש.
- **גרסה**: לנהל גרסה ב-csproj (`<Version>`) — bump בכל שינוי קוד, מתחיל מ-0.1.0.
- **פרויקט אחד בלבד**: ה-UI וה-CLI הם אותו exe (ראה §4). שים לב: WPF הוא `OutputType=WinExe` — כדי שה-CLI ידפיס לקונסולה של ההורה יש להשתמש ב-`AttachConsole(ATTACH_PARENT_PROCESS)` בענף ה-CLI לפני כתיבה ל-stdout.
- **סדר מימוש מומלץ בתוך ה-MVP**: (1) grid + DWM thumbnails עם picker; (2) focus/pin + stage; (3) Reserved Zone (AppBar); (4) pipe server + CLI; (5) persistence/auto-save.
- **בדיקות ידניות** בסוף כל אבן דרך: להריץ, לצרף 2–3 חלונות VSCode אמיתיים, לוודא תצוגה חיה, focus, ו-zone שדוחף חלונות maximized.
- מה שלא מוגדר כאן — החלטת מימוש חופשית; אם מתגלה סתירה או פער באפיון — לשאול את אייל ולעדכן מסמך זה.
