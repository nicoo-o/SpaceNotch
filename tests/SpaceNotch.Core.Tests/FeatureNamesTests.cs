using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Localization;
using SpaceNotch.Features.Productivity;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// n° 15 : le nom d'une fonctionnalité (menu Activités de l'icône, message des
/// Réglages) restait en français dans l'interface anglaise.
/// </summary>
[Collection(GlobalLanguageGroup.Name)]
public sealed class FeatureNamesTests
{
    // Un nom passé en dur au constructeur de base, au lieu de Lang.T(…).
    private static readonly Regex LiteralName = new(@":\s*base\(\s*\w+\s*,\s*""", RegexOptions.CultureInvariant);

    [Fact]
    public void No_feature_passes_a_name_in_one_language_only()
    {
        string features = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourceFile())!, "..", "..", "src", "SpaceNotch.Features"));
        Assert.True(Directory.Exists(features), features);

        string[] offenders = [.. Directory.EnumerateFiles(features, "*.cs", SearchOption.AllDirectories)
            .Where(f => LiteralName.IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetFileName(f)!)];

        Assert.Empty(offenders);
    }

    [Theory]
    [InlineData(": base(FeatureKey, \"Minuteur\", activities, events, isEnabled)")]
    [InlineData(":base(Id,\"Météo\", a, e)")]
    public void The_guard_sees_a_name_in_one_language(string line)
        => Assert.Matches(LiteralName, line);

    [Fact]
    public void The_guard_lets_a_translated_name_through()
        => Assert.DoesNotMatch(LiteralName, ": base(FeatureKey, Lang.T(\"Minuteur\", \"Timer\"), activities, events, isEnabled)");

    [Fact]
    public async Task A_feature_is_named_in_the_language_of_the_interface()
    {
        bool before = Lang.French;

        try
        {
            Lang.French = false;
            await using (var english = new TimerFeature(new ActivityManager(), new EventBus()))
            {
                Assert.Equal("Timer", english.DisplayName);
            }

            Lang.French = true;
            await using var french = new TimerFeature(new ActivityManager(), new EventBus());
            Assert.Equal("Minuteur", french.DisplayName);
        }
        finally
        {
            Lang.French = before;
        }
    }

    private static string SourceFile([CallerFilePath] string path = "") => path;
}
