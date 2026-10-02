using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using SpaceNotch.Infrastructure.Logging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace SpaceNotch_App.Phone;

/// <summary>
/// Le miroir avant une réunion (W5) : au survol de « Rejoindre », la webcam
/// s'affiche dans la notch pour vérifier sa tête et sa lumière. Rien n'est
/// enregistré ni envoyé ; la caméra est rendue dès que le pointeur s'en va.
/// </summary>
internal sealed class CameraMirror : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private MediaCapture? _capture;
    private MediaPlayer? _player;
    private int _generation;

    /// <summary>Le nom de la caméra allumée (« Caméra intégrée »), ou <c>null</c>.</summary>
    public string? CameraName { get; private set; }

    /// <summary>Allume la caméra dans <paramref name="target"/>. Rend <c>false</c> sans caméra ou sans accord.</summary>
    public async Task<bool> StartAsync(MediaPlayerElement target)
    {
        ArgumentNullException.ThrowIfNull(target);
        int generation = Interlocked.Increment(ref _generation);
        await _gate.WaitAsync().ConfigureAwait(true);

        try
        {
            // Le pointeur est déjà reparti pendant l'attente : ne rien allumer.
            if (generation != Volatile.Read(ref _generation))
            {
                return false;
            }

            await StopCoreAsync().ConfigureAwait(true);

            var groups = await MediaFrameSourceGroup.FindAllAsync();
            MediaFrameSourceGroup? group = groups.FirstOrDefault(g => g.SourceInfos.Any(i => i.SourceKind == MediaFrameSourceKind.Color));

            if (group is null)
            {
                return false;
            }

            var capture = new MediaCapture();
            await capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                SourceGroup = group,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                MemoryPreference = MediaCaptureMemoryPreference.Auto,
                SharingMode = MediaCaptureSharingMode.SharedReadOnly
            });

            MediaFrameSource? source = capture.FrameSources.Values.FirstOrDefault(s => s.Info.SourceKind == MediaFrameSourceKind.Color);

            if (source is null || generation != Volatile.Read(ref _generation))
            {
                capture.Dispose();
                return false;
            }

            var player = new MediaPlayer
            {
                Source = MediaSource.CreateFromMediaFrameSource(source),
                RealTimePlayback = true,
                AutoPlay = true,
                IsMuted = true
            };

            _capture = capture;
            _player = player;
            CameraName = group.DisplayName;
            target.SetMediaPlayer(player);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            // Caméra refusée dans les paramètres de confidentialité, ou déjà prise en exclusivité.
            MiniLogger.Log("[MIROIR] Caméra indisponible", ex);
            await StopCoreAsync().ConfigureAwait(true);
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Rend la caméra.</summary>
    public async Task StopAsync(MediaPlayerElement? target)
    {
        Interlocked.Increment(ref _generation);
        await _gate.WaitAsync().ConfigureAwait(true);

        try
        {
            target?.SetMediaPlayer(null);
            await StopCoreAsync().ConfigureAwait(true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private Task StopCoreAsync()
    {
        _player?.Dispose();
        _player = null;
        _capture?.Dispose();
        _capture = null;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _player?.Dispose();
        _capture?.Dispose();
        _gate.Dispose();
    }
}
