using System;
using System.Collections.Generic;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;
using SpaceNotch_App.Views;
using SpaceNotch_App.Views.Scenes;

namespace SpaceNotch_App.Windows;

/// <summary>
/// Passage des yeux aux fonctions (vague 7) : quand une activité arrive au
/// repos, les yeux de Pixel ne disparaissent pas, ils deviennent un morceau de
/// ce qui arrive — deux barres d'égaliseur, les écouteurs du casque, les yeux
/// de Clawd, les boutons d'un appel… Quand elle s'en va, ce morceau redevient
/// les yeux. Les recettes sont dans <see cref="PixelHandoff"/> ; la fenêtre
/// les joue avec deux pixels libres de <c>FaceMorphLayer</c>.
/// </summary>
public sealed partial class IslandWindow
{
    /// <summary>Un pixel libre : centre, taille et rondeur, dans le repère de la couche.</summary>
    private readonly record struct Spot(double X, double Y, double W, double H, double R)
    {
        public static Spot Lerp(Spot a, Spot b, double t)
            => new(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t), a.W + ((b.W - a.W) * t), a.H + ((b.H - a.H) * t), a.R + ((b.R - a.R) * t));

        public Spot Move(double dx, double dy) => this with { X = X + dx, Y = Y + dy };
    }

    /// <summary>Un départ préparé avant que le rendu ne replie la pastille : où étaient les morceaux.</summary>
    private sealed record HandoffOut(HandoffRecipe Recipe, Spot Left, Spot Right, global::Windows.UI.Color Color);

    private IslandActivity? _handoffLast;
    private UIElement? _handoffView;
    private readonly TextBlock _measureAny = new();

    /// <summary>
    /// Appelée au début du rendu, avant que les vues ne changent : décide s'il
    /// faut jouer une arrivée (repos → activité) ou un départ (activité → repos).
    /// </summary>
    private void PrepareHandoff(IslandActivity? activity)
    {
        IslandActivity? previous = _handoffLast;
        _handoffLast = activity;

        if (!PixelAtRest || !UseSpringAnimations())
        {
            return;
        }

        // Arrivée : les yeux étaient là, une activité arrive. Pas pour le signal
        // de copie : geste et voyage (jusqu'à 940 ms) mangeraient un signal de 2,5 s.
        if (previous is null && activity is not null && _restFace == RestFace.Eyes && _morphTarget is null
            && !string.Equals(activity.Id, SpaceNotch.Features.Clipboard.ClipboardFeature.SignalActivityId, StringComparison.Ordinal)
            && _lastEyeOffsets is { } eyes)
        {
            EyeShape shape = _lastEyeShape;
            _ = _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => HandoffIn(activity, eyes, shape));
            return;
        }

        // Départ : l'activité s'en va, les morceaux sont relevés tant qu'ils sont visibles.
        if (previous is not null && activity is null && _controller.PresentedActivity is null
            && !string.Equals(previous.Id, SpaceNotch.Features.Clipboard.ClipboardFeature.SignalActivityId, StringComparison.Ordinal))
        {
            HandoffRecipe recipe = PixelHandoff.For(previous);

            if (SpotFor(recipe, recipe.Left, previous) is { } left && SpotFor(recipe, recipe.Right, previous) is { } right)
            {
                double cx = FaceMorphLayer.ActualWidth / 2, cy = FaceMorphLayer.ActualHeight / 2;
                var leaving = new HandoffOut(recipe, left.Move(-cx, -cy), right.Move(-cx, -cy), TintOf(recipe, previous));
                _ = _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => HandoffBack(leaving));
            }
        }
    }


    // ---- Arrivée -------------------------------------------------------------

    private void HandoffIn(IslandActivity activity, (global::Windows.Foundation.Point Left, global::Windows.Foundation.Point Right) eyes, EyeShape shape, int attempt = 0)
    {
        if (_isClosed || _controller.PresentedActivity?.Id != activity.Id)
        {
            return;
        }

        HandoffRecipe recipe = PixelHandoff.For(activity);
        CancelFaceMorph();

        UIElement? view = HandoffView(activity);

        if (view is null || SpotFor(recipe, recipe.Left, activity) is null)
        {
            // Clawd, une scène qui s'ouvre : pas encore mis en page. On réessaie un peu plus tard.
            if (attempt < 4)
            {
                RunAfter(TimeSpan.FromMilliseconds(50), () => HandoffIn(activity, eyes, shape, attempt + 1));
            }

            return;
        }

        _handoffView = view;

        // Le contenu arrive pendant que la notch s'ouvre, et non après le voyage
        // des pixels : caché jusque-là, il laissait 400 à 800 ms une forme large
        // et vide (constats du 2026-10-03). Les pixels voyagent par-dessus.
        // Une scène ouverte a déjà son entrée (RenderOnce) : la rejouer ici la
        // ramenait à zéro en plein fondu, après une relance de 50 ms.
        if (ReferenceEquals(view, SignalRestView) || ReferenceEquals(view, CardRestView))
        {
            SpaceNotch_App.Animations.ContentTransition.Play(view, UseSpringAnimations(), TimeSpan.FromMilliseconds(120), TimeSpan.FromMilliseconds(260));
        }

        global::Windows.UI.Color tint = TintOf(recipe, activity);
        (Border a, Border b) = MorphPixels(EyeColor);
        int generation = _morphGeneration;

        Spot Eye(global::Windows.Foundation.Point p) => new((FaceMorphLayer.ActualWidth / 2) + p.X, (FaceMorphLayer.ActualHeight / 2) + p.Y, shape.Width, shape.Height, shape.Roundness);
        Spot startL = Eye(eyes.Left), startR = Eye(eyes.Right);

        Lead(recipe, a, b, startL, startR, tint, (endL, endR) =>
        {
            // Le voyage : chaque image recalcule la cible, la notch grandit pendant ce temps.
            RunMorph(TimeSpan.FromMilliseconds(PixelHandoff.TravelMilliseconds), t =>
            {
                double k = EaseSpring(t);
                Spot toL = SpotFor(recipe, recipe.Left, activity) ?? endL;
                Spot toR = SpotFor(recipe, recipe.Right, activity) ?? endR;
                PlaceSpot(a, Spot.Lerp(endL, toL, k));
                PlaceSpot(b, Spot.Lerp(endR, toR, k));
                Tint(a, Blend(CurrentColor(a), tint, t));
                Tint(b, Blend(CurrentColor(b), tint, t));
            }, () => After(recipe, activity, a, b, generation));
        });
    }

    /// <summary>Le geste d'avant : regard, sursaut, clignement, remplissage…</summary>
    private void Lead(HandoffRecipe recipe, Border a, Border b, Spot l, Spot r, global::Windows.UI.Color tint, Action<Spot, Spot> next)
    {
        PlaceSpot(a, l);
        PlaceSpot(b, r);
        TimeSpan d = TimeSpan.FromMilliseconds(PixelHandoff.LeadMilliseconds(recipe.Lead));
        (double lx, double ly) = recipe.Lead switch
        {
            HandoffLead.LookDown => (0, PixelGaze.MaxLookY),
            HandoffLead.LookUp => (0, -PixelGaze.MaxLookY),
            HandoffLead.LookUpRight => (PixelGaze.MaxLookX, -PixelGaze.MaxLookY),
            HandoffLead.LookRight => (PixelGaze.MaxLookX, 0),
            _ => (0, 0)
        };

        switch (recipe.Lead)
        {
            case HandoffLead.None:
                next(l, r);
                return;

            case HandoffLead.LookDown or HandoffLead.LookUp or HandoffLead.LookUpRight or HandoffLead.LookRight:
                RunMorph(d, t =>
                {
                    PlaceSpot(a, l.Move(lx * t, ly * t));
                    PlaceSpot(b, r.Move(lx * t, ly * t));
                }, () => next(l.Move(lx, ly), r.Move(lx, ly)));
                return;

            case HandoffLead.Shake:
            {
                // Yeux ronds de surprise, et le tremblement du téléphone.
                EyeShape round = PixelGaze.Shape(PixelMood.Surprised);
                Spot rl = l with { W = round.Width, H = round.Width, R = 1 }, rr = r with { W = round.Width, H = round.Width, R = 1 };
                RunMorph(d, t =>
                {
                    double dx = 1.5 * Math.Sin(t * Math.PI * 8);
                    PlaceSpot(a, rl.Move(dx, 0));
                    PlaceSpot(b, rr.Move(dx, 0));
                }, () => next(rl, rr));
                return;
            }

            case HandoffLead.Shutter:
                RunMorph(d, t =>
                {
                    // Deux fermetures très brèves, comme un obturateur.
                    double phase = t * 2 % 1;
                    double h = phase < 0.4 ? Lerp(l.H, 1.2, Math.Sin(phase / 0.4 * Math.PI)) : l.H;
                    PlaceSpot(a, l with { H = h });
                    PlaceSpot(b, r with { H = h });
                }, () =>
                {
                    Flash();
                    next(l, r);
                });
                return;

            case HandoffLead.Close:
                RunMorph(d, t =>
                {
                    PlaceSpot(a, Spot.Lerp(l, l with { H = 2, W = 9, Y = l.Y + 1.5 }, t));
                    PlaceSpot(b, Spot.Lerp(r, r with { H = 2, W = 9, Y = r.Y + 1.5 }, t));
                }, () => next(l with { H = 2, W = 9, Y = l.Y + 1.5 }, r with { H = 2, W = 9, Y = r.Y + 1.5 }));
                return;

            case HandoffLead.Fill or HandoffLead.Tint:
                RunMorph(d, t =>
                {
                    Tint(a, Blend(EyeColor, tint, t));
                    Tint(b, Blend(EyeColor, tint, t));
                }, () => next(l, r));
                return;

            case HandoffLead.Worry:
            {
                Spot wl = l with { H = 5 }, wr = r with { H = 5 };
                RunMorph(d, t =>
                {
                    PlaceSpot(a, Spot.Lerp(l, wl, t));
                    PlaceSpot(b, Spot.Lerp(r, wr, t));
                    Rotate(a, 14 * t);
                    Rotate(b, -14 * t);
                    Tint(a, Blend(EyeColor, tint, t));
                    Tint(b, Blend(EyeColor, tint, t));
                }, () =>
                {
                    Rotate(a, 0);
                    Rotate(b, 0);
                    next(wl, wr);
                });
                return;
            }

            case HandoffLead.Arc:
            {
                // L'arceau du casque se dessine au-dessus des yeux, qui prennent sa couleur.
                var arc = new Border
                {
                    Width = 25,
                    Height = 12,
                    BorderThickness = new Thickness(2, 2, 2, 0),
                    BorderBrush = new SolidColorBrush(tint),
                    CornerRadius = new CornerRadius(12.5, 12.5, 0, 0),
                    Opacity = 0,
                    IsHitTestVisible = false
                };
                FaceMorphLayer.Children.Add(arc);
                Canvas.SetLeft(arc, ((l.X + r.X) / 2) - 12.5);
                Canvas.SetTop(arc, l.Y - (l.H / 2) - 7);
                RunMorph(d, t =>
                {
                    arc.Opacity = Math.Min(1, t * 1.6) * (t > 0.8 ? (1 - t) / 0.2 : 1);
                    Tint(a, Blend(EyeColor, tint, t));
                    Tint(b, Blend(EyeColor, tint, t));
                }, () =>
                {
                    FaceMorphLayer.Children.Remove(arc);
                    next(l, r);
                });
                return;
            }

            case HandoffLead.Stretch:
            {
                // Les yeux s'écartent en tendant une barre fine entre eux.
                var bar = new Border { Height = 1.5, Background = new SolidColorBrush(tint), Opacity = 0.6, IsHitTestVisible = false };
                FaceMorphLayer.Children.Add(bar);
                Spot sl = l with { W = 3, H = 3, R = 1 }, sr = r with { W = 3, H = 3, R = 1 };
                RunMorph(d, t =>
                {
                    Spot nl = Spot.Lerp(l, sl.Move(-6, 0), t), nr = Spot.Lerp(r, sr.Move(6, 0), t);
                    PlaceSpot(a, nl);
                    PlaceSpot(b, nr);
                    bar.Width = Math.Max(0, nr.X - nl.X);
                    Canvas.SetLeft(bar, nl.X);
                    Canvas.SetTop(bar, nl.Y - 0.75);
                }, () =>
                {
                    FaceMorphLayer.Children.Remove(bar);
                    next(sl.Move(-6, 0), sr.Move(6, 0));
                });
                return;
            }

            default:
                next(l, r);
                return;
        }
    }

    /// <summary>Une fois arrivés : ils se fondent dans ce qui s'allume, lisent, tournent autour de l'anneau…</summary>
    private void After(HandoffRecipe recipe, IslandActivity activity, Border a, Border b, int generation)
    {
        void Done()
        {
            if (generation != _morphGeneration)
            {
                return;
            }

            if (_handoffView is { } view)
            {
                view.Opacity = 1;
            }

            RunMorph(TimeSpan.FromMilliseconds(140), t =>
            {
                a.Opacity = 1 - t;
                b.Opacity = 1 - t;
            }, () =>
            {
                RemoveMorphPixels();
                _handoffView = null;
            });
        }

        if (_handoffView is { } shown)
        {
            shown.Opacity = 1;
        }

        switch (recipe.After)
        {
            case HandoffAfter.Read when HandoffElementsOf(activity).Title is { } title:
            {
                // Les yeux lisent le titre de gauche à droite.
                global::Windows.Foundation.Rect r = LayerBounds(title);
                double width = Math.Min(r.Width, Measure(_measureAny, title.Text, title));
                // Le titre est déjà là (il est arrivé avec la forme) : les yeux le
                // parcourent sans le découper — découpé, il apparaissait puis s'effaçait.
                double y = r.Y + (r.Height / 2);
                RunMorph(TimeSpan.FromMilliseconds(900), t =>
                {
                    double x = r.X + (width * t);
                    PlaceSpot(a, new Spot(x - 2, y, 3, 4, 0.3));
                    PlaceSpot(b, new Spot(x + 3, y, 3, 4, 0.3));
                }, Done);
                return;
            }

            case HandoffAfter.Orbit when HandoffElementsOf(activity).Trailing is { } ring:
            {
                // L'œil droit fait le tour de l'anneau du compte à rebours.
                global::Windows.Foundation.Rect r = LayerBounds(ring);
                double cx = r.X + (r.Width / 2), cy = r.Y + (r.Height / 2), radius = (r.Width / 2) - 1;
                RunMorph(TimeSpan.FromMilliseconds(700), t =>
                {
                    double angle = (-Math.PI / 2) + (t * 2 * Math.PI * 0.8);
                    PlaceSpot(b, new Spot(cx + (radius * Math.Cos(angle)), cy + (radius * Math.Sin(angle)), 2.6, 2.6, 1));
                }, Done);
                return;
            }

            case HandoffAfter.Caret when HandoffElementsOf(activity).Glyph is null:
            {
                // La note : l'œil gauche glisse dans le curseur et s'y fond, rien ne reste sur le texte.
                Spot from = SpotOf(a), into = SpotOf(b);
                RunMorph(TimeSpan.FromMilliseconds(180), t =>
                {
                    double k = EaseSpring(t);
                    PlaceSpot(a, Spot.Lerp(from, into, k));
                    a.Opacity = 1 - (t * 0.6);
                }, Done);
                return;
            }

            default:
                Done();
                return;
        }
    }

    /// <summary>La place actuelle d'un pixel de passage, dans le repère de la couche.</summary>
    private static Spot SpotOf(Border pixel)
        => new(Canvas.GetLeft(pixel) + (pixel.Width / 2), Canvas.GetTop(pixel) + (pixel.Height / 2), pixel.Width, pixel.Height, pixel.CornerRadius.TopLeft / Math.Max(1, Math.Min(pixel.Width, pixel.Height) / 2));

    // ---- Départ --------------------------------------------------------------

    private void HandoffBack(HandoffOut leaving)
    {
        if (_isClosed || _controller.PresentedActivity is not null || _restFace != RestFace.Eyes || RestEyes.Visibility != Visibility.Visible)
        {
            return;
        }

        CancelFaceMorph();

        // Le repos qui suit une scène entre en fondu (RenderOnce) ; ici, ce sont
        // les pixels qui révèlent les yeux : leur conteneur doit déjà être là.
        SpaceNotch_App.Animations.ContentTransition.Settle(IdleRestView);
        RestEyes.Opacity = 0;
        (Border a, Border b) = MorphPixels(leaving.Color);
        int generation = _morphGeneration;
        _morphTarget = RestFace.Eyes;
        EyeShape shape = RestEyes.CurrentShape;

        RunMorph(TimeSpan.FromMilliseconds(PixelHandoff.ReturnMilliseconds), t =>
        {
            double cx = FaceMorphLayer.ActualWidth / 2, cy = FaceMorphLayer.ActualHeight / 2;
            (global::Windows.Foundation.Point l, global::Windows.Foundation.Point r) = RestEyes.Centers(FaceMorphLayer);
            double k = EaseSpring(t);
            PlaceSpot(a, Spot.Lerp(leaving.Left.Move(cx, cy), new Spot(l.X, l.Y, shape.Width, shape.Height, shape.Roundness), k));
            PlaceSpot(b, Spot.Lerp(leaving.Right.Move(cx, cy), new Spot(r.X, r.Y, shape.Width, shape.Height, shape.Roundness), k));
            Tint(a, Blend(leaving.Color, EyeColor, t));
            Tint(b, Blend(leaving.Color, EyeColor, t));
        }, () =>
        {
            if (generation != _morphGeneration)
            {
                return;
            }

            _morphTarget = null;
            RemoveMorphPixels();
            RestEyes.Opacity = 1;
        });
    }

    // ---- Où sont les choses --------------------------------------------------

    private sealed record HandoffElements(FrameworkElement? Glyph, ClawdView? Clawd, FrameworkElement? Trailing, TextBlock? Title, IReadOnlyList<FrameworkElement> Actions, FrameworkElement? Field);

    /// <summary>Les éléments visibles de l'activité présentée : la pastille, ou la scène ouverte.</summary>
    private HandoffElements HandoffElementsOf(IslandActivity activity)
    {
        bool expanded = _controller.State is IslandState.Expanded or IslandState.Expanding;

        if (expanded && _scenes.TryGetValue(activity.SceneKey, out IIslandSceneView? scene))
        {
            return scene switch
            {
                InfoScene info => new(info.ClawdElement is null ? info.IconElement : null, info.ClawdElement, null, info.TitleElement, info.ActionElements, null),
                LauncherScene launcher => new(launcher.SearchIcon, null, null, null, [], launcher.SearchField),
                NoteScene note => new(null, null, null, null, [], note.Field),
                _ => new(scene.AnchorFor(MorphAnchorKind.Icon), null, null, null, [], null)
            };
        }

        if (SignalRestView.Visibility == Visibility.Visible)
        {
            FrameworkElement glyph = SignalHypnoticHost.Visibility == Visibility.Visible ? SignalHypnoticHost : SignalGlyph;
            return new(SignalClawd.Visibility == Visibility.Visible ? null : glyph, SignalClawd.Visibility == Visibility.Visible ? SignalClawd : null, SignalTrailing.Visibility == Visibility.Visible ? SignalTrailing : null, SignalLabel, [], null);
        }

        if (CardRestView.Visibility == Visibility.Visible)
        {
            FrameworkElement glyph = CardHypnoticHost.Visibility == Visibility.Visible ? CardHypnoticHost : CardGlyph;
            return new(CardClawd.Visibility == Visibility.Visible ? null : glyph, CardClawd.Visibility == Visibility.Visible ? CardClawd : null, CardTrailing.Visibility == Visibility.Visible ? CardTrailing : null, CardHeadline, [], null);
        }

        return new(null, null, null, null, [], null);
    }

    /// <summary>Ce qui apparaît : la pastille, ou la scène ouverte.</summary>
    private UIElement? HandoffView(IslandActivity activity)
    {
        if (_controller.State is IslandState.Expanded or IslandState.Expanding)
        {
            return _scenes.TryGetValue(activity.SceneKey, out IIslandSceneView? scene) ? scene.Root : null;
        }

        return SignalRestView.Visibility == Visibility.Visible ? SignalRestView
            : CardRestView.Visibility == Visibility.Visible ? CardRestView
            : null;
    }

    /// <summary>La place d'un œil selon la recette, dans le repère de la couche ; null si l'ancre n'est pas là.</summary>
    private Spot? SpotFor(HandoffRecipe recipe, HandoffSpot spot, IslandActivity activity)
    {
        HandoffElements e = HandoffElementsOf(activity);

        switch (recipe.Anchor)
        {
            case HandoffAnchor.Clawd when e.Clawd is { ActualWidth: > 0 } clawd:
            {
                global::Windows.Foundation.Rect r = LayerBounds(clawd);
                double k = clawd.Pitch;
                return new Spot(r.X + (spot.X * k), r.Y + (spot.Y * k), spot.Width * k, spot.Height * k, spot.Roundness);
            }

            case HandoffAnchor.Trailing when e.Trailing is { ActualWidth: > 0 } trailing:
            {
                global::Windows.Foundation.Rect r = LayerBounds(trailing);
                return new Spot(r.X + (r.Width / 2) + spot.X, r.Y + (r.Height / 2) + spot.Y, spot.Width, spot.Height, spot.Roundness);
            }

            case HandoffAnchor.Colon when e.Title is { ActualWidth: > 0 } title && title.Text.IndexOf(':', StringComparison.Ordinal) is var i and >= 0:
            {
                global::Windows.Foundation.Rect r = LayerBounds(title);
                double x = r.X + Measure(_measureAny, title.Text[..i], title) + (Measure(_measureAny, ":", title) / 2);
                return new Spot(x + spot.X, r.Y + (r.Height / 2) + spot.Y, spot.Width, spot.Height, spot.Roundness);
            }

            case HandoffAnchor.Title when e.Title is { ActualWidth: > 0 } title:
            {
                global::Windows.Foundation.Rect r = LayerBounds(title);
                return new Spot(r.X + spot.X, r.Y + (r.Height / 2) + spot.Y, spot.Width, spot.Height, spot.Roundness);
            }

            case HandoffAnchor.Actions when e.Actions.Count > (int)spot.X && e.Actions[(int)spot.X] is { ActualWidth: > 0 } button:
            {
                global::Windows.Foundation.Rect r = LayerBounds(button);
                return new Spot(r.X + (r.Width / 2), r.Y + (r.Height / 2), r.Width, r.Height, 1);
            }

            case HandoffAnchor.Field when spot == recipe.Left && e.Glyph is { ActualWidth: > 0 } lens:
            {
                // La recherche : l'œil gauche se pose sur la loupe, pas sur le texte d'invite.
                global::Windows.Foundation.Rect r = LayerBounds(lens);
                return new Spot(r.X + (r.Width / 2), r.Y + (r.Height / 2), spot.Width, spot.Height, spot.Roundness);
            }

            case HandoffAnchor.Field when e.Field is { ActualWidth: > 0 } field:
            {
                global::Windows.Foundation.Rect r = LayerBounds(field);
                return new Spot(TextStart(field, r) + spot.X, r.Y + (r.Height / 2) + spot.Y, spot.Width, spot.Height, spot.Roundness);
            }

            default:
            {
                // Le glyphe 7 × 7, ou l'ancre de repli quand celle de la recette manque.
                FrameworkElement? glyph = e.Glyph ?? e.Clawd ?? e.Field ?? (FrameworkElement?)e.Title;

                if (glyph is not { ActualWidth: > 0 })
                {
                    return null;
                }

                global::Windows.Foundation.Rect r = LayerBounds(glyph);
                double size = glyph is GlyphView g ? g.Size : Math.Min(Math.Min(r.Width, r.Height), 20);
                double k = size / 7;
                double ox = r.X + ((r.Width - size) / 2), oy = r.Y + ((r.Height - size) / 2);
                HandoffSpot s = recipe.Anchor == HandoffAnchor.Glyph ? spot : new HandoffSpot(spot == recipe.Left ? 2 : 4, 3, 1, 1);
                return new Spot(ox + ((s.X + 0.5) * k), oy + ((s.Y + 0.5) * k), s.Width * k, s.Height * k, s.Roundness);
            }
        }
    }

    /// <summary>
    /// Où commence le texte d'un champ, là où se tient son curseur : les champs
    /// n'ont pas tous la même marge intérieure (la note est plus serrée que la recherche).
    /// </summary>
    private static readonly string[] TextParts = ["PlaceholderTextContentPresenter", "ContentElement"];

    private double TextStart(FrameworkElement field, global::Windows.Foundation.Rect bounds)
    {
        foreach (string part in TextParts)
        {
            if (FindNamed(field, part) is { ActualWidth: > 0 } inner)
            {
                double padding = inner is Control control ? control.Padding.Left : 0;
                return LayerBounds(inner).X + padding;
            }
        }

        return bounds.X + 12;
    }

    private static FrameworkElement? FindNamed(DependencyObject root, string name)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);

        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);

            if (child is FrameworkElement element && element.Name == name)
            {
                return element;
            }

            if (FindNamed(child, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private global::Windows.Foundation.Rect LayerBounds(FrameworkElement element)
        => element.TransformToVisual(FaceMorphLayer).TransformBounds(new global::Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight));

    /// <summary>Largeur d'un texte avec la police d'un autre.</summary>
    private static double Measure(TextBlock measure, string text, TextBlock like)
    {
        measure.FontFamily = like.FontFamily;
        measure.FontSize = like.FontSize;
        measure.FontWeight = like.FontWeight;
        return Measure(measure, text);
    }

    // ---- Dessin --------------------------------------------------------------

    private static void PlaceSpot(Border pixel, Spot s)
    {
        pixel.Width = Math.Max(0.5, s.W);
        pixel.Height = Math.Max(0.5, s.H);
        pixel.CornerRadius = new CornerRadius(Math.Min(s.W, s.H) / 2 * Math.Clamp(s.R, 0, 1));
        Canvas.SetLeft(pixel, s.X - (s.W / 2));
        Canvas.SetTop(pixel, s.Y - (s.H / 2));
    }

    private static void Rotate(Border pixel, double degrees)
    {
        pixel.RenderTransformOrigin = new global::Windows.Foundation.Point(0.5, 0.5);
        pixel.RenderTransform = new RotateTransform { Angle = degrees };
    }

    private static global::Windows.UI.Color CurrentColor(Border pixel)
        => pixel.Background is SolidColorBrush brush ? brush.Color : CyanColor;

    private static global::Windows.UI.Color Blend(global::Windows.UI.Color a, global::Windows.UI.Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        static byte Mix(byte x, byte y, double k) => (byte)Math.Round(x + ((y - x) * k));
        return global::Windows.UI.Color.FromArgb(Mix(a.A, b.A, t), Mix(a.R, b.R, t), Mix(a.G, b.G, t), Mix(a.B, b.B, t));
    }

    /// <summary>La teinte d'arrivée : celle de la recette, ou celle de l'activité.</summary>
    private static global::Windows.UI.Color TintOf(HandoffRecipe recipe, IslandActivity activity)
    {
        if (recipe.Tint is null)
        {
            return CyanColor;
        }

        if (recipe.Tint == PixelHandoff.ActivityTint)
        {
            return activity.Tint is { } t ? global::Windows.UI.Color.FromArgb(0xFF, t.R, t.G, t.B) : Microsoft.UI.ColorHelper.FromArgb(0xEB, 0xFF, 0xFF, 0xFF);
        }

        string hex = recipe.Tint.TrimStart('#');
        uint v = uint.Parse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
        return hex.Length == 8
            ? global::Windows.UI.Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v)
            : global::Windows.UI.Color.FromArgb(0xFF, (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    /// <summary>L'éclair blanc de l'obturateur (texte copié).</summary>
    private void Flash()
    {
        // Un éclair doux, aux coins arrondis, qui ne déborde pas de la notch.
        var flash = new Border
        {
            Width = Math.Max(0, FaceMorphLayer.ActualWidth - 16),
            Height = Math.Max(0, FaceMorphLayer.ActualHeight - 8),
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Microsoft.UI.Colors.White),
            Opacity = 0.35,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(flash, 8);
        Canvas.SetTop(flash, 2);
        FaceMorphLayer.Children.Insert(0, flash);
        DateTime start = DateTime.UtcNow;
        EventHandler<object>? frame = null;
        frame = (_, _) =>
        {
            double t = (DateTime.UtcNow - start).TotalMilliseconds / 260;
            flash.Opacity = Math.Max(0, 0.35 * (1 - t));

            if (t >= 1)
            {
                SpaceNotch_App.Animations.FrameClock.Rendering -= frame;
                FaceMorphLayer.Children.Remove(flash);
            }
        };
        SpaceNotch_App.Animations.FrameClock.Rendering += frame;
    }
}
