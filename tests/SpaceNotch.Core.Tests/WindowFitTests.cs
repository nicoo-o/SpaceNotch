using SpaceNotch.Core.Presentation;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>n° 29 : les Réglages tiennent dans la zone de travail d'un petit écran.</summary>
public sealed class WindowFitTests
{
    [Fact]
    public void A_window_that_fits_keeps_its_size()
        // 920 × 660 DIP à 100 % sur un écran 1920 × 1080 (barre des tâches de 48 px).
        => Assert.Equal((920, 660), WindowFit.Fit(920, 660, 1920, 1032, 16));

    [Fact]
    public void On_a_1366_by_768_screen_at_125_percent_the_window_shrinks_to_the_work_area()
    {
        // 920 × 660 DIP à 125 % = 1150 × 825 px ; zone de travail 1366 × 708 px.
        (int width, int height) = WindowFit.Fit(1150, 825, 1366, 708, 20);

        Assert.Equal(1150, width);
        Assert.Equal(668, height);
    }

    [Fact]
    public void A_screen_narrower_than_the_window_also_bounds_its_width()
        => Assert.Equal((1326, 668), WindowFit.Fit(1380, 990, 1366, 708, 20));

    [Fact]
    public void A_work_area_smaller_than_the_margins_is_filled()
        => Assert.Equal((30, 30), WindowFit.Fit(920, 660, 30, 30, 20));

    [Fact]
    public void An_unknown_work_area_leaves_the_size_unchanged()
        => Assert.Equal((920, 660), WindowFit.Fit(920, 660, 0, 0, 16));
}
