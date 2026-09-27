using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Features;
using SpaceNotch.Core.Localization;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;

namespace SpaceNotch.Features.Productivity;

/// <summary>
/// Note éclair (F7) : un double-clic sur la notch ouvre un petit bloc-notes,
/// directement dedans. Le texte s'enregistre tout seul dans le dossier de
/// SpaceNotch ; Échap ou un clic ailleurs referme, la note reste là au
/// prochain double-clic et dans le menu rapide.
/// </summary>
public sealed class NoteFeature : IslandFeatureBase
{
    public const string FeatureKey = "feature.note";

    public const string ActivityId = "feature.note.current";

    /// <summary>Enregistrer le texte (valeur : le texte entier).</summary>
    public const string SaveAction = "note.save";

    /// <summary>Longueur maximale gardée : une note, pas un document.</summary>
    public const int MaxLength = 4000;

    private readonly string _path;
    private readonly Lock _gate = new();
    private string _text;

    public NoteFeature(IActivityManager activities, IEventBus events, string directory)
        : base(FeatureKey, Lang.T("Note", "Note"), activities, events, isEnabled: true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _path = Path.Combine(directory, "note.txt");
        _text = Read(_path);
    }

    /// <summary>Texte actuel de la note.</summary>
    public string Text
    {
        get
        {
            lock (_gate)
            {
                return _text;
            }
        }
    }

    public bool IsShown { get; private set; }

    /// <summary>Ouvre la note dans la notch.</summary>
    public void Show()
    {
        IsShown = true;
        string text = Text;

        PublishActivity(new IslandActivity
        {
            Id = ActivityId,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Note,
            Title = Lang.T("Note", "Note"),
            Subtitle = text.Length == 0 ? Lang.T("Vide", "Empty") : FirstLine(text),
            IconKey = "Menu",
            State = IslandActivityState.Idle,
            Priority = ActivityPriority.High,
            Policy = ActivityPresentationPolicy.Passive,
            Payload = new NotePayload(text)
        });
    }

    /// <summary>Referme la note ; le texte reste enregistré.</summary>
    public void Dismiss()
    {
        if (!IsShown)
        {
            return;
        }

        IsShown = false;
        RemoveActivity(ActivityId);
    }

    /// <summary>Enregistre le texte, sur disque par un fichier voisin puis substitué.</summary>
    public bool Save(string? text)
    {
        string value = (text ?? string.Empty).Length > MaxLength ? text![..MaxLength] : text ?? string.Empty;

        lock (_gate)
        {
            if (string.Equals(value, _text, StringComparison.Ordinal))
            {
                return true;
            }

            _text = value;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                string temp = _path + ".tmp";
                File.WriteAllText(temp, value);
                File.Move(temp, _path, overwrite: true);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    public override Task<bool> HandleActionAsync(IslandActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(request.ActionId == SaveAction && Save(request.Value));
    }

    protected override Task OnStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    protected override Task OnStopAsync()
    {
        Dismiss();
        return Task.CompletedTask;
    }

    private static string FirstLine(string text)
    {
        string line = text.Split('\n', 2)[0].Trim();
        return line.Length > 40 ? line[..40] + "…" : line;
    }

    private static string Read(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }
}
