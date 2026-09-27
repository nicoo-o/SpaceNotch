using System.Linq;
using SpaceNotch.Core.Motion;
using Xunit;

namespace SpaceNotch.Core.Tests;

public sealed class PixelGlyphsTests
{
    [Theory]
    [InlineData("Music")]
    [InlineData("VolumeHigh")]
    [InlineData("VolumeMute")]
    [InlineData("Bluetooth")]
    [InlineData("Download")]
    [InlineData("Notification")]
    [InlineData("Timer")]
    [InlineData("Search")]
    [InlineData("Check")]
    public void KnownKeys_HaveAFullGrid(string key)
    {
        var mask = PixelGlyphs.Resolve(key);

        Assert.NotNull(mask);
        Assert.Equal(49, mask!.Count);
        Assert.InRange(mask.Count(on => on), 5, 44);
    }

    [Fact]
    public void Keys_IgnoreCase()
        => Assert.Same(PixelGlyphs.Resolve("music"), PixelGlyphs.Resolve("Music"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("✓")]
    [InlineData("PluginSomething")]
    public void UnknownKeys_KeepTheirFontGlyph(string? key)
        => Assert.Null(PixelGlyphs.Resolve(key));

    [Fact]
    public void EveryGlyph_IsDistinct()
    {
        var shapes = PixelGlyphs.Keys
            .Where(k => k != "Volume") // alias de VolumeHigh
            .Select(k => string.Concat(PixelGlyphs.Resolve(k)!.Select(on => on ? 'x' : '.')))
            .ToList();

        Assert.Equal(shapes.Count, shapes.Distinct().Count());
    }

    [Fact]
    public void Volume_GrowsWithLevel()
    {
        int Lit(string key) => PixelGlyphs.Resolve(key)!.Count(on => on);

        Assert.True(Lit("VolumeLow") < Lit("VolumeMedium"));
        Assert.True(Lit("VolumeMedium") < Lit("VolumeHigh"));
    }

    [Fact]
    public void LightOrder_StartsAtTheCentre()
    {
        Assert.Equal(0, PixelGlyphs.LightOrder(24));
        Assert.True(PixelGlyphs.LightOrder(0) > PixelGlyphs.LightOrder(16));
        Assert.Equal(PixelGlyphs.LightOrder(0), PixelGlyphs.LightOrder(48), 6);
    }
}
