using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using SessionDeck.Services;

namespace SessionDeck.ViewModels;

public enum BindState { Connected, Disconnected }

/// <summary>
/// A workspace card (SPEC §2ב): persistent entity representing a VSCode workspace.
/// The OS window is only its live binding — the card survives window close/reopen.
/// </summary>
public sealed class WorkspaceViewModel : INotifyPropertyChanged
{
    public int Id { get; init; }

    private string _path = "";
    /// <summary>Folder path; empty for drag-in adds until a hook reports cwd (SPEC decision 21).</summary>
    public string Path
    {
        get => _path;
        set { if (_path != value) { _path = value; Raise(); } }
    }

    private string _name = "";
    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; Raise(); Raise(nameof(DisplayTitle)); } }
    }

    private string? _customTitle;
    public string? CustomTitle
    {
        get => _customTitle;
        set { if (_customTitle != value) { _customTitle = value; Raise(); Raise(nameof(DisplayTitle)); } }
    }

    public string DisplayTitle => !string.IsNullOrEmpty(_customTitle) ? _customTitle : _name;

    private string _description = "";
    public string Description
    {
        get => _description;
        set { if (_description != value) { _description = value; Raise(); } }
    }

    // ---- card color (SPEC decision 18): manual override > Peacock > default ----

    private string? _customColor;
    public string? CustomColor
    {
        get => _customColor;
        set { if (_customColor != value) { _customColor = value; RaiseColor(); } }
    }

    private string? _peacockColor;
    public string? PeacockColor
    {
        get => _peacockColor;
        set { if (_peacockColor != value) { _peacockColor = value; RaiseColor(); } }
    }

    public string EffectiveColor => _customColor ?? _peacockColor ?? "#4A4A4A";

    public Brush CardBrush
    {
        get
        {
            var brush = new SolidColorBrush(ColorUtil.TryParse(EffectiveColor, out var c)
                ? c : Color.FromRgb(0x4A, 0x4A, 0x4A));
            brush.Freeze();
            return brush;
        }
    }

    private void RaiseColor()
    {
        Raise(nameof(EffectiveColor));
        Raise(nameof(CardBrush));
    }

    // ---- git branch (SPEC decision 17) ----

    private string _branch = "";
    public string Branch
    {
        get => _branch;
        set { if (_branch != value) { _branch = value; Raise(); Raise(nameof(HasBranch)); } }
    }

    public bool HasBranch => _branch.Length > 0;

    // ---- live window binding (engine reuse from stage A/B) ----

    private IntPtr _hwnd;
    public IntPtr Hwnd
    {
        get => _hwnd;
        set { if (_hwnd != value) { _hwnd = value; Raise(); } }
    }

    private string _windowTitle = "";
    public string WindowTitle
    {
        get => _windowTitle;
        set { if (_windowTitle != value) { _windowTitle = value; Raise(); } }
    }

    private string _processName = "";
    public string ProcessName
    {
        get => _processName;
        set { if (_processName != value) { _processName = value; Raise(); } }
    }

    /// <summary>Regex matching this workspace's VSCode window title (SPEC §6.6).</summary>
    public string TitlePattern => WorkspaceMetadata.BuildTitlePattern(_name);

    private BindState _state = BindState.Disconnected;
    public BindState State
    {
        get => _state;
        set { if (_state != value) { _state = value; Raise(); Raise(nameof(IsActive)); } }
    }

    // ---- deck management (SPEC decision 16) ----

    private bool _hidden;
    public bool Hidden
    {
        get => _hidden;
        set { if (_hidden != value) { _hidden = value; Raise(); Raise(nameof(IsActive)); } }
    }

    /// <summary>Set by the controller from Hidden + the global show-hidden toggle.</summary>
    private bool _visibleInDeck = true;
    public bool VisibleInDeck
    {
        get => _visibleInDeck;
        set { if (_visibleInDeck != value) { _visibleInDeck = value; Raise(); } }
    }

    private bool _expanded;
    /// <summary>Expanded card shows closed sessions too (SPEC §2ב).</summary>
    public bool Expanded
    {
        get => _expanded;
        set
        {
            if (_expanded == value) return;
            _expanded = value;
            RefreshSessionVisibility();
            Raise();
        }
    }

    public ObservableCollection<SessionViewModel> Sessions { get; } = new();

    public bool HasOpenSessions => Sessions.Any(s => !s.Closed);

    /// <summary>Active = bound window or a live session; actives sort to the top.</summary>
    public bool IsActive => _state == BindState.Connected || HasOpenSessions;

    public void RefreshSessionVisibility()
    {
        foreach (var s in Sessions)
            s.Visible = !s.Closed || _expanded;
        Raise(nameof(HasOpenSessions));
        Raise(nameof(IsActive));
    }

    public SessionViewModel? FindSession(string sessionId)
        => Sessions.FirstOrDefault(s => s.SessionId == sessionId);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
