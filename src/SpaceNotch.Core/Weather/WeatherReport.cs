using System.Globalization;
using System.Text.Json;

namespace SpaceNotch.Core.Weather;

/// <summary>Le temps qu'il fait : température, code WMO, jour ou nuit.</summary>
public sealed record WeatherReport(double TemperatureC, int Code, bool IsDay)
{
    /// <summary>« 14° », arrondi.</summary>
    public string Temperature => Math.Round(TemperatureC).ToString("0", CultureInfo.CurrentCulture) + "°";

    /// <summary>Icône pixel correspondante.</summary>
    public string IconKey => WeatherCodes.IconFor(Code, IsDay);
}

/// <summary>Une ville trouvée par le géocodage.</summary>
public sealed record WeatherPlace(string Name, double Latitude, double Longitude);

/// <summary>
/// Aperçu météo (F10) : codes WMO d'Open-Meteo, adresses du service (ouvert,
/// sans clé) et lecture de ses réponses. Aucune position n'est lue sur la
/// machine : la ville vient des réglages.
/// </summary>
public static class WeatherCodes
{
    /// <summary>Intervalle d'actualisation.</summary>
    public static readonly TimeSpan Refresh = TimeSpan.FromMinutes(30);

    public static string IconFor(int code, bool isDay) => code switch
    {
        0 or 1 => isDay ? "WeatherSun" : "Moon",
        2 or 3 => "WeatherCloud",
        45 or 48 => "WeatherFog",
        >= 51 and <= 67 => "WeatherRain",
        >= 80 and <= 82 => "WeatherRain",
        >= 71 and <= 77 => "WeatherSnow",
        85 or 86 => "WeatherSnow",
        >= 95 and <= 99 => "WeatherStorm",
        _ => "WeatherCloud"
    };

    /// <summary>Mot court du temps, pour les lecteurs d'écran.</summary>
    public static string Describe(int code, bool french) => IconFor(code, true) switch
    {
        "WeatherSun" => french ? "Dégagé" : "Clear",
        "WeatherFog" => french ? "Brouillard" : "Fog",
        "WeatherRain" => french ? "Pluie" : "Rain",
        "WeatherSnow" => french ? "Neige" : "Snow",
        "WeatherStorm" => french ? "Orage" : "Storm",
        _ => french ? "Nuageux" : "Cloudy"
    };

    public static Uri ForecastUri(double latitude, double longitude)
        => new(string.Create(
            CultureInfo.InvariantCulture,
            $"https://api.open-meteo.com/v1/forecast?latitude={latitude:0.####}&longitude={longitude:0.####}&current=temperature_2m,weather_code,is_day"));

    public static Uri GeocodingUri(string city, bool french)
        => new("https://geocoding-api.open-meteo.com/v1/search?count=1&language=" + (french ? "fr" : "en") + "&name=" + Uri.EscapeDataString(city.Trim()));

    public static WeatherReport? ParseForecast(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("current", out JsonElement current))
            {
                return null;
            }

            double temperature = current.GetProperty("temperature_2m").GetDouble();
            int code = current.GetProperty("weather_code").GetInt32();
            bool isDay = !current.TryGetProperty("is_day", out JsonElement day) || day.GetInt32() == 1;
            return new WeatherReport(temperature, code, isDay);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    public static WeatherPlace? ParsePlace(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("results", out JsonElement results) || results.GetArrayLength() == 0)
            {
                return null;
            }

            JsonElement first = results[0];
            return new WeatherPlace(
                first.GetProperty("name").GetString() ?? string.Empty,
                first.GetProperty("latitude").GetDouble(),
                first.GetProperty("longitude").GetDouble());
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
