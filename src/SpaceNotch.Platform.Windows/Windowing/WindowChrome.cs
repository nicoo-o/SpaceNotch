using System;
using SpaceNotch.Platform.Windows.Win32;

namespace SpaceNotch.Platform.Windows.Windowing;

/// <summary>
/// Rôle de la surface dans l'architecture de l'Island.
///
/// Voir ADR-006 : le rendu et l'interaction sont portés par deux fenêtres
/// distinctes. C'est la seule voie compatible avec WinUI 3 pour obtenir un
/// overlay qui ne vole aucun clic, <c>SetLayeredWindowAttributes(LWA_COLORKEY)</c>
/// étant inopérant dans une fenêtre WinUI 3 et <c>HTTRANSPARENT</c> ne
/// franchissant pas la frontière entre threads.
/// </summary>
public enum IslandSurfaceRole
{
    /// <summary>
    /// Surface interactive : reçoit hover, clic, molette et drag &amp; drop.
    /// </summary>
    Interactive,

    /// <summary>
    /// Surface décorative : halo, ombre et fondu. Entièrement clic-traversante
    /// grâce à <c>WS_EX_TRANSPARENT</c>.
    /// </summary>
    Atmosphere
}

/// <summary>
/// Applique à une fenêtre Win32 l'ensemble des styles que SpaceNotch exige :
/// absence de cadre, exclusion d'Alt+Tab et de la barre des tâches, absence de
/// vol de focus, et pour la surface décorative, transparence aux clics.
/// </summary>
public static class WindowChrome
{
    /// <summary>
    /// Configure la surface interactive de l'Island.
    /// </summary>
    public static void ApplyInteractiveSurface(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return;
        }

        // TOOLWINDOW : l'Island disparaît d'Alt+Tab et de la barre des tâches.
        // NOACTIVATE  : cliquer sur l'Island ne lui donne jamais le focus, donc
        //               la fenêtre active de l'utilisateur reste active.
        AddExtendedStyles(hWnd, NativeConstants.WS_EX_TOOLWINDOW | NativeConstants.WS_EX_NOACTIVATE);

        DisableDwmRounding(hWnd);
        ForceTopmost(hWnd);
    }

    /// <summary>
    /// Configure l'installeur : une notch comme l'Island, sans cadre ni coins
    /// de Windows et au premier plan — mais une vraie fenêtre, présente dans la
    /// barre des tâches et qui prend le focus, parce qu'on lui répond.
    /// </summary>
    public static void ApplySetupSurface(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return;
        }

        DisableDwmRounding(hWnd);
    }

    /// <summary>
    /// Configure la surface décorative : identique à la surface interactive, plus
    /// <c>WS_EX_TRANSPARENT</c> qui rend chaque clic traversant vers ce qui se
    /// trouve dessous. Le halo et le fondu ne captureront jamais une interaction.
    /// </summary>
    public static void ApplyAtmosphereSurface(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return;
        }

        AddExtendedStyles(
            hWnd,
            NativeConstants.WS_EX_TOOLWINDOW
            | NativeConstants.WS_EX_NOACTIVATE
            | NativeConstants.WS_EX_TRANSPARENT);

        DisableDwmRounding(hWnd);
        ForceTopmost(hWnd);
    }

    /// <summary>
    /// Rend aussi transparentes aux clics les fenêtres enfants de la surface
    /// décorative. WinUI 3 reçoit la souris par des enfants
    /// (<c>DesktopChildSiteBridge</c>, <c>InputSite</c>) créés après la fenêtre
    /// et qui n'héritent pas de <c>WS_EX_TRANSPARENT</c> : sans cela, un clic
    /// à côté de la notch, dans le halo, n'atteignait pas l'application en
    /// dessous (audit SN-02). À rappeler après l'affichage et à chaque
    /// redimensionnement, l'hôte pouvant recréer ses enfants.
    /// </summary>
    public static void MakeChildrenClickThrough(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.EnumChildWindows(hWnd, (child, _) =>
        {
            AddExtendedStyles(child, NativeConstants.WS_EX_TRANSPARENT | NativeConstants.WS_EX_NOACTIVATE);
            return true;
        }, IntPtr.Zero);
    }

    /// <summary>
    /// S'assure que la surface interactive reste au-dessus de la décorative
    /// lorsqu'elles sont toutes deux topmost.
    /// </summary>
    public static void PlaceAbove(IntPtr hWndAbove, IntPtr hWndBelow)
    {
        if (hWndAbove == IntPtr.Zero || hWndBelow == IntPtr.Zero || hWndAbove == hWndBelow)
        {
            return;
        }

        // Positionner la surface interactive en haut de la bande topmost
        // replace mécaniquement la décorative juste en dessous.
        NativeMethods.SetWindowPos(
            hWndAbove,
            NativeConstants.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeConstants.SWP_NOMOVE
            | NativeConstants.SWP_NOSIZE
            | NativeConstants.SWP_NOACTIVATE);
    }

    /// <summary>
    /// Autorise ou retire la prise de focus clavier sur la surface interactive.
    ///
    /// L'Island porte <c>WS_EX_NOACTIVATE</c> : elle ne vole jamais le focus, ce
    /// qui est indispensable pour un overlay. Mais une fenêtre qui ne peut pas
    /// être activée ne reçoit aucun message clavier, et l'Island serait alors
    /// inaccessible au clavier — inacceptable au regard des exigences
    /// d'accessibilité du projet.
    ///
    /// La solution retenue est de rendre le focus possible <em>pendant</em> que
    /// l'utilisateur désigne l'Island au pointeur, et de le retirer aussitôt
    /// qu'il s'en éloigne : le clavier fonctionne quand on interagit, et l'Island
    /// reste sans effet sur la fenêtre active le reste du temps.
    /// </summary>
    public static void SetKeyboardCapture(IntPtr hWnd, bool enabled)
    {
        if (hWnd == IntPtr.Zero)
        {
            return;
        }

        IntPtr current = NativeMethods.GetWindowLongPtr(hWnd, NativeConstants.GWL_EXSTYLE);
        int styles = current.ToInt32();

        int updated = enabled
            ? styles & ~NativeConstants.WS_EX_NOACTIVATE
            : styles | NativeConstants.WS_EX_NOACTIVATE;

        if (updated != styles)
        {
            NativeMethods.SetWindowLongPtr(hWnd, NativeConstants.GWL_EXSTYLE, new IntPtr(updated));
        }

        if (enabled)
        {
            NativeMethods.SetFocus(hWnd);
        }
    }

    /// <summary>Fenêtre actuellement au premier plan.</summary>
    public static IntPtr Foreground() => NativeMethods.GetForegroundWindow();

    /// <summary>
    /// Rend le premier plan à <paramref name="previous"/> si la notch l'a encore :
    /// après Échap, l'utilisateur retrouve sa fenêtre et peut taper sans cliquer
    /// (audit SN-15). Rien si un autre clic a déjà choisi une autre fenêtre.
    /// </summary>
    public static void ReturnForeground(IntPtr self, IntPtr previous)
    {
        if (previous == IntPtr.Zero || previous == self || !NativeMethods.IsWindow(previous))
        {
            return;
        }

        if (NativeMethods.GetForegroundWindow() == self)
        {
            NativeMethods.SetForegroundWindow(previous);
        }
    }

    /// <summary>
    /// Donne le premier plan à la fenêtre, pour qu'elle reçoive la frappe.
    /// Windows ne l'accorde qu'au processus qui vient de recevoir une entrée de
    /// l'utilisateur — ici, le clic qui a ouvert le lanceur.
    /// </summary>
    public static void BringToForeground(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SetForegroundWindow(hWnd);
        NativeMethods.SetFocus(hWnd);
    }

    /// <summary>
    /// Ajoute des bits à GWL_EXSTYLE sans écraser ceux déjà posés par WinUI.
    /// </summary>
    private static void AddExtendedStyles(IntPtr hWnd, int styles)
    {
        IntPtr current = NativeMethods.GetWindowLongPtr(hWnd, NativeConstants.GWL_EXSTYLE);
        int updated = current.ToInt32() | styles;

        if (updated == current.ToInt32())
        {
            return;
        }

        NativeMethods.SetWindowLongPtr(hWnd, NativeConstants.GWL_EXSTYLE, new IntPtr(updated));

        // Un changement de style étendu n'est réellement pris en compte par DWM
        // qu'après un passage de cadre explicite.
        NativeMethods.SetWindowPos(
            hWnd,
            IntPtr.Zero,
            0, 0, 0, 0,
            NativeConstants.SWP_NOMOVE
            | NativeConstants.SWP_NOSIZE
            | NativeConstants.SWP_NOZORDER
            | NativeConstants.SWP_NOACTIVATE
            | NativeConstants.SWP_FRAMECHANGED);
    }

    /// <summary>
    /// Désactive les coins arrondis que Windows 11 applique par défaut : la
    /// géométrie de l'Island est définie par son propre dégradé, pas par DWM.
    /// </summary>
    private static void DisableDwmRounding(IntPtr hWnd)
    {
        StripFrame(hWnd);

        int cornerPreference = NativeConstants.DWMWCP_DONOTROUND;
        NativeMethods.DwmSetWindowAttribute(
            hWnd,
            NativeConstants.DWMWA_WINDOW_CORNER_PREFERENCE,
            ref cornerPreference,
            sizeof(int));

        // Et sans bordure : Windows 11 trace un liseré de 1 px autour de toute
        // fenêtre, même sans cadre ni barre de titre. Sur une fenêtre
        // transparente, il dessinait un rectangle blanc autour de la notch, de
        // son halo, de la bulle et de l'installeur — et il s'allumait à
        // l'activation, d'où un « flash » au clic.
        int noBorder = NativeConstants.DWMWA_COLOR_NONE;
        NativeMethods.DwmSetWindowAttribute(
            hWnd,
            NativeConstants.DWMWA_BORDER_COLOR,
            ref noBorder,
            sizeof(int));
    }

    /// <summary>
    /// Retire tout cadre à la fenêtre : une fenêtre surgissante nue, sans
    /// bordure, sans cadre de dialogue ni bord redimensionnable.
    ///
    /// <para>
    /// <c>SetBorderAndTitleBar(false, false)</c> ne retire pas tout : il reste à
    /// la fenêtre WinUI des bits de cadre, et Windows dessine autour d'elle un
    /// liseré blanc de la taille de la fenêtre — de la notch, de son halo et de
    /// l'installeur. La couleur de bordure DWM « aucune » ne l'efface pas : ce
    /// n'est pas la bordure DWM (captures de la CI, v1.3.0).
    /// </para>
    /// </summary>
    public static void StripFrame(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return;
        }

        const int frame = NativeConstants.WS_CAPTION
            | NativeConstants.WS_BORDER
            | NativeConstants.WS_DLGFRAME
            | NativeConstants.WS_THICKFRAME
            | NativeConstants.WS_SYSMENU
            | NativeConstants.WS_MINIMIZEBOX
            | NativeConstants.WS_MAXIMIZEBOX;

        const int edges = NativeConstants.WS_EX_DLGMODALFRAME
            | NativeConstants.WS_EX_WINDOWEDGE
            | NativeConstants.WS_EX_CLIENTEDGE
            | NativeConstants.WS_EX_STATICEDGE;

        // Le bit de WS_POPUP est le bit de signe : pas de ToInt32, qui lèverait.
        int style = unchecked((int)NativeMethods.GetWindowLongPtr(hWnd, NativeConstants.GWL_STYLE).ToInt64());
        int exStyle = unchecked((int)NativeMethods.GetWindowLongPtr(hWnd, NativeConstants.GWL_EXSTYLE).ToInt64());

        int newStyle = (style & ~frame) | NativeConstants.WS_POPUP;
        int newExStyle = exStyle & ~edges;

        if (newStyle == style && newExStyle == exStyle)
        {
            return;
        }

        NativeMethods.SetWindowLongPtr(hWnd, NativeConstants.GWL_STYLE, new IntPtr(newStyle));
        NativeMethods.SetWindowLongPtr(hWnd, NativeConstants.GWL_EXSTYLE, new IntPtr(newExStyle));

        NativeMethods.SetWindowPos(
            hWnd,
            IntPtr.Zero,
            0, 0, 0, 0,
            NativeConstants.SWP_NOMOVE
            | NativeConstants.SWP_NOSIZE
            | NativeConstants.SWP_NOZORDER
            | NativeConstants.SWP_NOACTIVATE
            | NativeConstants.SWP_FRAMECHANGED);
    }

    /// <summary>
    /// Force le passage au premier plan de la bande topmost, sans activation —
    /// et sans montrer la fenêtre : chacune se montre elle-même, une fois
    /// placée. Montrer ici faisait apparaître la bulle à sa taille par défaut,
    /// invisible mais au premier plan, qui avalait les clics sur le bureau.
    /// </summary>
    private static void ForceTopmost(IntPtr hWnd)
    {
        NativeMethods.SetWindowPos(
            hWnd,
            NativeConstants.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeConstants.SWP_NOMOVE
            | NativeConstants.SWP_NOSIZE
            | NativeConstants.SWP_NOACTIVATE);
    }
}
