using SpaceNotch.Core.State;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SpaceNotch.Core.Tests;

public class IslandStateManagerTests
{
    [Fact]
    public void StateManager_InitialState_IsClosed()
    {
        var manager = new IslandStateManager();
        Assert.Equal(IslandState.Closed, manager.CurrentState);
    }

    [Fact]
    public void StateManager_ValidTransition_Succeeds()
    {
        var manager = new IslandStateManager();

        bool transitioned = manager.TryTransitionTo(IslandState.Preview);
        Assert.True(transitioned);
        Assert.Equal(IslandState.Preview, manager.CurrentState);

        transitioned = manager.TryTransitionTo(IslandState.Expanding);
        Assert.True(transitioned);
        Assert.Equal(IslandState.Expanding, manager.CurrentState);

        transitioned = manager.TryTransitionTo(IslandState.Expanded);
        Assert.True(transitioned);
        Assert.Equal(IslandState.Expanded, manager.CurrentState);
    }

    [Fact]
    public void StateManager_InvalidTransition_IsRejected()
    {
        var manager = new IslandStateManager();

        // Closed vers Expanded direct n'est pas autorisé sans passer par Expanding
        bool transitioned = manager.TryTransitionTo(IslandState.Expanded);
        Assert.False(transitioned);
        Assert.Equal(IslandState.Closed, manager.CurrentState);
    }

    [Fact]
    public async Task StateManager_ConcurrentTransitions_NotifyInCommitOrder()
    {
        var manager = new IslandStateManager();
        using var firstNotificationStarted = new ManualResetEventSlim();
        using var releaseFirstNotification = new ManualResetEventSlim();
        using var secondTransitionStarted = new ManualResetEventSlim();
        using var secondNotificationDelivered = new ManualResetEventSlim();
        var notifications = new List<IslandState>();

        manager.StateChanged += (_, args) =>
        {
            notifications.Add(args.NewState);

            if (args.NewState == IslandState.Preview)
            {
                firstNotificationStarted.Set();
                releaseFirstNotification.Wait();
            }
            else if (args.NewState == IslandState.Expanding)
            {
                secondNotificationDelivered.Set();
            }
        };

        Task first = Task.Run(() => manager.TryTransitionTo(IslandState.Preview));
        Assert.True(firstNotificationStarted.Wait(TimeSpan.FromSeconds(1)));

        Task second = Task.Run(() =>
        {
            secondTransitionStarted.Set();
            return manager.TryTransitionTo(IslandState.Expanding);
        });
        Assert.True(secondTransitionStarted.Wait(TimeSpan.FromSeconds(1)));
        Assert.False(secondNotificationDelivered.Wait(TimeSpan.FromMilliseconds(100)));

        releaseFirstNotification.Set();
        await Task.WhenAll(first, second);

        Assert.Equal(new[] { IslandState.Preview, IslandState.Expanding }, notifications);
    }
}
