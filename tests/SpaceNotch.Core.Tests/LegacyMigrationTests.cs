using System;
using System.IO;
using SpaceNotch.Infrastructure.Config;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// Reprise des données écrites sous l'ancien nom du produit, NotchFlow.
/// </summary>
public sealed class LegacyMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sn-migration-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ALegacyFolder_IsCopiedOnce_AndNeverMoved()
    {
        string legacy = Path.Combine(_root, LegacyMigration.LegacyProductName);
        string target = Path.Combine(_root, "SpaceNotch");

        Directory.CreateDirectory(Path.Combine(legacy, "plugins"));
        File.WriteAllText(Path.Combine(legacy, "config.json"), "{ \"CornerRadiusBottom\": 30 }");
        File.WriteAllText(Path.Combine(legacy, "plugins", "a.dll"), "x");

        Assert.True(LegacyMigration.CopyDirectoryOnce(legacy, target));

        Assert.True(File.Exists(Path.Combine(target, "config.json")));
        Assert.True(File.Exists(Path.Combine(target, "plugins", "a.dll")));

        // Copié, pas déplacé : une ancienne version relancée retrouve ses fichiers.
        Assert.True(File.Exists(Path.Combine(legacy, "config.json")));

        // Une seule fois : la destination fait foi ensuite.
        File.WriteAllText(Path.Combine(target, "config.json"), "{}");
        Assert.False(LegacyMigration.CopyDirectoryOnce(legacy, target));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(target, "config.json")));
    }

    [Fact]
    public void WithoutALegacyFolder_NothingHappens()
    {
        Assert.False(LegacyMigration.CopyDirectoryOnce(
            Path.Combine(_root, "absent"),
            Path.Combine(_root, "SpaceNotch")));

        Assert.False(Directory.Exists(Path.Combine(_root, "SpaceNotch")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
