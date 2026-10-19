using SpaceNotch.Core.Localization;

namespace SpaceNotch.Core.Features;

/// <summary>Pourquoi une fonction qui dépend du système peut ne rien faire.</summary>
public enum IntegrationStatus
{
    Ready,
    PermissionRequired,
    Unsupported,
    Disabled,
    Error
}

/// <summary>Ce que la fonction a constaté en interrogeant le système.</summary>
public enum IntegrationProbe
{
    /// <summary>Rien de constaté : on suppose que ça marche.</summary>
    Unknown,
    Ok,
    Denied,
    Missing
}

/// <summary>
/// États des intégrations (n° 2, P07 + P17). Une option qui ne fait rien sans
/// le dire passe pour un défaut : on nomme l'état final et l'action possible,
/// sans rien envoyer nulle part.
/// </summary>
public static class IntegrationStatuses
{
    /// <summary>Le cycle de vie prime ; le constat du système ne parle que d'une fonction en marche.</summary>
    public static IntegrationStatus Resolve(FeatureState state, IntegrationProbe probe)
        => state switch
        {
            FeatureState.Faulted => IntegrationStatus.Error,
            FeatureState.Running => probe switch
            {
                IntegrationProbe.Denied => IntegrationStatus.PermissionRequired,
                IntegrationProbe.Missing => IntegrationStatus.Unsupported,
                _ => IntegrationStatus.Ready
            },
            _ => IntegrationStatus.Disabled
        };

    /// <summary>L'état d'une fonctionnalité telle qu'elle est maintenant.</summary>
    public static IntegrationStatus For(IIslandFeature feature)
        => Resolve(feature.State, feature is IIntegrationStatusSource source ? source.Probe : IntegrationProbe.Unknown);

    /// <summary>Vrai quand l'utilisateur doit être prévenu : la fonction est active mais ne peut rien faire.</summary>
    public static bool NeedsAttention(IntegrationStatus status)
        => status is IntegrationStatus.PermissionRequired or IntegrationStatus.Unsupported or IntegrationStatus.Error;

    public static string Label(IntegrationStatus status) => status switch
    {
        IntegrationStatus.Ready => Lang.T("Prête", "Ready"),
        IntegrationStatus.PermissionRequired => Lang.T("Permission requise", "Permission required"),
        IntegrationStatus.Unsupported => Lang.T("Non prise en charge ici", "Not supported here"),
        IntegrationStatus.Error => Lang.T("En erreur", "Error"),
        _ => Lang.T("Désactivée", "Off")
    };

    /// <summary>L'action réaliste, ou rien quand il n'y a rien à faire.</summary>
    public static string Hint(IntegrationStatus status) => status switch
    {
        IntegrationStatus.PermissionRequired => Lang.T("Autorise l'accès dans Paramètres Windows.", "Allow access in Windows Settings."),
        IntegrationStatus.Unsupported => Lang.T("Cet appareil ou ce compte ne la fournit pas : rien à régler.", "This device or account doesn't provide it: nothing to set up."),
        IntegrationStatus.Error => Lang.T("Réessaie ; le détail est dans le journal.", "Try again; details are in the log."),
        IntegrationStatus.Disabled => Lang.T("Active-la pour l'utiliser.", "Turn it on to use it."),
        _ => string.Empty
    };

    /// <summary>« Libellé : action », pour une ligne ou une bulle.</summary>
    public static string Describe(IntegrationStatus status)
        => Hint(status) is { Length: > 0 } hint ? $"{Label(status)} — {hint}" : Label(status);
}

/// <summary>Une fonctionnalité qui sait dire où elle en est face au système.</summary>
public interface IIntegrationStatusSource
{
    IntegrationProbe Probe { get; }
}
