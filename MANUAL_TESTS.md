# SessionDeck — רשימת בדיקות ידניות (v0.4.0, שלב ג')

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

## 6. ידוע / מגבלות

- Session Card מזוהה לפי `session_id` מה-hook; קורלציה לטאב ספציפי ב-VSCode — שלב ד'.
- לחיצה על Session Card ממקדת את החלון בלבד (לא את הטאב) — שלב ד'.
- `Notification` של Claude Code מכסה permission/אינפוט; אין אירוע error ייעודי (SPEC §9.2).

*הפקודה הפנימית `SessionDeck.exe snapshot <path>.png` מרנדרת את ה-UI ל-PNG (בלי ה-thumbnails) — שימושית לדיבוג מרחוק.*
