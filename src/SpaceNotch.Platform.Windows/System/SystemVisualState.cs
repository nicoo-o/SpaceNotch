using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SpaceNotch.Platform.Windows.Win32;

namespace SpaceNotch.Platform.Windows.System;

/// <summary>
/// État visuel du système, tel que demandé par l'utilisateur dans Windows.
///
/// Deux réglages conditionnent directement le rendu de l'Island :
/// — « Show animations in Windows » doit désactiver le ressort au profit d'un
///   fondu simple (accessibilité, §38 du cahier des charges) ;
/// — « Transparency effects » conditionne la capacité de la fenêtre à laisser
///   voir le bureau au travers, ce qui impose un mode de fond de repli.
/// </summary>
public sealed record SystemVisualState(
    bool AnimationsEnabled,
    bool UiEffectsEnabled,
    bool HighContrast,
    bool TransparencyEffectsEnabled)
{
    /// <summary>
    /// État correspondant à « tout est autorisé », utilisé comme repli si la
    /// lecture échoue.
    /// </summary>
    public static SystemVisualState Permissive { get; } = new(
        AnimationsEnabled: true,
        UiEffectsEnabled: true,
        HighContrast: false,
        TransparencyEffectsEnabled: true);

    /// <summary>
    /// Le ressort n'est utilisé que si les animations sont autorisées et que le
    /// contraste élevé n'est pas actif.
    /// </summary>
    public bool UseSpringAnimations => AnimationsEnabled && !HighContrast;

    /// <summary>
    /// Lit l'état courant. Aucune scrutation : à appeler au démarrage puis à
    /// chaque <c>WM_SETTINGCHANGE</c> / <c>WM_THEMECHANGED</c>.
    /// </summary>
    public static SystemVisualState Read()
    {
        return new SystemVisualState(
            AnimationsEnabled: ReadBool(NativeConstants.SPI_GETCLIENTAREAANIMATION, defaultValue: true),
            UiEffectsEnabled: ReadBool(NativeConstants.SPI_GETUIEFFECTS, defaultValue: true),
            HighContrast: ReadHighContrast(),
            TransparencyEffectsEnabled: ReadTransparencyEffects());
    }

    private static bool ReadBool(uint spiAction, bool defaultValue)
    {
        try
        {
            uint value = 0;
            if (!NativeMethods.SystemParametersInfoUInt(spiAction, 0, ref value, 0))
            {
                return defaultValue;
            }

            return value != 0;
        }
        catch (EntryPointNotFoundException)
        {
            return defaultValue;
        }
        catch (DllNotFoundException)
        {
            return defaultValue;
        }
    }

    private static bool ReadHighContrast()
    {
        try
        {
            var hc = new NativeMethods.HIGHCONTRAST
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.HIGHCONTRAST>()
            };

            if (!NativeMethods.SystemParametersInfoHighContrast(NativeConstants.SPI_GETHIGHCONTRAST, hc.cbSize, ref hc, 0))
            {
                return false;
            }

            return (hc.dwFlags & NativeConstants.HCF_HIGHCONTRASTON) != 0;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// « Effets de transparence » n'a pas d'API Win32 dédiée : le réglage vit
    /// dans le registre du profil utilisateur. Absence de valeur = activé, ce qui
    /// correspond au comportement par défaut de Windows 11.
    /// </summary>
    private static bool ReadTransparencyEffects()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

            object? value = key?.GetValue("EnableTransparency");
            return value is not int intValue || intValue != 0;
        }
        catch (Exception)
        {
            // Clé absente ou accès refusé : on ne bloque jamais l'affichage pour ça.
            return true;
        }
    }
}
