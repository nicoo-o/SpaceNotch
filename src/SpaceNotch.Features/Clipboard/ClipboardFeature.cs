using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Features;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;
using SpaceNotch.Platform.Windows.Clipboard;
using SpaceNotch.Platform.Windows.Win32;

namespace SpaceNotch.Features.Clipboard;

/// <summary>
/// Historique du presse-papier.
///
/// Trois principes de sécurité structurent cette fonctionnalité, et non un
/// réglage :
/// <list type="bullet">
/// <item>rien n'est capturé tant que la fonctionnalité n'est pas active, et elle
/// est inactive par défaut ;</item>
/// <item>seule une prévisualisation tronquée circule dans le cœur et dans
/// l'interface — le contenu complet ne quitte jamais cette classe ;</item>
/// <item>rien n'est écrit sur disque. L'historique vit en mémoire et disparaît
/// avec le processus, ce qui exclut qu'il survive à la fermeture de session sur
/// un disque non chiffré.</item>
/// </list>
/// </summary>
public sealed class ClipboardFeature : IslandFeatureBase
{
    public const string FeatureKey = FeatureKeys.Clipboard;

    public const string PasteAction = "clipboard.paste";

    public const string PinAction = "clipboard.pin";

    public const string RemoveAction = "clipboard.remove";

    public const string ActivityId = "feature.clipboard.current";

    /// <summary>Longueur maximale d'une prévisualisation, en caractères.</summary>
    private const int PreviewLength = 120;

    private readonly ClipboardMonitor _monitor;
    private readonly IntPtr _windowHandle;
    private readonly int _capacity;

    private readonly List<Entry> _entries = [];
    private readonly object _lock = new();

    public ClipboardFeature(
        IActivityManager activities,
        IEventBus events,
        ClipboardMonitor monitor,
        IntPtr windowHandle,
        bool isEnabled = false,
        int capacity = 25)
        : base(FeatureKey, "Presse-papier", activities, events, isEnabled)
    {
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _windowHandle = windowHandle;
        _capacity = Math.Clamp(capacity, 1, 200);
    }

    /// <summary>Nombre de captures dans l'historique, exposé aux diagnostics.</summary>
    public int EntryCount
    {
        get
        {
            lock (_lock)
            {
                return _entries.Count;
            }
        }
    }

    protected override Task OnStartAsync(CancellationToken cancellationToken)
    {
        // L'inscription est tentée avant l'abonnement : si Windows la refuse, la
        // fonctionnalité passe en échec sans laisser d'abonné derrière elle.
        _monitor.Start(_windowHandle);
        _monitor.ClipboardUpdated += OnClipboardUpdated;

        return Task.CompletedTask;
    }

    protected override Task OnStopAsync()
    {
        _monitor.ClipboardUpdated -= OnClipboardUpdated;
        _monitor.Stop();

        lock (_lock)
        {
            // Désactiver la fonctionnalité efface ce qu'elle avait observé : une
            // capture qui resterait en mémoire après extinction serait contraire à
            // ce que l'utilisateur a demandé en la désactivant.
            _entries.Clear();
        }

        RemoveActivity(ActivityId);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Relaie <c>WM_CLIPBOARDUPDATE</c>. Le message ne transporte aucune donnée :
    /// il signale seulement que le presse-papier a changé.
    /// </summary>
    public override bool TryHandleWindowMessage(uint messageId, nuint wParam)
    {
        if (messageId != NativeConstants.WM_CLIPBOARDUPDATE)
        {
            return false;
        }

        _monitor.OnClipboardMessageReceived();
        return true;
    }

    public override Task<bool> HandleActionAsync(IslandActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        switch (request.ActionId)
        {
            case PasteAction:
                return Task.FromResult(Paste(request.Value));

            case PinAction:
                return Task.FromResult(SetPinned(request.Value));

            case RemoveAction:
                return Task.FromResult(Remove(request.Value));

            default:
                return Task.FromResult(false);
        }
    }

    private void OnClipboardUpdated()
    {
        if (!ClipboardAccess.TryReadText(out string text) || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        // Le signal système se déclenche aussi lorsque c'est nous qui écrivons :
        // sans ce filtre, coller une entrée créerait une capture de sa propre
        // écriture, et l'historique se nourrirait de lui-même.
        lock (_lock)
        {
            if (_entries.Count > 0 && string.Equals(_entries[0].Content, text, StringComparison.Ordinal))
            {
                return;
            }

            _entries.Insert(0, new Entry(
                Id: Guid.NewGuid().ToString("N"),
                Content: text,
                Kind: Classify(text),
                IsPinned: false));

            Trim();
        }

        PublishEvent(new ClipboardChangedEvent(Kind: "changed", Preview: null));
        Publish();
    }

    private bool Paste(string? entryId)
    {
        string? content = FindContent(entryId);

        if (content is null)
        {
            return false;
        }

        bool written = ClipboardAccess.SetText(content);

        if (written)
        {
            // La capture correspond à l'entrée déjà présente : le filtre de
            // OnClipboardUpdated empêche le doublon.
            RemoveActivity(ActivityId);
        }

        return written;
    }

    private bool SetPinned(string? entryId)
    {
        lock (_lock)
        {
            int index = IndexOf(entryId);

            if (index < 0)
            {
                return false;
            }

            _entries[index] = _entries[index] with { IsPinned = !_entries[index].IsPinned };
        }

        Publish();
        return true;
    }

    private bool Remove(string? entryId)
    {
        lock (_lock)
        {
            int index = IndexOf(entryId);

            if (index < 0)
            {
                return false;
            }

            _entries.RemoveAt(index);
        }

        Publish();
        return true;
    }

    private string? FindContent(string? entryId)
    {
        lock (_lock)
        {
            int index = IndexOf(entryId);
            return index < 0 ? null : _entries[index].Content;
        }
    }

    private int IndexOf(string? entryId)
    {
        if (string.IsNullOrEmpty(entryId))
        {
            return -1;
        }

        return _entries.FindIndex(e => string.Equals(e.Id, entryId, StringComparison.Ordinal));
    }

    /// <summary>
    /// Évince les entrées les plus anciennes au-delà de la capacité, en
    /// préservant les entrées épinglées : c'est le sens même d'une épingle.
    /// </summary>
    private void Trim()
    {
        for (int i = _entries.Count - 1; i >= 0 && _entries.Count > _capacity; i--)
        {
            if (!_entries[i].IsPinned)
            {
                _entries.RemoveAt(i);
            }
        }
    }

    private void Publish()
    {
        List<ClipboardEntry> previews;

        lock (_lock)
        {
            previews = _entries
                .Select(e => new ClipboardEntry(e.Id, e.Kind, BuildPreview(e.Content), e.IsPinned))
                .ToList();
        }

        PublishActivity(new IslandActivity
        {
            Id = ActivityId,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Clipboard,
            Title = $"{previews.Count} élément{(previews.Count > 1 ? "s" : string.Empty)}",
            Subtitle = "Presse-papier",
            Source = "Clipboard",
            IconKey = "Clipboard",
            State = IslandActivityState.Idle,
            Priority = ActivityPriority.Normal,
            Actions =
            [
                new ActivityAction(PasteAction, "Coller", "Paste"),
                new ActivityAction(PinAction, "Épingler", "Pin"),
                new ActivityAction(RemoveAction, "Supprimer", "Delete")
            ],
            Payload = new ClipboardPayload(previews)
        });
    }

    private static string BuildPreview(string content)
    {
        string flattened = content.Replace('\r', ' ').Replace('\n', ' ').Trim();

        return flattened.Length <= PreviewLength
            ? flattened
            : string.Concat(flattened.AsSpan(0, PreviewLength), "…");
    }

    private static string Classify(string content)
        => content.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || content.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? "Lien"
                : "Texte";

    /// <summary>
    /// Entrée d'historique. Le contenu complet n'existe qu'ici et n'est jamais
    /// projeté dans une activité.
    /// </summary>
    private sealed record Entry(string Id, string Content, string Kind, bool IsPinned);
}
