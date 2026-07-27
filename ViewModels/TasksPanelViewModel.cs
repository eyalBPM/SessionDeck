using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SessionDeck.Models;
using SessionDeck.Services;

namespace SessionDeck.ViewModels;

/// <summary>
/// State of the tasks feature (T-0116): the parsed task list (pinned first, then file
/// order — the producer owns the order), the visible error/warning state, and the page
/// navigation flags. Fully opt-in: Enabled is false until a file path is configured, and
/// nothing tasks-related renders while it is.
/// </summary>
public sealed class TasksPanelViewModel : INotifyPropertyChanged
{
    /// <summary>Pinned tasks, in file order — always listed first with a separator.</summary>
    public ObservableCollection<TaskItemViewModel> PinnedTasks { get; } = new();
    /// <summary>The rest, strictly in file order.</summary>
    public ObservableCollection<TaskItemViewModel> OtherTasks { get; } = new();

    public IEnumerable<TaskItemViewModel> AllTasks => PinnedTasks.Concat(OtherTasks);

    private bool _enabled;
    /// <summary>A tasks file path is configured — the strip (and everything else) exists.</summary>
    public bool Enabled
    {
        get => _enabled;
        set { if (_enabled != value) { _enabled = value; Raise(); } }
    }

    private string _errorText = "";
    /// <summary>File-level error (missing / broken JSON / bad version) — shown INSTEAD of
    /// the list (T-0116 decision: visible errors beat a stale list).</summary>
    public string ErrorText
    {
        get => _errorText;
        set { if (_errorText != value) { _errorText = value; Raise(); Raise(nameof(HasError)); } }
    }

    public bool HasError => _errorText.Length > 0;

    private string _warningText = "";
    /// <summary>Record-level warnings (skipped entries) — shown ALONGSIDE the list.</summary>
    public string WarningText
    {
        get => _warningText;
        set { if (_warningText != value) { _warningText = value; Raise(); Raise(nameof(HasWarning)); } }
    }

    public bool HasWarning => _warningText.Length > 0;

    private bool _showSeparator;
    public bool ShowSeparator
    {
        get => _showSeparator;
        set { if (_showSeparator != value) { _showSeparator = value; Raise(); } }
    }

    private bool _pageOpen;
    /// <summary>The tasks page replaces the deck's central area while open.</summary>
    public bool PageOpen
    {
        get => _pageOpen;
        set { if (_pageOpen != value) { _pageOpen = value; Raise(); } }
    }

    private bool _pageBlocked;
    /// <summary>Search mode is active — the page button is disabled (mutual exclusion).</summary>
    public bool PageBlocked
    {
        get => _pageBlocked;
        set { if (_pageBlocked != value) { _pageBlocked = value; Raise(); Raise(nameof(PageButtonEnabled)); } }
    }

    public bool PageButtonEnabled => !_pageBlocked;

    /// <summary>newSessionPrompt template from the file's envelope (may be null).</summary>
    public string? NewSessionPrompt { get; private set; }

    private string _generatedText = "";
    public string GeneratedText
    {
        get => _generatedText;
        set { if (_generatedText != value) { _generatedText = value; Raise(); } }
    }

    /// <summary>Apply a load result. A file-level error keeps nothing — the error state
    /// replaces the list entirely.</summary>
    public void Apply(TasksLoadResult result)
    {
        PinnedTasks.Clear();
        OtherTasks.Clear();
        if (result.Document is not { } doc)
        {
            ErrorText = result.FileError ?? "שגיאה לא ידועה";
            WarningText = "";
            GeneratedText = "";
            NewSessionPrompt = null;
            ShowSeparator = false;
            return;
        }
        ErrorText = "";
        NewSessionPrompt = string.IsNullOrWhiteSpace(doc.NewSessionPrompt) ? null : doc.NewSessionPrompt;
        GeneratedText = FormatGenerated(doc.Generated);
        foreach (var entry in doc.Tasks)
        {
            var item = TaskItemViewModel.From(entry, doc.StatusColors);
            (item.Pinned ? PinnedTasks : OtherTasks).Add(item);
        }
        ShowSeparator = PinnedTasks.Count > 0 && OtherTasks.Count > 0;
        WarningText = result.RecordWarnings.Count == 0 ? ""
            : $"‏{result.RecordWarnings.Count} רשומות לא נטענו: " + string.Join("; ", result.RecordWarnings);
    }

    public void Clear()
    {
        PinnedTasks.Clear();
        OtherTasks.Clear();
        ErrorText = "";
        WarningText = "";
        GeneratedText = "";
        NewSessionPrompt = null;
        ShowSeparator = false;
    }

    private static string FormatGenerated(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return "";
        return DateTimeOffset.TryParse(iso, out var dt) ? $"עודכן {dt.LocalDateTime:HH:mm dd.MM}" : "";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
