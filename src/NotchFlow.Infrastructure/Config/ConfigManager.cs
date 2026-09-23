using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace NotchFlow.Infrastructure.Config;

/// <summary>
/// Persistance des préférences dans <c>%AppData%\NotchFlow\config.json</c>.
///
/// Aucune donnée ne quitte la machine, aucun compte n'est requis : la
/// configuration est un simple fichier local, lisible et modifiable à la main.
///
/// La sérialisation passe par <see cref="AppSettingsJsonContext"/>, généré à la
/// compilation. Ce n'est pas un détail d'implémentation : l'élagage activé par le
/// toolchain WinUI désactive la sérialisation par réflexion, et une
/// sérialisation réflexive échouerait donc silencieusement à l'exécution.
/// </summary>
public sealed class ConfigManager
{
    private readonly string _configFilePath;

    /// <summary>
    /// Signalé lorsqu'une écriture échoue.
    ///
    /// La tolérance reste entière : une configuration non enregistrée ne doit
    /// jamais interrompre l'application. Mais elle ne doit pas non plus passer
    /// inaperçue : une préférence perdue au redémarrage est indiscernable, du
    /// point de vue de l'utilisateur, d'une régression fonctionnelle. La couche
    /// hôte branche ce signal sur le journal.
    /// </summary>
    public Action<string, Exception>? WriteFailed { get; set; }

    /// <summary>Signalé lorsque le fichier existe mais ne peut pas être lu.</summary>
    public Action<string, Exception>? ReadFailed { get; set; }

    /// <summary>
    /// Crée un gestionnaire de configuration.
    /// </summary>
    /// <param name="configDirectory">
    /// Répertoire de stockage. Par défaut <c>%AppData%\NotchFlow</c>. Un répertoire
    /// explicite est accepté pour que les tests exercent le vrai chemin de fichier
    /// sans toucher aux préférences de l'utilisateur.
    /// </param>
    public ConfigManager(string? configDirectory = null)
    {
        string folder = configDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NotchFlow");

        Directory.CreateDirectory(folder);
        _configFilePath = Path.Combine(folder, "config.json");
    }

    /// <summary>Chemin du fichier de configuration, exposé pour les diagnostics.</summary>
    public string ConfigFilePath => _configFilePath;

    /// <summary>
    /// Lecture synchrone, utilisée au démarrage : la configuration doit être
    /// connue avant la création des fenêtres, puisque c'est elle qui détermine
    /// leur géométrie et leur mode de fond.
    /// </summary>
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_configFilePath))
            {
                var defaults = new AppSettings();
                Save(defaults);
                return defaults;
            }

            string json = File.ReadAllText(_configFilePath);
            AppSettings? settings = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);

            if (settings is null)
            {
                ReadFailed?.Invoke(_configFilePath, new InvalidDataException("Configuration vide."));
                return new AppSettings();
            }

            settings.Sanitize();
            return settings;
        }
        catch (Exception ex)
        {
            // Un fichier corrompu ou inaccessible ne doit jamais empêcher
            // l'application de démarrer : on repart des valeurs par défaut.
            ReadFailed?.Invoke(_configFilePath, ex);

            var fallback = new AppSettings();
            fallback.Sanitize();
            return fallback;
        }
    }

    /// <summary>Écriture synchrone, utilisée lorsqu'une préférence change.</summary>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            settings.Sanitize();
            File.WriteAllText(
                _configFilePath,
                JsonSerializer.Serialize(settings, AppSettingsJsonContext.Default.AppSettings));
        }
        catch (Exception ex)
        {
            // Tolérance d'écriture : une préférence non enregistrée n'est pas
            // une raison d'interrompre l'application, mais elle est signalée.
            WriteFailed?.Invoke(_configFilePath, ex);
        }
    }

    public async Task<AppSettings> LoadAsync()
    {
        if (!File.Exists(_configFilePath))
        {
            return Load();
        }

        try
        {
            string json = await File.ReadAllTextAsync(_configFilePath).ConfigureAwait(false);
            AppSettings? settings = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);

            if (settings is null)
            {
                return Load();
            }

            settings.Sanitize();
            return settings;
        }
        catch (Exception ex)
        {
            ReadFailed?.Invoke(_configFilePath, ex);
            return Load();
        }
    }

    public Task SaveAsync(AppSettings settings)
    {
        Save(settings);
        return Task.CompletedTask;
    }
}
