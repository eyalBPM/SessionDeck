namespace WinGrid.Models;

public enum ZoneMode { Off, HalfLeft, HalfRight, Full }
public enum StageMode { Full, HalfLeft, HalfRight }

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
        _ => "full",
    };

    public static bool TryParseStage(string s, out StageMode m)
    {
        m = s switch
        {
            "full" => StageMode.Full,
            "half-left" => StageMode.HalfLeft,
            "half-right" => StageMode.HalfRight,
            _ => (StageMode)(-1),
        };
        return (int)m >= 0;
    }
}

public class TileConfig
{
    public int Id { get; set; }
    public string ProcessName { get; set; } = "";
    public string TitlePattern { get; set; } = "";
    public string Title { get; set; } = "";
    public bool ManualTitle { get; set; }
    public string Description { get; set; } = "";
    public string Color { get; set; } = "gray";
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
    public int SchemaVersion { get; set; } = 1;
    public int NextTileId { get; set; } = 1;
    public List<TileConfig> Tiles { get; set; } = new();
    public ZoneConfig Zone { get; set; } = new();
    public StageConfig Stage { get; set; } = new();
    public WindowBounds? Window { get; set; }
}
