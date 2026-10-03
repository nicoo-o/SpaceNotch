using System;

namespace SpaceNotch.Core.State;

/// <summary>
/// Machine d'état centrale de l'Island.
/// </summary>
public sealed class IslandStateManager
{
    private readonly object _lock = new();
    private readonly System.Collections.Generic.Queue<IslandStateChangedEventArgs> _outbox = new();
    private bool _delivering;
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
    ///
    /// <para>
    /// Les changements sont annoncés dans leur ordre, chacun à tous les abonnés
    /// avant le suivant (audit SN-22). Avant, un abonné qui changeait l'état
    /// depuis son gestionnaire faisait livrer la transition imbriquée avant que
    /// les abonnés suivants aient appris la première.
    /// </para>
    /// </summary>
    public bool TryTransitionTo(IslandState targetState)
    {
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

            _outbox.Enqueue(new IslandStateChangedEventArgs(_currentState, targetState));
            _currentState = targetState;
        }

        Deliver();
        return true;
    }

    private void Deliver()
    {
        lock (_lock)
        {
            if (_delivering)
            {
                return;
            }

            _delivering = true;
        }

        while (true)
        {
            IslandStateChangedEventArgs change;

            lock (_lock)
            {
                if (!_outbox.TryDequeue(out change!))
                {
                    _delivering = false;
                    return;
                }
            }

            try
            {
                StateChanged?.Invoke(this, change);
            }
            catch
            {
                lock (_lock)
                {
                    _delivering = false;
                }

                throw;
            }
        }
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
