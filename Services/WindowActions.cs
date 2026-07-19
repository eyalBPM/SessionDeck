using System.Diagnostics;
using System.IO;
using SessionDeck.Interop;

namespace SessionDeck.Services;

public static class WindowActions
{
    /// <summary>Open VSCode on a workspace folder (card click when no window is bound).
    /// The new window auto-binds via the WindowAppeared tracker event.
    /// Must go through the CLI shim (bin\code.cmd): launching Code.exe directly fails
    /// silently to open a window when another VSCode instance is already running
    /// (verified 2026-07-19).</summary>
    public static bool LaunchVsCode(string workspacePath)
    {
        try
        {
            string shim = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Microsoft VS Code", "bin", "code.cmd");
            string cli = File.Exists(shim) ? shim : "code";   // fall back to PATH
            var psi = new ProcessStartInfo("cmd.exe")
            {
                // cmd /c ""shim" "path"" — outer quotes protect the two quoted args.
                Arguments = $"/c \"\"{cli}\" \"{workspacePath}\"\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Focus: bring the real window to front at its current position (SPEC §F3).</summary>
    public static void Focus(IntPtr hwnd)
    {
        if (NativeMethods.IsIconic(hwnd))
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        NativeMethods.SetForegroundWindow(hwnd);
    }

    /// <summary>Graceful close (WM_CLOSE) — same as clicking the window's X, so VSCode
    /// gets to run its normal shutdown (save prompts etc.).</summary>
    public static void Close(IntPtr hwnd)
        => NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

    /// <summary>Pin: move the real window to the stage rect and activate it (SPEC §F3).</summary>
    public static void MoveTo(IntPtr hwnd, RECT rect)
    {
        if (NativeMethods.IsIconic(hwnd) || NativeMethods.IsZoomed(hwnd))
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, rect.Left, rect.Top, rect.Width, rect.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_SHOWWINDOW);
        NativeMethods.SetForegroundWindow(hwnd);
    }
}
