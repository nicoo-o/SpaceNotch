using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NotchFlow.SamplePlugin.Weather;

/// <summary>
/// Source météo réelle, appuyée sur l'API publique Open-Meteo.
///
/// Ce service a été retenu pour une raison précise : il ne demande <b>aucune clé
/// d'API</b>. Un greffon d'exemple qui exigerait une inscription ne serait pas
/// exécutable par un lecteur, et un exemple qu'on ne peut pas exécuter n'enseigne
/// rien.
///
/// <b>Lecture JSON.</b> Le projet désactive la sérialisation par réflexion
/// (<c>JsonSerializerIsReflectionEnabledByDefault=false</c>), parce que le
/// toolchain d'élagage de WinUI la rend inopérante à l'exécution. Un greffon
/// n'échappe pas à cette contrainte : il lit donc la réponse par
/// <see cref="JsonDocument"/>, qui n'utilise aucune réflexion, ou par un contexte
/// source-généré. Un <c>JsonSerializer.Deserialize&lt;T&gt;</c> réflexif compilerait
/// et échouerait à l'exécution — le piège est signalé dans le README du greffon.
/// </summary>
public sealed class OpenMeteoWeatherSource : IWeatherSource, IDisposable
{
    private const string Endpoint = "https://api.open-meteo.com/v1/forecast";

    /// <summary>Délai d'attente. Court volontairement : une information d'ambiance ne justifie pas de patienter.</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _http;
    private bool _disposed;

    public OpenMeteoWeatherSource()
        : this(new HttpClient { Timeout = RequestTimeout })
    {
    }

    /// <summary>
    /// Constructeur testable : le transport est injectable, ce qui permet
    /// d'exercer l'analyse de la réponse sans contact réseau.
    /// </summary>
    public OpenMeteoWeatherSource(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    public async Task<WeatherSnapshot> GetCurrentAsync(
        WeatherLocation location,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);

        // Les coordonnées sont mises en forme en culture invariante : le séparateur
        // décimal d'une culture francophone ferait échouer la requête.
        string query = string.Create(
            CultureInfo.InvariantCulture,
            $"latitude={location.Latitude:0.####}&longitude={location.Longitude:0.####}&current=temperature_2m,weather_code,is_day&timezone=auto");

        string url = $"{Endpoint}?{query}";

        try
        {
            using HttpResponseMessage response = await _http
                .GetAsync(url, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Le service météo a répondu {(int)response.StatusCode}.");
            }

            string body = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            return Parse(body);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Arrêt demandé : ce n'est pas une panne, la fonctionnalité est arrêtée.
            throw;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            // Réseau absent, DNS, délai dépassé : tous les cas se présentent au
            // greffon de la même façon — une erreur à signaler sans échouer.
            throw new InvalidOperationException($"Relevé météo impossible : {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Analyse la réponse du service. Exposée pour être vérifiable sans réseau.
    /// </summary>
    public static WeatherSnapshot Parse(string body)
    {
        using JsonDocument document = JsonDocument.Parse(body);

        if (!document.RootElement.TryGetProperty("current", out JsonElement current))
        {
            throw new InvalidOperationException("Réponse météo sans relevé courant.");
        }

        if (!current.TryGetProperty("temperature_2m", out JsonElement temperature)
            || !temperature.TryGetDouble(out double celsius))
        {
            throw new InvalidOperationException("Réponse météo sans température.");
        }

        int code = current.TryGetProperty("weather_code", out JsonElement weatherCode)
            && weatherCode.TryGetInt32(out int parsedCode)
                ? parsedCode
                : -1;

        bool isDay = !current.TryGetProperty("is_day", out JsonElement day)
            || day.TryGetInt32(out int parsedDay) && parsedDay != 0;

        DateTimeOffset observedAt = current.TryGetProperty("time", out JsonElement time)
            && time.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(
                time.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out DateTimeOffset parsedTime)
                ? parsedTime
                : DateTimeOffset.Now;

        return new WeatherSnapshot(celsius, code, isDay, observedAt);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _http.Dispose();
        GC.SuppressFinalize(this);
    }
}
