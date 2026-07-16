using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WinGrid.Interop;
using WinGrid.ViewModels;

namespace WinGrid;

/// <summary>
/// One grid tile: chrome (colored border + title bar) drawn by WPF, live preview drawn by
/// the DWM compositor into ThumbArea's client rect (SPEC §F1 — zero-CPU, no injection).
/// </summary>
public partial class TileView : UserControl
{
    private IntPtr _thumb;
    private IntPtr _thumbSource;
    private RECT _lastDest;
    private TileViewModel? _vm;

    private Point _downPos;
    private bool _mouseDown;
    private bool _dragging;

    private TileViewModel? Vm => DataContext as TileViewModel;
    private MainWindow? Owner => Window.GetWindow(this) as MainWindow;

    public TileView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => { LayoutUpdated += OnLayoutUpdated; RefreshThumbnail(); };
        Unloaded += (_, _) => { LayoutUpdated -= OnLayoutUpdated; UnregisterThumbnail(); };
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm != null) _vm.PropertyChanged -= OnVmChanged;
        _vm = Vm;
        if (_vm != null) _vm.PropertyChanged += OnVmChanged;
        RefreshThumbnail();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TileViewModel.Hwnd) or nameof(TileViewModel.State))
            RefreshThumbnail();
    }

    private void OnLayoutUpdated(object? sender, EventArgs e) => RefreshThumbnail();

    // ---- DWM thumbnail ----

    private void RefreshThumbnail()
    {
        var vm = Vm;
        if (!IsLoaded || vm == null || vm.State != TileState.Connected ||
            vm.Hwnd == IntPtr.Zero || !NativeMethods.IsWindow(vm.Hwnd))
        {
            UnregisterThumbnail();
            return;
        }

        if (PresentationSource.FromVisual(this) is not HwndSource source)
            return;

        if (_thumb == IntPtr.Zero || _thumbSource != vm.Hwnd)
        {
            UnregisterThumbnail();
            if (NativeMethods.DwmRegisterThumbnail(source.Handle, vm.Hwnd, out _thumb) != 0)
            {
                _thumb = IntPtr.Zero;
                return;
            }
            _thumbSource = vm.Hwnd;
        }

        RECT dest = ComputeDestRect(source.Handle);
        if (dest.Left == _lastDest.Left && dest.Top == _lastDest.Top &&
            dest.Right == _lastDest.Right && dest.Bottom == _lastDest.Bottom)
            return;
        _lastDest = dest;

        var props = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = NativeMethods.DWM_TNP_RECTDESTINATION | NativeMethods.DWM_TNP_VISIBLE | NativeMethods.DWM_TNP_OPACITY,
            rcDestination = dest,
            fVisible = true,
            opacity = 255,
        };
        NativeMethods.DwmUpdateThumbnailProperties(_thumb, ref props);
    }

    /// <summary>ThumbArea rect in the main window's client coordinates (device px), letterboxed
    /// to the source window's aspect ratio (SPEC §F1).</summary>
    private RECT ComputeDestRect(IntPtr mainHwnd)
    {
        Point tl = ThumbArea.PointToScreen(new Point(0, 0));
        Point br = ThumbArea.PointToScreen(new Point(ThumbArea.ActualWidth, ThumbArea.ActualHeight));

        var p1 = new POINT { X = (int)tl.X, Y = (int)tl.Y };
        var p2 = new POINT { X = (int)br.X, Y = (int)br.Y };
        NativeMethods.ScreenToClient(mainHwnd, ref p1);
        NativeMethods.ScreenToClient(mainHwnd, ref p2);

        int dw = p2.X - p1.X, dh = p2.Y - p1.Y;
        if (dw > 0 && dh > 0 &&
            NativeMethods.DwmQueryThumbnailSourceSize(_thumb, out SIZE src) == 0 && src.Cx > 0 && src.Cy > 0)
        {
            double scale = Math.Min((double)dw / src.Cx, (double)dh / src.Cy);
            int w = (int)(src.Cx * scale), h = (int)(src.Cy * scale);
            p1.X += (dw - w) / 2;
            p1.Y += (dh - h) / 2;
            p2.X = p1.X + w;
            p2.Y = p1.Y + h;
        }
        return new RECT { Left = p1.X, Top = p1.Y, Right = p2.X, Bottom = p2.Y };
    }

    private void UnregisterThumbnail()
    {
        if (_thumb != IntPtr.Zero)
        {
            NativeMethods.DwmUnregisterThumbnail(_thumb);
            _thumb = IntPtr.Zero;
            _thumbSource = IntPtr.Zero;
            _lastDest = default;
        }
    }

    // ---- mouse: click = Focus, drag = reorder (SPEC §F1/§F3) ----

    private void Root_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && FindAncestorButton(d) != null)
            return;
        _mouseDown = true;
        _dragging = false;
        _downPos = e.GetPosition(null);
        CaptureMouse();
    }

    private void Root_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_mouseDown || _dragging) return;
        Point pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _downPos.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(pos.Y - _downPos.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            _dragging = true;
            Mouse.OverrideCursor = Cursors.SizeAll;
        }
    }

    private void Root_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_mouseDown) return;
        _mouseDown = false;
        ReleaseMouseCapture();

        if (_dragging)
        {
            _dragging = false;
            Mouse.OverrideCursor = null;
            if (Vm != null && NativeMethods.GetCursorPos(out POINT pt))
                Owner?.HandleTileDrop(Vm, pt);
        }
        else if (Vm != null)
        {
            Owner?.HandleTileClick(Vm);
        }
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        if (Vm != null) Owner?.PinTile(Vm);
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (Vm != null) Owner?.RemoveTile(Vm);
    }

    private static Button? FindAncestorButton(DependencyObject? d)
    {
        while (d != null)
        {
            if (d is Button b) return b;
            d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }
}
