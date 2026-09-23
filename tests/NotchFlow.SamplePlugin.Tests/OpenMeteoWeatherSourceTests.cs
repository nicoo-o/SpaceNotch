using System;
using NotchFlow.SamplePlugin.Weather;
using Xunit;

namespace NotchFlow.SamplePlugin.Tests;

/// <summary>
/// Analyse de la réponse du service météo.
///
/// La réponse utilisée est celle, réellement observée, du service : champ
/// <c>current</c>, température, code WMO, indicateur jour/nuit, horodatage local.
/// Un exemple inventé ne vérifierait rien.
/// </summary>
public sealed class OpenMeteoWeatherSourceTests
{
    private const string ValidResponse = """
        {
          "latitude": 48.85,
          "longitude": 2.35,
          "timezone": "Europe/Paris",
          "current": {
            "time": "2026-09-19T16:30",
            "temperature_2m": 17.4,
            "weather_code": 3,
            "is_day": 1
          }
        }
        """;

    [Fact]
    public void ValidResponse_IsRead()
    {
        WeatherSnapshot snapshot = OpenMeteoWeatherSource.Parse(ValidResponse);

        Assert.Equal(17.4, snapshot.TemperatureCelsius, 4);
        Assert.Equal(3, snapshot.WeatherCode);
        Assert.True(snapshot.IsDay);
        Assert.Equal(2026, snapshot.ObservedAt.Year);
        Assert.Equal(16, snapshot.ObservedAt.Hour);
    }

    [Fact]
    public void MissingCurrentSection_IsAnExplicitFailure()
    {
        const string body = """{ "latitude": 48.85, "longitude": 2.35 }""";

        Assert.Throws<InvalidOperationException>(() => OpenMeteoWeatherSource.Parse(body));
    }

    [Fact]
    public void MissingTemperature_IsAnExplicitFailure()
    {
        const string body = """{ "current": { "weather_code": 3 } }""";

        Assert.Throws<InvalidOperationException>(() => OpenMeteoWeatherSource.Parse(body));
    }

    [Fact]
    public void MalformedBody_RaisesTheJsonReaderError()
    {
        // Le corps n'est pas du JSON : l'exception doit remonter, pas être
        // silencieusement transformée en relevé vide.
        Assert.ThrowsAny<Exception>(() => OpenMeteoWeatherSource.Parse("pas du json"));
    }

    [Fact]
    public void MissingOptionalFields_DegradeInsteadOfFailing()
    {
        const string body = """{ "current": { "temperature_2m": 9.5 } }""";

        WeatherSnapshot snapshot = OpenMeteoWeatherSource.Parse(body);

        Assert.Equal(9.5, snapshot.TemperatureCelsius, 4);

        // Sans indicateur jour/nuit, le jour est supposé ; sans horodatage, l'heure
        // courante est retenue. Une carte légèrement imprécise vaut mieux qu'une
        // carte absente.
        Assert.True(snapshot.IsDay);
        Assert.NotEqual(default, snapshot.ObservedAt);
    }
}
