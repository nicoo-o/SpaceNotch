namespace NotchFlow.Core.Activities;

/// <summary>
/// Teinte d'ambiance déclarée par une activité, exprimée en composantes rouge,
/// vert et bleu.
///
/// Elle reste volontairement neutre : le cœur ne connaît aucun type de couleur
/// d'interface. Ce qui compte, c'est que la teinte soit <em>déclarée par la
/// fonctionnalité</em> et non devinée par le rendu — l'atmosphère de l'Island est
/// alors une propriété de l'activité présentée, au même titre que sa scène.
/// </summary>
/// <param name="R">Composante rouge.</param>
/// <param name="G">Composante verte.</param>
/// <param name="B">Composante bleue.</param>
public readonly record struct ActivityTint(byte R, byte G, byte B)
{
    /// <summary>
    /// Teinte de référence, utilisée lorsqu'aucune couleur dominante n'a pu être
    /// extraite : un bleu très froid, presque neutre.
    /// </summary>
    public static ActivityTint Default { get; } = new(0x4E, 0x7C, 0xA1);
}
