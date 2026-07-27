using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace SessionDeck;

/// <summary>
/// Path input for the external tasks file (T-0116). Empty = feature off. The path may
/// point at a file that doesn't exist yet — the watcher reports that as a visible error
/// and picks the file up when it appears — but a nonexistent FOLDER can't be watched, so
/// that case is blocked here.
/// </summary>
public partial class TasksFileDialog : Window
{
    public string PathText => PathBox.Text.Trim();

    /// <summary>The file contract, for the 📋 button — hand this to whatever produces the
    /// file (a script, Claude, ...). Keep in sync with TasksFileService/TasksDocument.</summary>
    private const string SpecText = """
        # קובץ המשימות של SessionDeck — חוזה ה-JSON (version 1)

        SessionDeck קורא את הקובץ בלבד (read-only) ומתעדכן אוטומטית בכל שמירה שלו.
        ה-producer (הכלי שכותב את הקובץ) אחראי לתוכן, לסדר ולצבעים.

        ## מבנה הקובץ (מעטפת)

        ```json
        {
          "version": 1,
          "generated": "2026-07-27T10:00:00+03:00",
          "statusColors": { "in-progress": "#4FC3F7", "ready": "green" },
          "newSessionPrompt": "בוא נעבוד על משימה <id> — <name>",
          "tasks": [
            {
              "id": "T-0042",
              "name": "שם המשימה",
              "description": "תיאור קצר",
              "status": "in-progress",
              "pinned": true,
              "workspace": "D:\\BPM\\SessionDeck",
              "sessions": ["9d089f9a-058e-4afc-a60e-93979b772824"],
              "url": "obsidian://open?vault=taskdeck&file=..."
            }
          ]
        }
        ```

        ## שדות המעטפת

        - `version` — חובה, חייב להיות 1. ערך אחר = מצב שגיאה גלוי.
        - `generated` — רשות. חותמת ISO של זמן הייצור; מוצגת כחיווי רעננות.
        - `statusColors` — רשות. מיפוי סטטוס→צבע: `#RRGGBB` או שם
          (red, green, orange, blue, gray, yellow, purple, cyan, magenta, white, black).
          סטטוס בלי צבע מוצג ניטרלי. הצבע הוא סמנטיקה של הדאטה — באחריות ה-producer.
        - `newSessionPrompt` — רשות. תבנית טקסט לסשן חדש שנפתח מלחיצה על משימה;
          `<id>` ו-`<name>` מוחלפים בערכי המשימה. הטקסט ממתין בתיבת האינפוט (לא נשלח).
          חסר → סשן חדש נפתח ריק.

        ## שדות משימה

        - `id` — **חובה**. מזהה ייחודי (מחרוזת).
        - `name` — **חובה**. שם המשימה.
        - `description` — רשות.
        - `status` — רשות. מחרוזת חופשית; הצבע נקבע לפי statusColors.
        - `pinned` — רשות (bool). נעוצות מוצגות ראשונות עם חיווי וקו הפרדה.
        - `workspace` — רשות. **נתיב מלא** של תיקיית הפרויקט; ההתאמה לכרטיס ב-SessionDeck
          נעשית לפי path. בלעדיו אין כפתור פתיחת סשן.
        - `sessions` — רשות. רשימת UUID של סשני Claude Code (קשר רבים-לרבים).
        - `url` — רשות. קישור לפתיחת המשימה (נפתח ב-ShellExecute, למשל obsidian://...).

        ## הגדרת הקובץ ב-SessionDeck (CLI)

        ```
        SessionDeck.exe tasks --file "C:\path\to\tasks.json"   # הגדרה + הפעלת הפאנל
        SessionDeck.exe tasks                                  # הצגת המצב הנוכחי
        SessionDeck.exe tasks --off                            # כיבוי הפאנל
        ```

        (או דרך ה-UI: ⚙ ← "קובץ משימות (JSON)...")

        ## כללי התנהגות

        - רשומה בלי `id` או `name` מדולגת עם אזהרה גלויה; שאר הרשימה נטענת.
        - מפתחות לא מוכרים נבלעים בשקט (forward-compat) — מותר להוסיף שדות משלך.
        - סדר התצוגה = סדר המערך (אחרי הנעוצות); המיון באחריות ה-producer.
        - JSON שבור / קובץ חסר / version לא נתמך → מצב שגיאה גלוי במקום רשימה.
        - מומלץ לכתוב אטומית (קובץ זמני + rename); בכל מקרה יש retry קצר על קובץ נעול.
        """;

    public TasksFileDialog(string? current)
    {
        InitializeComponent();
        PathBox.Text = current ?? "";
        PathBox.Focus();
        PathBox.SelectAll();
        UpdatePreview();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "בחר קובץ משימות (JSON)",
            Filter = "JSON|*.json|כל הקבצים|*.*",
            CheckFileExists = false,
        };
        if (PathText.Length > 0)
        {
            try { dialog.InitialDirectory = Path.GetDirectoryName(PathText); } catch { }
        }
        if (dialog.ShowDialog(this) == true) PathBox.Text = dialog.FileName;
    }

    private void Path_Changed(object sender, TextChangedEventArgs e) => UpdatePreview();

    private (bool Ok, string Message) Validate()
    {
        string path = PathText;
        if (path.Length == 0) return (true, "הפאנל יכובה");
        try
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (dir == null || !Directory.Exists(dir))
                return (false, "התיקייה של הקובץ לא קיימת");
            return (true, File.Exists(path) ? "" : "הקובץ עדיין לא קיים — ייטען כשייווצר");
        }
        catch
        {
            return (false, "נתיב לא חוקי");
        }
    }

    private void UpdatePreview()
    {
        if (PreviewText == null || OkButton == null) return;   // during InitializeComponent
        var (ok, message) = Validate();
        PreviewText.Text = message;
        OkButton.IsEnabled = ok;
    }

    private void CopySpec_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(SpecText);
            PreviewText.Text = "ההנחיות הועתקו ללוח 📋";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            PreviewText.Text = "הלוח תפוס — נסה שוב";
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!Validate().Ok) return;
        DialogResult = true;
    }
}
