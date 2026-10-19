using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using SpaceNotch.Core.Motion;
using SpaceNotch.Infrastructure.Logging;
using SpaceNotch.Platform.Windows.Audio;
using SpaceNotch.Platform.Windows.Power;
using SpaceNotch.Platform.Windows.Shell;
using SpaceNotch.Platform.Windows.Win32;

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

    /// <summary>
    /// Pixel reprend la mesure du moniteur (toutes les 5 s au calme) plutôt que
    /// d'interroger le système à son tour : un seul échantillonneur (n° 74).
    /// Moniteur éteint, il mesure lui-même, au plus toutes les 5,5 s.
    /// </summary>
    private static readonly TimeSpan CpuReadingAge = TimeSpan.FromSeconds(5.5);
    private DispatcherQueueTimer? _vitalsTimer;
    private PixelCondition _condition;

    /// <summary>1 pendant une lecture de l'état du PC (<see cref="ReadVitals"/>).</summary>
    private int _vitalsReading;
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
            _nextYawn = DateTime.UtcNow + PixelVitals.NextYawn(_yawnSeed++, _settings.PixelIntensity);
        }
    }

    private void StopPixelLife()
    {
        _vitalsTimer?.Stop();
        RestEyes.SetSweating(false);
        RestEyes.SetBeat(0, 0);
    }

    /// <summary>
    /// Relit l'état du PC hors du fil de l'interface. Lue sur ce fil, la mesure
    /// coûtait environ 13 ms (surtout l'énumération des cartes réseau) au
    /// réveil des yeux puis toutes les 2 s : une image manquée à chaque fois.
    /// Une seule lecture à la fois.
    /// </summary>
    private void ReadVitals()
    {
        if (Interlocked.Exchange(ref _vitalsReading, 1) == 1)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            PixelCondition? condition = null;

            try
            {
                condition = MeasureVitals();
            }
            catch (Exception)
            {
                // Mesure impossible cette fois : l'état précédent reste.
            }

            bool queued = false;

            try
            {
                queued = _dispatcherQueue.TryEnqueueSafely(() =>
                {
                    Volatile.Write(ref _vitalsReading, 0);

                    if (condition is not { } measured || _isClosed || measured == _condition)
                    {
                        return;
                    }

                    _condition = measured;

                    // Les yeux rouverts montraient l'état d'avant : la lecture
                    // arrive après leur premier regard, et Pixel calmé ne
                    // regarde que toutes les 5 s.
                    if (_pixelLive && RestEyes.Visibility == Microsoft.UI.Xaml.Visibility.Visible)
                    {
                        PixelTick();
                    }
                });
            }
            finally
            {
                // File fermée ou en échec : sans cette remise à zéro, Pixel ne
                // relirait plus jamais l'état du PC.
                if (!queued)
                {
                    Volatile.Write(ref _vitalsReading, 0);
                }
            }
        });
    }

    /// <summary>Batterie, processeur, réseau : l'état le plus grave l'emporte. Sans fil imposé.</summary>
    private static PixelCondition MeasureVitals()
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

        double cpu = SpaceNotch.Platform.Windows.Power.CpuLoad.Shared.Get(CpuReadingAge) / 100;
        bool online = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
        return PixelVitals.Condition(battery, charging, cpu, online);
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
        // Expressif : le coup d'œil dure un peu plus, on le voit.
        _glanceUntil = DateTime.UtcNow + (PixelExpressive ? GlanceDuration * 1.5 : GlanceDuration);
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

    /// <summary>Vrai entre la mise en veille et la reprise (n° 35).</summary>
    private bool _systemAsleep;

    /// <summary>Vrai tant que l'écran est éteint (n° 35).</summary>
    private bool _displayOff;

    /// <summary>Inscriptions aux notifications de veille et d'écran, libérées à la fermeture.</summary>
    private (IntPtr SuspendResume, IntPtr Display) _powerNotifications;

    /// <summary>
    /// Veille, reprise et écran (n° 35). Sans mot de passe au réveil, la veille
    /// ne passe pas par le verrouillage de session : rien n'arrêtait la vie de
    /// Pixel et les minuteurs, et rien ne relisait les écrans ni le plein écran
    /// au réveil, alors qu'un portable a pu être débranché de son écran. Sur un
    /// PC en veille moderne (S0), l'écran s'éteint aussi sans veille : la notch
    /// s'arrête de même, et reprend quand il se rallume.
    /// </summary>
    private void OnPowerBroadcast(int change, IntPtr setting)
    {
        bool wasStopped = _systemAsleep || _displayOff;

        if (change == NativeConstants.PBT_APMSUSPEND)
        {
            _systemAsleep = true;
        }
        else if (change is NativeConstants.PBT_APMRESUMEAUTOMATIC or NativeConstants.PBT_APMRESUMESUSPEND)
        {
            _systemAsleep = false;
        }
        else if (change == NativeConstants.PBT_POWERSETTINGCHANGE
            && SpaceNotch.Platform.Windows.Shell.PowerNotifications.TryReadDisplayState(setting, out bool on))
        {
            _displayOff = !on;
        }
        else
        {
            return;
        }

        bool stopped = _systemAsleep || _displayOff;

        if (stopped == wasStopped)
        {
            return;
        }

        if (stopped)
        {
            MiniLogger.Log(_systemAsleep ? "[VEILLE] mise en veille : la notch s'arrête" : "[VEILLE] écran éteint : la notch s'arrête");
            SuspendLife(true);
            return;
        }

        MiniLogger.Log("[VEILLE] reprise : écrans et plein écran relus");
        ScheduleEnvironmentRefresh();

        // Relire la présence peut déjà ramener la notch, et ses boucles avec
        // (retour de plein écran) : ne pas les relancer une seconde fois.
        bool shownBefore = _islandShown;
        _presence.Recheck();

        // Verrouillée au réveil : c'est le déverrouillage qui la ranimera.
        if (shownBefore && _islandShown && !_sessionLocked)
        {
            SuspendLife(false);
        }
    }

    private DispatcherQueueTimer? _lockSuspendTimer;

    /// <summary>
    /// Ce que la vie de Pixel impose au regard et à la forme, à cet instant.
    /// Rend un regard imposé (bonjour, coup d'œil, fatigue) ou null pour suivre le curseur.
    /// </summary>
    private (double X, double Y)? ApplyPixelLife(PixelMood mood, TimeOnly time)
    {
        DateTime now = DateTime.UtcNow;
        RestEyes.SetTint(EyeColor);

        if (PixelCalm)
        {
            return ApplyCalmPixel(mood, time);
        }

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
            _nextYawn = now + PixelVitals.NextYawn(_yawnSeed++, _settings.PixelIntensity);

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

    /// <summary>
    /// Pixel calmé (n° 22) : l'état du PC et le soir se lisent encore sur la
    /// forme des yeux, mais rien ne bouge seul — ni bonjour, ni bâillement, ni
    /// battement, ni coup d'œil. Le regard reste au centre.
    /// </summary>
    private (double X, double Y)? ApplyCalmPixel(PixelMood mood, TimeOnly time)
    {
        RestEyes.SetBeat(0, 0);
        _yawnUntil = DateTime.MinValue;

        if (mood != PixelMood.Awake)
        {
            RestEyes.SetOverride(null);
            RestEyes.SetCrossed(false);
            RestEyes.SetSweating(false);
            return (0, 0);
        }

        // La forme d'abord : la goutte se pose contre l'œil tel qu'il sera dessiné.
        PixelCondition condition = _tourCondition ?? _condition;
        RestEyes.SetOverride(PixelVitals.Shape(condition) is { } shape ? shape
            : PixelVitals.IsEvening(time) ? PixelVitals.Drowsy
            : null);
        RestEyes.SetCrossed(condition == PixelCondition.Offline);
        RestEyes.SetSweating(condition == PixelCondition.Hot);
        return (0, 0);
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
