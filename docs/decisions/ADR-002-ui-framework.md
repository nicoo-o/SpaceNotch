# ADR-002 — WinUI 3 / Windows App SDK

**Statut** : accepté
**Date** : 2026

## Contexte

L'interface doit être native, fluide, et reposer sur le compositeur Windows plutôt que sur un moteur
de rendu embarqué.

## Décision

**WinUI 3** via **Windows App SDK**.

## Justification

C'est l'UI native moderne de Windows, utilisable en C#, et elle donne accès à
`Microsoft.UI.Composition` — le compositeur Windows. C'est cette dernière capacité qui est
déterminante : la contrainte esthétique du projet (dissolution du bas sans arête) exige un masque
calculé par le GPU, pas un dégradé calculé par le processeur. Voir
[ADR-008](ADR-008-dissolution.md).

Windows App SDK fournit également `SystemBackdrop`, `AppWindow` et le support DPI, ainsi que la
possibilité d'utiliser Native AOT.

## Conséquences

- La cible est Windows 11 23H2 minimum — voir [ADR-009](ADR-009-plateforme.md).
- La compatibilité WinUI 3 avec une fenêtre **sans bord** doit être établie par l'usage, pas
  supposée : d'où [ADR-006](ADR-006-deux-surfaces.md).
- `SetLayeredWindowAttributes(LWA_COLORKEY)` est inopérant dans une fenêtre WinUI 3. Cette limite a
  une conséquence architecturale directe et documentée.

## Alternatives écartées

- **WPF** : pas d'accès au compositeur moderne ; animations sur le processeur.
- **UWP** : modèle de distribution et d'exécution contraint, en fin de cycle.
- **Moteur de rendu custom (Direct2D/DirectComposition en direct)** : contrôle maximal, mais
  il faudrait réécrire la gestion de texte, la saisie, l'accessibilité et la mise en page. Le coût
  dépasse largement le bénéfice pour ce projet.
