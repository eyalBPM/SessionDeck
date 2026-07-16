using System.Text;
using System.Text.RegularExpressions;
using WinGrid.Models;
using WinGrid.Services;
using WinGrid.ViewModels;

namespace WinGrid.Cli;

/// <summary>
/// Executes CLI argv against the live app state. Always invoked on the UI thread
/// (the pipe handler dispatches here).
/// </summary>
public sealed class CommandExecutor
{
    private static readonly HashSet<string> ValueOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "match", "process", "desc", "color", "alt", "interval", "monitor", "half", "rect", "title",
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
                "list" => List(),
                "add" => Add(args),
                "remove" => Remove(args),
                "set" => Set(args),
                "border" => Border(args),
                "focus" => Focus(args),
                "pin" => Pin(args),
                "zone" => Zone(args),
                "stage" => Stage(args),
                "status" => Status(),
                "activate" => Activate(),
                "snapshot" => Snapshot(args),   // internal: render the WPF tree to PNG (debug aid)
                _ => Err($"unknown command '{args.Command}'. Available: list, add, remove, set, border, focus, pin, zone, stage, status"),
            };
        }
        catch (Exception ex)
        {
            return Err("error: " + ex.Message);
        }
    }

    // ---- commands ----

    private PipeResponse List()
    {
        if (Vm.Tiles.Count == 0) return Ok("(no tiles)");
        var sb = new StringBuilder();
        sb.AppendLine($"{"ID",-4} {"STATE",-13} {"PROCESS",-16} {"COLOR",-12} {"TITLE",-40} DESC");
        foreach (var t in Vm.Tiles)
        {
            string state = t.State == TileState.Connected ? "connected" : "disconnected";
            string color = t.AltColorName == null ? t.ColorName : $"{t.ColorName}/{t.AltColorName}";
            sb.AppendLine($"{t.Id,-4} {state,-13} {Trunc(t.ProcessName, 16),-16} {Trunc(color, 12),-12} {Trunc(t.Title, 40),-40} {t.Description}");
        }
        return Ok(sb.ToString().TrimEnd());
    }

    private PipeResponse Add(ParsedArgs a)
    {
        var (tile, err) = AddCore(a);
        if (tile == null) return Err(err!);
        string state = tile.State == TileState.Connected ? "connected" : "disconnected (no matching window yet)";
        return Ok($"added tile {tile.Id}: \"{tile.Title}\" [{state}]");
    }

    private (TileViewModel?, string?) AddCore(ParsedArgs a)
    {
        if (!a.Options.TryGetValue("match", out var pattern))
            return (null, "add requires --match \"<title regex>\" (interactive --pick is UI-only for now)");
        if (!TryRegex(pattern, out _, out var rxErr))
            return (null, rxErr);

        string color = a.Options.GetValueOrDefault("color", "gray");
        if (!ColorUtil.TryParse(color, out _))
            return (null, $"unknown color '{color}'. Use {ColorUtil.KnownNames} or #RRGGBB");
        string? process = a.Options.GetValueOrDefault("process");
        string desc = a.Options.GetValueOrDefault("desc", "");

        var candidate = WindowEnumerator.GetCandidates().FirstOrDefault(c =>
            Regex.IsMatch(c.Title, pattern) &&
            (process == null || string.Equals(c.ProcessName, process, StringComparison.OrdinalIgnoreCase)) &&
            Vm.FindByHwnd(c.Hwnd) == null);

        return (_window.AddTile(pattern, process ?? candidate?.ProcessName ?? "", desc, color, candidate), null);
    }

    private PipeResponse Remove(ParsedArgs a)
    {
        var (tile, err) = ResolveTarget(a);
        if (tile == null) return Err(err!);
        _window.RemoveTile(tile);
        return Ok($"removed tile {tile.Id} (\"{tile.Title}\")");
    }

    private PipeResponse Border(ParsedArgs a)
    {
        var (tile, err) = ResolveTarget(a);
        if (tile == null && a.Flags.Contains("auto-add") && a.Options.ContainsKey("match"))
        {
            // --auto-add (SPEC §4): the target window is not tiled yet — add it, then color it.
            (tile, err) = AddCore(a);
        }
        if (tile == null) return Err(err!);

        if (!a.Options.TryGetValue("color", out var color))
            return Err("border requires --color <c>");
        if (!ColorUtil.TryParse(color, out _))
            return Err($"unknown color '{color}'. Use {ColorUtil.KnownNames} or #RRGGBB");

        string? alt = a.Options.GetValueOrDefault("alt");
        int interval = tile.BlinkIntervalMs;
        if (alt != null)
        {
            if (!ColorUtil.TryParse(alt, out _))
                return Err($"unknown alt color '{alt}'. Use {ColorUtil.KnownNames} or #RRGGBB");
            if (a.Options.TryGetValue("interval", out var ivStr) &&
                (!int.TryParse(ivStr, out interval) || interval < 100 || interval > 10000))
                return Err("--interval must be 100..10000 (ms)");
        }
        else if (a.Options.ContainsKey("interval"))
        {
            return Err("--interval requires --alt <c2> (blinking mode)");
        }

        tile.ColorName = color;
        tile.AltColorName = alt;
        tile.BlinkIntervalMs = interval;
        _window.RefreshBlink();
        _window.QueueSave();
        return Ok(alt == null
            ? $"tile {tile.Id} border set to {color}"
            : $"tile {tile.Id} border blinking {color}/{alt} every {interval}ms");
    }

    private PipeResponse Set(ParsedArgs a)
    {
        var (tile, err) = ResolveTarget(a);
        if (tile == null) return Err(err!);
        if (!a.Options.ContainsKey("title") && !a.Options.ContainsKey("desc"))
            return Err("set requires --title \"...\" and/or --desc \"...\" (empty --title reverts to auto)");

        var changes = new List<string>();
        if (a.Options.TryGetValue("title", out var title))
        {
            if (title.Length == 0)
            {
                tile.ManualTitle = false;
                if (tile.Hwnd != IntPtr.Zero && Interop.NativeMethods.IsWindow(tile.Hwnd))
                {
                    string current = Interop.NativeMethods.GetWindowTextSafe(tile.Hwnd);
                    if (current.Length > 0) tile.Title = current;
                }
                changes.Add("title=auto");
            }
            else
            {
                tile.ManualTitle = true;
                tile.Title = title;
                changes.Add($"title=\"{title}\"");
            }
        }
        if (a.Options.TryGetValue("desc", out var desc))
        {
            tile.Description = desc;
            changes.Add($"desc=\"{desc}\"");
        }
        _window.QueueSave();
        return Ok($"tile {tile.Id}: {string.Join(", ", changes)}");
    }

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

    private PipeResponse Status()
    {
        int connected = Vm.Tiles.Count(t => t.State == TileState.Connected);
        string version = typeof(CommandExecutor).Assembly.GetName().Version?.ToString(3) ?? "?";
        string stage = Vm.StageMode == StageMode.Rect && Vm.StageRect is { } r
            ? $"rect {r.Left},{r.Top},{r.Width},{r.Height}"
            : $"{ModeNames.ToName(Vm.StageMode)} (monitor {Vm.StageMonitor + 1})";
        return Ok($"""
            WinGrid {version}
            zone:  {ModeNames.ToName(Vm.ZoneMode)} (monitor {Vm.ZoneMonitor + 1})
            stage: {stage}
            tiles: {Vm.Tiles.Count} ({connected} connected, {Vm.Tiles.Count - connected} disconnected)
            """);
    }

    private PipeResponse Focus(ParsedArgs a)
    {
        var (tile, err) = ResolveTarget(a);
        if (tile == null) return Err(err!);
        var (ok, msg) = _window.FocusTile(tile);
        return ok ? Ok($"focused tile {tile.Id}") : Err(msg);
    }

    private PipeResponse Pin(ParsedArgs a)
    {
        var (tile, err) = ResolveTarget(a);
        if (tile == null) return Err(err!);
        var (ok, msg) = _window.PinTile(tile);
        return ok ? Ok($"pinned tile {tile.Id} to stage") : Err(msg);
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

    private (TileViewModel?, string?) ResolveTarget(ParsedArgs a)
    {
        if (a.Positionals.Count > 0)
        {
            if (!int.TryParse(a.Positionals[0], out int id))
                return (null, $"invalid tile id '{a.Positionals[0]}'");
            var byId = Vm.FindById(id);
            return byId != null ? (byId, null) : (null, $"no tile with id {id}");
        }
        if (a.Options.TryGetValue("match", out var pattern))
        {
            if (!TryRegex(pattern, out var rx, out var rxErr))
                return (null, rxErr);
            var tile = Vm.Tiles.FirstOrDefault(t => rx!.IsMatch(t.Title));
            return tile != null ? (tile, null) : (null, $"no tile title matches /{pattern}/");
        }
        return (null, "target required: <tile id> or --match \"<regex>\"");
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

    private static string Trunc(string s, int max)
        => s.Length <= max ? s : s[..(max - 1)] + "…";

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
