using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using SessionDeck.Models;
using SessionDeck.Services;

namespace SessionDeck;

/// <summary>
/// GUI editor for the custom toggles (feature 2026-07-19, redesigned 2026-07-20).
/// Deliberately domain-neutral: a toggle is just a named flag whose 1/0 state is exported
/// to a file for external processes — this dialog never assumes what reads it.
/// Two pages in one window: the list, and per-flag details with copyable values.
/// Returns the new definitions in <see cref="Result"/> on OK; the caller persists.
/// </summary>
public partial class TogglesEditorDialog : Window
{
    /// <summary>One editable row. Id is the flag file name: editable while the toggle is
    /// new, locked once it exists, so a rename can never move the flag path.</summary>
    public sealed class ToggleDraft : INotifyPropertyChanged
    {
        public string Icon { get; set; } = "🔘";
        public string Name { get; set; } = "";
        public bool DefaultOn { get; set; } = true;

        /// <summary>True for rows loaded from config — Id is fixed for their lifetime.</summary>
        public bool IdLocked { get; init; }

        public string IdHint => IdLocked
            ? "המזהה נקבע ביצירה ואינו ניתן לשינוי — תהליכים חיצוניים מסתמכים על הנתיב הזה"
            : "שם קובץ ה-flag. ייקבע סופית עם האישור";

        private string _id = "";
        public string Id
        {
            get => _id;
            set { if (_id != value) { _id = value; Raise(); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Raise([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private readonly ObservableCollection<ToggleDraft> _rows;

    public List<CustomToggleConfig> Result { get; private set; } = new();

    public TogglesEditorDialog(IEnumerable<CustomToggleConfig> current)
    {
        InitializeComponent();
        _rows = new ObservableCollection<ToggleDraft>(current.Select(t => new ToggleDraft
        {
            Icon = t.Icon,
            Id = t.Id,
            Name = t.Name,
            DefaultOn = t.DefaultOn,
            IdLocked = true,
        }));
        TogglesList.ItemsSource = _rows;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
        => _rows.Add(new ToggleDraft());

    private void DeleteRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ToggleDraft row })
            _rows.Remove(row);
    }

    // ---- details page ----

    private void Details_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ToggleDraft row }) return;
        string id = ToggleStore.Sanitize(row.Id.Trim());
        if (id.Length == 0)
        {
            Warn("קודם תן למתג מזהה (id) — הוא שם קובץ ה-flag");
            return;
        }
        string path = Path.Combine(ToggleStore.Dir, id);

        DetailsHeader.Text = $"{row.Icon}  {(row.Name.Trim().Length > 0 ? row.Name.Trim() : id)}";
        DetailsId.Text = id;
        DetailsPath.Text = path;
        DetailsState.Text = File.Exists(path)
            ? $"{(ToggleStore.Read(id, row.DefaultOn) ? "1 (דלוק)" : "0 (כבוי)")}"
            : $"אין עדיין קובץ — ברירת המחדל ({(row.DefaultOn ? "1 / דלוק" : "0 / כבוי")}) תיכתב עם האישור";
        DetailsCli.Text = $"sessiondeck toggle get {id}\r\n" +
                          $"sessiondeck toggle set {id} on\r\n" +
                          $"sessiondeck toggle set {id} off";
        DetailsSnippet.Text = $"$flag = \"{path}\"\r\n" +
                              "if ((Test-Path $flag) -and ((Get-Content $flag -Raw).Trim() -eq '0')) { exit 0 }";
        DetailsPrompt.Text = BuildPrompt(id, row.Name.Trim(), path);

        ListPage.Visibility = Visibility.Collapsed;
        DetailsPage.Visibility = Visibility.Visible;
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        DetailsPage.Visibility = Visibility.Collapsed;
        ListPage.Visibility = Visibility.Visible;
    }

    /// <summary>Opening description of the flag for an AI agent (request 2026-07-19):
    /// facts only — what the flag is, where it lives, what its values mean. The user
    /// appends whatever they want the agent to wire up.</summary>
    private static string BuildPrompt(string id, string name, string path)
    {
        string named = name.Length > 0 ? $" named \"{name}\"" : "";
        return $"""
            I have a toggle flag{named} that I control from a toolbar button in SessionDeck.

            Its state lives in this file:
                {path}
            The file contains "1" (on) or "0" (off). A missing file means on. It persists
            across restarts and can be read at any time, whether or not SessionDeck is running.

            I want to gate one of my processes on this flag:
            """;
    }

    private void CopyId_Click(object sender, RoutedEventArgs e) => Copy(DetailsId.Text);
    private void CopyPath_Click(object sender, RoutedEventArgs e) => Copy(DetailsPath.Text);
    private void CopyCli_Click(object sender, RoutedEventArgs e) => Copy(DetailsCli.Text);
    private void CopySnippet_Click(object sender, RoutedEventArgs e) => Copy(DetailsSnippet.Text);
    private void CopyPrompt_Click(object sender, RoutedEventArgs e) => Copy(DetailsPrompt.Text);

    private void Copy(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
            Warn("ההעתקה ללוח נכשלה — נסה שוב");
        }
    }

    // ---- save ----

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var result = new List<CustomToggleConfig>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _rows)
        {
            string id = ToggleStore.Sanitize(row.Id.Trim());
            string name = row.Name.Trim();
            // A fully-empty row (fresh "+" click) is silently dropped.
            if (id.Length == 0 && name.Length == 0) continue;
            if (id.Length == 0)
            {
                Warn($"מתג \"{name}\" חסר מזהה (id) — המזהה הוא שם קובץ ה-flag");
                return;
            }
            if (!seen.Add(id))
            {
                Warn($"המזהה \"{id}\" מופיע יותר מפעם אחת");
                return;
            }
            result.Add(new CustomToggleConfig
            {
                Id = id,
                Name = name,
                Icon = row.Icon.Trim().Length > 0 ? row.Icon.Trim() : "🔘",
                DefaultOn = row.DefaultOn,
            });
        }
        Result = result;
        DialogResult = true;
    }

    // RtlReading: the message text is Hebrew, so the OS dialog must lay out RTL too.
    private void Warn(string message)
        => MessageBox.Show(this, message, "מתגים (Flags)", MessageBoxButton.OK, MessageBoxImage.Warning,
            MessageBoxResult.OK, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
}
