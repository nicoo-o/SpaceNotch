using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Features;

namespace SpaceNotch.TestPlugin;

/// <summary>
/// Greffon valide : il apporte une fonctionnalité qui ne fait rien, mais qui
/// respecte le cycle de vie.
/// </summary>
public sealed class RecordingPlugin : IIslandPlugin
{
    public string Name => "Greffon de test";

    public IEnumerable<IIslandFeature> CreateFeatures(IslandFeatureContext context)
        => [new RecordingFeature(context)];
}

/// <summary>
/// Fonctionnalité apportée par le greffon. Elle compte ses démarrages et arrêts,
/// ce qui permet de vérifier depuis les tests que le cycle de vie s'applique de la
/// même façon à une fonctionnalité externe qu'à une fonctionnalité intégrée.
/// </summary>
public sealed class RecordingFeature : IslandFeatureBase
{
    public const string FeatureKey = "plugin.test.recording";

    private readonly IslandFeatureContext _context;

    public RecordingFeature(IslandFeatureContext context)
        : base(FeatureKey, "Greffon de test", context.Activities, context.Events)
    {
        _context = context;
    }

    public int StartCount { get; private set; }

    public int StopCount { get; private set; }

    /// <summary>Contexte reçu, exposé pour vérifier qu'il a bien été transmis.</summary>
    public IslandFeatureContext Context => _context;

    protected override Task OnStartAsync(CancellationToken cancellationToken)
    {
        StartCount++;
        return Task.CompletedTask;
    }

    protected override Task OnStopAsync()
    {
        StopCount++;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Greffon qui échoue pendant la création de ses fonctionnalités.
///
/// Il sert à vérifier l'isolation à l'intérieur d'un même assemblage : le greffon
/// valide doit tout de même être chargé, et l'échec doit être rapporté.
/// </summary>
public sealed class ExplodingPlugin : IIslandPlugin
{
    public string Name => "Greffon défaillant";

    public IEnumerable<IIslandFeature> CreateFeatures(IslandFeatureContext context)
        => throw new InvalidOperationException("échec volontaire du greffon");
}
