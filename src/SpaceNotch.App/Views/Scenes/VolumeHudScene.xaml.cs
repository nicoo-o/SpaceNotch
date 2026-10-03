using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpaceNotch.Core.Activities;
using SpaceNotch_App.Animations;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Motion;
using SpaceNotch_App.Views;

namespace SpaceNotch_App.Views.Scenes;

/// <summary>
/// Retour visuel système : volume, luminosité, muet.
///
/// La scène ne connaît pas la nature de ce qu'elle affiche : la fonctionnalité
/// lui fournit une charge utile homogène — valeur, libellé, clé d'icône — et elle
/// la projette. C'est pourquoi la luminosité n'a pas nécessité de scène
/// supplémentaire.
/// </summary>
public sealed partial class VolumeHudScene : UserControl, IIslandSceneView
{
    private readonly ValueRoller _roller;

    private double _maximum = 100;
    private bool _muted;

    public VolumeHudScene()
    {
        InitializeComponent();

        _roller = new ValueRoller(ShowValue, TimeSpan.FromMilliseconds(MotionPresets.DurationMs(MotionKind.Quick) * 1.5));
        Unloaded += (_, _) => _roller.Stop();
    }

    public FrameworkElement Root => this;

    /// <summary>
    /// Défile-t-on la valeur ? Renseigné par la fenêtre, qui seule connaît la
    /// réduction des animations.
    /// </summary>
    public bool AnimateValues { get; set; } = true;

    /// <summary>Le glyphe prolonge celui de la forme compacte.</summary>
    public FrameworkElement? AnchorFor(MorphAnchorKind kind)
        => kind is MorphAnchorKind.Icon or MorphAnchorKind.Artwork ? VolumeIcon : null;

    public void Apply(IslandActivity activity)
    {
        if (activity.Payload is HudPayload hud)
        {
            UpdateHud(hud);
        }
    }

    public void UpdateHud(HudPayload hud)
    {
        _maximum = hud.Maximum <= 0 ? 100 : hud.Maximum;
        _muted = string.Equals(hud.ValueText, SpaceNotch.Features.SystemHud.HudActivity.MutedText, StringComparison.Ordinal);

        VolumeLabel.Text = hud.Label;
        VolumeIcon.Key = hud.IconKey;

        _roller.RollTo(_muted ? 0 : Math.Clamp(hud.Value, 0, _maximum), AnimateValues);
    }

    /// <summary>Une image du défilement : la valeur et le fil avancent ensemble.</summary>
    private void ShowValue(double value)
    {
        double ratio = Math.Clamp(value / _maximum, 0, 1);

        VolumeValue.Text = _muted
            ? "—"
            : Math.Round(ratio * 100).ToString("0", System.Globalization.CultureInfo.CurrentCulture);

        LevelScale.ScaleX = ratio;
        MoveFader(ratio);
    }

    private readonly Microsoft.UI.Xaml.Shapes.Rectangle[] _ticks = new Microsoft.UI.Xaml.Shapes.Rectangle[VolumeFader.Ticks];
    private int _litTicks = -1;
    private double _ratio;

    /// <summary>Graduations posées à la largeur du fader : onze traits de 2 × 6 DIPs.</summary>
    private void OnFaderSizeChanged(object sender, SizeChangedEventArgs e)
    {
        FaderTicks.Children.Clear();
        double width = FaderHost.ActualWidth;

        for (int i = 0; i < _ticks.Length; i++)
        {
            _ticks[i] = new Microsoft.UI.Xaml.Shapes.Rectangle { Width = 2, Height = 6, RadiusX = 1, RadiusY = 1 };
            Canvas.SetLeft(_ticks[i], Math.Round(((width - 2) * i / (VolumeFader.Ticks - 1)) * 2) / 2);
            Canvas.SetTop(_ticks[i], 0);
            FaderTicks.Children.Add(_ticks[i]);
        }

        _litTicks = -1;
        MoveFader(_ratio);
    }

    /// <summary>
    /// Place le curseur ; chaque cran franchi s'allume et fait rebondir le
    /// curseur (80 ms), comme un bouton rotatif qui clique.
    /// </summary>
    private void MoveFader(double ratio)
    {
        _ratio = ratio;
        double width = FaderHost.ActualWidth;
        KnobShift.X = Math.Max(0, (width - FaderKnob.Width) * ratio);

        int lit = VolumeFader.LitTicks(ratio);

        if (lit == _litTicks || _ticks[0] is null)
        {
            return;
        }

        bool crossed = _litTicks >= 0;
        _litTicks = lit;

        var on = SpaceNotch_App.UI.ThemeBrushes.Get("NfTextPrimaryBrush");
        var off = SpaceNotch_App.UI.ThemeBrushes.Get("NfStrokeStrongBrush");

        for (int i = 0; i < _ticks.Length; i++)
        {
            _ticks[i].Fill = i < lit ? on : off;
        }

        if (crossed && AnimateValues)
        {
            Bounce();
        }
    }

    private void Bounce()
    {
        try
        {
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(FaderKnob);
            var compositor = visual.Compositor;
            visual.CenterPoint = new System.Numerics.Vector3(5, 5, 0);
            var pop = compositor.CreateVector3KeyFrameAnimation();
            pop.InsertKeyFrame(0f, new System.Numerics.Vector3(1.35f, 1.35f, 1));
            pop.InsertKeyFrame(1f, System.Numerics.Vector3.One);
            pop.Duration = TimeSpan.FromSeconds(VolumeFader.BounceSeconds);
            visual.StartAnimation("Scale", pop);
        }
        catch (Exception)
        {
            // Le cran s'est allumé : le rebond n'est qu'un plus.
        }
    }
}
