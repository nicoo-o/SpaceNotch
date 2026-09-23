using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NotchFlow.Core.Activities;
using NotchFlow.Core.Events;
using NotchFlow.Core.Features;
using NotchFlow.Infrastructure.Plugins;
using Xunit;

namespace NotchFlow.SamplePlugin.Tests;

/// <summary>
/// Chaîne de chargement complète, sur l'assemblage réellement compilé.
///
/// C'est le test qui répond à la question posée : un tiers peut-il écrire un
/// greffon sans lire le code source de l'hôte ? Ici, aucun type du greffon
/// n'est référencé par son nom — l'assemblage est copié dans un dossier, le
/// chargeur le découvre, instancie le greffon et récupère la fonctionnalité
/// produite. Exactement le chemin que suivrait un greffon livré par un tiers.
/// </summary>
public sealed class SamplePluginLoadingTests : IDisposable
{
    private const string AssemblyFileName = "NotchFlow.SamplePlugin.Weather.dll";

    private readonly string _directory;
    private readonly IslandFeatureContext _context;

    private PluginLoader? _loader;

    public SamplePluginLoadingTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            "notchflow-sample-plugin",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_directory);

        _context = new IslandFeatureContext(new ActivityManager(), new EventBus());
    }

    public void Dispose()
    {
        _loader?.Dispose();
        _loader = null;

        TryDeleteDirectory(_directory);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void SamplePlugin_IsDiscoveredAndYieldsItsFeature()
    {
        CopySamplePlugin();

        _loader = new PluginLoader(_directory);
        PluginLoadResult result = _loader.LoadAll(_context);

        // Aucun échec : si le greffon levait au chargement, il serait rapporté ici
        // plutôt que de disparaître silencieusement.
        Assert.Empty(result.Failures);

        IIslandFeature feature = Assert.Single(result.Features);

        // L'identifiant est écrit en littéral, et non lu depuis une constante du
        // greffon : c'est précisément ce qu'on veut démontrer. Ce fichier de test ne
        // connaît aucun type du greffon à la compilation ; il n'observe que ce que
        // l'hôte observe — un identifiant déclaré par un assemblage chargé.
        Assert.Equal("plugin.weather", feature.Id);
        Assert.Equal("Météo locale", feature.DisplayName);
        Assert.True(feature.IsEnabled);
    }

    [Fact]
    public async Task FeatureFromTheSamplePlugin_IsDrivenByTheSameLifecycleContract()
    {
        CopySamplePlugin();

        _loader = new PluginLoader(_directory);
        IIslandFeature feature = _loader.LoadAll(_context).Features.Single();

        // Désactiver une fonctionnalité issue d'un greffon libère ses ressources par
        // le même contrat que les fonctionnalités intégrées. Aucun appel réseau n'est
        // déclenché ici : la désactivation ne fait que libérer.
        await feature.SetEnabledAsync(false);

        Assert.Equal(FeatureState.Stopped, feature.State);
        Assert.False(feature.IsEnabled);

        await feature.DisposeAsync();
    }

    [Fact]
    public void TheSamplePlugin_DoesNotDependOnTheContractAssemblyOfItsOwnCopy()
    {
        CopySamplePlugin();

        // Le dossier contient l'assemblage du greffon mais pas NotchFlow.Core : la
        // résolution doit passer par le contexte par défaut de l'hôte. C'est la
        // condition pour que IIslandFeature désigne le même type des deux côtés — et
        // sans elle, le transtypage échouerait en silence.
        Assert.False(File.Exists(Path.Combine(_directory, "NotchFlow.Core.dll")));

        _loader = new PluginLoader(_directory);
        PluginLoadResult result = _loader.LoadAll(_context);

        Assert.Empty(result.Failures);
        Assert.Single(result.Features);
    }

    private void CopySamplePlugin()
    {
        string source = Path.Combine(AppContext.BaseDirectory, AssemblyFileName);

        Assert.True(
            File.Exists(source),
            $"Greffon d'exemple introuvable : {source} — la référence de projet doit le copier dans la sortie des tests.");

        File.Copy(source, Path.Combine(_directory, AssemblyFileName), overwrite: true);
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
            // Un assemblage chargé garde son chemin verrouillé jusqu'au déchargement
            // effectif du contexte. Le dossier est temporaire : inutile de faire
            // échouer le test pour cela.
        }
    }
}
