using SpaceNotch.Core.Presentation;
using SpaceNotch.Infrastructure.Config;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>Réglages du détachement et du style : bornes et valeurs par défaut.</summary>
public class DetachSettingsTests
{
    [Fact]
    public void DefaultsMatchTheReference()
    {
        var settings = new AppSettings();

        Assert.Equal(NotchEdge.Top, settings.DockEdge);
        Assert.Equal(DetachFeel.Natural, settings.DetachFeel);
        Assert.Equal(0.05, settings.StretchAmount, 6);
        Assert.Equal(40, settings.TearDistance, 6);
        Assert.True(settings.MagnetsEnabled && settings.GooEnabled && settings.MonitorResistance && settings.AllowSideEdges);
        Assert.Equal(((byte)255, (byte)0, (byte)0, (byte)0), settings.SurfaceColor());
        Assert.False(settings.ShowOutline);
    }

    [Fact]
    public void OutOfRangeValuesAreBroughtBack()
    {
        var settings = new AppSettings
        {
            StretchAmount = 0.5,
            TearDistance = 2,
            SurfaceOpacity = 0.1,
            DockOffset = 3,
            FloatingShadowOpacity = 9,
            SideShoulderRadius = -4,
            CustomSurfaceColor = "vert",
            DetachFeel = (DetachFeel)42,
            DockEdge = (NotchEdge)9
        };

        settings.Sanitize();

        Assert.Equal(0.10, settings.StretchAmount, 6);
        Assert.Equal(Detachment.MinimumTearDistance, settings.TearDistance, 6);
        Assert.Equal(0.55, settings.SurfaceOpacity, 6);
        Assert.Equal(1, settings.DockOffset, 6);
        Assert.Equal(0.6, settings.FloatingShadowOpacity, 6);
        Assert.Equal(0, settings.SideShoulderRadius, 6);
        Assert.Equal("#14161C", settings.CustomSurfaceColor);
        Assert.Equal(DetachFeel.Natural, settings.DetachFeel);
        Assert.Equal(NotchEdge.Top, settings.DockEdge);
    }

    [Fact]
    public void ASideDockFallsBackToTopWhenSidesAreTurnedOff()
    {
        var settings = new AppSettings { DockEdge = NotchEdge.Right, AllowSideEdges = false };
        settings.Sanitize();

        Assert.Equal(NotchEdge.Top, settings.DockEdge);
    }

    [Fact]
    public void CustomColorAndTransparencyResolve()
    {
        var settings = new AppSettings { SurfaceTint = SurfaceTint.Custom, CustomSurfaceColor = "#3366CC", SurfaceOpacity = 0.8 };

        Assert.Equal(((byte)204, (byte)0x33, (byte)0x66, (byte)0xCC), settings.SurfaceColor());
    }

    [Fact]
    public void FirmFeelsFasterThanSoft()
    {
        var soft = new AppSettings { DetachFeel = DetachFeel.Soft }.DetachFollowSpring;
        var firm = new AppSettings { DetachFeel = DetachFeel.Firm }.DetachFollowSpring;

        Assert.True(firm.ResponseSeconds < soft.ResponseSeconds);
        Assert.True(firm.DampingRatio > soft.DampingRatio);
    }
}
