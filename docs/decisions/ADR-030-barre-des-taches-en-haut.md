# ADR-030 — Barre des tâches en haut : la notch demande à l'utilisateur où se placer

**Statut** : Proposé. Direction choisie par l'auteur le 2026-10-13 (direction §7, point 6) ; à
livrer (feuille de route, n° 93).

## Contexte

SpaceNotch s'accroche au bord supérieur de l'écran ([ADR-017](ADR-017-notch-attachee.md), §15 de
[SpaceNotch 2.0](../ux/spacenotch-2.0.md) : « notch attachée au top au repos et au démarrage »).
Elle connaît aussi deux bords latéraux, où elle devient une languette
([ADR-020](ADR-020-bords-et-ecrans.md)).

Windows 11 ne propose plus de barre des tâches en haut, mais des outils répandus la remettent là :
StartAllBack, ExplorerPatcher. Sur ces machines, le haut de l'écran est déjà occupé, et la notch
accrochée au bord supérieur se retrouve sur la barre ou contre elle.

Ce qui n'a pas été vérifié, et reste à mesurer avant la livraison :
- si la notch, en haut, se place par rapport à l'écran entier ou à la zone de travail
  (`WorkAreaDip`, `IslandWindow.Detach.cs`) ;
- ce que rapportent ces outils à Windows (zone de travail réduite par le haut, ou non) ;
- le comportement exact aujourd'hui : chevauchement, ou notch sous la barre par hasard.

Deux réponses étaient possibles : décaler la notch sous la barre, ou proposer un bord latéral. Elles
ont chacune des inconvénients, et les utilisateurs de ces outils ont des habitudes très différentes.

## Décision

Quand SpaceNotch détecte une barre des tâches au bord supérieur de l'écran de la notch, **elle le
dit à l'utilisateur et lui demande où se placer** :
- **sous la barre** : la notch garde sa forme et son comportement, et se pose juste sous la
  barre ;
- **en languette sur un côté**, gauche ou droit : le bord latéral d'[ADR-020](ADR-020-bords-et-ecrans.md).

La question est une carte dans la notch elle-même, posée une seule fois, comme les gestes qu'on
enseigne au moment utile ([ADR-028](ADR-028-acces-tableau-de-bord-et-gestes-enseignes.md)). Le choix
est enregistré et se change dans Réglages › Notch. Sans réponse, la notch se pose sous la barre
pour ne jamais la recouvrir.

## Justification

- Choix de l'auteur (2026-10-13), devant les deux options présentées : « demande à l'utilisateur
  quelle position il préfère ».
- Les deux placements ont un vrai public :
  - sous la barre, on garde la notch « en haut », c'est-à-dire l'identité du produit ;
  - en languette, on garde le haut de l'écran libre.
  Imposer l'un des deux décevrait l'autre moitié.
- Demander au moment où le problème se pose, dans la notch, suit la règle d'ADR-028 : enseigner
  quand c'est utile, sans réglage caché qu'il faudrait trouver.

## Conséquences

- Il faut **détecter** une barre des tâches en haut : bord de l'AppBar (`SHAppBarMessage`) ou zone
  de travail réduite par le haut. Il faut aussi suivre son apparition, sa disparition et son
  masquage automatique (`WorkAreaChanged` est déjà écouté, `IslandWindow.xaml.cs`).
- « Sous la barre » ajoute un décalage vertical au placement en haut. La forme accrochée au bord
  (épaules concaves, [ADR-017](ADR-017-notch-attachee.md)) ne touche alors plus le bord de l'écran :
  le dessin des épaules doit être revu pour ce cas. **C'est la partie la plus difficile.**
- Une carte de plus à enseigner, et un réglage de plus dans Réglages › Notch.
- Une barre en masquage automatique, qui n'occupe le haut que par moments, reste à trancher : faut-il
  la traiter comme une barre absente ?
- Les tests doivent couvrir la détection et le placement en Core, sans dépendre d'un outil tiers
  installé.

## Alternatives écartées

- **Toujours se décaler sous la barre** : la notch ne toucherait plus le bord, ce qui contredit
  l'identité de la forme accrochée, et ce serait imposé à ceux qui préfèrent un côté.
- **Toujours proposer un bord latéral** : la notch perdrait sa place en haut, qui est la promesse du
  produit.
- **Ne rien faire** : la notch recouvre ou bouscule la barre des tâches, ce qui casse un outil que
  l'utilisateur a installé exprès.
