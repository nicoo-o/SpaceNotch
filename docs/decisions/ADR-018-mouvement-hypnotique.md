# ADR-018 — Mouvement hypnotique : un langage du travail en cours, rejoué par le compositeur

**Statut** : Accepté

## Contexte

La référence « Hypnotizing UI » d'Inspora anime une notch HUD pendant qu'une IA lit, réfléchit et
construit : une source lumineuse chaude, un halo très doux, un contexte discret au-dessus de
l'état. On veut en faire un langage d'état réutilisable par toutes les activités — et par les
greffons — sans trahir deux principes du projet : aucune animation décorative, aucune boucle de
rendu au repos (ADR-004, ADR-005).

## Décision

1. **Deux dimensions d'état indépendantes** : la présentation (Hidden, Compact, Preview,
   Expanded) et le travail (`ActivityMotionState` : Idle, Attention, Working, Completing, Complete,
   Error).
2. **Des préréglages, pas des animations** : `HypnoticPreset` (Read, Think, Search, Process, Sync,
   Drop, Complete, Error). Une activité déclare un état et, éventuellement, un préréglage ;
   `HypnoticField.Resolve` décide. Un greffon ne dessine jamais sa propre animation.
3. **Le champ est une fonction pure du temps dans le cœur** (`HypnoticField`) : une source, un
   halo, quatre particules, une impulsion d'atmosphère. Les boucles sont exactement périodiques.
4. **Le rendu cuit une boucle en images clés** (`HypnoticSurface`, 32 échantillons, interpolation
   linéaire) et le compositeur la rejoue seul. Aucun calcul par image sur le fil d'interface.
5. **L'atmosphère respire avec la matière** : même fonction, même période, sur un conteneur de
   composition dédié qui ne dispute pas l'opacité de base de la dissolution.
6. **Réduction des animations et réglage utilisateur** : image fixe représentative de la même
   composition. L'information reste, le mouvement part.
7. **Usage réservé** au travail en cours ; jamais sur le volume, la luminosité, lecture/pause.

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
- Les timings exacts de la référence restent à caler sur la vidéo d'origine.

## Alternatives écartées

- **Un GIF ou une vidéo dans la notch** : ne se teinte pas, ne se synchronise pas avec
  l'atmosphère, coûte un décodage permanent.
- **Un vrai système de particules évalué à chaque image** : un rendu permanent pour un gain
  invisible à 14–28 DIP.
- **`CompositionTarget.Rendering`** comme pour la teinte d'atmosphère : acceptable pour une
  transition d'une demi-seconde, pas pour une boucle qui peut durer plusieurs minutes.
