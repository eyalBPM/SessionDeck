using WinGrid.Interop;

namespace WinGrid.Services;

/// <summary>
/// Global WinEvent hook (no polling — SPEC §5): title changes and window destruction.
/// Must be started on a thread with a message pump (the UI thread); callbacks arrive there.
/// </summary>
public sealed class WindowTracker : IDisposable
{
    private readonly NativeMethods.WinEventDelegate _proc;   // kept alive for the native hook
    private IntPtr _hook;

    public event Action<IntPtr, string>? TitleChanged;
    public event Action<IntPtr>? WindowDestroyed;

    public WindowTracker()
    {
        _proc = OnWinEvent;
    }

    public void Start()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_DESTROY, NativeMethods.EVENT_OBJECT_NAMECHANGE,
            IntPtr.Zero, _proc, 0, 0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != NativeMethods.OBJID_WINDOW || idChild != 0 || hwnd == IntPtr.Zero)
            return;

        if (eventType == NativeMethods.EVENT_OBJECT_DESTROY)
            WindowDestroyed?.Invoke(hwnd);
        else if (eventType == NativeMethods.EVENT_OBJECT_NAMECHANGE)
            TitleChanged?.Invoke(hwnd, NativeMethods.GetWindowTextSafe(hwnd));
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
