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
        _muted = string.Equals(hud.ValueText, "Muet", StringComparison.Ordinal);

        VolumeLabel.Text = hud.Label;
        VolumeIcon.Glyph = GlyphFor(hud.IconKey);

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
    }

    /// <summary>
    /// Traduction des clés d'icône logiques en glyphes.
    ///
    /// Le repli est le volume, et non l'information générique : un retour système
    /// dont la clé serait inconnue reste un retour système, et afficher un « i »
    /// à la place d'une icône de son donnerait à penser que la valeur affichée est
    /// une explication.
    /// </summary>
    private static string GlyphFor(string? iconKey) => GlyphCatalog.Resolve(iconKey, "Volume");
}

