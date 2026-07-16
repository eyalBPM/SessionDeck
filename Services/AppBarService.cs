using System.Windows.Interop;
using WinGrid.Interop;
using WinGrid.Models;

namespace WinGrid.Services;

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

    public void Attach(HwndSource source)
    {
        _source = source;
        _hwnd = source.Handle;
        _callbackMsg = NativeMethods.RegisterWindowMessage("WinGrid_AppBarCallback");
        source.AddHook(WndProc);
    }

    public void Apply(ZoneMode mode, MonitorEntry monitor)
    {
        if (_hwnd == IntPtr.Zero) return;

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
        int halfWidth = mon.Width / 2;
        uint edge = _mode == ZoneMode.HalfRight ? NativeMethods.ABE_RIGHT : NativeMethods.ABE_LEFT;
        int width = _mode == ZoneMode.Full ? mon.Width : halfWidth;

        var abd = NewData();
        abd.uEdge = edge;
        abd.rc = new RECT { Left = mon.Left, Top = mon.Top, Right = mon.Right, Bottom = mon.Bottom };
        if (_mode == ZoneMode.HalfLeft) abd.rc.Right = mon.Left + width;
        else if (_mode == ZoneMode.HalfRight) abd.rc.Left = mon.Right - width;

        NativeMethods.SHAppBarMessage(NativeMethods.ABM_QUERYPOS, ref abd);
        // QUERYPOS may trim for the taskbar/other appbars; re-assert our width from the granted edge.
        if (edge == NativeMethods.ABE_LEFT) abd.rc.Right = Math.Min(abd.rc.Left + width, mon.Right);
        else abd.rc.Left = Math.Max(abd.rc.Right - width, mon.Left);

        NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETPOS, ref abd);
        NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero,
            abd.rc.Left, abd.rc.Top, abd.rc.Width, abd.rc.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
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
        return IntPtr.Zero;
    }
}
