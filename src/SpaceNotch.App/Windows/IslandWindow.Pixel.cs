using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SpaceNotch.Core.Motion;
using SpaceNotch.Platform.Windows.Win32;

namespace SpaceNotch_App.Windows;

/// <summary>
/// Pixel (P1) : les yeux de la notch au repos. Le regard lit la position du
/// pointeur une douzaine de fois par seconde, et seulement tant que les yeux
/// sont visibles et éveillés ; endormi, Pixel vérifie son réveil toutes les
/// cinq secondes.
/// </summary>
public sealed partial class IslandWindow
{
    private static readonly TimeSpan GazeInterval = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan SleepCheckInterval = TimeSpan.FromSeconds(5);

    private DispatcherQueueTimer? _gazeTimer;
    private DispatcherQueueTimer? _blinkTimer;
    private int _blinkSeed;
    private bool _pixelHovered;
    private DateTime _pixelSurpriseUntil;

    /// <summary>Vrai quand Pixel habite la notch au repos.</summary>
    private bool PixelAtRest => _settings.ShowPixel && !UsesSideTab;

    /// <summary>
    /// Windows réduit les animations, ou l'intensité « Calme » est choisie :
    /// Pixel se calme (ADR-029, n° 22). Yeux ouverts et fixes, sans clignement,
    /// coup d'œil, bâillement ni battement ; il change encore d'humeur (survol,
    /// sommeil, soir), en fondu.
    /// </summary>
    private bool PixelCalm => !_visualState.UseSpringAnimations || _settings.PixelIntensity == PixelIntensity.Calm;

    private bool PixelExpressive => !PixelCalm && _settings.PixelIntensity == PixelIntensity.Expressive;


    /// <summary>
    /// Dernière position du pointeur, et depuis quand il n'a pas bougé (Pixel
    /// expressif). Vide avant la première lecture : un pointeur en (0,0) n'est
    /// pas pris pour un pointeur immobile depuis le démarrage.
    /// </summary>
    private NativeMethods.POINT? _lastCursor;
    private DateTime _cursorStillSince = DateTime.UtcNow;

    /// <summary>
    /// Windows vient de réduire ou de rétablir les animations, ou l'intensité de
    /// Pixel a changé : Pixel déjà vivant suit aussitôt — le clignement reprend,
    /// ou s'arrête avec la goutte qui glisse.
    /// </summary>
    private void OnPixelCalmChanged()
    {
        RestEyes.Animate = !PixelCalm;

        if (!_pixelLive)
        {
            return;
        }

        if (!PixelCalm && RestEyes.Mood != PixelMood.Asleep)
        {
            ArmBlink();
        }

        PixelTick();
    }

    /// <summary>Montre ou cache les yeux, et arme ou arrête ce qui les fait vivre.</summary>
    /// <remarks>
    /// Chaque rendu cache puis remontre les yeux : arrêter et relancer à chaque
    /// fois ce qui les fait vivre relisait batterie, processeur et réseau à
    /// chaque rendu, et faisait repartir regard et clignement. L'arrêt est donc
    /// différé à la fin du rendu, et n'a lieu que si les yeux sont restés cachés.
    /// </remarks>
    private void ShowRestPixel(bool visible)
    {
        RestEyes.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        if (!visible)
        {
            if (_pixelLive && !_pixelStopQueued)
            {
                _pixelStopQueued = true;
                _ = _dispatcherQueue.TryEnqueueSafely(DispatcherQueuePriority.Low, () =>
                {
                    _pixelStopQueued = false;

                    if (RestEyes.Visibility != Visibility.Visible)
                    {
                        StopRestPixel();
                    }
                });
            }

            return;
        }

        // Calmé par le mouvement réduit de Windows ou l'intensité « Calme » ;
        // « Rebond » ne concerne que le dépassement de la forme.
        RestEyes.Animate = !PixelCalm;

        // Session verrouillée ou notch retirée : les yeux restent fermés, rien ne
        // tourne — un rendu pendant le retrait les relançait.
        if (!LoopsShown())
        {
            return;
        }

        if (_pixelLive)
        {
            PixelTick();
            return;
        }

        _pixelLive = true;
        StartPixelLife();

        _gazeTimer ??= CreateRepeatingTimer(GazeInterval, PixelTick);
        PixelTick();
        _gazeTimer.Start();
        if (RestEyes.Mood != PixelMood.Asleep && !PixelCalm)
        {
            ArmBlink();
        }
    }

    private bool _pixelLive;
    private bool _pixelStopQueued;

    /// <summary>Arrête tout ce qui fait vivre les yeux (regard, clignement, constantes).</summary>
    private void StopRestPixel()
    {
        _pixelLive = false;
        _gazeTimer?.Stop();
        _blinkTimer?.Stop();
        StopPixelLife();
    }

    /// <summary>Une notification arrive : les yeux s'arrondissent un instant.</summary>
    private void SurprisePixel()
    {
        _pixelSurpriseUntil = DateTime.UtcNow.AddSeconds(1.4);

        if (RestEyes.Visibility == Visibility.Visible)
        {
            PixelTick();
        }
    }

    private void PixelTick()
    {
        TimeOnly time = _pixelNight ?? TimeOnly.FromDateTime(DateTime.Now);
        PixelMood mood = PixelGaze.MoodFor(
            DateTime.UtcNow < _pixelSurpriseUntil,
            _pixelHovered,
            _eyesClosing || (!_touring && PixelGaze.IsIdle(LastInputIdle())),
            time);
        bool wasAsleep = RestEyes.Mood == PixelMood.Asleep;

        if (mood != RestEyes.Mood && PixelCalm && RestEyes.Visibility == Visibility.Visible)
        {
            SpaceNotch_App.Animations.ContentTransition.Dissolve(RestEyes, from: 0f);
        }

        RestEyes.SetMood(mood);

        // Pixel vivant : état du PC, soir, musique, coup d'œil, bonjour et au revoir.
        (double X, double Y)? imposed = ApplyPixelLife(mood, time);
        TrackYawnEnd();

        if (_gazeTimer is not null)
        {
            // Calme, Pixel ne suit plus le pointeur : son humeur se relit au rythme du
            // sommeil, sauf une surprise, relue à sa fin (1,4 s) plutôt que 5 s après.
            TimeSpan surpriseLeft = _pixelSurpriseUntil - DateTime.UtcNow;
            _gazeTimer.Interval = PixelCalm && surpriseLeft > TimeSpan.Zero ? surpriseLeft + TimeSpan.FromMilliseconds(50)
                : mood == PixelMood.Asleep || PixelCalm ? SleepCheckInterval
                : GazeInterval;
        }

        if (mood == PixelMood.Asleep)
        {
            _blinkTimer?.Stop();
            return;
        }

        if (wasAsleep)
        {
            ArmBlink();
        }

        TrackEyes();

        if (PixelCalm)
        {
            _blinkTimer?.Stop();
            RestEyes.Look(0, 0);
            return;
        }

        if (_pixelLook is { } forced)
        {
            RestEyes.Look(forced.X, forced.Y);
            return;
        }

        if (imposed is { } life)
        {
            RestEyes.Look(life.X, life.Y);
            return;
        }

        if (!NativeMethods.GetCursorPos(out NativeMethods.POINT cursor))
        {
            return;
        }

        if (_lastCursor is not { } last || cursor.X != last.X || cursor.Y != last.Y)
        {
            _lastCursor = cursor;
            _cursorStillSince = DateTime.UtcNow;
        }

        // Expressif : un pointeur oublié ne fige pas le regard, Pixel regarde autour
        // de lui — éveillé seulement : survolé ou surpris, il regarde le pointeur.
        if (PixelExpressive && mood == PixelMood.Awake && PixelGaze.Wander(DateTime.UtcNow - _cursorStillSince) is { } wander)
        {
            RestEyes.Look(wander.X, wander.Y);
            return;
        }

        double scale = RootLayout.XamlRoot?.RasterizationScale ?? 1.0;
        // Le centre de la forme, pas celui de la fenêtre : accrochée en haut, la
        // fenêtre est une toile plus grande que la notch (IslandWindow.Canvas).
        (int boundsX, int boundsY, int boundsWidth, int boundsHeight) = IslandScreenBounds();
        double centerX = boundsX + (boundsWidth / 2.0);
        double centerY = boundsY + (boundsHeight / 2.0);
        (double x, double y) = PixelGaze.Look((cursor.X - centerX) / scale, (cursor.Y - centerY) / scale);
        RestEyes.Look(x, y);
    }

    /// <summary>Temps écoulé depuis la dernière saisie/souris, sans sondage plus rapide que le sommeil de Pixel.</summary>
    private static TimeSpan LastInputIdle()
    {
        var info = new NativeMethods.LASTINPUTINFO
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.LASTINPUTINFO>()
        };

        if (!NativeMethods.GetLastInputInfo(ref info))
        {
            return TimeSpan.Zero;
        }

        // GetTickCount et LASTINPUTINFO.dwTime sont deux uint millisecondes ;
        // la soustraction non signée reste correcte au débordement (~49,7 jours).
        uint elapsed = unchecked(NativeMethods.GetTickCount() - info.dwTime);
        return TimeSpan.FromMilliseconds(elapsed);
    }

    private void ArmBlink()
    {
        // Un regard ou un réveil pendant un retrait ne relance pas le clignement ;
        // Pixel calmé ne cligne pas.
        if (!LoopsShown() || PixelCalm)
        {
            return;
        }

        _blinkTimer ??= CreateRepeatingTimer(PixelGaze.NextBlink(_blinkSeed), () =>
        {
            RestEyes.Blink();
            _blinkTimer!.Interval = PixelGaze.NextBlink(++_blinkSeed);
        });

        _blinkTimer.Start();
    }

    /// <summary>Visite : heure et regard imposés, pour filmer Pixel sans souris.</summary>
    private TimeOnly? _pixelNight;

    private (double X, double Y)? _pixelLook;

    private DispatcherQueueTimer CreateRepeatingTimer(TimeSpan interval, Action tick)
    {
        DispatcherQueueTimer timer = DispatcherQueue.CreateTimer();
        timer.Interval = interval;
        timer.IsRepeating = true;
        timer.Tick += SpaceNotch_App.Diagnostics.Guard.Tick((_, _) =>
        {
            if (!_isClosed)
            {
                tick();
            }
        });
        return TrackTimer(timer);
    }
}
