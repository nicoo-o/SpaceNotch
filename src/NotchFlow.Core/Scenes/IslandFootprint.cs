namespace NotchFlow.Core.Scenes;

/// <summary>
/// Densité du contenu des cartes : c'est le seul réglage de taille offert à
/// l'utilisateur.
///
/// Une largeur et une hauteur réglables au pixel près semblaient plus souples,
/// mais elles obligeaient à arbitrer des cas absurdes — une carte de 180 de large
/// pour deux lignes de texte, une hauteur de 34 pour un glyphe de 20 — et
/// laissaient l'utilisateur construire lui-même une Islet mal proportionnée.
/// Une densité, elle, déplace les trois dimensions ensemble et garde les
/// proportions du langage visuel.
/// </summary>
public enum IslandContentDensity
{
    /// <summary>Compact : pour un écran très chargé ou une préférence discrète.</summary>
    Compact = 0,

    /// <summary>Confortable : la densité de référence du langage visuel.</summary>
    Comfortable = 1,

    /// <summary>Aéré : plus d'air autour du texte, pour un écran de grande taille.</summary>
    Aired = 2
}

/// <summary>
/// Encombrement d'une forme de l'Island, exprimé en DIPs (pixels indépendants de
/// la densité).
///
/// L'encombrement appartient à la <em>forme</em>, jamais à la fenêtre : c'est ce
/// qui garantit que l'interface n'a pas à décider de sa propre taille. La fenêtre
/// lit cette valeur et convertit en pixels physiques avec l'échelle du moniteur
/// cible.
///
/// <para>
/// <b>Pourquoi des paliers et pas des dimensions réglables.</b> Les trois paliers
/// ci-dessous sont les états d'un même objet, et leurs hauteurs sont déduites du
/// contenu qu'ils portent — jamais choisies : la carte vaut exactement
/// 10 + 14 + 2 + 16 + 10, soit la marge, la ligne de légende, l'écart, la ligne
/// de titre et la marge. Une hauteur réglable au curseur permettrait de séparer
/// ces deux lignes jusqu'à en faire un panneau vide. Voir ADR-012.
/// </para>
/// </summary>
public readonly record struct IslandFootprint(double Width, double Height)
{
    /// <summary>
    /// Veille : un point neutre et sa marge. L'Island dit qu'elle est là, rien de
    /// plus.
    /// </summary>
    public static IslandFootprint Idle => new(34, 28);

    /// <summary>
    /// Signal : un glyphe, un libellé court, sur une ligne.
    /// </summary>
    public static IslandFootprint Signal => new(132, 28);

    /// <summary>
    /// Carte : glyphe, légende et titre. La hauteur est la somme de son contenu.
    /// </summary>
    public static IslandFootprint Card => For(IslandPresentationTier.Card);

    /// <summary>
    /// Encombrement d'un palier, la densité ne gouvernant que la carte.
    ///
    /// Les paliers veille et signal sont inchangés par la densité : ils portent un
    /// point et une ligne, et les étirer ne les rendrait pas plus lisibles, juste
    /// plus présents.
    /// </summary>
    public static IslandFootprint For(
        IslandPresentationTier tier,
        IslandContentDensity density = IslandContentDensity.Comfortable) => tier switch
        {
            IslandPresentationTier.Signal => Signal,
            IslandPresentationTier.Card => density switch
            {
                IslandContentDensity.Compact => new IslandFootprint(216, 44),
                IslandContentDensity.Aired => new IslandFootprint(264, 60),
                _ => new IslandFootprint(240, 52)
            },
            _ => Idle
        };

    /// <summary>
    /// Marge verticale d'une carte, selon la densité.
    ///
    /// <para>
    /// C'est la densité qui déplace l'air, et jamais la taille du texte. Une
    /// carte compacte n'est pas une carte plus petite : c'est la même carte avec
    /// moins de respiration. Réduire la typographie pour tenir dans une hauteur
    /// plus faible produirait un texte illisible sur un écran à forte densité, et
    /// l'Island n'aurait plus la même voix selon le réglage.
    /// </para>
    ///
    /// <para>
    /// La hauteur d'une carte en découle exactement : deux marges, une ligne de
    /// légende de 14, un écart de 2, une ligne de titre de 16. Ces nombres ne sont
    /// pas indépendants les uns des autres — les modifier séparément déformerait
    /// la forme, puisque sa hauteur en est la somme. Voir ADR-012.
    /// </para>
    /// </summary>
    public static double CardVerticalPadding(IslandContentDensity density) => density switch
    {
        IslandContentDensity.Compact => 6,
        IslandContentDensity.Aired => 14,
        _ => 10
    };

    /// <summary>
    /// Forme au repos de référence, lorsque rien n'est présenté. Conservée sous
    /// ce nom parce que c'est le repli documenté du répertoire de scènes : une
    /// clé inconnue ramène l'Island à sa forme la plus discrète plutôt que de la
    /// faire disparaître.
    /// </summary>
    public static IslandFootprint Collapsed => Idle;

    public bool IsValid => Width > 0 && Height > 0;
}
