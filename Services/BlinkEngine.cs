using System.Collections.ObjectModel;
using System.Windows.Threading;
using SessionDeck.ViewModels;

namespace SessionDeck.Services;

/// <summary>
/// One shared DispatcherTimer for all blinking borders (SPEC §5 — never a timer per tile).
/// Each tile's phase is derived from the wall clock and its own interval, so tiles with
/// different intervals coexist on the same tick.
/// </summary>
public sealed class BlinkEngine
{
    private readonly ObservableCollection<TileViewModel> _tiles;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    public BlinkEngine(ObservableCollection<TileViewModel> tiles)
    {
        _tiles = tiles;
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>Call after any change that can start/stop blinking (border edits, add/remove).</summary>
    public void Refresh()
    {
        bool any = _tiles.Any(t => t.AltColorName != null);
        if (any && !_timer.IsEnabled) _timer.Start();
        else if (!any && _timer.IsEnabled)
        {
            _timer.Stop();
            foreach (var t in _tiles) t.AltPhase = false;
        }
    }

    private void Tick()
    {
        long now = Environment.TickCount64;
        bool any = false;
        foreach (var t in _tiles)
        {
            if (t.AltColorName == null) continue;
            any = true;
            int interval = Math.Max(100, t.BlinkIntervalMs);
            t.AltPhase = now / interval % 2 == 1;
        }
        if (!any) Refresh();
    }
}
