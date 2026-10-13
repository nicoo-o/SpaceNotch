using System;
using SpaceNotch.Core.Motion;

namespace SpaceNotch.Infrastructure.Config;

/// <summary>
/// Ce que la notch a déjà appliqué des réglages (n° 92). <see cref="SettingsService.Update"/>
/// modifie ses réglages sur place et notifie avec le même objet : comparer
/// l'objet reçu à celui qu'on garde, c'était le comparer à lui-même, et un
/// raccourci ou un écran changé dans les Réglages n'était appliqué qu'au
/// redémarrage. Une photo des valeurs dit, elle, ce qui a changé.
/// </summary>
public readonly record struct AppliedSettings(
    IslandDisplayMode DisplayMode,
    long? CustomDisplayHandle,
    string? DockDisplayBounds,
    string LauncherHotkey,
    PixelIntensity PixelIntensity)
{
    /// <summary>Les valeurs de <paramref name="settings"/>, à cet instant.</summary>
    public static AppliedSettings Of(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new AppliedSettings(settings.DisplayMode, settings.CustomDisplayHandle, settings.DockDisplayBounds, settings.LauncherHotkey, settings.PixelIntensity);
    }

    /// <summary>
    /// L'écran de la notch a changé : mode, écran choisi, ou écran d'accroche
    /// (le mode « Custom » se résout d'abord par <see cref="DockDisplayBounds"/>).
    /// </summary>
    public bool DisplayDiffers(AppliedSettings other)
        => DisplayMode != other.DisplayMode
            || CustomDisplayHandle != other.CustomDisplayHandle
            || !string.Equals(DockDisplayBounds, other.DockDisplayBounds, StringComparison.Ordinal);

    /// <summary>Le raccourci de la recherche a changé.</summary>
    public bool HotkeyDiffers(AppliedSettings other)
        => !string.Equals(LauncherHotkey, other.LauncherHotkey, StringComparison.Ordinal);
}
