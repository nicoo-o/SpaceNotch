using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Features;
using SpaceNotch.Infrastructure.Plugins;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// Vérifie le chargement des greffons sur un assemblage réel.
///
/// Le greffon de test est compilé puis copié dans un dossier temporaire : c'est la
/// seule manière de prouver ce qui compte vraiment — que l'assemblage de contrat
/// est bien partagé entre l'hôte et le greffon, sans quoi le transtypage vers
/// <see cref="IIslandFeature"/> échouerait silencieusement.
/// </summary>
public class PluginLoaderTests : IDisposable
{
    private readonly string _directory;
    private readonly IslandFeatureContext _context;

    private PluginLoader? _loader;

    public PluginLoaderTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            "spacenotch-plugins",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_directory);

        _context = new IslandFeatureContext(new ActivityManager(), new EventBus());
    }

    public void Dispose()
    {
        // Le chargeur est libéré avant le dossier : un contexte de chargement
        // collectible rend ses fichiers de façon asynchrone, et le verrou peut
        // subsister un instant même après Unload.
        _loader?.Dispose();
        _loader = null;

        TryDeleteDirectory(_directory);

        GC.SuppressFinalize(this);
    }

    /// <summary>Crée le chargeur observé et le retient pour la libération finale.</summary>
    private PluginLoader CreateLoader(string? directory = null)
    {
        _loader = new PluginLoader(directory ?? _directory);
        return _loader;
    }

    /// <summary>Copie l'assemblage du greffon de test dans le dossier observé.</summary>
    private void CopyTestPlugin()
    {
        string source = Path.Combine(AppContext.BaseDirectory, "SpaceNotch.TestPlugin.dll");

        Assert.True(File.Exists(source), $"Greffon de test introuvable : {source}");

        File.Copy(source, Path.Combine(_directory, "SpaceNotch.TestPlugin.dll"), overwrite: true);
    }

    [Fact]
    public void EmptyDirectory_YieldsNothingAndNoFailure()
    {
        PluginLoadResult result = CreateLoader().LoadAll(_context);

        Assert.Empty(result.Features);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void MissingDirectory_IsCreatedRatherThanTreatedAsAnError()
    {
        string nested = Path.Combine(_directory, "absent");

        PluginLoader loader = CreateLoader(nested);

        Assert.True(Directory.Exists(nested));
        Assert.Empty(loader.LoadAll(_context).Features);
    }

    [Fact]
    public void ValidPlugin_IsLoadedAndSharesTheContractAssembly()
    {
        CopyTestPlugin();

        PluginLoadResult result = CreateLoader().LoadAll(_context);

        IIslandFeature feature = Assert.Single(result.Features);
        Assert.Equal("plugin.test.recording", feature.Id);
        Assert.Equal("Greffon de test", feature.DisplayName);
    }

    [Fact]
    public async Task FeatureFromAPlugin_FollowsTheSameLifecycleAsABuiltInOne()
    {
        CopyTestPlugin();

        IIslandFeature feature = CreateLoader().LoadAll(_context).Features.Single();

        await feature.StartAsync();
        await feature.SetEnabledAsync(false);

        // Le cycle de vie est hérité du cœur : une fonctionnalité externe ne peut
        // pas se soustraire à la règle « inactive = zéro travail ».
        Assert.Equal(FeatureState.Stopped, feature.State);
        Assert.False(feature.IsEnabled);
    }

    [Fact]
    public void OneFaultyPluginDoesNotPreventTheValidOne()
    {
        CopyTestPlugin();

        PluginLoadResult result = CreateLoader().LoadAll(_context);

        // Isolation à l'intérieur d'un même assemblage : le greffon qui lève est
        // rapporté, l'autre est chargé.
        Assert.Single(result.Features);
        Assert.Contains(result.Failures, failure => failure.Contains("ExplodingPlugin", StringComparison.Ordinal));
    }

    [Fact]
    public void CorruptedAssembly_IsReportedWithoutThrowing()
    {
        File.WriteAllText(
            Path.Combine(_directory, "Corrompu.dll"),
            "ceci n'est pas un assemblage .NET");

        PluginLoadResult result = CreateLoader().LoadAll(_context);

        Assert.Empty(result.Features);
        Assert.Contains(result.Failures, failure => failure.Contains("Corrompu.dll", StringComparison.Ordinal));
    }

    [Fact]
    public void Dispose_UnloadsWithoutThrowing()
    {
        CopyTestPlugin();

        PluginLoader loader = CreateLoader();
        loader.LoadAll(_context);

        loader.Dispose();
        loader.Dispose();

        // Après déchargement, plus rien n'est chargé : le dossier n'est pas relu.
        Assert.Empty(loader.LoadAll(_context).Features);
    }

    private static void TryDeleteDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception)
        {
            // Un assemblage chargé depuis un chemin garde ce chemin verrouillé
            // jusqu'au déchargement effectif du contexte. Le dossier est de toute
            // façon temporaire : ce n'est pas la peine de faire échouer le test
            // pour un artefact que le système nettoiera.
        }
    }
}
