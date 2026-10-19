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

## Confirmé ensuite

- Entrée/Espace sur un bouton focalisé : `Button` traite lui-même ces touches et le `KeyDown` ne remonte pas (documentation Microsoft Learn, « Keyboard events »). `InvokePrimaryActionAsync` ne se déclenche donc pas en double.
- Tabulation : ni `ColorScene` ni le menu rapide ne désactivent `IsTabStop` ; l'ordre est celui de l'arbre visuel. Le menu rapide cycle dans sa liste (`TabFocusNavigation="Cycle"`).

## Limite

Tout vient de la lecture du code et de la documentation ; aucun passage au Narrateur de Windows n'a eu lieu.
