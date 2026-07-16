using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using WinGrid.Models;

namespace WinGrid.Services;

/// <summary>
/// Persistence with permanent auto-save (SPEC §F7): every change is queued and flushed
/// after a ~1s debounce; writes are atomic (temp file + rename). No "save" button exists.
/// </summary>
public sealed class ConfigStore
{
    public static readonly string ConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinGrid");
    public static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly Func<AppConfig> _snapshot;
    private readonly DispatcherTimer _debounce;

    public ConfigStore(Func<AppConfig> snapshot)
    {
        _snapshot = snapshot;
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); SaveNow(); };
    }

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath)) ?? new AppConfig();
        }
        catch
        {
            // Corrupt config — start fresh rather than crash on startup.
        }
        return new AppConfig();
    }

    public void QueueSave()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    public void SaveNow()
    {
        _debounce.Stop();
        try
        {
            Directory.CreateDirectory(ConfigDir);
            string tmp = ConfigPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_snapshot(), JsonOptions));
            File.Move(tmp, ConfigPath, overwrite: true);
        }
        catch
        {
            // Never let a failed save take down the app.
        }
    }
}
