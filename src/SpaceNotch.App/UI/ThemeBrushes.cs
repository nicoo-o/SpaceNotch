using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace SpaceNotch_App.UI;

/// <summary>
/// Jetons de couleur pour les éléments construits en code (phase F) : le
/// pinceau du thème <em>de l'élément</em> — sombre, clair ou contraste élevé.
/// <c>Application.Current.Resources[clé]</c> rendait toujours le thème par
/// défaut, si bien que l'aide des gestes et la pile restaient blanches sur
/// une notch claire.
/// </summary>
public static class ThemeBrushes
{
    /// <summary>Le pinceau <paramref name="key"/> pour le thème effectif de <paramref name="owner"/>.</summary>
    public static Brush Get(FrameworkElement? owner, string key, Brush? fallback = null)
    {
        string theme = owner?.ActualTheme == ElementTheme.Light ? "Light" : "Default";

        try
        {
            if (new global::Windows.UI.ViewManagement.AccessibilitySettings().HighContrast)
            {
                theme = "HighContrast";
            }
        }
        catch (Exception)
        {
            // Lecture du contraste impossible : le thème de l'élément suffit.
        }

        return Find(Application.Current.Resources, theme, key)
            ?? Find(Application.Current.Resources, "Default", key)
            ?? fallback
            ?? new SolidColorBrush(Microsoft.UI.Colors.White);
    }

    private static Brush? Find(ResourceDictionary dictionary, string theme, string key)
    {
        if (dictionary.ThemeDictionaries.TryGetValue(theme, out object? themed)
            && themed is ResourceDictionary themeDictionary
            && themeDictionary.TryGetValue(key, out object? value)
            && value is Brush brush)
        {
            return brush;
        }

        foreach (ResourceDictionary merged in dictionary.MergedDictionaries)
        {
            if (Find(merged, theme, key) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
