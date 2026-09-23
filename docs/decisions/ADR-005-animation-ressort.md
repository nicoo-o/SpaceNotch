# ADR-005 — Animation par ressort, résolue analytiquement

**Statut** : accepté
**Date** : 2026

## Contexte

L'ouverture de l'Island doit produire une sensation organique : ouverture rapide, léger dépassement,
stabilisation. Une interpolation `ease-in-out` entre deux tailles ne la donne pas.

## Décision

L'encombrement de l'Island est un scalaire confié à un **ressort physique** (`stiffness`, `damping`,
`mass`), évalué par la **solution analytique** de l'équation de l'oscillateur harmonique amorti.

## Justification

Trois régimes sont distingués — sous-amorti, critique, sur-amorti — et c'est cette distinction qui
donne la sensation recherchée. Le régime sous-amorti produit le léger dépassement caractéristique.

Le choix de la solution **analytique** plutôt que d'une intégration pas à pas est délibéré :
l'intégration accumule une erreur proportionnelle au nombre d'images, donc l'animation n'aurait pas
la même durée à 60 Hz et à 144 Hz. Ici l'évaluation à l'instant `t` est exacte quelle que soit la
cadence — **la physique est indépendante du matériel**.

Corollaire : les paramètres sont réglables en direct, avec des plages bornées, sans que l'utilisateur
puisse produire une Island qui tremble indéfiniment ou n'arrive jamais.

## Conséquences

- `SpringSolver` et `SpringParameters` vivent dans `Core`, donc sont **testés sans machine
  Windows graphique**. Plusieurs tests couvrent les trois régimes.
- La boucle n'existe que pendant une transition et se termine sur le critère `HasSettled` —
  un critère mathématique, pas une durée arbitraire.
- Si Windows demande la réduction des animations, le contrôleur reçoit `UseSpringAnimations = false`
  et les transitions deviennent des fondus. Le comportement demandé par l'utilisateur prime.

## Alternatives écartées

- **`ease-in-out`** : pas de dépassement, sensation mécanique.
- **Interpolation numérique du ressort** : dépendante de la cadence, durée non reproductible.
- **Animations déclaratives XAML** : WinUI sait animer des propriétés, mais l'animation s'arrêterait
  aux limites du modèle de disposition — la fenêtre elle-même doit changer de taille, ce qui exige
  un pilotage impératif.
