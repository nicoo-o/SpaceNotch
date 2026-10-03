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

    [Theory]
    [InlineData(true, 0, "0 notification retenue")]
    [InlineData(true, 1, "1 notification retenue")]
    [InlineData(true, 3, "3 notifications retenues")]
    [InlineData(false, 0, "0 held notifications")]
    [InlineData(false, 1, "1 held notification")]
    [InlineData(false, 3, "3 held notifications")]
    public void AgreesTheNounWithTheCount(bool french, int count, string expected)
    {
        bool before = Lang.French;
        Lang.French = french;

        try
        {
            Assert.Equal(expected, Lang.Count(count, "notification retenue", "notifications retenues", "held notification", "held notifications"));
        }
        finally
        {
            Lang.French = before;
        }
    }
}
