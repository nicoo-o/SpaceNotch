using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Features;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;

namespace SpaceNotch.Features.Downloads;

/// <summary>
/// Téléchargements en cours, quel que soit le navigateur.
///
/// <para>
/// La fonctionnalité observe le dossier Téléchargements par
/// <see cref="FileSystemWatcher"/> — des notifications du système de fichiers,
/// jamais une scrutation — et confie l'interprétation à <see cref="DownloadTracker"/>.
/// Pendant le transfert, la notch porte la grille « Process » et la taille
/// reçue ; à la fin, la grille converge (« Complete ») et propose d'ouvrir le
/// fichier. C'est l'exemple de référence du langage hypnotique : un travail qui
/// dure, qui évolue, puis qui se termine.
/// </para>
/// </summary>
public sealed class DownloadsFeature : IslandFeatureBase
{
    public const string FeatureKey = FeatureKeys.Downloads;

    public const string ActivityId = "downloads";

    public const string OpenAction = "downloads.open";

    public const string RevealAction = "downloads.reveal";

    /// <summary>
    /// Intervalle minimal entre deux republications de progression : un
    /// navigateur écrit des centaines de blocs par seconde, la notch n'a besoin
    /// d'en voir que deux.
    /// </summary>
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(500);

    private static readonly TimeSpan CompletedLifetime = TimeSpan.FromSeconds(6);

    private readonly Func<string> _resolveFolder;
    private readonly DownloadTracker _tracker = new();
    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private long _lastPublish;

    public DownloadsFeature(
        IActivityManager activities,
        IEventBus events,
        Func<string> resolveFolder,
        bool isEnabled = true)
        : base(FeatureKey, "Téléchargements", activities, events, isEnabled)
    {
        _resolveFolder = resolveFolder ?? throw new ArgumentNullException(nameof(resolveFolder));
    }

    /// <summary>Le suivi, exposé pour les tests et les diagnostics.</summary>
    public DownloadTracker Tracker => _tracker;

    protected override Task OnStartAsync(CancellationToken cancellationToken)
    {
        string folder = _resolveFolder();

        if (!Directory.Exists(folder))
        {
            // Pas de dossier, pas de téléchargement à suivre : la fonctionnalité
            // reste démarrée et ne fait rien, plutôt que d'échouer.
            return Task.CompletedTask;
        }

        _watcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite
        };

        _watcher.Created += OnWritten;
        _watcher.Changed += OnWritten;
        _watcher.Renamed += OnRenamed;
        _watcher.Deleted += OnDeleted;
        _watcher.Error += OnWatcherError;
        _watcher.EnableRaisingEvents = true;

        return Task.CompletedTask;
    }

    protected override Task OnStopAsync()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnWritten;
            _watcher.Changed -= OnWritten;
            _watcher.Renamed -= OnRenamed;
            _watcher.Deleted -= OnDeleted;
            _watcher.Error -= OnWatcherError;
            _watcher.Dispose();
            _watcher = null;
        }

        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------
    // Entrées : appelables directement, ce qui rend la fonctionnalité testable
    // sans système de fichiers réel.
    // ------------------------------------------------------------------

    /// <summary>Un fichier du dossier a été écrit.</summary>
    public void Written(string path, long size)
    {
        lock (_gate)
        {
            DownloadChange change = _tracker.OnWritten(path, size);

            if (change == DownloadChange.Progressed && !DuePublish())
            {
                return;
            }

            React(change);
        }
    }

    /// <summary>Un fichier du dossier a été renommé.</summary>
    public void Renamed(string oldPath, string newPath, long size)
    {
        lock (_gate)
        {
            React(_tracker.OnRenamed(oldPath, newPath, size));
        }
    }

    /// <summary>Un fichier du dossier a été supprimé.</summary>
    public void Deleted(string path)
    {
        lock (_gate)
        {
            React(_tracker.OnDeleted(path));
        }
    }

    public override Task<bool> HandleActionAsync(IslandActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? path = _tracker.LastCompletedPath;

        if (!string.Equals(request.ActivityId, ActivityId, StringComparison.Ordinal) || path is null)
        {
            return Task.FromResult(false);
        }

        try
        {
            switch (request.ActionId)
            {
                case OpenAction:
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                    RemoveActivity(ActivityId);
                    return Task.FromResult(true);

                case RevealAction:
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                    RemoveActivity(ActivityId);
                    return Task.FromResult(true);

                default:
                    return Task.FromResult(false);
            }
        }
        catch (Exception ex)
        {
            ReportError(ex);
            return Task.FromResult(true);
        }
    }

    private void React(DownloadChange change)
    {
        switch (change)
        {
            case DownloadChange.Started:
            case DownloadChange.Progressed:
                _lastPublish = Environment.TickCount64;
                PublishWorking();
                break;

            case DownloadChange.Completed:
                PublishCompleted();
                break;

            case DownloadChange.Cancelled:
                if (_tracker.Active.Count == 0)
                {
                    RemoveActivity(ActivityId);
                }
                else
                {
                    PublishWorking();
                }

                break;
        }
    }

    private bool DuePublish() => Environment.TickCount64 - _lastPublish >= ProgressInterval.TotalMilliseconds;

    private void PublishWorking()
    {
        DownloadProgress[] active = [.. _tracker.Active];

        if (active.Length == 0)
        {
            return;
        }

        DownloadProgress first = active[0];
        long total = active.Sum(d => d.BytesReceived);

        PublishActivity(new IslandActivity
        {
            Id = ActivityId,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Card,
            Title = active.Length == 1 ? "Téléchargement" : $"{active.Length} téléchargements",
            Eyebrow = active.Length == 1 ? first.DisplayName : $"{first.DisplayName} et {active.Length - 1} autre(s)",
            Metric = DownloadTracker.FormatSize(total),
            Source = "Téléchargements",
            IconKey = "Download",
            State = IslandActivityState.DownloadActive,
            Priority = ActivityPriority.Normal,
            Policy = ActivityPresentationPolicy.Passive,
            MotionState = ActivityMotionState.Working,
            MotionPreset = HypnoticPreset.Process
        });
    }

    private void PublishCompleted()
    {
        // D'autres téléchargements continuent : la notch reste sur le travail
        // en cours plutôt que d'annoncer une fin partielle.
        if (_tracker.Active.Count > 0)
        {
            PublishWorking();
            return;
        }

        string name = _tracker.LastCompletedPath is { } completed
            ? DownloadTracker.FileNameOf(completed)
            : "Fichier";

        PublishActivity(new IslandActivity
        {
            Id = ActivityId,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Card,
            Title = "Téléchargé",
            Eyebrow = name,
            Metric = "✓",
            Source = "Téléchargements",
            IconKey = "Check",
            State = IslandActivityState.DownloadActive,
            Priority = ActivityPriority.Normal,
            Policy = ActivityPresentationPolicy.Temporary,
            MotionState = ActivityMotionState.Completing,
            Duration = CompletedLifetime,
            Actions =
            [
                new ActivityAction(OpenAction, "Ouvrir", "\uE8E5", ActivityActionKind.Invoke, IsPrimary: true),
                new ActivityAction(RevealAction, "Afficher", "\uE838")
            ]
        });
    }

    private void OnWritten(object sender, FileSystemEventArgs e) => Written(e.FullPath, SizeOf(e.FullPath));

    private void OnRenamed(object sender, RenamedEventArgs e) => Renamed(e.OldFullPath, e.FullPath, SizeOf(e.FullPath));

    private void OnDeleted(object sender, FileSystemEventArgs e) => Deleted(e.FullPath);

    private void OnWatcherError(object sender, ErrorEventArgs e) => ReportError(e.GetException());

    private static long SizeOf(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception)
        {
            // Le navigateur a pu renommer ou supprimer le fichier entre-temps.
            return 0;
        }
    }
}
