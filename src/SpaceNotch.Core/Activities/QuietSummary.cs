using System;
using System.Collections.Generic;
using System.Linq;

namespace SpaceNotch.Core.Activities;

/// <summary>Une application et ce qu'elle a envoyé pendant le calme.</summary>
public readonly record struct QuietGroup(string App, int Count, string Latest);

/// <summary>Une notification retenue pendant le calme, telle qu'elle est arrivée.</summary>
/// <param name="App">Application d'origine.</param>
/// <param name="Sender">Titre de la notification : souvent l'expéditeur.</param>
/// <param name="Body">Texte de la notification.</param>
/// <param name="At">Heure d'arrivée.</param>
public sealed record HeldNotification(string App, string Sender, string Body, DateTimeOffset At)
{
    /// <summary>L'aperçu d'une ligne : le titre, sinon le texte.</summary>
    public string Preview => string.IsNullOrWhiteSpace(Sender) ? Body : Sender;
}

/// <summary>
/// Ne pas déranger (F9) : pendant le calme, les notifications arrivées sont
/// comptées sans rien montrer ; à la sortie, la notch résume ce qui est arrivé,
/// groupé par application, les plus bavardes d'abord. Le résumé intelligent
/// (I1) relit les notifications elles-mêmes : <see cref="Items"/>.
/// </summary>
public sealed class QuietSummary
{
    private readonly List<HeldNotification> _held = [];

    /// <summary>Notifications retenues.</summary>
    public int Count => _held.Count;

    /// <summary>Les notifications retenues, dans l'ordre d'arrivée.</summary>
    public IReadOnlyList<HeldNotification> Items => [.. _held];

    public void Hold(string app, string text, DateTimeOffset at) => Hold(app, string.Empty, text, at);

    public void Hold(string app, string sender, string body, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(app);
        _held.Add(new HeldNotification(app, sender ?? string.Empty, body ?? string.Empty, at));
    }

    /// <summary>Résumé groupé par application ; la dernière notification de chacune sert d'aperçu.</summary>
    public IReadOnlyList<QuietGroup> Groups()
        => _held
            .GroupBy(n => n.App, StringComparer.OrdinalIgnoreCase)
            .Select(g => new QuietGroup(g.First().App, g.Count(), g.OrderByDescending(n => n.At).First().Preview))
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.App, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public void Clear() => _held.Clear();
}
