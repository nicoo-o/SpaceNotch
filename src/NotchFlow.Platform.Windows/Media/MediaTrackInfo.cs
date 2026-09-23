using System;
using NotchFlow.Core.Activities;

namespace NotchFlow.Platform.Windows.Media;

/// <summary>
/// Métadonnées d'une piste média extraite d'une session Windows (GSMTC).
///
/// La pochette est déjà décodée et la teinte déjà extraite au moment où cet
/// instantané est produit : la tâche coûteuse est ainsi faite une seule fois, sur
/// le thread d'arrière-plan, et non à chaque rendu de l'Island.
/// </summary>
public sealed record MediaTrackInfo(
    string Title,
    string Artist,
    string AlbumTitle,
    string AppId,
    bool IsPlaying,
    TimeSpan Position,
    TimeSpan Duration,
    byte[]? ArtworkBytes,
    ActivityTint? Tint
);
