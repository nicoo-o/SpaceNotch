using System;
using System.IO;
using Microsoft.UI.Windowing;
using SpaceNotch.Infrastructure.Logging;

namespace SpaceNotch_App.Windows;

/// <summary>
/// Le logo de SpaceNotch — la grille hypnotique 3×3 — posé sur les fenêtres.
///
/// L'exécutable porte déjà l'icône (<c>ApplicationIcon</c>) : c'est elle que
/// montrent l'Explorateur, la barre des tâches et le menu Démarrer. Les
/// fenêtres, elles, ne la reprennent pas d'elles-mêmes ; la zone de
/// notification et la barre de titre des réglages lisent l'icône de la
/// fenêtre. Voir ADR-022.
/// </summary>
internal static class AppIcon
{
    /// <summary>Chemin de l'icône à côté de l'exécutable (ou dans son dossier d'extraction).</summary>
    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");

    /// <summary>Pose l'icône sur une fenêtre, sans jamais faire échouer son ouverture.</summary>
    public static void ApplyTo(AppWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        try
        {
            if (File.Exists(FilePath))
            {
                window.SetIcon(FilePath);
            }
        }
        catch (Exception ex)
        {
            MiniLogger.Log("[WARN] Icône de fenêtre indisponible", ex);
        }
    }
}
