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
    /// Veille — l'état <c>Hidden</c> du plan : une lèvre descendue du bord de
    /// l'écran, presque imperceptible. L'Island dit qu'elle est là, rien de plus.
    ///
    /// Elle reste une cible facile malgré sa taille : le pointeur bute contre le
    /// bord supérieur de l'écran, qui se comporte comme une cible de hauteur
    /// infinie (loi de Fitts). La lèvre n'a donc pas besoin d'être grande pour
    /// être trouvée — seulement d'être au bord.
    /// </summary>
    public static IslandFootprint Idle => new(80, 18);

    /// <summary>
    /// Signal — l'état <c>Compact</c> du plan : un glyphe, un libellé court, sur
    /// une ligne. 34 de haut, dans la fourchette 32–40 : assez pour porter un
    /// congé généreux sans que le texte touche la courbe.
    ///
    /// La largeur comprend les deux épaules : c'est la forme entière, du bord de
    /// l'écran au bord de l'écran.
    /// </summary>
    public static IslandFootprint Signal => new(148, 34);

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
                IslandContentDensity.Compact => new IslandFootprint(232, 44),
                IslandContentDensity.Aired => new IslandFootprint(280, 60),
                _ => new IslandFootprint(256, 52)
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

    /// <summary>
    /// Encombrement de l'aperçu au survol — l'état <c>Preview</c> du plan.
    ///
    /// <para>
    /// Le survol n'ouvre pas : il fait descendre et élargir légèrement la notch,
    /// d'environ 10 à 20 %. C'est le clic qui exprime l'intention d'ouvrir. Une
    /// Island qui sauterait à pleine taille à chaque passage du pointeur en haut
    /// de l'écran se lirait comme une interface nerveuse.
    /// </para>
    ///
    /// <para>
    /// Une seule exception de contenu : depuis le palier signal, l'aperçu porte
    /// la seconde ligne — c'est l'information qui donne envie de cliquer. Il la
    /// porte dans une largeur à peine accrue, en tronquant plutôt qu'en
    /// s'étalant.
    /// </para>
    ///
    /// <para>La règle de monotonie tient : l'aperçu n'est jamais plus petit que
    /// la forme qu'il prolonge, dans aucune dimension.</para>
    /// </summary>
    public static IslandFootprint PreviewOf(
        IslandPresentationTier tier,
        IslandContentDensity density = IslandContentDensity.Comfortable)
    {
        IslandFootprint rest = For(tier, density);

        return tier switch
        {
            IslandPresentationTier.Signal => new IslandFootprint(Math.Round(rest.Width * 1.2), 48),
            IslandPresentationTier.Card => new IslandFootprint(Math.Round(rest.Width * 1.08), rest.Height + 6),
            _ => new IslandFootprint(Math.Round(rest.Width * 1.2), rest.Height + 6)
        };
    }
}
