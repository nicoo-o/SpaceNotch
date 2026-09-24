using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpaceNotch.Core.Activities;
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
    public VolumeHudScene()
    {
        InitializeComponent();
    }

    public FrameworkElement Root => this;

    public void Apply(IslandActivity activity)
    {
        if (activity.Payload is HudPayload hud)
        {
            UpdateHud(hud);
        }
    }

    public void UpdateHud(HudPayload hud)
    {
        double maximum = hud.Maximum <= 0 ? 100 : hud.Maximum;

        VolumeBar.Maximum = maximum;
        VolumeBar.Value = Math.Clamp(hud.Value, 0, maximum);

        VolumeLabel.Text = hud.Label;
        VolumePercent.Text = hud.ValueText;
        VolumeIcon.Glyph = GlyphFor(hud.IconKey);
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

