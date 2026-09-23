using System;
using System.Collections.Generic;
using System.Globalization;
using NotchFlow.Core.Features;

namespace NotchFlow.SamplePlugin.Weather;

/// <summary>
/// Configuration du greffon, lue depuis l'environnement.
///
/// Un greffon ne reçoit pas les préférences de l'hôte : le contexte qui lui est
/// remis contient de quoi publier une activité et écouter des événements, et rien
/// d'autre. Une configuration propre lui est donc nécessaire. Les variables
/// d'environnement sont le moyen le plus simple qui ne dépende d'aucune API :
/// <list type="bullet">
/// <item><c>NOTCHFLOW_WEATHER_PLACE</c> — nom affiché du lieu ;</item>
/// <item><c>NOTCHFLOW_WEATHER_LATITUDE</c> — latitude décimale ;</item>
/// <item><c>NOTCHFLOW_WEATHER_LONGITUDE</c> — longitude décimale.</item>
/// </list>
/// Un greffon réel lirait plutôt un fichier de configuration à côté de son
/// assemblage. Attention, dans ce cas : la sérialisation JSON par réflexion est
/// désactivée à l'échelle du projet, il faut donc un contexte source-généré ou une
/// lecture par <c>JsonDocument</c>.
/// </summary>
public sealed record WeatherConfiguration(WeatherLocation Location)
{
    /// <summary>Lieu par défaut, utilisé lorsque rien n'est configuré.</summary>
    public static readonly WeatherLocation DefaultLocation = new("Paris", 48.8566, 2.3522);

    /// <summary>
    /// Construit la configuration depuis l'environnement, en retombant sur le lieu
    /// par défaut dès qu'une valeur est absente ou illisible — une configuration
    /// fautive ne doit pas empêcher le greffon de fonctionner.
    /// </summary>
    public static WeatherConfiguration FromEnvironment()
        => From(Environment.GetEnvironmentVariable);

    /// <summary>
    /// Construit la configuration depuis une source de valeurs quelconque.
    ///
    /// La lecture passe par une fonction plutôt que d'appeler directement
    /// l'environnement : c'est ce qui rend la validation vérifiable. Un test qui
    /// devrait modifier les variables d'environnement du processus pour éprouver
    /// une latitude hors bornes serait à la fois instable — l'environnement est
    /// global et partagé — et destructeur pour les autres tests.
    /// </summary>
    public static WeatherConfiguration From(Func<string, string?> read)
    {
        ArgumentNullException.ThrowIfNull(read);

        string name = ReadString(read, "NOTCHFLOW_WEATHER_PLACE") ?? DefaultLocation.Name;

        double latitude = ReadDouble(read, "NOTCHFLOW_WEATHER_LATITUDE", -90, 90)
            ?? DefaultLocation.Latitude;

        double longitude = ReadDouble(read, "NOTCHFLOW_WEATHER_LONGITUDE", -180, 180)
            ?? DefaultLocation.Longitude;

        return new WeatherConfiguration(new WeatherLocation(name, latitude, longitude));
    }

    private static string? ReadString(Func<string, string?> read, string variable)
    {
        string? value = read(variable);

        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static double? ReadDouble(
        Func<string, string?> read,
        string variable,
        double minimum,
        double maximum)
    {
        string? raw = ReadString(read, variable);

        if (raw is null)
        {
            return null;
        }

        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            return null;
        }

        return value >= minimum && value <= maximum ? value : null;
    }
}

/// <summary>
/// Greffon d'exemple : météo locale.
///
/// Un greffon est une <em>fabrique</em>, et ce choix n'est pas cosmétique. C'est
/// ce qui lui permet de construire ses propres dépendances — ici la source de
/// données et la configuration — avant de créer sa fonctionnalité, et c'est aussi
/// ce qui rend le chargement vérifiable : un greffon qui ne peut produire aucune
/// fonctionnalité est un greffon invalide, et cela se voit immédiatement au
/// chargement au lieu de se manifester par une absence silencieuse.
///
/// Un greffon est instancié par son constructeur public sans paramètre, puis
/// <see cref="CreateFeatures"/> est appelée une fois au démarrage.
/// </summary>
public sealed class WeatherPlugin : IIslandPlugin
{
    private WeatherConfiguration? _configuration;

    /// <summary>Nom lisible, utilisé par les diagnostics et les réglages.</summary>
    public string Name => "Météo locale (exemple)";

    /// <summary>
    /// Configuration retenue, exposée pour les diagnostics et les tests.
    /// </summary>
    public WeatherConfiguration Configuration => _configuration ??= WeatherConfiguration.FromEnvironment();

    public IEnumerable<IIslandFeature> CreateFeatures(IslandFeatureContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return
        [
            new WeatherFeature(
                context.Activities,
                context.Events,
                new OpenMeteoWeatherSource(),
                Configuration.Location)
        ];
    }
}
