# ADR-003 — Aucun WebView, aucune technologie web embarquée

**Statut** : accepté
**Date** : 2026

## Décision

Aucun **Electron**, **Tauri**, **React**, **WebView2** dans le cœur de l'application, ni **Python**.

## Justification

Le but du projet est précisément d'être extrêmement léger. Embarquer un moteur de navigateur dans une
application dont la raison d'être est la légèreté serait une contradiction interne.

Au-delà de la mémoire, il y a un coût que la mesure RAM ne montre pas : le démarrage d'un moteur
Chromium, la latence d'un pont inter-processus entre l'interface et le système, et l'impossibilité
d'utiliser le compositeur Windows directement pour la dissolution atmosphérique — précisément la
contrainte esthétique centrale du projet.

## Conséquences

- Chaque écran est écrit en XAML. C'est plus verbeux qu'un composant web, et c'est assumé.
- L'absence de WebView2 est une contrainte **architecturale**, pas une préférence : la surface
  décorative s'appuie sur le compositeur, ce qu'une couche web rendrait impossible.
- Aucun `npm`, aucun bundler, aucune chaîne de construction JavaScript dans le dépôt.

## Alternatives écartées

- **Electron** : la négation directe de l'objectif.
- **Tauri** : plus léger qu'Electron, mais repose toujours sur le moteur web du système, avec les
  mêmes limites de composition.
- **WebView2 seul pour certains écrans** : introduirait une seconde chaîne de rendu et deux langages
  de style. Refusé pour cette raison, même partiellement.
