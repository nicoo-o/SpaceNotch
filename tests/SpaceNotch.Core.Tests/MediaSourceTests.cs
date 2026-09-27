using SpaceNotch.Core.Activities;
using Xunit;

namespace SpaceNotch.Core.Tests;

public class MediaSourceTests
{
    [Theory]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "Spotify")]
    [InlineData("MSEdge", "Edge")]
    [InlineData("Chrome", "Chrome")]
    [InlineData("308046B0AF4A39CB", "308046B0AF4A39CB")]
    [InlineData("AppleInc.AppleMusicWin_nzyj5cx40ttqa!App", "Apple Music")]
    [InlineData("foobar2000.exe", "Foobar2000")]
    [InlineData(null, "")]
    [InlineData("  ", "")]
    public void TechnicalIds_BecomeReadableNames(string? appId, string expected)
        => Assert.Equal(expected, MediaSource.FriendlyName(appId));
}
