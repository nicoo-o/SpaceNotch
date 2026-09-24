using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SpaceNotch.Infrastructure.Config;
using System.Runtime.Loader;
using SpaceNotch.Core.Features;

namespace SpaceNotch.Infrastructure.Plugins;

/// <summary>
/// Résultat d'un chargement de greffons.
/// </summary>
/// <param name="Features">Fonctionnalités apportées par les greffons valides.</param>
/// <param name="Failures">
/// Échecs rencontrés, formulés pour l'utilisateur. Un greffon défaillant ne doit
/// jamais empêcher les autres de se charger, mais il ne doit pas non plus
/// disparaître sans laisser de trace : l'utilisateur doit pouvoir savoir lequel
/// n'a pas été chargé, et pourquoi.
/// </param>
public sealed record PluginLoadResult(
    IReadOnlyList<IIslandFeature> Features,
    IReadOnlyList<string> Failures);

/// <summary>
/// Charge les greffons déposés dans un dossier.
///
/// Trois propriétés sont tenues ici, et chacune a une raison d'être :
/// <list type="bullet">
/// <item><b>Isolation</b> : chaque greffon vit dans son propre contexte de
/// chargement, collectible. Une erreur de sa part ne peut donc pas corrompre
/// l'application, et ses dépendances ne peuvent pas entrer en collision avec
/// celles de l'hôte ;</item>
/// <item><b>Contrat partagé</b> : les types <c>SpaceNotch.Core</c> sont résolus par
/// le contexte par défaut, jamais rechargés. Sans cela, un greffon déclarerait sa
/// propre copie de <see cref="IIslandFeature"/> et le transtypage échouerait — le
/// piège classique du chargement dynamique ;</item>
/// <item><b>Échec isolé</b> : un assemblage illisible, un greffon sans
/// constructeur public, une fabrique qui lève — chaque cas est consigné et
/// n'interrompt pas le chargement des suivants.</item>
/// </list>
/// </summary>
public sealed class PluginLoader : IDisposable
{
    private readonly List<PluginLoadContext> _contexts = [];
    private bool _disposed;

    /// <summary>
    /// Crée un chargeur. Le dossier est créé s'il n'existe pas : son absence ne
    /// doit pas être une condition d'échec, seulement une absence de greffons.
    /// </summary>
    /// <param name="pluginsDirectory">
    /// Dossier des greffons. Par défaut <c>%LocalAppData%\SpaceNotch\plugins</c>.
    /// Un chemin explicite est accepté pour que les tests exercent le vrai
    /// chargement d'assemblages sans toucher au dossier de l'utilisateur.
    /// </param>
    public PluginLoader(string? pluginsDirectory = null)
    {
        PluginsDirectory = pluginsDirectory ?? ResolveDefaultDirectory();

        // Les greffons déposés sous l'ancien nom du produit sont repris une fois.
        if (pluginsDirectory is null)
        {
            LegacyMigration.CopyDirectoryOnce(
                Path.Combine(
                    LegacyMigration.LegacySibling(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
                    "plugins"),
                PluginsDirectory);
        }

        Directory.CreateDirectory(PluginsDirectory);
    }

    /// <summary>
    /// Emplacement par défaut des greffons :
    /// <c>%LocalAppData%\SpaceNotch\plugins</c>.
    ///
    /// Exposé publiquement pour que l'interface puisse le montrer à l'utilisateur
    /// — un dossier de greffons que personne ne trouve est un dossier inutile.
    /// </summary>
    public static string ResolveDefaultDirectory()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SpaceNotch",
            "plugins");

    /// <summary>Dossier observé.</summary>
    public string PluginsDirectory { get; }

    /// <summary>
    /// Charge tous les greffons du dossier.
    ///
    /// L'opération est synchrone et n'a lieu qu'au démarrage : un greffon est un
    /// assemblage local, il n'y a rien à attendre d'un réseau.
    /// </summary>
    public PluginLoadResult LoadAll(IslandFeatureContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var features = new List<IIslandFeature>();
        var failures = new List<string>();

        if (_disposed)
        {
            return new PluginLoadResult(features, failures);
        }

        foreach (string assemblyPath in EnumerateCandidates())
        {
            try
            {
                LoadFromPath(assemblyPath, context, features, failures);
            }
            catch (Exception ex)
            {
                failures.Add($"{Path.GetFileName(assemblyPath)} : {ex.Message}");
            }
        }

        return new PluginLoadResult(features, failures);
    }

    /// <summary>
    /// Décharge les contextes de chargement. Appelé à l'arrêt : un greffon ne doit
    /// pas retenir de ressources après la fermeture de l'application.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (PluginLoadContext context in _contexts)
        {
            try
            {
                context.Unload();
            }
            catch (Exception)
            {
                // Un contexte déjà déchargé n'est pas une erreur.
            }
        }

        _contexts.Clear();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Assemblages candidats : les fichiers <c>.dll</c> du dossier, parcourus dans
    /// un ordre stable. Le tri rend le chargement reproductible, ce qui importe
    /// lorsque deux greffons publient la même activité.
    /// </summary>
    private List<string> EnumerateCandidates()
    {
        try
        {
            return Directory.EnumerateFiles(PluginsDirectory, "*.dll", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private void LoadFromPath(
        string assemblyPath,
        IslandFeatureContext context,
        List<IIslandFeature> features,
        List<string> failures)
    {
        var pluginContext = new PluginLoadContext(assemblyPath);

        Assembly assembly;

        try
        {
            assembly = pluginContext.LoadFromAssemblyPath(assemblyPath);
        }
        catch (Exception ex)
        {
            pluginContext.Unload();
            failures.Add($"{Path.GetFileName(assemblyPath)} : assemblage illisible ({ex.Message}).");
            return;
        }

        _contexts.Add(pluginContext);

        Type[] candidates;

        try
        {
            // GetTypes peut lever si une dépendance du greffon est absente : c'est
            // un cas normal pour un greffon mal déployé, pas une raison d'arrêter.
            candidates = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            candidates = ex.Types.Where(t => t is not null).Select(t => t!).ToArray();

            failures.Add($"{Path.GetFileName(assemblyPath)} : certaines dépendances sont absentes.");
        }
        catch (Exception ex)
        {
            failures.Add($"{Path.GetFileName(assemblyPath)} : types illisibles ({ex.Message}).");
            return;
        }

        Type[] pluginTypes = candidates
            .Where(t => t is { IsAbstract: false, IsInterface: false })
            .Where(t => typeof(IIslandPlugin).IsAssignableFrom(t))
            .ToArray();

        if (pluginTypes.Length == 0)
        {
            // L'assemblage a été chargé sans contenir de greffon : c'est le cas
            // d'une dépendance déposée à côté d'un greffon. Ce n'est pas un échec.
            return;
        }

        foreach (Type pluginType in pluginTypes)
        {
            try
            {
                if (Activator.CreateInstance(pluginType) is not IIslandPlugin plugin)
                {
                    failures.Add($"{pluginType.Name} : constructeur public sans paramètre requis.");
                    continue;
                }

                foreach (IIslandFeature feature in plugin.CreateFeatures(context))
                {
                    features.Add(feature);
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{pluginType.Name} : chargement impossible ({ex.Message}).");
            }
        }
    }

    /// <summary>
    /// Contexte de chargement d'un greffon.
    ///
    /// <see cref="Load"/> retourne <c>null</c> volontairement : le runtime se
    /// rabat alors sur le contexte par défaut, où résident déjà
    /// <c>SpaceNotch.Core</c> et les assemblies du cadre. Le greffon partage donc
    /// les mêmes types que l'hôte — condition indispensable pour que
    /// <see cref="IIslandFeature"/> désigne la même chose des deux côtés.
    /// </summary>
    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public PluginLoadContext(string pluginPath)
            : base(name: $"SpaceNotch.Plugin:{Path.GetFileNameWithoutExtension(pluginPath)}", isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // Seul l'assemblage de contrat est partagé avec l'hôte : c'est lui qui
            // porte IIslandFeature, et deux copies de ce type rendraient tout
            // transtypage impossible. Tout le reste — y compris les dépendances du
            // greffon — est résolu depuis son propre dossier, afin qu'aucune version
            // ne puisse entrer en conflit avec celle de l'application.
            if (string.Equals(assemblyName.Name, "SpaceNotch.Core", StringComparison.Ordinal))
            {
                return null;
            }

            string? path = _resolver.ResolveAssemblyToPath(assemblyName);

            return path is null ? null : LoadFromAssemblyPath(path);
        }
    }
}
