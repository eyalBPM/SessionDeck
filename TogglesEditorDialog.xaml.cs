using System.Collections.ObjectModel;
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
