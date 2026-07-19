using System.Text;
using System.Text.RegularExpressions;
using SessionDeck.Models;
using SessionDeck.Services;
using SessionDeck.ViewModels;

namespace SessionDeck.Cli;

/// <summary>
/// Executes CLI argv against the live app state. Always invoked on the UI thread
/// (the pipe handler dispatches here). Session commands are the hooks' entry point
/// (SPEC §4ב) — they must stay fast and atomic.
/// </summary>
public sealed class CommandExecutor
{
    private static readonly HashSet<string> ValueOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "match", "desc", "color", "monitor", "half", "rect", "title",
        "id", "workspace", "state", "path",
        "detail", "transcript", "source", "mode", "reason",
    };

    private readonly MainWindow _window;
    private MainViewModel Vm => _window.Vm;

    public CommandExecutor(MainWindow window)
    {
        _window = window;
    }

    public PipeResponse Execute(string[] argv)
    {
        try
        {
            var args = Parse(argv);
            return args.Command switch
            {
                "list" => List(args),
                "add" => Add(args),
                "remove" => Remove(args),
                "set" => Set(args),
                "focus" => Focus(args),
                "pin" => Pin(args),
                "zone" => Zone(args),
                "stage" => Stage(args),
                "session" => Session(args),
                "status" => Status(),
                "activate" => Activate(),
                "snapshot" => Snapshot(args),   // internal: render the WPF tree to PNG (debug aid)
                _ => Err($"unknown command '{args.Command}'. Available: list, add, remove, set, focus, pin, zone, stage, session, status"),
            };
        }
        catch (Exception ex)
        {
            return Err("error: " + ex.Message);
        }
    }

    // ---- workspace commands ----

    private PipeResponse List(ParsedArgs a)
    {
        if (Vm.Workspaces.Count == 0) return Ok("(no workspaces)");
        var sb = new StringBuilder();
        foreach (var w in Vm.Workspaces)
        {
            string bind = w.State == BindState.Connected ? "connected" : "no window";
            string flags = w.Hidden ? " [hidden]" : "";
            string branch = w.HasBranch ? $" ({w.Branch})" : "";
            sb.AppendLine($"[{w.Id}] {w.DisplayTitle}{branch} — {bind}{flags}  {w.Path}");
            foreach (var s in w.Sessions.Where(s => !s.Closed || a.Flags.Contains("all")))
            {
                string ack = s.Acknowledged ? " ack" : "";
                sb.AppendLine($"     {s.SessionId}  {s.StatusText}{ack}  {s.DisplayTitle}" +
                              (s.Description.Length > 0 ? $"  — {s.Description}" : ""));
            }
        }
        return Ok(sb.ToString().TrimEnd());
    }

    private PipeResponse Add(ParsedArgs a)
    {
        string? path = a.Options.GetValueOrDefault("path") ??
                       (a.Positionals.Count > 0 ? a.Positionals[0] : null);
        if (path == null) return Err("add requires a folder path: sessiondeck add <path>");
        var (ws, err) = _window.AddWorkspaceFromPath(path);
        if (ws == null) return Err(err!);
        string bind = ws.State == BindState.Connected ? "connected" : "no open window yet";
        return Ok($"added workspace {ws.Id}: \"{ws.DisplayTitle}\" [{bind}]");
    }

    private PipeResponse Remove(ParsedArgs a)
    {
        var (ws, err) = ResolveTarget(a);
        if (ws == null) return Err(err!);
        _window.RemoveWorkspace(ws);
        return Ok($"removed workspace {ws.Id} (\"{ws.DisplayTitle}\")");
    }

    private PipeResponse Set(ParsedArgs a)
    {
        var (ws, err) = ResolveTarget(a);
        if (ws == null) return Err(err!);
        if (!a.Options.ContainsKey("title") && !a.Options.ContainsKey("desc") && !a.Options.ContainsKey("color"))
            return Err("set requires --title/--desc/--color (empty value reverts to auto)");

        var changes = new List<string>();
        if (a.Options.TryGetValue("title", out var title))
        {
            ws.CustomTitle = title.Length == 0 ? null : title;
            changes.Add(title.Length == 0 ? "title=auto" : $"title=\"{title}\"");
        }
        if (a.Options.TryGetValue("desc", out var desc))
        {
            ws.Description = desc;
            changes.Add($"desc=\"{desc}\"");
        }
        if (a.Options.TryGetValue("color", out var color))
        {
            if (color.Length == 0)
            {
                ws.CustomColor = null;
                changes.Add("color=auto");
            }
            else
            {
                if (!ColorUtil.TryParse(color, out _))
                    return Err($"unknown color '{color}'. Use {ColorUtil.KnownNames} or #RRGGBB");
                ws.CustomColor = color;
                changes.Add($"color={color}");
            }
        }
        _window.QueueSave();
        return Ok($"workspace {ws.Id}: {string.Join(", ", changes)}");
    }

    private PipeResponse Focus(ParsedArgs a)
    {
        var (ws, err) = ResolveTarget(a);
        if (ws == null) return Err(err!);
        var (ok, msg) = _window.FocusWorkspace(ws);
        return ok ? Ok($"focused workspace {ws.Id}") : Err(msg);
    }

    private PipeResponse Pin(ParsedArgs a)
    {
        var (ws, err) = ResolveTarget(a);
        if (ws == null) return Err(err!);
        var (ok, msg) = _window.PinWorkspace(ws);
        return ok ? Ok($"pinned workspace {ws.Id} to stage") : Err(msg);
    }

    // ---- session commands (SPEC §4ב — called by the Claude Code hooks) ----

    private PipeResponse Session(ParsedArgs a)
    {
        string sub = a.Positionals.Count > 0 ? a.Positionals[0].ToLowerInvariant() : "";
        switch (sub)
        {
            case "start":
            {
                if (!a.Options.TryGetValue("id", out var id)) return Err("session start requires --id <session_id>");
                string workspace = a.Options.GetValueOrDefault("workspace", "");
                var (msg, ok) = _window.StartSession(id, workspace, a.Options.GetValueOrDefault("title"), HookInfoFrom(a));
                return ok ? Ok(msg) : Err(msg);
            }
            case "status":
            {
                if (!a.Options.TryGetValue("id", out var id)) return Err("session status requires --id <session_id>");
                if (!a.Options.TryGetValue("state", out var stateStr) ||
                    !SessionStatusNames.TryParse(stateStr, out var status))
                    return Err("session status requires --state working|waiting|done|error|idle");
                var (msg, ok) = _window.SetSessionStatus(id, status,
                    a.Options.GetValueOrDefault("workspace", ""), HookInfoFrom(a));
                return ok ? Ok(msg) : Err(msg);
            }
            case "end":
            {
                if (!a.Options.TryGetValue("id", out var id)) return Err("session end requires --id <session_id>");
                var (msg, ok) = _window.EndSession(id, HookInfoFrom(a));
                return ok ? Ok(msg) : Err(msg);
            }
            case "open":
            {
                if (!a.Options.TryGetValue("id", out var id)) return Err("session open requires --id <session_id>");
                if (Vm.FindSession(id) is not { } found) return Err($"unknown session id {id}");
                var (ws, session) = found;
                _window.FocusWorkspace(ws);
                var (sent, msg) = _window.OpenSessionInVscode(ws, session);
                return sent ? Ok($"opening session {id} in VSCode") : Err(msg);
            }
            case "list":
            {
                var wanted = a.Options.GetValueOrDefault("workspace");
                bool all = a.Flags.Contains("all");
                var sb = new StringBuilder();
                foreach (var w in Vm.Workspaces)
                {
                    if (wanted != null && !string.Equals(w.Name, wanted, StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (var s in w.Sessions.Where(s => all || !s.Closed))
                        sb.AppendLine($"{s.SessionId}  {s.StatusText,-8} {w.DisplayTitle}  {s.DisplayTitle}");
                }
                return Ok(sb.Length > 0 ? sb.ToString().TrimEnd() : "(no sessions)");
            }
            default:
                return Err("session requires: start | status | end | open | list");
        }
    }

    // ---- zone / stage / status ----

    private PipeResponse Stage(ParsedArgs a)
    {
        int monitor = Vm.StageMonitor;
        if (a.Options.TryGetValue("monitor", out var monStr))
        {
            if (!int.TryParse(monStr, out int mon1) || mon1 < 1 || mon1 > _window.MonitorCount)
                return Err($"--monitor must be 1..{_window.MonitorCount}");
            monitor = mon1 - 1;
        }

        if (a.Options.TryGetValue("rect", out var rectStr))
        {
            var rect = MainWindow.ParseRect(rectStr);
            if (rect == null) return Err("--rect must be x,y,w,h (virtual-screen px, w/h > 0)");
            _window.SetStage(monitor, StageMode.Rect, rect);
            return Ok($"stage: rect {rectStr}");
        }

        StageMode mode;
        if (a.Flags.Contains("full")) mode = StageMode.Full;
        else if (a.Options.TryGetValue("half", out var half))
        {
            if (half == "left") mode = StageMode.HalfLeft;
            else if (half == "right") mode = StageMode.HalfRight;
            else return Err("--half must be left or right");
        }
        else return Err("stage requires --half left|right, --full, or --rect x,y,w,h");

        _window.SetStage(monitor, mode, null);
        return Ok($"stage: {ModeNames.ToName(mode)} on monitor {monitor + 1}");
    }

    private PipeResponse Zone(ParsedArgs a)
    {
        ZoneMode mode;
        if (a.Flags.Contains("off")) mode = ZoneMode.Off;
        else if (a.Flags.Contains("full")) mode = ZoneMode.Full;
        else if (a.Options.TryGetValue("half", out var half))
        {
            if (half == "left") mode = ZoneMode.HalfLeft;
            else if (half == "right") mode = ZoneMode.HalfRight;
            else return Err("--half must be left or right");
        }
        else return Err("zone requires --half left|right, --full, or --off");

        int monitor = Vm.ZoneMonitor;
        if (a.Options.TryGetValue("monitor", out var monStr))
        {
            if (!int.TryParse(monStr, out int mon1) || mon1 < 1 || mon1 > _window.MonitorCount)
                return Err($"--monitor must be 1..{_window.MonitorCount}");
            monitor = mon1 - 1;
        }

        _window.ApplyZone(monitor, mode);
        return Ok($"zone: {ModeNames.ToName(mode)} on monitor {monitor + 1}");
    }

    private PipeResponse Status()
    {
        int connected = Vm.Workspaces.Count(w => w.State == BindState.Connected);
        int openSessions = Vm.AllSessions().Count(s => !s.Closed);
        string version = typeof(CommandExecutor).Assembly.GetName().Version?.ToString(3) ?? "?";
        string stage = Vm.StageMode == StageMode.Rect && Vm.StageRect is { } r
            ? $"rect {r.Left},{r.Top},{r.Width},{r.Height}"
            : $"{ModeNames.ToName(Vm.StageMode)} (monitor {Vm.StageMonitor + 1})";
        return Ok($"""
            SessionDeck {version}
            zone:  {ModeNames.ToName(Vm.ZoneMode)} (monitor {Vm.ZoneMonitor + 1})
            stage: {stage}
            workspaces: {Vm.Workspaces.Count} ({connected} with window, {Vm.Workspaces.Count(w => w.Hidden)} hidden)
            sessions: {openSessions} open
            """);
    }

    private PipeResponse Activate()
    {
        _window.ActivateFromCli();
        return Ok("");
    }

    /// <summary>Internal debug command: renders the window's WPF visual tree to a PNG.
    /// DWM thumbnails are composited by the OS and never appear here — chrome only.</summary>
    private PipeResponse Snapshot(ParsedArgs a)
    {
        if (a.Positionals.Count == 0) return Err("snapshot requires a target .png path");
        string path = a.Positionals[0];
        var root = (System.Windows.Media.Visual?)_window.Content;
        if (root == null || _window.ActualWidth < 1) return Err("window has no content");
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(_window);
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)(_window.ActualWidth * dpi.DpiScaleX), (int)(_window.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(root);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        using var fs = System.IO.File.Create(path);
        encoder.Save(fs);
        return Ok($"saved {path}");
    }

    // ---- helpers ----

    private static MainWindow.HookInfo HookInfoFrom(ParsedArgs a) => new(
        Detail: a.Options.GetValueOrDefault("detail"),
        Transcript: a.Options.GetValueOrDefault("transcript"),
        Source: a.Options.GetValueOrDefault("source"),
        Mode: a.Options.GetValueOrDefault("mode"),
        Reason: a.Options.GetValueOrDefault("reason"));

    private (WorkspaceViewModel?, string?) ResolveTarget(ParsedArgs a)
    {
        if (a.Positionals.Count > 0)
        {
            if (!int.TryParse(a.Positionals[0], out int id))
                return (null, $"invalid workspace id '{a.Positionals[0]}'");
            var byId = Vm.FindById(id);
            return byId != null ? (byId, null) : (null, $"no workspace with id {id}");
        }
        if (a.Options.TryGetValue("match", out var pattern))
        {
            if (!TryRegex(pattern, out var rx, out var rxErr))
                return (null, rxErr);
            var ws = Vm.Workspaces.FirstOrDefault(w => rx!.IsMatch(w.DisplayTitle) || rx.IsMatch(w.Name));
            return ws != null ? (ws, null) : (null, $"no workspace matches /{pattern}/");
        }
        return (null, "target required: <workspace id> or --match \"<regex>\"");
    }

    private static bool TryRegex(string pattern, out Regex? rx, out string? error)
    {
        try
        {
            rx = new Regex(pattern);
            error = null;
            return true;
        }
        catch (ArgumentException ex)
        {
            rx = null;
            error = $"invalid regex: {ex.Message}";
            return false;
        }
    }

    private static PipeResponse Ok(string output) => new(0, output);
    private static PipeResponse Err(string output) => new(1, output);

    private sealed class ParsedArgs
    {
        public string Command = "";
        public List<string> Positionals { get; } = new();
        public Dictionary<string, string> Options { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Flags { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static ParsedArgs Parse(string[] argv)
    {
        var result = new ParsedArgs { Command = argv[0].ToLowerInvariant() };
        for (int i = 1; i < argv.Length; i++)
        {
            string token = argv[i];
            if (token.StartsWith("--"))
            {
                string name = token[2..];
                if (ValueOptions.Contains(name) && i + 1 < argv.Length)
                    result.Options[name] = argv[++i];
                else
                    result.Flags.Add(name);
            }
            else
            {
                result.Positionals.Add(token);
            }
        }
        return result;
    }
}
