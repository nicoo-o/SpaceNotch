---
name: prompt-opus
description: Prépare le brief complet d'une session de code ou d'audit confiée à une autre conversation (Opus 5.5 par défaut) — mission, critères de réussite, périmètre, outillage à employer (skills, sous-agents, MCP, web, questions, plugins), autorisations écrites, rapport final. À utiliser quand l'utilisateur veut le prompt d'une session ; ne sert pas à faire la tâche lui-même.
disable-model-invocation: true
argument-hint: "<la tâche, même en vrac> [--court]"
---

# Brief de session pour Opus

Tâche : $ARGUMENTS

Ce skill **n'exécute pas la tâche** : il écrit le brief qu'une autre conversation Claude Code (modèle
cible : Opus 5.5) exécutera, dans `docs/prompts/`. Il ne modifie ni le code ni l'historique git. Un
brief est réussi quand l'autre conversation n'a plus rien à demander de ce qui était déjà connu ici.
Une tâche de moins de 20 minutes se donne directement, sans brief.

Deux faits dictent la forme du brief (guides officiels Opus 5 et 5.5, relus le 2026-10-03) :

- Opus n'agit que sur ce qui est nommé : un outil, un type de sous-agent, un push, l'accès à
  l'ordinateur ne sont employés que s'ils sont écrits, et autorisés quand ils sont risqués.
- Il rend mieux avec un objectif, des garde-fous et des critères de sortie qu'avec une procédure : le
  brief décrit des **jalons**, pas des étapes.

## 1. Cadrer

- Écrire en une phrase le résultat voulu et pourquoi il compte. Garder les observations de
  l'utilisateur dans ses mots : ce sont des **constats à reproduire**, pas des spécifications.
- Argument vide, ou trop vague pour savoir ce que « fini » veut dire : poser la question (étape 3) d'abord.
- Argument = fichier de relais (`docs/plans/relais-*.md`) : repartir de son état, pas de zéro.
- Une demande qui mêle corriger, concevoir et découvrir reste **un** brief, en jalons. La scinder en
  plusieurs briefs seulement si l'utilisateur le demande.

## 2. Ancrer dans le dépôt (lecture seule, borné)

- `git fetch`, puis `git rev-list --left-right --count origin/main...HEAD`, `git status -sb` et le
  dernier commit. Le brief les donne datés : l'utilisateur travaille sur plusieurs PC, la copie locale
  peut être en retard.
- Trouver les docs, ADR, fichiers et tests concernés (`Grep`, `Glob`, `cwm-roslyn-navigator` pour les
  symboles ; ne pas lire `IslandWindow*.cs` en entier). **Vérifier que chaque chemin cité existe.** Ce
  qui n'est pas vérifié s'écrit « hypothèse à vérifier ».
- S'arrêter dès qu'on sait où pointer Opus : l'enquête est son travail, pas celui du brief.
- Relever les décisions verrouillées et les pièges de `CLAUDE.md` et des ADR qui touchent la tâche. Le
  brief y renvoie au lieu de les recopier.

## 3. Interroger l'utilisateur (`AskUserQuestion`)

Seulement ce qui change vraiment le brief et que le dépôt ne tranche pas : deux appels au plus, quatre
questions chacun. Le reste devient une hypothèse écrite. Ne pas reposer ce que l'argument ou la
conversation ont déjà réglé. Questions types :

1. **Tâches ou priorités**, si la demande est un lot.
2. **Autorisations** (choix multiple) : git (branche, commits, push, PR — jamais de merge) ;
   computer-use ; Workflow / « ultracode » ; plugins. Une case décochée devient un interdit écrit dans
   le brief.
3. **Cadence des questions d'Opus** : un lot au début puis autonome, jalons fixes, ou au fil de l'eau.
4. **Critères de réussite**, quand l'utilisateur n'en donne pas et que les proposer ne suffit pas.

## 4. Composer

Partir de `references/gabarit.md` (trame, relecture) et `references/outils.md` (bloc d'outillage, à
filtrer selon la tâche et les autorisations). Règles d'écriture :

- Jalons avec critère de sortie, jamais d'étapes détaillées. Nommer chaque comportement voulu et dire
  **quand** il s'applique ; expliquer le pourquoi des contraintes qui ne vont pas de soi.
- Périmètre explicite, et « un outillage large n'élargit pas le périmètre » : ce qui est découvert en
  plus va dans la feuille de route, pas dans le diff.
- Aucune consigne générique de re-vérification (« revérifie », « fais contrôler par un sous-agent ») :
  Opus se vérifie déjà seul, et ces lignes le font sur-vérifier. N'écrire que les preuves propres au
  projet (build Release, tests, `/snapshot`).
- Ne jamais demander d'écrire son raisonnement : demander les décisions, leur justification en quelques
  lignes et un résumé des actions. La demande de raisonnement peut être refusée.
- Sous-agents : dire quand déléguer (pistes grandes, indépendantes, parallélisables) et plafonner. Les
  autoriser par leur nom : faute de quoi l'outil Agent reste inutilisé.
- Autorisations accordées **et** refusées, noir sur blanc. Brief rédigé à la première personne, comme un
  message de l'utilisateur.
- Mission, critères et règle de non-arrêt en tête ; rappel en dernière ligne.
- Français, proportionné à la tâche, sans recopier `CLAUDE.md`. `--court` : mission, critères,
  périmètre, autorisations et cinq lignes d'outillage, sans jalons ni bilan.

## 5. Écrire le fichier

`docs/prompts/AAAA-MM-JJ-<slug>.md` : l'en-tête pour l'utilisateur (réglages de session, avant de
lancer, ligne d'amorce — voir le gabarit), une ligne `---`, puis le brief. Réglages conseillés : Opus
5.5, effort `high`, permissions `acceptEdits`. `xhigh` seulement pour une conception très ouverte : à
ces niveaux Opus 5.5 réfléchit davantage par tour. L'effort se règle dans l'app, pas dans le texte.

## 6. Relire à froid

Passer la liste de `references/gabarit.md` § Relecture, comme le ferait un collègue qui ne connaît ni le
projet ni cette conversation. Corriger avant de rendre.

## 7. Rendre

Le lien du fichier, la **ligne d'amorce** dans un bloc de code, les réglages conseillés, ce que le brief
autorise et interdit, les hypothèses et les questions ouvertes. Ne pas recoller le brief dans la
conversation. Ne rien commiter.

Pourquoi une ligne d'amorce : l'app peut encadrer un texte collé et le traiter comme non fiable ; Opus
n'obéit alors qu'aux consignes que l'utilisateur formule lui-même. La ligne, tapée avant de coller,
adopte le brief et répète les autorisations. Pas d'envoi par `SendMessage` : le message arriverait
étiqueté comme venant d'une autre session, donc sans la valeur d'une consigne tapée par l'utilisateur.

`references/outils.md` vieillit, car les outils changent de nom. Quand l'un manque, le retrouver avec
`ToolSearch`, `.mcp.json` et la liste des skills, puis corriger le fichier.
