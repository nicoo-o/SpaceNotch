using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using SpaceNotch.Core.Localization;
using Xunit;

namespace SpaceNotch.Core.Tests;

// Ce test bascule la langue globale : jamais en même temps qu'un autre (n° 84).
[Collection(GlobalLanguageGroup.Name)]
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

    [Fact]
    public void OnlyThisCollectionChangesTheLanguage()
    {
        // Garde-fou de n° 84 : un test qui change Lang.French hors de cette
        // collection rend les autres instables, au hasard de l'ordre d'exécution.
        string folder = Path.GetDirectoryName(SourceFile())!;
        string[] offenders = [.. Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", System.StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", System.StringComparison.Ordinal)
                && !f.EndsWith("TestLanguage.cs", System.StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f) is var text
                && ChangesTheLanguage(text)
                && !text.Contains("[Collection(GlobalLanguageGroup.Name)]", System.StringComparison.Ordinal))
            .Select(f => Path.GetFileName(f)!)];

        Assert.Empty(offenders);
    }

    // n° 87 : la garde lisait « Lang.French = » à la lettre ; un espace, un nom
    // complet ou une affectation composée (|=, &=, ^=) lui échappaient. « == »
    // et « => » (bras de switch) sont des lectures.
    private static readonly Regex Assignment = new(@"\bLang\s*\.\s*French\s*(=(?![=>])|[|&^]=)", RegexOptions.CultureInvariant);

    private static bool ChangesTheLanguage(string text) => Assignment.IsMatch(text);

    [Theory]
    [InlineData("Lang.French = true;")]
    [InlineData("Lang.French=false;")]
    [InlineData("Lang . French = true;")]
    [InlineData("SpaceNotch.Core.Localization.Lang.French = before;")]
    [InlineData("Lang.French |= true;")]
    [InlineData("Lang.French &= false;")]
    [InlineData("Lang.French ^= true;")]
    public void TheGuardSeesEveryWayOfChangingTheLanguage(string line)
        => Assert.True(ChangesTheLanguage(line));

    [Theory]
    [InlineData("if (Lang.French == true) { }")]
    [InlineData("bool french = Lang.French;")]
    [InlineData("Assert.True(Lang.French);")]
    [InlineData("MyLang.French = true;")]
    [InlineData("string s = x switch { _ when Lang.French => \"fr\", _ => \"en\" };")]
    public void TheGuardIgnoresReadingTheLanguage(string line)
        => Assert.False(ChangesTheLanguage(line));

    private static string SourceFile([CallerFilePath] string path = "") => path;
}
