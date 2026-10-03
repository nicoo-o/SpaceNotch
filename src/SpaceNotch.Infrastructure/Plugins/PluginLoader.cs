using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SpaceNotch.Infrastructure.Config;
using System.Runtime.Loader;
using SpaceNotch.Core.Activities;
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
/// <param name="Pending">
/// Greffons présents mais pas (ou plus) approuvés : non chargés, proposés à
/// l'approbation dans Réglages › À propos.
/// </param>
public sealed record PluginLoadResult(
    IReadOnlyList<IIslandFeature> Features,
    IReadOnlyList<string> Failures,
    IReadOnlyList<string>? Pending = null);

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
    /// <param name="context">Contexte donné aux greffons.</param>
    /// <param name="allowlist">
    /// Greffons approuvés. <c>null</c> charge tout : réservé aux tests ; l'application
    /// passe toujours sa liste.
    /// </param>
    public PluginLoadResult LoadAll(
        IslandFeatureContext context,
        PluginAllowlist? allowlist = null,
        PluginLoadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        var features = new List<IIslandFeature>();
        var failures = new List<string>();
        var pending = new List<string>();

        if (_disposed)
        {
            return new PluginLoadResult(features, failures, pending);
        }

        foreach (string assemblyPath in EnumerateCandidates())
        {
            if (allowlist is not null && !allowlist.IsAllowed(assemblyPath))
            {
                pending.Add(assemblyPath);
                continue;
            }

            try
            {
                LoadFromPath(assemblyPath, context, features, failures, allowlist, options);
            }
            catch (Exception ex)
            {
                failures.Add($"{Path.GetFileName(assemblyPath)} : {ex.Message}");
            }
        }

        return new PluginLoadResult(features, failures, pending);
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
    /// <summary>Greffons du dossier qui attendent une approbation, sans rien charger.</summary>
    public static IReadOnlyList<string> FindPending(string directory, PluginAllowlist allowlist)
    {
        ArgumentNullException.ThrowIfNull(allowlist);

        try
        {
            return Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly)
                .Where(path => !allowlist.IsAllowed(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

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
        List<string> failures,
        PluginAllowlist? allowlist,
        PluginLoadOptions? options)
    {
        if (allowlist is not null && !allowlist.IsAllowed(assemblyPath))
        {
            failures.Add($"{Path.GetFileName(assemblyPath)} : l'empreinte a changé avant le chargement.");
            return;
        }

        if (options?.RequireAuthenticodeSignature == true
            && !PluginSignatureVerifier.HasValidSignature(assemblyPath))
        {
            failures.Add($"{Path.GetFileName(assemblyPath)} : signature Authenticode absente ou invalide.");
            return;
        }

        // Les dépendances passent les mêmes contrôles que le greffon (audit
        // SN-18) : sans cela, un greffon approuvé chargeait depuis son dossier
        // n'importe quelle .dll non approuvée.
        bool requireSignature = options?.RequireAuthenticodeSignature == true;
        var pluginContext = new PluginLoadContext(assemblyPath, dependency =>
        {
            if (allowlist is not null && !allowlist.IsAllowed(dependency))
            {
                failures.Add($"{Path.GetFileName(dependency)} : dépendance non approuvée de {Path.GetFileName(assemblyPath)}.");
                return false;
            }

            if (requireSignature && !PluginSignatureVerifier.HasValidSignature(dependency))
            {
                failures.Add($"{Path.GetFileName(dependency)} : dépendance sans signature Authenticode valide.");
                return false;
            }

            return true;
        });

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

                if (plugin.ApiVersion != PluginContract.CurrentVersion)
                {
                    failures.Add($"{plugin.Name} : contrat incompatible (v{plugin.ApiVersion}, attendu v{PluginContract.CurrentVersion}).");
                    continue;
                }

                // Chaque greffon ne voit et ne touche que ses propres activités
                // (audit SN-19).
                IslandFeatureContext scoped = context with
                {
                    Activities = new ScopedActivityManager(context.Activities)
                };

                foreach (IIslandFeature feature in plugin.CreateFeatures(scoped))
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
        private readonly Func<string, bool> _admit;

        public PluginLoadContext(string pluginPath, Func<string, bool> admit)
            : base(name: $"SpaceNotch.Plugin:{Path.GetFileNameWithoutExtension(pluginPath)}", isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
            _admit = admit;
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

            return path is null || !_admit(path) ? null : LoadFromAssemblyPath(path);
        }
    }
}
