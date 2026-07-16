using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using WinGrid.Models;

namespace WinGrid.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    public ObservableCollection<TileViewModel> Tiles { get; } = new();

    public int NextTileId { get; set; } = 1;

    private int _gridColumns = 1;
    public int GridColumns
    {
        get => _gridColumns;
        set { if (_gridColumns != value) { _gridColumns = value; Raise(); } }
    }

    public ZoneMode ZoneMode { get; set; } = ZoneMode.Off;
    public int ZoneMonitor { get; set; }
    public StageMode StageMode { get; set; } = StageMode.HalfRight;
    public int StageMonitor { get; set; }

    public TileViewModel? FindById(int id)
    {
        foreach (var t in Tiles)
            if (t.Id == id) return t;
        return null;
    }

    public TileViewModel? FindByHwnd(IntPtr hwnd)
    {
        foreach (var t in Tiles)
            if (t.Hwnd == hwnd) return t;
        return null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
