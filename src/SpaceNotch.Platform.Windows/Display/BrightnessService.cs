using System;
using System.Management;

namespace SpaceNotch.Platform.Windows.Display;

/// <summary>
/// État de luminosité d'un écran.
/// </summary>
/// <param name="Percent">Luminosité courante, de 0 à 100.</param>
/// <param name="DisplayName">Écran concerné, pour l'indicateur (« Écran intégré »).</param>
public readonly record struct BrightnessInfo(int Percent, string DisplayName = "Écran intégré");

/// <summary>
/// Luminosité de l'écran intégré, suivie par l'événement WMI
/// <c>WmiMonitorBrightnessEvent</c>.
///
/// <para>
/// L'ancienne version interrogeait l'API DDC/CI avec un moniteur nul — l'appel
/// échouait toujours, et l'indicateur n'apparaissait jamais — et installait un
/// crochet clavier global pour deviner les touches de luminosité. Windows
/// publie pourtant un événement à chaque changement de luminosité de l'écran
/// intégré, quelle qu'en soit la source : touches, curseur des paramètres rapides,
/// économiseur de batterie. Plus de crochet, plus de devinette, et la valeur
/// annoncée est celle qui vient d'être appliquée.
/// </para>
///
/// <para>
/// Limite assumée : un écran externe réglé par ses propres boutons n'émet
/// rien que Windows puisse observer. Seul l'écran intégré est suivi.
/// </para>
/// </summary>
public sealed class BrightnessService : IDisposable
{
    private const string Scope = @"root\WMI";

    private ManagementEventWatcher? _watcher;
    private bool _disposed;

    /// <summary>Signalé à chaque changement de luminosité de l'écran intégré.</summary>
    public event EventHandler<BrightnessInfo>? BrightnessChanged;

    /// <summary>Diagnostic : erreurs WMI, jamais levées.</summary>
    public Action<string, Exception>? Failed { get; set; }

    /// <summary>
    /// Vrai lorsque l'écran intégré expose sa luminosité à Windows. Faux sur un
    /// poste de bureau, où la fonctionnalité n'a pas de sens.
    /// </summary>
    public bool IsAvailable { get; private set; }

    /// <summary>Commence l'écoute. Idempotent.</summary>
    public void Start()
    {
        if (_disposed || _watcher is not null)
        {
            return;
        }

        if (!TryRead(out _))
        {
            // Pas d'écran intégré réglable : rien à écouter.
            return;
        }

        try
        {
            _watcher = new ManagementEventWatcher(
                new ManagementScope(Scope),
                new EventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
            _watcher.EventArrived += OnEventArrived;
            _watcher.Start();
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            Failed?.Invoke("démarrage", ex);
            DisposeWatcher();
        }
    }

    /// <summary>Arrête l'écoute. Strictement symétrique de <see cref="Start"/>.</summary>
    public void Stop()
    {
        DisposeWatcher();
        IsAvailable = false;
    }

    private void OnEventArrived(object sender, EventArrivedEventArgs e)
    {
        try
        {
            if (e.NewEvent["Brightness"] is byte level)
            {
                BrightnessChanged?.Invoke(this, new BrightnessInfo(level));
            }
        }
        catch (Exception ex)
        {
            Failed?.Invoke("événement", ex);
        }
    }

    /// <summary>
    /// Lit la luminosité courante de l'écran intégré. Retourne <c>false</c>
    /// lorsqu'il n'y en a pas — un poste de bureau, une machine virtuelle.
    /// </summary>
    public static bool TryRead(out BrightnessInfo info)
    {
        info = default;

        try
        {
            using var searcher = new ManagementObjectSearcher(Scope, "SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active=TRUE");
            using ManagementObjectCollection results = searcher.Get();

            foreach (ManagementBaseObject result in results)
            {
                using (result)
                {
                    if (result["CurrentBrightness"] is byte level)
                    {
                        info = new BrightnessInfo(level);
                        return true;
                    }
                }
            }
        }
        catch (ManagementException)
        {
            // « Non pris en charge » : aucun écran intégré ne publie sa luminosité.
        }
        catch (global::System.Runtime.InteropServices.COMException)
        {
            // Service WMI indisponible.
        }

        return false;
    }

    /// <summary>Applique une luminosité à l'écran intégré.</summary>
    public static bool TrySet(int percent)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(Scope, "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=TRUE");
            using ManagementObjectCollection results = searcher.Get();

            foreach (ManagementObject method in results)
            {
                using (method)
                {
                    method.InvokeMethod("WmiSetBrightness", [1u, (byte)Math.Clamp(percent, 0, 100)]);
                    return true;
                }
            }
        }
        catch (ManagementException)
        {
        }
        catch (global::System.Runtime.InteropServices.COMException)
        {
        }

        return false;
    }

    private void DisposeWatcher()
    {
        ManagementEventWatcher? watcher = _watcher;
        _watcher = null;

        if (watcher is null)
        {
            return;
        }

        watcher.EventArrived -= OnEventArrived;

        try
        {
            watcher.Stop();
        }
        catch (Exception ex)
        {
            Failed?.Invoke("arrêt", ex);
        }

        watcher.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }
}
