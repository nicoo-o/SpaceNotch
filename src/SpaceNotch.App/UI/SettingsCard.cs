using Microsoft.UI.Xaml;

namespace SpaceNotch_App.UI;

/// <summary>
/// Une carte des Réglages réservée aux curieux (n° 23) : formes fines,
/// mouvement détaillé, services pour développeurs. Masquée tant que
/// « Afficher les réglages avancés » est éteint ; la recherche la trouve
/// toujours.
/// </summary>
public static class SettingsCard
{
    public static readonly DependencyProperty AdvancedProperty = DependencyProperty.RegisterAttached(
        "Advanced", typeof(bool), typeof(SettingsCard), new PropertyMetadata(false));

    public static bool GetAdvanced(DependencyObject element) => (bool)element.GetValue(AdvancedProperty);

    public static void SetAdvanced(DependencyObject element, bool value) => element.SetValue(AdvancedProperty, value);
}
