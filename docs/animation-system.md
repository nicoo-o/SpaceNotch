# Système d'animation

## Principe : pas de redimensionnement linéaire

L'Island ne passe jamais d'une taille à une autre instantanément, ni par une interpolation
`ease-in-out`. Elle **morphe** : l'encombrement est un scalaire confié à un ressort physique, et
chaque image est la solution analytique exacte de l'équation de l'oscillateur.

## Le résolveur

`SpringSolver` résout analytiquement l'oscillateur harmonique amorti :

```
m·x″ + c·x′ + k·x = 0
ω₀ = √(k/m)          pulsation propre
ζ  = c / (2·√(m·k))  rapport d'amortissement
```

Trois régimes sont distingués, et c'est cette distinction qui donne la sensation recherchée :

| Régime | Condition | Aspect |
|---|---|---|
| Sous-amorti | `ζ < 1` | Léger dépassement puis stabilisation — le « rebond » de la Dynamic Island |
| Critique | `ζ = 1` | Retour le plus rapide sans oscillation |
| Sur-amorti | `ζ > 1` | Retour lent et sans dépassement |

Pourquoi une solution analytique et non une intégration pas à pas ? Parce qu'une intégration
numérique accumule une erreur proportionnelle au nombre d'images : à 144 Hz, l'animation n'aurait pas
la même durée qu'à 60 Hz. Ici, l'évaluation à l'instant `t` est exacte quelle que soit la cadence —
la physique est indépendante du matériel.

## Paramètres

| Paramètre | Défaut | Effet | Plage |
|---|---|---|---|
| `SpringStiffness` | 220 | Raideur : plus élevée = ouverture plus vive | 40–600 |
| `SpringDamping` | 22 | Amortissement : plus faible = rebond plus marqué | 4–80 |
| `SpringMass` | 1.0 | Inertie | 0.2–4 |

Les trois sont réglables en direct depuis la fenêtre de réglages. La plage est bornée par
`AppSettings.Sanitize` : une valeur aberrante — ou un fichier de configuration édité à la main — ne
peut pas produire une Island qui tremble indéfiniment ou n'arrive jamais.

## Boucle d'animation

Le ressort est évalué sur un fil dédié, à **60 images par seconde**, et **uniquement pendant une
transition**. La boucle se termine par une auto-déclaration de repos :

```
HasSettled(t, départ, cible)
  ⇔ |valeur − cible| < seuil  ET  |vitesse| < seuil
```

Ce critère est ce qui rend le repos mesurable : il n'y a pas de « continue à tourner pour voir », il
y a une condition d'arrêt mathématique. Une fois satisfaite, le ressort cesse d'être évalué.

`RuntimeDiagnostics.RenderedFrames` compte les images réellement appliquées, et `IdleFor` mesure le
temps écoulé depuis la dernière. Sur une Island inutilisée, `IdleFor` croît indéfiniment — c'est la
définition opérationnelle du repos.

## Géométrie

`IslandController.ApplyGeometry` est le **seul** point qui écrit une position ou une taille. Les deux
fenêtres — interactive et décorative — sont positionnées par le même appel, ce qui rend impossible
qu'elles divergent d'un pixel.

L'encombrement provient du `IslandSceneCatalog`, jamais d'une constante dans la fenêtre. Les
dimensions codées en dur (300×70, 360×80, 260×75…) ont été supprimées pour cette raison.

## DPI

Toutes les valeurs manipulées sont en **DIPs**. La conversion en pixels physiques est faite au
dernier moment, avec l'échelle du **moniteur cible** — pas celle du moniteur principal. Sur une
configuration 100 % + 150 %, l'Island se place correctement sur les deux écrans.

## Masque atmosphérique

Le fondu du bas n'est pas un dégradé XAML : c'est un `CompositionMaskBrush` appliqué par le
compositeur. Le passage à l'alpha zéro est calculé par le GPU et suit la surface à chaque image.

`AtmosphericSurface.Configure(width, height, fadeStart)` déplace un arrêt de dégradé — il ne
reconstruit pas la collection. Recréer la collection à chaque image serait précisément le travail
inutile que le projet s'interdit.

`fadeStart` est la fraction de hauteur encore pleine : à 0,62, la dissolution occupe les 38 %
inférieurs. C'est ce paramètre qui distingue une pilule au repos — fondu court, presque immédiat —
d'une Island ouverte, dont le bas doit se perdre beaucoup plus loin.

## Réduction des animations

Si `SystemVisualState` signale que Windows demande la réduction des animations, le contrôleur reçoit
`UseSpringAnimations = false` et les transitions deviennent des fondus. Le comportement demandé par
l'utilisateur prime sur l'esthétique du projet.
