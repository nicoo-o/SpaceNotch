# ADR-001 — C# plutôt que C++

**Statut** : accepté
**Date** : 2026

## Contexte

L'objectif est une application Windows native, très légère, avec un rendu GPU, une architecture
modulaire et un coût de maintenance faible sur plusieurs années. Le langage doit permettre d'atteindre
les API système (compositeur, Win32, média, audio) sans introduire de couche intermédiaire lourde.

## Décision

**C#**, avec interop Win32/P/Invoke pour les parties qui l'exigent.

## Justification

C++ permettrait d'aller plus loin en optimisation mémoire. Mais le coût de développement et de
maintenance serait supérieur, et pour ce projet c'est le facteur décisif : une Dynamic Island est un
assemblage de nombreuses fonctionnalités modestes, pas un noyau de calcul intensif. La vitesse
d'écriture et la facilité de modification pèsent plus lourd que quelques mégaoctets.

C# donne par ailleurs accès à **toutes** les API nécessaires : le compositeur via
`Microsoft.UI.Composition`, Win32 via P/Invoke, les sessions média et l'audio via les projections
WinRT. Aucune capacité requise n'est absente.

## Conséquences

- Le plancher mémoire du runtime .NET et de WinUI 3 est réel : la cible de 30–60 MB n'est pas
  atteinte (~96 MB mesurés). Voir [performance.md](../performance.md) pour la mesure publiée telle
  quelle.
- Une réduction ultérieure passerait par **Native AOT**, dont la compatibilité WinUI doit être
  vérifiée avant d'être promise.
- Le cœur (`SpaceNotch.Core`) ne référence aucune bibliothèque Windows, ce qui rend la suite de tests
  exécutable sans machine graphique — un bénéfice direct et mesurable de ce choix.

## Alternatives écartées

- **C++** : maintenance plus coûteuse, gain mémoire non déterminant pour ce profil d'application.
- **Rust** : écosystème WinUI 3 en C#/interop insuffisant ; le coût d'interop annulerait le gain.
