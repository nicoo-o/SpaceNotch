using System;
using System.Collections.Generic;
using System.Linq;

namespace SpaceNotch.Core.Activities;

/// <summary>Une application et ce qu'elle a envoyé pendant le calme.</summary>
public readonly record struct QuietGroup(string App, int Count, string Latest);

/// <summary>
/// Ne pas déranger (F9) : pendant le calme, les notifications arrivées sont
/// comptées sans rien montrer ; à la sortie, la notch résume ce qui est arrivé,
/// groupé par application, les plus bavardes d'abord.
/// </summary>
public sealed class QuietSummary
{
    private readonly List<(string App, string Text, DateTimeOffset At)> _held = [];

    /// <summary>Notifications retenues.</summary>
    public int Count => _held.Count;

    public void Hold(string app, string text, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(app);
        _held.Add((app, text ?? string.Empty, at));
    }

    /// <summary>Résumé groupé par application ; la dernière notification de chacune sert d'aperçu.</summary>
    public IReadOnlyList<QuietGroup> Groups()
        => _held
            .GroupBy(n => n.App, StringComparer.OrdinalIgnoreCase)
            .Select(g => new QuietGroup(g.First().App, g.Count(), g.OrderByDescending(n => n.At).First().Text))
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.App, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public void Clear() => _held.Clear();
}
