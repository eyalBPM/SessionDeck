using Microsoft.Win32;

namespace SessionDeck.Services;

/// <summary>
/// Start with Windows (SPEC §F9): per-user Run key, no admin. Full state is then
/// restored from the profile via matcher re-bind.
/// </summary>
public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SessionDeck";

    /// <summary>One-time migration from the pre-rename "WinGrid" Run value (decision 19):
    /// if the old value exists, replace it with a "SessionDeck" value pointing at the current exe.</summary>
    public static void MigrateLegacyValue()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (key.GetValue("WinGrid") == null)
            return;
        key.DeleteValue("WinGrid", throwOnMissingValue: false);
        if (Environment.ProcessPath is { } exe)
            key.SetValue(ValueName, $"\"{exe}\"");
    }

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) != null;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled && Environment.ProcessPath is { } exe)
            key.SetValue(ValueName, $"\"{exe}\"");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
