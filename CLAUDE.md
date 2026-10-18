# SpaceNotch

@AGENTS.md

Les règles du projet (construire, tester, architecture, conventions, preuves, pièges) sont dans
`AGENTS.md`, importé ci-dessus et partagé avec les autres agents de code. Ce fichier ne garde que ce qui
est propre à Claude Code.

## Spécifique à Claude Code

- Un hook de fin de tour construit en Release quand du code a changé (`.claude/hooks/release-build.js`).
- Ajouter une fonctionnalité : skill `/new-feature`. Décision d'architecture : skill `/adr`. Vérifier un
  changement visuel : skill `/snapshot` (capture du haut de l'écran).
- Relire un changement d'interface ou de fenêtre : subagent `winui-reviewer`.

## Façon de travailler

- superpowers est installé : ses plans et specs s'écrivent **en français**, dans `docs/plans/`
  (pas `docs/superpowers/`). **Pas de brainstorming pour une tâche de moins de 20 minutes** : la
  plupart des demandes sont courtes, les faire directement. Pas de worktree : une seule copie du
  dépôt par machine.
- Naviguer dans le C# avec le serveur MCP `cwm-roslyn-navigator` (`find_callers`,
  `get_symbol_source`, `find_references`) plutôt qu'en lisant des fichiers entiers. Premier appel :
  ~25 s de chargement.
- Gros chantier (nouvelle vague, machine à états) : proposer le mode plan avant d'écrire du code.
- Confier une session à une autre conversation (Opus) : `/prompt-opus <tâche>` écrit le brief dans
  `docs/prompts/` (mission, critères, outillage, autorisations). Le coller après la ligne d'amorce qu'il
  donne.
- Auditer un périmètre (fonctionnalité, couche, tout le dépôt) : skill `/audit` (lecture seule, rapport
  dans `docs/audit/`). Clore une session : skill `/cloture` (relais, feuille de route, PR de clôture).

## Modèle, effort et contexte

- Routage automatique seulement par l'en-tête (`model:`/`effort:`) des skills et agents : `reperage` (haiku,
  low : recherche mécanique en lecture seule, à lui confier plutôt qu'au modèle principal), `winui-reviewer`
  (opus, high), `/audit` (opus, xhigh), `/cloture` (sonnet, medium). Ne pas définir
  `CLAUDE_CODE_SUBAGENT_MODEL` : il écraserait ces choix. **Le modèle principal reste celui qui délègue et
  rédige les consignes** : jamais haiku, qui ne fait que chercher. Le reste se règle à la main avec
  `/model` et `/effort`. Consultation pure ou retouche
  mécanique : `haiku`, effort `low`. Défaut : `sonnet`, effort `medium`. Architecture, cycle de vie WinUI,
  animations, machine à états : `sonnet`, effort `high`. Bogue intermittent, problème multi-couches ou
  essai déjà raté : `opus`, effort `high` (`/model opusplan` : Opus planifie, Sonnet exécute). Fichier ou
  test sauté : monter l'effort ; erreur malgré tout le contexte : monter le modèle. `/audit` et
  `winui-reviewer` fixent déjà le leur (Opus).
- Une session coûte par sa longueur, pas par son démarrage (80 k tokens fixes, mesurés le 2026-10-18) :
  `/clear` entre deux tâches sans lien ; l'état durable vit dans `docs/plans/relais-*.md`.

## Compact instructions

Quand la conversation est compactée, conserver : la liste des fichiers modifiés, les commandes de test
lancées avec leur résultat, le numéro de la feuille de route en cours, et les décisions de l'auteur pas
encore écrites dans `docs/`.
