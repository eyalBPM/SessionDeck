using System.Windows.Interop;
using SessionDeck.Interop;
using SessionDeck.Models;

namespace SessionDeck.Services;

/// <summary>
/// Reserved Zone via the AppBar API (SPEC §F4): the main window docks to a monitor edge
/// and the OS shrinks the work area — maximized/snapped windows stay out, the mouse moves freely.
/// </summary>
public sealed class AppBarService
{
    private IntPtr _hwnd;
    private HwndSource? _source;
    private uint _callbackMsg;
    private bool _registered;
    private ZoneMode _mode = ZoneMode.Off;
    private MonitorEntry? _monitor;
    private RECT _savedBounds;
    private bool _hasSavedBounds;
    private bool _selfPositioning;
    private double _customFraction = 1.0 / 3;

    public void Attach(HwndSource source)
    {
        _source = source;
        _hwnd = source.Handle;
        _callbackMsg = NativeMethods.RegisterWindowMessage("SessionDeck_AppBarCallback");
        source.AddHook(WndProc);
    }

    public void Apply(ZoneMode mode, MonitorEntry monitor, double customFraction = 1.0 / 3)
    {
        if (_hwnd == IntPtr.Zero) return;
        _customFraction = Math.Clamp(customFraction, 0.05, 1.0);

        if (mode == ZoneMode.Off)
        {
            Remove();
            return;
        }

        if (!_registered)
        {
            SaveWindowBounds();
            var abdNew = NewData();
            abdNew.uCallbackMessage = _callbackMsg;
            NativeMethods.SHAppBarMessage(NativeMethods.ABM_NEW, ref abdNew);
            _registered = true;
        }

        _mode = mode;
        _monitor = monitor;
        SetPosition();
    }

    public void Remove()
    {
        _mode = ZoneMode.Off;
        if (!_registered) return;
        var abd = NewData();
        NativeMethods.SHAppBarMessage(NativeMethods.ABM_REMOVE, ref abd);
        _registered = false;
        if (_hasSavedBounds)
        {
            NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero,
                _savedBounds.Left, _savedBounds.Top, _savedBounds.Width, _savedBounds.Height,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        }
    }

    private void SetPosition()
    {
        if (_monitor is null) return;
        RECT mon = _monitor.Bounds;
        bool rightEdge = _mode is ZoneMode.HalfRight or ZoneMode.QuarterRight or ZoneMode.CustomRight;
        uint edge = rightEdge ? NativeMethods.ABE_RIGHT : NativeMethods.ABE_LEFT;
        int width = _mode switch
        {
            ZoneMode.Full => mon.Width,
            ZoneMode.QuarterLeft or ZoneMode.QuarterRight => mon.Width / 4,
            ZoneMode.CustomLeft or ZoneMode.CustomRight =>
                Math.Clamp((int)Math.Round(mon.Width * _customFraction), 50, mon.Width),
            _ => mon.Width / 2,
        };

        var abd = NewData();
        abd.uEdge = edge;
        abd.rc = new RECT { Left = mon.Left, Top = mon.Top, Right = mon.Right, Bottom = mon.Bottom };
        if (_mode != ZoneMode.Full)
        {
            if (rightEdge) abd.rc.Left = mon.Right - width;
            else abd.rc.Right = mon.Left + width;
        }

        NativeMethods.SHAppBarMessage(NativeMethods.ABM_QUERYPOS, ref abd);
        // QUERYPOS may trim for the taskbar/other appbars; re-assert our width from the granted edge.
        if (edge == NativeMethods.ABE_LEFT) abd.rc.Right = Math.Min(abd.rc.Left + width, mon.Right);
        else abd.rc.Left = Math.Max(abd.rc.Right - width, mon.Left);

        NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETPOS, ref abd);
        // Win10/11 windows carry invisible resize borders: the visible (DWM) frame is
        // inset a few px from the window rect on the left/right/bottom, so placing the
        // window rect exactly on the zone leaves visible gaps. Inflate by the inset so
        // the VISIBLE frame fills the zone — the same compensation the OS applies to
        // maximized windows. The appbar reservation itself stays abd.rc, so neighbors
        // still align to the zone edge and only the transparent border overlaps them.
        RECT inset = GetInvisibleFrameInset();
        _selfPositioning = true;
        try
        {
            NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero,
                abd.rc.Left - inset.Left, abd.rc.Top - inset.Top,
                abd.rc.Width + inset.Left + inset.Right,
                abd.rc.Height + inset.Top + inset.Bottom,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        }
        finally { _selfPositioning = false; }
    }

    /// <summary>Per-side inset of the visible (DWM extended-frame) bounds within the
    /// window rect — i.e. the invisible resize-border thickness. Zero on failure.</summary>
    private RECT GetInvisibleFrameInset()
    {
        if (NativeMethods.GetWindowRect(_hwnd, out RECT wr) &&
            NativeMethods.DwmGetWindowAttribute(_hwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                out RECT fr, System.Runtime.InteropServices.Marshal.SizeOf<RECT>()) == 0)
        {
            return new RECT
            {
                Left = Math.Max(0, fr.Left - wr.Left),
                Top = Math.Max(0, fr.Top - wr.Top),
                Right = Math.Max(0, wr.Right - fr.Right),
                Bottom = Math.Max(0, wr.Bottom - fr.Bottom),
            };
        }
        return default;
    }

    private void SaveWindowBounds()
    {
        if (_source?.RootVisual is System.Windows.Window w)
        {
            // Convert DIPs to device px via the window's current DPI.
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(w);
            _savedBounds = new RECT
            {
                Left = (int)(w.Left * dpi.DpiScaleX),
                Top = (int)(w.Top * dpi.DpiScaleY),
                Right = (int)((w.Left + w.ActualWidth) * dpi.DpiScaleX),
                Bottom = (int)((w.Top + w.ActualHeight) * dpi.DpiScaleY),
            };
            _hasSavedBounds = true;
        }
    }

    private APPBARDATA NewData() => new()
    {
        cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<APPBARDATA>(),
        hWnd = _hwnd,
    };

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_registered && msg == (int)_callbackMsg && wParam.ToInt64() == NativeMethods.ABN_POSCHANGED)
        {
            SetPosition();
            handled = true;
        }
        else if (_mode != ZoneMode.Off && msg == NativeMethods.WM_SYSCOMMAND)
        {
            // While zoned the window is locked in place: swallow caption-drag, border-resize
            // and caption double-click maximize. Minimize/restore stay allowed.
            long cmd = wParam.ToInt64() & 0xFFF0;
            if (cmd is NativeMethods.SC_MOVE or NativeMethods.SC_SIZE or NativeMethods.SC_MAXIMIZE)
                handled = true;
        }
        else if (_mode != ZoneMode.Off && !_selfPositioning && msg == NativeMethods.WM_WINDOWPOSCHANGING)
        {
            // Hard lock against programmatic moves (Win+Shift+Arrow, snap, etc.) — only our own
            // SetPosition (guarded by _selfPositioning) may reposition the window.
            // Minimize (x/y = -32000) and restore-from-minimized are left alone.
            if (!NativeMethods.IsIconic(hwnd))
            {
                var wp = System.Runtime.InteropServices.Marshal.PtrToStructure<WINDOWPOS>(lParam);
                if (wp.x != -32000 || wp.y != -32000)
                {
                    wp.flags |= NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE;
                    System.Runtime.InteropServices.Marshal.StructureToPtr(wp, lParam, false);
                }
            }
        }
        return IntPtr.Zero;
    }
}
