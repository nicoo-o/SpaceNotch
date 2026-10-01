using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Assistant;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Features;
using SpaceNotch.Core.Localization;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;
using SpaceNotch.Platform.Windows.Clipboard;
using SpaceNotch.Platform.Windows.Win32;

namespace SpaceNotch.Features.Assistant;

/// <summary>
/// Actions sur ce qu'on copie (I3). Désactivée par défaut : rien n'est lu
/// tant que l'utilisateur ne l'a pas voulu, rien n'est envoyé sans un clic, et
/// ce que les gestionnaires de mots de passe marquent n'est jamais lu.
///
/// <para>
/// Une copie de phrase fait apparaître une pastille discrète : « Traduire ·
/// Répondre · Rappel ». Un clic demande au modèle choisi ; le résultat
/// remplace le presse-papier. Le rappel, lui, se fait sans modèle.
/// </para>
/// </summary>
public sealed class CopyAssistFeature : IslandFeatureBase
{
    public const string FeatureKey = FeatureKeys.CopyAssist;

    public const string ActivityId = "feature.copy-assist.current";

    public const string ActionPrefix = "copy.";

    private static readonly ActivityTint Lilac = new(0xB9, 0xA8, 0xFF);

    private readonly ClipboardMonitor _monitor;
    private readonly IntPtr _windowHandle;
    private readonly Func<DateTimeOffset> _now;
    private string? _copied;
    private string? _written;
    private int _busy;

    public CopyAssistFeature(IActivityManager activities, IEventBus events, ClipboardMonitor monitor, IntPtr windowHandle, bool isEnabled, Func<DateTimeOffset>? now = null)
        : base(FeatureKey, "Actions sur copie", activities, events, isEnabled)
    {
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _windowHandle = windowHandle;
        _now = now ?? (() => DateTimeOffset.Now);
    }

    /// <summary>Le modèle choisi, ou <c>null</c> : seul le rappel est alors proposé.</summary>
    public Func<AssistantRequest, Task<string?>>? Ask { get; set; }

    /// <summary>Pose un rappel (fourni par <see cref="ReminderFeature"/>).</summary>
    public Func<NaturalIntent, Reminder?>? AddReminder { get; set; }

    /// <summary>« fr » ou « en » : la langue des traductions.</summary>
    public static string UiLanguage => Lang.French ? "fr" : "en";

    protected override Task OnStartAsync(CancellationToken cancellationToken)
    {
        _monitor.Start(_windowHandle);
        _monitor.ClipboardUpdated += OnClipboardUpdated;
        return Task.CompletedTask;
    }

    protected override Task OnStopAsync()
    {
        _monitor.ClipboardUpdated -= OnClipboardUpdated;
        _monitor.Stop();
        _copied = null;
        RemoveActivity(ActivityId);
        return Task.CompletedTask;
    }

    public override bool TryHandleWindowMessage(uint messageId, nuint wParam)
    {
        if (messageId != NativeConstants.WM_CLIPBOARDUPDATE)
        {
            return false;
        }

        _monitor.OnClipboardMessageReceived();
        return true;
    }

    private void OnClipboardUpdated()
    {
        // Jamais un secret : la marque des gestionnaires de mots de passe est respectée, toujours.
        if (!ClipboardAccess.TryReadTextForHistory(out string text, out _) || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        // Notre propre écriture (un résultat) ne se propose pas à elle-même.
        if (string.Equals(text, _written, StringComparison.Ordinal))
        {
            return;
        }

        Offer(text);
    }

    /// <summary>Une copie : les actions possibles, en pastille. Appelable directement (tests, visite).</summary>
    public void Offer(string text)
    {
        IReadOnlyList<CopyAction> actions = CopyActions.Offer(text, UiLanguage, Ask is not null, _now());

        if (actions.Count == 0)
        {
            return;
        }

        _copied = text;
        PublishActivity(new IslandActivity
        {
            Id = ActivityId,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Card,
            Title = Lang.T("Texte copié", "Text copied"),
            Subtitle = Preview(text),
            Source = Lang.T("Presse-papier", "Clipboard"),
            IconKey = "Clipboard",
            Tint = Lilac,
            State = IslandActivityState.Idle,
            Priority = ActivityPriority.Normal,
            Policy = ActivityPresentationPolicy.Passive,
            Duration = TimeSpan.FromSeconds(12),
            Actions = [.. actions.Select((a, i) => new ActivityAction(ActionPrefix + a.ToString().ToLowerInvariant(), CopyActions.Label(a, Lang.French), Icon(a), ActivityActionKind.Invoke, IsPrimary: i == 0))]
        });
    }

    public override async Task<bool> HandleActionAsync(IslandActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ActivityId != ActivityId || !request.ActionId.StartsWith(ActionPrefix, StringComparison.Ordinal) || _copied is not { } text)
        {
            return false;
        }

        if (!Enum.TryParse(request.ActionId[ActionPrefix.Length..], ignoreCase: true, out CopyAction action))
        {
            return false;
        }

        if (action == CopyAction.Remind)
        {
            Remind(text);
            return true;
        }

        if (Ask is not { } ask || Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return true;
        }

        try
        {
            PublishActivity(Working(action, text));

            string? result = CopyActions.Clean(await ask(CopyActions.Prompt(action, text, UiLanguage)).ConfigureAwait(false));

            if (result is null)
            {
                Publish(Lang.T("Pas de réponse", "No answer"), Lang.T("Le modèle n'a pas répondu", "The model did not answer"), ActivityMotionState.Error);
                return true;
            }

            _written = result;
            ClipboardAccess.SetText(result);

            string what = action switch
            {
                CopyAction.Translate => Lang.T("Traduction copiée", "Translation copied"),
                CopyAction.Summarize => Lang.T("Résumé copié", "Summary copied"),
                _ => Lang.T("Réponse copiée", "Reply copied")
            };

            Publish(what, Preview(result), ActivityMotionState.Completing);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }

        return true;
    }

    private void Remind(string text)
    {
        if (CopyActions.ReminderFrom(text, _now()) is not { } intent || AddReminder?.Invoke(intent) is not { } reminder)
        {
            Publish(Lang.T("Pas d'échéance", "No deadline"), Lang.T("Aucune date dans ce texte", "No date in this text"), ActivityMotionState.Idle);
            return;
        }

        string when = reminder.At.ToLocalTime().ToString(Lang.French ? "dddd HH:mm" : "dddd h:mm tt", Lang.French ? CultureInfo.GetCultureInfo("fr-FR") : CultureInfo.GetCultureInfo("en-US"));
        Publish(Lang.T("Rappel posé · ", "Reminder set · ") + when, reminder.Text, ActivityMotionState.Completing);
    }

    private static IslandActivity Working(CopyAction action, string text) => new()
    {
        Id = ActivityId,
        FeatureId = FeatureKey,
        SceneKey = IslandSceneCatalog.Card,
        Title = action switch
        {
            CopyAction.Translate => Lang.T("Traduction…", "Translating…"),
            CopyAction.Summarize => Lang.T("Résumé…", "Summarizing…"),
            _ => Lang.T("Réponse…", "Replying…")
        },
        Subtitle = Preview(text),
        Source = Lang.T("Presse-papier", "Clipboard"),
        IconKey = "Clipboard",
        Tint = Lilac,
        State = IslandActivityState.Idle,
        MotionState = ActivityMotionState.Working,
        MotionPreset = HypnoticPreset.Think,
        Priority = ActivityPriority.Normal,
        Duration = TimeSpan.FromSeconds(60)
    };

    private void Publish(string title, string subtitle, ActivityMotionState motion) => PublishActivity(new IslandActivity
    {
        Id = ActivityId,
        FeatureId = FeatureKey,
        SceneKey = IslandSceneCatalog.Card,
        Title = title,
        Subtitle = subtitle,
        Source = Lang.T("Presse-papier", "Clipboard"),
        IconKey = "Clipboard",
        Metric = motion == ActivityMotionState.Completing ? "✓" : null,
        Tint = Lilac,
        State = IslandActivityState.Idle,
        MotionState = motion,
        Priority = ActivityPriority.Normal,
        Duration = TimeSpan.FromSeconds(6)
    });

    private static string Icon(CopyAction action) => action switch
    {
        CopyAction.Translate => "Globe",
        CopyAction.Summarize => "Text",
        CopyAction.Reply => "Message",
        _ => "Clock"
    };

    private static string Preview(string text)
    {
        string line = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= 56 ? line : string.Concat(line.AsSpan(0, 55), "…");
    }
}
