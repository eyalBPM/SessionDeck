# SessionDeck — תוכנית אריזה והפצה (יעד: v0.6.28)

> נכתב 2026-07-20. מיועד ל-session מימוש נפרד של Claude Code.
> קרא את הקובץ הזה **במלואו** לפני שאתה נוגע בקוד.

---

## 0. מה המטרה

היום, מי שרוצה להתקין את SessionDeck צריך לעשות חמישה דברים ידניים:

1. להתקין .NET 10 SDK
2. `git clone` + `dotnet build`
3. להוסיף ידנית את `SessionDeck.exe` ל-PATH
4. `npm install` + אריזת `.vsix` + `code --install-extension`
5. לפתוח את `~/.claude/settings.json`, להדביק ~40 שורות JSON, **ולהחליף בתוכן את `D:\Eyal\SessionDeck` בנתיב שלו — בשבעה מקומות**

שלב 5 הוא נקודת הכישלון. הוא גם שביר אצל אייל עצמו: ברגע שהפרויקט יזוז לתיקייה אחרת, ה-hooks יפסיקו לעבוד **בשקט, בלי הודעת שגיאה** (‏[`hooks/sessiondeck-hook.ps1:19`](hooks/sessiondeck-hook.ps1#L19) עושה `exit 0` כשהוא לא מוצא את ה-exe).

**היעד:** המשתמש מוריד zip אחד מ-GitHub Releases, מחלץ, מריץ `install.ps1`, וזהו.

---

## 1. החלטות שכבר התקבלו — אל תפתח מחדש

| נושא | הוחלט |
|---|---|
| תוכן ה-zip | **self-contained single-file** (~150MB). עובד על מחשב נקי בלי .NET מותקן. השיקול: 150MB בהורדה חד-פעמית זול יותר מתמיכה ב"למה זה לא נפתח". |
| רישיון | MIT, ‏© BPM Ltd. כבר ב-`main`. |
| נראות הריפו | **private** בינתיים. |
| ‏VS Marketplace / winget / Claude Code plugin | **נדחים.** כולם דורשים ריפו ציבורי. |
| ‏`.vsix` ב-git | **נשאר ב-`.gitignore`.** מקומו ב-Release artifacts, לא ב-repo. |

---

## 2. שלב 0 — להעתיק את `hooks/` לתיקיית ה-build

**זה תנאי מקדים לכל השאר.** נכון לעכשיו `hooks/sessiondeck-hook.ps1` לא מועתק ל-`bin/`, ולכן ה-exe לא יכול למצוא אותו אחרי התקנה.

ב-[`SessionDeck.csproj`](SessionDeck.csproj), הוסף `ItemGroup` חדש:

```xml
<ItemGroup>
  <Content Include="hooks\sessiondeck-hook.ps1">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

**אימות:** אחרי `dotnet build`, הקובץ `bin\Debug\net10.0-windows\hooks\sessiondeck-hook.ps1` קיים, **ועדיין עם UTF-8 BOM** (בדוק: `Format-Hex -Path <file> -Count 3` צריך להתחיל ב-`EF BB BF`). בלי ה-BOM, PowerShell 5.1 קורא את המחרוזות העבריות כ-ANSI והסקריפט נשבר — ראה [`hooks/README.md`](hooks/README.md).

---

## 3. שלב 1 — הפקודה `sessiondeck install-hooks`

זה החלק העיקרי. הערכה: 2-3 שעות.

### 3.1 מה הפקודה עושה

```
sessiondeck install-hooks [--settings <path>] [--dry-run]
sessiondeck uninstall-hooks [--settings <path>]
```

פותחת את `~/.claude/settings.json`, מגבה אותו, וממזגת לתוכו את שבעת ה-hooks של SessionDeck — כשהיא כותבת את **הנתיב האמיתי שבו הותקנה האפליקציה**, במקום `D:\Eyal\SessionDeck` הקבוע.

### 3.2 עיצוב קריטי — הפקודה לא עוברת דרך ה-pipe

**קרא את זה לפני שאתה כותב שורה.**

[`Program.cs:14-15`](Program.cs#L14-L15) קובע ש**כל** ארגומנט פירושו "שלח דרך ה-pipe לאפליקציה שרצה":

```csharp
if (args.Length > 0)
    return Cli.CliClient.Run(args);
```

אבל `install-hooks` צריכה לעבוד **כשהאפליקציה עדיין לא רצה** — זה בדיוק המצב בהתקנה. לכן היא חייבת להיתפס ב-`Main` **לפני** השורה הזאת, ולרוץ בתהליך ה-CLI עצמו:

```csharp
if (args.Length > 0)
{
    // Install commands run locally — they must work before the app has ever started.
    if (args[0] is "install-hooks" or "uninstall-hooks")
        return Cli.HookInstaller.Run(args);

    return Cli.CliClient.Run(args);
}
```

**לכן: אל תוסיף אותה ל-`switch` ב-[`CommandExecutor.cs:38-50`](Cli/CommandExecutor.cs#L38-L50).** כל מה ששם רץ על ה-UI thread של אפליקציה חיה. צור מחלקה חדשה `Cli/HookInstaller.cs`.

### 3.3 פתרון נתיבים

| מה | איך |
|---|---|
| נתיב ה-`.ps1` | `Path.Combine(AppContext.BaseDirectory, "hooks", "sessiondeck-hook.ps1")` — עובד גם ב-single-file publish (‏.NET 6+ מחזיר שם את תיקיית ה-exe). |
| נתיב `settings.json` | `Path.Combine(Environment.GetFolderPath(SpecialFolder.UserProfile), ".claude", "settings.json")` — ניתן לדריסה ב-`--settings`. |

אם ה-`.ps1` לא נמצא — **כשל עם הודעה ברורה**, אל תכתוב hooks שמצביעים לקובץ שלא קיים.

### 3.4 אלגוריתם המיזוג — החלק המסוכן

ל-`settings.json` של המשתמש כבר עשויים להיות hooks משלו. **אסור למחוק אותם.**

השתמש ב-`JsonNode` (‏`System.Text.Json.Nodes`) ולא ב-deserialize למחלקה — כדי לא לאבד שדות שאתה לא מכיר.

לכל אחד משבעת האירועים:

1. השג (או צור) את המערך ב-`hooks.<Event>`
2. **הסר** כל group שבתוך `hooks[]` שלו יש `command` שמכיל את המחרוזת `sessiondeck-hook.ps1` — זה מה שהופך את הפקודה לאידמפוטנטית ומטפל בשדרוגים שבהם הנתיב השתנה
3. הסר groups שנשארו ריקים
4. הוסף את ה-group שלנו
5. אם המערך התרוקן לגמרי (רלוונטי ל-`uninstall`) — מחק את המפתח

`uninstall-hooks` = אותו דבר בלי שלב 4.

### 3.5 מבנה שבעת ה-Hooks

חמישה בלי matcher — `SessionStart`, `UserPromptSubmit`, `Notification`, `Stop`, `SessionEnd`:

```json
{ "hooks": [ { "type": "command", "command": "<CMD> <EventName>" } ] }
```

שניים **עם** matcher — `PreToolUse`, `PostToolUse`:

```json
{ "matcher": "AskUserQuestion|ExitPlanMode",
  "hooks": [ { "type": "command", "command": "<CMD> <EventName>" } ] }
```

כאשר `<CMD>` הוא:

```
powershell -NoProfile -ExecutionPolicy Bypass -File "<resolved .ps1 path>"
```

המקור המלא והמוסמך לטבלת ה-hooks — [`hooks/README.md`](hooks/README.md), סעיף "התקנה". **ודא שאתה תואם לו בדיוק**, כולל ה-matcher.

### 3.6 כתיבה בטוחה

1. **גיבוי לפני נגיעה:** העתק ל-`settings.json.sessiondeck-backup-<yyyyMMdd-HHmmss>`
2. **כתיבה אטומית:** אותה תבנית כמו [`ConfigStore.cs:78-80`](Services/ConfigStore.cs#L78-L80) — כתוב ל-`.tmp` ואז `File.Move(..., overwrite: true)`
3. **שמור עיצוב:** `new JsonSerializerOptions { WriteIndented = true }`
4. **UTF-8 בלי BOM** — זה קובץ JSON, לא PowerShell

### 3.7 מקרי קצה שחייבים לעבוד

| מצב | התנהגות נדרשת |
|---|---|
| `~/.claude/settings.json` לא קיים | צור אותו (וגם את התיקייה) עם `{ "hooks": { ... } }` |
| הקובץ קיים אבל ריק / `{}` | הוסף את מפתח `hooks` |
| כבר יש hooks של SessionDeck מנתיב **ישן** | הוחלפו בחדש. אין כפילויות. |
| יש hooks של **כלי אחר** על אותו event | נשמרים כמו שהם, לצד שלנו |
| הקובץ הוא JSON פגום | **כשל בלי לכתוב.** אמור למשתמש לתקן. אל תדרוס. |
| הרצה שנייה ברצף | אין שינוי בקובץ (מלבד גיבוי חדש) |

### 3.8 בדיקות

בדיקות יחידה על אלגוריתם המיזוג — טבלת 3.7 היא רשימת מקרי הבדיקה. הזרק את נתיב ה-settings דרך `--settings` כדי לבדוק על קבצים זמניים.

בדיקה ידנית מקצה לקצה:
```powershell
Copy-Item ~/.claude/settings.json ~/settings-real-backup.json
sessiondeck install-hooks --dry-run     # להסתכל על הפלט לפני
sessiondeck install-hooks
sessiondeck install-hooks               # שוב — לוודא אפס שינוי
sessiondeck uninstall-hooks             # לוודא חזרה נקייה למצב המקורי
```

---

## 3ב. שלב 1ב — שני תיקונים שה-installer מחייב

**אל תדלג על הסעיף הזה.** בלי שני התיקונים האלה ה-installer ירוץ, ייראה מוצלח, וישבור שני דברים **בשקט**. שניהם קטנים.

### 3ב.1 — פקודת `sessiondeck quit`

**הבעיה:** כדי להחליף את ה-exe צריך לעצור את האפליקציה שרצה, אבל אין פקודה לזה — ראה את רשימת הפקודות ב-[`CommandExecutor.cs:38-50`](Cli/CommandExecutor.cs#L38-L50). יש `activate`, `status`, `focus`, ואין `quit`.

לכן `install.ps1` ייאלץ לעשות `Stop-Process -Force`. וזה הורס דבר אמיתי: [`MainWindow.xaml.cs:213-218`](MainWindow.xaml.cs#L213-L218) משחרר את ה-**AppBar** רק באירוע `Closing`:

```csharp
private void OnClosing(object? sender, CancelEventArgs e)
{
    ...
    _appBar.Remove();   // ← SHAppBarMessage(ABM_REMOVE)
}
```

הריגה כפויה מדלגת על זה, וה-work area של Windows נשאר מצומצם: **חצי מסך נשאר "תפוס" אחרי שהאפליקציה כבר מתה**, בלי שום הסבר למשתמש. תקלה שאנשים לא יודעים לדווח עליה.

**הפתרון:** הוסף `quit` ל-`switch` ב-[`CommandExecutor.cs`](Cli/CommandExecutor.cs) (זו פקודה רגילה שכן עוברת ב-pipe — בניגוד ל-`install-hooks`). היא צריכה לסגור את החלון דרך המסלול הרגיל כדי ש-`OnClosing` יירה. `install.ps1` יקרא לה, ויפול ל-`Stop-Process -Force` רק אחרי timeout של ~5 שניות.

### 3ב.2 — רענון נתיב ה-startup

**הבעיה:** [`StartupService.cs:37-38`](Services/StartupService.cs#L37-L38) כותב ל-registry את הנתיב **המוחלט** של ה-exe:

```csharp
key.SetValue(ValueName, $"\"{exe}\"");
```

הערך מתעדכן רק כשמדליקים/מכבים את ההגדרה ידנית. אחרי שההתקנה תעבור ל-`%LOCALAPPDATA%\Programs\SessionDeck`, ה-registry ימשיך להצביע לנתיב הישן — ובעלייה הבאה Windows יפעיל את ה-build הישן, או כלום אם הוא נמחק.

**זה יקרה כבר בהתקנה הראשונה, לא בעדכון.** ערך ה-Run הנוכחי של אייל מצביע ל-`D:\Eyal\SessionDeck\bin\...`.

**הפתרון:** בעלייה, אם `IsEnabled()` והערך השמור לא תואם ל-`Environment.ProcessPath` — לכתוב אותו מחדש. המקום הטבעי הוא ליד `MigrateLegacyValue()` שכבר נקראת מ-[`Program.cs`](Program.cs). הוסף `RefreshPathIfStale()` ב-`StartupService` וקרא לה משם.

---

## 4. שלב 2 — `install.ps1`

קובץ חדש בשורש. קטן. סדר הפעולות:

1. דורש PowerShell; **לא** דורש הרשאות admin (הכל תחת המשתמש)
2. עוצר instance רץ של SessionDeck — **דרך `sessiondeck quit` (סעיף 3ב.1), לא `Stop-Process`.** נפילה ל-`Stop-Process -Force` רק אחרי timeout של ~5 שניות.
3. מעתיק את תוכן ה-zip ל-`%LOCALAPPDATA%\Programs\SessionDeck`
4. מוסיף את התיקייה ל-**user PATH** — רק אם היא לא שם כבר
5. מתקין את ה-extension: `code --install-extension .\sessiondeck-connector-*.vsix`
   - אם `code` לא ב-PATH → **אזהרה, לא כשל.** האפליקציה עובדת בלי ה-extension; רק הפעלת טאב ספציפי ותוויות הטאבים לא יעבדו.
6. מריץ `install-hooks` מהנתיב שהותקן
7. מפעיל את האפליקציה
8. מדפיס סיכום: לאן הותקן, מה נוסף ל-PATH, איזה קובץ גיבוי נוצר, **ואת שלוש הגרסאות שהותקנו בפועל — אפליקציה, extension, hooks.**

למה שלוש הגרסאות: שלושת החלקים מתעדכנים בנפרד ויכולים להיפרד. אם שלב 5 נכשל בשקט (אין `code` ב-PATH), האפליקציה תעבוד — רק הפעלת טאב ותוויות הטאבים יישברו. הדפסת הגרסאות הופכת אי-התאמה כזאת לגלויה במקום לבאג מסתורי.

**שדרוג = הרצה חוזרת של אותו סקריפט.** כל השלבים אידמפוטנטיים, ולכן אין מסלול שדרוג נפרד.

כדאי גם `uninstall.ps1` תואם.

---

## 5. שלב 3 — בניית ה-Release

```powershell
# 1. bump ל-0.6.28 ב-SessionDeck.csproj

# 2. האפליקציה — self-contained, single file
dotnet publish -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

# 3. ה-extension
cd vscode-extension; npm install; npx @vscode/vsce package; cd ..

# 4. לארוז: תוכן ה-publish (כולל hooks\) + ה-vsix + install.ps1 + uninstall.ps1
#    לתוך SessionDeck-0.6.28-win-x64.zip

# 5. לפרסם
gh release create v0.6.28 SessionDeck-0.6.28-win-x64.zip --title "v0.6.28" --notes "..."
```

**‏`gh` נמצא ב-`C:\Program Files\GitHub CLI\gh.exe`** ומאומת כ-`eyalBPM`. אם `gh` לא מזוהה בטרמינל — פתח טרמינל חדש (PATH נטען פעם אחת בפתיחת חלון).

**אימות אמיתי:** חלץ את ה-zip למכונה או VM **בלי .NET מותקן**, הרץ `install.ps1`, ובדוק שסשן Claude Code חדש מייצר כרטיס ב-SessionDeck. זו הבדיקה היחידה שמוכיחה שהאריזה עובדת.

---

## 5ב. תהליך העדכון

**אין מסלול שדרוג נפרד.** עדכון = הורדת ה-zip החדש והרצת אותו `install.ps1`. כל השלבים אידמפוטנטיים: הקבצים נדרסים, ה-PATH הוא no-op, ה-extension מתעדכן, ו-`install-hooks` כותב מחדש את הנתיבים.

**מה שורד עדכון:** כל הגדרות המשתמש — `config.json`, ה-workspaces, המתגים, ה-stage וה-zone. כולם ב-`%APPDATA%\SessionDeck`, מחוץ לתיקיית ההתקנה. אם גרסה עתידית תשנה סכימת config, המיגרציה היא באחריות האפליקציה בעלייה (יש כבר תקדים — `MigrateLegacyValue`).

**מה שלא ייפגע כי טיפלנו בו:** ה-AppBar (סעיף 3ב.1) ורישום ה-startup (סעיף 3ב.2).

**‏Hooks שנורים תוך כדי העדכון** — סשני Claude Code פתוחים ימשיכו לירות hooks בזמן שה-exe מוחלף. זה בטוח: [`sessiondeck-hook.ps1:19`](hooks/sessiondeck-hook.ps1#L19) עושה `exit 0` כשהוא לא מוצא את ה-exe. הכרטיסים פשוט יפסידו כמה עדכוני סטטוס ויתקנו את עצמם בסריקה הבאה.

### מה שלא נפתר — הודעה על עדכון

**אין auto-update ואין התראה,** וריפו private אומר שאין גם עמוד ציבורי להסתכל בו. בפועל: אייל יצטרך להודיע לאנשים ידנית.

**זו החלטה מודעת, לא פספוס.** לשימוש פנימי בקנה מידה של כמה אנשים, מנגנון עדכון הוא overhead לא מוצדק. הצעד הזול היחיד שכן שווה: **שהאפליקציה תציג את מספר הגרסה שלה במקום גלוי** (‏tooltip ב-toolbar או תפריט ⚙), כדי שכשמישהו מדווח על באג יהיה אפשר לדעת על איזו גרסה הוא מסתכל.

אם בהמשך הריפו יהפוך לציבורי — זה הרגע לשקול auto-update, וגם winget שנותן `winget upgrade` בחינם.

---

## 6. כללי עבודה לסשן המימוש

- **ענף:** `feat/packaging-installer`. אל תעבוד על `main`.
- **Version bump חובה** — `<Version>` ב-[`SessionDeck.csproj`](SessionDeck.csproj) מ-`0.6.27` ל-`0.6.28`.
- **אין commit ואין push בלי אישור מפורש של אייל.**
- קבצי zip / publish זמניים → הוסף דפוס ל-`.gitignore` **לפני** שאתה יוצר אותם (‏`*.zip`, ‏`publish/`).
- עדכן את [`README.md`](README.md) בסוף: להחליף את סעיף ההתקנה הידני בהוראות ה-Release, ולהסיר את השורה תחת "Known limitations" שאומרת שההתקנה ידנית.

## 7. Definition of Done

- [ ] `hooks/` מועתק לפלט ה-build, עם BOM שלם
- [ ] `install-hooks` ממזג נכון את כל שבעת מקרי הקצה בטבלה 3.7
- [ ] `sessiondeck quit` סוגר נקי — **ואחריו ה-work area של Windows חזר למלוא גודלו** (בדוק עם zone פעיל: מקסם חלון ווודא שהוא תופס את כל המסך)
- [ ] אחרי התקנה לתיקייה חדשה, ערך ה-Run ב-registry מצביע ל-exe החדש
- [ ] `install.ps1` מדפיס בסוף את שלוש הגרסאות
- [ ] הרצה כפולה של `install-hooks` לא משנה כלום
- [ ] `uninstall-hooks` מחזיר את הקובץ למצבו המקורי
- [ ] `install.ps1` מתקין מקצה לקצה על מכונה נקייה בלי .NET
- [ ] Release ‏`v0.6.28` פורסם עם ה-zip
- [ ] README מעודכן
