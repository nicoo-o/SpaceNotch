using System;
using SpaceNotch.Platform.Windows.Win32;

namespace SpaceNotch.Platform.Windows.Windowing;

/// <summary>
/// Nature d'un changement signalé par le système.
/// </summary>
[Flags]
public enum ScreenChangeKind
{
    None = 0,

    /// <summary>Résolution, orientation ou jeu de moniteurs modifié.</summary>
    DisplayChanged = 1,

    /// <summary>Le DPI du moniteur de la fenêtre a changé.</summary>
    DpiChanged = 2,

    /// <summary>Zone de travail modifiée (barre des tâches, dock).</summary>
    WorkAreaChanged = 4,

    /// <summary>
    /// Effets visuels, contraste élevé ou transparence modifiés : l'Island doit
    /// réévaluer son mode de fond et ses animations.
    /// </summary>
    VisualStateChanged = 8,

    /// <summary>Toute autre modification des paramètres système.</summary>
    SettingsChanged = 16
}

/// <summary>
/// Interprète les messages de fenêtre qui affectent la géométrie ou l'apparence
/// de l'Island.
///
/// Volontairement passif : la classe ne s'abonne à rien et ne possède aucun
/// minuteur. L'hôte lui transmet les messages qu'il reçoit, ce qui la rend
/// testable sans fenêtre et sans boucle d'attente — et garantit qu'aucun travail
/// n'est effectué en l'absence d'événement.
/// </summary>
public sealed class ScreenChangeWatcher : IDisposable
{
    private bool _disposed;

    /// <summary>
    /// Déclenché lorsqu'un message reçu implique une réévaluation du placement.
    /// </summary>
    public event EventHandler<ScreenChangeKind>? Changed;

    /// <summary>
    /// Analyse un message et publie le changement correspondant.
    /// </summary>
    /// <returns>
    /// <c>true</c> si le message a été reconnu et a produit un changement, ce qui
    /// permet à l'appelant de décider s'il doit coalescer.
    /// </returns>
    public bool HandleMessage(uint messageId, nuint wParam)
    {
        if (_disposed)
        {
            return false;
        }

        ScreenChangeKind kind = Classify(messageId, wParam);

        if (kind == ScreenChangeKind.None)
        {
            return false;
        }

        Changed?.Invoke(this, kind);
        return true;
    }

    /// <summary>
    /// Classification pure, exposée pour être testée directement.
    /// </summary>
    public static ScreenChangeKind Classify(uint messageId, nuint wParam)
    {
        switch (messageId)
        {
            case NativeConstants.WM_DISPLAYCHANGE:
                return ScreenChangeKind.DisplayChanged;

            case NativeConstants.WM_DPICHANGED:
                return ScreenChangeKind.DpiChanged;

            case NativeConstants.WM_DWMCOMPOSITIONCHANGED:
            case NativeConstants.WM_THEMECHANGED:
                return ScreenChangeKind.VisualStateChanged;

            case NativeConstants.WM_SETTINGCHANGE:
                return ClassifySettingChange((uint)wParam);

            default:
                return ScreenChangeKind.None;
        }
    }

    private static ScreenChangeKind ClassifySettingChange(uint spiAction) => spiAction switch
    {
        NativeConstants.SPI_SETWORKAREA => ScreenChangeKind.WorkAreaChanged,

        // Ces actions signalent que l'utilisateur a touché aux effets visuels :
        // animations, transparence ou contraste élevé.
        NativeConstants.SPI_SETUIEFFECTS => ScreenChangeKind.VisualStateChanged,
        NativeConstants.SPI_SETHIGHCONTRAST => ScreenChangeKind.VisualStateChanged,

        // Un WM_SETTINGCHANGE avec wParam à 0 est diffusé à tous les niveaux
        // supérieurs et peut porter n'importe quel changement.
        0 => ScreenChangeKind.SettingsChanged,

        _ => ScreenChangeKind.None
    };

    public void Dispose()
    {
        _disposed = true;
        Changed = null;
    }
}
