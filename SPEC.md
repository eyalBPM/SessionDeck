# WinGrid — אפיון פיתוח (v0.4)

> Last updated: 2026-07-16
> Status: **אפיון בלבד — טרם נכתב קוד.**
> שם: **WinGrid** (אושר סופית 2026-07-16).
> מחבר האפיון: Claude Code בשיתוף אייל, סשן 2026-07-16.

---

## 1. רקע ומטרה

אפליקציית Windows כללית לניטור ושליטה בחלונות: לוח (grid) של "אריחים" (tiles), כל אריח מציג **תצוגה חיה** של חלון OS נבחר, עם כותרת, תיאור ומסגרת צבעונית. לחיצה מקפיצה/מפעילה את החלון האמיתי. האפליקציה נשלטת גם מ-CLI.

**מטרת-על (זכורה, אך האפיון נשאר גנרי):** שליטה בכל ה-sessions של Claude Code שרצים במחשב (כל session בחלון VSCode). בשלב 2, hooks של Claude Code יקראו ל-CLI כדי לעדכן צבע מסגרת לפי סטטוס העבודה של כל session (למשל: עובד = ירוק, ממתין לאינפוט = מהבהב כתום/שחור).

---

## 2. מושגי יסוד

| מושג | הגדרה |
|------|--------|
| **Tile** | ריבוע ב-grid המייצג חלון OS אחד (HWND top-level): תצוגה חיה + כותרת + תיאור + מסגרת צבעונית. |
| **Stage** | אזור יעד גלובלי מוגדר-מראש (מסך שלם / חצי מסך / מלבן), שאליו "קופץ" חלון בפעולת **Pin**. |
| **Reserved Zone** | שטח המסך שהאפליקציה עצמה תופסת (מסך מלא או חצי מסך). מנוכה מה-work area של Windows — חלונות אחרים (כולל maximized) לא נכנסים אליו; העכבר חופשי לנוע לשם. |
| **Matcher** | כלל שיוך persistent של tile לחלון: process name + title pattern (regex), לצורך re-bind אחרי הפעלה מחדש. |

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
3. **CLI**: `wingrid add ...` (ראה §4).
- חלונות מסוננים מה-picker: חלונות של WinGrid עצמו, חלונות ללא title, tool windows.

### F6 — ניהול Tiles (עודכן 2026-07-16)
- הסרה (UI + CLI), עריכת כותרת/תיאור/צבע (UI + CLI), שינוי סדר בגרירה.
- לכל tile יש שדה **state**: `connected` / `disconnected`. ה-state מוצג כאינדיקציית סטטוס על ה-tile (למשל אייקון/תג + placeholder במקום ה-thumbnail) — **הכותרת, התיאור והצבע לא משתנים בגלל ניתוק** (הוחלט 2026-07-16). ה-state מופיע גם ב-`wingrid list`.
- **חלון שנסגר**: ה-tile עובר ל-`disconnected` ולא נמחק; כשנפתח חלון חדש שתואם ל-Matcher — re-bind אוטומטי וה-tile חוזר ל-`connected`. הסרה אוטומטית = אופציה בהגדרות (כבויה כברירת מחדל).

### F7 — Persistence + Auto-Save (עודכן 2026-07-16)
- פרופיל JSON ב-`%APPDATA%\WinGrid\config.json`: רשימת tiles (Matcher, כותרת ידנית, תיאור, צבע/הבהוב), מצב zone, הגדרת Stage, סדר, מיקום/גודל החלון הראשי.
- **Auto-save תמידי**: כל שינוי (הוספה/הסרה/עריכה/צבע/סדר/zone/stage) נשמר מיידית (debounce ~1s, כתיבה אטומית: temp file + rename). אין כפתור "שמור" ואין מצב לא-שמור.
- בהפעלה: טעינת הפרופיל, enumerate של חלונות קיימים + re-bind לפי Matchers; tiles שחלונם טרם נפתח מוצגים "מנותקים" (אפור) עד שיופיע חלון תואם.

### F8 — CLI (ראה §4)
- כל יכולות הניהול זמינות מ-CLI, לצורך אוטומציה (ובפרט hooks של Claude Code בשלב 2).

### F9 — עלייה אוטומטית עם Windows (נוסף 2026-07-16)
- WinGrid נרשם ל-startup של המשתמש (registry `HKCU\...\Run`, ללא admin; ניתן לכיבוי בהגדרות).
- בעלייה משוחזר **המצב המלא** מהפרופיל: כל ה-tiles (דרך Matchers — אחרי ריסטארט כל החלונות מקבלים HWND חדש, ולכן השחזור מבוסס re-bind), מצב ה-Reserved Zone, הגדרת ה-Stage, וסדר ה-grid.
- tiles שחלונם עוד לא נפתח אחרי הריסטארט מופיעים מנותקים ומתחברים אוטומטית ברגע שהחלון התואם נפתח.

---

## 4. CLI — פירוט

### מודל הפעלה
- **Singleton**: ההרצה הראשונה מרימה את ה-UI + שרת **named pipe** (`\\.\pipe\wingrid`).
- הרצה חוזרת עם ארגומנטים = CLI client: מעביר את הפקודה ל-pipe, מדפיס תשובה, מחזיר exit code (0 = הצלחה, ≠0 = שגיאה + הודעה ל-stderr). זמן ריצה יעד: <100ms (קריטי ל-hooks).
- אם אין instance רץ: פקודות CLI נכשלות עם הודעה ברורה (אופציה עתידית: `--start` שמרים את ה-UI).

### פקודות (טיוטה)

```
wingrid list                          # טבלת tiles: id, title, desc, process, color, state
wingrid add --pick                    # פותח picker אינטראקטיבי
wingrid add --match "<title regex>" [--process code] [--desc "..."] [--color <c>]
wingrid remove <target>
wingrid set <target> [--title "..."] [--desc "..."]
wingrid border <target> --color <c>                        # צבע סטטי
wingrid border <target> --color <c> --alt <c2> [--interval <ms>]   # מהבהב (ברירת מחדל 500ms)
wingrid focus <target>                # הפעלה במקום הנוכחי
wingrid pin <target>                  # הקפצה ל-Stage + הפעלה
wingrid stage --monitor <n> --half left|right | --full | --rect x,y,w,h
wingrid zone --monitor <n> --half left|right | --full | --off
wingrid status                        # מצב האפליקציה: zone, stage, מספר tiles
```

- **`<target>`**: id מספרי יציב של tile, או `--match "<regex>"` על הכותרת (שימושי ל-hooks: לפי שם ה-workspace).
- **צבעים**: שמות (`red`, `green`, `orange`, `blue`, `gray`, ...) או hex `#RRGGBB`.
- `wingrid border --match "..."` על חלון שעדיין לא ב-grid: שגיאה, עם דגל אופציונלי `--auto-add` (שלב 2 — נוח ל-hooks).

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
2. **SetForegroundWindow restrictions** — כשמשתמש לוחץ ב-UI שלנו, אנחנו ה-foreground process ומותר להעביר פוקוס. אבל `wingrid focus` שמגיע מתהליך רקע (hook) עלול להיחסם ע"י Windows (anti focus-stealing). לא קריטי: שלב 2 משנה רק צבעים. אם יידרש — יש workarounds מוכרים (AttachThreadInput וכו').
3. **גרנולריות = חלון OS** — כמה sessions של Claude Code באותו חלון VSCode לא ניתנים להבחנה. מוסכמת עבודה: **session אחד = חלון VSCode אחד**.
4. **Drag-in דורש global low-level mouse hook** — רכיב רגיש (ביצועים/יציבות); לכן Picker הוא המנגנון העיקרי, drag-in נדחה לשלב ב'.
5. **חלונות elevated (admin)** — thumbnail יעבוד, אבל move/activate ייחסמו אם WinGrid לא elevated. מתועד כמגבלה ידועה.
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

### שלב ג' — אינטגרציית Claude Code (המטרה)
- סקריפט hooks (PowerShell) שנרשם ב-settings של Claude Code ומריץ `wingrid border --match "<workspace>" ...` לפי אירועים:
  - `UserPromptSubmit` / תחילת עבודה → ירוק
  - `Notification` (ממתין ל-permission/אינפוט) → מהבהב כתום/שחור
  - `Stop` (סיים turn) → כחול
- `--auto-add` ל-hooks, פרופיל "Claude sessions"
- מיפוי session→window לפי workspace name ב-title של VSCode

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
| 9 | מיקום ה-repo | **`D:\Eyal\WinGrid`** (עודכן 2026-07-16 — אייל יצר שם את הפרויקט בפועל) — repo git עצמאי, מחוץ לעץ ה-OneDrive. הערה: תחת `D:\Eyal\` חל סיווג "פרויקט אישי" בהגדרות של אייל — פרוטוקולי BPM לא חלים, כללי Git/safety כלליים כן |
| 10 | Reserved Zone | **בשלב א' (MVP)** — חלק מהותי מהאפליקציה (החלטת אייל 2026-07-16) |

## 9. שאלות פתוחות

אין — כל שאלות האפיון הוכרעו (ראה §8). שאלות מימוש יוכרעו תוך כדי הפיתוח.

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
