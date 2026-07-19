namespace SessionDeck.Models;

public enum ZoneMode { Off, HalfLeft, HalfRight, Full }
public enum StageMode { Full, HalfLeft, HalfRight, Rect }

public static class ModeNames
{
    public static string ToName(ZoneMode m) => m switch
    {
        ZoneMode.Off => "off",
        ZoneMode.HalfLeft => "half-left",
        ZoneMode.HalfRight => "half-right",
        ZoneMode.Full => "full",
        _ => "off",
    };

    public static bool TryParseZone(string s, out ZoneMode m)
    {
        m = s switch
        {
            "off" => ZoneMode.Off,
            "half-left" => ZoneMode.HalfLeft,
            "half-right" => ZoneMode.HalfRight,
            "full" => ZoneMode.Full,
            _ => (ZoneMode)(-1),
        };
        return (int)m >= 0;
    }

    public static string ToName(StageMode m) => m switch
    {
        StageMode.Full => "full",
        StageMode.HalfLeft => "half-left",
        StageMode.HalfRight => "half-right",
        StageMode.Rect => "rect",
        _ => "full",
    };

    public static bool TryParseStage(string s, out StageMode m)
    {
        m = s switch
        {
            "full" => StageMode.Full,
            "half-left" => StageMode.HalfLeft,
            "half-right" => StageMode.HalfRight,
            "rect" => StageMode.Rect,
            _ => (StageMode)(-1),
        };
        return (int)m >= 0;
    }
}

/// <summary>Legacy stage A/B generic tile — carried through the config untouched so
/// pre-cards data is never lost, but no longer shown in the UI (SPEC decision 15).</summary>
public class TileConfig
{
    public int Id { get; set; }
    public string ProcessName { get; set; } = "";
    public string TitlePattern { get; set; } = "";
    public string Title { get; set; } = "";
    public bool ManualTitle { get; set; }
    public string Description { get; set; } = "";
    public string Color { get; set; } = "gray";
    public string? AltColor { get; set; }
    public int BlinkIntervalMs { get; set; } = 500;
}

/// <summary>A VSCode workspace on the deck (SPEC §2ב) — persistent entity; the OS window
/// is only its live binding.</summary>
public class WorkspaceConfig
{
    public int Id { get; set; }
    public string Path { get; set; } = "";           // folder path; may be empty for drag-in adds until a hook reports cwd
    public string Name { get; set; } = "";           // project name (folder leaf by default)
    public string? CustomTitle { get; set; }         // null = show Name
    public string Description { get; set; } = "";
    public string? CustomColor { get; set; }         // null = auto (Peacock / default)
    public bool Hidden { get; set; }
    public string? TranscriptDir { get; set; }       // learned from hooks (stage D)
    public List<SessionConfig> Sessions { get; set; } = new();
}

/// <summary>A Claude Code session reported by the hooks (SPEC §2ב/§4ב).</summary>
public class SessionConfig
{
    public string SessionId { get; set; } = "";
    public string? CustomTitle { get; set; }
    public string Description { get; set; } = "";
    public string Status { get; set; } = "idle";     // idle|working|waiting|done|error
    public bool Acknowledged { get; set; }
    public bool Closed { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    // Everything the Claude Code hook payload provides (v0.4 — decision: keep it all):
    public string Detail { get; set; } = "";         // last prompt / notification message
    public string? TranscriptPath { get; set; }
    public string? Source { get; set; }              // SessionStart source: startup|resume|clear|compact
    public string? PermissionMode { get; set; }
    public string? EndReason { get; set; }
    public DateTime? LastEventAt { get; set; }
    public string? AutoTitle { get; set; }           // derived from the transcript (stage D)
    public string? TabTitle { get; set; }            // VSCode tab label (last ai-title entry)
}

/// <summary>Session status → border style. Lives in config so the mapping can change
/// without touching hooks or code (SPEC decision 11).</summary>
public class StatusStyle
{
    public string Color { get; set; } = "gray";
    public string? AltColor { get; set; }            // non-null = blinking
    public int BlinkIntervalMs { get; set; } = 500;
    public bool UntilAcknowledge { get; set; }       // blink stops (solid Color) after user click
}

public class ZoneConfig
{
    public int Monitor { get; set; }          // 0-based
    public string Mode { get; set; } = "off";
}

public class StageConfig
{
    public int Monitor { get; set; }          // 0-based
    public string Mode { get; set; } = "half-right";
    public string? Rect { get; set; }         // "x,y,w,h" in virtual-screen device px (mode=rect)
}

public class WindowBounds
{
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
}

public class AppConfig
{
    public int SchemaVersion { get; set; } = 2;
    public int NextTileId { get; set; } = 1;
    public List<TileConfig> Tiles { get; set; } = new();      // legacy, round-tripped only
    public int NextWorkspaceId { get; set; } = 1;
    public List<WorkspaceConfig> Workspaces { get; set; } = new();
    public Dictionary<string, StatusStyle> StatusStyles { get; set; } = new();
    public int ClosedSessionRetention { get; set; } = 20;     // per workspace (SPEC decision 12)
    public bool OpenSessionMaximized { get; set; } = true;    // stage D: collapse VSCode panels on session open
    public bool ShowHidden { get; set; }
    public bool AlwaysOnTop { get; set; }                     // 📌 pin toggle (feature 2026-07-19)
    public ZoneConfig Zone { get; set; } = new();
    public StageConfig Stage { get; set; } = new();
    public WindowBounds? Window { get; set; }
    public bool AutoRemoveDisconnected { get; set; }          // legacy tile option, unused since v0.4

    /// <summary>Default status→style mapping (SPEC decision 11). Missing entries are
    /// filled in on load, so a hand-edited config only needs the overrides.</summary>
    public static Dictionary<string, StatusStyle> DefaultStatusStyles() => new()
    {
        ["idle"] = new StatusStyle { Color = "gray" },
        ["working"] = new StatusStyle { Color = "blue" },
        ["waiting"] = new StatusStyle { Color = "orange", AltColor = "black", UntilAcknowledge = true },
        ["done"] = new StatusStyle { Color = "green", AltColor = "black", UntilAcknowledge = true },
        ["error"] = new StatusStyle { Color = "red", AltColor = "black", UntilAcknowledge = true },
    };
}
