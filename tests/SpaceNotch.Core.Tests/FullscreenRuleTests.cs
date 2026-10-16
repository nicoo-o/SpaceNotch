using SpaceNotch.Platform.Windows.Shell;
using SpaceNotch.Platform.Windows.Win32;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// n° 11 : une surcouche « toujours devant » que les clics traversent fait dire
/// « occupé » au shell ; elle n'est pas un plein écran, la notch reste.
/// </summary>
public sealed class FullscreenRuleTests
{
    private static readonly PixelRect Monitor = new(0, 0, 2560, 1600);
    private static readonly PixelRect Exact = new(0, 0, 2560, 1600);

    private static CoveringWindow Classify(
        PixelRect window,
        uint style = 0,
        bool minimized = false,
        bool maximized = false,
        bool cloaked = false,
        bool shell = false,
        bool inFront = true)
        => FullscreenRule.Classify(window, Monitor, style, minimized, maximized, cloaked, shell, inFront);

    [Fact]
    public void A_window_covering_the_whole_monitor_in_front_is_a_fullscreen()
        => Assert.Equal(CoveringWindow.Fullscreen, Classify(Exact));

    [Theory]
    [InlineData(NativeConstants.WS_EX_TRANSPARENT | NativeConstants.WS_EX_LAYERED)]
    [InlineData(NativeConstants.WS_EX_NOACTIVATE | NativeConstants.WS_EX_LAYERED | NativeConstants.WS_EX_TOPMOST)]
    public void An_overlay_is_seen_through(int style)
        => Assert.Equal(CoveringWindow.Ignore, Classify(Exact, style: (uint)style));

    [Fact]
    public void A_maximized_window_over_an_auto_hidden_taskbar_hides_what_is_below_but_is_not_a_fullscreen()
    {
        // Mesuré : barre masquée automatiquement, Claude agrandie couvre
        // -11,-11 → 2571,1611 (son cadre déborde) ; la notch se retirait devant elle.
        var maximized = new PixelRect(-11, -11, 2571, 1611);

        Assert.Equal(CoveringWindow.Occludes, Classify(maximized, maximized: true));
        Assert.False(FullscreenRule.IsFullscreen(maximized, Monitor, maximized: true));
    }

    [Fact]
    public void A_borderless_maximized_window_fitting_the_monitor_is_a_fullscreen()
    {
        // Relecture de #87 : un jeu sans bordure, SDL2, garde parfois WS_MAXIMIZE.
        Assert.Equal(CoveringWindow.Fullscreen, Classify(Exact, maximized: true));
        Assert.True(FullscreenRule.IsFullscreen(Exact, Monitor, maximized: true));
    }

    [Fact]
    public void A_fullscreen_left_behind_the_window_one_works_in_does_not_count()
        => Assert.Equal(CoveringWindow.Occludes, Classify(Exact, inFront: false));

    [Fact]
    public void A_maximized_window_leaving_the_taskbar_does_not_cover_the_screen()
        => Assert.Equal(CoveringWindow.Ignore, Classify(new PixelRect(-8, -8, 2568, 1528), maximized: true));

    [Fact]
    public void A_fullscreen_on_another_monitor_does_not_count()
        => Assert.Equal(CoveringWindow.Ignore, Classify(new PixelRect(2560, 0, 4480, 1080)));

    [Fact]
    public void A_window_larger_than_the_monitor_that_is_not_maximized_still_covers_it()
        => Assert.Equal(CoveringWindow.Fullscreen, Classify(new PixelRect(-8, -8, 2568, 1608)));

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void Minimized_cloaked_or_shell_windows_do_not_count(bool minimized, bool cloaked, bool shell)
        => Assert.Equal(CoveringWindow.Ignore, Classify(Exact, minimized: minimized, cloaked: cloaked, shell: shell));
}
