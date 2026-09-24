using System;
using System.Collections.Generic;

namespace SpaceNotch.Platform.Windows.Privacy;

/// <summary>Capteur protégé par l'indicateur de confidentialité de Windows.</summary>
public enum CapabilityKind
{
    Microphone,
    Camera
}

/// <summary>Une application qui utilise un capteur en ce moment.</summary>
/// <param name="Kind">Capteur utilisé.</param>
/// <param name="AppId">
/// Identifiant tel que Windows le range : un nom de famille de paquet
/// (<c>Microsoft.WindowsCamera_8wekyb3d8bbwe</c>) ou un chemin d'exécutable dont
/// les séparateurs sont des <c>#</c> (<c>C:#Program Files#Zoom#Zoom.exe</c>).
/// </param>
public readonly record struct CapabilityUsage(CapabilityKind Kind, string AppId);

/// <summary>
/// Source des capteurs en cours d'utilisation. L'implémentation Windows lit le
/// magasin de consentement que l'indicateur de confidentialité du système
/// utilise lui-même, et prévient à chaque changement — sans scrutation.
/// </summary>
public interface ICapabilityUsageSource : IDisposable
{
    /// <summary>Déclenché quand un capteur est pris ou rendu, depuis n'importe quel fil.</summary>
    event EventHandler? Changed;

    /// <summary>Commence à surveiller.</summary>
    void StartWatching();

    /// <summary>Cesse de surveiller.</summary>
    void StopWatching();

    /// <summary>Capteurs en cours d'utilisation, maintenant.</summary>
    IReadOnlyList<CapabilityUsage> Snapshot();
}
