using System;
using System.IO;
using SpaceNotch.Core.Motion;
using SpaceNotch.Infrastructure.Config;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// n° 92 : un raccourci ou un écran changé dans les Réglages s'applique tout de
/// suite. La notch comparait l'objet notifié à elle-même.
/// </summary>
public sealed class AppliedSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "spacenotch-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void The_service_notifies_with_the_object_it_just_changed()
    {
        // La cause : comparer « reçu » et « gardé », c'est comparer un objet à lui-même.
        var service = new SettingsService(new ConfigManager(_directory));
        AppSettings kept = service.Current;
        AppSettings? received = null;
        service.Changed += (_, settings) => received = settings;

        service.Update(s => s.LauncherHotkey = "Alt+Space");

        Assert.Same(kept, received);
        Assert.Equal(kept.LauncherHotkey, received!.LauncherHotkey);
    }

    [Fact]
    public void A_snapshot_sees_a_hotkey_display_or_intensity_change_made_in_place()
    {
        var service = new SettingsService(new ConfigManager(_directory));
        AppliedSettings applied = AppliedSettings.Of(service.Current);

        service.Update(s => s.LauncherHotkey = "Alt+Space");
        AppliedSettings now = AppliedSettings.Of(service.Current);
        Assert.True(now.HotkeyDiffers(applied));
        Assert.False(now.DisplayDiffers(applied));

        applied = now;
        service.Update(s => s.DisplayMode = IslandDisplayMode.Current);
        now = AppliedSettings.Of(service.Current);
        Assert.True(now.DisplayDiffers(applied));
        Assert.False(now.HotkeyDiffers(applied));

        applied = now;
        service.Update(s => s.PixelIntensity = PixelIntensity.Expressive);
        now = AppliedSettings.Of(service.Current);
        Assert.NotEqual(applied.PixelIntensity, now.PixelIntensity);
    }

    [Fact]
    public void An_unrelated_change_changes_nothing_that_is_applied()
    {
        var service = new SettingsService(new ConfigManager(_directory));
        AppliedSettings applied = AppliedSettings.Of(service.Current);

        service.Update(s => s.ShowClockAtRest = !s.ShowClockAtRest);

        Assert.Equal(applied, AppliedSettings.Of(service.Current));
    }
}
