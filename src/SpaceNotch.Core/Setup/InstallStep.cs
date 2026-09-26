namespace SpaceNotch.Core.Setup;

/// <summary>Les étapes visibles d'une installation, dans l'ordre.</summary>
public enum InstallStep
{
    /// <summary>Préparation du dossier.</summary>
    Preparing = 0,

    /// <summary>Fermeture de la notch en cours d'exécution.</summary>
    Stopping,

    /// <summary>Copie de l'exécutable.</summary>
    Copying,

    /// <summary>Raccourcis du menu Démarrer et du bureau.</summary>
    Shortcuts,

    /// <summary>Entrée de Paramètres › Applications et lancement au démarrage.</summary>
    Registering,

    /// <summary>Terminé.</summary>
    Done
}

/// <summary>Où en est l'installation.</summary>
/// <param name="Step">Étape en cours.</param>
/// <param name="Within">Avancement dans l'étape, de 0 à 1.</param>
/// <param name="Indeterminate">
/// Vrai quand l'avancement n'est pas connu : le travail se fait dans le
/// processus élevé, qui ne rend compte qu'à la fin.
/// </param>
public readonly record struct SetupProgress(InstallStep Step, double Within = 0, bool Indeterminate = false)
{
    /// <summary>Part accomplie de toute l'installation, de 0 à 1.</summary>
    public double Overall => InstallProgress.At(Step, Within);
}

/// <summary>Avancement affiché pour chaque étape : la copie, la plus longue, pèse le plus.</summary>
public static class InstallProgress
{
    /// <summary>Part accomplie au début d'une étape, de 0 à 1.</summary>
    public static double At(InstallStep step) => step switch
    {
        InstallStep.Preparing => 0.04,
        InstallStep.Stopping => 0.10,
        InstallStep.Copying => 0.18,
        InstallStep.Shortcuts => 0.78,
        InstallStep.Registering => 0.88,
        _ => 1.0
    };

    /// <summary>Part accomplie à un point d'une étape : interpolée jusqu'au début de la suivante.</summary>
    public static double At(InstallStep step, double within)
    {
        double start = At(step);
        double end = step == InstallStep.Done ? 1.0 : At(step + 1);

        return start + ((end - start) * Math.Clamp(within, 0, 1));
    }
}
