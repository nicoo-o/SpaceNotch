using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NotchFlow.SamplePlugin.Weather;

namespace NotchFlow.SamplePlugin.Tests;

/// <summary>
/// Source météo simulée.
///
/// Elle n'est pas un détail de test : elle est la raison pour laquelle la
/// fonctionnalité a été écrite derrière une interface. Sans elle, vérifier qu'un
/// second relevé <em>remplace</em> le premier exigerait d'attendre un vrai service
/// et de le contrôler — c'est-à-dire de ne pas le vérifier.
/// </summary>
internal sealed class FakeWeatherSource : IWeatherSource
{
    /// <summary>Relevé par défaut, utilisé lorsque la file d'attente est vide.</summary>
    public static readonly WeatherSnapshot Default = new(
        TemperatureCelsius: 18.0,
        WeatherCode: 0,
        IsDay: true,
        ObservedAt: new DateTimeOffset(2026, 9, 19, 14, 32, 0, TimeSpan.Zero));

    private readonly Queue<WeatherSnapshot> _queued = new();

    /// <summary>Nombre d'appels reçus, utilisé pour vérifier qu'une actualisation a bien eu lieu.</summary>
    public int Calls { get; private set; }

    /// <summary>Erreur à produire, ou <c>null</c> pour répondre normalement.</summary>
    public Exception? Failure { get; set; }

    /// <summary>Ajoute un relevé à servir, dans l'ordre.</summary>
    public FakeWeatherSource Enqueue(WeatherSnapshot snapshot)
    {
        _queued.Enqueue(snapshot);
        return this;
    }

    public Task<WeatherSnapshot> GetCurrentAsync(
        WeatherLocation location,
        CancellationToken cancellationToken)
    {
        Calls++;

        if (Failure is not null)
        {
            return Task.FromException<WeatherSnapshot>(Failure);
        }

        return Task.FromResult(_queued.Count > 0 ? _queued.Dequeue() : Default);
    }
}
