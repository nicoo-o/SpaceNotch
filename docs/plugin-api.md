# API de greffons

## Le contrat

Un greffon est une **fabrique**, pas une fonctionnalité :

```csharp
public interface IIslandPlugin
{
    string Name { get; }

    IEnumerable<IIslandFeature> CreateFeatures(IslandFeatureContext context);
}
```

Pourquoi une fabrique ? Parce qu'un greffon a souvent besoin d'instancier ses propres dépendances
internes avant de créer sa fonctionnalité. Et parce que le chargement devient *vérifiable* : un
greffon qui ne peut pas produire de fonctionnalité est un greffon invalide, et cela se voit
immédiatement au chargement au lieu de se manifester par une absence silencieuse.

## Le contexte

```csharp
public sealed record IslandFeatureContext(IActivityManager Activities, IEventBus Events);
```

Le contexte est **volontairement étroit**. Un greffon ne reçoit ni l'application, ni ses fenêtres, ni
son état. Il reçoit de quoi publier une activité et de quoi écouter des événements — l'exact
nécessaire pour être une fonctionnalité, et rien de plus. Une API de greffon large serait une API
qu'on ne pourrait jamais restreindre.

## La fonctionnalité

```csharp
public interface IIslandFeature : IAsyncDisposable
{
    string Id { get; }
    FeatureState State { get; }
    bool IsEnabled { get; }
    event EventHandler<FeatureStateChangedEventArgs>? StateChanged;

    Task InitializeAsync(CancellationToken ct = default);
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();
    Task SetEnabledAsync(bool enabled, CancellationToken ct = default);

    IReadOnlyList<IslandActivity> GetActivities();
    Task<bool> HandleActionAsync(IslandActionRequest request);
}
```

### Le contrat qui compte : la symétrie

`OnStartAsync` acquiert les écouteurs système ; `OnStopAsync` les libère. **Ces deux méthodes doivent
être strictement symétriques.** C'est cette symétrie, et rien d'autre, qui rend une bascule rejouable
sans doubler les abonnements : activer, désactiver, réactiver, et le second `AddClipboardFormatListener`
aurait sans le pendant un second abonnement jamais retiré.

`IslandFeatureBase` fournit le squelette : la garde d'état, la journalisation et l'isolation des
erreurs sont déjà écrites. Hériter de cette classe et implémenter les deux méthodes symétriques est
la voie normale.

### Isolation des erreurs

Une fonctionnalité qui échoue ne fait pas tomber les autres. L'échec est rapporté — `ErrorReported`
et `StateChanged` remontent au registre, qui journalise — et le démarrage continue. Une erreur
partielle signalée par une fonctionnalité qui continue de tourner est rapportée comme les autres :
un échec mineur n'est pas un succès.

## Exemple complet et exécutable

Un greffon entier — météo locale, source de données réelle, configuration, tests — vit dans
[`samples/SpaceNotch.SamplePlugin.Weather`](../samples/SpaceNotch.SamplePlugin.Weather/README.md).

Il est écrit sans modifier l'application et ne référence que `SpaceNotch.Core`. Son README est le
guide d'un auteur tiers : démarrage, publication d'une carte, actions, glyphes, pièges, limites.

Le squelette minimal, pour situer :

```csharp
public sealed class MyFeature : IslandFeatureBase
{
    public const string FeatureKey = "plugin.mine";

    public MyFeature(IActivityManager activities, IEventBus events, IMySource source)
        : base(FeatureKey, "Mon greffon", activities, events) { }

    // Acquérir ici. Libérer exactement la même chose dans OnStopAsync.
    protected override Task OnStartAsync(CancellationToken ct) { /* … */ return Task.CompletedTask; }

    protected override Task OnStopAsync() { /* … */ return Task.CompletedTask; }
}
```

### Ce que la carte accepte

Une activité publiée sous `IslandSceneCatalog.Card` rend quatre choses : `Title`, `Subtitle`,
`IconKey` et `Actions`. Le reste — mise en page, espacement, typographie — appartient à l'hôte.

| Champ | Attendu |
|---|---|
| `Id` | Identifiant **stable** : republier sous le même identifiant remplace au lieu d'empiler. |
| `SceneKey` | Une clé de `IslandSceneCatalog`. `Card` est celle destinée au contenu tiers. |
| `IconKey` | Clé logique (`"Timer"`, `"Folder"`…) **ou** glyphe littéral (`"\uE753"`). |
| `Actions` | Contrôles déclarés, rendus tels quels et renvoyés par identifiant. |
| `Priority` | `Background` pour une information d'ambiance : elle ne doit pas supplanter un appel. |
| `Eyebrow` | Facultatif. Ligne de contexte discrète au-dessus du titre : « Read app-sidebar.tsx · 219 lines ». |
| `Progress` | Facultatif. Avancement de 0 à 1. |
| `MotionState` | `Working` pendant un travail, `Completing` à sa fin, `Error` en cas d'échec, `Idle` sinon. |
| `MotionPreset` | Nature du mouvement demandé : `Read`, `Think`, `Search`, `Process`, `Sync`, `Drop`. |
| `Policy` | Facultatif. `Persistent`, `Passive`, `Temporary` ou `Interrupting` ; déduit sinon. |
| `Metric` | Facultatif. Valeur courte à droite de la forme compacte : « 62 % », « 12 Mo », « ✓ ». Déduite de `Progress` sinon. |
| `Artwork` | Facultatif. Image encodée (PNG, JPEG) montrée à la place du glyphe, qui grandit à l'ouverture. |
| `ExpandedFootprint` | Facultatif. Taille ouverte propre au contenu, quand celle de la scène ne suffit pas. |

**Le mouvement se demande, il ne se dessine pas.** Un greffon qui travaille déclare
`MotionState = Working` et un préréglage ; l'hôte rend la matière hypnotique, la synchronise avec
l'atmosphère, la fige si l'utilisateur l'a demandé, et l'arrête au repos. Le greffon météo
d'exemple publie `Sync` pendant chaque relevé. Voir
[ADR-018](decisions/ADR-018-mouvement-hypnotique.md).

Une clé de scène inconnue ne casse rien — l'Island retombe sur la pilule — mais elle est
**signalée une fois au journal**, avec la liste des clés valides. Sans ce signal, un greffon mal
configuré se contenterait de ne jamais s'afficher sans rien dire.

### Règles de performance

Trois règles que l'application applique à ses propres fonctionnalités, et auxquelles un greffon
n'échappe pas :

1. **Ne jamais bloquer le démarrage.** Le registre attend chaque `StartAsync` : y attendre une
   réponse réseau repousserait l'apparition de l'Island d'autant.
2. **Aucun sondage.** Un minuteur à longue période, borné et désarmé à l'arrêt, est l'exception
   admise ; une vérification périodique courte ne l'est pas.
3. **Désactivé = zéro travail.** Après `StopAsync`, plus aucun appel, plus aucune publication.

Publier depuis un fil d'arrière-plan est sûr : **c'est l'hôte qui rétablit le fil d'interface**, pour
tous les signaux qui touchent son rendu. Un greffon n'a donc aucune précaution de fil à prendre — et
il ne pourrait pas en prendre, son contexte ne contenant aucun répartiteur. Voir
[ADR-011](decisions/ADR-011-fil-interface.md).

### Le piège JSON

`Directory.Build.props` déclare `JsonSerializerIsReflectionEnabledByDefault=false` pour tous les
projets, parce que l'élagage WinUI rend la sérialisation réflexive inopérante. **Un greffon y est
soumis.** Un `JsonSerializer.Deserialize<T>` réflexif compile et échoue à l'exécution — souvent
absorbé par un `catch` de tolérance, ce qui donne une fonctionnalité qui ne fait rien sans message.
Utilisez `JsonDocument`, ou un contexte source-généré.

### Configuration

Le contexte transmis ne porte **ni l'application, ni ses fenêtres, ni ses préférences** : c'est
volontaire, une API de greffon large ne peut plus être restreinte. Un greffon se configure donc lui-
même — variables d'environnement, ou fichier à côté de son assemblage en tenant compte du piège
JSON ci-dessus.

### Restriction de fil

Aucune. Une fonctionnalité peut publier depuis un rappel système, un fil de minuteur ou une
continuation asynchrone : la fenêtre rétablit le fil d'interface elle-même.

### Limites connues

- **Aucune vue personnalisée** : un greffon utilise la carte générique, il ne dessine pas sa scène.
- **Aucune préférence partagée** : la fenêtre de réglages ne connaît pas les greffons installés.
- **Aucun rechargement à chaud** : les greffons sont chargés au démarrage.
- **`Payload` n'est lu par aucune scène générique** : il est destiné aux scènes dédiées.

## Chargement

`PluginLoader` parcourt `%AppData%\SpaceNotch\plugins\`, charge chaque assembly et instancie les types
implémentant `IIslandPlugin`. Les échecs sont **isolés et rapportés** : un greffon mal formé, une
dépendance manquante ou un constructeur fautif produit une ligne de journal, pas un arrêt de
l'application.

Les greffons sont chargés **avant** la création du registre : ils en font donc partie dès le
démarrage et bénéficient exactement du même cycle de vie, des mêmes bascules et du même routage
d'actions que les fonctionnalités intégrées.

## Vérification

`tests/SpaceNotch.TestPlugin` est un greffon réel, compilé et chargé par les tests. Il contient
délibérément des cas dégradés :

- une fabrique valide ;
- une fabrique qui lève une exception ;
- un type qui implémente l'interface sans être instanciable.

Les tests vérifient qu'un greffon défaillant est signalé et **n'empêche pas** les autres de se
charger. Une API de greffon qui n'est testée qu'avec un cas nominal est une API dont l'isolation des
échecs n'est pas démontrée.

## Stabilité

L'API est en v0.x. Tant que la version majeure est zéro, une rupture est possible mais sera
documentée dans un ADR et annoncée dans les notes de version. `SpaceNotch.Core` est la seule
dépendance qu'un greffon doit référencer — et cette dépendance ne tire aucune bibliothèque Windows,
ce qui permet de compiler un greffon sans machine Windows graphique.
