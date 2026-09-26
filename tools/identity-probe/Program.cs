using System.Runtime.InteropServices;
using Windows.UI.Notifications.Management;

uint length = 0;
int result = GetCurrentPackageFullName(ref length, IntPtr.Zero);
Console.WriteLine(result == 15700 ? "IDENTITE: absente" : $"IDENTITE: presente (code {result})");

if (result != 15700)
{
    try
    {
        UserNotificationListener listener = UserNotificationListener.Current;
        Console.WriteLine($"ACCES NOTIFICATIONS: {listener.GetAccessStatus()}");

        try
        {
            listener.NotificationChanged += (_, _) => { };
            Console.WriteLine("EVENEMENT NotificationChanged: accepte");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"EVENEMENT NotificationChanged: refuse ({ex.HResult:X8} {ex.Message})");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"ECOUTEUR: erreur {ex.HResult:X8} {ex.Message}");
    }
}

return 0;

[DllImport("kernel32.dll")]
static extern int GetCurrentPackageFullName(ref uint length, IntPtr name);
