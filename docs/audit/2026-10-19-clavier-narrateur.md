# Clavier et Narrateur : audit des scènes (chantier 1, P11)

Audit par lecture du code, **sans Narrateur lancé** : les points marqués « à confirmer » restent des hypothèses.

## Corrigé

- `InfoScene` (carte d'agent) : le développement/repli ne se faisait qu'à la souris (appui sur la scène).
  Le repère ▾/▴ est devenu un bouton nommé (« Développer » / « Replier »), même action que l'appui.

## Vérifié, rien à corriger

- Presse-papier : les lignes sont des boutons (coller, épingler, supprimer) ; le balayage n'est qu'un raccourci souris.
- Couleur, tuiles du lanceur et du menu rapide : boutons nommés.

## Confirmé par lecture du code

- Échap : un seul gestionnaire sur la notch (`HandleIslandKeyAsync`) referme toutes les scènes ; Bienvenue et menu rapide ne le captent pas.
- Focus : `CaptureKeyboardForTyping` mémorise la fenêtre d'avant ; à la fermeture (`IslandState.Closed`), `ReturnForeground` la rétablit.
- Annonces : `Announcement.Key` ignore les chiffres (un minuteur n'est pas redit chaque seconde) ; `For` ne dit rien d'une republication ni d'un retour système. Test ajouté : `AnnouncementTests` (4 cas, écrits après coup, verts du premier coup : ils figent le comportement existant).

## Non vérifié (demande l'application lancée et le Narrateur)

- Ordre de tabulation de `ColorScene` et du menu rapide.
- Entrée sur un bouton focalisé : le bouton agit-il sans que `InvokePrimaryActionAsync` se déclenche aussi ?
