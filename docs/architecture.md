# Architecture

## Principe

NotchFlow suit une architecture en couches où **la dépendance ne remonte jamais**. Le cœur ne connaît
ni WinUI, ni Win32, ni le système de fichiers ; il décrit des états, des activités et des contrats.
Les couches périphériques les réalisent.

```
Windows Event
      ↓
Feature            (NotchFlow.Features)
      ↓
Island Event       (NotchFlow.Core.Events)
      ↓
State Manager      (NotchFlow.Core.State)
      ↓
Island State
      ↓
UI Renderer        (NotchFlow.App)
      ↓
Animation
```

L'interface ne décide de rien. Elle reçoit un état, un encombrement et une scène, et les projette.

## Projets

| Projet | Rôle | Dépendances |
|---|---|---|
| `NotchFlow.Core` | États, activités, bus d'événements, résolveur de ressort, catalogue de scènes, contrats de fonctionnalité. Aucune dépendance Windows. | — |
| `NotchFlow.Platform.Windows` | Interop : fenêtres, moniteurs, DPI, média système, audio, presse-papier, notifications, Bluetooth, luminosité, démarrage automatique. | Core |
| `NotchFlow.Features` | Les fonctionnalités concrètes : média, HUD volume et luminosité, notifications, Bluetooth, focus, minuteur, lanceur, étagère de fichiers, presse-papier. | Core, Platform.Windows |
| `NotchFlow.Infrastructure` | Configuration persistée, journalisation, chargement des greffons. | Core |
| `NotchFlow.App` | Fenêtres, vues de scène, composition, animations, zone de notification, réglages. | Toutes |
| `samples/` | Greffon d'exemple, écrit comme le ferait un tiers : `NotchFlow.Core` pour seule dépendance. | Core |

La direction des flèches est le contrat : `Core` ne peut pas référencer `App`. C'est ce qui rend le
cœur testable sans machine Windows graphique — 75 tests s'exécutent en une fraction de seconde.

## Flux d'un événement

Exemple : Spotify change de piste.

1. `WindowsMediaSessionManager` reçoit le rappel de session média.
2. `MediaFeature` en déduit une `IslandActivity` et la publie dans `IActivityManager`.
3. `ActivityManager` remplace l'activité média précédente — même identifiant.
4. `IslandController` recalcule la présentation : priorité, encombrement cible.
5. Le ressort anime l'encombrement vers la cible ; chaque image appliquée est signalée.
6. `RuntimeDiagnostics` observe cette image ; `IdleFor` repart de zéro.
7. Plus rien ne se produit. Le ressort se stabilise, le minuteur s'arrête, **le processus devient
   silencieux**.

Aucune de ces étapes n'est déclenchée par une scrutation.

## Fenêtres

Deux surfaces distinctes, et cette séparation est structurelle :

- **`IslandWindow`** — la surface interactive. Sa taille vaut *exactement* l'encombrement de
  l'Island : elle ne capture donc jamais un clic hors de son corps. Elle est marquée
  `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, ce qui la retire d'Alt+Tab et empêche qu'elle vole le focus.
- **`AtmosphereWindow`** — la couche décorative. Elle porte le halo et la dissolution qui rayonnent
  au-delà du corps, et elle est marquée `WS_EX_TRANSPARENT` : elle est clic-traversante par
  construction, pas par gestion d'événements.

Une seule fenêtre ne pouvait pas satisfaire les deux exigences : pour laisser passer les clics dans
la marge, il faut une surface distincte.

## Le contenu tiers

Un greffon ne peut pas modifier la fenêtre : il déclare une **clé de scène** parmi celles du
répertoire, et `IslandSceneCatalog.Card` est celle qui lui est destinée. La scène générique rend le
titre, le sous-titre, l'icône et les contrôles déclarés — c'est-à-dire assez pour un domaine
arbitraire, sans qu'une seule ligne du rendu ne le mentionne. Voir
[ADR-010](decisions/ADR-010-scene-tiers.md).

## Où va quoi

Une règle simple pour placer du code neuf :

- **Décision** → `Core`. Quoi présenter, à quelle priorité, pendant combien de temps.
- **Appareil** → `Platform.Windows`. Ce qui parle à Windows.
- **Information** → `Features`. Ce qui observe le système et en fait une activité.
- **Rendu** → `App`. Comment cela apparaît, jamais *ce qui* apparaît.

## Ce que l'architecture interdit

- Une fonctionnalité qui référence une fenêtre : elle publie une activité, elle n'affiche rien.
- Une vue de scène qui décide d'une priorité : elle reçoit une activité déjà arbitrée.
- Un `Timer` périodique dans une fonctionnalité. Les entrées sont des rappels système, les sorties
  des échéances bornées.
- Un `switch` sur les fonctionnalités dans la fenêtre. Le registre existe pour cela.

## Voir aussi

- [Machine à états](state-machine.md)
- [Système d'activités](activity-system.md)
- [Système d'animation](animation-system.md)
- [Performance](performance.md)
- [Intégration Windows](windows-integration.md)
- [API de greffons](plugin-api.md)
