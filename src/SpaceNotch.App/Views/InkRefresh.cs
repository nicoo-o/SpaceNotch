using System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using SpaceNotch.Core.Motion;

namespace SpaceNotch_App.Views;

/// <summary>
/// Rafraîchissement d'encre (A3) : quand une valeur change, seuls les
/// caractères touchés s'inversent un instant en cyan, comme sur une liseuse.
/// </summary>
public static class InkRefresh
{
    private static readonly SolidColorBrush Ink = new(Microsoft.UI.ColorHelper.FromArgb(255, 0x7F, 0xE6, 0xFF));
    private static readonly SolidColorBrush Paper = new(Microsoft.UI.Colors.Black);

    /// <summary>Pose <paramref name="text"/> ; si la valeur change, la partie changée s'inverse brièvement.</summary>
    public static void Set(TextBlock target, string text, bool animate)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(text);

        string previous = target.Text;
        target.Text = text;

        (int start, int length) = Afterglow.InkSpan(previous, text);

        if (!animate || length == 0)
        {
            return;
        }

        var highlighter = new TextHighlighter { Background = Ink, Foreground = Paper };
        highlighter.Ranges.Add(new TextRange { StartIndex = start, Length = length });
        target.TextHighlighters.Add(highlighter);

        Microsoft.UI.Dispatching.DispatcherQueueTimer timer = target.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(Afterglow.InkMilliseconds);
        timer.IsRepeating = false;
        timer.Tick += SpaceNotch_App.Diagnostics.Guard.Tick((_, _) => target.TextHighlighters.Remove(highlighter));
        timer.Start();
    }
}
