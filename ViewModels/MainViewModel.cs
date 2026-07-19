using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SessionDeck.Models;

namespace SessionDeck.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    public ObservableCollection<WorkspaceViewModel> Workspaces { get; } = new();

    public int NextWorkspaceId { get; set; } = 1;

    private bool _showHidden;
    public bool ShowHidden
    {
        get => _showHidden;
        set { if (_showHidden != value) { _showHidden = value; Raise(); } }
    }

    public ZoneMode ZoneMode { get; set; } = ZoneMode.Off;
    public int ZoneMonitor { get; set; }
    public StageMode StageMode { get; set; } = StageMode.HalfRight;
    public int StageMonitor { get; set; }
    public Interop.RECT? StageRect { get; set; }       // used when StageMode == Rect
    public int ClosedSessionRetention { get; set; } = 20;

    public WorkspaceViewModel? FindById(int id)
        => Workspaces.FirstOrDefault(w => w.Id == id);

    public WorkspaceViewModel? FindByHwnd(IntPtr hwnd)
        => hwnd == IntPtr.Zero ? null : Workspaces.FirstOrDefault(w => w.Hwnd == hwnd);

    public WorkspaceViewModel? FindByPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string norm = Services.WorkspaceMetadata.NormalizePath(path);
        return Workspaces.FirstOrDefault(w =>
            w.Path.Length > 0 && Services.WorkspaceMetadata.NormalizePath(w.Path) == norm);
    }

    public (WorkspaceViewModel, SessionViewModel)? FindSession(string sessionId)
    {
        foreach (var w in Workspaces)
            if (w.FindSession(sessionId) is { } s)
                return (w, s);
        return null;
    }

    public IEnumerable<SessionViewModel> AllSessions()
        => Workspaces.SelectMany(w => w.Sessions);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
