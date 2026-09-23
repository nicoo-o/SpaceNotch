using System;
using Microsoft.Win32;

namespace NotchFlow.Platform.Windows.System;

/// <summary>
/// Inscription de l'application au démarrage de Windows.
///
/// L'inscription se fait dans la ruche de l'utilisateur courant
/// (<c>HKCU\...\Run</c>), jamais dans la ruche machine : le projet s'interdit
/// toute élévation de privilèges et toute modification qui concernerait d'autres
/// comptes que le sien.
///
/// Aucune écriture n'est effectuée au démarrage de l'application. Le registre
/// n'est touché que lorsque l'utilisateur agit explicitement sur la préférence :
/// un logiciel qui s'inscrit tout seul au premier lancement est précisément ce
/// que ce projet refuse d'être.
/// </summary>
public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Nom de la valeur, visible par l'utilisateur dans le gestionnaire des tâches.</summary>
    public const string ValueName = "NotchFlow";

    /// <summary>
    /// Indique si l'application est inscrite, et si la commande enregistrée
    /// correspond encore à l'exécutable courant.
    /// </summary>
    public static bool IsRegistered(string expectedCommand)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedCommand);

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);

            if (key?.GetValue(ValueName) is not string existing)
            {
                return false;
            }

            return string.Equals(existing, expectedCommand, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            // Une ruche illisible n'est pas une raison d'échouer : on considère
            // l'inscription absente, ce qui est le cas le plus sûr.
            return false;
        }
    }

    /// <summary>
    /// Inscrit ou désinscrit l'application.
    ///
    /// Le résultat est retourné plutôt que silencieusement ignoré : une case à
    /// cocher qui ne fait rien est pire qu'une case absente.
    /// </summary>
    public static bool SetEnabled(bool enabled, string command, out string? error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        error = null;

        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                ?? throw new InvalidOperationException("Clé de démarrage inaccessible.");

            if (enabled)
            {
                key.SetValue(ValueName, command, RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Commande d'inscription attendue pour l'exécutable courant.
    /// </summary>
    public static string BuildCommand(string executablePath)
        => $"\"{executablePath}\" --startup";
}
