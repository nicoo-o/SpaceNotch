using SpaceNotch.Platform.Windows.System;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// n° 65 : le Gestionnaire des tâches désactive le démarrage dans
/// StartupApproved, sans toucher à Run ; les Réglages disaient « activé ».
/// </summary>
public sealed class StartupApprovedTests
{
    [Fact]
    public void Without_a_mark_from_windows_the_registration_is_active()
    {
        Assert.True(StartupRegistration.IsApproved(null));
        Assert.True(StartupRegistration.IsApproved([]));
    }

    [Theory]
    [InlineData((byte)0x02, true)]
    [InlineData((byte)0x06, true)]
    [InlineData((byte)0x03, false)]
    [InlineData((byte)0x07, false)]
    public void An_odd_first_byte_means_disabled(byte first, bool approved)
    {
        // Mesuré sur la machine de l'auteur le 2026-10-07 : 03 puis 02 une fois réactivé.
        var state = new byte[12];
        state[0] = first;

        Assert.Equal(approved, StartupRegistration.IsApproved(state));
    }
}
