using System;

namespace NotchFlow.Core.State;

/// <summary>
/// Machine d'état centrale de l'Island.
/// </summary>
public sealed class IslandStateManager
{
    private readonly object _lock = new();
    private IslandState _currentState = IslandState.Closed;

    public IslandState CurrentState
    {
        get
        {
            lock (_lock)
            {
                return _currentState;
            }
        }
    }

    public event EventHandler<IslandStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Tente d'effectuer une transition d'état.
    /// </summary>
    public bool TryTransitionTo(IslandState targetState)
    {
        IslandState oldState;

        lock (_lock)
        {
            if (_currentState == targetState)
            {
                return false;
            }

            if (!IsValidTransition(_currentState, targetState))
            {
                return false;
            }

            oldState = _currentState;
            _currentState = targetState;
        }

        StateChanged?.Invoke(this, new IslandStateChangedEventArgs(oldState, targetState));
        return true;
    }

    private static bool IsValidTransition(IslandState current, IslandState target)
    {
        return (current, target) switch
        {
            (IslandState.Closed, IslandState.Preview) => true,
            (IslandState.Closed, IslandState.Expanding) => true,
            (IslandState.Preview, IslandState.Closed) => true,
            (IslandState.Preview, IslandState.Expanding) => true,
            (IslandState.Expanding, IslandState.Expanded) => true,
            (IslandState.Expanding, IslandState.Collapsing) => true,
            (IslandState.Expanded, IslandState.Collapsing) => true,
            (IslandState.Collapsing, IslandState.Closed) => true,
            (IslandState.Collapsing, IslandState.Expanding) => true,
            _ => false
        };
    }
}

public sealed class IslandStateChangedEventArgs : EventArgs
{
    public IslandState OldState { get; }
    public IslandState NewState { get; }

    public IslandStateChangedEventArgs(IslandState oldState, IslandState newState)
    {
        OldState = oldState;
        NewState = newState;
    }
}
