using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using SessionDeck.Cli;
using SessionDeck.Interop;
using SessionDeck.Models;
using SessionDeck.Services;
using SessionDeck.ViewModels;

namespace SessionDeck;

/// <summary>
/// Main controller: owns the view-model, services, workspace/session engine,
/// zone/stage orchestration and the operations shared by UI and CLI.
/// </summary>
public partial class MainWindow : Window
{
    public MainViewModel Vm { get; } = new();

    private readonly ConfigStore _configStore;
    private readonly WindowTracker _tracker = new();
    private readonly AppBarService _appBar = new();
    private readonly BlinkEngine _blink;
    private readonly DispatcherTimer _metadataTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private PipeServer? _pipe;
    private CommandExecutor? _executor;
    private List<MonitorEntry> _monitors;
    private bool _initializing = true;
    private bool _syncingUi;

    // Legacy stage A/B tile data — round-tripped so nothing is lost (SPEC decision 15).
    private List<TileConfig> _legacyTiles = new();
    private int _legacyNextTileId = 1;
    private bool _legacyAutoRemove;

    private Dictionary<string, StatusStyle> _statusStyles = AppConfig.DefaultStatusStyles();

    // Live VSCode-extension connections (stage D). UI thread only (handlers are dispatched).
    private readonly List<VscodeConnection> _connectors = new();
    // A session click with no connector yet (VSCode still launching) parks here until the
    // extension's first sync for that workspace, then the open command is flushed to it.
    private readonly Dictionary<string, (string SessionId, DateTime At)> _pendingOpens = new();
    private static readonly TimeSpan PendingOpenTtl = TimeSpan.FromSeconds(90);
    private bool _titleScanRunning;

    public int MonitorCount => _monitors.Count;

    public MainWindow()
    {
        var config = ConfigStore.Load();
        InitializeComponent();
        DataContext = Vm;
        _configStore = new ConfigStore(BuildConfig);
        _blink = new BlinkEngine(() => Vm.AllSessions());
        _monitors = MonitorService.GetMonitors();

        LoadFromConfig(config);
        PopulateCombos();
        _blink.Refresh();

        Vm.Workspaces.CollectionChanged += (_, _) =>
        {
            UpdateEmptyHint();
            _blink.Refresh();
            QueueSave();
        };
        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => UpdateEmptyHint();
        Closing += OnClosing;
        LocationChanged += (_, _) => { if (Vm.ZoneMode == ZoneMode.Off) QueueSave(); };
        SizeChanged += (_, _) => { if (Vm.ZoneMode == ZoneMode.Off) QueueSave(); };
        _metadataTimer.Tick += (_, _) => RefreshAllMetadata();
        _metadataTimer.Start();

        _initializing = false;
    }

    // ---- startup / shutdown ----

    private void LoadFromConfig(AppConfig config)
    {
        _legacyTiles = config.Tiles;
        _legacyNextTileId = config.NextTileId;
        _legacyAutoRemove = config.AutoRemoveDisconnected;

        // Status→style mapping (SPEC decision 11): config overrides on top of defaults.
        _statusStyles = AppConfig.DefaultStatusStyles();
        foreach (var (key, style) in config.StatusStyles)
            _statusStyles[key.ToLowerInvariant()] = style;
        SessionViewModel.ResolveStyle = status =>
            _statusStyles.GetValueOrDefault(SessionStatusNames.ToName(status)) ?? new StatusStyle();

        Vm.NextWorkspaceId = Math.Max(1, config.NextWorkspaceId);
        Vm.ClosedSessionRetention = Math.Max(0, config.ClosedSessionRetention);
        Vm.OpenSessionMaximized = config.OpenSessionMaximized;
        Vm.ShowHidden = config.ShowHidden;

        foreach (var wc in config.Workspaces)
        {
            var ws = new WorkspaceViewModel
            {
                Id = wc.Id,
                Path = wc.Path,
                Name = wc.Name,
                CustomTitle = wc.CustomTitle,
                Description = wc.Description,
                CustomColor = wc.CustomColor,
                Hidden = wc.Hidden,
                State = BindState.Disconnected,
            };
            foreach (var sc in wc.Sessions)
            {
                if (!SessionStatusNames.TryParse(sc.Status, out var status)) status = SessionStatus.Idle;
                ws.Sessions.Add(new SessionViewModel
                {
                    SessionId = sc.SessionId,
                    CustomTitle = sc.CustomTitle,
                    Description = sc.Description,
                    Status = status,
                    Acknowledged = sc.Acknowledged,
                    Closed = sc.Closed,
                    StartedAt = sc.StartedAt,
                    EndedAt = sc.EndedAt,
                    Detail = sc.Detail,
                    TranscriptPath = sc.TranscriptPath,
                    Source = sc.Source,
                    PermissionMode = sc.PermissionMode,
                    EndReason = sc.EndReason,
                    LastEventAt = sc.LastEventAt,
                    AutoTitle = sc.AutoTitle,
                });
            }
            ws.RefreshSessionVisibility();
            RefreshMetadata(ws);
            Vm.Workspaces.Add(ws);
        }
        ApplyDeckVisibility();
        SortWorkspaces();

        if (ModeNames.TryParseZone(config.Zone.Mode, out var zm)) Vm.ZoneMode = zm;
        Vm.ZoneMonitor = Math.Clamp(config.Zone.Monitor, 0, _monitors.Count - 1);
        if (ModeNames.TryParseStage(config.Stage.Mode, out var sm)) Vm.StageMode = sm;
        Vm.StageMonitor = Math.Clamp(config.Stage.Monitor, 0, _monitors.Count - 1);
        Vm.StageRect = ParseRect(config.Stage.Rect);
        if (Vm.StageMode == StageMode.Rect && Vm.StageRect == null) Vm.StageMode = StageMode.HalfRight;

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
        _pipe = new PipeServer(
            argv => Dispatcher.Invoke(() => _executor.Execute(argv)),
            (sync, conn) => Dispatcher.BeginInvoke(() => OnVscodeSync(sync, conn)),
            conn => Dispatcher.BeginInvoke(() => OnVscodeClosed(conn)));
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
            NextTileId = _legacyNextTileId,
            Tiles = _legacyTiles,
            AutoRemoveDisconnected = _legacyAutoRemove,
            NextWorkspaceId = Vm.NextWorkspaceId,
            StatusStyles = _statusStyles,
            ClosedSessionRetention = Vm.ClosedSessionRetention,
            OpenSessionMaximized = Vm.OpenSessionMaximized,
            ShowHidden = Vm.ShowHidden,
            Zone = new ZoneConfig { Monitor = Vm.ZoneMonitor, Mode = ModeNames.ToName(Vm.ZoneMode) },
            Stage = new StageConfig
            {
                Monitor = Vm.StageMonitor,
                Mode = ModeNames.ToName(Vm.StageMode),
                Rect = Vm.StageRect is { } r ? $"{r.Left},{r.Top},{r.Width},{r.Height}" : null,
            },
        };
        foreach (var w in Vm.Workspaces)
        {
            var wc = new WorkspaceConfig
            {
                Id = w.Id,
                Path = w.Path,
                Name = w.Name,
                CustomTitle = w.CustomTitle,
                Description = w.Description,
                CustomColor = w.CustomColor,
                Hidden = w.Hidden,
            };
            foreach (var s in w.Sessions)
            {
                wc.Sessions.Add(new SessionConfig
                {
                    SessionId = s.SessionId,
                    CustomTitle = s.CustomTitle,
                    Description = s.Description,
                    Status = SessionStatusNames.ToName(s.Status),
                    Acknowledged = s.Acknowledged,
                    Closed = s.Closed,
                    StartedAt = s.StartedAt,
                    EndedAt = s.EndedAt,
                    Detail = s.Detail,
                    TranscriptPath = s.TranscriptPath,
                    Source = s.Source,
                    PermissionMode = s.PermissionMode,
                    EndReason = s.EndReason,
                    LastEventAt = s.LastEventAt,
                    AutoTitle = s.AutoTitle,
                });
            }
            cfg.Workspaces.Add(wc);
        }
        if (Vm.ZoneMode == ZoneMode.Off && WindowState == WindowState.Normal)
            cfg.Window = new WindowBounds { X = Left, Y = Top, W = Width, H = Height };
        return cfg;
    }

    public void QueueSave()
    {
        if (!_initializing) _configStore.QueueSave();
    }

    // ---- workspaces: add / remove / metadata (SPEC §2ב) ----

    /// <summary>Primary add flow (SPEC decision 21.1): pick a project folder.</summary>
    public (WorkspaceViewModel?, string?) AddWorkspaceFromPath(string path)
    {
        if (!Directory.Exists(path))
            return (null, $"folder not found: {path}");
        if (Vm.FindByPath(path) is { } existing)
            return (null, $"workspace \"{existing.DisplayTitle}\" already on the deck (id {existing.Id})");

        var ws = new WorkspaceViewModel
        {
            Id = Vm.NextWorkspaceId++,
            Path = Path.GetFullPath(path),
            Name = WorkspaceMetadata.NameFromPath(path),
        };
        RefreshMetadata(ws);
        Vm.Workspaces.Add(ws);
        TryBindWorkspace(ws);
        ApplyDeckVisibility();
        SortWorkspaces();
        return (ws, null);
    }

    public void RemoveWorkspace(WorkspaceViewModel ws)
    {
        Vm.Workspaces.Remove(ws);
        UpdateEmptyHint();
    }

    public void ToggleHideWorkspace(WorkspaceViewModel ws)
    {
        ws.Hidden = !ws.Hidden;
        ApplyDeckVisibility();
        SortWorkspaces();
        QueueSave();
        SetStatus(ws.Hidden ? $"‏\"{ws.DisplayTitle}\" הוסתר (👁 מוסתרים כדי להציג)" : $"‏\"{ws.DisplayTitle}\" מוצג שוב");
    }

    public void EditWorkspace(WorkspaceViewModel ws)
    {
        var dialog = new EditCardDialog(ws) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            RefreshMetadata(ws);
            QueueSave();
        }
    }

    /// <summary>Branch + Peacock color straight from the folder (SPEC decisions 17-18).</summary>
    private static void RefreshMetadata(WorkspaceViewModel ws)
    {
        if (ws.Path.Length == 0) return;
        ws.Branch = WorkspaceMetadata.ReadBranch(ws.Path);
        ws.PeacockColor = WorkspaceMetadata.ReadPeacockColor(ws.Path);
    }

    private void RefreshAllMetadata()
    {
        foreach (var ws in Vm.Workspaces)
            RefreshMetadata(ws);
        RefreshTranscriptTitles();
    }

    /// <summary>Background scan of session transcripts for auto titles (stage D).
    /// Only files whose mtime changed since the last scan are re-read.</summary>
    private void RefreshTranscriptTitles()
    {
        if (_titleScanRunning) return;
        var stale = new List<(SessionViewModel Session, string Path, DateTime Mtime)>();
        foreach (var s in Vm.AllSessions())
        {
            if (s.CustomTitle != null || s.TranscriptPath is not { Length: > 0 } path) continue;
            try
            {
                DateTime mtime = File.GetLastWriteTimeUtc(path);
                if (mtime != s.TranscriptScannedAt) stale.Add((s, path, mtime));
            }
            catch { }
        }
        if (stale.Count == 0) return;

        _titleScanRunning = true;
        Task.Run(() =>
        {
            var results = stale.Select(x => (x.Session, Title: TranscriptReader.ReadTitle(x.Path), x.Mtime)).ToList();
            Dispatcher.BeginInvoke(() =>
            {
                _titleScanRunning = false;
                bool changed = false;
                foreach (var (session, title, mtime) in results)
                {
                    session.TranscriptScannedAt = mtime;
                    if (title != null && session.AutoTitle != title)
                    {
                        session.AutoTitle = title;
                        changed = true;
                    }
                }
                if (changed) QueueSave();
            });
        });
    }

    /// <summary>Actives (bound window / live session) float to the top (SPEC decision 16).
    /// Stable in-place sort via Move so DWM thumbnails survive.</summary>
    public void SortWorkspaces()
    {
        var desired = Vm.Workspaces
            .OrderByDescending(w => w.IsActive)
            .ThenBy(w => w.DisplayTitle, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        for (int target = 0; target < desired.Count; target++)
        {
            int current = Vm.Workspaces.IndexOf(desired[target]);
            if (current != target)
                Vm.Workspaces.Move(current, target);
        }
    }

    private void ApplyDeckVisibility()
    {
        foreach (var ws in Vm.Workspaces)
            ws.VisibleInDeck = !ws.Hidden || Vm.ShowHidden;
        UpdateEmptyHint();
    }

    // ---- window binding (engine reuse; VSCode-only per SPEC decision 13) ----

    private void RebindAll()
    {
        var candidates = WindowEnumerator.GetCandidates()
            .Where(c => WorkspaceMetadata.IsVsCodeProcess(c.ProcessName)).ToList();
        var used = new HashSet<IntPtr>(Vm.Workspaces.Where(w => w.Hwnd != IntPtr.Zero).Select(w => w.Hwnd));

        // ToList: Bind() re-sorts the collection, which must not happen mid-enumeration.
        foreach (var ws in Vm.Workspaces.Where(w => w.State == BindState.Disconnected).ToList())
        {
            var match = candidates.FirstOrDefault(c =>
                !used.Contains(c.Hwnd) && SafeIsMatch(c.Title, ws.TitlePattern));
            if (match == null) continue;
            used.Add(match.Hwnd);
            Bind(ws, match.Hwnd, match.Title, match.ProcessName);
        }
    }

    private void TryBindWorkspace(WorkspaceViewModel ws)
    {
        if (ws.State == BindState.Connected) return;
        var bound = new HashSet<IntPtr>(Vm.Workspaces.Where(w => w.Hwnd != IntPtr.Zero).Select(w => w.Hwnd));
        var match = WindowEnumerator.GetCandidates().FirstOrDefault(c =>
            !bound.Contains(c.Hwnd) &&
            WorkspaceMetadata.IsVsCodeProcess(c.ProcessName) &&
            SafeIsMatch(c.Title, ws.TitlePattern));
        if (match != null)
            Bind(ws, match.Hwnd, match.Title, match.ProcessName);
    }

    private void Bind(WorkspaceViewModel ws, IntPtr hwnd, string title, string process)
    {
        ws.Hwnd = hwnd;
        ws.WindowTitle = title;
        ws.ProcessName = process;
        ws.State = BindState.Connected;
        SortWorkspaces();
        QueueSave();
    }

    private void OnWindowTitleChanged(IntPtr hwnd, string newTitle)
    {
        var ws = Vm.FindByHwnd(hwnd);
        if (ws == null)
        {
            // A title change can make an unbound VSCode window match a workspace.
            if (newTitle.Length > 0) TryRebindWindow(hwnd);
            return;
        }
        if (newTitle.Length > 0) ws.WindowTitle = newTitle;
    }

    private void OnWindowDestroyed(IntPtr hwnd)
    {
        var ws = Vm.FindByHwnd(hwnd);
        if (ws == null) return;
        ws.Hwnd = IntPtr.Zero;
        ws.State = BindState.Disconnected;
        SortWorkspaces();
        QueueSave();
    }

    /// <summary>Automatic re-bind: a new/renamed VSCode window that matches an unbound
    /// workspace's title pattern connects to it.</summary>
    private void TryRebindWindow(IntPtr hwnd)
    {
        if (Vm.FindByHwnd(hwnd) != null) return;
        if (!Vm.Workspaces.Any(w => w.State == BindState.Disconnected)) return;
        if (NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT) != hwnd) return;
        if (!WindowEnumerator.IsEligible(hwnd, Environment.ProcessId)) return;

        string process = WindowEnumerator.GetProcessName(hwnd);
        if (!WorkspaceMetadata.IsVsCodeProcess(process)) return;
        string title = NativeMethods.GetWindowTextSafe(hwnd);

        var ws = Vm.Workspaces.FirstOrDefault(w =>
            w.State == BindState.Disconnected && SafeIsMatch(title, w.TitlePattern));
        if (ws == null) return;
        Bind(ws, hwnd, title, process);
        SetStatus($"‏\"{ws.DisplayTitle}\" התחבר לחלון: {title}");
    }

    /// <summary>Drag-in (SPEC decision 21.3, secondary channel): only VSCode windows,
    /// blocked when the workspace is already on the deck.</summary>
    private void HandleDragIn(IntPtr hwnd)
    {
        if (!IsVisible || !NativeMethods.GetCursorPos(out POINT pt)) return;
        if (PresentationSource.FromVisual(this) is not HwndSource source) return;
        if (!NativeMethods.GetWindowRect(source.Handle, out RECT self)) return;
        if (pt.X < self.Left || pt.X >= self.Right || pt.Y < self.Top || pt.Y >= self.Bottom) return;

        IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        if (Vm.FindByHwnd(root) != null)
        {
            SetStatus("החלון הזה כבר קשור ל-workspace בלוח");
            return;
        }
        if (!WindowEnumerator.IsEligible(root, Environment.ProcessId)) return;

        string process = WindowEnumerator.GetProcessName(root);
        if (!WorkspaceMetadata.IsVsCodeProcess(process))
        {
            SetStatus("רק חלונות VSCode נתמכים בלוח (החלטה 13)");
            return;
        }

        TryRebindWindow(root);
        if (Vm.FindByHwnd(root) != null) return;   // connected to an existing workspace

        string title = NativeMethods.GetWindowTextSafe(root);
        string name = WorkspaceNameFromTitle(title);
        if (name.Length == 0)
        {
            SetStatus("לא זוהה שם workspace בכותרת החלון");
            return;
        }
        var ws = new WorkspaceViewModel { Id = Vm.NextWorkspaceId++, Name = name };
        Vm.Workspaces.Add(ws);
        Bind(ws, root, title, process);
        ApplyDeckVisibility();
        SetStatus($"נוסף workspace ‏\"{name}\" (גרירה; הנתיב יתמלא מה-hook הראשון)");
    }

    /// <summary>"file - {workspace} - Visual Studio Code" → workspace segment (SPEC §6.6).</summary>
    private static string WorkspaceNameFromTitle(string title)
    {
        var parts = title.Split(" - ");
        int vsIdx = Array.FindIndex(parts, p => p.StartsWith("Visual Studio Code"));
        if (vsIdx > 0) return parts[vsIdx - 1].Trim();
        return parts.Length >= 2 ? parts[^2].Trim() : "";
    }

    private static bool SafeIsMatch(string input, string pattern)
    {
        try { return Regex.IsMatch(input, pattern); }
        catch { return false; }
    }

    // ---- sessions engine (SPEC §4ב — driven by the hooks only) ----

    /// <summary>Extra hook-payload data attached to any session command (all optional).</summary>
    public sealed record HookInfo(string? Detail = null, string? Transcript = null, string? Source = null,
                                  string? Mode = null, string? Reason = null)
    {
        public static readonly HookInfo Empty = new();
    }

    public (string, bool) StartSession(string sessionId, string workspaceArg, string? title, HookInfo info)
    {
        if (Vm.FindSession(sessionId) is { } found)
        {
            var (fw, fs) = found;
            fs.Closed = false;
            fs.Status = SessionStatus.Idle;
            fs.StartedAt = DateTime.Now;
            fs.EndedAt = null;
            if (!string.IsNullOrEmpty(title)) fs.CustomTitle = title;
            ApplyHookInfo(fs, info);
            fw.RefreshSessionVisibility();
            AfterSessionChange(fw);
            return ($"session {sessionId} restarted in \"{fw.DisplayTitle}\"", true);
        }

        var ws = ResolveOrCreateWorkspace(workspaceArg, out string? err);
        if (ws == null) return (err!, false);

        var session = new SessionViewModel
        {
            SessionId = sessionId,
            CustomTitle = string.IsNullOrEmpty(title) ? null : title,
            Status = SessionStatus.Idle,
            StartedAt = DateTime.Now,
        };
        ApplyHookInfo(session, info);
        ws.Sessions.Insert(0, session);
        ws.RefreshSessionVisibility();
        AfterSessionChange(ws);
        return ($"session {sessionId} started in \"{ws.DisplayTitle}\" [idle]", true);
    }

    private static void ApplyHookInfo(SessionViewModel session, HookInfo info)
    {
        session.LastEventAt = DateTime.Now;
        if (info.Detail != null) session.Detail = Sanitize(info.Detail);
        if (info.Transcript != null) session.TranscriptPath = info.Transcript;
        if (info.Source != null) session.Source = info.Source;
        if (info.Mode != null) session.PermissionMode = info.Mode;
        if (info.Reason != null) session.EndReason = info.Reason;
    }

    /// <summary>Hook details (prompts, messages) become one bounded display line.</summary>
    private static string Sanitize(string s)
    {
        string oneLine = Regex.Replace(s, @"\s+", " ").Trim();
        return oneLine.Length <= 300 ? oneLine : oneLine[..299] + "…";
    }

    /// <summary>Workspace resolution for hooks (SPEC decision 21.4 — cwd is the safety net):
    /// by path → by name (adopting the path into a pathless workspace) → auto-create.</summary>
    private WorkspaceViewModel? ResolveOrCreateWorkspace(string workspaceArg, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(workspaceArg))
        {
            error = "session start requires --workspace <path or name>";
            return null;
        }

        bool isPath = workspaceArg.Contains('\\') || workspaceArg.Contains('/');
        if (isPath)
        {
            if (Vm.FindByPath(workspaceArg) is { } byPath) return byPath;
            string leaf = WorkspaceMetadata.NameFromPath(workspaceArg);
            var byName = Vm.Workspaces.FirstOrDefault(w =>
                w.Path.Length == 0 && string.Equals(w.Name, leaf, StringComparison.OrdinalIgnoreCase));
            if (byName != null)
            {
                // A drag-in workspace learns its path from the first hook that reports cwd.
                byName.Path = workspaceArg;
                RefreshMetadata(byName);
                QueueSave();
                return byName;
            }
            var (created, err) = AddWorkspaceFromPath(workspaceArg);
            if (created == null) error = err;
            else SetStatus($"נוצר workspace ‏\"{created.DisplayTitle}\" מ-hook (cwd)");
            return created;
        }

        var named = Vm.Workspaces.FirstOrDefault(w =>
            string.Equals(w.Name, workspaceArg, StringComparison.OrdinalIgnoreCase));
        if (named == null) error = $"no workspace named \"{workspaceArg}\" (pass the folder path to auto-create)";
        return named;
    }

    public (string, bool) SetSessionStatus(string sessionId, SessionStatus status, string workspaceArg, HookInfo info)
    {
        if (Vm.FindSession(sessionId) is not { } found)
        {
            // Self-healing (feedback 2026-07-19): the session may have been deleted with its
            // workspace. Every hook event carries cwd — recreate instead of dropping updates.
            if (workspaceArg.Length == 0)
                return ($"unknown session id {sessionId} (was 'session start' called?)", false);
            var host = ResolveOrCreateWorkspace(workspaceArg, out string? err);
            if (host == null) return (err!, false);
            var recreated = new SessionViewModel
            {
                SessionId = sessionId,
                Status = status,
                StartedAt = DateTime.Now,
            };
            ApplyHookInfo(recreated, info);
            host.Sessions.Insert(0, recreated);
            AfterSessionChange(host);
            return ($"session {sessionId} recreated in \"{host.DisplayTitle}\" [{SessionStatusNames.ToName(status)}]", true);
        }
        var (ws, session) = found;
        if (session.Closed)
            return ($"session {sessionId} is closed — status not changed", false);
        session.Status = status;
        ApplyHookInfo(session, info);
        AfterSessionChange(ws);
        return ($"session {sessionId} → {SessionStatusNames.ToName(status)}", true);
    }

    public (string, bool) EndSession(string sessionId, HookInfo info)
    {
        if (Vm.FindSession(sessionId) is not { } found)
            return ($"unknown session id {sessionId}", false);
        var (ws, session) = found;
        session.Closed = true;
        session.EndedAt = DateTime.Now;
        ApplyHookInfo(session, info);

        // Retention (SPEC decision 12): keep only the last N closed sessions per workspace.
        var closed = ws.Sessions.Where(s => s.Closed).OrderByDescending(s => s.EndedAt ?? DateTime.MinValue).ToList();
        foreach (var extra in closed.Skip(Math.Max(0, Vm.ClosedSessionRetention)))
            ws.Sessions.Remove(extra);

        ws.RefreshSessionVisibility();
        AfterSessionChange(ws);
        return ($"session {sessionId} ended", true);
    }

    private void AfterSessionChange(WorkspaceViewModel ws)
    {
        ws.RefreshSessionVisibility();
        SortWorkspaces();
        _blink.Refresh();
        QueueSave();
    }

    /// <summary>Click on a session card = acknowledge + focus the window + open/resume the
    /// session's tab in VSCode via the connector (stage D).</summary>
    public void HandleSessionClick(WorkspaceViewModel ws, SessionViewModel session)
    {
        if (!session.Acknowledged)
        {
            session.Acknowledged = true;
            _blink.Refresh();
            QueueSave();
        }
        FocusWorkspace(ws);
        var (sent, _) = OpenSessionInVscode(ws, session);
        if (sent) SetStatus($"פותח את הסשן ב-VSCode: {session.DisplayTitle}");
    }

    // ---- VSCode extension connector (stage D) ----

    private void OnVscodeSync(VscodeSyncMessage sync, VscodeConnection conn)
    {
        if (!_connectors.Contains(conn)) _connectors.Add(conn);
        conn.Pid = sync.Pid;
        conn.WorkspacePath = sync.Workspace ?? "";
        if (conn.WorkspacePath.Length == 0) return;

        if (Vm.FindByPath(conn.WorkspacePath) is { } ws)
        {
            // The extension is the fresher branch source (event-driven vs our 10s poll).
            if (!string.IsNullOrEmpty(sync.Branch)) ws.Branch = sync.Branch;
            var labels = sync.Tabs.Select(t => t.Label).ToList();
            ws.SetClaudeTabs(labels);
            foreach (var s in ws.Sessions)
                s.OpenAsTab = labels.Contains(s.DisplayTitle);
        }

        // A click that had to launch VSCode first parked its open request here.
        string norm = WorkspaceMetadata.NormalizePath(conn.WorkspacePath);
        if (_pendingOpens.TryGetValue(norm, out var pending))
        {
            _pendingOpens.Remove(norm);
            if (DateTime.Now - pending.At < PendingOpenTtl)
                conn.TrySend(new { Cmd = "openSession", SessionId = pending.SessionId, Maximize = Vm.OpenSessionMaximized });
        }
    }

    private void OnVscodeClosed(VscodeConnection conn)
    {
        _connectors.Remove(conn);
        if (conn.WorkspacePath.Length > 0 &&
            Vm.FindByPath(conn.WorkspacePath) is { } ws && FindConnector(ws) == null)
        {
            ws.SetClaudeTabs(new List<string>());
            foreach (var s in ws.Sessions) s.OpenAsTab = false;
        }
    }

    private VscodeConnection? FindConnector(WorkspaceViewModel ws)
    {
        if (ws.Path.Length == 0) return null;
        string norm = WorkspaceMetadata.NormalizePath(ws.Path);
        return _connectors.LastOrDefault(c => c.WorkspacePath.Length > 0 &&
            WorkspaceMetadata.NormalizePath(c.WorkspacePath) == norm);
    }

    /// <summary>Open/resume the session's tab in VSCode. Without a live connector the request
    /// is parked; it's flushed when the extension connects (VSCode may still be launching).</summary>
    public (bool, string) OpenSessionInVscode(WorkspaceViewModel ws, SessionViewModel session)
    {
        var conn = FindConnector(ws);
        if (conn == null)
        {
            if (ws.Path.Length > 0)
                _pendingOpens[WorkspaceMetadata.NormalizePath(ws.Path)] = (session.SessionId, DateTime.Now);
            return (false, "no VSCode connector for this workspace yet — request queued");
        }
        if (!conn.TrySend(new { Cmd = "openSession", SessionId = session.SessionId, Maximize = Vm.OpenSessionMaximized }))
        {
            _connectors.Remove(conn);
            return (false, "connector connection lost");
        }
        return (true, "");
    }

    // ---- focus / pin / stage (SPEC §F3) ----

    public (bool, string) FocusWorkspace(WorkspaceViewModel ws)
    {
        if (ws.State != BindState.Connected || !NativeMethods.IsWindow(ws.Hwnd))
        {
            // No bound window — open VSCode on the folder; auto-bind picks it up (feedback 2026-07-19).
            if (ws.Path.Length > 0 && Directory.Exists(ws.Path))
            {
                if (WindowActions.LaunchVsCode(ws.Path))
                {
                    SetStatus($"פותח VSCode עבור \"{ws.DisplayTitle}\"...");
                    return (true, $"launching VSCode for workspace {ws.Id}");
                }
                SetStatus($"‏\"{ws.DisplayTitle}\" — פתיחת VSCode נכשלה");
                return (false, $"failed to launch VSCode for workspace {ws.Id}");
            }
            SetStatus($"‏\"{ws.DisplayTitle}\" — אין חלון פתוח ואין נתיב תיקייה");
            return (false, $"workspace {ws.Id} has no bound window and no path");
        }
        WindowActions.Focus(ws.Hwnd);
        return (true, "");
    }

    public (bool, string) PinWorkspace(WorkspaceViewModel ws)
    {
        if (ws.State != BindState.Connected || !NativeMethods.IsWindow(ws.Hwnd))
        {
            // No bound window — same launch fallback as Focus; the user can pin once it binds.
            return FocusWorkspace(ws);
        }
        WindowActions.MoveTo(ws.Hwnd, GetStageRect());
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

    /// <summary>Called by the CLI after changes that may start/stop blinking.</summary>
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
        ShowHiddenToggle.IsChecked = Vm.ShowHidden;
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
            // Custom rect can only be defined via CLI (sessiondeck stage --rect x,y,w,h).
            SetStatus("מלבן מותאם מוגדר רק דרך CLI: sessiondeck stage --rect x,y,w,h");
            SyncCombosFromVm();
            return;
        }
        Vm.StageMonitor = StageMonitorCombo.SelectedIndex;
        Vm.StageMode = mode;
        QueueSave();
    }

    private void AddWorkspace_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "בחר תיקיית פרויקט (workspace)",
        };
        if (dialog.ShowDialog(this) != true) return;
        var (ws, err) = AddWorkspaceFromPath(dialog.FolderName);
        SetStatus(ws != null ? $"נוסף workspace ‏\"{ws.DisplayTitle}\"" : err!);
    }

    private void ShowHidden_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingUi || _initializing) return;
        Vm.ShowHidden = ShowHiddenToggle.IsChecked == true;
        ApplyDeckVisibility();
        QueueSave();
    }

    // ---- settings (SPEC §F9) ----

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

    private void SetStatus(string message) => StatusText.Text = message;

    private void UpdateEmptyHint()
        => EmptyHint.Visibility = Vm.Workspaces.Any(w => w.VisibleInDeck) ? Visibility.Collapsed : Visibility.Visible;

    // ---- CLI ----

    public void ActivateFromCli()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
    }
}
