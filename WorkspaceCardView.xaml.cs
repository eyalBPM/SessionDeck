using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using SessionDeck.Interop;
using SessionDeck.ViewModels;

namespace SessionDeck;

/// <summary>
/// One workspace card: chrome (Peacock-colored border, header, session cards) drawn by WPF;
/// the live preview of the bound VSCode window is drawn by the DWM compositor into
/// ThumbArea's client rect (SPEC §F1 — zero-CPU, no injection).
/// </summary>
public partial class WorkspaceCardView : UserControl
{
    private IntPtr _thumb;
    private IntPtr _thumbSource;
    private RECT _lastDest;
    private WorkspaceViewModel? _vm;

    private WorkspaceViewModel? Vm => DataContext as WorkspaceViewModel;
    private MainWindow? Owner => Window.GetWindow(this) as MainWindow;

    public WorkspaceCardView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => { LayoutUpdated += OnLayoutUpdated; RefreshThumbnail(); SyncExpandGlyph(); SyncHideGlyph(); };
        Unloaded += (_, _) => { LayoutUpdated -= OnLayoutUpdated; UnregisterThumbnail(); };
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm != null) _vm.PropertyChanged -= OnVmChanged;
        _vm = Vm;
        if (_vm != null) _vm.PropertyChanged += OnVmChanged;
        RefreshThumbnail();
        SyncExpandGlyph();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkspaceViewModel.Hwnd) or nameof(WorkspaceViewModel.State))
            RefreshThumbnail();
        else if (e.PropertyName is nameof(WorkspaceViewModel.Expanded))
            SyncExpandGlyph();
        else if (e.PropertyName is nameof(WorkspaceViewModel.Hidden))
            SyncHideGlyph();
    }

    private void OnLayoutUpdated(object? sender, EventArgs e) => RefreshThumbnail();

    // ---- DWM thumbnail (same approach as stage A/B tiles) ----

    private void RefreshThumbnail()
    {
        var vm = Vm;
        if (!IsLoaded || vm == null || vm.State != BindState.Connected ||
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

    // ---- interactions ----

    private void Card_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && FindAncestorButton(d) != null) return;
        if (Vm != null) Owner?.FocusWorkspace(Vm);
    }

    private void Session_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SessionViewModel session } && Vm != null)
        {
            Owner?.HandleSessionClick(Vm, session);
            e.Handled = true;
        }
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Vm != null) Owner?.EditWorkspace(Vm);
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        if (Vm != null) Owner?.PinWorkspace(Vm);
    }

    private void Expand_Click(object sender, RoutedEventArgs e)
    {
        if (Vm != null) Vm.Expanded = !Vm.Expanded;
    }

    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        if (Vm != null) Owner?.ToggleHideWorkspace(Vm);
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (Vm != null) Owner?.RemoveWorkspace(Vm);
    }

    private void SyncExpandGlyph()
    {
        if (Vm is { } vm)
            ExpandButton.Content = vm.Expanded ? "▲" : "▼";
    }

    /// <summary>Hidden card (visible via the show-hidden toggle) must read as "unhide" (feedback 2026-07-19).</summary>
    private void SyncHideGlyph()
    {
        if (Vm is not { } vm) return;
        HideButton.Content = vm.Hidden ? "👁" : "🗕";
        HideButton.ToolTip = vm.Hidden ? "הצג חזרה בלוח" : "הסתר מהלוח";
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
