# Langage visuel

## Règle n°1 — TopAttached

> **SpaceNotch est une notch attachée au bord supérieur de l'écran, jamais une capsule flottante.**

```
████████╭────────────╮████████      le bord de l'écran coule dans la notch (épaules)
        │            │
        │  CONTENU   │
        ╰────────────╯              grands congés organiques en bas
          ░░░░░░░░░░
        ░░░░░░░░░░░░░░              l'atmosphère se dissout dessous
```

Le bord supérieur est à zéro sur toute la largeur, sans aucun décalage possible ; des épaules
concaves raccordent l'écran à la forme ; les congés du bas sont grands et suivent la hauteur. Voir
[ADR-017](../decisions/ADR-017-notch-attachee.md) et le plan [SpaceNotch 2.0](spacenotch-2.0.md).

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
| Corps | `#000000` | Noir pur : la notch se fond dans le bord de l'écran — et, sur OLED, se lit comme une découpe matérielle. |
| Verre / surfaces internes | `#08090C` | Le « noir relevé » : il laisse la place à la profondeur là où une surface doit se distinguer d'une autre. |
| Texte | blanc à ~94 % d'opacité | Sur fond clair, encre sombre à la place. |
| Indicateur de pile | blanc à ~44 % | Doit se lire comme un indice, jamais comme un texte. |

La teinte d'ambiance peut être influencée par la couleur dominante d'une pochette d'album, mais
**désaturée** et appliquée par ressort. L'intention est un halo ambiant discret — jamais un dégradé
saturé.

## Coins

Très arrondis, **interpolés continûment selon la hauteur**, et configurables en direct :

| Élément | Défaut | Plage |
|---|---|---|
| Congé compact | 26 DIP | 8–40 |
| Congé ouvert | 34 DIP | 12–48 |
| Épaules (raccord au bord de l'écran) | 8 DIP | 0–16 |

Le congé est borné par ce que la forme porte — `min((largeur − 2·épaule)/2, hauteur − épaule)` —
et non par la moitié de la hauteur : la notch n'a qu'un bord libre. Une forme compacte de 34 DIP
porte donc un congé de 26.

## Typographie

**Segoe UI Variable** — parfaitement intégré à Windows, aucune police embarquée à charger.
Un ajustement : les chiffres qui défilent (minuteur, position de lecture) utilisent des chiffres à
chasse fixe, sinon le texte « danse » à chaque incrément.

## Espacement

Au repos sans activité, la notch est une **lèvre** de 80 × 18 au bord de l'écran ; avec une
activité, une forme compacte de 34 DIP portant au plus une icône et un libellé court. L'ouverture révèle la structure : le contenu n'est jamais comprimé dans la forme
fermée.

Les encombrements sont **déclarés** par le catalogue de scènes, jamais écrits en dur dans une vue.
C'est ce qui garantit qu'ajouter une fonctionnalité n'oblige pas à retoucher la fenêtre.

## Icônes

Un jeu de glyphes, résolu par `.IconKey`. Les émojis sont réservés aux contenus où ils portent du
sens — jamais comme jeu d'icônes d'interface.

## Mouvement

Aucune animation permanente. Aucun visualiseur continu. Le mouvement est une réponse à une
transition, et il cesse.

Une seule exception, et elle a un sens : le **mouvement hypnotique**, qui signale qu'un travail est
en cours — lecture, recherche, traitement, synchronisation, dépôt. Il est rejoué par le compositeur
sans travail par image, s'arrête avec le travail, et devient une image fixe si l'utilisateur le
refuse ou si Windows demande la réduction des animations. Voir
[ADR-018](../decisions/ADR-018-mouvement-hypnotique.md).

Seule la géométrie rebondit : l'opacité, la couleur et le flou ne dépassent jamais leur cible.

Les transitions suivent une physique de ressort : ouverture vive, léger dépassement, stabilisation.
Voir [animation-system.md](../animation-system.md).

## Accessibilité

- Le clavier fonctionne : Échap réduit, flèches parcourent la pile. Le focus est possible **pendant**
  l'interaction et retiré ensuite (voir [windows-integration.md](../windows-integration.md)).
- Si Windows demande la réduction des animations, les ressorts deviennent des fondus.
- Le contraste élevé et les effets de transparence sont lus dans `SystemVisualState` ; en mode
  `Auto`, l'absence de transparence retombe sur une surface opaque, jamais sur une surface invisible.
