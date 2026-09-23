using System;

namespace NotchFlow.Platform.Windows.Display;

/// <summary>
/// Informations complètes sur un moniteur : bornes physiques, zone de travail et
/// DPI réel. Les coordonnées sont exprimées dans l'espace du bureau virtuel, qui
/// peut contenir des moniteurs à gauche (coordonnées négatives).
/// </summary>
public record DisplayInfo(
    IntPtr Handle,
    int Left,
    int Top,
    int Right,
    int Bottom,
    int WorkLeft,
    int WorkTop,
    int WorkRight,
    int WorkBottom,
    bool IsPrimary,
    uint Dpi)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public int WorkWidth => WorkRight - WorkLeft;

    public int WorkHeight => WorkBottom - WorkTop;

    /// <summary>Facteur d'échelle du moniteur (96 → 1.0, 144 → 1.5).</summary>
    public double DpiScale => MonitorDpi.ToScale(Dpi);

    /// <summary>Centre horizontal du moniteur en pixels physiques.</summary>
    public int CenterX => Left + (Width / 2);

    /// <summary>
    /// Indique si un rectangle physique donné est intégralement contenu dans ce
    /// moniteur.
    /// </summary>
    public bool Contains(int x, int y, int width, int height)
        => x >= Left && y >= Top && x + width <= Right && y + height <= Bottom;
}
