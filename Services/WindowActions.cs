using WinGrid.Interop;

namespace WinGrid.Services;

public static class WindowActions
{
    /// <summary>Focus: bring the real window to front at its current position (SPEC §F3).</summary>
    public static void Focus(IntPtr hwnd)
    {
        if (NativeMethods.IsIconic(hwnd))
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        NativeMethods.SetForegroundWindow(hwnd);
    }

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
