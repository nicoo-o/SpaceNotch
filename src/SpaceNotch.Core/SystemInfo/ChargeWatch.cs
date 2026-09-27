namespace SpaceNotch.Core.SystemInfo;

/// <summary>
/// Charge de la batterie (F1) : décide quand la notch annonce le branchement.
/// Seul le passage « débranché → branché » s'annonce ; la première lecture,
/// au démarrage, ne fait que prendre l'état — un portable déjà branché au
/// lancement ne déclenche rien. Un ordinateur sans batterie n'annonce jamais.
/// </summary>
public sealed class ChargeWatch
{
    private bool? _plugged;

    /// <summary>Dernier niveau connu, en pourcentage.</summary>
    public int Percent { get; private set; }

    /// <summary>
    /// Nouvelle lecture. Vrai quand le chargeur vient d'être branché.
    /// </summary>
    public bool Update(bool hasBattery, bool plugged, int percent)
    {
        Percent = Math.Clamp(percent, 0, 100);

        if (!hasBattery)
        {
            _plugged = null;
            return false;
        }

        bool announce = _plugged == false && plugged;
        _plugged = plugged;
        return announce;
    }
}
