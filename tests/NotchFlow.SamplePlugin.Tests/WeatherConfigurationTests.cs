using System;
using System.Collections.Generic;
using NotchFlow.SamplePlugin.Weather;
using Xunit;

namespace NotchFlow.SamplePlugin.Tests;

/// <summary>
/// Configuration du greffon.
///
/// Chaque cas vérifie qu'une valeur absente ou invalide retombe sur le défaut
/// plutôt que de produire un lieu inexploitable : un greffon mal configuré doit
/// continuer de fonctionner, pas disparaître.
/// </summary>
public sealed class WeatherConfigurationTests
{
    private static Func<string, string?> Values(params (string Key, string? Value)[] entries)
    {
        var map = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach ((string key, string? value) in entries)
        {
            map[key] = value;
        }

        return key => map.TryGetValue(key, out string? value) ? value : null;
    }

    [Fact]
    public void NoConfiguration_YieldsTheDefaultLocation()
    {
        WeatherConfiguration configuration = WeatherConfiguration.From(Values());

        Assert.Equal("Paris", configuration.Location.Name);
        Assert.Equal(48.8566, configuration.Location.Latitude, 4);
        Assert.Equal(2.3522, configuration.Location.Longitude, 4);
    }

    [Fact]
    public void CompleteConfiguration_IsRead()
    {
        WeatherConfiguration configuration = WeatherConfiguration.From(
            Values(
                ("NOTCHFLOW_WEATHER_PLACE", "Lyon"),
                ("NOTCHFLOW_WEATHER_LATITUDE", "45.7640"),
                ("NOTCHFLOW_WEATHER_LONGITUDE", "4.8357")));

        Assert.Equal("Lyon", configuration.Location.Name);
        Assert.Equal(45.7640, configuration.Location.Latitude, 4);
        Assert.Equal(4.8357, configuration.Location.Longitude, 4);
    }

    [Fact]
    public void LatitudeOutOfRange_FallsBackRatherThanProducingAnImpossiblePlace()
    {
        WeatherConfiguration configuration = WeatherConfiguration.From(
            Values(("NOTCHFLOW_WEATHER_LATITUDE", "412")));

        Assert.Equal(WeatherConfiguration.DefaultLocation.Latitude, configuration.Location.Latitude, 4);
    }

    [Fact]
    public void UnparsableNumber_FallsBackRatherThanThrowing()
    {
        WeatherConfiguration configuration = WeatherConfiguration.From(
            Values(
                ("NOTCHFLOW_WEATHER_LATITUDE", "quarante-huit"),
                ("NOTCHFLOW_WEATHER_LONGITUDE", "2,3522")));

        // La virgule décimale d'une culture francophone est refusée : l'API attend
        // un point. Le défaut est préférable à une requête systématiquement rejetée.
        Assert.Equal(WeatherConfiguration.DefaultLocation.Latitude, configuration.Location.Latitude, 4);
        Assert.Equal(WeatherConfiguration.DefaultLocation.Longitude, configuration.Location.Longitude, 4);
    }

    [Fact]
    public void BlankPlace_IsIgnoredRatherThanDisplayedEmpty()
    {
        WeatherConfiguration configuration = WeatherConfiguration.From(
            Values(("NOTCHFLOW_WEATHER_PLACE", "   ")));

        Assert.Equal(WeatherConfiguration.DefaultLocation.Name, configuration.Location.Name);
    }
}
