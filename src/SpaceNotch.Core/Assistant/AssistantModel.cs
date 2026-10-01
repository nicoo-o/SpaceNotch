namespace SpaceNotch.Core.Assistant;

/// <summary>Le modèle de langage que la notch peut consulter (vague 6c, ADR-025).</summary>
public enum AssistantSource
{
    /// <summary>Aucun modèle : seules les règles locales répondent. Le défaut.</summary>
    Off = 0,

    /// <summary>Le modèle de Windows (Phi Silica), sur PC Copilot+ : rien ne quitte la machine.</summary>
    Local = 1,

    /// <summary>Claude, par l'API d'Anthropic, avec la clé de l'utilisateur.</summary>
    Claude = 2
}

/// <summary>Une demande au modèle : une consigne, un texte, une longueur de réponse.</summary>
/// <param name="System">La consigne : ce que le modèle doit faire et sous quelle forme.</param>
/// <param name="Prompt">Le texte de l'utilisateur.</param>
/// <param name="MaxTokens">Longueur maximale de la réponse.</param>
public sealed record AssistantRequest(string System, string Prompt, int MaxTokens = 600);

/// <summary>
/// Un modèle de langage. L'hôte en fournit une implémentation (local ou
/// distant) ; le cœur ne connaît que ce contrat, ce qui le garde testable.
/// </summary>
public interface IAssistantModel
{
    /// <summary>Nom montré à l'utilisateur : « Phi Silica », « Claude ».</summary>
    string DisplayName { get; }

    /// <summary>Vrai si le texte ne quitte jamais la machine.</summary>
    bool IsLocal { get; }

    /// <summary>La réponse du modèle ; <c>null</c> s'il n'a pas pu répondre (pas de réseau, refus, modèle absent).</summary>
    Task<string?> AskAsync(AssistantRequest request, CancellationToken cancellationToken = default);
}
