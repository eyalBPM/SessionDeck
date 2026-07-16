using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using WinGrid.Cli;
using WinGrid.Interop;
using WinGrid.Models;
using WinGrid.Services;
using WinGrid.ViewModels;

namespace WinGrid;

/// <summary>
/// Main controller: owns the view-model, services, zone/stage orchestration
/// and the operations shared by UI and CLI.
/// </summary>
public partial class MainWindow : Window
{
    public MainViewModel Vm { get; } = new();

    private readonly ConfigStore _configStore;
    private readonly WindowTracker _tracker = new();
    private readonly AppBarService _appBar = new();
    private readonly BlinkEngine _blink;
    private PipeServer? _pipe;
    private CommandExecutor? _executor;
    private List<MonitorEntry> _monitors;
    private bool _initializing = true;
    private bool _syncingUi;
    private bool _picking;

    public int MonitorCount => _monitors.Count;

    public MainWindow()
    {
        var config = ConfigStore.Load();
        InitializeComponent();
        DataContext = Vm;
        _configStore = new ConfigStore(BuildConfig);
        _blink = new BlinkEngine(Vm.Tiles);
        _monitors = MonitorService.GetMonitors();

        LoadFromConfig(config);
        PopulateCombos();
        _blink.Refresh();

        Vm.Tiles.CollectionChanged += (_, _) =>
        {
            UpdateGridColumns();
            UpdateEmptyHint();
            _blink.Refresh();
            QueueSave();
        };
        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => { UpdateGridColumns(); UpdateEmptyHint(); };
        Closing += OnClosing;
        LocationChanged += (_, _) => { if (Vm.ZoneMode == ZoneMode.Off) QueueSave(); };
        SizeChanged += (_, _) => { if (Vm.ZoneMode == ZoneMode.Off) QueueSave(); };

        _initializing = false;
    }

    // ---- startup / shutdown ----

    private void LoadFromConfig(AppConfig config)
    {
        Vm.NextTileId = Math.Max(1, config.NextTileId);
        foreach (var tc in config.Tiles)
        {
            Vm.Tiles.Add(new TileViewModel
            {
                Id = tc.Id,
                Title = tc.Title,
                ManualTitle = tc.ManualTitle,
                Description = tc.Description,
                ColorName = tc.Color,
                AltColorName = tc.AltColor,
                BlinkIntervalMs = tc.BlinkIntervalMs,
                ProcessName = tc.ProcessName,
                TitlePattern = tc.TitlePattern,
                State = TileState.Disconnected,
            });
        }

        if (ModeNames.TryParseZone(config.Zone.Mode, out var zm)) Vm.ZoneMode = zm;
        Vm.ZoneMonitor = Math.Clamp(config.Zone.Monitor, 0, _monitors.Count - 1);
        if (ModeNames.TryParseStage(config.Stage.Mode, out var sm)) Vm.StageMode = sm;
        Vm.StageMonitor = Math.Clamp(config.Stage.Monitor, 0, _monitors.Count - 1);
        Vm.StageRect = ParseRect(config.Stage.Rect);
        if (Vm.StageMode == StageMode.Rect && Vm.StageRect == null) Vm.StageMode = StageMode.HalfRight;
        Vm.AutoRemoveDisconnected = config.AutoRemoveDisconnected;

        if (config.Window is { } wb && wb.W > 100 && wb.H > 100)
        {
            Left = wb.X; Top = wb.Y; Width = wb.W; Height = wb.H;
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (PresentationSource.FromVisual(this) is not HwndSource source) return;

        _appBar.Attach(source);
        _tracker.TitleChanged += OnWindowTitleChanged;
        _tracker.WindowDestroyed += OnWindowDestroyed;
        _tracker.WindowAppeared += TryRebindWindow;
        _tracker.MoveSizeEnded += HandleDragIn;
        _tracker.Start();

        RebindAll();

        if (Vm.ZoneMode != ZoneMode.Off)
            ApplyZone(Vm.ZoneMonitor, Vm.ZoneMode, save: false);

        _executor = new CommandExecutor(this);
        _pipe = new PipeServer(argv => Dispatcher.Invoke(() => _executor.Execute(argv)));
        _pipe.Start();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _configStore.SaveNow();
        _pipe?.Dispose();
        _tracker.Dispose();
        _appBar.Remove();
    }

    // ---- persistence (SPEC §F7) ----

    private AppConfig BuildConfig()
    {
        var cfg = new AppConfig
        {
            NextTileId = Vm.NextTileId,
            Zone = new ZoneConfig { Monitor = Vm.ZoneMonitor, Mode = ModeNames.ToName(Vm.ZoneMode) },
            Stage = new StageConfig
            {
                Monitor = Vm.StageMonitor,
                Mode = ModeNames.ToName(Vm.StageMode),
                Rect = Vm.StageRect is { } r ? $"{r.Left},{r.Top},{r.Width},{r.Height}" : null,
            },
            AutoRemoveDisconnected = Vm.AutoRemoveDisconnected,
        };
        foreach (var t in Vm.Tiles)
        {
            cfg.Tiles.Add(new TileConfig
            {
                Id = t.Id,
                ProcessName = t.ProcessName,
                TitlePattern = t.TitlePattern,
                Title = t.Title,
                ManualTitle = t.ManualTitle,
                Description = t.Description,
                Color = t.ColorName,
                AltColor = t.AltColorName,
                BlinkIntervalMs = t.BlinkIntervalMs,
            });
        }
        if (Vm.ZoneMode == ZoneMode.Off && WindowState == WindowState.Normal)
            cfg.Window = new WindowBounds { X = Left, Y = Top, W = Width, H = Height };
        return cfg;
    }

    public void QueueSave()
    {
        if (!_initializing) _configStore.QueueSave();
    }

    // ---- tiles: add / remove / bind ----

    public TileViewModel AddTile(string pattern, string process, string desc, string color, CandidateWindow? window)
    {
        var tile = new TileViewModel
        {
            Id = Vm.NextTileId++,
            TitlePattern = pattern,
            ProcessName = process,
            Description = desc,
            ColorName = color,
            Title = window?.Title ?? pattern,
            Hwnd = window?.Hwnd ?? IntPtr.Zero,
            State = window != null ? TileState.Connected : TileState.Disconnected,
        };
        Vm.Tiles.Add(tile);
        return tile;
    }

    public void RemoveTile(TileViewModel tile) => Vm.Tiles.Remove(tile);

    /// <summary>Re-bind loaded tiles to existing windows by Matcher (SPEC §F7).</summary>
    private void RebindAll()
    {
        var candidates = WindowEnumerator.GetCandidates();
        var used = new HashSet<IntPtr>(Vm.Tiles.Where(t => t.Hwnd != IntPtr.Zero).Select(t => t.Hwnd));

        foreach (var tile in Vm.Tiles.Where(t => t.State == TileState.Disconnected))
        {
            CandidateWindow? match = null;
            foreach (var c in candidates)
            {
                if (used.Contains(c.Hwnd)) continue;
                if (tile.ProcessName.Length > 0 &&
                    !string.Equals(c.ProcessName, tile.ProcessName, StringComparison.OrdinalIgnoreCase)) continue;
                bool titleOk;
                try { titleOk = Regex.IsMatch(c.Title, tile.TitlePattern); }
                catch { titleOk = false; }
                if (!titleOk) continue;
                match = c;
                break;
            }
            if (match == null) continue;

            used.Add(match.Hwnd);
            tile.Hwnd = match.Hwnd;
            tile.ProcessName = match.ProcessName;
            if (!tile.ManualTitle) tile.Title = match.Title;
            tile.State = TileState.Connected;
        }
    }

    // ---- window tracking (SPEC §F2/§F6) ----

    private void OnWindowTitleChanged(IntPtr hwnd, string newTitle)
    {
        var tile = Vm.FindByHwnd(hwnd);
        if (tile == null)
        {
            // A title change can make an unbound window match a disconnected tile's Matcher.
            if (newTitle.Length > 0) TryRebindWindow(hwnd);
            return;
        }
        if (newTitle.Length == 0) return;
        if (!tile.ManualTitle && tile.Title != newTitle)
        {
            tile.Title = newTitle;
            QueueSave();
        }
    }

    private void OnWindowDestroyed(IntPtr hwnd)
    {
        var tile = Vm.FindByHwnd(hwnd);
        if (tile == null) return;
        if (Vm.AutoRemoveDisconnected)
        {
            RemoveTile(tile);   // F6 option (off by default); CollectionChanged persists
            return;
        }
        tile.Hwnd = IntPtr.Zero;
        tile.State = TileState.Disconnected;
        QueueSave();
    }

    /// <summary>Automatic re-bind (SPEC §F6): a new/renamed window that matches a
    /// disconnected tile's Matcher reconnects that tile.</summary>
    private void TryRebindWindow(IntPtr hwnd)
    {
        if (Vm.FindByHwnd(hwnd) != null) return;
        if (!Vm.Tiles.Any(t => t.State == TileState.Disconnected)) return;
        if (NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT) != hwnd) return;
        if (!WindowEnumerator.IsEligible(hwnd, Environment.ProcessId)) return;

        string title = NativeMethods.GetWindowTextSafe(hwnd);
        string process = WindowEnumerator.GetProcessName(hwnd);

        foreach (var tile in Vm.Tiles.Where(t => t.State == TileState.Disconnected))
        {
            if (tile.ProcessName.Length > 0 &&
                !string.Equals(process, tile.ProcessName, StringComparison.OrdinalIgnoreCase)) continue;
            bool titleOk;
            try { titleOk = Regex.IsMatch(title, tile.TitlePattern); }
            catch { titleOk = false; }
            if (!titleOk) continue;

            tile.Hwnd = hwnd;
            tile.ProcessName = process;
            if (!tile.ManualTitle) tile.Title = title;
            tile.State = TileState.Connected;
            SetStatus($"אריח {tile.Id} התחבר מחדש: {title}");
            QueueSave();
            return;
        }
    }

    /// <summary>Drag-in (SPEC §F5 stage B): a foreign window whose move-drag ended with the
    /// cursor over WinGrid gets added as a tile (or reconnects a matching disconnected tile).</summary>
    private void HandleDragIn(IntPtr hwnd)
    {
        if (!IsVisible || !NativeMethods.GetCursorPos(out POINT pt)) return;
        if (PresentationSource.FromVisual(this) is not HwndSource source) return;
        if (!NativeMethods.GetWindowRect(source.Handle, out RECT self)) return;
        if (pt.X < self.Left || pt.X >= self.Right || pt.Y < self.Top || pt.Y >= self.Bottom) return;

        IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        if (Vm.FindByHwnd(root) != null) return;
        if (!WindowEnumerator.IsEligible(root, Environment.ProcessId)) return;

        TryRebindWindow(root);
        if (Vm.FindByHwnd(root) != null) return;   // reconnected an existing tile

        string title = NativeMethods.GetWindowTextSafe(root);
        var candidate = new CandidateWindow(root, title, WindowEnumerator.GetProcessName(root));
        var tile = AddTile("^" + Regex.Escape(title) + "$", candidate.ProcessName, "", "gray", candidate);
        SetStatus($"נוסף אריח {tile.Id} (גרירה): {title}");
    }

    // ---- focus / pin / stage (SPEC §F3) ----

    public (bool, string) FocusTile(TileViewModel tile)
    {
        if (tile.State != TileState.Connected || !NativeMethods.IsWindow(tile.Hwnd))
            return (false, $"tile {tile.Id} is disconnected");
        WindowActions.Focus(tile.Hwnd);
        return (true, "");
    }

    public (bool, string) PinTile(TileViewModel tile)
    {
        if (tile.State != TileState.Connected || !NativeMethods.IsWindow(tile.Hwnd))
            return (false, $"tile {tile.Id} is disconnected");
        WindowActions.MoveTo(tile.Hwnd, GetStageRect());
        return (true, "");
    }

    /// <summary>Stage rect from the target monitor's work area — respects taskbar and our own zone.</summary>
    private RECT GetStageRect()
    {
        if (Vm.StageMode == StageMode.Rect && Vm.StageRect is { } custom)
            return custom;
        _monitors = MonitorService.GetMonitors();
        var mon = _monitors[Math.Clamp(Vm.StageMonitor, 0, _monitors.Count - 1)];
        RECT work = mon.WorkArea;
        return Vm.StageMode switch
        {
            StageMode.HalfLeft => new RECT { Left = work.Left, Top = work.Top, Right = work.Left + work.Width / 2, Bottom = work.Bottom },
            StageMode.HalfRight => new RECT { Left = work.Left + work.Width / 2, Top = work.Top, Right = work.Right, Bottom = work.Bottom },
            _ => work,
        };
    }

    public static RECT? ParseRect(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var parts = s.Split(',');
        if (parts.Length != 4) return null;
        if (!int.TryParse(parts[0], out int x) || !int.TryParse(parts[1], out int y) ||
            !int.TryParse(parts[2], out int w) || !int.TryParse(parts[3], out int h) || w <= 0 || h <= 0)
            return null;
        return new RECT { Left = x, Top = y, Right = x + w, Bottom = y + h };
    }

    public void EditTile(TileViewModel tile)
    {
        var dialog = new EditTileDialog(tile) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _blink.Refresh();
            QueueSave();
        }
    }

    /// <summary>Called by the CLI after border changes that may start/stop blinking.</summary>
    public void RefreshBlink() => _blink.Refresh();

    /// <summary>Stage definition from the CLI (SPEC §F3): monitor + full/half, or a custom rect.</summary>
    public void SetStage(int monitor, StageMode mode, RECT? rect)
    {
        Vm.StageMonitor = Math.Clamp(monitor, 0, _monitors.Count - 1);
        Vm.StageMode = mode;
        if (mode == StageMode.Rect) Vm.StageRect = rect;
        SyncCombosFromVm();
        QueueSave();
    }

    // ---- Reserved Zone (SPEC §F4) ----

    public void ApplyZone(int monitor, ZoneMode mode, bool save = true)
    {
        _monitors = MonitorService.GetMonitors();
        monitor = Math.Clamp(monitor, 0, _monitors.Count - 1);
        Vm.ZoneMonitor = monitor;
        Vm.ZoneMode = mode;
        _appBar.Apply(mode, _monitors[monitor]);
        SyncCombosFromVm();
        if (save) QueueSave();
    }

    // ---- UI: toolbar ----

    private void PopulateCombos()
    {
        _syncingUi = true;
        foreach (var combo in new[] { ZoneMonitorCombo, StageMonitorCombo })
        {
            combo.Items.Clear();
            foreach (var m in _monitors) combo.Items.Add(m.DisplayName);
        }
        ZoneModeCombo.Items.Clear();
        foreach (var name in new[] { "כבוי", "חצי שמאל", "חצי ימין", "מסך מלא" }) ZoneModeCombo.Items.Add(name);
        StageModeCombo.Items.Clear();
        foreach (var name in new[] { "מסך מלא", "חצי שמאל", "חצי ימין", "מלבן (CLI)" }) StageModeCombo.Items.Add(name);
        StartupMenuItem.IsChecked = StartupService.IsEnabled();
        AutoRemoveMenuItem.IsChecked = Vm.AutoRemoveDisconnected;
        _syncingUi = false;
        SyncCombosFromVm();
    }

    private void SyncCombosFromVm()
    {
        _syncingUi = true;
        ZoneMonitorCombo.SelectedIndex = Vm.ZoneMonitor;
        ZoneModeCombo.SelectedIndex = (int)Vm.ZoneMode;
        StageMonitorCombo.SelectedIndex = Vm.StageMonitor;
        StageModeCombo.SelectedIndex = (int)Vm.StageMode;
        _syncingUi = false;
    }

    private void ZoneUi_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingUi || _initializing) return;
        if (ZoneMonitorCombo.SelectedIndex < 0 || ZoneModeCombo.SelectedIndex < 0) return;
        ApplyZone(ZoneMonitorCombo.SelectedIndex, (ZoneMode)ZoneModeCombo.SelectedIndex);
    }

    private void StageUi_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingUi || _initializing) return;
        if (StageMonitorCombo.SelectedIndex < 0 || StageModeCombo.SelectedIndex < 0) return;
        var mode = (StageMode)StageModeCombo.SelectedIndex;
        if (mode == StageMode.Rect && Vm.StageRect == null)
        {
            // Custom rect can only be defined via CLI (wingrid stage --rect x,y,w,h).
            SetStatus("מלבן מותאם מוגדר רק דרך CLI: wingrid stage --rect x,y,w,h");
            SyncCombosFromVm();
            return;
        }
        Vm.StageMonitor = StageMonitorCombo.SelectedIndex;
        Vm.StageMode = mode;
        QueueSave();
    }

    // ---- settings (SPEC §F6/§F9) ----

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        SettingsButton.ContextMenu.PlacementTarget = SettingsButton;
        SettingsButton.ContextMenu.IsOpen = true;
    }

    private void StartupMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StartupService.SetEnabled(StartupMenuItem.IsChecked);
        }
        catch (Exception ex)
        {
            SetStatus("שגיאה בעדכון ה-startup: " + ex.Message);
            StartupMenuItem.IsChecked = StartupService.IsEnabled();
        }
    }

    private void AutoRemoveMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Vm.AutoRemoveDisconnected = AutoRemoveMenuItem.IsChecked;
        QueueSave();
    }

    // ---- picker (SPEC §F5): drag the crosshair grip onto a window, release to add ----

    private void PickerGrip_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _picking = PickerGrip.CaptureMouse();
        if (_picking) Mouse.OverrideCursor = Cursors.Cross;
        e.Handled = true;
    }

    private void PickerGrip_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_picking) return;
        _picking = false;
        PickerGrip.ReleaseMouseCapture();
        Mouse.OverrideCursor = null;
        e.Handled = true;

        if (!NativeMethods.GetCursorPos(out POINT pt)) return;
        IntPtr hit = NativeMethods.WindowFromPoint(pt);
        if (hit == IntPtr.Zero) return;
        IntPtr root = NativeMethods.GetAncestor(hit, NativeMethods.GA_ROOT);

        if (!WindowEnumerator.IsEligible(root, Environment.ProcessId))
        {
            SetStatus("החלון שנבחר אינו תקף (WinGrid עצמו / ללא כותרת / tool window)");
            return;
        }
        if (Vm.FindByHwnd(root) != null)
        {
            SetStatus("החלון הזה כבר נמצא ב-grid");
            return;
        }

        string title = NativeMethods.GetWindowTextSafe(root);
        var candidate = new CandidateWindow(root, title, WindowEnumerator.GetProcessName(root));
        var tile = AddTile("^" + Regex.Escape(title) + "$", candidate.ProcessName, "", "gray", candidate);
        SetStatus($"נוסף אריח {tile.Id}: {title}");
    }

    private void SetStatus(string message) => StatusText.Text = message;

    // ---- grid layout + reorder (SPEC §F1) ----

    private void TilesHost_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateGridColumns();

    private void UpdateGridColumns()
    {
        int n = Vm.Tiles.Count;
        double w = TilesHost.ActualWidth, h = TilesHost.ActualHeight;
        if (n <= 1 || w <= 0 || h <= 0)
        {
            Vm.GridColumns = 1;
            return;
        }
        // Pick the column count that maximizes the letterboxed (16:9) tile area.
        const double aspect = 16.0 / 9.0;
        const double titleBar = 30;
        double bestArea = -1;
        int bestCols = 1;
        for (int cols = 1; cols <= n; cols++)
        {
            int rows = (n + cols - 1) / cols;
            double tw = w / cols, th = h / rows - titleBar;
            if (th <= 0) continue;
            double effW = Math.Min(tw, th * aspect);
            double area = effW * effW / aspect;
            if (area > bestArea) { bestArea = area; bestCols = cols; }
        }
        Vm.GridColumns = bestCols;
    }

    private void UpdateEmptyHint()
        => EmptyHint.Visibility = Vm.Tiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public void HandleTileClick(TileViewModel tile)
    {
        var (ok, msg) = FocusTile(tile);
        if (!ok) SetStatus(msg);
    }

    public void HandleTileDrop(TileViewModel tile, POINT screenPt)
    {
        int from = Vm.Tiles.IndexOf(tile);
        if (from < 0) return;
        int to = HitTestTileIndex(screenPt);
        if (to < 0 || to == from) return;
        Vm.Tiles.Move(from, to);
    }

    private int HitTestTileIndex(POINT screenPt)
    {
        for (int i = 0; i < Vm.Tiles.Count; i++)
        {
            if (TilesHost.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement fe || !fe.IsLoaded)
                continue;
            Point tl = fe.PointToScreen(new Point(0, 0));
            Point br = fe.PointToScreen(new Point(fe.ActualWidth, fe.ActualHeight));
            if (screenPt.X >= tl.X && screenPt.X < br.X && screenPt.Y >= tl.Y && screenPt.Y < br.Y)
                return i;
        }
        return -1;
    }

    // ---- CLI ----

    public void ActivateFromCli()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
    }
}
