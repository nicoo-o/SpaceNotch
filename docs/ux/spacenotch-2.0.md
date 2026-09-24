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
| **Hidden** | aucune activité | lèvre 80 × 18, masquée en plein écran | presque rien ; cible facile au bord (Fitts) |
| **Compact** | une activité, au repos | 36 de haut, largeur ajustée au texte (120–320) | savoir qu'il se passe quelque chose |
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
| Épaules concaves | 12 DIP (mesurées sur la vidéo : ~⅔ du congé) | 0–20 |
| Congé compact | 26 DIP | 8–40 |
| Congé ouvert | 34 DIP | 12–48 |
| Courbure | superellipse K = 2 (squircle) | arc / squircle |
| Hauteur compacte | 36 DIP (hauteur de la Dynamic Island) | — |
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

### Ce que montre la vidéo de référence

Analyse image par image (34 s, 30 i/s) :

- **Une grille de 3 × 3 pixels carrés**, presque jointifs, avec un halo doux (« bloom ») — pas une
  orbe floue. Environ 18 DIP de côté, à gauche du titre.
- **Des motifs qui se succèdent** toutes les 150 à 250 ms, avec un fondu court : croix, anneau,
  losange, plein, coins, plus, colonnes, « L ».
- **Une couleur qui dérive selon l'état** : bleu / cyan pour « Reading file » et « Responding »,
  orange → corail pour « Thinking », pêche → rose → bleu → lavande pour « Creating prototype ».
- **Deux niveaux de texte** : contexte discret (« Read sidebar.tsx » + « 741 lines » un ton plus
  clair) au-dessus de l'état en blanc.
- **La largeur de la notch suit le texte**, et les états se remplacent par un fondu sur place.
- **Épaules concaves** franches au raccord du bord supérieur, congés bas ≈ 1,5 × l'épaule.
- **Une pluie binaire « 0 1 »** très pâle tombe sous la notch pendant « Creating prototype ».

### Préréglages (grille 3 × 3)

| Préréglage | Motifs | Palette | Pas | Usage |
|---|---|---|---|---|
| Read | curseur qui parcourt la grille avec une traîne (marches) | bleu ↔ cyan | 150 ms | lecture, indexation |
| Think | serpent de 3 pixels autour de l'anneau (les « L ») | orange → corail → rose | 160 ms | réflexion, automatisation |
| Search | colonne qui balaie de gauche à droite | cyan ↔ bleu | 180 ms | lanceur (lecture du menu Démarrer) |
| Process | croix, anneau, losange, plein, coins, plus | pêche → rose → bleu → lavande | 220 ms | téléchargement, installation |
| Sync | colonne qui va et vient | bleu ↔ orange | 200 ms | greffons, synchronisation |
| Drop | anneau → losange → centre → extinction | pêche ↔ orange | 180 ms | fichier glissé sur la notch |
| Complete | plein → plus → un seul pixel apaisé | menthe | une fois, 0,95 s | fin de travail |
| Error | croix qui clignote deux fois, secousse, reste pâle | rouge | une fois, 0,8 s | échec |

L'état décide **s'il y a** mouvement, le préréglage **lequel** :
`Working + None → Process`, `Attention + None → Read`, `Completing → Complete`, `Error → Error`,
`Idle / Complete → rien`. La couleur appartient au préréglage (choix validé) : elle dit ce qui se
passe.

### Architecture

```text
Activity → ActivityMotionState → HypnoticField (fonction linéaire par morceaux, Core, testée)
        → images clés EXACTES (points de rupture) → compositeur (App) : 9 pixels + halo LayerVisual
        → AmbientState → l'atmosphère respire ET dérive dans la couleur de la grille
```

- Le GPU rejoue seul la boucle : **aucun calcul par image** sur le fil d'interface. Les images
  clés sont les points de rupture de la fonction : le rendu est identique au modèle, sans
  approximation.
- Au repos : animations arrêtées, visuels masqués, coût nul.
- **Apaisement** : en Compact, une boucle se fige après 20 s (WCAG 2.2.2) et reprend au survol.
- Réduction des animations ou réglage désactivé : un motif fixe, dans la couleur du préréglage.
- La pluie binaire est une **option**, désactivée par défaut (Réglages › Mouvement).
- Un greffon demande `MotionPreset = Sync` ; il ne dessine jamais sa propre animation.
- Jamais sur : volume, luminosité, lecture/pause, notifications.

### Scénarios

**Dépôt de fichier** : survol → la notch devient cible (Drop) → dépôt → absorption (Complete) →
l'étagère.
**Téléchargement** : Compact + Process, taille reçue à droite ; fin → Complete → « Ouvrir ».
**Lanceur** : Search pendant la lecture réelle du menu Démarrer, puis la liste.
**Greffon météo** : Sync pendant chaque relevé.
**Démonstration** (`--demo`) : la vidéo rejouée — Thinking, Reading file, Creating prototype, fin.

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
| 1. Fondation | jetons Glass / Elevated / Hypnotic / ArtworkSmall, styles Body+, Hero, Metric ; couleurs en dur retirées de toutes les scènes | **fait** — seule la fenêtre de réglages garde son fond sombre propre |
| 2. TopAttached | silhouette à épaules (12), rayon interpolé, bord collé, ombre au sommet plat, contenu mesuré depuis les flancs, `TopOffset` neutralisé | **fait** |
| 3. Présentation | Hidden / Compact / Preview / Expanded, politiques, interruptions, attente pendant l'ouverture, aperçu +10–20 %, largeur ajustée au texte, hauteur ouverte ajustée au contenu | **fait** |
| 4. Morph | `MorphTransform` (FLIP), pochette et titre qui grandissent depuis la forme compacte, transitions de contenu sur place, entrée des scènes | **fait** |
| 5. Hypnotic | grille 3 × 3 d'après la vidéo, 8 préréglages, images clés exactes, apaisement, pluie binaire optionnelle | **fait** |
| 6. Atmosphère 2.0 | `AmbientState`, respiration et dérive de couleur synchronisées avec la grille | **fait** — le flou réel sous la notch reste le mode « Flouté » existant |
| 7. Media | pochette compacte, morphing vers la scène, jetons | **fait** |
| 8. HUD | recouvrement compact (glyphe, fil de niveau, valeur), valeur qui défile dans la scène ouverte ; corrige la charge utile du volume | **fait** |
| 9. Notifications | groupes par application (« Discord 4 »), historique dans la scène ouverte | **fait** |
| 10. Étagère | rangée horizontale, reprise des fichiers par glisser-déposer vers l'extérieur | **fait** |
| 11. Presse-papier | jetons, noms Narrateur, balayer une entrée vers la gauche pour la supprimer (seuil 45 % ou élan 650 DIPs/s) | **fait** |
| 12. Lanceur | surface de commande : champ, liste au clavier, Entrée lance | **fait** |
| 13. Réglages | six sections navigables, aperçu à la vraie géométrie et à la vraie grille, thème sombre imposé | **fait** |
| Menu | Déployer · Lancer (dont Démonstration) · Activités · Mouvement · Apparence · Réglages | **fait** |
| 14. Motion polish | seule la géométrie rebondit ; effets sans dépassement ; réduction des animations partout ; clic extérieur referme | **fait**, à affiner à l'œil sur Windows |
| 15. Performance | aucune boucle au repos : grille et atmosphère rejouées par le compositeur, rouleau de valeur limité à ~180 ms, un minuteur à usage unique par échéance | **à mesurer** sur Windows (voir performance.md) |
| 16. Torture test | scénario rejoué en test (aucune ouverture forcée, musique préservée, pile lisible) et en vrai (`--demo`) | **fait** |
| Nom | NotchFlow → SpaceNotch, reprise des préférences et greffons | **fait** |
| Téléchargements | dossier Téléchargements (Chromium, Firefox, Opera, Safari), Ouvrir / Afficher | **fait** |
| Accessibilité | annonces Narrateur (polie ; assertive pour un appel), noms des boutons | **fait** |
| Détachement | tirage avec résistance, arrachement en goutte, pastille qui suit la main (ressort + étirement), lancer vers les aimants, raccrochage par la goutte inversée, ouverture vers l'espace libre (ADR-019) | **fait**, à juger à l'œil sur Windows |
| Bulle | mini-notch accrochée pour téléchargement, appel, enregistrement, priorité critique ; échange au toucher ; suit la notch détachée | **fait** |
| Micro et caméra | appels et enregistrements d'après l'indicateur de confidentialité de Windows | **fait** |
| Survol prolongé | option : une seconde de survol ouvre la notch (désactivée par défaut) | **fait** |

Vérification : 189 tests du cœur et 23 du greffon d'exemple passent ; le code C# de l'App compile
sans avertissement en Release. Le rendu XAML n'a pas pu être exécuté hors Windows : à juger avec
`SpaceNotch.App.exe --demo`.

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
   (image fixe), et l'**apaisement automatique** est *implémenté* : après 20 s en Compact, la
   boucle se fige ; elle reprend au survol. Et WCAG 2.3.1 : jamais plus de 3 flashs/s — la grille
   change de motif par fondus, sans flash plein écran, et Complete ne s'allume qu'une fois.
4. **Anatomie Compact de la Dynamic Island.** Apple sépare *leading* (identité, métrique
   principale) et *trailing* (petit état : ✓, pause, 62 %). *Implémenté* : `Metric` /
   `TrailingMetric`, et un fil de niveau pour les retours système.
5. **Concurrence.** Apple affiche deux activités avec une bulle *détachée* — contraire à notre
   règle n°1. *Décidé* : une seule notch ; le satellite est supprimé, la pile se signale par des
   points dans la notch.
6. **Expanded n'est pas un mini-dashboard** (Apple) : peu d'actions, à haute confiance. Et l'île
   est plus immersive sans fond ni imagerie ajoutés — cohérent avec « une seule matière ».
7. **Noir pur et OLED.** Le noir pur provoque du *smearing* au défilement et de la halation avec du
   blanc pur. La notch ne défile pas et doit se fondre dans le bord : **corps en `#000000`**,
   surfaces internes en `#08090C`, encre primaire plafonnée à 92 %, aucun reflet au bord.
8. **Accessibilité Narrator.** *Implémenté.* Les arrivées doivent être annoncées : région vivante *polite* pour
   Passive/Temporary, *assertive* pour Interrupting (`RaiseNotificationEvent`, WinUI 1.4+).
9. **Durées Fluent** : ~100 ms pour les micro-interactions, jusqu'à ~500 ms pour les mouvements
   complexes, plus long pour les grands éléments — nos catégories (120 / 220 / 420 / ressort) s'y
   alignent.
10. **Loi de Fitts** : le bord supérieur est une cible de hauteur infinie ; une lèvre de 18 DIP y
    suffit, ce qui autorise une veille presque invisible sans perdre la découvrabilité.

11. **Téléchargements sans extension de navigateur.** Chromium (Chrome, Edge, Brave, Vivaldi)
    écrit un `.crdownload`, Firefox un `.part`, Opera un `.opdownload`, Safari un `.download`,
    puis chacun renomme le fichier à la fin : observer le dossier Téléchargements couvre tous les
    navigateurs — *implémenté*.

Sources : [fileinfo — CRDOWNLOAD](https://fileinfo.com/extension/crdownload),
[NN/g — timing](https://www.nngroup.com/articles/timing-exposing-content/),
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
| Forme | notch attachée au top au repos et au démarrage — flottante seulement si l'utilisateur l'arrache (ADR-019) |
| Nombre de notches | **une seule** — la pile se signale dans la notch ; une bulle accrochée seulement pour une activité importante |
| Détachement | tirer vers le bas ; goutte qui s'étire ; ressort + étirement ≤ 5 % ; lancer + aimants ; toujours raccrochée au redémarrage |
| Presse-papier | balayer vers la gauche pour supprimer ; boutons conservés |
| Survol prolongé | option d'une seconde pour ouvrir, désactivée par défaut |
| Coins | très arrondis, interpolés selon la hauteur ; épaules concaves au bord de l'écran |
| Ligne inférieure / bordure / reflet au bord | non |
| Surface | **noir OLED pur** pour le corps, encre plafonnée à 92 % |
| Atmosphère, flou, dissolution | oui, subtils, pilotés par la même géométrie |
| Veille | lèvre visible ; masquée quand une application est en plein écran |
| Survol | aperçu léger après une pose de 220 ms |
| Clic | expansion ; clic extérieur ou Échap referment |
| Animation | ressort + morph ; aucune animation permanente |
| Mouvement hypnotique | grille 3 × 3, couleurs de la référence, uniquement pour un travail en cours |
| Pluie binaire | option, désactivée par défaut |
| Téléchargements | dossier Téléchargements, tous navigateurs, sans extension |
| Retours système | recouvrement compact, jamais une ouverture forcée |
| Notifications | groupées par application |
| Architecture | pas de réécriture ; les scènes évoluent vers la présentation |
| Performance | silence au repos |

## 16. Questions tranchées et questions ouvertes

Tranchées :

1. Vidéo de référence : reçue et analysée (§8).
2. Nom : le code s'appelle désormais SpaceNotch.
3. Couleur : noir OLED pur pour le corps.
4. Épaules arrondies au raccord de l'écran : oui.
5. Veille : visible, masquée en plein écran.
6. Une seule notch.
7. Téléchargements : le dossier Téléchargements, qui couvre tous les navigateurs.
8. Couleurs de la grille : celles de la référence, par préréglage.
9. Pluie binaire : option désactivée par défaut.

10. Presse-papier : balayer pour supprimer (les boutons restent pour le clavier et Narrateur).
11. Survol prolongé : option d'une seconde, désactivée par défaut.
12. Deux activités importantes : la notch se partage avec une petite bulle accrochée au bord,
    seulement pour téléchargement, appel, enregistrement et priorité critique ; toucher la
    bulle échange les rôles.
13. Détachement : goutte qui s'étire, ressort + étirement léger, tirage avec résistance,
    lancer + aimants, ouverture vers l'espace libre, toujours accrochée au redémarrage, la
    bulle suit la notch détachée (ADR-019).

Ouvertes :

- Aucune question bloquante. À juger sur Windows : la vitesse du ressort de suivi, la durée
  de la goutte (340 ms) et le seuil d'arrachement (40 DIPs).
