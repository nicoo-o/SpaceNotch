using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace NotchFlow_App.Controls;

public sealed partial class AmbientGlowControl : UserControl
{
    public AmbientGlowControl()
    {
        InitializeComponent();
    }

    public void SetGlowColor(Color color)
    {
        GlowColorStop.Color = color;
    }

    public void SetGlowSize(double width, double height)
    {
        GlowEllipse.Width = width;
        GlowEllipse.Height = height;
    }

    public void SetGlowOpacity(double opacity)
    {
        GlowEllipse.Opacity = opacity;
    }

    /// <summary>
    /// Décale la lueur verticalement, afin qu'elle se dissolve sous l'Island
    /// plutôt que de la traverser.
    /// </summary>
    public void SetGlowOffset(double topOffset)
    {
        GlowEllipse.Margin = new Thickness(0, topOffset, 0, 0);
    }

    /// <summary>
    /// Intensité de la teinte. Une valeur faible est indispensable : le halo
    /// doit suggérer la couleur du contenu, jamais l'imposer.
    /// </summary>
    public void SetGlowIntensity(double opacity, double tintOpacity)
    {
        GlowEllipse.Opacity = opacity;

        Color existing = GlowColorStop.Color;
        GlowColorStop.Color = Color.FromArgb(
            (byte)Math.Clamp(tintOpacity * 255.0, 0, 255),
            existing.R,
            existing.G,
            existing.B);
    }
}
