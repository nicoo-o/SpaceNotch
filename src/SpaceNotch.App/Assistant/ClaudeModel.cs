using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using SpaceNotch.Core.Assistant;
using SpaceNotch.Infrastructure.Logging;

namespace SpaceNotch_App.Assistant;

/// <summary>
/// Claude, par l'API d'Anthropic, avec la clé de l'utilisateur (gardée dans
/// le coffre de Windows). Seul ce que l'utilisateur a demandé est envoyé :
/// les notifications retenues pour un résumé, le texte copié pour une action,
/// la phrase tapée quand la grammaire locale ne l'a pas comprise. Voir ADR-025.
/// </summary>
internal sealed class ClaudeModel : IAssistantModel
{
    /// <summary>Le modèle par défaut.</summary>
    public const string DefaultModel = "claude-opus-5-5";

    private readonly string _model;
    private readonly Func<string?> _key;

    public ClaudeModel(string? model, Func<string?> key)
    {
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
        _key = key ?? throw new ArgumentNullException(nameof(key));
    }

    public string DisplayName => "Claude";

    public bool IsLocal => false;

    public async Task<string?> AskAsync(AssistantRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_key() is not { } key)
        {
            return null;
        }

        try
        {
            var client = new AnthropicClient { ApiKey = key };

            BetaMessage response = await client.Beta.Messages.Create(new MessageCreateParams
            {
                Model = _model,
                MaxTokens = Math.Clamp(request.MaxTokens, 64, 4000) + 1024,

                // Des tâches courtes et simples : peu de réflexion suffit.
                OutputConfig = new BetaOutputConfig { Effort = Effort.Low },

                // Un refus de sécurité est repris par un modèle de repli, dans le même appel.
                Betas = ["server-side-fallback-2026-07-01"],
                Fallbacks = new Default(),

                System = request.System,
                Messages = [new() { Role = Role.User, Content = request.Prompt }],
            }, cancellationToken).ConfigureAwait(false);

            if (response.StopReason == BetaStopReason.Refusal)
            {
                MiniLogger.Log("[IA] Claude a décliné la demande.");
                return null;
            }

            string text = string.Concat(response.Content
                .Select(b => b.TryPickText(out BetaTextBlock? t) ? t.Text : string.Empty));

            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch (AnthropicApiException ex)
        {
            MiniLogger.Log($"[IA] Claude : erreur d'API ({ex.Message})");
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            MiniLogger.Log("[IA] Claude injoignable", ex);
            return null;
        }
    }
}
