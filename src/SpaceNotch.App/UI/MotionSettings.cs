using System;

namespace SpaceNotch_App.UI;

/// <summary>
/// « Animer l'interface » de Windows, lu une fois puis gardé. Sous réduction
/// des animations, les contrôles ne jouent ni ressort ni échelle : de simples
/// fondus, ou rien.
/// </summary>
public static class MotionSettings
{
    private static bool? _animationsEnabled;

    public static bool AnimationsEnabled
    {
        get
        {
            if (_animationsEnabled is bool known)
            {
                return known;
            }

            try
            {
                _animationsEnabled = new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
            }
            catch (Exception)
            {
                _animationsEnabled = true;
            }

            return _animationsEnabled.Value;
        }
    }

    /// <summary>Relu au prochain accès : la notch l'appelle en rafraîchissant son environnement.</summary>
    public static void Invalidate() => _animationsEnabled = null;
}
