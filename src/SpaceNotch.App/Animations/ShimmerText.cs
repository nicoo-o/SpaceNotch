using System;
using System.Runtime.CompilerServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
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
        public Storyboard? Story;
    }

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
                shine.Story?.Stop();
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

        var sweep = new DoubleAnimation
        {
            From = -1,
            To = 1,
            Duration = new Duration(TimeSpan.FromSeconds(1.6)),
            RepeatBehavior = RepeatBehavior.Forever,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(sweep, move);
        Storyboard.SetTargetProperty(sweep, "X");

        var story = new Storyboard();
        story.Children.Add(sweep);
        story.Begin();

        shine.Story = story;
        Shines.Add(target, shine);
    }
}
