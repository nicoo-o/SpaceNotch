namespace SpaceNotch.Core.Features;

/// <summary>
/// Identifiants stables des fonctionnalités.
///
/// Ils sont déclarés dans le cœur, et non dans chaque fonctionnalité, pour deux
/// raisons. La première est qu'un identifiant est une clé de persistance : il
/// finit dans la configuration et dans les diagnostics, donc il ne doit pas
/// dépendre de l'endroit où la fonctionnalité est implémentée. La seconde est
/// qu'il doit exister un seul endroit où la correspondance entre une
/// fonctionnalité et sa préférence d'activation est écrite ; autrement, ajouter
/// une fonctionnalité obligerait à retrouver tous les endroits qui la
/// mentionnent, ce qui est exactement le type de couplage que l'architecture
/// cherche à éviter.
/// </summary>
public static class FeatureKeys
{
    public const string Media = "feature.media";

    public const string VolumeHud = "feature.hud";

    public const string Notifications = "feature.notifications";

    public const string Bluetooth = "feature.bluetooth";

    public const string Pomodoro = "feature.pomodoro";

    public const string Clipboard = "feature.clipboard";

    public const string FileShelf = "feature.fileshelf";
}
