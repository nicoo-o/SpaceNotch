using System;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Features;
using SpaceNotch.Features.Calendar;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// États des intégrations (n° 2, P07 + P17) : dire pourquoi une fonction ne fait rien.
/// </summary>
public class IntegrationStatusTests
{
    [Theory]
    [InlineData(FeatureState.Stopped, IntegrationProbe.Ok, IntegrationStatus.Disabled)]
    [InlineData(FeatureState.Stopped, IntegrationProbe.Missing, IntegrationStatus.Disabled)]
    [InlineData(FeatureState.Faulted, IntegrationProbe.Ok, IntegrationStatus.Error)]
    [InlineData(FeatureState.Running, IntegrationProbe.Ok, IntegrationStatus.Ready)]
    [InlineData(FeatureState.Running, IntegrationProbe.Unknown, IntegrationStatus.Ready)]
    [InlineData(FeatureState.Running, IntegrationProbe.Denied, IntegrationStatus.PermissionRequired)]
    [InlineData(FeatureState.Running, IntegrationProbe.Missing, IntegrationStatus.Unsupported)]
    public void Resolve_NamesTheFinalState(FeatureState state, IntegrationProbe probe, IntegrationStatus expected)
        => Assert.Equal(expected, IntegrationStatuses.Resolve(state, probe));

    [Fact]
    public void EveryStatus_HasALabel_AndEveryBlockedOneAnAction()
    {
        foreach (IntegrationStatus status in Enum.GetValues<IntegrationStatus>())
        {
            Assert.False(string.IsNullOrWhiteSpace(IntegrationStatuses.Label(status)));
            Assert.DoesNotContain("(s)", IntegrationStatuses.Label(status));

            if (status != IntegrationStatus.Ready)
            {
                Assert.False(string.IsNullOrWhiteSpace(IntegrationStatuses.Hint(status)), $"Pas d'action pour {status}.");
            }
        }
    }

    [Fact]
    public async Task ARunningMeetingFeature_WithoutACalendar_SaysItIsUnsupported()
    {
        var feature = new MeetingFeature(new ActivityManager(), new EventBus(), reader: null);
        await feature.StartAsync();

        Assert.Equal(IntegrationStatus.Unsupported, IntegrationStatuses.For(feature));

        await feature.StopAsync();
        Assert.Equal(IntegrationStatus.Disabled, IntegrationStatuses.For(feature));
    }
}
