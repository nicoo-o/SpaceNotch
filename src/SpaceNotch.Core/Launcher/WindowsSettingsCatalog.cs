namespace SpaceNotch.Core.Launcher;

/// <summary>Une page de Paramètres de Windows, en français et en anglais, avec ses mots-clés.</summary>
/// <param name="Uri">Adresse <c>ms-settings:</c> qui ouvre la page.</param>
/// <param name="French">Nom en français, tel que Windows l'affiche.</param>
/// <param name="English">Nom en anglais.</param>
/// <param name="Keywords">Autres mots par lesquels on la cherche.</param>
public sealed record WindowsSetting(string Uri, string French, string English, string Keywords);

/// <summary>
/// Les pages de Paramètres qu'on cherche le plus : « bluetooth » ouvre
/// directement Paramètres › Bluetooth et appareils. Les adresses sont celles
/// que Microsoft documente pour <c>ms-settings:</c>.
/// </summary>
public static class WindowsSettingsCatalog
{
    public static IReadOnlyList<WindowsSetting> All { get; } =
    [
        new("ms-settings:bluetooth", "Bluetooth et appareils", "Bluetooth & devices", "bluetooth appareil casque souris clavier device"),
        new("ms-settings:connecteddevices", "Ajouter un appareil Bluetooth", "Add a Bluetooth device", "bluetooth appairer pair ajouter"),
        new("ms-settings:network-wifi", "Wi-Fi", "Wi-Fi", "wifi wi-fi réseau internet sans fil wireless"),
        new("ms-settings:network-status", "Réseau et Internet", "Network & internet", "réseau internet ethernet vpn network"),
        new("ms-settings:network-vpn", "VPN", "VPN", "vpn réseau privé"),
        new("ms-settings:display", "Écran", "Display", "écran affichage résolution luminosité échelle display resolution brightness scale"),
        new("ms-settings:nightlight", "Éclairage nocturne", "Night light", "nuit lumière bleue night light"),
        new("ms-settings:sound", "Son", "Sound", "son audio volume haut-parleur micro sound speaker microphone"),
        new("ms-settings:notifications", "Notifications", "Notifications", "notifications alertes ne pas déranger"),
        new("ms-settings:quiethours", "Assistant de concentration", "Focus", "concentration focus ne pas déranger do not disturb"),
        new("ms-settings:powersleep", "Alimentation et batterie", "Power & battery", "batterie alimentation veille économie power battery sleep"),
        new("ms-settings:batterysaver", "Économiseur de batterie", "Battery saver", "batterie économie énergie battery saver"),
        new("ms-settings:storagesense", "Stockage", "Storage", "stockage disque espace nettoyage storage disk"),
        new("ms-settings:multitasking", "Multitâche", "Multitasking", "multitâche fenêtres ancrage snap"),
        new("ms-settings:about", "Informations système", "About", "à propos système pc nom version windows about"),
        new("ms-settings:personalization-background", "Arrière-plan", "Background", "fond d'écran arrière-plan wallpaper background"),
        new("ms-settings:colors", "Couleurs", "Colors", "couleurs thème sombre clair mode dark light accent"),
        new("ms-settings:themes", "Thèmes", "Themes", "thèmes apparence"),
        new("ms-settings:lockscreen", "Écran de verrouillage", "Lock screen", "verrouillage écran lock"),
        new("ms-settings:taskbar", "Barre des tâches", "Taskbar", "barre des tâches taskbar"),
        new("ms-settings:fonts", "Polices", "Fonts", "polices fonts"),
        new("ms-settings:appsfeatures", "Applications installées", "Installed apps", "applications installées désinstaller programmes apps uninstall"),
        new("ms-settings:defaultapps", "Applications par défaut", "Default apps", "par défaut navigateur défaut default browser"),
        new("ms-settings:startupapps", "Applications au démarrage", "Startup apps", "démarrage startup lancement"),
        new("ms-settings:yourinfo", "Vos informations", "Your info", "compte profil account"),
        new("ms-settings:signinoptions", "Options de connexion", "Sign-in options", "connexion mot de passe code pin windows hello password"),
        new("ms-settings:dateandtime", "Date et heure", "Date & time", "date heure fuseau horaire time zone"),
        new("ms-settings:regionlanguage", "Langue et région", "Language & region", "langue région language"),
        new("ms-settings:keyboard", "Clavier", "Keyboard", "clavier saisie keyboard typing"),
        new("ms-settings:mousetouchpad", "Souris", "Mouse", "souris pointeur mouse"),
        new("ms-settings:devices-touchpad", "Pavé tactile", "Touchpad", "pavé tactile touchpad trackpad"),
        new("ms-settings:printers", "Imprimantes et scanners", "Printers & scanners", "imprimante scanner printer"),
        new("ms-settings:gaming-gamebar", "Game Bar", "Game Bar", "jeux game bar capture"),
        new("ms-settings:easeofaccess", "Accessibilité", "Accessibility", "accessibilité narrateur loupe contraste accessibility"),
        new("ms-settings:privacy", "Confidentialité et sécurité", "Privacy & security", "confidentialité vie privée sécurité privacy"),
        new("ms-settings:privacy-microphone", "Microphone (confidentialité)", "Microphone privacy", "micro microphone confidentialité"),
        new("ms-settings:privacy-webcam", "Caméra (confidentialité)", "Camera privacy", "caméra webcam confidentialité camera"),
        new("ms-settings:windowsupdate", "Windows Update", "Windows Update", "mise à jour update"),
        new("ms-settings:windowsdefender", "Sécurité Windows", "Windows Security", "antivirus sécurité defender"),
        new("ms-settings:recovery", "Récupération", "Recovery", "récupération réinitialiser reset")
    ];

    /// <summary>Nom affiché selon la langue de Windows.</summary>
    public static string NameFor(WindowsSetting setting, bool french)
    {
        ArgumentNullException.ThrowIfNull(setting);
        return french ? setting.French : setting.English;
    }
}
