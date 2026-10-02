using System.Collections.Generic;
using Microsoft.UI.Xaml;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;
using SpaceNotch.Core.Weather;

namespace SpaceNotch_App.Windows;

/// <summary>
/// Aperçu météo (F10) : au repos, la notch montre l'heure (si elle est
/// réglée) ; au survol, elle s'élargit un peu et ajoute l'icône du temps —
/// la pluie tombe vraiment, pixel par pixel — et la température. Rien sans
/// ville réglée, rien sur un côté.
/// </summary>
public sealed partial class IslandWindow
{
    /// <summary>Largeur du repos survolé avec la météo : l'heure, l'icône, la température.</summary>
    private const double WeatherPreviewWidth = 150;

    private string? _weatherShown;

    private bool WeatherAtRest
        => _controller.PresentedActivity is null
            && _weatherFeature.Current is not null
            && !UsesSideTab;

    /// <summary>Forme de l'aperçu du repos quand la météo s'y ajoute ; null sinon.</summary>
    private IslandFootprint? RestWeatherPreview()
    {
        if (!WeatherAtRest)
        {
            return null;
        }

        // Depuis la forme du repos : assoupie, la notch a déjà la taille de l'aperçu.
        IslandFootprint preview = IslandFootprint.PreviewOf(IslandPresentationTier.Idle, IslandFootprint.Idle);
        return new IslandFootprint(System.Math.Max(preview.Width, WeatherPreviewWidth), System.Math.Max(preview.Height, 30));
    }

    /// <summary>Montre ou cache la météo dans la lèvre du repos.</summary>
    private void ShowRestWeather()
    {
        bool show = WeatherAtRest && (_controller.State == IslandState.Preview || _dozing);

        if (!show || _weatherFeature.Current is not { } report)
        {
            WeatherGlyph.Visibility = Visibility.Collapsed;
            WeatherText.Visibility = Visibility.Collapsed;
            _weatherShown = null;
            return;
        }

        // Au survol, l'heure accompagne toujours la météo.
        IdleClock.Visibility = Visibility.Visible;
        IdleStatusDot.Visibility = Visibility.Collapsed;
        WeatherGlyph.Visibility = Visibility.Visible;
        WeatherText.Visibility = Visibility.Visible;
        WeatherGlyph.Key = report.IconKey;
        SpaceNotch_App.Views.InkRefresh.Set(WeatherText, report.Temperature, UseSpringAnimations());
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            WeatherText,
            $"{_weatherFeature.PlaceName} {report.Temperature} {WeatherCodes.Describe(report.Code, SpaceNotch.Core.Localization.Lang.French)}");

        // L'animation repart à chaque survol, pas à chaque rendu.
        string key = report.IconKey + report.Temperature;

        if (key == _weatherShown)
        {
            return;
        }

        _weatherShown = key;
        int length = WeatherAnimation.Length(report.IconKey);

        if (length > 0)
        {
            var frames = new List<bool[]>();

            for (int i = 0; i < length * 6; i++)
            {
                frames.Add(WeatherAnimation.Frame(report.IconKey, i));
            }

            WeatherGlyph.Play(frames, WeatherAnimation.FrameMilliseconds);
        }
    }
}
