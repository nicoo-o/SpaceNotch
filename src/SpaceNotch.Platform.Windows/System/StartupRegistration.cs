using System;
using Microsoft.Win32;

namespace SpaceNotch.Platform.Windows.System;

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

    /// <summary>
    /// Où le Gestionnaire des tâches (onglet Démarrage) et Paramètres › Applications
    /// › Démarrage notent qu'une inscription est désactivée, sans toucher à
    /// <c>Run</c> (n° 65).
    /// </summary>
    private const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    /// <summary>Nom de la valeur, visible par l'utilisateur dans le gestionnaire des tâches.</summary>
    public const string ValueName = "SpaceNotch";

    /// <summary>Nom de la valeur sous l'ancien nom du produit.</summary>
    private const string LegacyValueName = "NotchFlow";

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
    /// L'état noté par Windows pour une inscription : sans valeur, elle est
    /// active ; sinon, un premier octet impair veut dire « désactivée » (02 :
    /// active, 03 : désactivée depuis le Gestionnaire des tâches).
    /// </summary>
    public static bool IsApproved(byte[]? state) => state is not { Length: > 0 } || (state[0] & 1) == 0;

    /// <summary>
    /// Inscrite <em>et</em> laissée active par Windows : ce que l'interrupteur
    /// des Réglages doit montrer. Il disait « activé » alors que le Gestionnaire
    /// des tâches l'avait désactivée (n° 65).
    /// </summary>
    public static bool IsEnabled(string expectedCommand)
    {
        if (!IsRegistered(expectedCommand))
        {
            return false;
        }

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, writable: false);
            return IsApproved(key?.GetValue(ValueName) as byte[]);
        }
        catch (Exception)
        {
            // Illisible : on s'en tient à l'inscription.
            return true;
        }
    }

    /// <summary>
    /// Lève la désactivation notée par Windows : l'utilisateur vient de rallumer
    /// l'interrupteur des Réglages. Seulement sur ce geste — une mise à jour qui
    /// réinscrit l'application ne doit pas défaire un choix fait dans le
    /// Gestionnaire des tâches.
    /// </summary>
    public static void ClearWindowsDisable()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception)
        {
            // Sans droit d'écriture : l'interrupteur relit l'état réel ensuite.
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

            // L'inscription faite sous l'ancien nom du produit est retirée dans
            // tous les cas : sans cela, une mise à jour démarrerait deux fois.
            key.DeleteValue(LegacyValueName, throwOnMissingValue: false);

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
