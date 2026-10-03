using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Features;

namespace SpaceNotch.AuditProbe;

public sealed class HostilePlugin : IIslandPlugin
{
    public int ApiVersion => PluginContract.CurrentVersion;
    public string Name => "Sonde d'audit";
    public IEnumerable<IIslandFeature> CreateFeatures(IslandFeatureContext context)
        => [new SlowStartFeature(context), new SpamFeature(context)];
}

/// <summary>Bloque 8 s dans son démarrage, de façon synchrone.</summary>
public sealed class SlowStartFeature(IslandFeatureContext context)
    : IslandFeatureBase("plugin.audit.slow", "Sonde lente", context.Activities, context.Events)
{
    protected override Task OnStartAsync(CancellationToken cancellationToken)
    {
        Thread.Sleep(8000);
        return Task.CompletedTask;
    }

    protected override Task OnStopAsync() => Task.CompletedTask;
}

/// <summary>Après 20 s, publie 2000 activités en 2 s depuis un autre fil, puis vide tout (y compris celles des autres).</summary>
public sealed class SpamFeature(IslandFeatureContext context)
    : IslandFeatureBase("plugin.audit.spam", "Sonde bavarde", context.Activities, context.Events)
{
    protected override Task OnStartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(20000).ConfigureAwait(false);
            for (int i = 0; i < 2000; i++)
            {
                PublishActivity(new IslandActivity
                {
                    Id = $"audit.spam.{i}",
                    FeatureId = "plugin.audit.spam",
                    SceneKey = "card",
                    Title = $"Spam {i}",
                    Priority = i % 50 == 0 ? ActivityPriority.High : ActivityPriority.Normal,
                    Duration = TimeSpan.FromSeconds(6),
                });
                if (i % 20 == 0) await Task.Delay(20).ConfigureAwait(false);
            }
            await Task.Delay(10000).ConfigureAwait(false);
            // Un greffon peut-il effacer les activités des autres ?
            foreach (IslandActivity other in Activities.GetActiveActivities())
            {
                Activities.RemoveActivity(other.Id);
            }
        });
        return Task.CompletedTask;
    }

    protected override Task OnStopAsync() => Task.CompletedTask;
}
