using Windows.UI.Composition;
using WinUIEx;

namespace SpaceNotch_App.Composition;

/// <summary>
/// Fond de l'Island qui floute ce qui se trouve derrière elle.
///
/// Ce mode passe par le même chemin que le fond transparent — le pinceau de fond
/// du compositeur — plutôt que par Desktop Acrylic. La différence est
/// déterminante : Acrylic est désactivé par Windows en mode économie d'énergie,
/// alors que ce pinceau reste sous notre contrôle.
/// </summary>
public sealed class BlurredBackdrop : CompositionBrushBackdrop
{
    protected override CompositionBrush CreateBrush(Compositor compositor)
        => compositor.CreateHostBackdropBrush();
}
