using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Calendar;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Features;
using SpaceNotch.Core.Localization;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;
using SpaceNotch.Platform.Windows.Calendar;

namespace SpaceNotch.Features.Calendar;

/// <summary>
/// Prochain rendez-vous (F2) : cinq minutes avant une réunion, une pastille
/// compte à rebours avec un anneau qui se vide ; à l'heure, « Rejoindre »
/// ouvre Teams, Meet, Zoom ou Webex.
///
/// <para>
/// Pas de scrutation fine : la fonctionnalité se réveille au prochain
/// changement de phase (<see cref="MeetingCountdown.NextChange"/>), quand le
/// calendrier change, et au plus tard toutes les dix minutes pour découvrir
/// une invitation arrivée sans événement. Pendant les cinq minutes du compte
/// à rebours seulement, l'anneau avance toutes les cinq secondes.
/// </para>
/// </summary>
public sealed class MeetingFeature : IslandFeatureBase
{
    public const string FeatureKey = FeatureKeys.Meeting;

    public const string ActivityId = "feature.meeting.next";

    public const string JoinAction = "meeting.join";

    /// <summary>Silence de réunion (W2) : couper les notifications jusqu'à la fin du rendez-vous.</summary>
    public const string QuietAction = "meeting.quiet";

    public static readonly ActivityTint Blue = new(0x7F, 0xB8, 0xFF);

    private static readonly TimeSpan Rescan = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RingStep = TimeSpan.FromSeconds(5);

    private readonly CalendarReader? _reader;
    private readonly Func<DateTimeOffset> _now;
    private readonly Timer _timer;
    private CalendarMeeting? _meeting;
    private Uri? _link;

    /// <summary>Demande de silence jusqu'à l'heure donnée (W2). L'hôte la transmet aux notifications.</summary>
    public event Action<DateTimeOffset>? QuietRequested;

    /// <summary>Vrai si les notifications sont déjà coupées : la proposition n'a alors pas lieu d'être.</summary>
    public Func<bool> IsQuiet { get; set; } = () => false;

    /// <summary>Le rendez-vous montré, pour le focus calé sur l'agenda (W3).</summary>
    public CalendarMeeting? Current => _meeting;

    public MeetingFeature(IActivityManager activities, IEventBus events, CalendarReader? reader, bool isEnabled = true, Func<DateTimeOffset>? now = null)
        : base(FeatureKey, "Rendez-vous", activities, events, isEnabled)
    {
        _reader = reader;
        _now = now ?? (() => DateTimeOffset.Now);
        _timer = new Timer(_ => _ = RefreshAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    protected override Task OnStartAsync(CancellationToken cancellationToken)
    {
        if (_reader is not null)
        {
            _reader.Changed += OnCalendarChanged;
        }

        _ = RefreshAsync();
        return Task.CompletedTask;
    }

    protected override Task OnStopAsync()
    {
        if (_reader is not null)
        {
            _reader.Changed -= OnCalendarChanged;
        }

        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        RemoveActivity(ActivityId);
        return Task.CompletedTask;
    }

    protected override void OnDisposed() => _timer.Dispose();

    private void OnCalendarChanged() => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        try
        {
            CalendarMeeting? meeting = _reader is null ? _meeting : await _reader.NextAsync(_now()).ConfigureAwait(false);
            Show(meeting);
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }

    /// <summary>Montre (ou retire) le rendez-vous selon l'heure. Appelable directement (tests, visite).</summary>
    /// <summary>Hauteur ajoutée à la carte quand le miroir (W5) est ouvert sous « Rejoindre ».</summary>
    public const double MirrorHeight = 104;

    private bool _mirror;

    /// <summary>Ouvre ou ferme la place du miroir sous les contrôles ; la carte grandit vers le bas.</summary>
    public void SetMirror(bool open)
    {
        if (_mirror == open)
        {
            return;
        }

        _mirror = open;

        if (_meeting is not null)
        {
            Show(_meeting);
        }
    }

    public void Show(CalendarMeeting? meeting)
    {
        DateTimeOffset now = _now();
        _meeting = meeting;
        _link = meeting is null ? null : MeetingLink.Find(meeting.OnlineLink, meeting.Location, meeting.Details);

        MeetingPhase phase = meeting is null ? MeetingPhase.None : MeetingCountdown.Phase(meeting.Start, meeting.End, now);
        TimeSpan wake = Rescan;

        switch (phase)
        {
            case MeetingPhase.Soon:
                Publish(meeting!, now, soon: true);
                wake = RingStep;
                break;

            case MeetingPhase.Now:
                Publish(meeting!, now, soon: false);
                break;

            default:
                RemoveActivity(ActivityId);
                break;
        }

        if (meeting is not null && MeetingCountdown.NextChange(meeting.Start, meeting.End, now) is { } next && next - now < wake)
        {
            wake = next - now + TimeSpan.FromMilliseconds(200);
        }

        _timer.Change(wake, Timeout.InfiniteTimeSpan);
    }

    public override Task<bool> HandleActionAsync(IslandActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ActionId == QuietAction && _meeting is { } meeting)
        {
            QuietRequested?.Invoke(MeetingQuiet.Until(meeting.Start, meeting.End));
            Show(meeting);
            return Task.FromResult(true);
        }

        if (request.ActionId != JoinAction || _link is null)
        {
            return Task.FromResult(false);
        }

        try
        {
            Process.Start(new ProcessStartInfo(_link.AbsoluteUri) { UseShellExecute = true });
            RemoveActivity(ActivityId);
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }

        return Task.FromResult(true);
    }

    private void Publish(CalendarMeeting meeting, DateTimeOffset now, bool soon)
    {
        bool french = Lang.French;
        string service = _link is null ? string.Empty : " · " + MeetingLink.ServiceOf(_link);

        PublishActivity(new IslandActivity
        {
            Id = ActivityId,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Card,
            Title = string.IsNullOrWhiteSpace(meeting.Subject) ? Lang.T("Rendez-vous", "Meeting") : meeting.Subject,
            Subtitle = (soon ? MeetingCountdown.Label(meeting.Start, now, french) : Lang.T("Maintenant", "Now")) + service,
            Source = Lang.T("Calendrier", "Calendar"),

            // Une ligne, comme la maquette (W2, W5) : icône, titre, contrôles à droite.
            Layout = ActivityLayout.Row,
            ShowEnterHint = _mirror && _link is not null,
            IconKey = "Calendar",
            Tint = Blue,
            Progress = soon ? MeetingCountdown.Remaining(meeting.Start, now) : null,

            // L'anneau se vide ; à côté, le temps qui reste plutôt qu'un pourcentage.
            Metric = soon ? MeetingCountdown.TimeLeft(meeting.Start, now) : null,
            State = IslandActivityState.Idle,
            Priority = soon ? ActivityPriority.Normal : ActivityPriority.High,
            Policy = ActivityPresentationPolicy.Passive,
            Actions = Actions(meeting, now),
            ExpandedFootprint = SceneInsets.Wrap(380, 44 + (_mirror && _link is not null ? MirrorHeight : 0))
        });
    }

    private List<ActivityAction> Actions(CalendarMeeting meeting, DateTimeOffset now)
    {
        var actions = new List<ActivityAction>();

        if (_link is not null)
        {
            actions.Add(new ActivityAction(JoinAction, Lang.T("Rejoindre", "Join"), "Call", ActivityActionKind.Invoke, IsPrimary: true, Tone: ActivityActionTone.Positive));
        }

        if (MeetingQuiet.Offer(meeting.Start, meeting.End, now, IsQuiet()))
        {
            // « Silence jusqu'à 11:00 » : l'heure de fin dit ce que le clic va faire.
            string until = meeting.End.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);
            actions.Add(new ActivityAction(QuietAction, Lang.T("Silence jusqu'à ", "Quiet until ") + until, "Moon"));
        }

        return actions;
    }
}
