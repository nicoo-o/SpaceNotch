# Greffon d'exemple — Météo locale

Un greffon complet et fonctionnel pour SpaceNotch, écrit **sans modifier l'application** et en ne
référençant qu'un seul assemblage : `SpaceNotch.Core`.

Son but est de répondre à une question précise : *quelqu'un peut-il écrire un greffon en ne lisant
que cette documentation ?* Tout ce qui suit est vérifié par les tests de `tests/SpaceNotch.SamplePlugin.Tests`,
y compris le chargement de l'assemblage compilé par le chargeur réel.

```
╭──────────────────────────────╮
│  ☁  18 °C · Couvert          │
│     Paris · relevé de 16:30  │
│                              │
│      [ ⟳ Actualiser ]  [ °F ]│
╰──────────────────────────────╯
```

---

## 1. Ce qu'il faut référencer

| Élément | Valeur |
|---|---|
| Assemblage | `SpaceNotch.Core` — et **rien d'autre** |
| Ciblage | identique à celui de l'hôte (`net10.0-windows10.0.26100.0`) |
| Constructeur du greffon | public, **sans paramètre** |

Le ciblage doit correspondre : un projet `net10.0` ne peut pas référencer un projet
`net10.0-windows10.0.26100.0`. C'est la contrainte la moins évidente du lot, et la première qui fait
échouer une première tentative.

Dans ce dépôt, la référence est un `ProjectReference`. Pour un greffon distribué séparément, elle
devient une référence à `SpaceNotch.Core.dll`.

---

## 2. Les deux contrats

### Le greffon est une fabrique

```csharp
public interface IIslandPlugin
{
    string Name { get; }

    IEnumerable<IIslandFeature> CreateFeatures(IslandFeatureContext context);
}
```

Une **fabrique**, pas une fonctionnalité. Cela permet au greffon de construire ses propres
dépendances avant de créer sa fonctionnalité — ici la source de données et la configuration — et
cela rend le chargement vérifiable : un greffon qui ne peut produire aucune fonctionnalité est un
greffon invalide, et cela se voit tout de suite.

### La fonctionnalité

```csharp
public interface IIslandFeature : IAsyncDisposable
{
    string Id { get; }
    string DisplayName { get; }
    FeatureState State { get; }
    bool IsEnabled { get; }

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
    Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default);

    IEnumerable<IslandActivity> GetActivities();
    Task<bool> HandleActionAsync(IslandActionRequest request);
}
```

En pratique, on hérite de `IslandFeatureBase` et on implémente deux méthodes :

```csharp
public sealed class WeatherFeature : IslandFeatureBase
{
    public const string FeatureKey = "plugin.weather";

    public WeatherFeature(IActivityManager activities, IEventBus events, IWeatherSource source,
                          WeatherLocation location)
        : base(FeatureKey, "Météo locale", activities, events)
    {
    }

    protected override Task OnStartAsync(CancellationToken cancellationToken) { /* acquérir */ }
    protected override Task OnStopAsync()  { /* libérer, exactement ce qui a été acquis */ }
}
```

La classe de base apporte déjà : l'idempotence du cycle de vie, l'isolation des échecs, le retrait
automatique des activités à l'arrêt, et `PublishActivity` / `ReportError`.

### La règle qui compte : la symétrie

`OnStartAsync` **acquiert**, `OnStopAsync` **libère exactement la même chose**.

Un greffon est activable et désactivable depuis le menu de l'Island. Sans symétrie, la seconde
activation doublerait les abonnements et les minuteurs — c'est la fuite classique, et elle est
invisible jusqu'au jour où le processeur ne redescend plus.

Le greffon d'exemple détient deux ressources : un minuteur et un jeton d'annulation. Les deux sont
libérés, et un test vérifie qu'un arrêt suivi d'un redémarrage ne produit qu'une seule carte.

---

## 3. Publier une carte

```csharp
var activity = new IslandActivity
{
    Id = ActivityId,                       // stable
    FeatureId = Id,
    SceneKey = IslandSceneCatalog.Card,    // la scène générique
    Title = "18 °C · Couvert",
    Subtitle = "Paris · relevé de 16:30",
    IconKey = "\uE753",                    // glyphe littéral
    Priority = ActivityPriority.Background,
    Actions = [ /* voir §5 */ ]
};

PublishActivity(activity);
```

### L'identifiant est une identité, pas une étiquette

Republier sous le même `Id` **remplace** l'activité précédente. Le cycle de vie appartient au
gestionnaire de l'hôte : vous publiez, et vous cessez de vous en soucier. Vous n'avez ni à retirer
vos activités, ni à armer un minuteur d'expiration, ni à craindre l'accumulation.

### Les scènes appartiennent à l'hôte

`SceneKey` doit être **une clé déclarée** par `IslandSceneCatalog`. Le répertoire comprend :

| Clé | Usage |
|---|---|
| `IslandSceneCatalog.Card` | **celle à utiliser** — titre, sous-titre, icône, jusqu'à trois contrôles |
| `Pill` | pilule au repos |
| `Notification`, `Bluetooth`, `DropZone` | scènes génériques employées par les fonctionnalités intégrées |
| `Media`, `VolumeHud`, `Launcher`, … | scènes dédiées, mises en page dessinées |

`Card` existe précisément pour le contenu tiers : sans elle, la seule ressource serait de détourner
une scène existante — déclarer une météo comme « Bluetooth » pour qu'elle s'affiche — ce qui serait
un mensonge inscrit dans votre code.

**Une clé inconnue ne casse rien** : l'Island retombe sur la pilule fermée, et une ligne est écrite
au journal une seule fois, avec la liste des clés valides :

```
[SCENE] clé inconnue « weather » (fonctionnalité plugin.weather) : repli sur la pilule.
Clés valides : pill, media, volume-hud, …
```

C'est le repli le plus sûr — une fonctionnalité mal configurée ne peut pas faire disparaître
l'Island — et il est signalé pour que vous n'ayez pas à le deviner.

### Les icônes

`IconKey` accepte **deux formes** :

- une **clé logique** du répertoire de l'hôte : `"Music"`, `"Folder"`, `"Timer"`, `"Bluetooth"`,
  `"Notification"`, `"Clipboard"`, `"Volume"`, `"VolumeMute"`, `"Info"` ;
- un **glyphe littéral** : un unique caractère de la zone à usage privé, par exemple `"\uE753"`.

La seconde forme est ce qui vous permet d'apporter votre propre icône sans attendre qu'une clé soit
ajoutée pour votre domaine. Toute autre valeur retombe sur `Info`.

---

## 4. Glyphes : la table vérifiée

Ces codes proviennent de la [table officielle Segoe Fluent Icons](https://learn.microsoft.com/windows/apps/design/style/segoe-fluent-icons-font).
Ils sont donnés ici parce que les chercher est fastidieux et que se tromper produit une case vide.

| Glyphe | Code | Nom officiel |
|---|---|---|
| ☀ | `\uE706` | `Brightness` |
| ☾ | `\uE708` | `QuietHours` |
| ☁ | `\uE753` | `Cloud` |
| ❄ | `\uE9CA` | `Frigid` |
| ⚡ | `\uE945` | `LightningBolt` |
| ⟳ | `\uE72C` | `Refresh` |
| ↕ | `\uECC6` | `Unit` |
| ⚙ | `\uE713` | `Settings` |
| ⓘ | `\uE946` | `Info` |

**Il n'existe aucun glyphe de pluie dans Segoe Fluent Icons.** Les conditions pluvieuses retombent
donc sur le nuage. C'est une limite de la police, pas un choix — mieux vaut la connaître que la
découvrir après avoir essayé une dizaine de codes.

Vérifiez toujours un code avant de l'utiliser : `\uE9B9`, qui ressemble à une icône météo, est en
réalité `PuncKey6`.

---

## 5. Les actions

Vous **déclarez** vos contrôles ; l'hôte les affiche et vous renvoie l'identifiant de celui qui a été
activé.

```csharp
Actions =
[
    new ActivityAction(
        "weather.refresh", "Actualiser", "\uE72C",
        ActivityActionKind.Invoke, IsPrimary: true),

    new ActivityAction(
        "weather.toggle-unit", "En °F", "\uECC6")
]
```

| Paramètre | Rôle |
|---|---|
| `Id` | identifiant que vous recevrez en retour |
| `Label` | texte du bouton |
| `IconKey` | clé logique ou glyphe littéral |
| `Kind` | `Invoke`, `Toggle` ou `Open` |
| `IsPrimary` | mise en avant visuelle — un contrôle principal par carte |
| `IsEnabled` | contrôle visible mais inactif |

Le retour arrive dans `HandleActionAsync` :

```csharp
public override async Task<bool> HandleActionAsync(IslandActionRequest request)
{
    if (!string.Equals(request.ActivityId, ActivityId, StringComparison.Ordinal))
    {
        return false;
    }

    switch (request.ActionId)
    {
        case RefreshAction:
            await RefreshAsync().ConfigureAwait(false);
            return true;

        case ToggleUnitAction:
            _units = _units == WeatherUnits.Celsius ? WeatherUnits.Fahrenheit : WeatherUnits.Celsius;
            Publish();
            return true;

        default:
            return false;
    }
}
```

Retournez `true` **seulement** si l'action a été traitée. La vue ne connaît que des identifiants :
elle ne sait pas ce qu'ils font, et elle n'exécute jamais rien elle-même.

Pour une action continue — une position déplacée — `request.Value` transporte la cible sous forme de
texte neutre. Le cœur reste ainsi sans dépendance à un type de contenu particulier.

---

## 6. Les règles de performance

Ces règles ne sont pas des recommandations : l'application les applique à ses propres
fonctionnalités, et un greffon qui les ignore dégrade l'Island entière.

### Ne bloquez jamais le démarrage

Le registre de l'hôte démarre les fonctionnalités **en les attendant**. Attendre ici une réponse HTTP
retarderait l'apparition de l'Island de toute la durée de l'appel.

```csharp
protected override Task OnStartAsync(CancellationToken cancellationToken)
{
    _lifetime = new CancellationTokenSource();

    QueueRefresh();                 // premier relevé lancé, NON attendu
    _timer = new Timer(_ => QueueRefresh(), null, RefreshInterval, RefreshInterval);

    return Task.CompletedTask;      // rendu immédiatement
}
```

Un appelant — ou un test — qui veut savoir quand la carte est à jour attend `PendingRefresh`.

### Aucun sondage

`Événement → État → Animation → Repos` est la règle du projet. Un greffon ne doit pas vérifier son
état périodiquement.

Il existe une exception, et le greffon météo l'illustre : un **minuteur à longue période, borné,
désarmé à l'arrêt**. Une donnée d'ambiance n'a aucun événement pour la signaler ; quinze minutes de
période et un seul minuteur sont acceptables là où une vérification à la seconde ne le serait pas.

Désactivée, la fonctionnalité doit cesser **tout** travail. Un test le vérifie : après `StopAsync`,
aucun appel n'est émis et aucune carte n'est publiée.

### Publier depuis un fil d'arrière-plan est sûr

Publier depuis un rappel système, un fil de minuteur ou une continuation asynchrone ne demande
**aucune précaution** : c'est l'hôte qui rétablit le fil d'interface. Votre greffon n'a pas à s'en
occuper, et il ne pourrait pas s'en occuper — le contexte qui lui est remis ne contient aucun
répartiteur, par choix.

> Ce comportement a été un défaut, pas une évidence. Les fonctionnalités intégrées publiaient depuis
> des rappels que Windows marshale déjà, si bien que le problème est resté invisible jusqu'à ce
> qu'un greffon publie depuis un fil de travail. Le symptôme, alors, est un `COMException` **au
> message vide** — ce qui a valu à `MiniLogger` de décrire désormais une exception par son type et
> son code de retour. Voir ADR-011.

---

## 7. Erreurs : deux niveaux, à ne pas confondre

```csharp
catch (Exception ex)
{
    ReportError(ex);   // incident non fatal, l'hôte journalise, la fonctionnalité continue
}
```

| Méthode | Effet |
|---|---|
| `ReportError(ex)` | signale et **continue**. À utiliser dès que l'incident est local — réseau absent, périphérique en veille. |
| exception dans `OnStartAsync` | met la fonctionnalité en `Faulted`. À réserver à une impossibilité réelle de démarrer. |

`ReportError` est la bonne réponse dans la quasi-totalité des cas. Un service indisponible ne doit
pas rendre votre fonctionnalité définitivement inutilisable, et il ne doit pas non plus passer
inaperçu : la classe de base conserve la dernière erreur et la porte à la connaissance de l'hôte.

Le greffon d'exemple conserve le dernier relevé connu après un échec, et n'affiche rien plutôt qu'une
valeur inventée s'il n'en a jamais obtenu.

---

## 8. Le piège JSON

Le projet désactive la sérialisation JSON par réflexion
(`JsonSerializerIsReflectionEnabledByDefault=false`), parce que le toolchain d'élagage de WinUI la
rend inopérante. **Votre greffon y est soumis comme le reste du projet.**

```csharp
// Échoue à l'exécution — et seulement à l'exécution.
var data = JsonSerializer.Deserialize<MyDto>(json);

// Fonctionne : aucune réflexion.
using JsonDocument document = JsonDocument.Parse(json);
```

Ou utilisez un contexte source-généré. Le symptôme, sinon, est une exception attrapée par un `catch`
de tolérance : une fonctionnalité qui ne fait rien, sans message d'erreur. C'est exactement le genre
de panne qui coûte une soirée.

---

## 9. Configuration

Un greffon **ne reçoit pas** les préférences de l'hôte. Le contexte qui lui est remis contient de
quoi publier une activité et écouter des événements, et rien d'autre — c'est délibéré : une API de
greffon large est une API qu'on ne peut plus restreindre.

Le greffon d'exemple lit donc l'environnement :

```bash
set NOTCHFLOW_WEATHER_PLACE=Lyon
set NOTCHFLOW_WEATHER_LATITUDE=45.7640
set NOTCHFLOW_WEATHER_LONGITUDE=4.8357
```

Une valeur absente ou invalide retombe sur Paris plutôt que de rendre le greffon inutilisable. Un
greffon réel lirait plutôt un fichier à côté de son assemblage — en tenant compte du §8.

---

## 10. Construire, installer, vérifier

```bash
# 1. Construire
dotnet build samples/SpaceNotch.SamplePlugin.Weather -c Release -p:Platform=x64

# 2. Installer : copier l'assemblage dans le dossier des greffons
cp samples/SpaceNotch.SamplePlugin.Weather/bin/x64/Release/net10.0-windows10.0.26100.0/SpaceNotch.SamplePlugin.Weather.dll \
   "$LOCALAPPDATA/SpaceNotch/plugins/"

# 3. Lancer SpaceNotch, puis vérifier le journal
tail -20 "$LOCALAPPDATA/SpaceNotch/logs/spacenotch.log"
```

Le journal doit contenir :

```
[PLUGIN] …
Greffons chargés : 1 fonctionnalité(s), 0 échec(s)
```

`0 échec(s)` est la ligne qui compte. Un greffon défaillant est **isolé** : les autres se chargent,
et l'échec est rapporté avec son nom — il ne disparaît pas silencieusement.

### Tester votre greffon

`tests/SpaceNotch.SamplePlugin.Tests` montre comment : une source de données simulée, puis le vrai
cycle de vie — démarrage, publication, action, arrêt, redémarrage. Le test de chargement, lui, ne
référence **aucun type du greffon** : il copie l'assemblage dans un dossier temporaire et observe ce
que l'hôte observe. C'est le même chemin qu'un greffon livré par un tiers.

---

## 11. Limites connues

Ce que l'API ne permet pas encore, dit franchement :

- **Aucune vue personnalisée.** Un greffon ne peut pas dessiner sa propre scène ; il utilise la carte
  générique. Une météo soignée y tient, un visualiseur audio non.
- **Aucune préférence partagée.** La fenêtre de réglages de l'hôte ne connaît pas votre greffon ;
  configurez-le de votre côté.
- **Aucun retrait à chaud.** Le chargement a lieu au démarrage ; pour recharger un greffon, relancez
  l'application.
- **`Payload` n'est lu par aucune scène générique.** Il existe pour les scènes dédiées ; une carte
  rendue par la scène générique doit se contenter de `Title`, `Subtitle`, `IconKey` et `Actions`.

Ces limites sont des manques de l'API, pas des défauts de conception de votre greffon. Si vous butez
sur l'une d'elles, c'est le signal qu'une évolution de `SpaceNotch.Core` est nécessaire.

---

## Licence

Cet exemple est fourni comme point de départ : copiez-le, renommez-le, réutilisez-le sans
cérémonie.
