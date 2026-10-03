using System;
using Windows.UI.ViewManagement;

namespace SpaceNotch_App.UI;

/// <summary>
/// Thème de Windows (clair ou sombre), pour l'apparence « Automatique »
/// (phase C) : jusque-là, « Automatique » valait « Sombre ». Le changement de
/// thème est signalé par <see cref="Changed"/>, hors du fil d'interface.
/// </summary>
public static class SystemTheme
{
    private static UISettings? _settings;

    /// <summary>Le thème des applications a changé (fil quelconque).</summary>
    public static event Action? Changed;

    /// <summary>Vrai si Windows est en thème clair : le fond des applications est clair.</summary>
    public static bool IsLight
    {
        get
        {
            try
            {
                global::Windows.UI.Color background = Ensure().GetColorValue(UIColorType.Background);
                return (background.R + background.G + background.B) / 3 > 128;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    private static UISettings Ensure()
    {
        if (_settings is null)
        {
            _settings = new UISettings();
            _settings.ColorValuesChanged += (_, _) => Changed?.Invoke();
        }

        return _settings;
    }

    /// <summary>Commence à écouter les changements de thème.</summary>
    public static void Watch()
    {
        try
        {
            _ = Ensure();
        }
        catch (Exception)
        {
            // Sans UISettings, « Automatique » reste sombre.
        }
    }
}
