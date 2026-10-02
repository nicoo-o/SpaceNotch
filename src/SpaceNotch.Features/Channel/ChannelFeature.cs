using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Channel;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Features;
using SpaceNotch.Core.Localization;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;
using SpaceNotch.Platform.Windows.Channel;

namespace SpaceNotch.Features.Channel;

/// <summary>
/// Le canal local dans la notch (ADR-024) :
///
/// <list type="bullet">
/// <item>Agents IA (I4) — Claude Code réfléchit, la grille pense ; il demande
/// l'autorisation, la notch s'ouvre sur « Autoriser / Refuser » et la réponse
/// retourne au terminal ; il a fini, une coche.</item>
/// <item>Progression ouverte (W1) — un build, un rendu ou un script annonce
/// ses étapes avec <c>SpaceNotch.exe --progress</c>.</item>
/// </list>
///
/// <para>
/// Une activité par identifiant. Un travail dont on n'a plus de nouvelles
/// s'efface de lui-même : un terminal fermé brutalement ne laisse pas la
/// notch occupée pour toujours.
/// </para>
/// </summary>
public sealed class ChannelFeature : IslandFeatureBase
{
    public const string FeatureKey = FeatureKeys.Channel;

    public const string AllowAction = "channel.allow";

    public const string DenyAction = "channel.deny";

    /// <summary>Ouvre ce que la notification d'un script propose (journal, dossier, page).</summary>
    public const string OpenAction = "channel.open";

    /// <summary>Écarte la notification d'un script.</summary>
    public const string DismissAction = "channel.dismiss";

    /// <summary>Préfixe des activités du canal : l'identifiant du message suit.</summary>
    public const string Prefix = "feature.channel.";

    /// <summary>Violet des agents : ni le bleu des rendez-vous, ni le vert des téléchargements.</summary>
    public static readonly ActivityTint Violet = new(0xB3, 0x9D, 0xFF);

    private static readonly TimeSpan Stale = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan DoneLifetime = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan ErrorLifetime = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan NotifyLifetime = TimeSpan.FromSeconds(12);

    /// <summary>Vert d'une réussite, corail d'un échec.</summary>
    private static readonly ActivityTint Mint = new(0x7F, 0xE8, 0xB0);
    private static readonly ActivityTint Coral = new(0xFF, 0x6B, 0x6B);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _started = new();

    /// <summary>Dernières actions de chaque agent, la plus récente en bas.</summary>
    private readonly ConcurrentDictionary<string, string[]> _recent = new();

    /// <summary>Agents dont la carte est développée.</summary>
    private readonly ConcurrentDictionary<string, byte> _expanded = new();

    /// <summary>Dernier message de chaque agent : développer la carte la republie.</summary>
    private readonly ConcurrentDictionary<string, AgentMessage> _agents = new();

    /// <summary>Durée affichée sur la carte de chaque agent.</summary>
    private readonly ConcurrentDictionary<string, string> _clock = new();

    /// <summary>Ce que chaque notification de script propose d'ouvrir.</summary>
    private readonly ConcurrentDictionary<string, string> _targets = new();
    private ChannelServer? _server;

    public ChannelFeature(IActivityManager activities, IEventBus events, bool isEnabled = true)
        : base(FeatureKey, "Canal local", activities, events, isEnabled)
    {
    }

    /// <summary>Une question vient d'arriver : l'hôte ouvre la notch sur cette activité.</summary>
    public event Action<string>? QuestionAsked;

    protected override Task OnStartAsync(CancellationToken cancellationToken)
    {
        _server = new ChannelServer();
        _server.Received += Receive;
        _server.Abandoned += OnAbandoned;
        _server.Start();
        return Task.CompletedTask;
    }

    protected override Task OnStopAsync()
    {
        if (_server is not null)
        {
            _server.Received -= Receive;
            _server.Abandoned -= OnAbandoned;
            _server.Dispose();
            _server = null;
        }

        foreach (string id in _started.Keys)
        {
            RemoveActivity(Prefix + id);
        }

        foreach (string id in _targets.Keys)
        {
            RemoveActivity(Prefix + id);
        }

        _started.Clear();
        _recent.Clear();
        _expanded.Clear();
        _agents.Clear();
        _clock.Clear();
        _targets.Clear();
        return Task.CompletedTask;
    }

    protected override void OnDisposed() => _server?.Dispose();

    /// <summary>Un message du canal. Appelable directement (tests, visite).</summary>
    public void Receive(ChannelMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        switch (message)
        {
            case ClearMessage clear:
                _started.TryRemove(clear.Id, out _);
                Forget(clear.Id);
                RemoveActivity(Prefix + clear.Id);
                break;

            case NotifyMessage notify:
                PublishActivity(Notify(notify));
                break;

            case ProgressMessage progress:
                PublishActivity(Progress(progress));
                break;

            case AgentMessage agent:
                Remember(agent);
                PublishActivity(Agent(agent));

                if (agent is { State: ChannelState.Waiting, Question: not null })
                {
                    QuestionAsked?.Invoke(Prefix + agent.Id);
                }

                break;
        }
    }

    public override Task<bool> HandleActionAsync(IslandActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.ActivityId.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return Task.FromResult(false);
        }

        string id = request.ActivityId[Prefix.Length..];

        switch (request.ActionId)
        {
            case ClawdPayload.ToggleAction:
                return Task.FromResult(ToggleDetails(id));

            case OpenAction:
                if (_targets.TryRemove(id, out string? target))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                    }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
                    {
                        // Le fichier a disparu depuis : la notification s'efface quand même.
                    }
                }

                RemoveActivity(request.ActivityId);
                return Task.FromResult(true);

            case DismissAction:
                _targets.TryRemove(id, out _);
                RemoveActivity(request.ActivityId);
                return Task.FromResult(true);

            case AllowAction or DenyAction:
                break;

            default:
                return Task.FromResult(false);
        }

        bool allow = request.ActionId == AllowAction;
        bool delivered = _server?.Answer(id, allow) ?? false;

        // La réponse part ; l'agent reprend. S'il n'écoutait plus, on le dit.
        string detail = !delivered
            ? Lang.T("Réponds dans le terminal", "Answer in the terminal")
            : allow ? Lang.T("Autorisé", "Allowed") : Lang.T("Refusé", "Denied");

        PublishActivity(Agent(new AgentMessage(id, ClaudeHook.AgentName, detail, null, ChannelState.Working)));
        return Task.FromResult(true);
    }

    private void OnAbandoned(string id)
        => PublishActivity(Agent(new AgentMessage(id, ClaudeHook.AgentName, Lang.T("Réponds dans le terminal", "Answer in the terminal"), null, ChannelState.Waiting)));

    /// <summary>Garde les trois dernières actions d'un agent, sans doublon d'affilée.</summary>
    private void Remember(AgentMessage m)
    {
        _agents[m.Id] = m;

        if (m.Detail is not { } detail)
        {
            return;
        }

        _recent.AddOrUpdate(
            m.Id,
            _ => [detail],
            (_, lines) => lines.Length > 0 && lines[^1] == detail
                ? lines
                : [.. lines.Skip(Math.Max(0, lines.Length + 1 - ClawdPayload.MaxRecent)), detail]);
    }

    private void Forget(string id)
    {
        _recent.TryRemove(id, out _);
        _expanded.TryRemove(id, out _);
        _agents.TryRemove(id, out _);
        _clock.TryRemove(id, out _);
        _targets.TryRemove(id, out _);
    }

    /// <summary>Un appui sur la carte d'un agent : elle se développe, ou se replie.</summary>
    public bool ToggleDetails(string id)
    {
        if (!_agents.TryGetValue(id, out AgentMessage? last) || !_recent.ContainsKey(id))
        {
            return false;
        }

        if (!_expanded.TryRemove(id, out _))
        {
            _expanded[id] = 0;
        }

        PublishActivity(Agent(last, keepClock: true));
        return true;
    }

    /// <summary>Temps écoulé depuis le premier message de ce travail : « 42 s », « 3:07 ».</summary>
    private string Elapsed(string id, ChannelState state)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset start = _started.GetOrAdd(id, now);

        if (state is ChannelState.Done or ChannelState.Error)
        {
            _started.TryRemove(id, out _);
        }

        TimeSpan elapsed = now - start;
        return elapsed.TotalMinutes >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{Math.Max(0, (int)elapsed.TotalSeconds)} s");
    }

    private IslandActivity Agent(AgentMessage m, bool keepClock = false)
    {
        // Développer la carte ne remet pas la durée à zéro : elle reste celle affichée.
        string elapsed = keepClock && _clock.TryGetValue(m.Id, out string? shown) ? shown : Elapsed(m.Id, m.State);
        _clock[m.Id] = elapsed;
        bool asks = m is { State: ChannelState.Waiting, Question: not null };
        string? eyebrow = asks || m.State == ChannelState.Waiting ? null : m.Detail;

        // Claude Code garde sa mascotte ; ses dernières actions attendent un appui.
        ClawdPayload? clawd = IsClaudeCode(m.Name)
            ? new ClawdPayload(MoodOf(m.State, asks), _recent.TryGetValue(m.Id, out string[]? recent) ? recent : null, _expanded.ContainsKey(m.Id))
            : null;

        string subtitle = m.State switch
        {
            ChannelState.Waiting when asks => m.Question!,
            ChannelState.Waiting => m.Detail ?? Lang.T("Attend ta réponse", "Waiting for you"),
            ChannelState.Done => Lang.T("Terminé", "Done") + " · " + elapsed,
            ChannelState.Error => Lang.T("Arrêté sur une erreur", "Stopped on an error"),
            _ => Lang.T("Réfléchit…", "Thinking…")
        };

        return new IslandActivity
        {
            Id = Prefix + m.Id,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Card,
            // Maquette I4 : « Claude attend ta réponse », la commande en dessous.
            Title = asks ? Lang.T($"{ShortName(m.Name)} attend ta réponse", $"{ShortName(m.Name)} is waiting for you") : m.Name,
            Subtitle = subtitle,
            Eyebrow = eyebrow,
            ShowEnterHint = asks,
            Layout = ActivityLayout.Stack,
            Source = m.Name,
            IconKey = "Agent",
            Tint = Violet,
            Metric = m.State switch
            {
                ChannelState.Done => "✓",
                ChannelState.Error => "!",
                ChannelState.Waiting => asks ? null : "?",
                _ => elapsed
            },
            State = IslandActivityState.Notification,
            MotionState = m.State switch
            {
                ChannelState.Waiting => ActivityMotionState.Attention,
                ChannelState.Done => ActivityMotionState.Completing,
                ChannelState.Error => ActivityMotionState.Error,
                _ => ActivityMotionState.Working
            },
            MotionPreset = m.State == ChannelState.Working ? HypnoticPreset.Think : HypnoticPreset.None,

            // Claude Code a sa mascotte : Clawd remplace la grille, dans l'humeur de l'agent.
            Payload = clawd,

            // La carte épouse son contenu (A) : plus de grand noir sous le texte.
            ExpandedFootprint = CardFit.For(eyebrow is not null, subtitle, progress: false, actions: asks, ActivityLayout.Stack, clawd?.ShownLines ?? 0),
            Priority = asks ? ActivityPriority.High : ActivityPriority.Normal,
            Policy = asks ? null : ActivityPresentationPolicy.Passive,
            Duration = m.State switch
            {
                ChannelState.Done => DoneLifetime,
                ChannelState.Error => ErrorLifetime,
                _ => Stale
            },
            Actions = asks
                ?
                [
                    new ActivityAction(AllowAction, Lang.T("Autoriser", "Allow"), "Allow", ActivityActionKind.Invoke, IsPrimary: true, Tone: ActivityActionTone.Positive),
                    new ActivityAction(DenyAction, Lang.T("Refuser", "Deny"), "Deny")
                ]
                : []
        };
    }

    /// <summary>« Claude » pour Claude Code : le titre reste court.</summary>
    private static string ShortName(string name) => IsClaudeCode(name) ? "Claude" : name;

    private static bool IsClaudeCode(string name)
        => string.Equals(name, ClaudeHook.AgentName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// L'humeur de Clawd. Une attente sans question — Claude Code attend une
    /// saisie dans le terminal — se montre comme une erreur : il faut y aller.
    /// </summary>
    public static ClawdMood MoodOf(ChannelState state, bool asks) => state switch
    {
        ChannelState.Waiting when asks => ClawdMood.Asking,
        ChannelState.Waiting => ClawdMood.Error,
        ChannelState.Done => ClawdMood.Done,
        ChannelState.Error => ClawdMood.Error,
        _ => ClawdMood.Thinking
    };

    /// <summary>
    /// Notification d'un script (B) : ce qu'il annonce et, s'il donne de quoi
    /// l'ouvrir, « Ouvrir » et « Ignorer ». Sans cible, la carte s'ajuste à son texte.
    /// </summary>
    private IslandActivity Notify(NotifyMessage m)
    {
        if (m.Open is { } target)
        {
            _targets[m.Id] = target;
        }
        else
        {
            _targets.TryRemove(m.Id, out _);
        }

        bool open = m.Open is not null;

        return new IslandActivity
        {
            Id = Prefix + m.Id,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Card,
            Title = m.Title,
            Subtitle = m.Body,
            Eyebrow = m.Source,
            Layout = ActivityLayout.Stack,
            Source = m.Source ?? Lang.T("Script", "Script"),
            IconKey = m.State switch
            {
                ChannelState.Done => "Check",
                ChannelState.Error => "Warning",
                _ => "Notification"
            },
            Tint = m.State switch
            {
                ChannelState.Done => Mint,
                ChannelState.Error => Coral,
                _ => null
            },
            State = IslandActivityState.Notification,
            MotionState = m.State switch
            {
                ChannelState.Done => ActivityMotionState.Completing,
                ChannelState.Error => ActivityMotionState.Error,
                _ => ActivityMotionState.Idle
            },
            Priority = m.State == ChannelState.Error ? ActivityPriority.High : ActivityPriority.Normal,
            Duration = NotifyLifetime,
            ExpandedFootprint = CardFit.For(m.Source is not null, m.Body, progress: false, actions: open, ActivityLayout.Stack),
            Actions = open
                ?
                [
                    new ActivityAction(OpenAction, m.OpenLabel ?? Lang.T("Ouvrir", "Open"), "Folder", ActivityActionKind.Invoke, IsPrimary: true, Tone: ActivityActionTone.Positive),
                    new ActivityAction(DismissAction, Lang.T("Ignorer", "Dismiss"), "Close")
                ]
                : []
        };
    }

    private IslandActivity Progress(ProgressMessage m)
    {
        string elapsed = Elapsed(m.Id, m.State);

        string? step = m.Steps > 0 ? $"{m.Step}/{m.Steps}" : null;
        string subtitle = m.State switch
        {
            ChannelState.Done => Lang.T("Terminé", "Done"),
            ChannelState.Error => m.Label is null ? Lang.T("Échec", "Failed") : Lang.T("Échec · ", "Failed · ") + m.Label,
            _ => m.Label ?? Lang.T("En cours", "Running")
        };

        return new IslandActivity
        {
            Id = Prefix + m.Id,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Card,
            // Maquette W1 : à la fin, « Build réussi » et la durée.
            Title = m.State == ChannelState.Done ? m.Title + Lang.T(" réussi", " succeeded") : m.Title,
            Subtitle = subtitle,
            Eyebrow = step is null ? null : Lang.T($"Étape {step}", $"Step {step}"),
            Source = Lang.T("Progression", "Progress"),
            IconKey = m.State == ChannelState.Done ? "Check" : "Progress",
            Progress = ProgressSteps.Overall(m),
            Metric = m.State == ChannelState.Done ? elapsed : ProgressSteps.Metric(m),
            Payload = new ProgressStepsPayload(ProgressSteps.Segments(m)),
            ExpandedFootprint = CardFit.For(step is not null, subtitle, progress: true, actions: false, ActivityLayout.Card),
            State = IslandActivityState.DownloadActive,
            MotionState = m.State switch
            {
                ChannelState.Done => ActivityMotionState.Completing,
                ChannelState.Error => ActivityMotionState.Error,
                _ => ActivityMotionState.Working
            },
            MotionPreset = m.State == ChannelState.Working ? HypnoticPreset.Process : HypnoticPreset.None,
            Priority = m.State == ChannelState.Error ? ActivityPriority.High : ActivityPriority.Background,
            Policy = m.State == ChannelState.Error ? null : ActivityPresentationPolicy.Passive,
            Duration = m.State switch
            {
                ChannelState.Done => DoneLifetime,
                ChannelState.Error => ErrorLifetime,
                _ => Stale
            }
        };
    }
}
