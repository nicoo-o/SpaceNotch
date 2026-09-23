# Système d'activités

## Le modèle

Une activité est une unité d'information contextuelle. Tout ce dont le rendu a besoin y figure :
la fonctionnalité propriétaire, la nature de l'information, la scène à présenter, les actions
disponibles et une teinte d'ambiance facultative.

```csharp
public sealed class IslandActivity
{
    public required string Id { get; init; }        // stable, sert d'identité
    public required string FeatureId { get; init; } // propriétaire
    public required string SceneKey { get; init; }  // résolue en vue par le catalogue
    public required string Title { get; set; }
    public string? Subtitle { get; set; }
    public string? IconKey { get; set; }
    public string? Source { get; set; }
    public IslandActivityState State { get; set; }
    public ActivityPriority Priority { get; set; }
    public TimeSpan? Duration { get; init; }
    public IReadOnlyList<ActivityAction> Actions { get; init; }
    public ActivityTint? Tint { get; init; }
    public object? Payload { get; set; }
}
```

## Le cycle de vie appartient au gestionnaire

C'est la décision structurante du projet. Une fonctionnalité **ne retire pas** ses activités, ne
planifie pas leur expiration, et ne se préoccupe pas de leur accumulation. `ActivityManager` s'en
charge :

| Situation | Comportement | Pourquoi |
|---|---|---|
| Republication sous le même `Id` | Remplacement, pas ajout | Un curseur de volume produit des dizaines de valeurs par seconde ; sans cela, l'Island accumulerait des centaines d'entrées. |
| `Duration` écoulée | Retrait automatique | Une fonctionnalité ne doit pas orchestrer un `Timer` pour se nettoyer. |
| Plus de N activités de fond | Éviction des plus anciennes | Un plafond borne la mémoire sans intervention. |

L'hôte n'arme qu'**un seul minuteur borné**, à usage unique, pour la prochaine échéance — et le
désarme quand il n'y a plus rien à expirer. Aucune scrutation périodique n'existe.

Vérifié par test : 50 changements de volume produisent **1** activité ; 10 activités de fond sont
ramenées à 3.

## Priorités

```
CRITICAL    appel entrant, batterie critique
HIGH        batterie faible
NORMAL      téléchargement terminé
BACKGROUND  lecture en cours
```

L'Island choisit automatiquement ce qu'elle présente. L'utilisateur peut forcer un autre choix par
la molette ou les flèches, mais cet arbitrage manuel est temporaire.

## La pile

Plusieurs activités coexistent. La première est présentée, les autres sont indiquées par
`StackIndicator`. La navigation est réactive : un cran de molette, une flèche, et la présentation
change — sans toucher à la liste elle-même.

## Scènes

Une activité ne décrit pas sa vue ; elle déclare une **clé de scène**. `IslandSceneCatalog` fait
correspondre cette clé à une vue et à un encombrement.

C'est ce qui permet d'ajouter une fonctionnalité **sans modifier la fenêtre**. Une clé inconnue
retombe sur `InfoScene`, donc une activité nouvelle s'affiche immédiatement, même avant que sa vue
dédiée n'existe.

## Teinte d'ambiance

`ActivityTint` permet à une activité d'influencer l'atmosphère. La fonctionnalité média la déduit de
la couleur dominante de la pochette : la teinte est **désaturée** avant d'être appliquée, et
l'atmosphère l'atteint par un ressort — pas par un changement instantané. L'intention est un halo
ambiant discret, jamais un dégradé saturé.

Laissée à `null`, la teinte de référence est conservée.

## Actions

Les contrôles sont **déclarés** par la fonctionnalité, jamais devinés par la vue :

```csharp
public sealed record ActivityAction(string ActionId, string Label, string? IconKey, bool IsPrimary);
```

Un clic produit une `IslandActionRequest`, routée par le registre vers la fonctionnalité qui
revendique l'activité concernée. Une fonctionnalité **arrêtée est ignorée** : une action sur une
fonctionnalité désactivée ne doit rien faire, et surtout pas la réveiller.
