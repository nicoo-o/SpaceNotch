using System;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SpaceNotch.Core.Activities;
using SpaceNotch_App.Views;
using Windows.UI;

namespace SpaceNotch_App.Views.Scenes;

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

    /// <summary>L'action annoncée par le bouton principal, pour ne la poser qu'au changement.</summary>
    private string? _action;

    public void Apply(IslandActivity activity)
    {
        _activityId = activity.Id;

        // Sans contenu, un état neutre plutôt que celui de l'activité précédente :
        // la fin d'une session gardait « Suspendre » sur une mesure arrêtée (n° 78).
        TimerPayload timer = activity.Payload as TimerPayload ?? new TimerPayload(TimeSpan.Zero, false, string.Empty);

        TimeText.Text = timer.Formatted;
        ModeText.Text = timer.Mode.ToUpperInvariant();
        NoteText.Text = timer.Note ?? string.Empty;
        NoteText.Visibility = string.IsNullOrEmpty(timer.Note) ? Visibility.Collapsed : Visibility.Visible;

        // Le bouton ne change plus de **remplissage** en marche : aucun contrôle
        // n'est rempli, par règle de matière. Ce qui change est le glyphe — pause
        // ou lecture — et l'intensité du trait, ce qui suffit à dire l'état sans
        // faire clignoter la carte à chaque battement.
        ToggleIcon.Key = timer.IsRunning ? "Pause" : "Play";

        // Un bouton fait d'une icône : Narrateur lit ce nom, qui suit l'état (n° 48),
        // et un minuteur suspendu se reprend (n° 78). L'infobulle dit la même chose.
        string action = timer.Toggle switch
        {
            TimerToggle.Pause => SpaceNotch.Core.Localization.Lang.T("Suspendre", "Pause"),
            TimerToggle.Resume => SpaceNotch.Core.Localization.Lang.T("Reprendre", "Resume"),
            _ => SpaceNotch.Core.Localization.Lang.T("Démarrer", "Start")
        };
        // Seulement quand l'action change : le rendu revient à chaque seconde, et
        // une infobulle réaffectée se refermait sous le pointeur.
        if (action != _action)
        {
            _action = action;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ToggleButton, action);
            ToolTipService.SetToolTip(ToggleButton, action);
        }

        ToggleButton.BorderBrush = Ink(
            timer.IsRunning ? "NfStrokeSubtleBrush" : "NfStrokeStrongBrush",
            timer.IsRunning ? (byte)0x14 : (byte)0x24);

        ToggleIcon.Tint = Ink(
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
        => SpaceNotch_App.UI.ThemeBrushes.Get(key, new SolidColorBrush(Color.FromArgb(fallbackAlpha, 0xFF, 0xFF, 0xFF)));

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
