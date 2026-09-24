using System;
using System.Threading;
using System.Threading.Tasks;

namespace SpaceNotch.SamplePlugin.Weather;

/// <summary>
/// Accès à un relevé météo.
///
/// Cette interface est la couture du greffon, et elle n'est pas décorative : sans
/// elle, la fonctionnalité dépendrait directement du réseau, et il serait
/// impossible de vérifier son comportement — publication, remplacement,
/// rafraîchissement, panne — sans dépendre d'un service externe. Les tests
/// injectent une source simulée et exercent la fonctionnalité pour de vrai.
///
/// C'est le premier conseil à retenir pour un greffon : isoler l'entrée derrière
/// une interface, même si une seule implémentation existe.
/// </summary>
public interface IWeatherSource
{
    /// <summary>
    /// Relève la météo du lieu demandé.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Lancée si la réponse est inexploitable (réseau absent, service indisponible,
    /// format inattendu). La fonctionnalité la traduit en erreur non fatale ; elle
    /// ne remonte donc jamais jusqu'à l'hôte.
    /// </exception>
    Task<WeatherSnapshot> GetCurrentAsync(WeatherLocation location, CancellationToken cancellationToken);
}
