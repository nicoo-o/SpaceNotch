using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Windows.AI;
using Microsoft.Windows.AI.Text;
using SpaceNotch.Core.Assistant;
using SpaceNotch.Infrastructure.Logging;

namespace SpaceNotch_App.Assistant;

/// <summary>
/// Le modèle de langage de Windows (Phi Silica), sur les PC Copilot+ :
/// tout se passe sur la machine, rien n'est envoyé. Ailleurs, il se dit
/// indisponible et la notch s'en tient à ses règles locales. Voir ADR-025.
/// </summary>
internal sealed class WindowsLocalModel : IAssistantModel, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private LanguageModel? _model;

    public string DisplayName => "Phi Silica";

    public bool IsLocal => true;

    /// <summary>L'état du modèle sur ce PC, sans rien télécharger.</summary>
    public static AIFeatureReadyState State()
    {
        try
        {
            return LanguageModel.GetReadyState();
        }
        catch (Exception)
        {
            return AIFeatureReadyState.NotSupportedOnCurrentSystem;
        }
    }

    /// <summary>Vrai si le modèle peut répondre ou être préparé par Windows.</summary>
    public static bool IsAvailable => State() is AIFeatureReadyState.Ready or AIFeatureReadyState.NotReady;

    public async Task<string?> AskAsync(AssistantRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            LanguageModel? model = await EnsureAsync().ConfigureAwait(false);

            if (model is null)
            {
                return null;
            }

            // Le modèle local n'a pas de rôle « système » séparé : la consigne précède le texte.
            string prompt = request.System + "\n\n" + request.Prompt;
            LanguageModelResponseResult result = await model.GenerateResponseAsync(prompt).AsTask(cancellationToken).ConfigureAwait(false);

            return result.Status == LanguageModelResponseStatus.Complete ? result.Text : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            MiniLogger.Log("[IA] Phi Silica n'a pas répondu", ex);
            return null;
        }
    }

    private async Task<LanguageModel?> EnsureAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);

        try
        {
            if (_model is not null)
            {
                return _model;
            }

            AIFeatureReadyState state = State();

            if (state == AIFeatureReadyState.NotReady)
            {
                AIFeatureReadyResult ready = await LanguageModel.EnsureReadyAsync();

                if (ready.Status != AIFeatureReadyResultState.Success)
                {
                    MiniLogger.Log($"[IA] Phi Silica indisponible : {ready.ErrorDisplayText}");
                    return null;
                }
            }
            else if (state != AIFeatureReadyState.Ready)
            {
                return null;
            }

            _model = await LanguageModel.CreateAsync();
            return _model;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _model?.Dispose();
        _gate.Dispose();
    }
}
