# Protéger `main` (ouverture aux contributions)

Décision de l'auteur, le 2026-10-13 (direction §7, point 8) : SpaceNotch s'ouvre aux contributions.
[CONTRIBUTING.md](../../CONTRIBUTING.md), [SECURITY.md](../../SECURITY.md), les modèles d'issue et de
PR existent déjà (#50). Il reste à protéger `main` sur GitHub. Ce sont des réglages de la page du
dépôt : **l'auteur les applique lui-même**, l'assistant ne touche pas à la page GitHub.

## Marche à suivre

GitHub › dépôt `nicoo-o/SpaceNotch` › **Settings** › **Rules** › **Rulesets** › **New ruleset** ›
**New branch ruleset**.

1. **Ruleset Name** : `main`. **Enforcement status** : *Active*.
2. **Target branches** : *Add target* › *Include default branch* (`main`).
3. **Bypass list** : ajouter *Repository admin*, seulement si tu veux garder la possibilité de
   pousser en urgence. Sinon, laisser vide.
4. Cocher :
   - **Restrict deletions** ;
   - **Block force pushes** ;
   - **Require a pull request before merging** :
     - *Required approvals* : `0` tant que tu es seul à relire, à monter à `1` dès qu'un second
       relecteur existe ;
     - *Dismiss stale pull request approvals when new commits are pushed* ;
     - *Allowed merge methods* : **Squash** seulement, comme aujourd'hui ;
   - **Require status checks to pass** :
     - *Require branches to be up to date before merging* ;
     - *Add checks* : `Construction et tests` et `Variantes de l'exécutable` (les deux travaux
       de `ci.yml`) ;
   - **Require linear history** (la fusion par squash le garantit déjà).
5. **Create**.

## Ce qui change ensuite

- Plus aucune poussée directe sur `main` : tout passe par une PR, CI verte, branche à jour. C'est
  déjà la règle de travail de l'assistant ; elle devient une règle du dépôt.
- « Branche à jour » oblige à fusionner `main` dans une branche avant de la fusionner. GitHub
  propose un bouton *Update branch* sur la PR.
- Les PR venant d'un fork lancent la CI après approbation du premier passage
  (*Settings › Actions › Fork pull request workflows*). C'est le réglage par défaut, à garder.

## Pour vérifier

Après l'avoir créé :
- `gh api repos/nicoo-o/SpaceNotch/rulesets` doit lister le ruleset `main` ;
- une poussée directe de test sur `main` doit être refusée.
