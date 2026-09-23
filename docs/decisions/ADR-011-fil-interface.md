# ADR-011 — L'hôte rétablit le fil d'interface, pas la fonctionnalité

**Statut** : accepté
**Date** : 2026

## Contexte

Une fonctionnalité publie une activité. `ActivityManager` notifie `ActiveActivityChanged`, le
contrôleur en déduit la présentation, et la fenêtre projette le résultat dans le XAML.

Tant que les fonctionnalités publiaient depuis des rappels que Windows marshale déjà — messages de
fenêtre, événements de session média — tout se passait bien. **Rien ne l'imposait.**

Un greffon tiers publie depuis un fil de travail : c'est le cas naturel d'un rafraîchissement
périodique ou d'une réponse réseau. Il atteint alors un minuteur de file d'attente depuis le mauvais
fil, et WinUI lève un `COMException`… **au message vide** :

```
[FEATURE] plugin.weather en échec :: COMException:
```

Le gestionnaire d'activités est pourtant intégralement verrouillé. Le verrou protège la collection :
il ne dit rien du fil sur lequel les abonnés sont appelés.

Découvert en exécutant le greffon d'exemple — pas en relisant le code. La documentation affirmait
que publier depuis n'importe quel fil était sûr ; c'était faux au niveau de l'interface.

## Décision

La fenêtre rétablit le fil d'interface pour **tous** les signaux qui touchent son rendu :

- `PresentedActivityChanged`, `AnimationCompleted`, `StateChanged` → rendu ;
- `ActiveActivityChanged`, `ActivityRemoved` → réarmement du minuteur d'expiration ;
- `ShelfUpdated` → mise à jour de la vue d'étagère.

Le passage est immédiat lorsqu'on est déjà sur le fil d'interface — ce qui préserve l'ordre des
opérations sur tous les chemins internes — et confié au répartiteur sinon. Les demandes de rendu
d'une même rafale sont regroupées par un drapeau atomique, puisque le rendu projette l'état
*courant* et que le rejouer n'apporterait rien.

## Justification

**Pourquoi pas dans le gestionnaire ou la fonctionnalité ?** Parce que ni l'un ni l'autre ne possède
de répartiteur. Le contexte transmis à un greffon ne contient que de quoi publier et écouter — c'est
délibéré, voir [plugin-api.md](../plugin-api.md). Une fonctionnalité ne *peut pas* se défendre
elle-même : si l'hôte ne rétablit pas le fil, aucune API ne le permet.

**Pourquoi pas dans le contrôleur ?** Le contrôleur est du cœur, testable sans machine graphique. Y
introduire un répartiteur lui ferait perdre cette propriété — 98 tests s'exécutent aujourd'hui sans
interface.

**Pourquoi pas exiger de la fonctionnalité qu'elle publie sur le fil d'interface ?** Parce que ce
serait une règle invérifiable, qu'un auteur de greffon ignore par définition, et dont la violation se
manifeste par un message d'erreur vide. Une contrainte qui produit une panne illisible n'est pas une
contrainte, c'est un piège.

## Conséquences

- Publier depuis n'importe quel fil est **réellement** sûr, et le restera : c'est l'hôte qui porte
  la garantie, pas le bon comportement des appelants.
- Le rendu peut être différé d'un tour de file sur les chemins non-interface. Sans effet observable :
  la projection est idempotente.
- Un défaut voisin a été corrigé au passage : le signalement d'une clé de scène inconnue se
  déclenchait aussi lorsque l'Island était simplement **fermée**, ce qui accusait à tort une clé
  valide. Il ne se déclenche plus que si la clé ne correspond réellement à aucune vue.
- `MiniLogger` décrit désormais une exception sans message par son type, son code de retour et la
  chaîne de ses exceptions internes. « COMException: » n'oriente vers rien ; le code
  `0x8001010E` désigne immédiatement un appel hors du fil d'interface.

## Alternatives écartées

- **Verrou global dans le gestionnaire autour de la notification** : bloque le fil appelant sans
  rétablir le bon fil ; le problème n'est pas la concurrence mais le rattachement.
- **Marshaling côté fonctionnalité** : impossible, le contexte n'expose aucun répartiteur.
- **Rattachement dans le contrôleur** : écarté dans un premier temps, puis retenu sous une forme qui
  préserve la testabilité — voir l'amendement ci-dessous.

## Amendement du 23 septembre 2026 — ce qui est établi, et ce qui ne l'est pas

Cet ADR affirmait, dans ses conséquences, que « publier depuis n'importe quel fil est **réellement**
sûr ». Une exécution réelle oblige à corriger cette phrase.

**Ce qui est mesuré.** Avec le greffon d'exemple installé, l'Island se fige en cours de morphing
(136 × 39 DIP, valeur reproduite à chaque démarrage) et le processus consomme 25 à 55 % d'un cœur,
sans qu'aucune erreur n'apparaisse au journal. Le même binaire, greffon retiré, rend un encombrement
exact au repos (34 × 28) et **0,00 % d'un cœur sur 10 s**. Le déclencheur est donc bien la publication
depuis un fil de travail, et le défaut est un blocage, pas seulement un avertissement.

**Ce qui n'est pas établi.** Le mécanisme. L'hypothèse d'origine — un `COMException 0x8001010E`
remonté depuis le greffon — a été observée sur une copie antérieure du greffon, mais **non reproduite**
avec la version courante : la cause de cette observation reste inconnue, et cet ADR ne doit pas
s'en attribuer le mérite. Le blocage actuel n'est pas davantage expliqué : deux fils tournent, la
pompe de messages n'a pas pu être observée directement, et les sondes posées pour le faire étaient
eux-mêmes défectueuses. Le défaut est **ouvert**, documenté ici pour qu'il ne soit pas pris pour une
question réglée.

**Ce qui est décidé malgré tout.** Le contrôleur reçoit désormais de quoi rejoindre le fil d'interface
— `SetDispatcher(Action<Action>)`. C'est un délégué, pas un type d'interface : le contrôleur reste
testable sans machine graphique, ce qui était la seule objection sérieuse à cette place. La raison de
fond est qu'une réaction à une activité peut animer, et qu'animer touche le compositeur et le minuteur
de file d'attente, deux objets qui n'existent que sur ce fil. Que la publication vienne du pool rend
cette réaction fautive *par construction* : il n'y a pas de scénario où elle serait acceptable.

Cet amendement ne prétend pas fermer le défaut. Il retire une affirmation fausse et corrige un chemin
fautif ; la recherche de la cause reste ouverte.
