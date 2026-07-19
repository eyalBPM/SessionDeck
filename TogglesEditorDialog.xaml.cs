using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using SessionDeck.Models;
using SessionDeck.Services;

namespace SessionDeck;

/// <summary>
/// GUI editor for the custom toolbar toggles (feature 2026-07-19) — previously the
/// CustomToggles list was hand-edited in config.json. Returns the new definitions in
/// <see cref="Result"/> on OK; the caller rebuilds the toolbar and persists.
/// </summary>
public partial class TogglesEditorDialog : Window
{
    /// <summary>One editable row; plain properties — values are read back on OK.</summary>
    public sealed class ToggleDraft
    {
        public string Icon { get; set; } = "🔘";
        public string Id { get; set; } = "";
        public string Tooltip { get; set; } = "";
        public bool DefaultOn { get; set; } = true;
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
            Tooltip = t.Tooltip ?? "",
            DefaultOn = t.DefaultOn,
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

    /// <summary>Copies an English prompt for the user's AI agent (request 2026-07-19):
    /// the user pastes it to their agent, which wires their own hook/script to this
    /// toggle's flag file. Agent-side integration beats a raw code snippet — the agent
    /// finds the right script and merges the check idiomatically.</summary>
    private void CopyPrompt_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ToggleDraft row }) return;
        string id = ToggleStore.Sanitize(row.Id.Trim());
        if (id.Length == 0)
        {
            Warn("קודם תן למתג מזהה (id) — הוא קובע את שם קובץ הדגל");
            return;
        }
        string flagPath = Path.Combine(ToggleStore.Dir, id);
        string tooltipNote = row.Tooltip.Trim().Length > 0 ? $" ({row.Tooltip.Trim()})" : "";
        string prompt = $$"""
            I use SessionDeck (a dashboard for Claude Code sessions). I defined a custom toggle
            button named "{{id}}"{{tooltipNote}} on its toolbar, and I want it to control one of
            my own hooks/scripts.

            How the toggle works: SessionDeck writes the toggle's current state to the flag file:
                {{flagPath}}
            The file contains "1" (toggle on) or "0" (toggle off). A missing file means ON.
            The file persists across restarts and is readable even when SessionDeck isn't running.

            Your task: find the hook/script of mine that this toggle should control (ask me which
            one if unclear), and add a guard at its start: read the flag file, and if its trimmed
            content equals "0", exit silently without doing anything. Keep the change minimal and
            match the script's existing language and style. PowerShell example:

                $flag = "$env:APPDATA\SessionDeck\toggles\{{id}}"
                if ((Test-Path $flag) -and ((Get-Content $flag -Raw).Trim() -eq '0')) { exit 0 }
            """;
        try
        {
            Clipboard.SetText(prompt);
            MessageBox.Show(this, "הפרומפט הועתק — הדבק אותו אצל הסוכן שלך (למשל Claude Code) והוא יחבר את ה-hook למתג.",
                "מתגים אישיים", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch
        {
            Warn("ההעתקה ללוח נכשלה — נסה שוב");
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var result = new List<CustomToggleConfig>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _rows)
        {
            string id = ToggleStore.Sanitize(row.Id.Trim());
            // A fully-empty row (fresh "+" click) is silently dropped.
            if (id.Length == 0 && row.Tooltip.Trim().Length == 0) continue;
            if (id.Length == 0)
            {
                Warn($"מתג \"{row.Tooltip}\" חסר מזהה (id) — המזהה הוא שם קובץ הדגל");
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
                Icon = row.Icon.Trim().Length > 0 ? row.Icon.Trim() : "🔘",
                Tooltip = row.Tooltip.Trim().Length > 0 ? row.Tooltip.Trim() : null,
                DefaultOn = row.DefaultOn,
            });
        }
        Result = result;
        DialogResult = true;
    }

    private void Warn(string message)
        => MessageBox.Show(this, message, "מתגים אישיים", MessageBoxButton.OK, MessageBoxImage.Warning);
}
