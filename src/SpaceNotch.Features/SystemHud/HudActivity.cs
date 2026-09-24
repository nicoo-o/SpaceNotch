using System;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;

namespace SpaceNotch.Features.SystemHud;

/// <summary>
/// Forme commune des retours système — volume, luminosité.
///
/// <para>
/// <b>Un retour, pas une ouverture.</b> Changer le volume ne doit pas ouvrir la
/// notch en grand : c'est un retour bref qui recouvre ce qui était présenté —
/// une musique, par exemple — puis rend la main. Il se présente donc dans la
/// forme compacte : glyphe, fil de niveau, valeur. Un clic l'ouvre, comme
/// n'importe quelle activité, sur la valeur en grand.
/// </para>
///
/// <para>
/// La valeur est une mesure, pas une information d'état : pas de teinte, pas de
/// mouvement hypnotique, pas d'annonce à Narrateur — Windows dit déjà le volume.
/// </para>
/// </summary>
public static class HudActivity
{
    /// <summary>Construit l'activité d'un retour système.</summary>
    public static IslandActivity Build(
        string id,
        string featureId,
        string sceneKey,
        string label,
        double value,
        double maximum,
        string iconKey,
        string source,
        TimeSpan lifetime,
        bool muted = false,
        DateTimeOffset? createdAt = null)
    {
        double max = maximum <= 0 ? 100 : maximum;
        double clamped = Math.Clamp(value, 0, max);
        string valueText = muted
            ? "Muet"
            : string.Create(System.Globalization.CultureInfo.CurrentCulture, $"{Math.Round(clamped / max * 100):0} %");

        return new IslandActivity
        {
            Id = id,
            FeatureId = featureId,
            SceneKey = sceneKey,
            Title = label,
            Subtitle = source,
            Source = source,
            IconKey = iconKey,
            State = IslandActivityState.SystemHud,
            Priority = ActivityPriority.Normal,
            Presentation = IslandPresentationTier.Signal,
            Policy = ActivityPresentationPolicy.Temporary,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            Duration = lifetime,
            Metric = valueText,
            Progress = muted ? 0 : clamped / max,
            Payload = new HudPayload(clamped, max, label, valueText, iconKey)
        };
    }
}
