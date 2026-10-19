# Clavier et Narrateur : audit des scènes (chantier 1, P11)

Audit par lecture du code, **sans Narrateur lancé** : les points marqués « à confirmer » restent des hypothèses.

## Corrigé

- `InfoScene` (carte d'agent) : le développement/repli ne se faisait qu'à la souris (appui sur la scène).
  Le repère ▾/▴ est devenu un bouton nommé (« Développer » / « Replier »), même action que l'appui.

## Vérifié, rien à corriger

- Presse-papier : les lignes sont des boutons (coller, épingler, supprimer) ; le balayage n'est qu'un raccourci souris.
- Couleur, tuiles du lanceur et du menu rapide : boutons nommés.

## À confirmer avec le Narrateur

- Échap dans `WelcomeScene` et `QuickMenuScene` (géré sans doute par `OnIslandKeyDown`, `IslandWindow.xaml.cs`).
- Retour du focus à l'application précédente à la fermeture (chemin inverse de `CaptureKeyboardForTyping`).
- Filtrage des annonces répétées (`Announcement.For`) et ordre de tabulation de `ColorScene` et du menu rapide.
