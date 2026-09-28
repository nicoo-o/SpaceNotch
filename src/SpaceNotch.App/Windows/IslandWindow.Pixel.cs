using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SpaceNotch.Core.Motion;
using SpaceNotch.Platform.Windows.Win32;

namespace SpaceNotch_App.Windows;

/// <summary>
/// Pixel (P1) : les yeux de la notch au repos. Le regard lit la position du
/// pointeur une douzaine de fois par seconde, et seulement tant que les yeux
/// sont visibles et éveillés ; endormi, Pixel ne regarde plus que l'heure,
/// toutes les trente secondes.
/// </summary>
public sealed partial class IslandWindow
{
    private static readonly TimeSpan GazeInterval = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan SleepCheckInterval = TimeSpan.FromSeconds(30);

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
            return;
        }

        RestEyes.Animate = UseSpringAnimations();
        _gazeTimer ??= CreateRepeatingTimer(GazeInterval, PixelTick);
        PixelTick();
        _gazeTimer.Start();
        ArmBlink();
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
        PixelMood mood = PixelGaze.MoodFor(
            DateTime.UtcNow < _pixelSurpriseUntil,
            _pixelHovered,
            idle: false,
            _pixelNight ?? TimeOnly.FromDateTime(DateTime.Now));

        RestEyes.SetMood(mood);

        if (_gazeTimer is not null)
        {
            _gazeTimer.Interval = mood == PixelMood.Asleep ? SleepCheckInterval : GazeInterval;
        }

        if (mood == PixelMood.Asleep)
        {
            return;
        }

        if (_pixelLook is { } forced)
        {
            RestEyes.Look(forced.X, forced.Y);
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
        timer.Tick += (_, _) => tick();
        return timer;
    }
}
