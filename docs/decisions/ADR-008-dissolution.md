# ADR-008 — Dissolution calculée par le compositeur, repli XAML

**Statut** : accepté, **amendé le 2026-09-20** (voir « Amendement » en fin de document)
**Date** : 2026

## Contexte

La contrainte esthétique centrale du projet : l'Island ne doit présenter **aucune arête**, ni sur sa
partie inférieure, ni sur ses côtés. Le pourtour doit se dissoudre progressivement, sans ligne, sans
bord, sans séparateur.

> **Note d'amendement.** La rédaction initiale de ce paragraphe ne mentionnait que « la partie
> inférieure ». C'était un rétrécissement silencieux du cahier des charges, qui exige explicitement
> l'absence de délimitation **« ni sur les côtés (gauche/droite) »**. Le texte ci-dessus rétablit
> l'exigence complète.

Deux moyens d'y parvenir existent :

1. un **dégradé XAML** — simple, mais calculé par le processeur, et son fondu ne suit pas la
   géométrie réelle de la surface ; il devient approximatif lors de l'ouverture ;
2. un **masque de composition** — un `CompositionMaskBrush` appliqué par le compositeur Windows.

## Décision

Le rendu de référence est le **masque de composition**. Un **repli XAML** reste disponible, exposé
par la préférence `UseCompositionAtmosphere`, ainsi qu'une **sonde** qui rend le chemin actif
observable.

## Justification

Avec un masque de composition :

- le fondu est un masque appliqué à une teinte unie ;
- le passage à l'alpha zéro est calculé par le **GPU** ;
- il suit la surface à chaque image ;
- **rien n'est recalculé au repos** — `Configure` déplace un arrêt de dégradé, il ne reconstruit pas
  la collection.

Cette dernière propriété est décisive : recréer la collection d'arrêts à chaque image serait
précisément le travail inutile que [ADR-004](ADR-004-evenementiel.md) interdit.

### Pourquoi conserver le repli

Ce n'est pas une concession esthétique, c'est une assurance. `AtmosphericSurface.TryAttach` peut
échouer — un pilote graphique peut refuser la surface. Dans ce cas, `TryAttach` retourne `null` et
l'appelant conserve son dégradé XAML : **une décoration ne doit jamais empêcher l'Island de
s'afficher**.

Le réglage permet en outre de comparer les deux chemins sur la même machine, dans la même session.

### Pourquoi une sonde

Tant que le chemin actif n'est pas exposé, les deux sont **indistinguables à l'exécution** — y compris
pour un rapport de performance, qui attribuerait au compositeur le coût du dégradé. Un repli
silencieux serait invisible.

`AtmosphereRenderPath` (`NotProbed`, `Composition`, `XamlFallback`) et
`RuntimeDiagnostics.ReportAtmospherePath` rendent la distinction observable. La valeur est **rapportée
par la surface elle-même** après sa tentative d'attachement — elle est observée, pas supposée. Le
chemin est loggué au démarrage et figure dans le résumé détaillé des diagnostics.

## Conséquences

- `RuntimeDiagnostics` doit exister avant le premier `ApplyBackdropMode` : la sonde l'interroge.
- Le libellé est explicite dans le rapport : « compositeur » désigne le rendu de référence, tout
  autre libellé signale un repli.
- La séparation `AtmosphereWindow` / `IslandWindow` reste inchangée : c'est un autre sujet, traité
  par [ADR-006](ADR-006-deux-surfaces.md).

## Alternatives écartées

- **Dégradé XAML uniquement** : fondu approximatif à l'ouverture, ne suit pas la géométrie.
- **Image préparée avec canal alpha** : dépendante du DPI et de la taille, donc coûteuse à maintenir
  à chaque changement d'encombrement.
- **Masque de composition sans repli** : un pilote récalcitrant rendrait l'Island invisible.

## Amendement — 2026-09-20

Deux défauts ont été relevés à la relecture, et corrigés.

### 1. Le masque de référence ne pouvait pas fonctionner

`CompositionMaskBrush.Mask` n'accepte que deux types : `CompositionSurfaceBrush` ou
`CompositionNineGridBrush`
([documentation](https://learn.microsoft.com/en-us/uwp/api/windows.ui.composition.compositionmaskbrush?view=winrt-26100)).
L'implémentation y affectait un `CompositionLinearGradientBrush`. L'affectation était refusée,
l'exception absorbée par `AtmosphericSurface.TryAttach`, et le rendu retombait **silencieusement**
sur le dégradé XAML.

Le chemin de référence décrit par cet ADR n'a donc jamais été emprunté. La sonde `AtmosphereRenderPath`
aurait signalé `XamlFallback` — c'était précisément son rôle — mais aucune exécution n'a été observée.

**Correction.** Le dégradé est peint dans un visuel hors écran, capturé par `CompositionVisualSurface`,
puis exposé en `CompositionSurfaceBrush`. Le masque redevient un type accepté, sans dépendance Win2D.
`CompositionVisualSurface` est disponible depuis Windows 10 1903, très en dessous du plancher 23H2
retenu par [ADR-009](ADR-009-plateforme.md).

### 2. La dissolution ne traitait que le bas

Le masque était un dégradé **linéaire vertical** — `(0.5, 0)` vers `(0.5, 1)`. Il dissout le bas et
laisse les bords gauche et droit parfaitement francs, ce qui fait à nouveau lire l'Island comme une
fenêtre posée.

**Correction.** Le profil est désormais un dégradé **radial**, ellipse ancrée au milieu du bord
supérieur :

- le haut reste plein d'un bord à l'autre, là où l'Island touche l'écran ;
- les côtés se dissipent à mesure que l'on descend ;
- le bas disparaît entièrement, le rayon vertical valant exactement 1.

Le rayon horizontal est **déduit** du début de fondu (`0,5 / fadeStart`) plutôt que réglé séparément :
il place les coins supérieurs pile sur le premier arrêt plein. Les régler indépendamment laisserait
apparaître une échancrure claire aux deux coins hauts dès que `fadeStart` descend.

### Conséquence sur le repli

Le repli XAML suit désormais la **même géométrie** (`RadialGradientBrush`, même ancrage, même rayon
déduit). Un repli qui dissoudrait autrement ferait voir deux produits différents selon le pilote
graphique — ce qui viderait de son sens la notion même de repli.
