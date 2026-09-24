using System.Text.Json.Serialization;

namespace SpaceNotch.Infrastructure.Config;

/// <summary>
/// Métadonnées de sérialisation de la configuration, produites à la compilation.
///
/// La génération à la compilation n'est pas une optimisation de confort ici, elle
/// est obligatoire. Le toolchain WinUI active l'élagage, et les cibles d'élagage
/// désactivent la sérialisation JSON par réflexion
/// (<c>System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault=false</c>).
/// Toute sérialisation réflexive échoue alors à l'exécution — dans un
/// <c>catch</c> de tolérance, cela se traduit par une configuration jamais lue ni
/// écrite, sans le moindre signe extérieur.
///
/// Les énumérations sont écrites en clair plutôt qu'en nombres : la configuration
/// est un fichier destiné à être relu et modifié à la main.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext;
