using System;

namespace NotchFlow.Infrastructure.Config;

/// <summary>
/// Point unique de lecture et de modification des préférences.
///
/// Il existe parce qu'une même préférence est désormais modifiable depuis
/// plusieurs endroits — la fenêtre de réglages, le menu de la zone de
/// notification — et qu'elle est consommée par plusieurs composants : géométrie
/// de l'Island, ressort, mode de fond, activation des fonctionnalités. Sans point
/// unique, chaque surface aurait gardé sa propre copie et la dernière écrite
/// aurait gagné, au hasard.
///
/// Toute modification est enregistrée immédiatement et notifiée : une préférence
/// qui ne survit pas au redémarrage est vécue comme une régression.
/// </summary>
public sealed class SettingsService
{
    private readonly ConfigManager _config;

    public SettingsService(ConfigManager? config = null)
    {
        _config = config ?? new ConfigManager();

        // Les pannes de configuration sont remontées à la couche hôte, qui les
        // journalise : une préférence perdue sans trace est indétectable.
        _config.ReadFailed = (path, ex) => ReadFailed?.Invoke(path, ex);
        _config.WriteFailed = (path, ex) => WriteFailed?.Invoke(path, ex);

        Current = _config.Load();

        // Une configuration écrite par une version antérieure est traduite avant
        // d'être bornée : l'ordre compte, puisque la migration produit des valeurs
        // que le bornage doit ensuite valider comme les autres.
        Current.Migrate();
        Current.Sanitize();
    }

    /// <summary>Préférences courantes. Mutées uniquement via <see cref="Update"/>.</summary>
    public AppSettings Current { get; private set; }

    /// <summary>Chemin du fichier de configuration, exposé pour les diagnostics.</summary>
    public string ConfigFilePath => _config.ConfigFilePath;

    /// <summary>Signalé après chaque modification enregistrée.</summary>
    public event EventHandler<AppSettings>? Changed;

    public Action<string, Exception>? ReadFailed { get; set; }

    public Action<string, Exception>? WriteFailed { get; set; }

    /// <summary>
    /// Applique une modification, l'enregistre et notifie les abonnés. Les valeurs
    /// sont ramenées dans leurs bornes avant écriture : une géométrie aberrante ne
    /// doit pas pouvoir être enregistrée, puis relue au démarrage suivant.
    /// </summary>
    public void Update(Action<AppSettings> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        mutate(Current);
        Current.Sanitize();
        _config.Save(Current);

        Changed?.Invoke(this, Current);
    }

    /// <summary>
    /// Applique une modification et notifie, <em>sans</em> écrire le fichier.
    ///
    /// Destiné aux réglages manipulés en continu — un curseur déplacé produit des
    /// dizaines de valeurs par seconde. L'interface doit suivre en direct, mais
    /// écrire le fichier à chaque pixel serait un travail inutile et une usure
    /// gratuite du disque. L'appelant termine par <see cref="Persist"/>.
    /// </summary>
    public void UpdateTransient(Action<AppSettings> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        mutate(Current);
        Current.Sanitize();

        Changed?.Invoke(this, Current);
    }

    /// <summary>Enregistre l'état courant des préférences.</summary>
    public void Persist()
    {
        Current.Sanitize();
        _config.Save(Current);
    }

    /// <summary>Restaure les valeurs par défaut et notifie.</summary>
    public void ResetToDefaults()
    {
        Current = new AppSettings();
        Current.Sanitize();
        _config.Save(Current);

        Changed?.Invoke(this, Current);
    }

    /// <summary>
    /// Applique une préférence d'activation de fonctionnalité.
    /// </summary>
    /// <returns>
    /// <c>false</c> si la fonctionnalité n'expose pas de préférence persistée, auquel
    /// cas rien n'a été enregistré. L'appelant décide de l'effet sur le cycle de vie.
    /// </returns>
    public bool SetFeatureEnabled(string featureId, bool enabled)
    {
        bool recognised = Current.BindFeature(featureId, enabled);

        if (recognised)
        {
            _config.Save(Current);
        }

        return recognised;
    }
}
