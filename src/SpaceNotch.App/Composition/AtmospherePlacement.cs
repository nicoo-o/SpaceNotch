namespace SpaceNotch_App.Composition;

/// <summary>
/// Placement de la couche décorative autour du corps de l'Island.
///
/// <para>
/// Les deux unités y figurent côte à côte, et ce n'est pas une redondance : le
/// gestionnaire de fenêtres ne travaille qu'en pixels physiques, tandis que le
/// contenu XAML et le compositeur ne travaillent qu'en DIPs. Un poste à 150 %
/// d'échelle rend l'écart visible, et confondre les deux revient à dessiner une
/// ombre ou un halo d'un tiers trop grands sur un écran qui n'est pas à 100 %.
/// </para>
/// </summary>
/// <param name="X">Abscisse physique du corps de l'Island.</param>
/// <param name="Y">Ordonnée physique du corps de l'Island.</param>
/// <param name="WidthPx">Largeur physique du corps.</param>
/// <param name="HeightPx">Hauteur physique du corps.</param>
/// <param name="WidthDip">Largeur du corps en DIPs.</param>
/// <param name="HeightDip">Hauteur du corps en DIPs.</param>
/// <param name="CornerRadiusDip">Rayon des congés du bas effectivement tracé, en DIPs.</param>
/// <param name="ShoulderDip">Épaule effectivement tracée, en DIPs.</param>
/// <param name="Deployment">
/// Avancement du déploiement, de 0 au repos à 1 complètement ouvert.
///
/// Il est déduit de la géométrie et non d'un état : la forme grandit, et la
/// dissolution naît de cette croissance. Un basculement piloté par la machine
/// d'état se produirait à un instant précis du mouvement, ce qui se verrait comme
/// un clignotement au milieu du morphing.
/// </param>
/// <param name="Floating">
/// Vrai quand la notch a été arrachée au bord (ADR-019). La couche ne garde
/// alors que l'ombre, tout autour de la pastille : la dissolution et le halo
/// naissent du bord de l'écran, et n'ont pas de sens loin de lui.
/// </param>
/// <param name="Edge">
/// Bord auquel la notch est accrochée. Sur un côté, la couche ne garde que
/// l'ombre, prolongée au-delà du bord pour que sa base soit droite.
/// </param>
public readonly record struct AtmospherePlacement(
    int X,
    int Y,
    int WidthPx,
    int HeightPx,
    double WidthDip,
    double HeightDip,
    double CornerRadiusDip,
    double ShoulderDip,
    double Deployment,
    bool Floating = false,
    SpaceNotch.Core.Presentation.NotchEdge Edge = SpaceNotch.Core.Presentation.NotchEdge.Top);
