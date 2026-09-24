using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;

namespace SpaceNotch.Core.Features;

/// <summary>
/// Registre des fonctionnalités.
///
/// Il centralise ce que la fenêtre faisait sinon à la main : démarrer les
/// fonctionnalités activées en isolant les échecs, les arrêter toutes à la
/// fermeture, et router les messages de fenêtre vers celles qui savent les
/// traiter. Sans ce point unique, chaque nouvelle fonctionnalité aurait ajouté
/// une branche conditionnelle supplémentaire dans la fenêtre.
/// </summary>
public sealed class IslandFeatureRegistry : IAsyncDisposable
{
    private readonly IIslandFeature[] _features;
    private readonly Action<string, Exception>? _onFault;
    private bool _disposed;

    public IslandFeatureRegistry(
        IEnumerable<IIslandFeature> features,
        Action<string, Exception>? onFault = null)
    {
        ArgumentNullException.ThrowIfNull(features);

        _features = features.ToArray();
        _onFault = onFault;

        foreach (IIslandFeature feature in _features)
        {
            feature.StateChanged += OnFeatureStateChanged;

            if (feature is IslandFeatureBase implementation)
            {
                implementation.ErrorReported += OnFeatureErrorReported;
            }
        }
    }

    /// <summary>Fonctionnalités enregistrées, dans l'ordre de déclaration.</summary>
    public IReadOnlyList<IIslandFeature> Features => _features;

    /// <summary>Nombre de fonctionnalités actuellement actives.</summary>
    public int RunningCount => _features.Count(f => f.State == FeatureState.Running);

    public IIslandFeature? Find(string featureId)
        => _features.FirstOrDefault(f => string.Equals(f.Id, featureId, StringComparison.Ordinal));

    /// <summary>
    /// Démarre toutes les fonctionnalités activées. Un échec est isolé : les
    /// autres continuent. Aucun ordre de dépendance n'est supposé.
    /// </summary>
    public async Task StartEnabledAsync(CancellationToken cancellationToken = default)
    {
        foreach (IIslandFeature feature in _features)
        {
            if (!feature.IsEnabled)
            {
                continue;
            }

            try
            {
                await feature.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // IslandFeatureBase absorbe déjà ses propres erreurs ; cette
                // ceinture protège d'une implémentation tierce qui ne le ferait pas.
                _onFault?.Invoke(feature.Id, ex);
            }
        }
    }

    /// <summary>Arrête toutes les fonctionnalités, y compris celles déjà arrêtées.</summary>
    public async Task StopAllAsync()
    {
        foreach (IIslandFeature feature in _features)
        {
            try
            {
                await feature.StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _onFault?.Invoke(feature.Id, ex);
            }
        }
    }

    /// <summary>
    /// Active ou désactive une fonctionnalité par son identifiant.
    /// </summary>
    /// <returns><c>true</c> si la fonctionnalité existe et a été basculée.</returns>
    public async Task<bool> SetEnabledAsync(string featureId, bool enabled, CancellationToken cancellationToken = default)
    {
        IIslandFeature? feature = Find(featureId);

        if (feature is null)
        {
            return false;
        }

        await feature.SetEnabledAsync(enabled, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Route un message de fenêtre vers la première fonctionnalité qui le
    /// reconnaît. Toutes les fonctionnalités actives sont consultées, car
    /// plusieurs peuvent légitimement réagir au même message.
    /// </summary>
    /// <returns><c>true</c> si au moins une fonctionnalité l'a consommé.</returns>
    public bool TryHandleWindowMessage(uint messageId, nuint wParam)
    {
        bool handled = false;

        foreach (IIslandFeature feature in _features)
        {
            if (feature is IslandFeatureBase implementation
                && implementation.IsRunning
                && implementation.TryHandleWindowMessage(messageId, wParam))
            {
                handled = true;
            }
        }

        return handled;
    }

    /// <summary>Activités publiées par l'ensemble des fonctionnalités.</summary>
    public IReadOnlyList<IslandActivity> GetActivities()
        => _features.SelectMany(f => f.GetActivities()).ToList();

    /// <summary>
    /// Achemine une demande d'action vers la fonctionnalité propriétaire de
    /// l'activité concernée. Les fonctionnalités arrêtées sont ignorées : une
    /// action demandée sur une fonctionnalité désactivée ne doit rien faire, et
    /// surtout pas la réveiller.
    /// </summary>
    /// <returns><c>true</c> si une fonctionnalité active a traité la demande.</returns>
    public async Task<bool> HandleActionAsync(IslandActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        foreach (IIslandFeature feature in _features)
        {
            if (feature.State != FeatureState.Running)
            {
                continue;
            }

            bool claimed = feature.GetActivities()
                .Any(a => string.Equals(a.Id, request.ActivityId, StringComparison.Ordinal));

            if (!claimed)
            {
                continue;
            }

            try
            {
                if (await feature.HandleActionAsync(request).ConfigureAwait(false))
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                _onFault?.Invoke(feature.Id, ex);
            }
        }

        return false;
    }

    private void OnFeatureStateChanged(object? sender, FeatureStateChangedEventArgs e)
    {
        if (e.Error is not null)
        {
            _onFault?.Invoke(e.FeatureId, e.Error);
        }
    }

    /// <summary>
    /// Erreur partielle signalée par une fonctionnalité qui continue de
    /// fonctionner. Elle est rapportée comme les autres : un échec mineur tu n'est
    /// pas un succès.
    /// </summary>
    private void OnFeatureErrorReported(object? sender, Exception exception)
    {
        string featureId = sender is IIslandFeature feature ? feature.Id : "feature.inconnue";

        _onFault?.Invoke(featureId, exception);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (IIslandFeature feature in _features)
        {
            feature.StateChanged -= OnFeatureStateChanged;

            if (feature is IslandFeatureBase implementation)
            {
                implementation.ErrorReported -= OnFeatureErrorReported;
            }

            try
            {
                await feature.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _onFault?.Invoke(feature.Id, ex);
            }
        }

        GC.SuppressFinalize(this);
    }
}
