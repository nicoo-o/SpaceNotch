using System;

namespace SpaceNotch.Platform.Windows.Display;

/// <summary>
/// Utilitaire pour la conversion stricte entre DIPs (Device-Independent Pixels) et pixels physiques.
/// </summary>
public static class DpiHelper
{
    public static int ToPhysicalPixels(double dips, double scale)
    {
        if (scale <= 0)
        {
            scale = 1.0;
        }
        return (int)Math.Round(dips * scale);
    }

    public static double ToDips(int physicalPixels, double scale)
    {
        if (scale <= 0)
        {
            scale = 1.0;
        }
        return physicalPixels / scale;
    }
}
