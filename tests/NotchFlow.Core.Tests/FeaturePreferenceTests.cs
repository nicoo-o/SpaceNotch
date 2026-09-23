using NotchFlow.Core.Features;
using NotchFlow.Infrastructure.Config;
using Xunit;

namespace NotchFlow.Core.Tests;

/// <summary>
/// Vérifie la correspondance entre une fonctionnalité et sa préférence.
///
/// C'est le maillon qui rend une bascule durable : sans lui, désactiver une
/// fonctionnalité l'arrêterait bel et bien, mais le redémarrage suivant la
/// relancerait — l'utilisateur croirait à une régression de l'arrêt.
/// </summary>
public class FeaturePreferenceTests
{
    [Fact]
    public void Defaults_MatchTheDocumentedBehaviour()
    {
        var settings = new AppSettings();

        // Tout est actif sauf la surveillance du presse-papier : aucune donnée
        // n'est observée tant que l'utilisateur ne l'a pas demandé.
        Assert.True(settings.IsFeatureEnabled(FeatureKeys.Media));
        Assert.True(settings.IsFeatureEnabled(FeatureKeys.VolumeHud));
        Assert.True(settings.IsFeatureEnabled(FeatureKeys.Notifications));
        Assert.True(settings.IsFeatureEnabled(FeatureKeys.Bluetooth));
        Assert.True(settings.IsFeatureEnabled(FeatureKeys.FileShelf));
        Assert.False(settings.IsFeatureEnabled(FeatureKeys.Clipboard));
    }

    [Fact]
    public void BindFeature_IsReadBackByIdentifier()
    {
        var settings = new AppSettings();

        foreach (string key in new[]
                 {
                     FeatureKeys.Media,
                     FeatureKeys.VolumeHud,
                     FeatureKeys.Notifications,
                     FeatureKeys.Bluetooth,
                     FeatureKeys.FileShelf,
                     FeatureKeys.Clipboard
                 })
        {
            Assert.True(settings.BindFeature(key, enabled: false));
            Assert.False(settings.IsFeatureEnabled(key));

            Assert.True(settings.BindFeature(key, enabled: true));
            Assert.True(settings.IsFeatureEnabled(key));
        }
    }

    [Fact]
    public void BindFeature_OnPomodoro_IsNotPersisted_ButStaysEnabled()
    {
        var settings = new AppSettings();

        // Le minuteur de focus n'expose pas de bascule enregistrée : le refuser
        // silencieusement serait pire que de le laisser actif.
        Assert.False(settings.BindFeature(FeatureKeys.Pomodoro, enabled: false));
        Assert.True(settings.IsFeatureEnabled(FeatureKeys.Pomodoro));
    }

    [Fact]
    public void UnknownFeature_IsEnabledByDefault()
    {
        var settings = new AppSettings();

        Assert.True(settings.IsFeatureEnabled("feature.inconnue"));
        Assert.False(settings.BindFeature("feature.inconnue", enabled: false));
    }

    [Fact]
    public void Settings_SurviveRoundTripThroughSanitize()
    {
        var settings = new AppSettings();

        settings.BindFeature(FeatureKeys.Bluetooth, enabled: false);
        settings.BindFeature(FeatureKeys.Clipboard, enabled: true);

        // Sanitize est appelé à chaque chargement : il ne doit pas réinitialiser
        // les préférences d'activation.
        settings.Sanitize();

        Assert.False(settings.IsFeatureEnabled(FeatureKeys.Bluetooth));
        Assert.True(settings.IsFeatureEnabled(FeatureKeys.Clipboard));
    }
}
