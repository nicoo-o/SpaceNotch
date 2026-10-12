using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// n° 57 : les encres des Réglages sont des jetons de thème. Un jeton absent
/// d'un des trois thèmes ne se voit qu'à l'exécution, en contraste élevé ou en
/// thème clair — la fenêtre ne s'ouvrirait plus. Ce test le voit avant.
/// </summary>
public sealed class ThemeTokensTests
{
    private static string Tokens([CallerFilePath] string here = "")
        => File.ReadAllText(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "src", "SpaceNotch.App", "UI", "Themes", "Tokens.xaml"));

    [Fact]
    public void Every_settings_ink_exists_in_the_three_themes()
    {
        string xaml = Tokens();
        string[] themes = ["Default", "Light", "HighContrast"];

        string[][] keys = themes.Select(theme =>
        {
            int start = xaml.IndexOf($"<ResourceDictionary x:Key=\"{theme}\">", System.StringComparison.Ordinal);
            Assert.True(start >= 0, theme);
            int end = xaml.IndexOf("</ResourceDictionary>", start, System.StringComparison.Ordinal);

            return Regex.Matches(xaml[start..end], "x:Key=\"(NfSettings\\w+)\"")
                .Select(m => m.Groups[1].Value)
                .Order(System.StringComparer.Ordinal)
                .ToArray();
        }).ToArray();

        Assert.NotEmpty(keys[0]);
        Assert.Equal(keys[0], keys[1]);
        Assert.Equal(keys[0], keys[2]);
    }

    [Fact]
    public void In_high_contrast_settings_inks_are_system_colors()
    {
        string xaml = Tokens();
        int start = xaml.IndexOf("<ResourceDictionary x:Key=\"HighContrast\">", System.StringComparison.Ordinal);
        string highContrast = xaml[start..xaml.IndexOf("</ResourceDictionary>", start, System.StringComparison.Ordinal)];

        foreach (Match brush in Regex.Matches(highContrast, "<SolidColorBrush x:Key=\"NfSettings\\w+\" Color=\"([^\"]+)\""))
        {
            Assert.StartsWith("{ThemeResource SystemColor", brush.Groups[1].Value);
        }
    }
}
