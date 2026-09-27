using System;
using System.Runtime.InteropServices;

namespace SpaceNotch.Platform.Windows.Notifications;

/// <summary>
/// Ne pas déranger de Windows (« Assistant de concentration », puis
/// « Ne pas déranger » sous Windows 11).
///
/// <para>
/// Windows ne publie pas d'API documentée pour cet état. Deux sources sont
/// lues, la première qui répond l'emporte : l'état WNF du shell
/// (<c>WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED</c>, 0 = désactivé,
/// 1 = priorité seulement, 2 = alarmes seulement), puis
/// <c>SHQueryUserNotificationState</c>, qui rapporte les heures calmes et le
/// mode présentation. Une lecture qui échoue répond « pas calme » : la notch
/// ne retient jamais une notification sur un doute.
/// </para>
/// </summary>
public static class FocusAssistProbe
{
    private const ulong QuietHoursProfile = 0x0D83063EA3BF1C75;

    /// <summary>Vrai lorsque Windows retient ses propres notifications.</summary>
    public static bool IsQuiet()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            if (TryReadProfile(out int profile))
            {
                return profile != 0;
            }

            return SHQueryUserNotificationState(out int state) == 0 && state is PresentationMode or QuietTime;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            return false;
        }
    }

    private static bool TryReadProfile(out int profile)
    {
        profile = 0;
        ulong name = QuietHoursProfile;
        uint size = sizeof(int);
        int value = 0;

        int status = NtQueryWnfStateData(ref name, IntPtr.Zero, IntPtr.Zero, out _, ref value, ref size);

        if (status != 0 || size < sizeof(int))
        {
            return false;
        }

        profile = value;
        return true;
    }

    private const int PresentationMode = 4;

    private const int QuietTime = 6;

    [DllImport("ntdll.dll")]
    private static extern int NtQueryWnfStateData(
        ref ulong stateName,
        IntPtr typeId,
        IntPtr explicitScope,
        out uint changeStamp,
        ref int buffer,
        ref uint bufferSize);

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHQueryUserNotificationState(out int state);
}
