using System;

namespace NotchFlow.SamplePlugin.Weather;

/// <summary>
/// Unité d'affichage des températures.
/// </summary>
public enum WeatherUnits
{
    Celsius,

    Fahrenheit
}

/// <summary>
/// Lieu observé.
/// </summary>
/// <param name="Name">Nom affiché, tel que fourni par la configuration du greffon.</param>
public sealed record WeatherLocation(string Name, double Latitude, double Longitude);

/// <summary>
/// Relevé météo brut, tel que renvoyé par une source.
///
/// Ce modèle ne contient <em>aucune</em> mise en forme : ni « 18 °C », ni nom
/// d'icône, ni horodatage lisible. La traduction en contenu d'Island appartient à
/// la fonctionnalité, ce qui permet de changer de source — ou d'unité — sans
/// toucher à la présentation, et inversement.
/// </summary>
/// <param name="TemperatureCelsius">Température, toujours en degrés Celsius : l'unité d'affichage est une décision de présentation.</param>
/// <param name="WeatherCode">Code WMO décrivant la condition.</param>
/// <param name="IsDay">Vrai s'il fait jour au lieu observé.</param>
/// <param name="ObservedAt">Instant du relevé, tel que rapporté par la source.</param>
public sealed record WeatherSnapshot(
    double TemperatureCelsius,
    int WeatherCode,
    bool IsDay,
    DateTimeOffset ObservedAt);
