using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using WinGrid.Services;

namespace WinGrid.ViewModels;

public enum TileState { Connected, Disconnected }

public sealed class TileViewModel : INotifyPropertyChanged
{
    public int Id { get; init; }

    private IntPtr _hwnd;
    public IntPtr Hwnd
    {
        get => _hwnd;
        set { if (_hwnd != value) { _hwnd = value; Raise(); } }
    }

    private string _title = "";
    public string Title
    {
        get => _title;
        set { if (_title != value) { _title = value; Raise(); } }
    }

    public bool ManualTitle { get; set; }

    private string _description = "";
    public string Description
    {
        get => _description;
        set { if (_description != value) { _description = value; Raise(); } }
    }

    private string _colorName = "gray";
    public string ColorName
    {
        get => _colorName;
        set
        {
            if (_colorName == value) return;
            _colorName = value;
            _borderBrush = null;
            Raise();
            Raise(nameof(BorderBrush));
        }
    }

    private Brush? _borderBrush;
    public Brush BorderBrush
    {
        get
        {
            if (_borderBrush == null)
            {
                var color = ColorUtil.TryParse(_colorName, out var c) ? c : Colors.Gray;
                _borderBrush = new SolidColorBrush(color);
                _borderBrush.Freeze();
            }
            return _borderBrush;
        }
    }

    private string _processName = "";
    public string ProcessName
    {
        get => _processName;
        set { if (_processName != value) { _processName = value; Raise(); } }
    }

    public string TitlePattern { get; set; } = "";

    private TileState _state = TileState.Disconnected;
    public TileState State
    {
        get => _state;
        set { if (_state != value) { _state = value; Raise(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
