using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Features;
using SpaceNotch.Core.Localization;
using SpaceNotch.Core.Weather;

namespace SpaceNotch.Features.Weather;

/// <summary>
/// Aperçu météo (F10) : le temps de la ville choisie dans les réglages, lu
/// auprès d'Open-Meteo toutes les trente minutes. Pas d'activité : la notch
/// l'ajoute à l'heure, au survol du repos. Sans ville, rien n'est demandé au
/// réseau (ADR-011).
/// </summary>
public sealed class WeatherFeature : IslandFeatureBase
{
    public const string FeatureKey = "feature.weather";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly Timer _timer;
    private string? _city;
    private WeatherPlace? _place;

    public WeatherFeature(IActivityManager activities, IEventBus events, string? city)
        : base(FeatureKey, "Météo", activities, events, isEnabled: true)
    {
        _city = city;
        _timer = new Timer(_ => _ = RefreshAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Le dernier relevé, ou null (pas de ville, pas de réseau).</summary>
    public WeatherReport? Current { get; private set; }

    /// <summary>Nom de la ville tel que le service l'a trouvée.</summary>
    public string? PlaceName => _place?.Name;

    /// <summary>Un nouveau relevé est arrivé (ou la météo a été coupée).</summary>
    public event Action? Changed;

    /// <summary>Change la ville ; vide coupe la météo.</summary>
    public void SetCity(string? city)
    {
        if (string.Equals(city?.Trim(), _city?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _city = city;
        _place = null;
        Current = null;
        Changed?.Invoke();
        _ = RefreshAsync();
    }

    /// <summary>Pose un relevé sans réseau (visite, tests).</summary>
    public void Inject(WeatherReport report, string place)
    {
        Current = report;
        _place = new WeatherPlace(place, 0, 0);
        Changed?.Invoke();
    }

    protected override Task OnStartAsync(CancellationToken cancellationToken)
    {
        _ = RefreshAsync();
        return Task.CompletedTask;
    }

    protected override Task OnStopAsync()
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        return Task.CompletedTask;
    }

    protected override void OnDisposed() => _timer.Dispose();

    private async Task RefreshAsync()
    {
        string? city = _city;

        if (string.IsNullOrWhiteSpace(city))
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            return;
        }

        try
        {
            _place ??= WeatherCodes.ParsePlace(await Http.GetStringAsync(WeatherCodes.GeocodingUri(city, Lang.French)).ConfigureAwait(false));

            if (_place is { } place)
            {
                WeatherReport? report = WeatherCodes.ParseForecast(
                    await Http.GetStringAsync(WeatherCodes.ForecastUri(place.Latitude, place.Longitude)).ConfigureAwait(false));

                if (report is not null)
                {
                    Current = report;
                    Changed?.Invoke();
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Hors ligne : on garde le dernier relevé et on réessaie au prochain passage.
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }

        _timer.Change(WeatherCodes.Refresh, Timeout.InfiniteTimeSpan);
    }
}
