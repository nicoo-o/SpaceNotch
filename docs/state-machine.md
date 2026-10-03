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

Une vue de scène ne peut pas demander une transition : elle décrit ce qu'elle contient, et le
contrôleur en déduit l'encombrement. L'état n'est pas encore la seule entrée du rendu : la fenêtre
garde des drapeaux à elle (appui, traction, visite, retrait). C'est ce que la machine ci-dessous
doit résorber (audit d'octobre 2026, RFC §3).

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


## La machine à états de la notch (phase E de l'audit)

`SpaceNotch.Core.Machine.NotchMachine` est la machine pure de la RFC (§3.2). Elle vit en Core et se
teste sans fenêtre. Elle a quatre régions orthogonales :

- **Présence** : visible, retirée (plein écran, session verrouillée) ou arrêtée ;
- **Placement et main** : accrochée, languette ou flottante ; libre, pressée, traction, arrachement
  ou déplacement ;
- **Surface** : repos, aperçu ou ouverte (« en mouvement » est l'animateur, pas un état) ;
- **Clavier** : libre ou capturé.

Des gardes les relient :

- retirée, la surface est figée et le clavier rendu ;
- main occupée, pas d'aperçu ;
- flottante, pas de traction ;
- ouverte par l'utilisateur, le clavier est capturé.

Chaque entrée (`NotchInput`) rend l'état suivant et une liste d'effets (`NotchEffect`), que l'hôte
interprète.

Elle tourne **en ombre** (`NotchShadow`, `IslandWindow.Shadow.cs`) :

- la fenêtre lui donne les mêmes entrées que la notch réelle : survol posé, sortie, appui,
  traction, lâcher, clic droit, Échap, clic ailleurs, raccourci, arrivées, retrait, placement ;
- elle compare sa surface à l'état réel chaque fois que celui-ci se pose ;
- elle journalise les écarts (`[OMBRE]`, 40 au plus) et un résumé à l'arrêt ;
- elle ne pilote rien.

La bascule se fera région par région, quand les journaux réels ne montreront plus d'écart ; chacune
sera vérifiée sur une visite filmée. Il n'y a jamais deux sources de vérité qui pilotent.

`IslandController` est en Core lui aussi (`SpaceNotch.Core.State`), derrière `IShapeAnimator` :
l'application lui fournit le ressort branché sur l'horloge d'images, et les tests un faux qui se
pose à la demande.
