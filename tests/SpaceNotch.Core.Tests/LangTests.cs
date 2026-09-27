using System.Globalization;
using SpaceNotch.Core.Localization;
using Xunit;

namespace SpaceNotch.Core.Tests;

public class LangTests
{
    [Theory]
    [InlineData("fr-FR", true)]
    [InlineData("fr-CA", true)]
    [InlineData("fr-BE", true)]
    [InlineData("en-US", false)]
    [InlineData("de-DE", false)]
    public void FollowsTheWindowsLanguage(string culture, bool french)
        => Assert.Equal(french, Lang.IsFrench(CultureInfo.GetCultureInfo(culture)));
}
