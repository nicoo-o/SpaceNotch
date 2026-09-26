using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Features;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;

namespace SpaceNotch.Features.FileShelf;

/// <summary>
/// Étagère de fichiers : les fichiers déposés sur l'Island y restent à disposition.
///
/// Elle expose une API typée en plus du contrat de fonctionnalité, parce que
/// l'interaction de dépôt a besoin de manipuler les éléments directement.
/// </summary>
public sealed class FileShelfManager : IslandFeatureBase
{
    public const string FeatureKey = FeatureKeys.FileShelf;

    public const string ShelfActivityId = "feature.fileshelf.current";

    private readonly List<ShelfItem> _items = [];
    private readonly object _lock = new();

    public FileShelfManager(
        IActivityManager activities,
        IEventBus events,
        bool isEnabled = true)
        : base(FeatureKey, "Étagère de fichiers", activities, events, isEnabled)
    {
    }

    /// <summary>Signalé lorsque le contenu de l'étagère change.</summary>
    public event EventHandler? ShelfUpdated;

    /// <summary>
    /// Désactiver la fonctionnalité retire les activités publiées, mais ne
    /// détruit pas les fichiers déposés : l'étagère reste en mémoire. Sa
    /// persistance sur disque relève du lot dédié au stockage.
    /// </summary>
    protected override Task OnStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    protected override Task OnStopAsync()
    {
        RemoveActivity(ShelfActivityId);
        return Task.CompletedTask;
    }

    public void AddFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return;
        }

        var fileInfo = new FileInfo(filePath);

        var item = new ShelfItem(
            Id: Guid.NewGuid().ToString("N"),
            FilePath: filePath,
            FileName: fileInfo.Name,
            FileSizeBytes: fileInfo.Length,
            AddedAt: DateTime.UtcNow);

        lock (_lock)
        {
            _items.Insert(0, item);
        }

        UpdateActivity();
        ShelfUpdated?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveFile(string id)
    {
        lock (_lock)
        {
            _items.RemoveAll(i => string.Equals(i.Id, id, StringComparison.Ordinal));
        }

        UpdateActivity();
        ShelfUpdated?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<ShelfItem> GetItems()
    {
        lock (_lock)
        {
            return _items.ToList();
        }
    }

    private void UpdateActivity()
    {
        int count;

        lock (_lock)
        {
            count = _items.Count;
        }

        if (count == 0)
        {
            RemoveActivity(ShelfActivityId);
            return;
        }

        // Identifiant stable : l'étagère est une activité unique dont le contenu
        // évolue, pas une activité par fichier déposé.
        PublishActivity(new IslandActivity
        {
            Id = ShelfActivityId,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.FileShelf,
            Title = $"{count} fichier{(count > 1 ? "s" : string.Empty)} déposé{(count > 1 ? "s" : string.Empty)}",
            Subtitle = "Prêt à être glissé ou partagé",
            Source = "FileShelf",
            IconKey = "Folder",
            State = IslandActivityState.Idle,
            Priority = ActivityPriority.Normal
        });
    }
}
