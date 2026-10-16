using SpaceNotch.Platform.Windows.Shell;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// n° 11 : une surcouche « toujours devant » que les clics traversent fait dire
/// « occupé » au shell ; elle n'est pas un plein écran, la notch reste.
/// </summary>
public sealed class FullscreenRuleTests
{
    private static readonly PixelRect Monitor = new(0, 0, 2560, 1600);

    private static bool Fullscreen(
        PixelRect window,
        uint style = 0,
        bool visible = true,
        bool minimized = false,
        bool maximized = false,
        bool cloaked = false,
        bool shell = false,
        bool ours = false)
        => FullscreenRule.IsFullscreenWindow(window, Monitor, style, visible, minimized, maximized, cloaked, shell, ours);

    [Fact]
    public void A_window_covering_the_whole_monitor_is_a_fullscreen()
        => Assert.True(Fullscreen(new PixelRect(0, 0, 2560, 1600)));

    [Fact]
    public void A_click_through_overlay_is_not_a_fullscreen()
        => Assert.False(Fullscreen(new PixelRect(0, 0, 2560, 1600), style: FullscreenRule.ExTransparent | 0x00080000));

    [Fact]
    public void A_maximized_window_leaves_the_taskbar_and_does_not_count()
        => Assert.False(Fullscreen(new PixelRect(-8, -8, 2568, 1528)));

    [Fact]
    public void A_maximized_window_over_an_auto_hidden_taskbar_is_not_a_fullscreen()
    {
        // Mesuré : barre masquée automatiquement, Claude agrandie couvre
        // -11,-11 → 2571,1611 ; la notch se retirait devant elle.
        var maximized = new PixelRect(-11, -11, 2571, 1611);

        Assert.False(Fullscreen(maximized, maximized: true));
        Assert.False(FullscreenRule.IsFullscreen(maximized, Monitor, maximized: true));
        Assert.True(FullscreenRule.IsFullscreen(new PixelRect(0, 0, 2560, 1600), Monitor, maximized: false));
    }

    [Fact]
    public void A_fullscreen_on_another_monitor_does_not_count()
        => Assert.False(Fullscreen(new PixelRect(2560, 0, 4480, 1080)));

    [Fact]
    public void A_window_larger_than_the_monitor_still_covers_it()
        => Assert.True(Fullscreen(new PixelRect(-8, -8, 2568, 1608)));

    [Theory]
    [InlineData(false, false, false, false, false)]
    [InlineData(true, true, false, false, false)]
    [InlineData(true, false, true, false, false)]
    [InlineData(true, false, false, true, false)]
    [InlineData(true, false, false, false, true)]
    public void Hidden_minimized_cloaked_shell_or_own_windows_do_not_count(bool visible, bool minimized, bool cloaked, bool shell, bool ours)
        => Assert.False(Fullscreen(new PixelRect(0, 0, 2560, 1600), visible: visible, minimized: minimized, cloaked: cloaked, shell: shell, ours: ours));
}
