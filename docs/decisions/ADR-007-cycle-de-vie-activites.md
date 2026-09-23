# ADR-007 — Le cycle de vie des activités appartient au gestionnaire

**Statut** : accepté
**Date** : 2026

## Contexte

Chaque fonctionnalité produit des activités. Trois problèmes apparaissent immédiatement si chacune
les gère elle-même :

1. **Accumulation.** Un curseur de volume produit des dizaines de valeurs par seconde. Sans règle de
   remplacement, l'Island accumule des centaines d'entrées identiques.
2. **Expiration.** Une notification doit disparaître au bout de quelques secondes. Si chaque
   fonctionnalité arme son propre minuteur, le projet se remplit de minuteurs — précisément ce que
   [ADR-004](ADR-004-evenementiel.md) interdit.
3. **Croissance non bornée.** Rien ne limite le nombre d'activités de fond simultanées.

## Décision

`ActivityManager` est **propriétaire du cycle de vie** :

| Situation | Comportement |
|---|---|
| Publication sous un `Id` existant | **Remplacement**, jamais ajout |
| `Duration` écoulée | Retrait automatique par le gestionnaire |
| Trop d'activités de fond | Éviction des plus anciennes au-delà d'un plafond |

Une fonctionnalité publie et oublie. Elle ne retire pas ses activités et n'arme aucun minuteur
d'expiration.

## Justification

La règle de remplacement par identifiant est la plus importante, et c'est aussi la plus facile à
manquer : elle transforme un `Id` d'étiquette en **identité**. Cela a une conséquence de conception
utile — une fonctionnalité qui met à jour une activité (position de lecture, pourcentage) republie
sous le même identifiant et n'a rien d'autre à faire.

Le gestionnaire n'arme qu'**un seul minuteur borné**, à usage unique, pour la prochaine échéance, et
le désarme quand il n'y a plus rien à expirer. Le coût du cycle de vie reste donc constant, quel que
soit le nombre d'activités.

## Conséquences

- Vérifié par test : 50 changements de volume produisent **1** activité ; 10 activités de fond sont
  ramenées à 3 ; une activité dont la durée est écoulée disparaît sans intervention de sa
  fonctionnalité.
- Une fonctionnalité **ne peut pas** garder une activité en vie contre la règle du gestionnaire.
  C'est voulu : le contraire rendrait le comportement global imprévisible.
- La priorité détermine ce qui est présenté ; le plafond détermine ce qui est conservé. Les deux
  mécanismes sont distincts et ne se confondent pas.

## Alternatives écartées

- **Cycle de vie par fonctionnalité** : mène aux trois problèmes ci-dessus.
- **File sans remplacement, avec dédoublonnage à l'affichage** : déplace le problème dans le rendu et
  laisse la mémoire croître.
