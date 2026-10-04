using System;
using SpaceNotch.Platform.Windows.Display;
using SpaceNotch.Platform.Windows.Win32;

namespace SpaceNotch_App.Windows;

/// <summary>
/// La toile de la notch accrochée en haut : une fenêtre qui ne fait que
/// grandir, et une région qui suit la forme.
///
/// <para>
/// La surface XAML suit un redimensionnement de fenêtre avec une ou plusieurs
/// images de retard. Une fenêtre redimensionnée pendant une transition était
/// donc peinte pour l'ancienne taille : forme décalée d'un côté, bords coupés,
/// zones grises encore vides. La toile prend la plus grande taille qu'une
/// transition ait demandée et ne rétrécit plus : passé les premières
/// ouvertures, plus aucune transition ne la redimensionne, et la forme s'anime
/// à l'intérieur, centrée par la mise en page.
/// </para>
///
/// <para>
/// Ce qui suit la forme, c'est la région de la fenêtre (<c>SetWindowRgn</c>) :
/// hors d'elle, la fenêtre ne reçoit pas la souris — les clics traversent vers
/// les applications dessous, comme si la fenêtre avait la taille de la forme.
/// Changer la région ne redimensionne rien, donc ne décale rien.
/// </para>
/// </summary>
public sealed partial class IslandWindow
{
    private int _canvasWidth;
    private int _canvasHeight;
    private (int Left, int Top, int Width, int Height, double Scale) _canvasDisplay;
    private (int X, int Y, int Width, int Height) _hitRect = (int.MinValue, 0, 0, 0);

    /// <summary>
    /// Place la toile pour une forme de <paramref name="frameWidthPx"/> ×
    /// <paramref name="frameHeightPx"/> centrée sur <paramref name="centerX"/>,
    /// collée en <paramref name="top"/>.
    /// </summary>
    private void PlaceOnCanvas(DisplayInfo display, int centerX, int top, int frameWidthPx, int frameHeightPx)
    {
        // Un autre écran, une autre échelle : la toile repart, d'emblée à la taille
        // de la plus grande scène. Partie de la forme du repos, elle grandissait à
        // la première grande ouverture, et la notch était peinte décalée de la
        // moitié de l'agrandissement pendant deux images (NotchCanvas).
        //
        // Sauf en plein mouvement (raccrochage d'une pastille détachée, retour
        // d'une languette) : sauter d'un coup à la grande toile décalerait
        // davantage la forme qui s'anime. Elle part alors de la forme, comme avant.
        var identity = (display.Left, display.Top, display.Width, display.Height, display.DpiScale);

        if (identity != _canvasDisplay)
        {
            _canvasDisplay = identity;
            (_canvasWidth, _canvasHeight) = _controller is { IsAnimating: true }
                ? (0, 0)
                : SpaceNotch.Core.Scenes.NotchCanvas.InitialSize(display.DpiScale, display.Width, display.Height);
        }

        _canvasWidth = Math.Max(_canvasWidth, frameWidthPx);
        _canvasHeight = Math.Max(_canvasHeight, frameHeightPx);

        MoveWindow(centerX - (_canvasWidth / 2), top, _canvasWidth, _canvasHeight, recheck: true);
        SetHitRect((_canvasWidth - frameWidthPx) / 2, 0, frameWidthPx, frameHeightPx);
    }

    /// <summary>
    /// Quitte la toile (notch détachée, languette) : la fenêtre y reprend la
    /// taille exacte de ce qu'elle porte, et la région est retirée.
    /// </summary>
    private void LeaveCanvas()
    {
        if (_canvasWidth == 0 && _hitRect.X == int.MinValue)
        {
            return;
        }

        _canvasWidth = 0;
        _canvasHeight = 0;
        _canvasDisplay = default;
        _hitRect = (int.MinValue, 0, 0, 0);
        NativeMethods.SetWindowRgn(_hWnd, IntPtr.Zero, true);
    }

    private void SetHitRect(int x, int y, int width, int height)
    {
        if (_hitRect == (x, y, width, height))
        {
            return;
        }

        IntPtr region = NativeMethods.CreateRectRgn(x, y, x + width, y + height);

        if (region == IntPtr.Zero)
        {
            return;
        }

        if (NativeMethods.SetWindowRgn(_hWnd, region, true))
        {
            _hitRect = (x, y, width, height);
        }
        else
        {
            NativeMethods.DeleteObject(region);
        }
    }

    /// <summary>
    /// Rectangle d'écran réellement occupé par la notch, en pixels : la forme
    /// sur la toile, ou la fenêtre entière hors de la toile.
    /// </summary>
    private (int X, int Y, int Width, int Height) IslandScreenBounds()
        => _hitRect.X == int.MinValue
            ? (_lastWindowX, _lastWindowY, _lastWindowWidth, _lastWindowHeight)
            : (_lastWindowX + _hitRect.X, _lastWindowY + _hitRect.Y, _hitRect.Width, _hitRect.Height);
}
