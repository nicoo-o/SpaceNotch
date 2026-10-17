using System;
using System.Runtime.InteropServices;
using SpaceNotch.Platform.Windows.Win32;

namespace SpaceNotch.Platform.Windows.Shell;

/// <summary>
/// Veille et écran (n° 35). Sur un PC en veille moderne (S0, la plupart des
/// portables, dont celui de l'auteur), une application de bureau n'a pas la
/// garantie de recevoir <c>PBT_APMSUSPEND</c> sans s'y inscrire, et l'écran
/// s'éteint souvent sans veille. La fenêtre s'inscrit donc aux deux : veille et
/// reprise, et état de l'écran (allumé, éteint, assombri), reçus en
/// <c>WM_POWERBROADCAST</c>.
/// </summary>
public static partial class PowerNotifications
{
    /// <summary>GUID_CONSOLE_DISPLAY_STATE : l'écran de la console s'allume, s'éteint ou s'assombrit.</summary>
    public static readonly Guid ConsoleDisplayState = new("6FE69556-704A-47A0-8F24-C28D936FDA47");

    /// <summary>Inscrit la fenêtre ; rend les deux jetons à libérer à la fermeture (zéro si refusé).</summary>
    public static (IntPtr SuspendResume, IntPtr Display) Register(IntPtr window)
    {
        IntPtr suspend = RegisterSuspendResumeNotification(window, NativeConstants.DEVICE_NOTIFY_WINDOW_HANDLE);
        Guid display = ConsoleDisplayState;
        IntPtr screen = RegisterPowerSettingNotification(window, ref display, NativeConstants.DEVICE_NOTIFY_WINDOW_HANDLE);
        return (suspend, screen);
    }

    /// <summary>Libère les inscriptions faites par <see cref="Register"/>.</summary>
    public static void Unregister((IntPtr SuspendResume, IntPtr Display) handles)
    {
        if (handles.SuspendResume != IntPtr.Zero)
        {
            _ = UnregisterSuspendResumeNotification(handles.SuspendResume);
        }

        if (handles.Display != IntPtr.Zero)
        {
            _ = UnregisterPowerSettingNotification(handles.Display);
        }
    }

    /// <summary>
    /// Lit un <c>PBT_POWERSETTINGCHANGE</c> : vrai si c'est l'état de l'écran, avec
    /// <paramref name="on"/> à faux quand il est éteint (0) ; allumé (1) ou
    /// assombri (2) comptent comme allumé.
    /// </summary>
    public static bool TryReadDisplayState(IntPtr setting, out bool on)
    {
        on = true;

        if (setting == IntPtr.Zero)
        {
            return false;
        }

        // POWERBROADCAST_SETTING : GUID (16 octets), longueur (4), puis les données.
        var guid = Marshal.PtrToStructure<Guid>(setting);

        if (guid != ConsoleDisplayState || Marshal.ReadInt32(setting, 16) < sizeof(int))
        {
            return false;
        }

        on = Marshal.ReadInt32(setting, 20) != 0;
        return true;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr RegisterSuspendResumeNotification(IntPtr recipient, int flags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterSuspendResumeNotification(IntPtr handle);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref Guid setting, int flags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterPowerSettingNotification(IntPtr handle);
}
