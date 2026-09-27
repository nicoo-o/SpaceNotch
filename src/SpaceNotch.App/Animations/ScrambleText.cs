using System;
using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using SpaceNotch.Core.Motion;

namespace SpaceNotch_App.Animations;

/// <summary>
/// Titres qui se décodent (A1) : quand un titre change vraiment — un nouveau
/// morceau, une nouvelle notification — ses lettres défilent en blocs de
/// pixels puis se fixent de gauche à droite, en 0,5 s. Le texte final est
/// gardé à part : les comparaisons et Narrateur lisent toujours le vrai titre.
/// </summary>
internal static class ScrambleText
{
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(33);

    private sealed class Run
    {
        public string Final = string.Empty;
        public DispatcherQueueTimer? Timer;
        public DateTime Start;
        public int Frame;
    }

    private static readonly ConditionalWeakTable<TextBlock, Run> Runs = new();

    /// <summary>Texte final d'un bloc, qu'il soit en train de se décoder ou non.</summary>
    public static string FinalOf(TextBlock target)
        => Runs.TryGetValue(target, out Run? run) && run.Timer is { IsRunning: true } ? run.Final : target.Text;

    /// <summary>
    /// Pose <paramref name="text"/> ; se décode si c'est un vrai changement et
    /// que les animations sont permises. Retourne vrai si l'effet joue.
    /// </summary>
    public static bool Set(TextBlock target, string text, bool animate)
    {
        ArgumentNullException.ThrowIfNull(target);
        string before = FinalOf(target);
        Run run = Runs.GetValue(target, _ => new Run());
        run.Timer?.Stop();
        run.Final = text;

        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(target, text);

        if (!animate || !TextScramble.ShouldPlay(before, text) || target.DispatcherQueue is null)
        {
            target.Text = text;
            return false;
        }

        run.Start = DateTime.UtcNow;
        run.Frame = 0;
        run.Timer ??= CreateTimer(target, run);
        target.Text = TextScramble.Frame(text, 0, 0);
        run.Timer.Start();
        return true;
    }

    private static DispatcherQueueTimer CreateTimer(TextBlock target, Run run)
    {
        DispatcherQueueTimer timer = target.DispatcherQueue.CreateTimer();
        timer.Interval = Frame;
        timer.Tick += (_, _) =>
        {
            double progress = (DateTime.UtcNow - run.Start).TotalSeconds / TextScramble.Seconds;
            run.Frame++;

            if (progress >= 1)
            {
                timer.Stop();
                target.Text = run.Final;
                return;
            }

            target.Text = TextScramble.Frame(run.Final, progress, run.Frame);
        };

        return timer;
    }
}
