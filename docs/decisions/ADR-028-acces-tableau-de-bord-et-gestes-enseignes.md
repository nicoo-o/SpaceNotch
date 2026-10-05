# ADR-028 — Accès aux fonctions : un tableau de bord au clic, des gestes enseignés en contexte

**Statut** : Proposé

## Contexte

SpaceNotch vise désormais des utilisateurs qui ne lisent pas de documentation. Or presque
tout ce que fait la notch passe par des gestes invisibles :

- au repos, le clic ouvre la recherche (`IslandWindow.OpenLauncher`), et rien d'autre ;
- le minuteur, le presse-papier, la note, le détachement et les réglages passent par le clic
  droit (menu rapide, `QuickMenuScene`), le double-clic ou la molette ;
- l'aide des gestes existe (`SpaceNotch.Core.Presentation.GestureHelp`, vague 7), mais elle
  n'apparaît qu'au survol **avec Alt tenu** (`IslandWindow.QueueUndoHelp.cs`, `ArmGestureHelp`),
  un geste que personne ne devine.

Un néophyte voit donc une pilule noire qui ouvre une recherche, et rien qui lui dise qu'elle fait
davantage. Le constat vient du jalon 1 (`docs/plans/2026-10-03-constats.md`, branche
`chore/constats-jalon-1`) ; les scénarios d'accès à rejouer seront dans
`docs/plans/2026-10-04-scenarios-acces.md`.

Contraintes verrouillées (`docs/ux/spacenotch-2.0.md` §15) :
- une seule notch (ADR-017) ;
- le survol ne fait qu'un aperçu léger, c'est le clic qui ouvre ;
- aucune animation permanente ;
- silence au repos.

## Décision

L'utilisateur a choisi le 2026-10-05, sur maquettes, la combinaison **C + A** :

1. **C — un tableau de bord au clic.** Au repos, le clic ouvre la recherche, toujours en tête et
   prête à taper, au-dessus d'une rangée de quatre tuiles : Minuteur, Presse-papier, Note, Plus
   (le menu rapide). Avec une activité présentée, le clic ouvre l'activité comme aujourd'hui.
   Le clic droit, le double-clic et la molette restent des raccourcis ; ils ne sont plus le
   seul chemin.
2. **A — des gestes enseignés au moment utile.** La rangée d'aide des gestes, qui existe déjà
   dans la notch, s'affiche d'elle-même une fois quand un geste devient utile. Par exemple,
   la première musique montre « Molette · volume ». Au plus une fois par session et par geste,
   et plus du tout une fois le geste utilisé. Alt tenu au survol continue de tout montrer.

Les règles (quand montrer, quel geste, quand se taire) vivent dans `SpaceNotch.Core`, sans
dépendance Windows, et se testent sans fenêtre. Le retenu (gestes appris) va dans `AppSettings`.

## Justification

- Trois directions ont été comparées sur maquettes, chacune sur trois moments (premier
  lancement, repos, activité) :
  - **A** seul n'ajoutait aucun chemin visible ;
  - **B** rendait le survol interactif ;
  - **C** seul laissait les gestes à découvrir.
- C ne coûte rien à l'utilisateur qui ouvre la notch pour chercher : le champ garde le focus et
  la frappe part comme avant. Il ne touche ni au survol ni au repos.
- A réutilise l'aide existante (`GestureHelp`, la rangée qui reste dans la notch) : aucune
  seconde surface, aucune bulle hors de la notch, ce qui respecte ADR-017.

## Conséquences

- La scène du lanceur grandit : la rangée de tuiles s'ajoute au-dessus des fichiers récents. Sa
  hauteur et la mesure de la forme ouverte (`LauncherLayout`) changent, comme la taille de la
  toile qui en dépend (`NotchCanvas`, PR #41).
- Chaque tuile doit être joignable au clavier et nommée pour Narrateur ; Entrée tapée dans la
  recherche vide ne doit pas déclencher une tuile par erreur.
- L'aide qui s'affiche seule doit rester rare. Un compteur par geste est mémorisé dans les
  réglages, et il faut décider ce qui compte comme « appris ».
- Deux chemins vers les mêmes fonctions (tuiles et menu rapide) sont à garder cohérents :
  libellés, icônes et ordre viennent d'une seule source.
- Plus difficile : toute nouvelle fonction de premier plan doit choisir si elle mérite une
  tuile. Quatre places, pas plus, sinon le tableau redevient un menu.

## Alternatives écartées

- **A seul (gestes enseignés)** : rien de visible n'invite à ouvrir autre chose que la
  recherche ; l'aide arrive seulement quand l'occasion se présente.
- **B (pastilles au survol)** : un aperçu cliquable brouille la règle verrouillée « le survol
  invite, le clic ouvre », et ajoute un mouvement à chaque passage de la souris.
- **C seul** : la molette, le glisser et le double-clic resteraient introuvables.
