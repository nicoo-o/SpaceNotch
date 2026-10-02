using System;
using System.Globalization;
using System.IO;
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

namespace SpaceNotch.Features.Assistant;

/// <summary>
/// Rappels (I2, I3) : « rappelle-moi d'appeler Paul à 17 h » dans le lanceur,
/// ou une échéance lue dans un texte copié. La notch garde un petit rappel
/// discret jusqu'à l'heure, puis s'ouvre : « OK » ou « +10 min ».
///
/// <para>
/// Le minuteur ne bat qu'une fois par minute tant qu'un rappel attend, pour
/// le compte à rebours ; sans rappel, rien ne tourne. Le carnet est écrit sur
/// le disque à chaque changement et relu au démarrage.
/// </para>
/// </summary>
public sealed class ReminderFeature : IslandFeatureBase
{
    public const string FeatureKey = FeatureKeys.Reminders;

    public const string NextActivityId = "feature.reminders.next";

    public const string DueActivityPrefix = "feature.reminders.due.";

    public const string DoneAction = "reminder.done";

    public const string SnoozeAction = "reminder.snooze";

    public static readonly TimeSpan Snooze = TimeSpan.FromMinutes(10);

    private static readonly ActivityTint Amber = new(0xFF, 0xB2, 0x6B);

    private static string Capitalize(string text)
        => text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.CurrentCulture) + text[1..];

    private readonly object _gate = new();
    private readonly string? _path;
    private readonly Func<DateTimeOffset> _now;
    private readonly Timer _timer;
    private ReminderBook _book;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Reminder> _ringing = new(StringComparer.Ordinal);

    public ReminderFeature(IActivityManager activities, IEventBus events, string? folder, Func<DateTimeOffset>? now = null)
        : base(FeatureKey, "Rappels", activities, events, isEnabled: true)
    {
        _now = now ?? (() => DateTimeOffset.Now);
        _path = folder is null ? null : Path.Combine(folder, "reminders.json");
        _book = Load(_path);
        _timer = new Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Les rappels en attente.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _book.Items.Count;
            }
        }
    }

    /// <summary>Pose un rappel. Rend <c>null</c> si l'intention n'en est pas un.</summary>
    public Reminder? Add(NaturalIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);

        if (intent is not { Kind: NaturalKind.Reminder, Text: { Length: > 0 } text, At: { } at })
        {
            return null;
        }

        Reminder reminder;

        lock (_gate)
        {
            reminder = _book.Add(text, at);
            Save();
        }

        Tick();
        return reminder;
    }

    /// <summary>Oublie tous les rappels, en attente comme en train de sonner.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _book = new ReminderBook();
            Save();
        }

        foreach (string id in _ringing.Keys)
        {
            RemoveActivity(id);
        }

        _ringing.Clear();
        Tick();
    }

    protected override Task OnStartAsync(CancellationToken cancellationToken)
    {
        Tick();
        return Task.CompletedTask;
    }

    protected override Task OnStopAsync()
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        RemoveActivity(NextActivityId);
        return Task.CompletedTask;
    }

    protected override void OnDisposed() => _timer.Dispose();

    public override Task<bool> HandleActionAsync(IslandActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.ActivityId.StartsWith(DueActivityPrefix, StringComparison.Ordinal))
        {
            return Task.FromResult(false);
        }

        if (!_ringing.TryRemove(request.ActivityId, out Reminder? ringing))
        {
            return Task.FromResult(false);
        }

        if (request.ActionId == SnoozeAction)
        {
            lock (_gate)
            {
                _book.Add(ringing.Text, _now() + Snooze);
                Save();
            }
        }

        RemoveActivity(request.ActivityId);
        Tick();
        return Task.FromResult(request.ActionId is DoneAction or SnoozeAction);
    }

    /// <summary>Fait sonner ce qui est échu et met à jour le rappel discret. Appelable directement (tests, visite).</summary>
    public void Tick()
    {
        DateTimeOffset now = _now();
        Reminder[] due;
        Reminder? next;

        lock (_gate)
        {
            due = [.. _book.TakeDue(now)];

            if (due.Length > 0)
            {
                Save();
            }

            next = _book.Next;
        }

        foreach (Reminder reminder in due)
        {
            _ringing[DueActivityPrefix + reminder.Id] = reminder;
            PublishActivity(Due(reminder));
            PublishEvent(new NotificationPostedEvent(Lang.T("Rappel", "Reminder"), reminder.Text, reminder.At.ToLocalTime().ToString("t", CultureInfo.CurrentCulture)));
        }

        if (next is null)
        {
            RemoveActivity(NextActivityId);
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            return;
        }

        PublishActivity(Upcoming(next, now));

        // Une minute au plus : le compte à rebours, et l'heure exacte du rappel.
        TimeSpan wait = next.At - now;
        wait = wait < TimeSpan.FromMinutes(1) ? (wait < TimeSpan.Zero ? TimeSpan.Zero : wait) : TimeSpan.FromMinutes(1);
        _timer.Change(wait + TimeSpan.FromMilliseconds(200), Timeout.InfiniteTimeSpan);
    }

    /// <summary>« 2 h », « 35 min », « 1 min ».</summary>
    public static string Countdown(TimeSpan left)
        => left.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)Math.Round(left.TotalHours)} h")
            : string.Create(CultureInfo.InvariantCulture, $"{Math.Max(1, (int)Math.Ceiling(left.TotalMinutes))} min");

    // Maquette I2 : une cloche orange, « Appeler Paul », l'heure à droite.
    private static IslandActivity Upcoming(Reminder reminder, DateTimeOffset now) => new()
    {
        Id = NextActivityId,
        FeatureId = FeatureKey,
        SceneKey = IslandSceneCatalog.Card,
        Title = Capitalize(reminder.Text),
        Subtitle = Lang.T("Rappel dans ", "Reminder in ") + Countdown(reminder.At - now),
        Source = Lang.T("Rappel", "Reminder"),
        IconKey = "Notification",
        Metric = reminder.At.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture),
        Layout = ActivityLayout.Row,
        Tint = Amber,
        State = IslandActivityState.TimerActive,
        Priority = ActivityPriority.Background,
        Policy = ActivityPresentationPolicy.Passive
    };

    private static IslandActivity Due(Reminder reminder) => new()
    {
        Id = DueActivityPrefix + reminder.Id,
        FeatureId = FeatureKey,
        SceneKey = IslandSceneCatalog.Card,
        Title = Capitalize(reminder.Text),
        Subtitle = Lang.T("C'est l'heure", "It's time"),
        Layout = ActivityLayout.Row,
        Eyebrow = Lang.T("Rappel · ", "Reminder · ") + reminder.At.ToLocalTime().ToString("t", CultureInfo.CurrentCulture),
        Source = Lang.T("Rappel", "Reminder"),
        IconKey = "Clock",
        Tint = Amber,
        State = IslandActivityState.TimerActive,
        MotionState = ActivityMotionState.Attention,
        Priority = ActivityPriority.High,
        Duration = TimeSpan.FromMinutes(5),
        Actions =
        [
            new ActivityAction(DoneAction, "OK", "Check", ActivityActionKind.Invoke, IsPrimary: true, Tone: ActivityActionTone.Positive),
            new ActivityAction(SnoozeAction, "+10 min", "Timer")
        ]
    };

    private static ReminderBook Load(string? path)
    {
        try
        {
            return path is not null && File.Exists(path) ? ReminderBook.Parse(File.ReadAllText(path)) : new ReminderBook();
        }
        catch (IOException)
        {
            return new ReminderBook();
        }
    }

    private void Save()
    {
        if (_path is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temporary = _path + ".tmp";
            File.WriteAllText(temporary, _book.Serialize());
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ReportError(ex);
        }
    }
}
