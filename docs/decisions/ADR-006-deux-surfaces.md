# ADR-006 — Deux surfaces distinctes : interactive et décorative

**Statut** : accepté
**Date** : 2026

## Contexte

L'Island doit satisfaire deux exigences simultanées, et elles sont **contradictoires dans une seule
fenêtre** :

1. interagir : survol, clic, molette, glisser-déposer ;
2. ne capturer **aucun** clic en dehors de son corps — la marge du halo et du fondu doit laisser
   passer les clics vers ce qui se trouve dessous.

## Décision

Deux fenêtres, chacune homogène :

- **`IslandWindow`** — surface interactive, de la taille **exacte** de l'Island, marquée
  `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`.
- **`AtmosphereWindow`** — surface décorative portant le halo, l'ombre et la dissolution, marquée
  `WS_EX_TRANSPARENT`, donc clic-traversante par construction.

## Justification

Il faudrait une surface clic-traversante **par zone**. WinUI 3 ne l'offre pas :

- `SetLayeredWindowAttributes(LWA_COLORKEY)` est **inopérant** dans une fenêtre WinUI 3 ;
- `HTTRANSPARENT` ne franchit **pas** la frontière entre threads — la fenêtre WinUI 3 vit sur un
  autre fil que celui qui traite la boucle de messages ;
- `SetWindowRgn` découperait le **rendu** en même temps que la zone cliquable, ce qui amputerait le
  halo et la dissolution.

La séparation en deux fenêtres est donc la seule voie compatible : chacune est homogène — l'une
capture tout ce qu'elle couvre, l'autre ne capture rien. C'est une contrainte de la plateforme, pas
un choix esthétique.

Le même ADR couvre la question analogue du **fond** : les modes de composition (`Transparent`,
`Blurred`, `Opaque`) sont appliqués à la surface interactive, tandis que la surface décorative reste
transparente dans tous les cas — elle ne porte aucune surface, seulement de la lumière. Son fond est
donc déclaré une fois pour toutes dans son propre XAML.

## Conséquences

- Les deux surfaces sont positionnées par le **même appel** (`ApplyGeometry`), ce qui rend impossible
  qu'elles divergent d'un pixel. Le contrôleur applique sa géométrie initiale dès sa construction :
  la couche décorative doit donc être créée **avant** lui. Un bug d'exécution a été trouvé sur ce
  point précis — la fenêtre décorative était créée après le contrôleur.
- `WindowChrome.PlaceAbove` garantit l'ordre d'empilement lorsque les deux surfaces sont `topmost`.
- Les messages système sont routés vers la surface **interactive**, seule à en recevoir ; la
  surface décorative n'en traite aucun.
- `AtmosphereWindow.UsesCompositionSurface` expose l'état réel du rendu, ce qui permet de
  [sonder](ADR-008-dissolution.md) lequel des deux chemins est actif.

## Alternatives écartées

- **Une seule fenêtre avec gestion d'`HTTRANSPARENT`** : ne franchit pas la frontière entre threads.
- **`WS_EX_LAYERED` avec `LWA_COLORKEY`** : inopérant en WinUI 3.
- **Fenêtre plein écran transparente** : capturerait ou laisserait passer *tous* les clics, sans
  distinction possible.
