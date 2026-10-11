using System;
using Microsoft.UI.Dispatching;
using SpaceNotch.Core.Motion;
using SpaceNotch.Platform.Windows.Audio;
using SpaceNotch.Platform.Windows.Power;
using SpaceNotch.Platform.Windows.Shell;

namespace SpaceNotch_App.Windows;

/// <summary>
/// Pixel vivant (vague 7) : il montre l'état du PC (fatigué, chaud, hors
/// ligne), fatigue le soir et bâille, bat la mesure de la musique, jette un
/// coup d'œil vers la fenêtre qui prend la main, dit bonjour au démarrage et
/// au revoir au verrouillage, et sa teinte se réchauffe le soir. Tout passe
/// par <see cref="ApplyPixelLife"/>, appelée à chaque regard (80 ms).
/// </summary>
public sealed partial class IslandWindow
{
    private static readonly TimeSpan VitalsInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan GlanceDuration = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan HelloDuration = TimeSpan.FromMilliseconds(2700);
    private static readonly TimeSpan ByeDuration = TimeSpan.FromMilliseconds(1400);

    private readonly CpuSampler _cpuSampler = new();
    private DispatcherQueueTimer? _vitalsTimer;
    private PixelCondition _condition;
    private ForegroundWatcher? _foreground;
    private DateTime _glanceUntil;
    private (double X, double Y) _glance;
    private DateTime _nextYawn = DateTime.MaxValue;
    private DateTime _yawnUntil;
    private int _yawnSeed;
    private DateTime _helloStart = DateTime.MinValue;
    private DateTime _byeStart = DateTime.MinValue;
    private bool _helloDone;
    private readonly DateTime _lifeStart = DateTime.UtcNow;

    /// <summary>Visite : un état du PC, ou un battement, imposés.</summary>
    private PixelCondition? _tourCondition;
    private bool _tourBeat;

    /// <summary>La teinte des yeux à cette heure (cyan le jour, ambre le soir).</summary>
    private global::Windows.UI.Color EyeColor
    {
        get
        {
            (byte r, byte g, byte b) = PixelVitals.Tint(_pixelNight ?? TimeOnly.FromDateTime(DateTime.Now));
            return global::Windows.UI.Color.FromArgb(0xFF, r, g, b);
        }
    }

    /// <summary>Arme ce qui fait vivre Pixel quand ses yeux sont montrés.</summary>
    private void StartPixelLife()
    {
        _vitalsTimer ??= CreateRepeatingTimer(VitalsInterval, ReadVitals);

        if (!_vitalsTimer.IsRunning)
        {
            _vitalsTimer.Start();
            ReadVitals();
        }

        if (_foreground is null)
        {
            _foreground = new ForegroundWatcher { Ignore = _hWnd };
            _foreground.Changed += window => _ = _dispatcherQueue.TryEnqueueSafely(() => GlanceAt(window));
            _foreground.Start();
        }

        // Le premier affichage des yeux est un réveil : bonjour.
        if (!_helloDone)
        {
            _helloDone = true;
            _helloStart = DateTime.UtcNow;
        }

        if (_nextYawn == DateTime.MaxValue)
        {
            _nextYawn = DateTime.UtcNow + PixelVitals.NextYawn(_yawnSeed++);
        }
    }

    private void StopPixelLife()
    {
        _vitalsTimer?.Stop();
        RestEyes.SetSweating(false);
        RestEyes.SetBeat(0, 0);
    }

    /// <summary>Batterie, processeur, réseau : l'état le plus grave l'emporte.</summary>
    private void ReadVitals()
    {
        int? battery = null;
        bool charging = true;

        try
        {
            if (global::Windows.System.Power.PowerManager.BatteryStatus != global::Windows.System.Power.BatteryStatus.NotPresent)
            {
                battery = global::Windows.System.Power.PowerManager.RemainingChargePercent;
                charging = global::Windows.System.Power.PowerManager.PowerSupplyStatus != global::Windows.System.Power.PowerSupplyStatus.NotPresent;
            }
        }
        catch (Exception)
        {
            // Pas d'information sur l'alimentation : on ne suppose rien.
        }

        double cpu = _cpuSampler.Sample() / 100;
        bool online = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
        _condition = PixelVitals.Condition(battery, charging, cpu, online);
    }

    /// <summary>Une fenêtre prend la main : Pixel jette un coup d'œil dans sa direction.</summary>
    private void GlanceAt(ForegroundWindow window)
    {
        if (_isClosed || _touring || RestEyes.Visibility != Microsoft.UI.Xaml.Visibility.Visible)
        {
            return;
        }

        double scale = RootLayout.XamlRoot?.RasterizationScale ?? 1.0;
        // Le centre de la forme, pas celui de la toile (IslandWindow.Canvas).
        (int boundsX, int boundsY, int boundsWidth, int boundsHeight) = IslandScreenBounds();
        double cx = boundsX + (boundsWidth / 2.0);
        double cy = boundsY + (boundsHeight / 2.0);
        _glance = PixelGaze.Look((window.CenterX - cx) / scale * 3, (window.CenterY - cy) / scale * 3);
        _glanceUntil = DateTime.UtcNow + GlanceDuration;
        PixelTick();
    }

    /// <summary>Session verrouillée : au revoir ; déverrouillée : bonjour.</summary>
    private void OnSessionChange(int change)
    {
        if (change == SessionNotifications.SessionLock)
        {
            _byeStart = DateTime.UtcNow;
            PixelTick();

            // Session verrouillée (audit SN-23) : une fois l'au revoir joué,
            // regard, clignement, constantes, assoupissement et aimant
            // s'arrêtent. Ils tournaient toute la nuit derrière l'écran de
            // verrouillage.
            _lockSuspendTimer ??= CreateOneShotTimer(ByeDuration + TimeSpan.FromMilliseconds(200), () =>
            {
                if (_sessionLocked)
                {
                    SuspendLife(true);
                }
            });
            _sessionLocked = true;
            _lockSuspendTimer.Stop();
            _lockSuspendTimer.Start();
            return;
        }

        if (change == SessionNotifications.SessionUnlock)
        {
            _lockSuspendTimer?.Stop();
            bool wasLocked = _sessionLocked;
            _sessionLocked = false;
            _byeStart = DateTime.MinValue;
            _helloStart = DateTime.UtcNow;

            if (wasLocked && _islandShown)
            {
                // Reprend par le rendu : les yeux se rouvrent sur un bonjour.
                SuspendLife(false);
            }

            PixelTick();
        }
    }

    /// <summary>Vrai entre le verrouillage de la session et son déverrouillage.</summary>
    private bool _sessionLocked;

    private DispatcherQueueTimer? _lockSuspendTimer;

    /// <summary>
    /// Ce que la vie de Pixel impose au regard et à la forme, à cet instant.
    /// Rend un regard imposé (bonjour, coup d'œil, fatigue) ou null pour suivre le curseur.
    /// </summary>
    private (double X, double Y)? ApplyPixelLife(PixelMood mood, TimeOnly time)
    {
        DateTime now = DateTime.UtcNow;
        RestEyes.SetTint(EyeColor);

        // Bonjour : les yeux s'ouvrent lentement, regardent à gauche, à droite, clignent.
        double hello = (now - _helloStart).TotalMilliseconds;

        if (hello >= 0 && hello < HelloDuration.TotalMilliseconds)
        {
            RestEyes.SetCrossed(false);
            RestEyes.SetSweating(false);
            RestEyes.SetBeat(0, 0);
            RestEyes.SetOverride(hello < 500 ? new EyeShape(8, 1.2, 0.2) : hello < 1100 ? new EyeShape(8, 6, 0.2) : null);
            return hello switch
            {
                < 1300 => (0, 0),
                < 1800 => (-PixelGaze.MaxLookX, 0),
                < 2300 => (PixelGaze.MaxLookX, 0),
                _ => (0, 0)
            };
        }

        // Au revoir : les yeux se ferment et restent fermés jusqu'au retour.
        double bye = (now - _byeStart).TotalMilliseconds;

        if (_byeStart != DateTime.MinValue && bye >= 0)
        {
            double k = Math.Min(1, bye / ByeDuration.TotalMilliseconds);
            RestEyes.SetOverride(new EyeShape(8, 10 - (8.5 * k), 0.2));
            return (0, PixelGaze.MaxLookY * k);
        }

        if (mood != PixelMood.Awake)
        {
            RestEyes.SetOverride(null);
            RestEyes.SetCrossed(false);
            RestEyes.SetSweating(false);
            RestEyes.SetBeat(0, 0);
            return null;
        }

        // L'état du PC.
        PixelCondition condition = _tourCondition ?? _condition;
        RestEyes.SetCrossed(condition == PixelCondition.Offline);
        RestEyes.SetSweating(condition == PixelCondition.Hot);

        // Le soir, et ses bâillements.
        bool evening = PixelVitals.IsEvening(time);

        if (evening && now >= _nextYawn)
        {
            _yawnUntil = now + TimeSpan.FromMilliseconds(PixelVitals.YawnMilliseconds);
            _nextYawn = now + PixelVitals.NextYawn(_yawnSeed++);

            // Différé : ce calcul tourne pendant le rendu, qui ne doit pas s'appeler lui-même.
            _ = _dispatcherQueue.TryEnqueueSafely(RequestRender);
        }

        bool yawning = now < _yawnUntil;
        RestEyes.SetOverride(yawning ? PixelVitals.Yawn
            : PixelVitals.Shape(condition) is { } shape ? shape
            : evening ? PixelVitals.Drowsy
            : null);

        // La musique : les yeux battent la mesure.
        double level = _tourBeat ? 0.8 : condition == PixelCondition.Normal ? ReadLevel() : 0;
        (double offset, double squash) = PixelVitals.Beat((now - _lifeStart).TotalSeconds, level);
        RestEyes.SetBeat(offset, squash);

        if (now < _glanceUntil)
        {
            return _glance;
        }

        return PixelVitals.Look(condition) ?? (evening ? (0, PixelGaze.MaxLookY * 0.5) : null);
    }

    private static double ReadLevel()
    {
        try
        {
            return AudioPeakMeter.Shared.Read();
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private bool _wasYawning;

    /// <summary>Le bâillement fini, la notch reprend sa forme.</summary>
    private void TrackYawnEnd()
    {
        bool yawning = Yawning;
        bool ended = _wasYawning && !yawning;

        // Noté avant de redessiner : le rendu relance le regard, qui repasserait ici.
        _wasYawning = yawning;

        if (ended)
        {
            _ = _dispatcherQueue.TryEnqueueSafely(RequestRender);
        }
    }

    /// <summary>Vrai pendant un bâillement : la notch s'étire un peu vers le bas.</summary>
    private bool Yawning => DateTime.UtcNow < _yawnUntil && PixelAtRest && _controller.PresentedActivity is null;
}
