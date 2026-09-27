using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// <c>{ThemeResource}</c> ne peut viser qu'une propriété de dépendance. Posé
/// sur une propriété ordinaire d'une vue maison, il fait échouer le
/// chargement de la fenêtre au lancement (XamlParseException) — une erreur
/// que la compilation ne voit pas. Ce test la voit.
/// </summary>
public class XamlThemeResourceTests
{
    [Fact]
    public void ThemeResources_OnCustomViews_TargetDependencyProperties()
    {
        string app = FindApp();
        string[] sources = [.. Directory.EnumerateFiles(app, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText)];

        var offenders = new List<string>();

        foreach (string file in Directory.EnumerateFiles(app, "*.xaml", SearchOption.AllDirectories))
        {
            string xaml = File.ReadAllText(file);

            foreach (Match element in Regex.Matches(xaml, @"<(?:views|scenes):(\w+)([^>]*)>", RegexOptions.Singleline))
            {
                foreach (Match attribute in Regex.Matches(element.Groups[2].Value, @"(\w+)=""\{ThemeResource"))
                {
                    string owner = element.Groups[1].Value;
                    string property = attribute.Groups[1].Value;
                    string code = string.Join("\n", sources.Where(c => Regex.IsMatch(c, $@"\bclass\s+{owner}\b")));

                    // Les propriétés héritées des contrôles WinUI sont toutes des propriétés de dépendance.
                    bool declaredHere = Regex.IsMatch(code, $@"\bpublic\s+[\w?<>]+\s+{property}\s*\{{");

                    if (declaredHere && !code.Contains($"{property}Property", StringComparison.Ordinal))
                    {
                        offenders.Add($"{Path.GetFileName(file)} : {owner}.{property}");
                    }
                }
            }
        }

        Assert.Empty(offenders);
    }

    private static string FindApp()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "SpaceNotch.App");

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Projet de l'application introuvable depuis " + AppContext.BaseDirectory);
    }
}
