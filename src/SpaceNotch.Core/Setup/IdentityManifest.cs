using System.Text;

namespace SpaceNotch.Core.Setup;

/// <summary>
/// L'élément <c>&lt;msix&gt;</c> du manifeste de SpaceNotch.exe, qui relie
/// l'exécutable au paquet d'identité (ADR-023), allumé ou éteint dans le
/// fichier lui-même.
///
/// <para>
/// Pourquoi : Windows refuse de lancer <em>tout</em> exécutable qui déclare une
/// identité dont l'enregistrement est cassé (« Nous ne pouvons pas ouvrir cette
/// application… Réparer »). L'installeur, qui est le même fichier, ne pouvait
/// alors même plus démarrer pour réparer. L'installeur et le portable sont donc
/// livrés éteints ; seule la copie installée est rallumée.
/// </para>
///
/// <para>
/// La bascule remplace l'élément par un commentaire XML <b>de même longueur</b>
/// (<c>&lt;msix …&gt;&lt;/msix&gt;</c> ⇄ <c>&lt;!--x …&gt;&lt;/ms--&gt;</c>) :
/// aucun décalage dans l'exécutable, dont l'application .NET est accolée à la
/// suite de la ressource de manifeste.
/// </para>
/// </summary>
public static class IdentityManifest
{
    /// <summary>Début de l'élément allumé, tel que l'écrit l'outil de manifeste.</summary>
    public static ReadOnlySpan<byte> OnStart => "<msix "u8;

    /// <summary>Fin de l'élément allumé.</summary>
    public static ReadOnlySpan<byte> OnEnd => "></msix>"u8;

    /// <summary>Début de l'élément éteint : un commentaire, même longueur.</summary>
    public static ReadOnlySpan<byte> OffStart => "<!--x "u8;

    /// <summary>Fin de l'élément éteint : même longueur que <see cref="OnEnd"/>.</summary>
    public static ReadOnlySpan<byte> OffEnd => "></ms-->"u8;

    /// <summary>Le marqueur qui distingue notre élément de tout autre texte.</summary>
    public static ReadOnlySpan<byte> Namespace => "urn:schemas-microsoft-com:msix.v1"u8;

    /// <summary>Longueur maximale admise pour l'élément, en octets.</summary>
    public const int MaxLength = 512;

    /// <summary>Un élément trouvé : position, longueur, et s'il est allumé.</summary>
    public readonly record struct Element(long Offset, int Length, bool On);

    /// <summary>
    /// Cherche l'élément (allumé ou éteint) dans un morceau de fichier.
    /// <paramref name="baseOffset"/> est la position du morceau dans le fichier.
    /// </summary>
    public static IEnumerable<Element> Find(byte[] buffer, int count, long baseOffset)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var found = new List<Element>();

        Scan(buffer, count, baseOffset, OnStart, OnEnd, on: true, found);
        Scan(buffer, count, baseOffset, OffStart, OffEnd, on: false, found);

        return found.OrderBy(e => e.Offset);
    }

    private static void Scan(byte[] buffer, int count, long baseOffset, ReadOnlySpan<byte> start, ReadOnlySpan<byte> end, bool on, List<Element> found)
    {
        ReadOnlySpan<byte> data = buffer.AsSpan(0, count);
        int from = 0;

        while (from < data.Length)
        {
            int at = data[from..].IndexOf(start);

            if (at < 0)
            {
                return;
            }

            at += from;
            int window = Math.Min(MaxLength, data.Length - at);
            ReadOnlySpan<byte> candidate = data.Slice(at, window);
            int close = candidate.IndexOf(end);

            // Un élément complet, sans « < » intermédiaire, qui porte bien notre espace de noms.
            if (close > 0
                && candidate[start.Length..close].IndexOf((byte)'<') < 0
                && candidate[..close].IndexOf(Namespace) >= 0)
            {
                found.Add(new Element(baseOffset + at, close + end.Length, on));
            }

            from = at + 1;
        }
    }

    /// <summary>
    /// Réécrit l'élément sur place, dans l'état voulu. Même longueur en entrée et
    /// en sortie ; renvoie faux si les octets ne sont pas un de nos éléments.
    /// </summary>
    public static bool Rewrite(Span<byte> element, bool on)
    {
        bool isOn = element.StartsWith(OnStart) && element.EndsWith(OnEnd);
        bool isOff = element.StartsWith(OffStart) && element.EndsWith(OffEnd);

        if (!isOn && !isOff)
        {
            return false;
        }

        if (isOn == on)
        {
            return true;
        }

        (on ? OnStart : OffStart).CopyTo(element);
        (on ? OnEnd : OffEnd).CopyTo(element[^OnEnd.Length..]);
        return true;
    }

    /// <summary>L'élément allumé, tel que l'outil de manifeste l'écrit (pour les tests).</summary>
    public static byte[] Sample(bool on)
    {
        string attributes = "xmlns=\"urn:schemas-microsoft-com:msix.v1\" publisher=\"CN=SpaceNotch\" packageName=\"SpaceNotch.Identity\" applicationId=\"SpaceNotch\"";
        return Encoding.ASCII.GetBytes(on ? $"<msix {attributes}></msix>" : $"<!--x {attributes}></ms-->");
    }
}
