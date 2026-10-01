using System;
using System.Globalization;
using SpaceNotch.Core.Launcher;
using SpaceNotch.Core.Sound;
using SpaceNotch.Infrastructure.Logging;
using SpaceNotch.Platform.Windows.Audio;
using SpaceNotch.Platform.Windows.Notifications;

namespace SpaceNotch_App.Windows;

/// <summary>
/// Commandes tapées (F4) et sons discrets (D3). Une commande validée dans la
/// recherche touche une autre fonctionnalité : le minuteur, le volume, le
/// presse-papier — la couleur copiée s'affiche ensuite d'elle-même (F5).
/// </summary>
public sealed partial class IslandWindow
{
    private void RunCommand(LauncherCommandKind kind, string value)
    {
        try
        {
            if (RunAssistantCommand(kind, value))
            {
                return;
            }

            switch (kind)
            {
                case LauncherCommandKind.Timer when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds):
                    _timerFeature.StartCountdown(TimeSpan.FromSeconds(seconds));
                    break;

                case LauncherCommandKind.Volume when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int level):
                    _volumeListener.SetLevel(Math.Clamp(level, 0, 100) / 100f);
                    break;

                case LauncherCommandKind.Color:
                    var package = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
                    package.SetText("#" + value);
                    global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
                    break;

                case LauncherCommandKind.Capture:
                    StartTextCapture();
                    break;
            }
        }
        catch (Exception ex)
        {
            MiniLogger.Log($"[CMD] {kind} {value} impossible", ex);
        }
    }

    /// <summary>
    /// Un son discret, si l'utilisateur les a activés et que Windows n'est pas
    /// en « Ne pas déranger ».
    /// </summary>
    private void PlayCue(SoundCueKind kind)
    {
        if (!_settings.PlaySounds || FocusAssistProbe.IsQuiet())
        {
            return;
        }

        CuePlayer.Play(kind);
    }
}
