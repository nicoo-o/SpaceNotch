# ADR-018 — Mouvement hypnotique : un langage du travail en cours, rejoué par le compositeur

**Statut** : Accepté

## Contexte

La référence « Hypnotizing UI » d'Inspora anime une notch HUD pendant qu'une IA lit, réfléchit et
construit. L'analyse image par image de la vidéo montre une **grille de 3 × 3 pixels lumineux**
avec un halo doux, dont le motif change toutes les 150 à 250 ms par un fondu court, et dont la
couleur dérive selon l'état (bleu pour lire, orange puis corail pour réfléchir, pêche → rose →
bleu → lavande pour construire), sous un contexte discret au-dessus de l'état.

On veut en faire un langage d'état réutilisable par toutes les activités — et par les greffons — sans trahir deux principes du projet : aucune animation décorative, aucune boucle de
rendu au repos (ADR-004, ADR-005).

## Décision

1. **Deux dimensions d'état indépendantes** : la présentation (Hidden, Compact, Preview,
   Expanded) et le travail (`ActivityMotionState` : Idle, Attention, Working, Completing, Complete,
   Error).
2. **Des préréglages, pas des animations** : `HypnoticPreset` (Read, Think, Search, Process, Sync,
   Drop, Complete, Error). Une activité déclare un état et, éventuellement, un préréglage ;
   `HypnoticField.Resolve` décide. Un greffon ne dessine jamais sa propre animation.
3. **Le champ est une fonction pure du temps dans le cœur** (`HypnoticField`) : neuf intensités de
   pixels, une couleur, un halo, une secousse, une impulsion d'atmosphère — toutes **linéaires par
   morceaux**. Les boucles sont exactement périodiques.
4. **Le rendu reçoit les images clés exactes** — les points de rupture de la fonction — et le
   compositeur les rejoue seul (`HypnoticSurface` : neuf `SpriteVisual` partageant un pinceau
   animé, halo par l'ombre d'un `LayerVisual`). Le rendu est identique au modèle, sans
   approximation, et aucun calcul n'a lieu par image sur le fil d'interface.
5. **La couleur appartient au préréglage**, comme dans la référence : elle dit ce qui se passe.
6. **L'atmosphère respire avec la matière** : même fonction, même période, sur un conteneur de
   composition dédié qui ne dispute pas l'opacité de base de la dissolution.
7. **Réduction des animations, réglage utilisateur et apaisement** : image fixe représentative de la même
   composition. L'information reste, le mouvement part. En compact, une boucle se fige après 20 s
   et reprend au survol (WCAG 2.2.2).
8. **Usage réservé** au travail en cours ; jamais sur le volume, la luminosité, lecture/pause.

## Justification

- La fonction pure se teste sans interface : bornes, périodicité, convergence de Complete,
  dispersion d'Error, attraction de Drop, sens du balayage de Search.
- Les animations par images clés du compositeur s'exécutent hors du fil d'interface : le coût CPU
  pendant le mouvement est quasi nul, et nul au repos (animations arrêtées, visuels masqués).
- Un langage commun évite qu'un écosystème de greffons produise dix spinners différents.

## Conséquences

- Les préréglages en boucle doivent rester exactement périodiques : un test l'impose.
- Un préréglage ponctuel signale sa fin par un lot de composition ; l'appelant garde un filet de
  sécurité temporel.
- Une longue boucle affichée en parallèle d'autres contenus relève de WCAG 2.2.2 : le réglage
  permet de l'arrêter ; une atténuation automatique est proposée (voir le plan SpaceNotch 2.0).
- Les cadences sont calées sur la vidéo d'origine (150 à 250 ms par motif) ; un test les borne.

## Alternatives écartées

- **Un GIF ou une vidéo dans la notch** : ne se teinte pas, ne se synchronise pas avec
  l'atmosphère, coûte un décodage permanent.
- **Un vrai système de particules évalué à chaque image** : un rendu permanent pour un gain
  invisible à 14–28 DIP.
- **Une orbe et des particules flottantes** (première version) : la vidéo montre une grille de
  pixels ; l'orbe se lisait comme un voyant, pas comme la référence.
- **`CompositionTarget.Rendering`** comme pour la teinte d'atmosphère : acceptable pour une
  transition d'une demi-seconde, pas pour une boucle qui peut durer plusieurs minutes.
