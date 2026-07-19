# SessionDeck — רשימת בדיקות ידניות (v0.5.0, שלבים ג'–ד')

> מטרה: לבדוק פיזית את מה שאי אפשר לבדוק מרחוק. סמן ✔ ליד מה שעבר.
> **הרצה:** `bin\Debug\net10.0-windows\SessionDeck.exe` (או F5 מ-Visual Studio).
> **CLI מטרמינל:** `SessionDeck.exe <command>` מאותה תיקייה (`SessionDeck.exe help` לרשימה המלאה).
> **קובץ ה-config:** `%APPDATA%\SessionDeck\config.json` — מכיל גם את ה-tiles הישנים משלבים א'-ב' (שדה legacy, לא מוצג).

## 1. Workspace Cards — הוספה וקישור

- [ ] "+ הוסף Workspace" → בחירת תיקיית פרויקט → נוצר כרטיס עם שם התיקייה.
- [ ] אם יש חלון VSCode פתוח על אותו workspace — הכרטיס מתחבר אליו מיד (thumbnail חי).
- [ ] `SessionDeck.exe add "D:\path\to\project"` — הוספה מה-CLI, אותה התנהגות.
- [ ] הוספת אותה תיקייה פעמיים — נחסם עם הודעה.
- [ ] כרטיס של פרויקט git מציג את ה-branch הנוכחי (⎇); החלפת branch ב-VSCode מתעדכנת תוך ~10 שניות.
- [ ] פרויקט עם Peacock (‏`.vscode/settings.json`) — מסגרת הכרטיס והכותרת בצבע ה-Peacock.
- [ ] סגירת חלון ה-VSCode — הכרטיס נשאר, "אין חלון VSCode פתוח"; פתיחה מחדש — מתחבר אוטומטית.
- [ ] גרירת חלון VSCode ושחרור מעל SessionDeck — נוצר/מתחבר כרטיס; חלון שאינו VSCode — נחסם עם הודעה.

## 2. Session Cards — סטטוסים מה-hooks

התקן את ה-hooks לפי `hooks/README.md`, ואז פתח session של Claude Code בפרויקט כלשהו:

- [ ] פתיחת session — נוצר תת-כרטיס אפור (idle) תחת ה-workspace (נוצר workspace אם לא היה).
- [ ] שליחת prompt — המסגרת כחולה קבועה (working).
- [ ] בקשת permission מ-Claude — כתום מהבהב (waiting).
- [ ] סיום turn — ירוק מהבהב (done); לחיצה על הכרטיס — ירוק קבוע + פוקוס לחלון.
- [ ] סגירת ה-session — הכרטיס נעלם; כפתור ▼ בכרטיס הראשי מציג אותו כסגור (עמום).
- [ ] בדיקה ידנית בלי Claude Code — הרץ את הפקודות מסוף `hooks/README.md`.
- [ ] `SessionDeck.exe session status --id <sid> --state error` — אדום מהבהב עד לחיצה.

## 3. ניהול הלוח

- [ ] לחיצה על כרטיס (לא על כפתור) — פוקוס לחלון ה-VSCode במקומו.
- [ ] לחיצה על כרטיס **ללא חלון פתוח** — נפתח VSCode על תיקיית הפרויקט והכרטיס מתחבר אליו.
- [ ] ▶ — החלון נפתח באזור הפתיחה (Stage) המוגדר בסרגל.
- [ ] ✏ — עריכת כותרת/תיאור/צבע (כולל חזרה ל"צבע אוטומטי").
- [ ] 🗕 — הכרטיס נעלם; טוגל "👁 מוסתרים" מציג אותו חזרה (מעומעם, והכפתור מתחלף ל-👁 "הצג חזרה בלוח").
- [ ] כרטיסים פעילים (חלון פתוח / session חי) קופצים לראש הלוח.
- [ ] הרבה כרטיסים — גלילה אנכית עובדת, הכרטיסים נשברים לשורות לפי רוחב החלון.

## 4. Zone / Stage (ללא שינוי משלב ב' — בדיקה קצרה)

- [ ] Zone "חצי ימין" — חלון SessionDeck נצמד וה-work area מצטמצם; "כבוי" משחרר.
- [ ] "אזור פתיחה" (Stage) — מסך/חצי + מוניטור משפיעים על ▶.

## 5. Persistence + Startup

- [ ] סגירה ופתיחה מחדש — כל ה-workspaces, הסשנים (כולל סגורים), הסטטוסים וה-acknowledge חוזרים.
- [ ] `%APPDATA%\SessionDeck\config.json` קריא; שינוי צבע ב-`StatusStyles` (למשל working→purple) נטען אחרי restart.
- [ ] טוגל "עלייה אוטומטית עם Windows" ב-⚙ יוצר/מוחק ערך registry ‏(`HKCU\...\Run\SessionDeck`).

## 6. שלב ד' — VSCode Extension ‏(SessionDeck Connector)

דרישה מוקדמת: ה-VSIX מותקן (`code --install-extension vscode-extension\sessiondeck-connector-0.5.0.vsix`) **וכל חלונות ה-VSCode עברו Reload** ‏(Ctrl+Shift+P ‏→ "Developer: Reload Window") אחרי ההתקנה. לוג ה-extension: ‏Output ‏→ ערוץ "SessionDeck".

- [ ] אחרי Reload — צ'יפ 📑 עם מספר טאבי ה-Claude הפתוחים מופיע בכותרת הכרטיס (tooltip מציג את שמותיהם); פתיחת/סגירת טאב Claude מעדכנת את המספר תוך ~שנייה.
- [ ] החלפת branch ב-VSCode — ה-⎇ בכרטיס מתעדכן מיידית (לא רק אחרי ~10 שניות).
- [ ] **לחיצה על Session Card עם טאב פתוח** — החלון מקבל פוקוס והטאב של אותו session נחשף.
- [ ] **לחיצה על Session Card שהטאב שלו סגור** (אך ה-VSCode פתוח) — הטאב נפתח מחדש (resume) עם ההיסטוריה.
- [ ] **לחיצה על סשן סגור** (תצוגה מורחבת ▼) — אותו דבר: הסשן קם לתחייה בטאב.
- [ ] פתיחה "ממוקסמת": ה-sidebar, ה-panel התחתון וה-sidebar המשני נסגרים לפני פתיחת הטאב (כיבוי: `OpenSessionMaximized: false` ב-config).
- [ ] **לחיצה על סשן כשה-VSCode סגור לגמרי** — ‏VSCode נפתח, ותוך ~שניות הטאב של הסשן נפתח מעצמו (pending open שממתין ל-connector).
- [ ] סשנים מציגים שם אמיתי (מה-transcript) במקום "session xxxxxxxx" תוך ~10 שניות מהעדכון הראשון.
- [ ] `SessionDeck.exe session open --id <sid>` — אותה התנהגות מה-CLI.
- [ ] סגירת SessionDeck ופתיחתו מחדש — ה-extension מתחבר לבד תוך ~5 שניות (רואים בלוג "connected").

## 7. ידוע / מגבלות

- תג 📑 על כרטיס סשן בודד (פתוח כטאב) הוא best-effort — לפי השוואת שם הטאב לכותרת הסשן.
- auto-acknowledge מפתיחת טאב ישירות ב-VSCode — לא ממומש (SPEC ‏§9.3).
- `claude-vscode.editor.open` הוא command פנימי של Claude Code; אם ייעלם בגרסה עתידית — ה-extension נופל אוטומטית ל-terminal עם `claude --resume`.
- `Notification` של Claude Code מכסה permission/אינפוט; אין אירוע error ייעודי (SPEC §9.2).

*הפקודה הפנימית `SessionDeck.exe snapshot <path>.png` מרנדרת את ה-UI ל-PNG (בלי ה-thumbnails) — שימושית לדיבוג מרחוק.*
