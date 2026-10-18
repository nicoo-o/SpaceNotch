---
name: cloture
description: Clôture une session SpaceNotch — écrit le relais docs/plans/relais-AAAA-MM-JJ.md, met à jour la feuille de route et la mémoire, puis prépare la PR « chore(cloture) ». À taper à la fin d'une session de travail.
disable-model-invocation: true
model: sonnet
effort: medium
argument-hint: "[suffixe du relais : soir, nuit… — facultatif]"
---

# Clôture de session

Suffixe du relais : $ARGUMENTS (vide : relais du jour, sans suffixe)

But : que la prochaine conversation parte d'un état **exact**. Une information vit à un seul endroit :
le relais dit ce qui est vrai maintenant, un ADR (`/adr`) porte une décision durable de structure, la
mémoire ne garde que ce que le dépôt ne dit pas.

## 1. Collecter (lecture seule)

- `git fetch`, `git status -sb`, `git rev-list --left-right --count origin/main...HEAD`.
- Le dernier relais : le plus récent de `docs/plans/relais-*.md` (à date égale : sans suffixe, puis
  `soir`, puis `nuit`). Sa date est la borne basse.
- Les PR fusionnées depuis cette date :
  `gh pr list --state merged --search "merged:>=AAAA-MM-JJ" --json number,title,mergedAt,headRefName --limit 100`,
  puis les PR ouvertes : `gh pr list --state open`.
- Dans cette conversation : ce que l'auteur a demandé, décidé (avec la date), et **comment chaque chose
  a été prouvée** (test, capture, journal, UIA). Pas de preuve : « non vérifié ». Ne rien inventer.

## 2. Écrire le relais

`docs/plans/relais-AAAA-MM-JJ[-suffixe].md`. Lire d'abord le dernier relais pour le ton et la forme,
puis reprendre ses sections :

1. `# Relais — AAAA-MM-JJ`, puis deux lignes : quelles sessions, quelle demande.
2. `## État` : ce que `main` contient (PR de #x à #y) ; tableau **Fait** (PR · sujet · preuve) ;
   **Relectures qui ont changé le fond** ; **Décisions de l'auteur** ; **Découvert en route** ;
   **Non vérifié** ; **Configuration de l'auteur** (ce qui a été changé puis restauré, et comment on
   sait qu'il l'est).
3. `## Décisions de cette session` : tableau Décision · Raison.
4. `## Reste à faire` : renvoi à l'« Ordre proposé » de la feuille de route, pas une copie.

Pas de « Prochain brief » sauf demande : c'est le rôle de `/prompt-opus`.

## 3. Feuille de route

`docs/plans/2026-10-04-feuille-de-route.md` : déplacer les n° terminés dans « Fait depuis le … » avec
leur PR, mettre à jour la date « mise à jour le … » en tête. Ne pas re-trier le reste sans demande.

## 4. Mémoire

Relire `MEMORY.md` et les fichiers que la session touche. Corriger ce qui est devenu faux (PR fusionnée,
réglage changé), n'ajouter que des faits durables absents du dépôt. Plusieurs fichiers qui se chevauchent :
skill `consolidate-memory`.

## 5. Vérifier, puis demander

- Chaque chemin cité existe ; chaque numéro de PR vient de `gh`, pas de la mémoire.
- Montrer `git diff --stat` et **demander avant** de créer la branche `chore/cloture-AAAA-MM-JJ`, de
  commiter (`chore(cloture): feuille de route et relais du AAAA-MM-JJ`), de pousser et d'ouvrir la PR en
  français. Ne pas la fusionner ; ensuite, les outils `ccd_pr`.
