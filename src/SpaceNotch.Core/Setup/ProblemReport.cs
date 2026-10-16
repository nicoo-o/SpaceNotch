using System;

namespace SpaceNotch.Core.Setup;

/// <summary>
/// Signaler un problème (n° 56) : la page GitHub d'un nouveau rapport, avec le
/// modèle de bogue, la version et Windows déjà remplis. Rien n'est envoyé : la
/// page s'ouvre dans le navigateur, l'utilisateur décrit, relit et publie.
/// </summary>
public static class ProblemReport
{
    /// <summary>Modèle d'issue des bogues (.github/ISSUE_TEMPLATE).</summary>
    public const string Template = "bogue.yml";

    /// <summary>
    /// Adresse d'un nouveau rapport. Seuls la version de SpaceNotch et le nom de
    /// Windows y figurent : ni chemin, ni nom d'utilisateur, ni journal.
    /// </summary>
    public static Uri NewIssue(string version, Version windows)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(windows);

        return new Uri(
            $"{SetupIdentity.HomePage}/issues/new?template={Template}"
            + $"&version={Uri.EscapeDataString(version)}"
            + $"&windows={Uri.EscapeDataString(WindowsName(windows))}");
    }

    /// <summary>
    /// Le nom de Windows tel qu'on le lit : Windows 11 garde le numéro 10.0, et
    /// se reconnaît à sa version de build (22000 et au-delà).
    /// </summary>
    public static string WindowsName(Version windows)
    {
        ArgumentNullException.ThrowIfNull(windows);

        return windows is { Major: 10, Minor: 0, Build: >= 22000 }
            ? $"Windows 11 (build {windows.Build})"
            : windows is { Major: 10, Minor: 0 }
                ? $"Windows 10 (build {windows.Build})"
                : $"Windows {windows.Major}.{windows.Minor} (build {windows.Build})";
    }
}
