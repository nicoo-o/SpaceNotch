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
using SpaceNotch.Core.Localization;
using SpaceNotch.Core.Presentation;
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

    /// <summary>Couleur copiée (F5) : l'activité qui montre la nuance.</summary>
    public const string ColorActivityId = "feature.clipboard.color";

    /// <summary>Recopier un format de la couleur (valeur : le texte à copier).</summary>
    public const string CopyTextAction = "clipboard.copy-text";

    /// <summary>Presse-papier en pile (vague 7) : l'activité de la pile.</summary>
    public const string StackActivityId = "feature.clipboard.stack";

    /// <summary>Recolle l'élément de devant de la pile.</summary>
    public const string StackPasteAction = "clipboard.stack-paste";

    /// <summary>Longueur maximale d'une prévisualisation, en caractères.</summary>
    private const int PreviewLength = 120;

    private readonly ClipboardMonitor _monitor;
    private readonly IntPtr _windowHandle;
    private readonly int _capacity;

    private readonly List<Entry> _entries = [];
    private (Entry Entry, int Index)? _lastRemoved;
    private int _stackIndex;
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

            case StackPasteAction:
                return Task.FromResult(PasteFromStack());

            case CopyTextAction when !string.IsNullOrEmpty(request.Value):
                return Task.FromResult(ClipboardAccess.SetText(request.Value));

            default:
                return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Vrai (par défaut) : ce que les gestionnaires de mots de passe marquent
    /// « ne pas enregistrer » reste hors de l'historique. Réglages › Activités.
    /// </summary>
    public bool IgnoreSecrets { get; set; } = true;

    /// <summary>Vide l'historique, épinglés compris (l'historique ne vit qu'en mémoire).</summary>
    public void ClearAll()
    {
        lock (_lock)
        {
            _entries.Clear();
        }

        RemoveActivity(ActivityId);
    }

    private void OnClipboardUpdated()
    {
        // Un mot de passe copié depuis un gestionnaire porte une marque « ne pas
        // enregistrer » : il n'entre jamais dans l'historique (réglable).
        bool read = IgnoreSecrets
            ? ClipboardAccess.TryReadTextForHistory(out string text, out _)
            : ClipboardAccess.TryReadText(out text);

        if (!read || string.IsNullOrWhiteSpace(text))
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
        PublishColor(text);
    }

    /// <summary>
    /// Couleur copiée (F5) : un code couleur seul dans le presse-papier fait
    /// apparaître la nuance dans la notch ; ouverte, elle donne HEX, RGB et HSL.
    /// </summary>
    private void PublishColor(string text)
    {
        if (!ColorCode.TryParse(text, out ColorCode color))
        {
            return;
        }

        PublishActivity(new IslandActivity
        {
            Id = ColorActivityId,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Color,
            Title = color.Hex,
            Subtitle = Lang.T("Couleur copiée", "Colour copied"),
            Source = Lang.T("Presse-papier", "Clipboard"),
            IconKey = "Palette",
            Tint = new ActivityTint(color.R, color.G, color.B),
            State = IslandActivityState.Idle,
            Priority = ActivityPriority.Normal,
            Policy = ActivityPresentationPolicy.Passive,
            Duration = TimeSpan.FromSeconds(10),
            Payload = new ColorPayload(color)
        });
    }

    private bool Paste(string? entryId)
    {
        string? content = Recall(entryId);

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

            _lastRemoved = (_entries[index], index);
            _entries.RemoveAt(index);
        }

        Publish();
        return true;
    }

    /// <summary>Visite filmée : des entrées de démonstration, la machine de tournage n'ayant rien copié.</summary>
    public void AddForTour(string content, string kind)
    {
        lock (_lock)
        {
            _entries.Insert(0, new Entry(Guid.NewGuid().ToString("N"), content, kind, false));
        }
    }

    /// <summary>Annuler en 3 s (vague 7) : la dernière entrée supprimée reprend sa place.</summary>
    public bool UndoRemove()
    {
        lock (_lock)
        {
            if (_lastRemoved is not { } removed)
            {
                return false;
            }

            _entries.Insert(Math.Clamp(removed.Index, 0, _entries.Count), removed.Entry);
            _lastRemoved = null;
        }

        Publish();
        return true;
    }

    // ---- Presse-papier en pile (vague 7) ------------------------------------

    /// <summary>Ouvre la pile, ou la fait défiler d'un cran : Ctrl + molette sur la notch.</summary>
    public bool ShowStack(int step)
    {
        List<ClipboardEntry> previews = Previews();

        if (previews.Count == 0)
        {
            return false;
        }

        _stackIndex = (((_stackIndex + step) % previews.Count) + previews.Count) % previews.Count;
        PublishStack(previews, recalled: false);
        return true;
    }

    /// <summary>Ferme la pile ; elle repart du plus récent la prochaine fois.</summary>
    public void HideStack()
    {
        _stackIndex = 0;
        RemoveActivity(StackActivityId);
    }

    private bool PasteFromStack()
    {
        List<ClipboardEntry> previews = Previews();

        if (previews.Count == 0)
        {
            return false;
        }

        ClipboardEntry front = previews[_stackIndex % previews.Count];
        string? content = Recall(front.Id);

        if (content is null || !ClipboardAccess.SetText(content))
        {
            return false;
        }

        // L'entrée recollée est passée en tête : la pile la garde devant.
        _stackIndex = 0;
        PublishStack(Previews(), recalled: true);
        return true;
    }

    private void PublishStack(List<ClipboardEntry> previews, bool recalled)
    {
        ClipboardEntry front = previews[_stackIndex % previews.Count];

        PublishActivity(new IslandActivity
        {
            Id = StackActivityId,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.ClipStack,
            Title = front.Preview,
            Subtitle = recalled ? Lang.T("Recollé", "Pasted again") : Lang.T($"{_stackIndex + 1} sur {previews.Count}", $"{_stackIndex + 1} of {previews.Count}"),
            Source = "Clipboard",
            IconKey = "Clipboard",
            State = IslandActivityState.Idle,
            Priority = ActivityPriority.Normal,
            Duration = TimeSpan.FromSeconds(recalled ? 1.2 : 8),
            Payload = new ClipStackPayload(previews, _stackIndex, recalled)
        });
    }

    private List<ClipboardEntry> Previews()
    {
        lock (_lock)
        {
            return _entries.Select(e => new ClipboardEntry(e.Id, e.Kind, BuildPreview(e.Content), e.IsPinned)).ToList();
        }
    }

    /// <summary>
    /// Recoller une entrée la remonte en tête de l'historique avant l'écriture :
    /// le filtre de <see cref="OnClipboardUpdated"/> reconnaît alors sa propre
    /// écriture, sans doublon ni pastille « copié » (ou couleur) de plus.
    /// </summary>
    private string? Recall(string? entryId)
    {
        lock (_lock)
        {
            int index = IndexOf(entryId);

            if (index < 0)
            {
                return null;
            }

            Entry entry = _entries[index];

            if (index > 0)
            {
                _entries.RemoveAt(index);
                _entries.Insert(0, entry);
            }

            return entry.Content;
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
            Title = Lang.T($"{previews.Count} élément{(previews.Count > 1 ? "s" : string.Empty)}", $"{previews.Count} item{(previews.Count == 1 ? string.Empty : "s")}"),
            Subtitle = Lang.T("Presse-papier", "Clipboard"),
            Source = "Clipboard",
            IconKey = "Clipboard",
            State = IslandActivityState.Idle,
            Priority = ActivityPriority.Normal,
            Actions =
            [
                new ActivityAction(PasteAction, Lang.T("Coller", "Paste"), "Paste"),
                new ActivityAction(PinAction, Lang.T("Épingler", "Pin"), "Pin"),
                new ActivityAction(RemoveAction, Lang.T("Supprimer", "Delete"), "Delete")
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
                ? Lang.T("Lien", "Link")
                : Lang.T("Texte", "Text");

    /// <summary>
    /// Entrée d'historique. Le contenu complet n'existe qu'ici et n'est jamais
    /// projeté dans une activité.
    /// </summary>
    private sealed record Entry(string Id, string Content, string Kind, bool IsPinned);
}
