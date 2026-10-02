using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Assistant;
using SpaceNotch.Core.Features;
using SpaceNotch.Core.Launcher;
using SpaceNotch.Core.Localization;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;
using SpaceNotch.Features.Assistant;
using SpaceNotch.Infrastructure.Logging;
using SpaceNotch_App.Assistant;

namespace SpaceNotch_App.Windows;

/// <summary>
/// Vague 6c, l'intelligence : le résumé des notifications (I1), la commande en
/// langage naturel (I2), les actions sur copie (I3). Le modèle — Phi Silica
/// sur la machine ou Claude avec la clé de l'utilisateur — est un choix des
/// réglages ; sans lui, les règles locales répondent. Voir ADR-025.
/// </summary>
public sealed partial class IslandWindow
{
    private const string AssistantActivityId = "feature.assistant.answer";

    private ReminderFeature? _reminderFeature;
    private CopyAssistFeature? _copyAssistFeature;
    private WindowsLocalModel? _localModel;
    private IAssistantModel? _claudeModel;

    private IEnumerable<IIslandFeature> CreateWave6cFeatures()
    {
        string folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpaceNotch");

        _reminderFeature = new ReminderFeature(_activityManager, _eventBus, folder);
        _copyAssistFeature = new CopyAssistFeature(_activityManager, _eventBus, _clipboardMonitor, _hWnd, _settings.CopyActions);

        return [_reminderFeature, _copyAssistFeature];
    }

    private void WireWave6c()
    {
        _notificationFeature.UserName = Environment.UserName;

        if (_copyAssistFeature is not null && _reminderFeature is not null)
        {
            _copyAssistFeature.AddReminder = _reminderFeature.Add;
        }

        ApplyAssistant();
    }

    /// <summary>Le modèle choisi dans les réglages, s'il peut répondre sur ce PC.</summary>
    private IAssistantModel? CurrentModel()
    {
        switch (_settings.AssistantSource)
        {
            case AssistantSource.Local when WindowsLocalModel.IsAvailable:
                _localModel ??= new WindowsLocalModel();
                return _localModel;

            case AssistantSource.Claude when AssistantKeys.HasKey:
                _claudeModel = new ClaudeModel(_settings.ClaudeModel, AssistantKeys.Read);
                return _claudeModel;

            default:
                return null;
        }
    }

    /// <summary>Donne (ou retire) le modèle aux fonctionnalités qui s'en servent.</summary>
    private void ApplyAssistant()
    {
        IAssistantModel? model = CurrentModel();
        Func<AssistantRequest, Task<string?>>? ask = model is null ? null : request => model.AskAsync(request);

        _notificationFeature.Ask = ask;
        _launcherFeature.AskModelName = model?.DisplayName;

        if (_copyAssistFeature is not null)
        {
            _copyAssistFeature.Ask = ask;
            _copyAssistFeature.ModelPlace = model is null ? null : model.IsLocal ? Lang.T("sur l'appareil", "on device") : Lang.T("via ", "via ") + model.DisplayName;
        }

        MiniLogger.Log(model is null
            ? "Assistant : règles locales seulement."
            : $"Assistant : {model.DisplayName}{(model.IsLocal ? " (sur l'appareil)" : string.Empty)}.");
    }

    /// <summary>Les commandes de la vague 6c tapées dans le lanceur.</summary>
    private bool RunAssistantCommand(LauncherCommandKind kind, string value)
    {
        switch (kind)
        {
            case LauncherCommandKind.Reminder when TryReadReminder(value, out NaturalIntent intent):
                // La pastille du rappel suffit (maquette I2) : pas de carte de confirmation.
                _reminderFeature?.Add(intent);

                return true;

            case LauncherCommandKind.Quiet when long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long until):
                _notificationFeature.QuietUntil(DateTimeOffset.FromUnixTimeSeconds(until).ToLocalTime());
                return true;

            case LauncherCommandKind.Ask:
                _ = AskModelAsync(value);
                return true;

            default:
                return false;
        }
    }

    private static bool TryReadReminder(string value, out NaturalIntent intent)
    {
        intent = null!;
        int bar = value.IndexOf('|', StringComparison.Ordinal);

        if (bar <= 0 || !long.TryParse(value.AsSpan(0, bar), NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds))
        {
            return false;
        }

        intent = new NaturalIntent(NaturalKind.Reminder, value[(bar + 1)..], DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime(), null, null);
        return true;
    }

    /// <summary>La phrase que la grammaire n'a pas comprise, demandée au modèle, puis validée comme une saisie.</summary>
    private async Task AskModelAsync(string query)
    {
        if (CurrentModel() is not { } model)
        {
            return;
        }

        AnswerInNotch(Lang.T("Je réfléchis…", "Thinking…"), query, ok: null);

        DateTimeOffset now = DateTimeOffset.Now;
        string? reply = await model.AskAsync(NaturalCommand.Prompt(query, now)).ConfigureAwait(false);

        OnUiThread(() =>
        {
            if (NaturalCommand.FromModelJson(reply, now) is not { } intent)
            {
                AnswerInNotch(Lang.T("Pas compris", "Not understood"), query, ok: false);
                return;
            }

            LauncherCommand command = LauncherCommands.FromIntent(intent, Lang.French, now);

            if (LauncherCommands.TryRead(command.Target, out LauncherCommandKind kind, out string value))
            {
                RunCommand(kind, value);
            }

            if (intent.Kind != NaturalKind.Reminder)
            {
                AnswerInNotch(command.Title, string.Join(" · ", System.Linq.Enumerable.Select(intent.Chips(Lang.French, now), c => c.Value)), ok: true);
            }
        });
    }

    /// <summary>Une réponse courte de l'assistant : en cours, faite, ou pas comprise.</summary>
    private void AnswerInNotch(string title, string subtitle, bool? ok) => _activityManager.PostActivity(new IslandActivity
    {
        Id = AssistantActivityId,
        FeatureId = "feature.assistant",
        SceneKey = IslandSceneCatalog.Card,
        Title = title,
        Subtitle = subtitle,
        Source = Lang.T("Assistant", "Assistant"),
        IconKey = ok == false ? "Warning" : "Command",
        Metric = ok == true ? "✓" : null,
        State = IslandActivityState.Idle,
        MotionState = ok switch
        {
            null => ActivityMotionState.Working,
            true => ActivityMotionState.Completing,
            false => ActivityMotionState.Error
        },
        MotionPreset = ok is null ? HypnoticPreset.Think : HypnoticPreset.None,
        Priority = ActivityPriority.Normal,
        Duration = TimeSpan.FromSeconds(ok is null ? 30 : 5)
    });
}
