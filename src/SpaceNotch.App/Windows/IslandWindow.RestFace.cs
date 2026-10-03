using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;
using SpaceNotch_App.Views;

namespace SpaceNotch_App.Windows;

/// <summary>
/// Les deux visages du repos : les yeux de Pixel, et l'heure avec la météo.
///
/// <list type="bullet">
/// <item>Au survol (0,5 s), et après 30 s sans souris ni clavier, l'heure
/// s'installe ; au départ du curseur, ou au premier mouvement, les yeux
/// reviennent.</item>
/// <item>Transition C : les yeux se rapprochent, rétrécissent en deux points et
/// pivotent d'un quart de tour : ils sont les deux-points de l'heure, et les
/// chiffres se déplient de part et d'autre. Le retour joue l'inverse.</item>
/// <item>Assoupi, Pixel baisse le regard et ferme les yeux avant que l'heure ne
/// s'installe ; réveillé, il sursaute (yeux ronds 1,4 s).</item>
/// </list>
/// </summary>
public sealed partial class IslandWindow
{
    /// <summary>Inactivité avant que l'heure ne remplace les yeux.</summary>
    private static readonly TimeSpan DozeAfter = TimeSpan.FromSeconds(30);

    /// <summary>Le temps de fermer les yeux avant que l'heure ne s'installe.</summary>
    private static readonly TimeSpan DozeClose = TimeSpan.FromMilliseconds(700);

    /// <summary>
    /// Pose du survol, au repos, avant l'heure : un passage n'est pas une
    /// intention. 0,35 s : les yeux répondent déjà au survol (moins de 0,1 s),
    /// et la transition C ajoute plus d'une demi-seconde ; à 0,5 s, l'heure
    /// mettait plus d'une seconde à se lire.
    /// </summary>
    private static readonly TimeSpan RestPreviewDwell = TimeSpan.FromMilliseconds(350);

    /// <summary>
    /// L'heure reste ce temps après le départ du curseur : de quoi finir de la
    /// lire, sans qu'elle s'attarde quand la main est déjà ailleurs (2 s avant).
    /// </summary>
    private static readonly TimeSpan RestPreviewGrace = TimeSpan.FromMilliseconds(1200);

    /// <summary>Durées de la transition C.</summary>
    private static readonly TimeSpan MorphGather = TimeSpan.FromMilliseconds(300);

    private static readonly TimeSpan MorphTurn = TimeSpan.FromMilliseconds(240);

    private static readonly TimeSpan MorphOpen = TimeSpan.FromMilliseconds(360);

    /// <summary>Teinte des chiffres (NfTextSecondaryBrush) : les deux points prennent la leur.</summary>
    private static readonly global::Windows.UI.Color InkColor = Microsoft.UI.ColorHelper.FromArgb(0x99, 0xFF, 0xFF, 0xFF);

    private static readonly global::Windows.UI.Color CyanColor = Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x7F, 0xE6, 0xFF);

    private enum RestFace
    {
        Eyes,
        Clock
    }

    private RestFace _restFace = RestFace.Eyes;
    private DispatcherQueueTimer? _dozeTimer;
    private DispatcherQueueTimer? _dozeCloseTimer;

    /// <summary>Pixel ferme les yeux : l'heure arrive dans un instant.</summary>
    private bool _eyesClosing;

    /// <summary>L'heure est installée faute d'activité de l'utilisateur.</summary>
    private bool _dozing;

    /// <summary>Le retour aux yeux vient d'un réveil : Pixel sursaute à l'arrivée.</summary>
    private bool _wakeSurprise;

    /// <summary>Visite : l'assoupissement imposé, sans attendre 30 s.</summary>
    private bool? _tourDoze;

    /// <summary>Les deux pixels qui vont des yeux aux deux-points, et l'animation en cours.</summary>
    private Border? _morphLeft;
    private Border? _morphRight;
    private EventHandler<object>? _morphFrame;
    private int _morphGeneration;

    /// <summary>Le visage que le repos devrait montrer maintenant.</summary>
    private RestFace WantedRestFace()
        => PixelAtRest && _controller.PresentedActivity is null
            && (_dozing || _controller.State == IslandState.Preview)
            ? RestFace.Clock
            : RestFace.Eyes;

    /// <summary>Forme du repos quand l'heure s'y installe sans survol (assoupi).</summary>
    private IslandFootprint? DozingFootprint()
    {
        if (!_dozing || !PixelAtRest || _controller.PresentedActivity is not null)
        {
            return null;
        }

        // Toujours depuis la forme du repos, jamais depuis la forme actuelle :
        // sinon chaque rendu agrandirait la notch d'un cinquième.
        IslandFootprint preview = IslandFootprint.PreviewOf(IslandPresentationTier.Idle, IslandFootprint.Idle);
        return WeatherAtRest
            ? new IslandFootprint(Math.Max(preview.Width, WeatherPreviewWidth), Math.Max(preview.Height, 30))
            : preview;
    }

    /// <summary>Arme la surveillance de l'inactivité tant que le repos est montré.</summary>
    private void ArmDozeWatch(bool atRest)
    {
        if (!atRest || !PixelAtRest || _sessionLocked)
        {
            _dozeTimer?.Stop();
            return;
        }

        _dozeTimer ??= CreateRepeatingTimer(TimeSpan.FromMilliseconds(400), CheckDoze);

        if (!_dozeTimer.IsRunning)
        {
            _dozeTimer.Start();
        }
    }

    private void CheckDoze()
    {
        if (_controller.PresentedActivity is not null || !PixelAtRest)
        {
            return;
        }

        // Filet (v1.16.1) : une transition yeux ↔ heure qui ne s'achève pas
        // laissait les yeux invisibles et l'heure repliée — une notch vide.
        if (_morphTarget is not null && DateTime.UtcNow - _morphStartedAt > MorphTimeout)
        {
            SpaceNotch.Infrastructure.Logging.MiniLogger.Log("[REPOS] transition du visage bloquée : les yeux sont remis");
            CancelFaceMorph();
            IdleClock.UnfoldAll();
            _restFace = RestFace.Eyes;
            _restFaceShown = false;
            RequestRender();
        }

        bool idle = _tourDoze ?? (!_touring && LastInputIdle() >= DozeAfter);

        if (idle && !_dozing && !_eyesClosing)
        {
            // Pixel baisse le regard et ferme les yeux ; l'heure suit.
            _eyesClosing = true;
            PixelTick();
            _dozeCloseTimer ??= CreateOneShotTimer(DozeClose, () =>
            {
                if (!_eyesClosing)
                {
                    return;
                }

                _eyesClosing = false;
                _dozing = true;
                RequestRender();
            });
            _dozeCloseTimer.Stop();
            _dozeCloseTimer.Start();
            return;
        }

        if (!idle && (_dozing || _eyesClosing))
        {
            bool wasDozing = _dozing;
            _eyesClosing = false;
            _dozing = false;
            _dozeCloseTimer?.Stop();
            _wakeSurprise = wasDozing && !PixelGaze.IsNight(_pixelNight ?? TimeOnly.FromDateTime(DateTime.Now));
            PixelTick();
            RequestRender();
        }
    }

    /// <summary>
    /// Applique le visage voulu au repos, avec la transition C quand il change.
    /// Appelée par le rendu, dans la branche « aucune activité », après les
    /// visibilités par défaut.
    /// </summary>
    private void ApplyRestFace()
    {
        RestFace wanted = WantedRestFace();

        if (!PixelAtRest)
        {
            _restFace = RestFace.Eyes;
            return;
        }

        // Une transition est en route vers ce visage : le rendu ne la relance pas,
        // il remet seulement ce qu'elle montre.
        if (_morphTarget == wanted)
        {
            if (wanted == RestFace.Clock)
            {
                ShowRestPixel(false);
                IdleStatusDot.Visibility = Visibility.Collapsed;
                IdleClock.Visibility = Visibility.Visible;
                ShowRestWeather();

                // La météo attend que les chiffres soient dépliés.
                if (_morphLeft is not null)
                {
                    SetWeatherOpacity(0);
                }
            }
            else
            {
                IdleClock.Visibility = Visibility.Visible;
            }

            return;
        }

        bool changed = wanted != _restFace;
        bool animate = changed && UseSpringAnimations() && _restFaceShown;
        _restFaceShown = true;

        if (wanted == RestFace.Clock)
        {
            // Point de départ des deux pixels : là où sont les yeux, avant de les cacher.
            (global::Windows.Foundation.Point Left, global::Windows.Foundation.Point Right)? eyes = animate && _morphTarget is null ? _lastEyeOffsets : null;
            EyeShape shape = _lastEyeShape;

            ShowRestPixel(false);
            IdleStatusDot.Visibility = Visibility.Collapsed;
            IdleClock.Visibility = Visibility.Visible;
            ShowRestWeather();
            _restFace = RestFace.Clock;

            if (eyes is { } from)
            {
                EyesToColon(from, shape);
            }
            else if (changed)
            {
                CancelFaceMorph();
                IdleClock.UnfoldAll();
            }

            return;
        }

        // Les yeux : l'heure ne reste que si elle est réglée pour le repos.
        if (changed && animate)
        {
            ColonToEyes();
            return;
        }

        if (changed)
        {
            CancelFaceMorph();
            IdleClock.UnfoldAll();
            RestEyes.Opacity = 1;
        }

        _restFace = RestFace.Eyes;
        FinishWake();
    }

    /// <summary>Visage vers lequel une transition est en route, ou null.</summary>
    private RestFace? _morphTarget;

    /// <summary>Début de la transition en cours, pour le filet de <see cref="CheckDoze"/>.</summary>
    private DateTime _morphStartedAt;

    /// <summary>Une transition complète dure moins d'une seconde et demie.</summary>
    private static readonly TimeSpan MorphTimeout = TimeSpan.FromSeconds(3);

    /// <summary>Où étaient les yeux au dernier regard, et leur forme : la transition part de là.</summary>
    private (global::Windows.Foundation.Point Left, global::Windows.Foundation.Point Right)? _lastEyeOffsets;

    private EyeShape _lastEyeShape = PixelGaze.Shape(PixelMood.Awake);

    /// <summary>Relevé à chaque regard (80 ms) : le rendu cache les yeux avant de savoir s'il faut les animer.</summary>
    private void TrackEyes()
    {
        if (_morphTarget is null && RestEyes.Visibility == Visibility.Visible && RestEyes.Opacity > 0)
        {
            _lastEyeOffsets = EyeOffsets();
            _lastEyeShape = RestEyes.CurrentShape;
        }
    }

    /// <summary>Une activité passe devant : le repos repartira de ses yeux, sans transition.</summary>
    private void ResetRestFace()
    {
        if (_morphTarget is not null || _restFace != RestFace.Eyes)
        {
            CancelFaceMorph();
            IdleClock.UnfoldAll();
        }

        _restFace = RestFace.Eyes;
        _restFaceShown = false;
        _lastEyeOffsets = null;
        _dozeTimer?.Stop();
    }

    /// <summary>Vrai dès que le repos a été montré une fois : le premier affichage ne s'anime pas.</summary>
    private bool _restFaceShown;

    /// <summary>Centre de chaque œil, depuis le centre de la couche de transition.</summary>
    private (global::Windows.Foundation.Point Left, global::Windows.Foundation.Point Right)? EyeOffsets()
    {
        if (RestEyes.Visibility != Visibility.Visible || FaceMorphLayer.ActualWidth <= 0)
        {
            return null;
        }

        (global::Windows.Foundation.Point l, global::Windows.Foundation.Point r) = RestEyes.Centers(FaceMorphLayer);
        double cx = FaceMorphLayer.ActualWidth / 2, cy = FaceMorphLayer.ActualHeight / 2;
        return (new(l.X - cx, l.Y - cy), new(r.X - cx, r.Y - cy));
    }

    /// <summary>Centre des deux-points, depuis le centre de la couche de transition.</summary>
    private global::Windows.Foundation.Point? ColonOffset()
    {
        IdleRestView.UpdateLayout();

        if (IdleClock.ColonCenter(FaceMorphLayer) is not { } colon || FaceMorphLayer.ActualWidth <= 0)
        {
            return null;
        }

        return new(colon.X - (FaceMorphLayer.ActualWidth / 2), colon.Y - (FaceMorphLayer.ActualHeight / 2));
    }

    /// <summary>Les yeux deviennent les deux-points, puis les chiffres se déplient.</summary>
    private void EyesToColon((global::Windows.Foundation.Point Left, global::Windows.Foundation.Point Right) from, EyeShape shape)
    {
        CancelFaceMorph();
        IdleClock.FoldAll();
        SetWeatherOpacity(0);

        if (ColonOffset() is not { } colon)
        {
            IdleClock.UnfoldAll();
            SetWeatherOpacity(1);
            return;
        }

        (Border a, Border b) = MorphPixels(EyeColor);
        double r = PixelClockView.ColonHalfGap, dot = PixelClockView.ColonDot;
        int generation = _morphGeneration;
        _morphTarget = RestFace.Clock;
        _morphStartedAt = DateTime.UtcNow;

        // 1. Ils se rapprochent et deviennent deux points, côte à côte.
        RunMorph(MorphGather, t =>
        {
            double k = EaseGlide(t);
            Place(a, Lerp(from.Left.X, colon.X - r, k), Lerp(from.Left.Y, colon.Y, k), Lerp(shape.Width, dot, k), Lerp(shape.Height, dot, k));
            Place(b, Lerp(from.Right.X, colon.X + r, k), Lerp(from.Right.Y, colon.Y, k), Lerp(shape.Width, dot, k), Lerp(shape.Height, dot, k));
        }, () =>
        {
            Tint(a, InkColor);
            Tint(b, InkColor);

            // 2. Un quart de tour : l'un en haut, l'autre en bas.
            RunMorph(MorphTurn, t =>
            {
                double angle = EaseInOut(t) * Math.PI / 2;
                Place(a, colon.X - (r * Math.Cos(angle)), colon.Y - (r * Math.Sin(angle)), dot, dot);
                Place(b, colon.X + (r * Math.Cos(angle)), colon.Y + (r * Math.Sin(angle)), dot, dot);
            }, () =>
            {
                if (generation != _morphGeneration)
                {
                    return;
                }

                // 3. Les vrais deux-points prennent le relais ; les chiffres se déplient.
                _morphTarget = null;
                RemoveMorphPixels();
                IdleClock.UnfoldFromColon();
                SetWeatherOpacity(1);
                ShowRestWeather();
            });
        });
    }

    /// <summary>Les chiffres se replient vers les deux-points, qui redeviennent les yeux.</summary>
    private void ColonToEyes()
    {
        CancelFaceMorph();

        if (ColonOffset() is not { } colon)
        {
            _restFace = RestFace.Eyes;
            IdleClock.UnfoldAll();
            RequestRender();
            return;
        }

        int generation = ++_morphGeneration;
        _morphTarget = RestFace.Eyes;
        _morphStartedAt = DateTime.UtcNow;
        SetWeatherOpacity(0);
        TimeSpan fold = IdleClock.FoldToColon();

        RunAfter(fold, () =>
        {
            if (generation != _morphGeneration)
            {
                return;
            }

            // Les yeux reprennent leur place, invisibles, pour qu'on sache où aller.
            _restFace = RestFace.Eyes;
            IdleClock.HideColon();
            IdleClock.Visibility = _settings.ShowClockAtRest ? Visibility.Visible : Visibility.Collapsed;
            WeatherGlyph.Visibility = Visibility.Collapsed;
            WeatherText.Visibility = Visibility.Collapsed;
            RestEyes.Opacity = 0;
            ShowRestPixel(true);
            EyeShape shape = _wakeSurprise ? PixelGaze.Shape(PixelMood.Surprised) : RestEyes.CurrentShape;
            IdleRestView.UpdateLayout();

            if (EyeOffsets() is not { } to)
            {
                _morphTarget = null;
                RestEyes.Opacity = 1;
                IdleClock.UnfoldAll();
                SetWeatherOpacity(1);
                FinishWake();
                return;
            }

            (Border a, Border b) = MorphPixels(InkColor);
            double r = PixelClockView.ColonHalfGap, dot = PixelClockView.ColonDot;

            RunMorph(MorphTurn, t =>
            {
                double angle = (1 - EaseInOut(t)) * Math.PI / 2;
                Place(a, colon.X - (r * Math.Cos(angle)), colon.Y - (r * Math.Sin(angle)), dot, dot);
                Place(b, colon.X + (r * Math.Cos(angle)), colon.Y + (r * Math.Sin(angle)), dot, dot);
            }, () =>
            {
                Tint(a, EyeColor);
                Tint(b, EyeColor);
                RunMorph(MorphOpen, t =>
                {
                    double k = EaseSpring(t);
                    Place(a, Lerp(colon.X - r, to.Left.X, k), Lerp(colon.Y, to.Left.Y, k), Lerp(dot, shape.Width, k), Lerp(dot, shape.Height, k), shape.Roundness);
                    Place(b, Lerp(colon.X + r, to.Right.X, k), Lerp(colon.Y, to.Right.Y, k), Lerp(dot, shape.Width, k), Lerp(dot, shape.Height, k), shape.Roundness);
                }, () =>
                {
                    if (generation != _morphGeneration)
                    {
                        return;
                    }

                    _morphTarget = null;
                    RemoveMorphPixels();
                    RestEyes.Opacity = 1;
                    IdleClock.UnfoldAll();
                    IdleClock.Visibility = _settings.ShowClockAtRest ? Visibility.Visible : Visibility.Collapsed;
                    SetWeatherOpacity(1);
                    FinishWake();
                });
            });
        });
    }

    /// <summary>Le réveil se lit en sursaut : les yeux ronds, comme pour une notification.</summary>
    private void FinishWake()
    {
        if (_wakeSurprise)
        {
            _wakeSurprise = false;
            SurprisePixel();
        }
    }

    /// <summary>Une activité arrive pendant la transition : les deux pixels s'effacent, tout reprend sa place.</summary>
    private void CancelFaceMorph()
    {
        _morphGeneration++;
        _morphTarget = null;

        if (_morphFrame is not null)
        {
            SpaceNotch_App.Animations.FrameClock.Rendering -= _morphFrame;
            _morphFrame = null;
        }

        RemoveMorphPixels();
        RestEyes.Opacity = 1;
        SetWeatherOpacity(1);

        // Une arrivée interrompue montre tout de suite ce qu'elle cachait.
        if (_handoffView is { } view)
        {
            view.Opacity = 1;
            _handoffView = null;
        }
    }

    private (Border, Border) MorphPixels(global::Windows.UI.Color color)
    {
        RemoveMorphPixels();
        _morphLeft = NewMorphPixel(color);
        _morphRight = NewMorphPixel(color);
        return (_morphLeft, _morphRight);
    }

    private Border NewMorphPixel(global::Windows.UI.Color color)
    {
        var pixel = new Border { Background = new SolidColorBrush(color), IsHitTestVisible = false };
        FaceMorphLayer.Children.Add(pixel);
        return pixel;
    }

    private void RemoveMorphPixels()
    {
        FaceMorphLayer.Children.Clear();
        _morphLeft = null;
        _morphRight = null;
    }

    /// <summary>Pose un pixel par son centre, repéré depuis le centre de la couche (qui suit la notch qui grandit).</summary>
    private void Place(Border pixel, double x, double y, double width, double height, double roundness = 0)
    {
        pixel.Width = Math.Max(0.5, width);
        pixel.Height = Math.Max(0.5, height);
        pixel.CornerRadius = new CornerRadius(Math.Min(width, height) / 2 * roundness);
        Canvas.SetLeft(pixel, (FaceMorphLayer.ActualWidth / 2) + x - (width / 2));
        Canvas.SetTop(pixel, (FaceMorphLayer.ActualHeight / 2) + y - (height / 2));
    }

    private static void Tint(Border pixel, global::Windows.UI.Color color) => pixel.Background = new SolidColorBrush(color);

    private void SetWeatherOpacity(double opacity)
    {
        WeatherGlyph.Opacity = opacity;
        WeatherText.Opacity = opacity;
    }

    /// <summary>Une étape de la transition, image par image ; une seule à la fois.</summary>
    private void RunMorph(TimeSpan duration, Action<double> step, Action done)
    {
        if (_morphFrame is not null)
        {
            SpaceNotch_App.Animations.FrameClock.Rendering -= _morphFrame;
        }

        int generation = _morphGeneration;
        DateTime start = DateTime.UtcNow;
        step(0);

        _morphFrame = (_, _) =>
        {
            if (generation != _morphGeneration || _isClosed)
            {
                SpaceNotch_App.Animations.FrameClock.Rendering -= _morphFrame;
                _morphFrame = null;
                return;
            }

            double t = Math.Clamp((DateTime.UtcNow - start) / duration, 0, 1);
            step(t);

            if (t >= 1)
            {
                SpaceNotch_App.Animations.FrameClock.Rendering -= _morphFrame;
                _morphFrame = null;
                done();
            }
        };

        SpaceNotch_App.Animations.FrameClock.Rendering += _morphFrame;
    }

    private void RunAfter(TimeSpan delay, Action action)
    {
        if (delay <= TimeSpan.Zero)
        {
            action();
            return;
        }

        DispatcherQueueTimer timer = _dispatcherQueue.CreateTimer();
        timer.Interval = delay;
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            timer.Stop();

            if (!_isClosed)
            {
                action();
            }
        };
        timer.Start();
    }

    private static double Lerp(double from, double to, double t) => from + ((to - from) * t);

    private static double EaseInOut(double t) => t < 0.5 ? 4 * t * t * t : 1 - (Math.Pow((-2 * t) + 2, 3) / 2);

    /// <summary>cubic-bezier(.4, 0, .2, 1), approché.</summary>
    private static double EaseGlide(double t) => 1 - Math.Pow(1 - t, 3);

    /// <summary>Ressort court : dépasse un peu, puis se pose.</summary>
    private static double EaseSpring(double t) => 1 + (2.2 * Math.Pow(t - 1, 3)) + (1.2 * Math.Pow(t - 1, 2));
}
