using System;

namespace NotchFlow.Core.Animation;

/// <summary>
/// Paramètres d'un ressort, exprimés dans les deux vocabulaires du mouvement.
///
/// <para>
/// Le vocabulaire <em>physique</em> — raideur, amortissement, masse — est celui
/// qui fait tourner le solveur. Le vocabulaire <em>perceptif</em> — temps de
/// réaction et rebond — est celui qui se règle : « 0,43 s avec un rebond de
/// 0,78 » se lit, se compare et se transmet, là où « raideur 213, amortissement
/// 22,8 » ne dit rien à personne. Les deux sont reliés par les identités
/// d'un oscillateur harmonique amorti, donc aucun des deux n'est une
/// approximation de l'autre.
/// </para>
///
/// <para>
/// Le rebond vaut le rapport d'amortissement : 1,0 ne dépasse jamais la cible,
/// en dessous de 1,0 le mouvement la dépasse d'autant plus qu'il s'en éloigne.
/// Un rebond de 0,78 dépasse à peine — c'est l'ouverture au clic ; un rebond de
/// 0,55 dépasse franchement — c'est le survol. Voir ADR-012.
/// </para>
///
/// <para>
/// La masse n'est pas exposée en réglage : elle n'agit qu'en combinaison avec la
/// raideur, si bien qu'un couple (raideur, masse) décrit un seul degré de
/// liberté réel. En la fixant à 1, l'interface n'offre plus deux curseurs qui
/// font la même chose sous deux noms.
/// </para>
/// </summary>
public record SpringParameters(
    double Stiffness = 213.47,
    double Damping = 22.79,
    double Mass = 1.0,
    double InitialVelocity = 0.0)
{
    /// <summary>
    /// Réglage de référence : l'ouverture au clic.
    ///
    /// Le dépassement est **franc** et non discret : environ 10 % de la distance,
    /// ce qui se voit. C'est un choix assumé — l'Island est ancrée au bord
    /// supérieur, donc son seul bord libre est le bas, et un dépassement franc y
    /// est la seule chose qui fasse lire l'objet comme matériel. Une valeur plus
    /// proche de 1,0 donnerait un redimensionnement propre mais inerte.
    /// </summary>
    public static SpringParameters Default => FromResponse(0.46, 0.58);

    /// <summary>
    /// Réglage du survol : le même geste, mais plus élastique. C'est lui qui
    /// porte le rebond à l'écran — l'Island étant ancrée au bord supérieur, son
    /// seul bord libre est le bas, donc l'amortissement s'y lit directement.
    /// </summary>
    public static SpringParameters Hover => FromResponse(0.40, 0.45);

    /// <summary>Réglage souple, avec un rebond franc.</summary>
    public static SpringParameters Bouncy => FromResponse(0.40, 0.45);

    /// <summary>Réglage rapide et ferme, pour les retours au repos.</summary>
    public static SpringParameters Snappy => FromResponse(0.30, 0.85);

    /// <summary>Réglage sans oscillation, utilisé lorsque le mouvement est à éviter.</summary>
    public static SpringParameters Calm => FromResponse(0.50, 1.0);

    /// <summary>Temps mis par le ressort pour s'approcher de sa cible, en secondes.</summary>
    public double ResponseSeconds => 2 * Math.PI / Math.Sqrt(Stiffness / Mass);

    /// <summary>
    /// Rebond, c'est-à-dire le rapport d'amortissement. 1,0 ou plus : aucun
    /// dépassement. En dessous de 1,0 : dépassement d'autant plus marqué que la
    /// valeur est faible.
    /// </summary>
    public double DampingRatio => Damping / (2 * Math.Sqrt(Stiffness * Mass));

    /// <summary>
    /// Construit un ressort depuis le vocabulaire perceptif.
    ///
    /// Les bornes ne sont pas décoratives : un temps de réaction trop court
    /// produit un saut que l'œil lit comme une apparition, et un rebond nul
    /// supprimerait tout dépassement, ce qui ferait perdre à l'Island sa
    /// qualité d'objet.
    /// </summary>
    public static SpringParameters FromResponse(double responseSeconds, double dampingRatio, double mass = 1.0)
    {
        double response = Clamp(responseSeconds, 0.18, 1.20, DefaultResponseSeconds);
        double ratio = Clamp(dampingRatio, 0.05, 1.50, DefaultDampingRatio);
        double inertia = Clamp(mass, 0.2, 4.0, 1.0);

        double angular = 2 * Math.PI / response;
        double stiffness = angular * angular * inertia;
        double damping = 2 * ratio * Math.Sqrt(stiffness * inertia);

        return new SpringParameters(stiffness, damping, inertia, 0.0);
    }

    private const double DefaultResponseSeconds = 0.46;

    private const double DefaultDampingRatio = 0.58;

    /// <summary>
    /// Construit des paramètres depuis les préférences utilisateur en garantissant
    /// que le système reste physiquement résoluble : une raideur nulle ou une
    /// masse négative produiraient un mouvement absurde ou une division par zéro.
    ///
    /// Cette voie reste nécessaire pour relire une configuration écrite par une
    /// version antérieure, qui ne connaissait que le vocabulaire physique.
    /// </summary>
    public static SpringParameters FromSettings(double stiffness, double damping, double mass)
    {
        SpringParameters reference = Default;

        return new SpringParameters(
            Stiffness: Clamp(stiffness, 40, 600, reference.Stiffness),
            Damping: Clamp(damping, 4, 80, reference.Damping),
            Mass: Clamp(mass, 0.2, 4, reference.Mass),
            InitialVelocity: 0);
    }

    private static double Clamp(double value, double min, double max, double fallback)
        => double.IsNaN(value) || double.IsInfinity(value)
            ? fallback
            : Math.Clamp(value, min, max);
}
