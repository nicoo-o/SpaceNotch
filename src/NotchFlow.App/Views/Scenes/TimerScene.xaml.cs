using System;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NotchFlow.Core.Activities;
using NotchFlow_App.Views;
using Windows.UI;

namespace NotchFlow_App.Views.Scenes;

/// <summary>
/// Minuteur, compte à rebours et chronomètre.
///
/// La scène n'exécute rien : elle déclare l'intention — démarrer, suspendre,
/// remettre à zéro — et la transmet par identifiant d'action. Le compteur qui bat
/// toutes les secondes vit dans la fonctionnalité, pas ici.
/// </summary>
public sealed partial class TimerScene : UserControl, IIslandSceneView
{
    public const string ToggleAction = "timer.toggle";

    public const string ResetAction = "timer.reset";

    private string? _activityId;

    public TimerScene()
    {
        InitializeComponent();
    }

    public event EventHandler<IslandActionRequest>? ActionRequested;

    public FrameworkElement Root => this;

    public void Apply(IslandActivity activity)
    {
        _activityId = activity.Id;

        if (activity.Payload is not TimerPayload timer)
        {
            return;
        }

        TimeText.Text = timer.Formatted;
        ModeText.Text = timer.Mode.ToUpperInvariant();

        // Le bouton ne change plus de **remplissage** en marche : aucun contrôle
        // n'est rempli, par règle de matière. Ce qui change est le glyphe — pause
        // ou lecture — et l'intensité du trait, ce qui suffit à dire l'état sans
        // faire clignoter la carte à chaque battement.
        ToggleIcon.Glyph = timer.IsRunning ? "\uE769" : "\uE768";

        ToggleButton.BorderBrush = Ink(
            timer.IsRunning ? "NfStrokeSubtleBrush" : "NfStrokeStrongBrush",
            timer.IsRunning ? (byte)0x14 : (byte)0x24);

        ToggleIcon.Foreground = Ink(
            timer.IsRunning ? "NfTextSecondaryBrush" : "NfTextPrimaryBrush",
            timer.IsRunning ? (byte)0x9E : (byte)0xF0);
    }

    /// <summary>
    /// Lit un pinceau des jetons de conception, avec repli neutre.
    ///
    /// Une clé absente dégrade la finition mais laisse le contrôle utilisable :
    /// une exception de ressource, elle, viderait la scène.
    /// </summary>
    private static Brush Ink(string key, byte fallbackAlpha)
        => Application.Current?.Resources?.TryGetValue(key, out object? value) == true && value is Brush brush
            ? brush
            : new SolidColorBrush(Color.FromArgb(fallbackAlpha, 0xFF, 0xFF, 0xFF));

    private void OnToggleClicked(object sender, RoutedEventArgs e)
        => Raise(ToggleAction);

    private void OnResetClicked(object sender, RoutedEventArgs e)
        => Raise(ResetAction);

    private void Raise(string actionId)
    {
        if (_activityId is null)
        {
            return;
        }

        ActionRequested?.Invoke(this, new IslandActionRequest(_activityId, actionId));
    }
}
