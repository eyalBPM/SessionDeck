using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using SessionDeck.Services;

namespace SessionDeck.ViewModels;

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

    // Blinking border (SPEC §F2): alternates between ColorName and AltColorName.
    private string? _altColorName;
    public string? AltColorName
    {
        get => _altColorName;
        set
        {
            if (_altColorName == value) return;
            _altColorName = value;
            _altBrush = null;
            if (value == null) _altPhase = false;
            Raise();
            Raise(nameof(BorderBrush));
        }
    }

    public int BlinkIntervalMs { get; set; } = 500;

    private bool _altPhase;
    /// <summary>Set by the shared BlinkEngine; true while the alternate color is shown.</summary>
    public bool AltPhase
    {
        get => _altPhase;
        set
        {
            if (_altPhase == value) return;
            _altPhase = value;
            Raise(nameof(BorderBrush));
        }
    }

    private Brush? _borderBrush;
    private Brush? _altBrush;
    public Brush BorderBrush
    {
        get
        {
            if (_altPhase && _altColorName != null)
                return _altBrush ??= MakeBrush(_altColorName);
            return _borderBrush ??= MakeBrush(_colorName);
        }
    }

    private static Brush MakeBrush(string name)
    {
        var brush = new SolidColorBrush(ColorUtil.TryParse(name, out var c) ? c : Colors.Gray);
        brush.Freeze();
        return brush;
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
