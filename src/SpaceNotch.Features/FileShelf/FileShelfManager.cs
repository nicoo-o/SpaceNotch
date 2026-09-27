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
using SpaceNotch.Core.Localization;

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

    /// <summary>Retire un fichier de l'étagère (valeur : son identifiant). Le fichier lui-même n'est jamais touché.</summary>
    public const string RemoveAction = "shelf.remove";

    /// <summary>Vide l'étagère. Les fichiers restent là où ils sont sur le disque.</summary>
    public const string ClearAction = "shelf.clear";

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

    /// <summary>« Partager sur le téléphone » (F8) : le chemin du fichier à servir.</summary>
    public event Action<string>? ShareRequested;

    /// <summary>Partage un fichier de l'étagère par QR code.</summary>
    public const string ShareAction = "shelf.share";

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

    /// <summary>Vide l'étagère — seulement la liste, jamais les fichiers.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _items.Clear();
        }

        UpdateActivity();
        ShelfUpdated?.Invoke(this, EventArgs.Empty);
    }

    public override Task<bool> HandleActionAsync(IslandActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        switch (request.ActionId)
        {
            case RemoveAction when request.Value is { Length: > 0 } id:
                RemoveFile(id);
                return Task.FromResult(true);

            case ClearAction:
                Clear();
                return Task.FromResult(true);

            case ShareAction when request.Value is { Length: > 0 } shareId:
                ShelfItem? item = GetItems().FirstOrDefault(i => i.Id == shareId);

                if (item is not null)
                {
                    ShareRequested?.Invoke(item.FilePath);
                }

                return Task.FromResult(item is not null);

            default:
                return Task.FromResult(false);
        }
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
            Title = Lang.T($"{count} fichier{(count > 1 ? "s" : string.Empty)} déposé{(count > 1 ? "s" : string.Empty)}", $"{count} file{(count == 1 ? string.Empty : "s")} dropped"),
            Subtitle = Lang.T("Prêt à être glissé ou partagé", "Ready to drag or share"),
            Source = "FileShelf",
            IconKey = "Folder",
            State = IslandActivityState.Idle,
            Priority = ActivityPriority.Normal,

            // La notch prend la hauteur de la liste : en-tête 30, lignes de 40,
            // quatre au plus avant de défiler.
            ExpandedFootprint = new IslandFootprint(ShelfWidth, ShelfChrome + (Math.Min(count, ShelfVisibleRows) * ShelfRow))
        });
    }

    private const double ShelfWidth = 332 + (2 * SceneInsets.Side) + (2 * NotchGeometry.DefaultShoulder);
    private const double ShelfChrome = SceneInsets.Top + 30 + SceneInsets.Bottom;
    private const double ShelfRow = 40;
    private const int ShelfVisibleRows = 4;
}
