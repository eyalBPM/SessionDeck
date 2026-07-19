using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SessionDeck.ViewModels;

/// <summary>A user-defined toolbar toggle (feature 2026-07-19). State changes are pushed
/// to the flag file via <see cref="Changed"/> so external hook scripts can read them.</summary>
public sealed class CustomToggleViewModel : INotifyPropertyChanged
{
    public string Id { get; init; } = "";
    public string Icon { get; init; } = "🔘";
    public string Tooltip { get; init; } = "";

    private bool _enabled;
    public bool Enabled
    {
        get => _enabled;
        set { if (_enabled != value) { _enabled = value; Raise(); Changed?.Invoke(this); } }
    }

    public event Action<CustomToggleViewModel>? Changed;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
