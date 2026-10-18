# Audit du setup Claude Code et comparaison avec des setups publics — 2026-10-18

Lecture seule, sauf une correction (§ 3, constat 1). Complète le rapport
[environnement Claude Code](2026-10-18-environnement-claude-code.md), qui ne couvre ni AGENTS.md, ni
ponytail, ni l'agent `reperage`.

Légende des statuts : **✔ démontré** (relu ou exécuté ici) · **≈ probable** (source tierce cohérente) ·
**? hypothèse** (à mesurer).

## 1. Ce que contient le setup aujourd'hui

| Élément | État |
|---|---|
| Instructions | `AGENTS.md` (93 lignes, neutre) importé par `CLAUDE.md` (52 lignes, propre à Claude Code) |
| Hooks du projet | `session-start` (état du dépôt), `format-cs` (PostToolUse, mode dossier), `release-build` (Stop, garde anti-boucle) |
| Skills du projet | `/adr`, `/snapshot`, `/new-feature`, `/audit` (Opus xhigh), `/cloture` (Sonnet medium), `/prompt-opus` |
| Agents | `winui-reviewer` (Opus high, lecture seule), `reperage` (Haiku low, lecture seule, Roslyn) |
| Plugins | superpowers 7.0.0, ponytail 5.3.2 (projet) ; context-bar, agent-profiler, i-have-adhd (local) |
| MCP | `cwm-roslyn-navigator`, `microsoft-learn`, `context7` ; connecteurs du compte (Vercel 237 outils, Figma 42…) conservés à ta demande |
| Permissions | `allow` ciblé, `ask` sur les opérations git destructives, `deny` sur `--admin` et les secrets |

Contexte fixe mesuré plus tôt dans la journée : environ 73 k tokens (outils MCP 20,6 k, outils système
32 k). **Non remesuré depuis l'ajout de ponytail** — voir § 4.

## 2. Comparaison avec ce que recommandent les sources publiques

| Pratique relevée | Source | Chez nous |
|---|---|---|
| Hook PostToolUse qui formate le `.cs` modifié | [guide .NET](https://blog.devgenius.io/claude-code-doesnt-understand-your-net-solution-cf2d3990c088), [autre guide](https://no.linkedin.com/in/tuan-pham-2316145) | ✔ présent, en mode dossier (2-3 s au lieu de dizaines) |
| Hook Stop qui lance `dotnet test` et garde Claude au travail tant que ça échoue | [même auteur](https://no.linkedin.com/in/tuan-pham-2316145) | **Partiel** : le Stop construit en Release mais **ne lance pas les tests** |
| Ce qui ne doit jamais arriver va dans un hook ou un `deny`, pas dans la consigne | [Anthropic, mémoire](https://code.claude.com/docs/en/memory) : « Claude traite CLAUDE.md comme du contexte, pas comme une configuration appliquée » | **Partiel** : « ne pas toucher `NotchFlow.*` » n'est qu'une consigne |
| `allow` minimal, `deny` agressif | [guides de config](https://vanja.io/claude-code-max-power/) | ✔ ; une exception locale, § 3 constat 3 |
| `CLAUDE.md` court : moins de 200 lignes (doc officielle), 60 pour d'autres | [Anthropic](https://code.claude.com/docs/en/memory) | ✔ 52 + 93 = 145 lignes chargées ; sous la limite officielle |
| Règles par chemin dans `.claude/rules/` avec `paths:`, chargées seulement au contact des fichiers | [Anthropic](https://code.claude.com/docs/en/memory) | **Absent** (gain faible, § 3 constat 7) |
| `AGENTS.md` lu par Claude Code | [Anthropic](https://code.claude.com/docs/en/memory) : un `CLAUDE.md` qui importe `AGENTS.md` est la forme prévue | ✔ mis en place ; l'import **ne réduit pas** le coût en contexte (dit par la doc) |
| LSP C# (OmniSharp) en plugin | [csharp-lsp](https://github.com/zircote/csharp-lsp) | Inutile : `cwm-roslyn-navigator` couvre déjà références, appelants, symboles |
| Skill qui génère un `CLAUDE.md` et des hooks .NET | [project-setup](https://playbooks.com/skills/jeongheonk/c-sharp-custom-marketplace/project-setup) | Inutile : le setup existe déjà et est plus précis |
| Tout autoriser (`allow: ["*"]`, `bypassPermissions`) | [vanja.io](https://vanja.io/claude-code-max-power/) (l'auteur note que ça ne faisait pas ce qu'il croyait) | À ne pas faire |

Limite de cette comparaison : les sources sont des blogs et des cours, souvent du même auteur. Seules les
lignes tirées de la documentation Anthropic sont fermes. Aucun benchmark public ne compare des setups entre
eux.

## 3. Constats, du plus important au moins important

1. **Régression que j'ai introduite — corrigée ✔.** `winui-reviewer` avait pour consigne « Lire
   `CLAUDE.md` ». Or `CLAUDE.md` ne contient plus que `@AGENTS.md` ; un agent qui le lit avec `Read` voit
   la ligne d'import, pas les règles. Corrigé : il lit maintenant `AGENTS.md`
   ([winui-reviewer.md](../../.claude/agents/winui-reviewer.md)). Non testé en session : l'agent n'a pas
   été relancé depuis.
2. **Le Stop hook ne lance pas les tests ≈.** La règle « un test non lancé n'est jamais réussi » reste une
   consigne. Mesure faite aujourd'hui : build Release 35 s, tests 4 s + 0,8 s (1 037 + 23 réussis). Ajouter
   `dotnet test` au hook coûterait environ 5 s de plus, avec la même garde anti-boucle. À décider : un test
   instable bloquerait la fin de tour.
3. **Autorisation locale trop large ✔.** `.claude/settings.local.json` contient `Bash(claude plugin *)` :
   cela autorise sans question `claude plugin install` depuis n'importe quelle marketplace, donc l'installation
   de hooks tiers. Elle sert à gérer les plugins, mais `Bash(claude plugin list)` suffirait pour lire l'état,
   et les installations devraient redemander. Même fichier : `Bash(grep -c superpowers CLAUDE.md)`, reste
   d'un test, à supprimer. Le fichier est ignoré par git.
4. **Traces du profileur non ignorées ✔.** `.claude/agent-profiler/` (648 Ko, non suivi) contient des
   traces de session avec des chemins de fichiers de ta machine. Il n'est pas dans `.gitignore` : un
   `git add .` le commiterait. À ajouter à `.gitignore`.
5. **Inventaire du skill `/prompt-opus` périmé ✔.** `references/outils.md` annonce superpowers 6.4.1 (installé :
   7.0.0) et ne cite ni `ponytail` ni l'agent `reperage`. Un brief généré aujourd'hui ne les mentionnerait pas.
6. **Coût des hooks de ponytail non mesuré ?.** Il ajoute trois hooks (SessionStart, SubagentStart, et un
   `UserPromptSubmit` à chaque message) et un module qui lance un processus détaché pour importer les
   journaux. Le code Node lu ne contient aucun appel réseau, et le webhook est vide par défaut. En plus, ton
   `~/.claude/settings.json` lance déjà `SpaceNotch.exe --hook` sur quatre événements. Latence ajoutée par
   message : inconnue. `agent-profiler`, déjà installé, peut la mesurer.
7. **`.claude/rules/` avec `paths:` — gain faible ?.** On pourrait sortir de `AGENTS.md` les règles P/Invoke
   et Win32 pour qu'elles ne se chargent qu'au contact de `Platform.Windows`. Estimation : moins de
   1 k tokens sur 73 k, et il faudrait dupliquer ces règles pour les autres agents. Pas recommandé tant que
   le fichier reste sous 100 lignes.
8. **Connecteurs du compte ✔ (décision de l'auteur : conservés).** Vercel + Figma restent le plus gros poste
   évitable du contexte MCP.
9. **Ponytail : réglages par défaut.** Quatre options ne sont pas fixées ; `ledger` est vrai par défaut et
   écrit un CSV par projet sous `~/.config/ponytail/band/`. Sans conséquence, mais à connaître.

## 4. Ce que cet audit n'a pas pu établir

- Le coût réel de ponytail en tokens et en latence (non remesuré).
- Que l'import `@AGENTS.md` se charge bien dans la prochaine session (la doc le prévoit ; pas encore constaté
  en session).
- Que `winui-reviewer` fonctionne avec la correction du constat 1.
- Que les skills de superpowers 7.0.0 se déclenchent comme avant après la réinstallation.
- Le comportement de `Edit` et `Read` avec une règle `deny` sur `src/NotchFlow.*` : la syntaxe exacte doit
  être vérifiée avant de l'écrire.

## 5. Propositions, dans l'ordre de gain

| N° | Action | Effort | Risque |
|---|---|---|---|
| 1 | Ajouter `.claude/agent-profiler/` à `.gitignore` | 1 ligne | Aucun |
| 2 | Réduire `Bash(claude plugin *)` et supprimer la ligne `grep -c superpowers` du fichier local | 2 lignes | Aucun |
| 3 | Mettre à jour `outils.md` (superpowers 7.0.0, ponytail, `reperage`) | 3 lignes | Aucun |
| 4 | Transformer « ne pas toucher `NotchFlow.*` » en règle `deny` (après vérification de la syntaxe) | 2 lignes | Faible |
| 5 | Ajouter `dotnet test` au hook Stop | ~15 lignes | Moyen : un test instable bloque la fin de tour |
| 6 | Dans une nouvelle session : remesurer le contexte fixe et la latence des hooks avec `agent-profiler` | Mesure | Aucun |

Rien de ce tableau n'est appliqué, hors le constat 1.

## 6. Appliqué après accord (propositions 1 à 5, rien de commité)

| N° | Fait | Vérifié |
|---|---|---|
| 1 | `.claude/agent-profiler/` ajouté à `.gitignore` | ✔ `git status` ne le liste plus |
| 2 | `settings.local.json` : `Bash(claude plugin *)` remplacé par `Bash(claude plugin list *)` ; ligne `grep -c superpowers` supprimée | ✔ JSON valide ; les installations de plugins redemanderont |
| 3 | `outils.md` : superpowers 7.0.0, ponytail, `reperage` | ✔ relu |
| 4 | `settings.json` : `Read(/src/NotchFlow.*/**)` et `Read(/tests/NotchFlow.*/**)`. Syntaxe tirée de la [doc des permissions](https://code.claude.com/docs/en/permissions) (motif gitignore, `/` ancré à la racine du projet ; un `deny` sur `Read` bloque aussi Edit et Write) | ✔ une lecture sous `src/NotchFlow.Core/` est refusée en session |
| 5 | `release-build.js` : `dotnet test SpaceNotch.sln -c Release` après un build réussi, même garde anti-boucle | ✔ voir ci-dessous |

Essais du hook (fichier de test jetable, supprimé ensuite) : test réussi → code 0 en 50 s ; erreur
d'analyseur → code 2, message de build ; vrai test échoué → code 2 avec le nom du test (`[FAIL]`) et le
résumé ; second passage sur le même état → code 0 (pas de boucle) ; sans code modifié → code 0.
Un premier jet filtrait mal les sorties en français (« Échoué! ») et renvoyait toute la sortie : corrigé et
retesté. Durée mesurée d'un passage complet : 27 à 50 s.

Reste non vérifié : le constat 1 (relecteur) et le chargement de l'import `@AGENTS.md`, qui demandent une
nouvelle session ; la remesure du contexte et de la latence des hooks (proposition 6).
