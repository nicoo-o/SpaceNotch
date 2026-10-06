using System;
using System.Runtime.CompilerServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Color = Windows.UI.Color;

namespace SpaceNotch_App.Animations;

/// <summary>
/// Reflet « en réflexion » (A3) : un reflet lumineux traverse le texte d'une
/// activité qui travaille — réflexion, analyse, téléchargement sans taille —
/// et s'arrête dès que le travail est fini. Le texte garde sa couleur ; seul
/// un étroit ruban plus clair glisse dessus.
/// </summary>
internal static class ShimmerText
{
    private sealed class Shine
    {
        public Brush? Original;
        public Microsoft.UI.Dispatching.DispatcherQueueTimer? Timer;
    }

    /// <summary>Durée d'un passage du ruban.</summary>
    private static readonly TimeSpan Sweep = TimeSpan.FromSeconds(1.6);

    /// <summary>
    /// Cadence du ruban : 30 images par seconde. Animé par un Storyboard, le
    /// reflet était une animation dépendante recalculée à chaque image de l'écran
    /// — 240 par seconde ici — et coûtait 19 % d'un cœur tant qu'un agent
    /// travaillait (mesure du 2026-10-06, n° 47). Un ruban qui glisse lentement
    /// ne gagne rien au-delà de 30.
    /// </summary>
    private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1.0 / 30);

    private static readonly ConditionalWeakTable<TextBlock, Shine> Shines = new();

    /// <summary>Allume ou éteint le reflet sur un texte.</summary>
    public static void Set(TextBlock target, bool working, bool animate)
    {
        ArgumentNullException.ThrowIfNull(target);
        Shines.TryGetValue(target, out Shine? shine);

        if (!working || !animate)
        {
            if (shine is not null)
            {
                shine.Timer?.Stop();
                target.Foreground = shine.Original;
                Shines.Remove(target);
            }

            return;
        }

        if (shine is not null)
        {
            return;
        }

        shine = new Shine { Original = target.Foreground };
        Color baseColor = target.Foreground is SolidColorBrush solid ? solid.Color : Colors.White;
        Color highlight = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);

        var move = new TranslateTransform { X = -1 };
        var brush = new LinearGradientBrush
        {
            StartPoint = new global::Windows.Foundation.Point(0, 0.5),
            EndPoint = new global::Windows.Foundation.Point(1, 0.5),
            RelativeTransform = move
        };
        brush.GradientStops.Add(new GradientStop { Color = baseColor, Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = baseColor, Offset = 0.4 });
        brush.GradientStops.Add(new GradientStop { Color = highlight, Offset = 0.5 });
        brush.GradientStops.Add(new GradientStop { Color = baseColor, Offset = 0.6 });
        brush.GradientStops.Add(new GradientStop { Color = baseColor, Offset = 1 });

        // Même motif de chaque côté : quand le ruban sort à droite, le texte
        // à gauche a déjà sa couleur, sans saut au recommencement.
        brush.SpreadMethod = GradientSpreadMethod.Pad;
        target.Foreground = brush;

        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        Microsoft.UI.Dispatching.DispatcherQueueTimer timer = target.DispatcherQueue.CreateTimer();
        timer.Interval = Frame;
        timer.IsRepeating = true;
        timer.Tick += (_, _) =>
        {
            // Un texte sorti de l'arbre sans Set(false) : le minuteur, retenu par
            // la file, tournerait sans fin. Éteint comme par Set(false), il
            // retrouve sa couleur et pourra se rallumer.
            if (target.XamlRoot is null)
            {
                Set(target, working: false, animate: false);
                return;
            }

            double phase = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalSeconds % Sweep.TotalSeconds / Sweep.TotalSeconds;
            move.X = -1 + (2 * phase);
        };
        timer.Start();

        shine.Timer = timer;
        Shines.Add(target, shine);
    }
}
