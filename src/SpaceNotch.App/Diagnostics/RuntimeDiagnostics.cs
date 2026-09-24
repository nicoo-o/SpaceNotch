using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;
using SpaceNotch_App.Controllers;

namespace SpaceNotch_App.Diagnostics;

/// <summary>
/// Instrumentation de fonctionnement.
///
/// L'objectif est de rendre <em>vérifiable</em> ce que le cahier des charges
/// affirme : aucune image rendue et aucun travail CPU lorsque rien ne se produit.
/// Les compteurs sont alimentés par les composants concernés — jamais par une
/// boucle de mesure, qui créerait précisément la charge qu'elle prétend mesurer.
/// </summary>
public sealed class RuntimeDiagnostics : IDisposable
{
    private readonly IActivityManager _activities;
    private readonly IslandStateManager _stateManager;
    private readonly IslandController _controller;

    private readonly Stopwatch _sinceLastFrame = Stopwatch.StartNew();
    private readonly Stopwatch _uptime = Stopwatch.StartNew();

    // Instance réutilisée : interroger la mémoire ne doit pas allouer un objet
    // Process à chaque lecture, encore moins lors d'une animation.
    private readonly Process _process = Process.GetCurrentProcess();

    private long _eventCount;
    private AtmosphereRenderPath _atmospherePath = AtmosphereRenderPath.NotProbed;
    private bool _disposed;

    public RuntimeDiagnostics(
        IActivityManager activities,
        IslandStateManager stateManager,
        IslandController controller)
    {
        _activities = activities ?? throw new ArgumentNullException(nameof(activities));
        _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));

        // Le contrôleur signale chaque encombrement appliqué, donc chaque image
        // réellement produite.
        _controller.FootprintChanged += OnFootprintChanged;
    }

    /// <summary>Images rendues par le moteur de ressort depuis le démarrage.</summary>
    public long RenderedFrames => _controller.RenderedFrames;

    /// <summary>
    /// Temps écoulé depuis la dernière image. C'est la mesure directe du repos :
    /// elle doit croître indéfiniment sur une Island inutilisée.
    /// </summary>
    public TimeSpan IdleFor => _controller.IsAnimating ? TimeSpan.Zero : _sinceLastFrame.Elapsed;

    public TimeSpan Uptime => _uptime.Elapsed;

    public int ActivityCount => _activities.Count;

    public IslandState State => _stateManager.CurrentState;

    public long EventCount => _eventCount;

    /// <summary>
    /// Chemin de rendu de la dissolution actuellement actif.
    ///
    /// La valeur est observée, pas supposée : elle est rapportée par la surface
    /// elle-même après sa tentative d'attachement. Un repli silencieux sur le
    /// dégradé XAML resterait sinon invisible dans un rapport.
    /// </summary>
    public AtmosphereRenderPath AtmospherePath => _atmospherePath;

    /// <summary>Compte un événement reçu d'une source externe ou du bus.</summary>
    public void CountEvent() => _eventCount++;

    /// <summary>
    /// Enregistre le chemin de rendu réellement retenu par la surface décorative.
    /// Appelé une fois par attachement, jamais par image.
    /// </summary>
    public void ReportAtmospherePath(AtmosphereRenderPath path) => _atmospherePath = path;

    /// <summary>Mémoire de travail du processus, à comparer aux cibles du cahier des charges.</summary>
    public long WorkingSetBytes
    {
        get
        {
            try
            {
                _process.Refresh();
                return _process.WorkingSet64;
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }

    public double WorkingSetMegabytes => WorkingSetBytes / (1024.0 * 1024.0);

    /// <summary>Résumé destiné au menu de la zone de notification.</summary>
    public string BuildCompactSummary()
    {
        string activity = _controller.IsAnimating
            ? "animation"
            : $"repos {IdleFor.TotalSeconds:0}s";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{State} · {activity} · {WorkingSetMegabytes:0} MB");
    }

    /// <summary>Résumé détaillé, exploitable pour un rapport de performance.</summary>
    public string BuildSummary()
    {
        var builder = new StringBuilder();

        builder.Append(CultureInfo.InvariantCulture, $"État : {State}");
        builder.Append(CultureInfo.InvariantCulture, $" · activités : {ActivityCount}");
        builder.Append(CultureInfo.InvariantCulture, $" · mémoire : {WorkingSetMegabytes:0.0} MB");

        builder.Append(_controller.IsAnimating
            ? $" · animation en cours ({RenderedFrames} images)"
            : $" · repos depuis {IdleFor.TotalSeconds:0} s");

        builder.Append(CultureInfo.InvariantCulture, $" · événements : {EventCount}");
        builder.Append(CultureInfo.InvariantCulture, $" · dissolution : {Describe(AtmospherePath)}");
        builder.Append(CultureInfo.InvariantCulture, $" · ombre : {DescribeShadow(ShadowComposed)}");
        builder.Append(CultureInfo.InvariantCulture, $" · actif depuis {Uptime.TotalMinutes:0} min");

        return builder.ToString();
    }

    /// <summary>
    /// Libellé du chemin de rendu. Volontairement explicite : « compositeur »
    /// désigne le rendu de référence, tout autre libellé signale un repli.
    /// </summary>
    public static string Describe(AtmosphereRenderPath path) => path switch
    {
        AtmosphereRenderPath.Composition => "compositeur",
        AtmosphereRenderPath.XamlFallback => "repli XAML",
        _ => "non sondée"
    };

    /// <summary>
    /// L'ombre portée est-elle réellement calculée par le compositeur ?
    ///
    /// La question mérite d'être posée séparément de celle de la dissolution : les
    /// deux dépendent de capacités différentes, et une ombre absente ne se
    /// remarque pas de la même manière selon le fond d'écran — sur un papier peint
    /// clair, l'Island flotte ; sur un fond sombre, personne ne voit rien. Sans
    /// cette remontée, une ombre silencieusement refusée serait indistinguable
    /// d'une ombre simplement invisible. Voir ADR-013.
    /// </summary>
    public bool? ShadowComposed { get; private set; }

    public void ReportShadow(bool composed) => ShadowComposed = composed;

    private static string DescribeShadow(bool? composed) => composed switch
    {
        true => "compositeur",
        false => "absente",
        _ => "non sondée"
    };

    private void OnFootprintChanged(object? sender, IslandFootprint footprint)
        => _sinceLastFrame.Restart();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _controller.FootprintChanged -= OnFootprintChanged;
        _sinceLastFrame.Stop();
        _uptime.Stop();
        _process.Dispose();
        GC.SuppressFinalize(this);
    }
}
