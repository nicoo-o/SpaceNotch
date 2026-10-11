namespace SpaceNotch_App.Views;

/// <summary>
/// Une vue dont des boucles tournent sans fin tant qu'elle est montrée :
/// l'icône d'un appel qui vibre, les illustrations de la présentation,
/// l'égaliseur et les paroles du média, la toupie de la bulle.
///
/// <para>
/// Notch retirée (plein écran, verrouillage), elles tournaient encore sur le
/// compositeur ou le fil d'interface (relecture de #42, n° 49). La fenêtre les
/// arrête au retrait et les relance au retour (<c>IslandWindow.SuspendLife</c>).
/// </para>
/// </summary>
internal interface ILoopingView
{
    /// <summary>Faux : les boucles s'arrêtent ; vrai : elles reprennent là où la vue en est.</summary>
    void SetLoopsShown(bool shown);
}
