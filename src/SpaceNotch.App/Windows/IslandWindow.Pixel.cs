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

    /// <summary>Montre ou cache les yeux, et arme ou arrête ce qui les fait vivre.</summary>
    private void ShowRestPixel(bool visible)
    {
        RestEyes.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        if (!visible)
        {
            _gazeTimer?.Stop();
            _blinkTimer?.Stop();
            StopPixelLife();
            return;
        }

        StartPixelLife();

        RestEyes.Animate = UseSpringAnimations();
        _gazeTimer ??= CreateRepeatingTimer(GazeInterval, PixelTick);
        PixelTick();
        _gazeTimer.Start();
        if (RestEyes.Mood != PixelMood.Asleep)
        {
            ArmBlink();
        }
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

        RestEyes.SetMood(mood);

        // Pixel vivant : état du PC, soir, musique, coup d'œil, bonjour et au revoir.
        (double X, double Y)? imposed = ApplyPixelLife(mood, time);
        TrackYawnEnd();

        if (_gazeTimer is not null)
        {
            _gazeTimer.Interval = mood == PixelMood.Asleep ? SleepCheckInterval : GazeInterval;
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

        double scale = RootLayout.XamlRoot?.RasterizationScale ?? 1.0;
        double centerX = _appWindow.Position.X + (_appWindow.Size.Width / 2.0);
        double centerY = _appWindow.Position.Y + (_appWindow.Size.Height / 2.0);
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
        timer.Tick += (_, _) =>
        {
            if (!_isClosed)
            {
                tick();
            }
        };
        return TrackTimer(timer);
    }
}
