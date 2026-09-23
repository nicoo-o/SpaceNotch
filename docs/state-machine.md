# Machine à états

## Les cinq états

```
CLOSED ──hover──► PREVIEW ──click──► EXPANDING ──► EXPANDED
   ▲                                                    │
   │                                                    │
   └────────────── COLLAPSING ◄──── click / Échap ──────┘
```

| État | Signification | Encombrement |
|---|---|---|
| `Closed` | Au repos, pilule discrète au sommet de l'écran. | Minimal |
| `Preview` | Survolée : expansion d'anticipation invitant à l'interaction. | Intermédiaire |
| `Expanding` | Transition en cours, animée par ressort. | Variable |
| `Expanded` | Ouverte : contenu riche, contrôles, dissolution atmosphérique. | Déclaré par la scène |
| `Collapsing` | Fermeture en cours. | Variable |

L'état est la **seule** entrée du rendu. Une vue de scène ne peut pas demander une transition : elle
décrit ce qu'elle contient, et le contrôleur en déduit l'encombrement.

## États d'activité

`IslandState` décrit la géométrie. `IslandActivityState` décrit la nature de l'information
présentée — les deux sont orthogonaux :

`MEDIA_ACTIVE`, `CALL_ACTIVE`, `DOWNLOAD_ACTIVE`, `FILE_DRAG`, `NOTIFICATION`, `SYSTEM_HUD`, `Idle`.

Une activité de type `SYSTEM_HUD` peut être présentée alors que l'Island est en `Preview` : la
géométrie et la sémantique ne se confondent pas.

## Qui pilote

`IslandStateManager` détient l'état courant et notifie les abonnés. `IslandController` traduit
« état + activité présentée » en un **encombrement cible**, et confie ce scalaire au ressort.

Ni la fenêtre ni les vues ne modifient l'état directement. Les seules entrées sont :

- la souris (survol, clic) ;
- le clavier (Échap réduit, flèches parcourent la pile) ;
- la molette (cycle dans la pile d'activités) ;
- l'arrivée et l'expiration d'activités ;
- les changements d'environnement (moniteur, DPI).

## Priorité et arbitrage

Quand plusieurs activités existent, l'Island doit choisir. Le tri est explicite :

```
CRITICAL  >  HIGH  >  NORMAL  >  BACKGROUND
```

À priorité égale, la plus récente l'emporte. `ActivityManager.CyclePresentation` permet à
l'utilisateur de forcer la main sans rompre l'arbitrage : la présentation manuelle est temporaire et
retombe sur la décision automatique dès que l'activité forcée disparaît.

## Réduction des animations

`SystemVisualState` lit les préférences système. Si Windows demande la réduction des animations, ou
si le contraste élevé est actif, `UseSpringAnimations` devient faux et la transition devient un
simple fondu. Ce n'est pas un repli dégradé : c'est le comportement demandé par l'utilisateur.

## Invariants

- Aucune transition ne peut être laissée en suspens : un état « en cours » (`Expanding`,
  `Collapsing`) se résout toujours en un état stable par l'échéance du ressort.
- `Collapsing` retourne vers `Closed`, jamais vers `Preview` — sinon un simple clic laisserait
  l'Island dans un état intermédiaire permanent.
- Une activité expirée pendant `Expanded` provoque la fermeture, pas un état vide affiché.
