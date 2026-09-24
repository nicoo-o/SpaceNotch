using SpaceNotch.Core.State;
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
}
