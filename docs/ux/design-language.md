# Langage visuel

## L'intention

L'Island ne doit pas ressembler à une fenêtre posée sur le bureau. Elle doit sembler **intégrée à
l'écran** — une surface de travail qui émerge au sommet, plutôt qu'un panneau flottant. C'est la
contrainte esthétique centrale du projet, et elle n'est pas négociable.

## La contrainte sans bord

> Lorsque l'Island est ouverte, il ne doit exister **aucune délimitation marquée** sur la partie
> inférieure, ni sur les côtés.

Une ligne sous l'Island casserait complètement l'illusion. C'est pourquoi il n'y a **pas** de bord,
pas de trait, pas de séparateur.

À la place, une dissolution atmosphérique :

```
████████████████████
████████████████████
 ░░░░░░░░░░░░░░░░░░
   ░░░░░░░░░░░░░░░
      ░░░░░░░░░░
         ░░░░
```

Le bas se dissout progressivement dans le fond de l'écran.

## Comment la dissolution est calculée

Ce n'est **pas** un dégradé XAML. Un dégradé XAML est calculé par le processeur, et son fondu ne suit
pas la géométrie réelle de la surface — à l'ouverture, il devient approximatif.

La dissolution est un `CompositionMaskBrush` appliqué par le compositeur Windows :

- le fondu est un **masque** appliqué à une teinte unie ;
- le passage à l'alpha zéro est calculé par le **GPU** ;
- il suit la surface à chaque image ;
- **rien n'est recalculé au repos**.

Voir [animation-system.md](../animation-system.md) pour `fadeStart`, le paramètre qui distingue la
pilule au repos — fondu court, presque immédiat — de l'Island ouverte, dont le bas se perd beaucoup
plus loin.

## Palette

| Rôle | Valeur | Note |
|---|---|---|
| Base | `#0B0B0D` | Noir proche, **pas** `#000000` : le noir absolu ne laisse aucune place à la translucidité. |
| Texte | blanc à ~94 % d'opacité | Sur fond clair, encre sombre à la place. |
| Indicateur de pile | blanc à ~44 % | Doit se lire comme un indice, jamais comme un texte. |

La teinte d'ambiance peut être influencée par la couleur dominante d'une pochette d'album, mais
**désaturée** et appliquée par ressort. L'intention est un halo ambiant discret — jamais un dégradé
saturé.

## Coins

Très arrondis, et **configurables en direct** :

| État | Défaut | Plage |
|---|---|---|
| Fermée | 26 px | 4–48 |
| Ouverte | 34 px | 4–56 |

Le cahier des charges recommande 24–32 px fermée et 28–36 px ouverte. Les valeurs par défaut se
situent dans ces fourchettes.

## Typographie

**Segoe UI Variable** — parfaitement intégré à Windows, aucune police embarquée à charger.
Un ajustement : les chiffres qui défilent (minuteur, position de lecture) utilisent des chiffres à
chasse fixe, sinon le texte « danse » à chaque incrément.

## Espacement

L'Island fermée est **fine** — une pilule discrète, presque invisible, portant au plus une icône et
un libellé court. L'ouverture révèle la structure : le contenu n'est jamais comprimé dans la forme
fermée.

Les encombrements sont **déclarés** par le catalogue de scènes, jamais écrits en dur dans une vue.
C'est ce qui garantit qu'ajouter une fonctionnalité n'oblige pas à retoucher la fenêtre.

## Icônes

Un jeu de glyphes, résolu par `.IconKey`. Les émojis sont réservés aux contenus où ils portent du
sens — jamais comme jeu d'icônes d'interface.

## Mouvement

Aucune animation permanente. Aucun visualiseur continu. Le mouvement est une réponse à une
transition, et il cesse.

Les transitions suivent une physique de ressort : ouverture vive, léger dépassement, stabilisation.
Voir [animation-system.md](../animation-system.md).

## Accessibilité

- Le clavier fonctionne : Échap réduit, flèches parcourent la pile. Le focus est possible **pendant**
  l'interaction et retiré ensuite (voir [windows-integration.md](../windows-integration.md)).
- Si Windows demande la réduction des animations, les ressorts deviennent des fondus.
- Le contraste élevé et les effets de transparence sont lus dans `SystemVisualState` ; en mode
  `Auto`, l'absence de transparence retombe sur une surface opaque, jamais sur une surface invisible.
