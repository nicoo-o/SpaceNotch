using System;
using System.Threading;
using SpaceNotch.Core.Clipboard;
using SpaceNotch.Features.Clipboard;
using SpaceNotch.Platform.Windows.Clipboard;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// Pause du presse-papier (n° 3, P04 + P25) : une pause arrête réellement les écouteurs,
/// et reprend d'elle-même à l'échéance.
/// </summary>
public class ClipboardPauseTests
{
    /// <summary>Horloge et minuteur à la main : l'échéance se déclenche quand le test le décide.</summary>
    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 19, 10, 0, 0, TimeSpan.Zero);

        private readonly System.Collections.Generic.List<(TimerCallback Callback, object? State)> _timers = [];

        public override DateTimeOffset GetUtcNow() => Now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _timers.Add((callback, state));
            return new NoTimer();
        }

        public void Fire(int index = -1) => _timers[index < 0 ? _timers.Count - 1 : index].Callback(_timers[index < 0 ? _timers.Count - 1 : index].State);

        private sealed class NoTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    [Fact]
    public void APausedClipboard_StopsBothListeners_AndResumesOnDemand()
    {
        var pause = new ClipboardPause(new ManualTime());
        using var monitor = new ClipboardMonitor();
        int history = 0, assist = 0;

        using IDisposable a = ClipboardPauseListener.Attach(monitor, () => history++, pause);
        using IDisposable b = ClipboardPauseListener.Attach(monitor, () => assist++, pause);

        monitor.OnClipboardMessageReceived();
        Assert.Equal((1, 1), (history, assist));

        pause.PauseUntilResumed();
        monitor.OnClipboardMessageReceived();
        Assert.Equal((1, 1), (history, assist));

        pause.Resume();
        monitor.OnClipboardMessageReceived();
        Assert.Equal((2, 2), (history, assist));
    }

    [Fact]
    public void ATimedPause_EndsAtItsDeadline()
    {
        var time = new ManualTime();
        var pause = new ClipboardPause(time);
        int changes = 0;
        pause.Changed += () => changes++;

        pause.PauseFor(TimeSpan.FromMinutes(15));
        Assert.True(pause.IsPaused);
        Assert.Equal(time.Now.AddMinutes(15), pause.Until);

        time.Fire();
        Assert.False(pause.IsPaused);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void AStaleTimerCallback_DoesNotEndANewerPause()
    {
        var time = new ManualTime();
        var pause = new ClipboardPause(time);

        pause.PauseFor(TimeSpan.FromMinutes(15));
        pause.PauseUntilResumed();
        time.Fire(0);

        Assert.True(pause.IsPaused);
    }

    [Fact]
    public void ADisposedListener_NoLongerListens()
    {
        var pause = new ClipboardPause(new ManualTime());
        using var monitor = new ClipboardMonitor();
        int count = 0;

        ClipboardPauseListener.Attach(monitor, () => count++, pause).Dispose();
        monitor.OnClipboardMessageReceived();

        Assert.Equal(0, count);
    }
}
