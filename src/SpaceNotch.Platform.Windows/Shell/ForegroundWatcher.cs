using System;
using System.Runtime.InteropServices;

namespace SpaceNotch.Platform.Windows.Shell;

/// <summary>Une fenêtre vient de prendre la main : son rectangle à l'écran, en pixels.</summary>
public readonly record struct ForegroundWindow(IntPtr Handle, int Left, int Top, int Right, int Bottom)
{
    public double CenterX => (Left + Right) / 2.0;

    public double CenterY => (Top + Bottom) / 2.0;
}

/// <summary>
/// Pixel suit l'app active (vague 7) : prévient quand une autre fenêtre passe
/// au premier plan. Un seul crochet système, hors processus, sans scrutation.
/// </summary>
public sealed partial class ForegroundWatcher : IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint WinEventOutOfContext = 0x0000;

    private readonly WinEventProc _callback;
    private IntPtr _hook;

    public ForegroundWatcher()
    {
        _callback = OnEvent;
    }

    /// <summary>Une autre fenêtre est au premier plan.</summary>
    public event Action<ForegroundWindow>? Changed;

    /// <summary>Fenêtres à ignorer (la notch elle-même).</summary>
    public IntPtr Ignore { get; set; }

    public void Start()
    {
        if (_hook == IntPtr.Zero)
        {
            _hook = SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, _callback, 0, 0, WinEventOutOfContext);
        }
    }

    public void Stop()
    {
        if (_hook != IntPtr.Zero)
        {
            _ = UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }

    public void Dispose() => Stop();

    private void OnEvent(IntPtr hook, uint type, IntPtr window, int objectId, int childId, uint thread, uint time)
    {
        if (window == IntPtr.Zero || window == Ignore || objectId != 0 || !GetWindowRect(window, out Rect r))
        {
            return;
        }

        // Une fenêtre sans surface (bureau, barre des tâches réduite) n'attire pas le regard.
        if (r.Right - r.Left < 80 || r.Bottom - r.Top < 60)
        {
            return;
        }

        Changed?.Invoke(new ForegroundWindow(window, r.Left, r.Top, r.Right, r.Bottom));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate void WinEventProc(IntPtr hook, uint type, IntPtr window, int objectId, int childId, uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc callback, uint process, uint thread, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out Rect rect);
}

/// <summary>
/// Bonjour et au revoir (vague 7) : la session se verrouille ou se déverrouille.
/// La fenêtre reçoit <c>WM_WTSSESSION_CHANGE</c> après <see cref="Register"/>.
/// </summary>
public static class SessionNotifications
{
    /// <summary>Message reçu par la fenêtre inscrite.</summary>
    public const uint WmSessionChange = 0x02B1;

    /// <summary>La session vient d'être verrouillée.</summary>
    public const int SessionLock = 0x7;

    /// <summary>La session vient d'être déverrouillée.</summary>
    public const int SessionUnlock = 0x8;

    private const int NotifyForThisSession = 0;

    /// <summary>Inscrit la fenêtre aux changements de session ; faux si Windows refuse.</summary>
    public static bool Register(IntPtr window) => window != IntPtr.Zero && WTSRegisterSessionNotification(window, NotifyForThisSession);

    public static void Unregister(IntPtr window)
    {
        if (window != IntPtr.Zero)
        {
            _ = WTSUnRegisterSessionNotification(window);
        }
    }

    [DllImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSRegisterSessionNotification(IntPtr window, int flags);

    [DllImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSUnRegisterSessionNotification(IntPtr window);
}
