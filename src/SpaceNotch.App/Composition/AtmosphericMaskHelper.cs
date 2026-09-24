using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace SpaceNotch_App.Composition;

/// <summary>
/// Fabrique du fond atmosphérique de l'Island — <b>voie de repli</b>.
///
/// La règle esthétique du projet tient en une phrase : aucune arête visible, ni
/// en bas, ni sur les côtés. Le corps de l'Island doit se dissoudre au lieu de
/// se terminer sur un bord franc — un arrêt net ferait immédiatement lire
/// l'élément comme une fenêtre posée sur le bureau, ce qui est précisément ce
/// qu'il ne doit pas être.
///
/// <para>
/// Le rendu de référence reste le masque de composition (voir
/// <see cref="AtmosphericSurface"/> et ADR-008). Ce fichier n'intervient que
/// lorsque le compositeur refuse la surface. Il reproduit donc <b>la même
/// géométrie</b> : ellipse ancrée au milieu du bord supérieur, rayon horizontal
/// déduit du début de fondu. Un repli qui dissoudrait autrement ferait voir
/// deux produits différents selon le pilote graphique.
/// </para>
///
/// <para>
/// Le dégradé est radial et non linéaire. Un dégradé vertical ne dissout que le
/// bas et laisse les côtés francs — c'est l'écart que cette version corrige.
/// </para>
/// </summary>
public static class AtmosphericMaskHelper
{
    /// <summary>
    /// Noir OLED pur.
    ///
    /// Ce n'est pas un détail de teinte : sur un écran OLED, ces pixels
    /// s'éteignent complètement, si bien que l'Island cesse d'être un rectangle
    /// posé sur le bureau pour devenir un vide. Un noir à peine remonté (#0B0B0D)
    /// laisse au contraire une surface visible en permanence, et se lit comme un
    /// dallage fatigué plutôt que comme un objet.
    /// </summary>
    private const byte BaseR = 0x00;

    private const byte BaseG = 0x00;

    private const byte BaseB = 0x00;

    /// <summary>Base claire, utilisée lorsque le thème clair est demandé.</summary>
    private const byte LightR = 0xF2;

    private const byte LightG = 0xF3;

    private const byte LightB = 0xF6;

    /// <summary>
    /// Début de fondu par défaut : fraction de rayon encore pleine. Aligné sur
    /// la valeur par défaut du chemin compositeur.
    /// </summary>
    private const double DefaultFadeStart = 0.62;

    /// <summary>
    /// Fond de référence : cœur opaque, dissolution progressive vers le bas et
    /// vers les côtés. Le dernier palier à alpha zéro garantit qu'aucune ligne
    /// ne se dessine, sur aucun bord.
    /// </summary>
    public static Brush CreateAtmosphericGradient(double fadeStartOffset = DefaultFadeStart, bool light = false)
    {
        (byte r, byte g, byte b) = light ? (LightR, LightG, LightB) : (BaseR, BaseG, BaseB);

        var gradient = CreateFalloff(fadeStartOffset, Color.FromArgb(242, r, g, b));

        gradient.GradientStops.Add(new GradientStop
        {
            Color = Color.FromArgb(242, r, g, b),
            Offset = 0.0
        });

        gradient.GradientStops.Add(new GradientStop
        {
            Color = Color.FromArgb(236, r, g, b),
            Offset = Clamp(fadeStartOffset)
        });

        gradient.GradientStops.Add(new GradientStop
        {
            Color = Color.FromArgb(150, r, g, b),
            Offset = Midpoint(fadeStartOffset)
        });

        // Le dernier palier vaut exactement alpha zéro : c'est lui, et lui seul,
        // qui garantit qu'aucune arête ne se dessine au pourtour de l'Island.
        gradient.GradientStops.Add(new GradientStop
        {
            Color = Color.FromArgb(0, r, g, b),
            Offset = 1.0
        });

        return gradient;
    }

    /// <summary>
    /// Surface du corps de l'Island : un aplat, au contour franc.
    ///
    /// <para>
    /// C'est la conséquence la plus visible du changement de langage visuel. Tant
    /// que la dissolution était la référence, le corps portait lui-même un dégradé
    /// dont l'alpha tombait à zéro sur les bords : son contour n'existait donc
    /// nulle part, et l'Island se lisait comme une tache posée sur l'écran plutôt
    /// que comme un objet. Au repos, elle redevient un objet — sa silhouette
    /// <em>est</em> son contour — et la dissolution n'apparaît qu'au déploiement,
    /// portée par la couche décorative. Voir ADR-013.
    /// </para>
    ///
    /// <para>
    /// En thème sombre, l'alpha est total. Garder une part de translucidité
    /// laisserait le bureau affleurer sous la surface, ce qui remonterait le noir
    /// et ferait perdre exactement ce qui vient d'être gagné : une Island qui
    /// s'éteint au lieu de se poser. Le thème clair, lui, garde une part de
    /// transparence — sur une surface claire, c'est ce qui la distingue d'un aplat.
    /// </para>
    /// </summary>
    public static Brush CreateSolidSurface(bool light = false, bool opaque = false)
    {
        (byte r, byte g, byte b) = light ? (LightR, LightG, LightB) : (BaseR, BaseG, BaseB);

        // Le noir pur est opaque par définition : rien ne doit pouvoir le remonter.
        byte alpha = light && !opaque ? (byte)0xF2 : (byte)0xFF;

        return new SolidColorBrush(Color.FromArgb(alpha, r, g, b));
    }

    /// <summary>
    /// Fond de repli du repli, utilisé lorsque le système n'autorise plus la
    /// transparence : la surface reste sombre et pleine, sans dépendre d'aucun
    /// réglage.
    /// </summary>
    public static Brush CreateOpaqueSurface(bool light = false)
    {
        (byte r, byte g, byte b) = light ? (LightR, LightG, LightB) : (BaseR, BaseG, BaseB);

        var gradient = CreateFalloff(0.94, Color.FromArgb(255, r, g, b));

        gradient.GradientStops.Add(new GradientStop
        {
            Color = Color.FromArgb(255, r, g, b),
            Offset = 0.0
        });

        gradient.GradientStops.Add(new GradientStop
        {
            Color = Color.FromArgb(255, r, g, b),
            Offset = 0.94
        });

        // Même en mode opaque, l'ultime palier s'adoucit : l'arête franche est
        // proscrite dans tous les modes. Elle ne disparaît pas complètement ici,
        // parce que sans transparence système il n'y a rien derrière pour
        // accueillir la dissolution.
        gradient.GradientStops.Add(new GradientStop
        {
            Color = Color.FromArgb(60, r, g, b),
            Offset = 1.0
        });

        return gradient;
    }

    /// <summary>
    /// Applique le fond atmosphérique à un panneau. Conservé pour les surfaces
    /// qui ne passent pas par le mode de composition configurable.
    /// </summary>
    public static void ApplyAtmosphericBackground(Panel panel, double fadeStartOffset = DefaultFadeStart)
    {
        if (panel is null)
        {
            return;
        }

        panel.Background = CreateAtmosphericGradient(fadeStartOffset);
    }

    /// <summary>
    /// Ellipse de dissolution, identique à celle du chemin compositeur.
    ///
    /// Le rayon horizontal est déduit du début de fondu plutôt que fixé : il
    /// place les coins supérieurs exactement sur le premier arrêt plein, ce qui
    /// garde le bord haut opaque d'un côté à l'autre — c'est là que l'Island
    /// touche l'écran, et une échancrure claire à cet endroit trahirait
    /// immédiatement la surface.
    /// </summary>
    private static RadialGradientBrush CreateFalloff(double fadeStartOffset, Color fallback)
    {
        double clamped = Clamp(fadeStartOffset);

        return new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            Center = new Point(0.5, 0.0),
            GradientOrigin = new Point(0.5, 0.0),
            RadiusX = System.Math.Clamp(0.5 / clamped, 0.55, 3.0),
            RadiusY = 1.0,

            // RadialGradientBrush dérive de XamlCompositionBrushBase : quand les
            // effets de composition sont indisponibles — bureau à distance, pilote
            // dégradé, rendu logiciel — il ne dessine plus le dégradé mais cette
            // seule couleur. Par défaut elle vaut transparent, ce qui ferait
            // purement et simplement disparaître l'Island. On y met donc le corps
            // opaque : sans atmosphère, mais visible.
            FallbackColor = fallback
        };
    }

    private static double Clamp(double fadeStartOffset)
        => System.Math.Clamp(fadeStartOffset, 0.05, 0.98);

    /// <summary>
    /// Palier intermédiaire, placé aux deux tiers entre le cœur plein et le
    /// bord transparent. Il évite que la dissolution ne se lise comme une rampe
    /// linéaire, ce qui lui donnerait un aspect de dégradé plutôt que
    /// d'atmosphère.
    /// </summary>
    private static double Midpoint(double fadeStartOffset)
    {
        double start = Clamp(fadeStartOffset);
        return start + ((1.0 - start) * 0.66);
    }
}
