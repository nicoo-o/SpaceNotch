# SpaceNotch 2.0 — Plan directeur de refonte UI/UX et logique

> **Règle n°1.** SpaceNotch est une notch **attachée au bord supérieur de l'écran**, jamais une
> capsule flottante. Grands arrondis organiques en bas, aucune ligne dure, une atmosphère qui se
> dissout sous la surface.

Ce document consolide la conversation de conception (audit du projet, références Moody, Inspora,
Notchy, Boring Notch, Dynamic Island, Fluent), la décision « TopAttached », le système de
mouvement « Hypnotic Activity », l'état d'implémentation, et les améliorations issues d'une
recherche UI/UX complémentaire. Les décisions structurantes sont aussi écrites en ADR :
[ADR-017](../decisions/ADR-017-notch-attachee.md) et [ADR-018](../decisions/ADR-018-mouvement-hypnotique.md).

---

## 0. La décision fondamentale : TopAttached

```text
❌ Capsule flottante                       ✅ SpaceNotch

┌──────────────────────────────┐           ████████╭────────────╮████████
│                              │                   │            │
│        ╭────────────╮        │                   │  CONTENU   │
│        │ SpaceNotch │        │                   │            │
│        ╰────────────╯        │                   ╰────────────╯
│                              │                     ░░░░░░░░░░
└──────────────────────────────┘                   ░░░░░░░░░░░░░░
```

- Le haut de la surface **n'a aucune marge** avec le bord de l'écran. La forme en *sort*.
- Les coins inférieurs sont **très arrondis** (26 compact → 34 ouvert).
- Des **épaules concaves** raccordent le bord de l'écran à la notch : le bord « coule » dans la forme.
- Sous la notch : **dissolution atmosphérique** (flou, teinte, fondu), jamais une ligne.
- Même très large, elle garde son identité de notch — jamais une « grosse fenêtre ».
- L'expansion vient **du haut** : largeur et hauteur se déploient depuis le bord, les coins suivent.
  Jamais un `scale 0 → 1` de popup.

Dans le code, la contrainte est **nommée** et **testée** :

```text
IslandGeometryMode
  TopAttached   ← seul mode existant (un test casse si on en ajoute un)
  Floating      ← n'existe pas, volontairement
```

---

## 1. Vision

SpaceNotch n'est ni une mini-fenêtre, ni une collection de widgets, ni un dashboard, ni une copie
de la Dynamic Island, ni une barre de notifications.

> Une surface contextuelle attachée au sommet de Windows, qui se transforme temporairement selon
> ce qui se passe sur le PC.

```text
Événement → SpaceNotch réagit → information essentielle → interaction éventuelle
          → la surface se contracte → retour au repos
```

**Elle naît du haut de l'écran, vit pendant l'événement, puis disparaît.**

---

## 2. Audit : on garde le moteur, on refait la présentation

Le socle Core / Features / Platform / App est sain et reste en place : `ActivityManager`,
`IslandStateManager`, `EventBus`, priorités, `IslandController`, `IslandWindow`,
`AtmosphereWindow`, `IslandGeometryFactory`, `AtmosphericSurface`, `CompositionMaskBrush`,
`SpringSolver` analytique, DPI par moniteur, multi-écran, média, audio, notifications,
presse-papier, Bluetooth, luminosité, étagère, lanceur, greffons, réglages, diagnostics.

**Pas de réécriture.** Le chantier porte sur : présentation, UI/UX, orchestration du mouvement,
présentation des activités, réglages, système de design.

Le problème principal : le modèle `Activity → Scene` pousse à construire **une carte par
fonctionnalité**. On veut **une seule SpaceNotch** — même matière, même géométrie, même mouvement,
même logique — dont seules les informations changent.

---

## 3. Modèle de présentation

```text
Activity → PresentationModel → PresentationResolver → Presentation
```

Une activité ne dit plus seulement « scène Media ». Elle décrit son information :

```text
Eyebrow     contexte discret au-dessus      « Read app-sidebar.tsx · 219 lines »
Title       l'état ou le sujet              « Thinking », « Good Days »
Subtitle    le détail                       « SZA »
Progress    0..1 ou rien                    0.42
Icon        glyphe du catalogue
MotionState Idle / Attention / Working / Completing / Complete / Error
MotionPreset None / Read / Think / Search / Process / Sync / Drop / Complete / Error
Accent      teinte d'ambiance (information, jamais décoration)
Policy      Persistent / Passive / Temporary / Interrupting
```

### Les quatre présentations

| Présentation | Quand | Forme | Objectif |
|---|---|---|---|
| **Hidden** | aucune activité | lèvre 80 × 18 | presque rien ; cible facile au bord (Fitts) |
| **Compact** | une activité, au repos | 148 × 34 | savoir qu'il se passe quelque chose |
| **Preview** | survol intentionnel | +10–20 %, 2ᵉ ligne depuis le signal | donner envie d'interagir |
| **Expanded** | clic | taille de la scène | vraie interaction |

Toutes utilisent **TopAttached**. Machine d'état mécanique conservée :

```text
HIDDEN ⇄ COMPACT ⇄ PREVIEW → EXPANDING → EXPANDED → COLLAPSING → COMPACT → HIDDEN
```

Et, **indépendante**, la machine de travail :

```text
IDLE → ATTENTION → WORKING → COMPLETING → COMPLETE        (ou ERROR)
```

`Compact + Working` = petite notch habitée de matière vivante.
`Expanded + Working` = la même matière à côté d'une progression.

### Navigation

| Geste | Effet |
|---|---|
| Survol (pose ~220 ms) | Preview |
| Clic | Expanded |
| Échap / clic extérieur | Collapse |
| Molette, swipe horizontal, flèches | activité précédente / suivante |
| Clic droit | lanceur |

Le survol **n'ouvre jamais** : le clic exprime l'intention.

---

## 4. Cohabitation des activités

| Politique | Exemple | Comportement |
|---|---|---|
| Persistent | Spotify | présente tant que la source l'est |
| Passive | téléchargement | présente, discrète |
| Temporary | volume 72 % | recouvre puis rend la main |
| Interrupting | appel entrant | prend la place et ouvre |

À l'arrivée d'une activité, le moteur décide : **Ignore, Queue, Overlay, Interrupt, Replace**.
Règle clé : quand l'utilisateur a ouvert la notch, une arrivée moins urgente **attend la
fermeture** au lieu de remplacer le contenu sous son pointeur. Un parcours demandé par
l'utilisateur (molette, flèches) n'est jamais mis en attente.

Pile : discrète — `Spotify  • •` — jamais un gestionnaire de tâches.

---

## 5. Géométrie

| Élément | Valeur | Réglable |
|---|---|---|
| Bord supérieur | collé, pleine largeur, y = 0 | non (invariant testé) |
| Épaules concaves | 8 DIP | 0–16 |
| Congé compact | 26 DIP | 8–40 |
| Congé ouvert | 34 DIP | 12–48 |
| Courbure | superellipse K = 2 (squircle) | arc / squircle |
| Hauteur compacte | 34 DIP | — |
| Hauteur aperçu | 40–60 DIP | — |
| Hauteur ouverte | déterminée par le contenu (70–260) | — |

- Le rayon est **interpolé continûment selon la hauteur** (lissage smoothstep) : aucun saut de
  courbure pendant le morphing.
- Le congé est borné par ce que la forme porte : `min((largeur − 2·épaule)/2, hauteur − épaule)` —
  pas la moitié de la hauteur, puisque la notch n'a qu'un bord libre.
- **Une seule géométrie** pilote surface, reflet, ombre (sommet plat), atmosphère et zone de clic.

---

## 6. Matière

```text
Surface + Encre + Accent + Atmosphère        (jamais carte ⊂ carte ⊂ carte)
```

| Jeton | Valeur | Usage |
|---|---|---|
| Surface.Core | `#000000` | corps de la notch — se fond dans le bord de l'écran |
| Surface.Glass | `#08090C` à 92 % | modes translucides, verre |
| Surface.Elevated | blanc 6 % | surfaces internes |
| Ink Primary / Secondary / Tertiary | blanc 94 / 62 / 44 % | texte |
| Semantic | Info, Success, Warning, Critical | états |
| Hypnotic.Warm | `#FFB46A` | lumière hypnotique par défaut |

**Aucune bordure visible.** La séparation vient du contraste, de la géométrie, de l'ombre, du flou
et de l'atmosphère — trois couches distinctes : **géométrie**, **surface**, **atmosphère**.

L'accent est une **information** : Spotify → violet discret, téléchargement → bleu, succès → vert,
erreur → rouge. Jamais un dégradé saturé.

---

## 7. Mouvement

> Une animation doit répondre à « pourquoi ça bouge ? ». « Parce que c'est joli » → supprimée.

- `SpringSolver` analytique conservé.
- Préréglages utilisateur : **Calme, Naturel, Dynamique** (+ Personnalisé).
- Catégories : **Quick** 120 ms, **Standard** 220 ms, **Slow** 420 ms, **Morph** 280 ms,
  **Spring** (forme).
- Les transitions de contenu finissent **avant** la forme.
- Réduction des animations : ressorts → fondus de 90–160 ms, matière hypnotique → image fixe.
- **Aucune animation permanente.** Au repos : 0 animation, 0 boucle, 0 scrutation.

### Morphing (phase 4)

Pas `opacity 0 → 1 / scale 0 → 1` à chaque changement : les **mêmes** éléments se déplacent.

```text
Compact.Title → Preview.Title → Expanded.Title     (une trajectoire continue)
```

`MorphAnchor` : `IconAnchor`, `ArtworkAnchor`, `TitleAnchor`, `SubtitleAnchor`, `ProgressAnchor`,
`PrimaryActionAnchor`. La pochette ne disparaît pas : elle grandit.

---

## 8. Hypnotic Activity System

Inspiré de la notch HUD « Hypnotizing UI » d'Inspora (une IA qui lit, réfléchit, construit),
**détaché de l'IA** : un langage visuel universel du travail en cours.

> **Mouvement hypnotique = quelque chose travaille, cherche, absorbe ou se transforme.**
> Jamais « quelque chose vient de se produire ».

Ce qu'on garde de la référence : notch collée en haut, grands coins, contexte discret en haut
(« Read app-sidebar.tsx · 219 lines »), état principal dessous (lumière + « Thinking »), source
lumineuse chaude, halo très doux, contraste noir / lumière, respiration plutôt que spinner.

### Primitives

Respiration · Flux · Attraction · Dispersion · Convergence — combinées dans une source, un halo et
quatre particules. Peu de primitives : l'effet vient du rythme, pas du nombre.

### Préréglages

| Préréglage | Caractère | Période | Usage |
|---|---|---|---|
| Read | lent, régulier, respirant | 3,6 s | lecture, indexation douce |
| Think | organique (Lissajous) | 4,8 s | réflexion, automatisation |
| Search | balayage directionnel | 1,8 s | lanceur, recherche |
| Process | dense, énergique | 1,4 s | téléchargement, installation |
| Sync | flux gauche ↔ droite | 2,4 s | OneDrive, Git, Bluetooth, greffons |
| Drop | attraction vers le centre | 1,2 s | glisser un fichier sur la notch |
| Complete | convergence, impulsion, silence | 0,9 s une fois | fin de travail |
| Error | dispersion, micro-secousse | 0,7 s une fois | échec |

L'état décide **s'il y a** mouvement, le préréglage **lequel** :
`Working + None → Process`, `Attention + None → Read`, `Completing → Complete`, `Error → Error`,
`Idle / Complete → rien`.

### Architecture

```text
Activity → ActivityMotionState → HypnoticField (fonction pure du temps, Core, testée)
        → échantillonnage d'une boucle (32 images) → images clés du compositeur (App)
        → Glow / Flow / Convergence  ⇄  AmbientState → respiration de l'atmosphère
```

- Le GPU rejoue seul la boucle : **aucun calcul par image** sur le fil d'interface.
- Au repos : animations arrêtées, visuels masqués, coût nul.
- La matière vit **dans** la notch ; seul son halo se diffuse dans l'atmosphère, jamais hors de
  la forme.
- La dissolution respire **avec** la matière : même fonction, même période.
- Un greffon demande `MotionPreset = Sync` ; il ne dessine jamais sa propre animation.
- Jamais sur : volume, luminosité, lecture/pause, notifications (mouvement court ou morph).

### Scénarios

**Dépôt de fichier** : survol → la notch devient cible (Drop, attraction) → dépôt → absorption
(Complete : convergence, impulsion) → `1 fichier`.
**Téléchargement** : Compact + Process ; au clic, progression et pourcentage à côté de la matière ;
fin → Complete → Compact.
**Recherche** : la frappe dans le lanceur passe en Search, puis les résultats convergent.
**Synchronisation / mise à jour** : Sync, puis Process → Complete.

---

## 9. Refontes par activité

- **Media** (référence absolue) : artwork, titre, artiste, progression, transport, ambiance,
  Compact / Preview / Expanded, morphing.
- **Volume / Luminosité** : icône + valeur deviennent la matière principale ; la valeur défile
  (63, 64, 65), chiffres à chasse fixe.
- **Minuteur** : `◷ 24:37` ; aucune animation chaque seconde — seulement démarrage, pause, reprise,
  fin.
- **Notifications** : arrivée courte puis réduction à `● Discord 1` ; regroupement
  (`Discord 4` → Lucas, Marie, Thomas, Alex).
- **Presse-papier** : une étagère, pas un ListView (clic = coller, glisser = supprimer, épingler).
- **Étagère de fichiers** : fonction signature ; la notch devient cible de dépôt.
- **Lanceur** : surface de commande (recherche, récents, favoris, clavier), pas une grille de
  Start Menu.
- **InfoScene → Generic Activity** : Icon, Eyebrow, Title, Subtitle, Progress, Status, Actions,
  Accent — de quoi afficher un greffon sans nouvelle UI.

---

## 10. Réglages et menu

Réglages : **Apparence · Comportement · Activités · Mouvement · Écrans · Avancé**. Les détails
techniques (ressort brut, composition, diagnostics, fichiers) vivent dans Avancé ou derrière le
« réglage fin ». L'aperçu utilise **la vraie géométrie** de la notch.

Menu de la zone de notification : **Déployer · Lancer ▸ · Activités ▸ · Mouvement ▸ · Apparence ▸ ·
Réglages… · Quitter**. Chaque libellé dit ce que le clic va faire.

---

## 11. Architecture finale

```text
Windows → Feature → Event/Activity → ActivityManager → StateManager
       → PresentationResolver (Hidden/Compact/Preview/Expanded) + ActivityMotionState
       → Morph Engine ── Geometry ── Content ── Motion (Standard | Hypnotic) ── Atmosphere
       → Composition → IslandWindow (interaction) + AtmosphereWindow (décor, clic-traversante)
```

---

## 12. État d'implémentation

| Phase | Contenu | État |
|---|---|---|
| 1. Fondation | jetons Glass/Elevated/Hypnotic, typo Body+ et Hero, `MotionPresets` | **partiel** — reste : couleurs en dur des scènes et de la fenêtre de réglages |
| 2. TopAttached | silhouette à épaules, rayon interpolé, bord collé, ombre au sommet plat, zone de contenu depuis les flancs, `TopOffset` neutralisé | **fait** — à valider à l'œil sur Windows (DPI 100–200 %) |
| 3. Présentation | `NotchPresentation`, résolveur, politiques, décisions d'interruption, attente pendant l'ouverture, aperçu +10–20 %, `Eyebrow` | **partiel** — reste : tailles pilotées par le contenu, Overlay dédié |
| 4. Morph | `MorphAnchor`, `ContentTransition` | à faire |
| 5. Hypnotic | `HypnoticField` + `HypnoticSurface`, Drop → Complete, respiration de l'atmosphère, greffon météo en Sync | **fait (moteur)** — reste : Search dans le lanceur, scènes ouvertes, atténuation longue durée |
| 6. Atmosphère 2.0 | `AmbientState`, intensité liée au travail | **partiel** — reste : flou réel sous la notch, reflet |
| 7–12. Activités | Media, HUD, notifications, étagère, presse-papier, lanceur | à faire |
| 13. Réglages | six sections, aperçu à la vraie géométrie, caractère de mouvement | **partiel** — reste : navigation, aperçu animé |
| Menu | menu de la zone de notification réorganisé | **fait** |
| 14–16. Polish, perf, torture test | | à faire |

Vérification : 151 tests du cœur et 23 du greffon d'exemple passent ; le code C# de l'App compile
sans avertissement en Release. Le rendu XAML lui-même n'a pas pu être exécuté hors Windows.

---

## 13. Améliorations issues de la recherche UI/UX

1. **Intention de survol.** NN/g place l'intention entre 0,3 et 0,5 s avant de révéler un contenu
   caché. L'aperçu ne révélant presque rien, une pose de **220 ms** suffit — *implémenté*. Le
   pointeur qui longe le bord pour atteindre un onglet ne fait plus bouger la notch.
2. **Ressorts spatiaux ≠ effets.** Material 3 Expressive sépare les ressorts *spatiaux* (position,
   taille, forme — peuvent dépasser) des *effets* (opacité, couleur, flou — jamais de
   dépassement). Règle ajoutée : seule la géométrie rebondit.
3. **WCAG 2.2.2 (Pause, Stop, Hide).** Un mouvement automatique de plus de 5 s affiché en parallèle
   d'autres contenus doit pouvoir être arrêté : le réglage « Mouvement hypnotique » le permet
   (image fixe). Proposition : **atténuation automatique** — après ~20 s en Compact, la boucle
   ralentit vers une respiration Read, puis se fige ; elle reprend au survol. Et WCAG 2.3.1 :
   jamais plus de 3 flashs/s (Process bat à 1,4 Hz, Complete ne pulse qu'une fois).
4. **Anatomie Compact de la Dynamic Island.** Apple sépare *leading* (identité, métrique
   principale) et *trailing* (petit état : ✓, pause, 62 %). Proposition : un emplacement trailing
   dans Compact, alimenté par `Progress` ou une valeur courte.
5. **Concurrence.** Apple affiche deux activités avec une bulle *détachée* — contraire à notre
   règle n°1. Le satellite flottant est donc désactivé par défaut ; alternatives à trancher :
   points dans la notch (actuel) ou « notch partagée » dont les deux lobes restent attachés.
6. **Expanded n'est pas un mini-dashboard** (Apple) : peu d'actions, à haute confiance. Et l'île
   est plus immersive sans fond ni imagerie ajoutés — cohérent avec « une seule matière ».
7. **Noir pur et OLED.** Le noir pur provoque du *smearing* au défilement et de la halation avec du
   blanc pur. La notch ne défile pas et doit se fondre dans le bord : **corps en `#000000`**,
   surfaces internes en `#08090C`, encre primaire plafonnée à ~94 %.
8. **Accessibilité Narrator.** Les arrivées doivent être annoncées : région vivante *polite* pour
   Passive/Temporary, *assertive* pour Interrupting (`RaiseNotificationEvent`, WinUI 1.4+).
9. **Durées Fluent** : ~100 ms pour les micro-interactions, jusqu'à ~500 ms pour les mouvements
   complexes, plus long pour les grands éléments — nos catégories (120 / 220 / 420 / ressort) s'y
   alignent.
10. **Loi de Fitts** : le bord supérieur est une cible de hauteur infinie ; une lèvre de 18 DIP y
    suffit, ce qui autorise une veille presque invisible sans perdre la découvrabilité.

Sources : [NN/g — timing](https://www.nngroup.com/articles/timing-exposing-content/),
[Baymard — hover delay](https://baymard.com/blog/dropdown-menu-flickering-issue),
[W3C — Understanding 2.2.2](https://www.w3.org/WAI/WCAG22/Understanding/pause-stop-hide.html),
[Material 3 — motion](https://m3.material.io/styles/motion/overview/how-it-works),
[Fluent 2 — motion](https://fluent2.microsoft.design/motion),
[Microsoft — timing and easing](https://learn.microsoft.com/en-us/windows/apps/design/motion/timing-and-easing),
[Apple HIG — Live Activities](https://developers.apple.com/design/human-interface-guidelines/components/system-experiences/live-activities),
[WWDC23 — Design dynamic Live Activities](https://developer.apple.com/videos/play/wwdc2023/10194/),
[Microsoft — LiveSetting](https://learn.microsoft.com/en-us/uwp/api/windows.ui.xaml.automation.automationproperties.livesetting?view=winrt-26100),
[Boring Notch](https://boringnotch.com/).

---

## 14. Critères de réussite

**UX** : on comprend immédiatement qu'elle est attachée au haut ; elle ne ressemble jamais à une
capsule ; l'expansion vient du haut ; les coins sont beaux et généreux ; aucune ligne dure ;
l'atmosphère se dissout ; les éléments morphent ; le survol reste discret ; le clic exprime
l'intention ; les temporaires n'écrasent pas les persistantes ; aucune animation décorative
permanente ; au repos, elle ne coûte presque rien.

**Technique** : Core indépendant de Windows ; UI indépendante de la logique ; aucune scrutation ;
aucune boucle de rendu au repos ; composition GPU ; DPI et multi-écran corrects ; atmosphère
clic-traversante ; aucun focus volé.

**Checklist de chaque composant** :
□ TopAttached □ coins inférieurs généreux □ aucune bordure □ atmosphère cohérente
□ ombre cohérente □ typographie □ accent discret □ Compact □ Preview □ Expanded □ morph
□ collapse □ réduction des animations □ DPI

**Règle d'or pour une nouvelle fonctionnalité** : ne pas demander « quelle fenêtre créer ? » mais
« quelle information cette activité apporte-t-elle, et comment se présente-t-elle en Compact,
Preview et Expanded — et travaille-t-elle (quel préréglage hypnotique) ? »

---

## 15. Décisions verrouillées

| Décision | Choix |
|---|---|
| Forme | notch attachée au top — capsule flottante interdite |
| Coins | très arrondis, interpolés selon la hauteur |
| Ligne inférieure / bordure | non |
| Atmosphère, flou, dissolution | oui, subtils, pilotés par la même géométrie |
| Surface | sombre (noir pur pour le corps) / verre |
| Survol | aperçu léger après une courte pose |
| Clic | expansion |
| Animation | ressort + morph ; aucune animation permanente |
| Mouvement hypnotique | uniquement pour un travail en cours |
| Media | référence de validation |
| Architecture | pas de réécriture ; les scènes évoluent vers la présentation |
| Réglages | refonte complète |
| Étagère | fonction signature ; presse-papier = étagère ; lanceur = surface de commande |
| Notifications | groupées, contextuelles |
| Performance | silence au repos |

## 16. Questions ouvertes

1. Vidéo de la référence Inspora (la page n'est pas accessible depuis l'environnement de travail) :
   indispensable pour caler périodes, amplitudes et transitions Reading → Thinking → Done.
2. Renommer le code `SpaceNotch` en `SpaceNotch` (espaces de noms, exécutable, dossier de
   configuration à migrer) ?
3. Corps en noir pur `#000000` (fusion avec le bord, recommandé) ou `#08090C` partout ?
4. Épaules concaves : conformes à la capture de référence, ou coins supérieurs à angle droit ?
5. Veille : lèvre visible de 80 × 18, ou notch totalement invisible (zone de survol seule) ?
6. Deux activités simultanées : points dans la notch, notch partagée attachée, ou satellite ?
7. Atténuation automatique du mouvement hypnotique après ~20 s : d'accord ?
8. Téléchargements : quelle source suivre (navigateurs, dossier Téléchargements, Steam…) ?
