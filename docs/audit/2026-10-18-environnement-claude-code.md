# Audit de l'environnement Claude Code de SpaceNotch — 2026-10-18

L'audit (§1 à §13) a été mené en **lecture seule** : rien n'a été installé ni supprimé. Après ton accord,
**P0, P1 et P2 du §9 ont été appliqués dans le dépôt** (§14), sans commit. P3 et P4 (réglages utilisateur,
application, machine) n'ont pas été touchés.

Légende des preuves : **✔ mesuré** (commande, journal ou outil de l'application) · **◐ estimé**
(calcul à partir d'une mesure, à confirmer) · **✖ non vérifié** (dit par une source, non contrôlé).

Environnement relevé : Claude Code natif **2.1.288** au premier relevé, **2.1.302** au second le même jour
(mise à jour constatée entre les deux, malgré `DISABLE_AUTOUPDATER`), Windows 11 Home 10.0.26200,
Node 24.15.0, .NET SDK 10.0.401, `gh` connecté (`nicoo-o`), `winapp` 0.6.1 (0.7.1 existe),
forfait **Pro**. Dépôt `https://github.com/nicoo-o/SpaceNotch` : `main` à jour avec `origin/main`
(0 retard, 0 avance), seul fichier non suivi : `docs/audit/2026-10-audit-produit.md` (à toi, intact).

---

## 1. Résumé

1. **La configuration du dépôt est déjà saine et légère.** `CLAUDE.md` fait 94 lignes (la doc officielle
   vise moins de 200), 4 skills dont 2 cachés au modèle, 1 agent, 2 hooks qui fonctionnent, 3 serveurs MCP
   (25 outils). Sa part dans le contexte de départ est de l'ordre de **2 à 3 k tokens sur 80 k** ◐.
2. **Le contexte fixe est de 79 900 tokens** ✔ (mesuré par l'application, §7) : 8 % de la fenêtre de
   1 M. Il vient presque entièrement de la plateforme : outils intégrés (37 k), outils MCP (20 k), prompt
   système (9 k), skills (8,6 k), instructions MCP (4,4 k). **Les connecteurs claude.ai Vercel, Figma et
   Claude Docs, sans rapport avec un projet WinUI, pèsent environ 18 k** ◐.
3. **Le vrai poste de dépense est la longueur des sessions**, pas le fixe : sur Opus 5.5, **1 209 tours,
   433 k tokens de contexte en moyenne par tour, pic à 705 k** ✔. Le seuil de compaction par défaut est à
   ~967 k. Le réglage documenté `/autocompact 400k` est le levier le plus puissant de cet audit.
4. **Un défaut de permission réel** ✔ : `Bash(git branch:*)` autorise `git branch -D` sans demander.
5. **Deux hypothèses de départ ont été infirmées par les journaux** ✔ : (a) les lectures de
   `IslandWindow*.cs` ne gaspillent pas (43 sur 47 sont partielles, ~1 k tokens chacune) ; (b) `CLAUDE.md`
   est suivi sur ce point. Aucun hook de blocage n'est donc justifié.
6. **Aucun plugin, mod ni MCP tiers n'est à installer.** Le MCP Memory est écarté (doublon de la mémoire
   native et des `docs/`). Les mods de hamzafer et de YeonwooSung sont écartés (Windows non documenté,
   exécutent du code non isolé, et leurs fonctions utiles existent en natif).
7. **Une boucle d'amélioration continue existe déjà** (clôtures → `relais-*.md` → feuille de route → ADR).
   Ce qui manque, c'est de la **codifier** en skill `/cloture`, pas d'ajouter un système de mémoire.

---

## 2. Sources étudiées

« Lu » = contenu réellement récupéré. Les pages longues sont tronquées par l'outil de lecture : la part
lue est indiquée.

| N° | Source | Contenu retenu | Vérification | Limites / risques | Décision |
|---|---|---|---|---|---|
| 1 | [MindStudio — Projects et skills](https://www.mindstudio.ai/blog/claude-projects-skills-setup-guide) (2026-09-18) | Instructions courtes, justification de chaque règle, procédures longues en fichiers de référence | Lu. Traite de **Claude.ai Projects**, pas de Claude Code ; les skills n'y sont que dans le titre | Aucune limite chiffrée, « recommandations Anthropic » sans lien | **Adaptée** : principes confirmés par la doc officielle (`memory`), rien à ajouter |
| 2 | [YeonwooSung/my-claude-code-mods](https://github.com/YeonwooSung/my-claude-code-mods) | 17 mods (context-inspector, handoff-pack, decision-ledger…) ; 979 ★, Apache-2.0 | Lu (page principale). Date du dernier commit absente de la page | Installation « macOS/Linux » seulement ; surcoût cumulé **non mesuré** (dit par le dépôt) ; un mod n'est pas isolé | **Rejetée** (installation). Idées couvertes en natif |
| 3 | [hamzafer/claude-code-mods](https://github.com/hamzafer/claude-code-mods) (= source 12) | context-bar, cache-clock, review-watch, md-preview, usage-meter… ; 267 ★, MIT | Lu. Compté **une seule fois** | Plusieurs mods exigent macOS/Chrome ; l'installeur modifie le profil Claude ; Windows non mentionné | **Rejetée** : `/context`, `/usage` (ligne `Prompt cache`) font la même chose |
| 4 | [MindStudio — meilleurs dépôts](https://www.mindstudio.ai/blog/best-claude-github-repos-workflow) (2026-10-12) | Les outils « d'économie de tokens » (Caveman, RTK, Ponytail) ne tiennent pas leurs promesses ; Claude Mem à éviter | Lu. L'éditeur vend son propre produit (Remy) ; **les tests JetBrains cités ne sont pas vérifiés** ✖ | Aucune source primaire | **Rejetée** (aucune installation) ; l'avertissement sur les économies de tokens est retenu |
| 5 | [Fast.io — modèles](https://fast.io/resources/claude-code-models-configuration-guide/) (maj 2026-06-30) | Alias, variables, `/model`, `fallbackModel`, `effortLevel` sans `max` | Lu. Mécanismes **✔ confirmés** par la doc officielle. **Noms de modèles périmés** (Opus 4.8, Sonnet 4.6, Fable 5) ; tarifs ✖ | Date de juin, avant Opus/Sonnet/Haiku 5.5 | **Adaptée** (mécanismes), **rejetée** (noms, tarifs) |
| 6 | [DEV — choisir le modèle](https://dev.to/klement_gunndu/pick-the-right-claude-code-model-for-every-task-1p6a) (2026-03-27) | Haiku/Sonnet/Opus, `opusplan`, sous-agents par modèle | Lu. **Contredit la doc** : « Explore tourne sur Haiku » — la doc actuelle dit qu'Explore **hérite du modèle principal** | Chiffres « 3× plus vite » sans source ; `max` réservé à Opus 4.6 (périmé) | **Adaptée** ; la doc officielle l'emporte |
| 7 | [Medium — 6 mois de réglages](https://medium.com/data-science-collective/i-spent-6-months-tuning-claude-code-heres-the-exact-setup-that-finally-worked-b41c67628478) | — | **Non lue** : HTTP 403. Une recherche n'a pas retrouvé cet article (titres voisins seulement) | — | **Aucune décision n'en découle** |
| 8 | [Anthropic — configuration du modèle (FR)](https://support.claude.com/fr/articles/11940350-configuration-du-modele-claude-code) | Trois mécanismes (`/model`, `--model`, `ANTHROPIC_MODEL`) ; identifiants complets dont `claude-sonnet-5-5` | Lu | Pas d'alias, pas de défauts par forfait | **Retenue** : seule page qui donne l'ID de Sonnet 5.5 |
| 9 | [Anthropic — modèle et effort](https://claude.com/resources/articles/claude-model-and-effort-level-in-claude-code) (2026-07-07) | Modèle = capacité, effort = minutie ; fichier sauté → monter l'effort, erreur avec tout le contexte → monter le modèle | Lu | Noms de modèles antérieurs aux 5.5 | **Retenue** (règle de décision, §5) |
| 10 | Docs officielles : [model-config](https://code.claude.com/docs/en/model-config), [memory](https://code.claude.com/docs/en/memory), [skills](https://code.claude.com/docs/en/skills), [sub-agents](https://code.claude.com/docs/en/sub-agents), [hooks](https://code.claude.com/docs/en/hooks), [mcp](https://code.claude.com/docs/en/mcp), [settings](https://code.claude.com/docs/en/settings) + [costs](https://code.claude.com/docs/en/costs), [best-practices](https://code.claude.com/docs/en/best-practices), [debug-your-config](https://code.claude.com/docs/en/debug-your-config), [mods](https://code.claude.com/docs/en/plugins/mods/overview), [permissions](https://code.claude.com/docs/en/permissions) | Référence tranchante de cet audit | Lues en grande partie : model-config ~100 k/119 k car., sub-agents ~100 k/111 k, hooks ~200 k/273 k, mcp ~100 k/123 k, settings-reference ~100 k/425 k. **La section « tool search » de `mcp` n'a pas été lue** ; `costs` en donne le résultat (« outils MCP différés par défaut ») | Version installée 2.1.288 ; certaines fonctions citées exigent plus récent (`onFailure` des hooks : 2.1.295) | **Référence** |
| 11 | [MCP servers](https://github.com/modelcontextprotocol/servers), [README Memory](https://github.com/modelcontextprotocol/servers/blob/main/src/memory/README.md), [index.ts](https://github.com/modelcontextprotocol/servers/blob/main/src/memory/index.ts) | 9 outils, graphe JSONL, `MEMORY_FILE_PATH`, verrou de fichier, écriture atomique ; sous Windows `cmd /c npx` | Lu. Paquet npm `@modelcontextprotocol/server-memory` **1.0.0**, modifié le 2026-10-11 ✔ | « Basic implementation » ; pas d'authentification (stdio) ; `read_graph` renvoie tout le graphe ; aucune limite de taille ✔ | **Rejeté** (§6) |
| 13 | [MindStudio — learnings.md](https://www.mindstudio.ai/blog/self-learning-ai-skill-system-learnings-md-wrap-up) (2026-03-24) | Fichier d'apprentissages + `/wrap-up` ; risques : accumulation, contradictions | Lu. Incohérence interne (un hook `PostToolUse` présenté comme « fin de session »). Seuils « 200 entrées », « 30 min », « 90 % » ✖ | Promotion de produits maison | **Adaptée** : équivalent déjà en place, on codifie `/cloture` |
| 14 | [MindStudio — bonnes pratiques de skills](https://www.mindstudio.ai/blog/claude-skills-best-practices-update) (2026-10-02) | SKILL.md < 500 lignes, fichiers de référence liés sur un niveau, scripts figés pour les opérations fragiles | Lu. « < 500 lignes » ✔ confirmé par la doc officielle ; « sommaire au-delà de 100 lignes » ✖ ; « modèle visé dans le frontmatter » ✖ (`metadata` est **ignoré** par Claude Code) | Pas de source primaire | **Adaptée** |
| 15 | [Issue MCP #3315](https://github.com/modelcontextprotocol/servers/issues/3315) | Rapport automatique (AgentAudit) sur la v2026.1.26 : « traversée de chemin » moyenne, « fuite d'info » faible | Lue. **Fermée le 2026-09-03, « not planned »**, aucune PR ; le rapport admet que le paramètre est défini par l'opérateur | — | **Pas une vulnérabilité en soi.** Le code confirme l'absence de confinement du chemin (sans conséquence pour un serveur stdio local) |

**Conflit entre sources, tranché** : l'article 6 affirme qu'Explore tourne sur Haiku ; la doc officielle des
sous-agents dit qu'il hérite du modèle de la conversation. Dans les journaux, les 7 Explore lancés ont tous
tourné sur le modèle de la session (aucun `model` explicite) ✔ — cohérent avec la doc.

---

## 3. Inventaire initial

| Élément | Emplacement | État réel | Utilité | Action |
|---|---|---|---|---|
| Instructions | `CLAUDE.md` (6 178 o, 94 lignes) | Chargé à chaque session ; ~1,9 k tokens ◐ | Commandes, architecture, pièges | **Conserver**, 3 ajouts (§9) |
| Mémoire native | `~/.claude/projects/…/memory/` (5 fichiers, 7,3 ko) | Index de 517 o chargé ; contenu partiellement **périmé** (« tous les plugins sont désactivés » alors que `superpowers` est actif ; PR #35/#40 d'octobre) | Profil, direction produit, instance de dev | **Corriger** |
| Skills du projet | `.claude/skills/` : `adr`, `snapshot` (visibles), `new-feature`, `prompt-opus` (`disable-model-invocation`, absents de la liste du modèle ✔ doc) | Fonctionnels, 30 à 108 lignes (< 500) | Conserver | **Conserver** ; rafraîchir `prompt-opus/references/outils.md` (daté du 2026-10-03) |
| Agent | `.claude/agents/winui-reviewer.md` (outils : Read, Grep, Glob) | 7 lancements, tous sur le modèle de la session (aucun `model:`) ✔ | Relecture WinUI après chaque PR (« chaque PR a été relue », relais du 17) | **Conserver**, fixer `model`/`effort` |
| Hooks projet | `.claude/hooks/format-cs.js` (PostToolUse), `release-build.js` (Stop) | Lancés à vide : sortie 0 dans les deux cas ✔. Empreinte du code évite la boucle Stop ; `MSB3021/26/27` ignorés | Formatage ; build Release = CI | **Conserver** |
| Hooks globaux | `~/.claude/settings.json` : `SpaceNotch.exe --hook` sur 4 événements | **Échoue ouvert** : toute erreur renvoie 0, notch absente = rien (`ChannelCommand.cs:45-49`) ✔ | Fonction du produit (agents IA dans la notch) | **Conserver** |
| Permissions | `.claude/settings.json` : 22 règles `allow`, **aucun `deny`** | `Bash(git branch:*)` couvre `git branch -D` ✔ | — | **Corriger** (P0) |
| MCP du projet | `.mcp.json` : `microsoft-learn` (3 outils), `context7` (2), `cwm-roslyn-navigator` (20, v0.8.0, MIT, màj 2026-07-22) | Les 3 **connectés** dans cette session ✔ ; `find_symbol NotchMachine` → `NotchMachine.cs:144` ✔ | Docs Microsoft, docs de bibliothèques, navigation C# | **Conserver** |
| MCP utilisateur | `playwright` (portée locale de `~/.claude.json`) | **Désactivé** pour ce projet ✔ | Playwright ne pilote pas une app WinUI 3 (cf. §4) | **Supprimer** (propre) |
| Plugins | `superpowers` 6.4.1 (projet, **actif**) ; `claude-code-setup` (projet, désactivé) ; `ecc` 2.2.1 (utilisateur, désactivé) ; 94 entrées `@synced: false` | Les entrées `false` n'empêchent pas le chargement des skills synchronisés par claude.ai (`cowork-plugin-management:*` visibles ✔) | `superpowers` : 9 invocations dans les journaux ✔ | **Conserver** `superpowers` ; laisser le reste |
| Connecteurs claude.ai | Vercel (237 outils), Figma (42), Claude Docs (8), visualize (2) | Connectés ✔ ; sans usage dans les journaux (`visualize` 2 appels) | — | **Désactiver pour ce projet** (P4) |
| Installation | `claude doctor` : natif 2.1.288 + **reste d'une installation npm globale** ; mises à jour auto **désactivées** (`DISABLE_AUTOUPDATER`) | 1 avertissement ✔ | — | Désinstaller la copie npm (action globale, à toi) |
| CLI dans un terminal | `claude -p` : « OAuth session expired and could not be refreshed » ✔ | Le terminal n'est plus connecté (l'application de bureau, si) | — | `claude` puis `/login` quand tu veux le terminal |
| Reliquats ECC | `~/.claude/bash-commands.log` (728 ko), `cost-tracker.log` (757 ko), `settings.json.spacenotch.bak` | Dernière écriture le 3 octobre ; recherche de motifs de jetons : **0 occurrence** ✔ | Aucune | Supprimer (à toi) |
| CI | `.github/workflows/ci.yml` (Release + Debug + tests + audit NuGet + variantes signées), `audit.yml`, `release.yml`… | Actions épinglées par SHA ; `--no-restore` évité | Valide ce que fait le hook Stop | **Conserver** |
| Banc d'essai runtime | `tools/audit/audit.py` (pilotage souris/clavier, UIA, métriques) | Déjà utilisé en CI | Audit de comportement réel | **Conserver** |
| Workflow Git | `main` protégée (squash, CI verte, branche à jour) ; 66 branches locales | `docs/development/protection-de-main.md` | — | Élaguer les branches fusionnées (hors audit) |

---

## 4. MCP, plugins, tests d'une app native

| Candidat | Fonction réelle | Windows / sécurité / coût | Décision |
|---|---|---|---|
| `cwm-roslyn-navigator` 0.8.0 | Navigation sémantique C# (20 outils) | Fonctionne ✔ ; 11,2 k téléchargements, MIT, un seul mainteneur | **Garder.** Peu appelé (5 appels contre 215 `Read`), mais ce n'est pas un gaspillage (cf. §7) |
| `microsoft-learn` | Docs Microsoft officielles | HTTP, fonctionne ✔ (a servi à vérifier `winapp ui`) | **Garder** |
| `context7` | Docs de bibliothèques tierces (QRCoder, WinUIEx…) | Connecté ✔ ; non appelé dans cet audit | **Garder** (2 outils, coût négligeable) |
| MCP Memory | Graphe de connaissances JSONL | Pas d'auth, tout le graphe lu, 9 outils de plus | **Rejeter** (§6) |
| Mods tiers (YeonwooSung, hamzafer) | Panneaux, garde-fous, métriques | **Code non isolé, permissions de l'utilisateur** (doc officielle) ; Windows non documenté | **Rejeter**. Si un jour : `claude plugin validate <dossier>` liste ses crochets avant installation |
| Playwright MCP | Pilote un navigateur | **Inutilisable pour WinUI 3** : Microsoft ne le réserve qu'au contenu WebView2 (Microsoft Learn) | **Supprimer** la déclaration résiduelle |
| `computer-use` / navigateur intégré | Captures + clics sur le bureau | 65 + 43 appels dans les journaux ✔ : déjà utilisés pour piloter la vraie notch | **Garder**, à la demande |
| **`winapp ui`** (déjà installé, v0.6.1 ✔) | **UI Automation** : `inspect`, `search`, `invoke`, `screenshot`, `record`, `hover`, `send-keys` sur une vraie app WinUI 3 | Vérifié : `winapp.exe ui --help` répond ✔ ; la plupart des verbes sont sans injection d'entrée ; ceux qui injectent exigent un bureau déverrouillé | **À adopter dans `/snapshot`** (arbre UIA : noms accessibles, cibles de clic). **Non testé** sur la notch (elle ne tournait pas ; le lancement d'une instance exige que tu fermes la tienne, cf. mémoire `spacenotch-dev-instance`) |
| Appium + pilote Windows | WebDriver sur UIA | WinAppDriver n'est « plus maintenu » (Microsoft Learn) | Non nécessaire : `winapp ui` et `tools/audit` suffisent |
| MSTest UIA / tests WinUI dans le processus | Aperçu ; **ne prend pas en charge WinUI/MSIX** pour l'automatisation UIA ; l'hébergement MTP WinUI exige MSTest 4.5 en préversion | — | Hors périmètre (les tests du cœur tournent sans interface) |
| Claude Security (article 4) | Audit multi-agents annoncé | **Non vérifié** ✖ | `/security-review` et `/code-review` couvrent le besoin |

---

## 5. Politique de routage des modèles et de l'effort

**Faits de la doc officielle** ✔ : `opus` → Opus 5.5, `sonnet` → Sonnet 5.5, `haiku` → Haiku 5.5, `fable`
→ Fable 5.1 (disponible pour ton compte : `claude-fable-5-1[1m]` figure dans les options). Niveaux
`low`/`medium`/`high`/`xhigh`/`max` sur les modèles 5.5 ; **défaut `medium`** pour les 5.5 (`high` pour
Fable). `effortLevel` dans `settings.json` **n'accepte pas `max`** (confirme l'article 5). Le champ `effort`
d'un skill ou d'un agent surcharge la session, pas `CLAUDE_CODE_EFFORT_LEVEL`. Un `model` dans les réglages
du **projet** s'applique à chaque lancement (et à tous les contributeurs : à éviter dans un dépôt ouvert).

**Ce qui n'existe pas** : aucun routage automatique par tâche. Il y a des mécanismes précis, listés
ci-dessous ; le reste est une consigne que **tu** appliques avec `/model` et `/effort`.

| Tâche | Modèle | Effort | Mécanisme réel |
|---|---|---|---|
| Question, recherche locale, petite modification | `sonnet` (ou `haiku` pour une pure consultation) | `low`–`medium` | `/model`, `/effort` — manuel |
| Implémentation habituelle | `sonnet` | `medium` (défaut) | Rien à régler |
| Architecture, cycle de vie WinUI 3, animations | `opus` | `high` | `/model opusplan` : Opus pour planifier, Sonnet pour exécuter |
| Bug difficile ou intermittent | `opus` | `high` → `xhigh` après un échec | Règle de l'article 9 : si Claude **saute** un fichier ou un test, monter l'effort ; s'il **se trompe avec tout le contexte**, monter le modèle (`fable` après deux échecs) |
| Audit global, refactorisation, multi-couches | `opus` | `xhigh` | **Skill `/audit`** avec `model: opus`, `effort: xhigh` (automatique à l'invocation) |
| Revue indépendante | `opus` | `high` | **`winui-reviewer`** avec `model: opus`, `effort: high` (automatique) ; contexte neuf par construction |

**Mesuré** ✔ (10 sessions de 30 jours, journaux locaux) : Opus 5.5 porte **82 % des tours** (1 209 sur
1 480), Sonnet 5.5 6 %, Opus 5 12 %. Les 17 sous-agents ont tous hérité du modèle de la session. Ces
chiffres décrivent un usage Opus par défaut ; je n'en tire **aucun coût en dollars** (grille de prix de la
famille 5.5 non vérifiée).

**Validation par quatre tâches (non exécutée)** : le brief la demande. Je ne peux pas lancer de `claude -p`
imbriqué (le terminal n'est plus connecté) ni fixer l'effort d'un sous-agent sans que tu le demandes. La
procédure proposée est en §9 (P3). Seul le routage par frontmatter se contrôle après coup, dans le champ
`model` des réponses du journal de session.

---

## 6. Stratégie de mémoire

| Couche | Rôle | Verdict |
|---|---|---|
| Mémoire native (`memory/`) | Profil, préférences, état de la direction produit, pièges d'environnement | **Garder**, corriger les entrées périmées. Coût : index ~0,2 k tokens |
| `docs/` versionné (ADR, relais, feuille de route, audits) | Décisions, état des travaux, preuves | **Source de vérité du projet.** Une règle suffit : *une information vit à un seul endroit* |
| `/cloture` (à créer) | Rituel de fin de session déjà pratiqué (5 PR « chore(cloture) » en 6 jours) : relais, feuille de route, mémoire, PR | **Créer** — c'est la « boucle d'amélioration continue » demandée |
| MCP Memory | Graphe de connaissances parallèle | **Rejeter** |

**Pourquoi rejeter le MCP Memory** : (1) il double deux systèmes qui fonctionnent, donc il crée le risque
même que le brief veut éviter (la même information à deux endroits) ; (2) il stocke hors du dépôt, sans
historique ni revue, dans un JSONL réécrit en entier à chaque modification ✔ ; (3) `read_graph` renvoie
tout le graphe sans pagination ✔ ; (4) +9 outils et leurs instructions à chaque session ; (5) le paquet se
décrit comme une « implémentation basique ». **Ce n'est pas une affaire de sécurité** : l'issue #3315 est
fermée « not planned » et le code ne contient aucun défaut exploitable pour un serveur stdio local.

*Si tu le retenais malgré tout* : `{"memory":{"command":"cmd","args":["/c","npx","-y","@modelcontextprotocol/server-memory"],"env":{"MEMORY_FILE_PATH":"C:/Users/bleta/.claude/mcp-memory/spacenotch.jsonl"}}}`
(`cmd /c` requis sous Windows d'après le README ✔), puis tester la persistance après redémarrage, la
sauvegarde du fichier et le retrait avec `claude mcp remove memory`.

**Contenu autorisé en mémoire** : faits durables et vérifiés (commandes, causes racines, mesures, décisions),
avec date et PR. Pas de secrets, de logs complets ni de hypothèses non étiquetées.

---

## 7. Contexte et tokens

### Mesure du contexte fixe ✔

Outil `get_usage` de l'application, sur cette session (269 314 tokens utilisés, 27 % de 1 M) :

| Catégorie | Tokens | Part du fixe |
|---|---|---|
| Outils intégrés | 37 279 | 47 % |
| Outils MCP (noms différés) | 20 241 | 25 % |
| Prompt système | 9 401 | 12 % |
| Skills | 8 588 | 11 % |
| Instructions MCP | 4 390 | 5 % |
| **Total fixe** | **79 899** | 100 % (8 % de la fenêtre) |

Recoupé par les journaux : le **premier appel** des sessions fait 80 646 à 118 080 tokens ✔ (neuf sessions
du 19 septembre au 18 octobre, messages de départ de tailles variables ; les plus courts donnent ~80 k).
`CLAUDE.md` (~1,9 k) n'explique qu'une très petite part de ces valeurs.

### Où va le coût ✔

| Modèle | Tours | Cache lu (M tokens) | Contexte moyen par tour |
|---|---|---|---|
| Opus 5.5 | 1 209 | 523,3 | **433 k** |
| Opus 5 | 181 | 74,7 | 413 k |
| Sonnet 5.5 | 90 | 30,7 | 341 k |

La plus longue session (1 885 tours) finit à 633 k ; une autre atteint 705 k. La doc le dit : *« la session
entière est renvoyée à chaque requête »* (`costs`), et la compaction n'intervient qu'à ~967 k.

### Hypothèse infirmée ✔

*« Les lectures de `IslandWindow*.cs` gaspillent du contexte. »* — Non : **47 lectures, dont 43 avec
`offset`/`limit`, ~1 k tokens en moyenne, 48 k au total** sur toutes les sessions. `CLAUDE.md` est suivi.
Le faible usage de Roslyn (5 appels) ne se traduit donc pas par un surcoût mesurable. Je ne propose pas de
hook de blocage.

### Leviers, classés par gain attendu

| # | Levier | Mécanisme (✔ doc) | Gain | Preuve |
|---|---|---|---|---|
| 1 | **`/autocompact 400k`** (par modèle, réglage utilisateur) | `modelSettings.<modèle>.autoCompactWindow`, de 100 k à 1 M | Borne le contexte moyen et le cache lu | ◐ **estimé −30 à −40 %** du cache lu sur les sessions longues ; à mesurer en relançant `usage.js` après 2 semaines |
| 2 | **Instructions de compaction** dans `CLAUDE.md` | Section « Compact instructions » (doc `costs`) | Garde l'essentiel quand la compaction a lieu | Qualitatif |
| 3 | **Couper Vercel, Figma, Claude Docs pour ce projet** | Réglage de connecteurs de l'application | ◐ **~18 k tokens fixes** (289 noms d'outils à UUID, ~55 tokens chacun, sur les 20 241 mesurés) | Se confirme en relisant `get_usage` dans une session neuve |
| 4 | `syncClaudeAiSkills: false` (réglage utilisateur) | Présent dans l'index de `settings-reference` ; **détail non lu** ✖ | ◐ 4 à 5 k (27 skills `anthropic-skills:*`) | À tester ; perd docx/pdf/pptx, sans objet ici |
| 5 | Refuser un outil entier (`deny` sans spécificateur) | « Retire l'outil du contexte » (doc `permissions`) | Quelques k par outil | **Non retenu** : `Artifact` a été appelé 18 fois |
| 6 | Sous-agent `Explore` sur Haiku | Agent de projet nommé `Explore` avec `model: haiku` (doc) | Plus petit et plus rapide pour la recherche | Non mesuré ; 7 Explore en 30 jours |
| 7 | `/clear` entre tâches, `/btw`, sous-agents pour l'enquête | Doc `best-practices` | — | Discipline, pas de configuration |

**Une leçon de cet audit même** ✔ : les quatre pages demandées en `.md` (`costs`, `best-practices`, `mods`,
`debug-your-config`) sont revenues **en texte brut dans la conversation** (~15 à 20 k tokens chacune),
alors que les pages sans `.md` revenaient résumées ou rangées dans un fichier (lisible par `Grep`). Le
contexte de cette session est passé de 97 k à 269 k, dont 187 k de messages. Constat sur cet audit, cause
non vérifiée : pour la documentation, éviter le suffixe `.md` et poser une question précise.

---

## 8. Architecture cible

```text
CLAUDE.md                      94 → ~105 lignes : + compaction, + routage (3 lignes), + /cloture
.claude/settings.json          permissions resserrées ; hooks inchangés + SessionStart
.claude/hooks/                 format-cs.js, release-build.js (inchangés) + session-start.js
.claude/agents/                winui-reviewer (model: opus, effort: high) — pas d'autre agent
.claude/skills/                adr, snapshot (+ étape UIA), new-feature, prompt-opus (outils.md rafraîchi)
                               + cloture, audit (model/effort au frontmatter)
docs/audit/                    rapports d'audit (celui-ci inclus) — pas de nouveau répertoire
mémoire native                 5 fichiers corrigés + 1 (mesures de cet audit)
```

Décisions de structure :

- **Pas de `.claude/rules/`** : les règles à chargement par chemin (`paths:`) ne sont utiles que si
  `CLAUDE.md` est lourd ; ici il pèse ~1,9 k tokens. Coût de la complexité > gain.
- **Pas de nouveaux agents** (UX, performance, architecture) : `winui-reviewer` couvre le cycle de vie, les
  fils, la géométrie et l'accessibilité ; `/code-review` couvre le C# général ; la revue contradictoire
  vient de `winui-reviewer` en contexte neuf. Un agent de plus ferait doublon.
- **Pas de skills pour le débogage, la validation avant commit, la revue indépendante** :
  `superpowers:systematic-debugging`, `verification-before-completion`, le hook Stop + la CI, et
  `/code-review` existent déjà.
- **Pas de worktrees** : décision déjà prise (`CLAUDE.md`), et elle tient — le restore WinUI est lourd et
  le verrou d'instance de la notch rend deux builds en parallèle fragiles.

---

## 9. Plan d'implémentation (rien n'est appliqué)

| Pr. | Changement | Fichiers | Risque | Temps | Critère de validation |
|---|---|---|---|---|---|
| **P0** | Remplacer `Bash(git branch:*)` par des formes en lecture seule (`--list`, `-a`, `-r`, `-vv`, `--show-current`, `--contains`, `--merged`) ; ajouter `deny` : `git push` avec `--force`/`-f`, `gh pr merge --admin`, `gh pr merge --auto` | `.claude/settings.json` | Faible. Les règles `Bash` ne sont pas une garantie dure (doc) | 15 min | `git branch -D x` redemande ; `git push --force --dry-run` est refusé (sans effet distant) |
| **P1** | `CLAUDE.md` : « Compact instructions » (3 lignes), routage (3 lignes), `/cloture` (1 ligne) | `CLAUDE.md` | Aucun | 15 min | Relire ; ≤ 110 lignes |
| **P1** | Corriger la mémoire native (superpowers actif ; PR périmées) et y consigner les mesures de cet audit | `memory/*.md` | Aucun | 10 min | Index à jour |
| **P1** | Rafraîchir l'inventaire de `outils.md` (procédure déjà écrite dans le fichier) | `.claude/skills/prompt-opus/references/outils.md` | Aucun | 20 min | Date du jour, noms vérifiés par `ToolSearch` |
| **P2** | `winui-reviewer` : `model: opus`, `effort: high` | `.claude/agents/winui-reviewer.md` | Coût d'Opus par relecture | 5 min | Champ `model` du journal sur la prochaine relecture |
| **P2** | Skill `/cloture` (`disable-model-invocation`, `model: sonnet`) : PR fusionnées depuis le dernier relais → relais, feuille de route, mémoire, PR `chore(cloture)` | `.claude/skills/cloture/SKILL.md` | Écrit dans `docs/` : demande confirmation avant le commit | 40 min | Essai à blanc sur la clôture du 17 : même structure que `relais-2026-10-17.md` |
| **P2** | Skill `/audit <périmètre>` (`model: opus`, `effort: xhigh`, `context: fork`, outils en lecture) : gabarit et échelle de gravité de `docs/audit/2026-10-audit.md`, constats graves revérifiés dans le code, aucune modification | `.claude/skills/audit/SKILL.md` | Long ; `context: fork` garde la session principale propre | 40 min | Audit d'une fonctionnalité réelle ; `model` du journal = Opus |
| **P2** | Hook `SessionStart` : `git fetch`, retard/avance, branche, nombre de fichiers modifiés, dernier relais (~120 tokens) | `.claude/hooks/session-start.js`, `settings.json` | `git fetch` réseau (délai de 10 s) ; échoue ouvert | 25 min | Sortie visible en début de session ; sans réseau, aucune erreur |
| **P3** | Étape `winapp ui inspect/screenshot` dans `/snapshot` (noms accessibles, cibles ≥ 32 DIP) | `.claude/skills/snapshot/SKILL.md` | Exige que tu fermes ta notch (instance unique) | 30 min | Arbre UIA lu sur une notch en marche |
| **P3** | Validation du routage : 4 tâches (simple, courante, analyse WinUI, bogue) avec `model` explicite par sous-agent | — | Coût en quota (Pro : 6 % sur 5 h, 5 % sur la semaine aujourd'hui) | 60 min | Tableau modèle / durée / reprises / résultat |
| **P4** | `/autocompact 400k` ; couper Vercel/Figma/Claude Docs ; (option) `syncClaudeAiSkills: false` | Réglages **utilisateur** / application | Changement global, réversible | 10 min | `get_usage` avant/après dans une session neuve |
| **P4** | Désinstaller la copie npm de Claude Code ; supprimer les reliquats ECC et le MCP `playwright` | Machine | Global | 10 min | `claude doctor` : 0 avertissement |
| **P4** | Commiter `usage.js` (mesure du contexte et des outils) dans `tools/` | `tools/claude-metrics/` | Lit des journaux personnels : à garder hors du dépôt si tu préfères | 15 min | Même sortie que ce rapport |

Ordre recommandé : **P0 → P1 → P2 → P3 → P4**. Les lignes P4 touchent la machine et l'application : elles
ne se font qu'avec ton accord ligne par ligne.

---

## 10. Validations exécutées

| Commande ou appel | Résultat |
|---|---|
| `claude --version`, `claude doctor` | 2.1.288 natif ; 1 avertissement (copie npm) ; mises à jour auto désactivées |
| `claude mcp list` (terminal) | 3 serveurs « Pending approval » (approbation propre à la CLI) ; `playwright` désactivé |
| `session_connectors_status` | `microsoft-learn`, `context7`, `cwm-roslyn-navigator` **connectés** ; 4 connecteurs claude.ai connectés |
| `find_symbol NotchMachine` (Roslyn) | `NotchMachine.cs:144` ✔ |
| `microsoft_docs_search` | Réponse utile (a révélé `winapp ui`) ✔ |
| `winapp.exe ui --help` (0.6.1) | Les commandes UIA existent ✔ |
| `release-build.js` et `format-cs.js` lancés à vide | Sortie 0 dans les deux cas ✔ |
| `git fetch`, `git rev-list --left-right --count origin/main...HEAD` | 0 / 0 ✔ |
| `npm view @modelcontextprotocol/server-memory` | 1.0.0, modifié le 2026-10-11 ✔ |
| `get_usage` + scripts d'analyse des journaux | §7 ✔ |
| `claude -p` (mesure imbriquée du contexte) | **Échec** : session OAuth du terminal expirée. Mesure faite autrement (§7) |
| Build / `dotnet test` | **Non lancés** : aucun fichier de code n'a changé |

**Non validé, avec la raison et la reproduction** :

- *Routage par frontmatter* : exige un skill ou un agent modifié. Reproduction : invoquer, puis lire le
  champ `model` des réponses dans `~/.claude/projects/…/<session>.jsonl`.
- *Effet des connecteurs coupés* : à mesurer dans une session neuve, sinon la modification d'outils
  invalide le cache de cette session (~270 k tokens à ré-écrire). Reproduction : `get_usage` au premier
  tour, avant et après.
- *`winapp ui` sur la notch* : exige la fermeture de ta notch installée.
- *Règles `deny`* : à tester après application avec des commandes sans effet (`--dry-run`).

---

## 11. Problèmes restants et limites

- Les 10 sessions mesurées (19 septembre – 18 octobre) ne représentent que ce que le nettoyage de 30 jours
  a conservé. Les moyennes sont celles de **tes** sessions longues sur Opus, pas d'un usage type.
- Le contexte fixe (§7) est mesuré en bloc : la part des connecteurs (~18 k) est une **estimation
  recoupée**, pas une mesure directe.
- **Correction (second relevé)** : la CLI native est passée de 2.1.288 à **2.1.302** pendant la session, donc
  l'affirmation « version figée » du premier relevé était fausse. `onFailure` des hooks (2.1.295) est
  désormais disponible côté CLI. L'application de bureau embarque sa propre version, non lue ici.
- La source 7 reste non lue. Aucune décision n'en dépend.
- Les pages `hooks` (« Debug hooks »), `mcp` (« tool search ») et `settings-reference` (au-delà de l'index)
  n'ont pas été lues en entier.

## 12. Gains

| Gain | Nature | Valeur |
|---|---|---|
| Contexte fixe actuel | **Mesuré** | 79 899 tokens (8 % de 1 M) |
| Part du dépôt dans ce fixe | Estimé | 2 à 3 k tokens |
| Couper les 3 connecteurs inutiles | **Estimé** | ~18 k tokens (−22 % du fixe) |
| `syncClaudeAiSkills: false` | **Estimé** | 4 à 5 k tokens |
| `/autocompact 400k` | **Estimé** | −30 à −40 % de cache lu sur les sessions longues |
| `git branch -D` sans demande | **Mesuré** (défaut) | Corrigé par P0 |
| Lectures de `IslandWindow*.cs` | **Mesuré** | Aucun gain à chercher (48 k tokens au total) |
| Mods tiers, MCP Memory | — | Aucun gain démontré, coût et risque certains : rejetés |

## 13. Maintenance

- **À chaque mise à jour de Claude Code** : `claude doctor` ; relire `model-config` si un modèle sort ;
  rafraîchir `prompt-opus/references/outils.md`.
- **Tous les mois** : relancer l'analyse des journaux (contexte moyen par tour, modèles, outils) ; élaguer
  la mémoire native (skill `consolidate-memory`) ; vérifier qu'aucun fait n'existe à deux endroits.
- **À chaque nouvelle fonctionnalité de l'application** : le skill `/new-feature` s'en charge ; aucune
  action sur l'environnement.
- **Avant d'installer quoi que ce soit** : mesurer le contexte fixe avant/après dans une session neuve
  (`get_usage`) ; ne garder que ce qui justifie ses tokens.

---

## 14. Application de P0 à P2 (2026-10-18, rien de commité)

Autorisation : « applique P0, P1 et P2 […] ne touche ni aux réglages utilisateur ni à l'application (P4).
Ne commite rien sans me le redemander. » Quatre fichiers suivis modifiés (+77 −16), quatre fichiers créés.

| Pr. | Fichier | Ce qui a changé |
|---|---|---|
| P0 | `.claude/settings.json` | `Bash(git branch:*)` remplacé par huit formes en lecture seule (`git branch`, `--list`, `--show-current`, `--contains`, `--merged`, `-a`, `-r`, `-vv`). Nouveau bloc **`ask`** (demande même en mode auto, d'après la doc) : `git push` avec `--force` ou `-f`, `git reset --hard`, `git clean`, `git restore`, `git checkout --`, `git branch -D`, `gh pr merge --auto`. Nouveau bloc **`deny`** : `gh pr merge --admin`, lecture de `~/.claude/.credentials.json`, `*.pfx`, `*.p12` |
| P1 | `CLAUDE.md` (94 → 111 lignes) | Section « Modèle, effort et contexte » (politique du §5 en quatre lignes), section « Compact instructions », et une ligne pour `/audit` et `/cloture` |
| P1 | `.claude/skills/prompt-opus/references/outils.md` | Inventaire refait le 2026-10-18 (skills synchronisés, connecteurs claude.ai, MCP, agents, skills `audit` et `cloture`) |
| P1 | mémoire native | `spacenotch-claude-tooling.md` corrigé (superpowers actif) ; `spacenotch-claude-env-audit.md` ajouté ; index à jour |
| P2 | `.claude/agents/winui-reviewer.md` | `model: opus`, `effort: high` |
| P2 | `.claude/skills/cloture/SKILL.md` (nouveau, 60 lignes) | `disable-model-invocation`, `model: sonnet`, `effort: medium`. Collecte (PR fusionnées depuis le dernier relais), relais au gabarit de `relais-2026-10-17.md`, feuille de route, mémoire, puis **demande avant** branche, commit et PR |
| P2 | `.claude/skills/audit/SKILL.md` (nouveau, 74 lignes) | `disable-model-invocation`, `model: opus`, `effort: xhigh`, `context: fork`, `background: false`. Lecture seule ; dimensions du projet ; constats graves revérifiés ; rapport au gabarit de `2026-10-audit.md` |
| P2 | `.claude/hooks/session-start.js` (nouveau) + `settings.json` | Événement `SessionStart` (`startup`, `resume`, `clear`) : branche, retard/avance sur `origin/main` après `git fetch` (8 s au plus), fichiers modifiés, dernier relais. Se tait sur toute erreur |

### Vérifications faites

| Vérification | Résultat |
|---|---|
| `settings.json` après chaque modification | JSON valide ; 28 règles `allow`, 10 `ask`, 4 `deny` |
| `gh pr merge 999999 --squash --admin` (PR inexistante) | **Refusé** par la règle `deny`, y compris derrière un `cd … &&` ✔ |
| `git branch --list 'chore/cloture*'` | Passe sans demande ✔ |
| `session-start.js` dans le dépôt | Sortie : branche `main`, 0 retard, 0 avance, 9 fichiers modifiés ou non suivis, dernier relais `relais-2026-10-17.md` ; **1,4 s**, exit 0 ✔ |
| `session-start.js` avec un chemin à la Git Bash et hors dépôt | Même sortie ; silence et exit 0 hors dépôt ✔ |
| Ordre des relais (logique du hook) | `…-12 < -12-soir < -12-nuit < -13 < -13-soir` ✔ |
| Frontmatter de l'agent et des deux skills | Champs lus, pas de `: ` parasite dans les valeurs ✔ (relecture) |
| Longueur des skills | 60 et 74 lignes (limite officielle : 500) ✔ |

### Non vérifié, et comment le faire

- **Règles `ask`** : elles demandent même en mode auto (doc). Essayées ensuite (§16.2) : l'auteur a vu une
  demande pour le force-push de test et pour `git branch -D` ; la source exacte de la demande (règle ou mode
  `auto`) n'est pas isolée.
- **Règle `Read(~/.claude/.credentials.json)`** : syntaxe `~/` conforme à la doc ; non testée, car le seul
  vrai test consiste à lire le secret.
- **Chargement du hook `SessionStart`, des skills `/cloture` et `/audit`, du `model` de l'agent** : ils
  ne se chargent qu'à l'ouverture d'une **nouvelle session**. À constater dans la prochaine : la ligne
  « État du dépôt au démarrage » en tête ; `/cloture` et `/audit` dans la liste des skills ; pour le
  routage, le champ `model` des réponses dans `~/.claude/projects/…/<session>.jsonl` après un `/audit`
  (attendu : `claude-opus-5-5`) et une relecture de `winui-reviewer`.
- **`/cloture` sur la clôture du 17** (essai à blanc prévu) : non lancé, car il écrit dans `docs/`.
- **Build et tests** : non lancés. Aucun fichier `.cs`, `.xaml`, `.csproj` ni `.props` n'a changé ; le hook
  Stop (`release-build.js`) ne se déclenche donc pas.

### Écarts avec le plan du §9

- Le plan prévoyait `deny` pour `--auto` ; il est passé en **`ask`** : l'activer peut devenir légitime un
  jour, et un `deny` ne se contourne pas sans éditer le fichier.
- `CLAUDE.md` fait 111 lignes (cible : 110). Toujours bien sous les 200 de la doc.
- Aucun nouveau `deny` sur `git push --force` : un `deny` empêcherait aussi un `--force-with-lease`
  légitime après une réécriture de branche ; `ask` le laisse à ton choix.

### Reste à faire (hors accord actuel)

P3 (étape `winapp ui` dans `/snapshot`, validation du routage sur quatre tâches) et P4 (`/autocompact 400k`,
connecteurs Vercel/Figma/Claude Docs, `syncClaudeAiSkills`, copie npm de Claude Code, reliquats ECC,
`playwright`, `usage.js`) : à valider séparément. Les modifications ci-dessus ne sont pas commitées.

---

## 15. Suite : P3 et P4 (2026-10-18, après ton accord)

Autorisation : les quatre options (étape `winapp ui`, validation du routage, réglages utilisateur,
nettoyage machine). Une seule action de ta liste n'a **pas** été faite, avec sa raison (§15.3).

### 15.1 Validation du routage — cinq sous-agents ✔

Le paramètre `model` et `effort` de l'outil Agent a été honoré : le modèle réel vient du journal de chaque
sous-agent (`subagents/agent-*.jsonl`), pas de la demande. Les tâches étaient en lecture seule sur le dépôt.

| Tâche | Demandé | Réel (journal) | Durée | Appels d'outils | Résultat contrôlé |
|---|---|---|---|---|---|
| T1 recherche simple : où est `ChannelPipe` et ses membres | `haiku`, `low` | `claude-haiku-5-5` | 8,8 s | 2 | **Exact** : `ChannelPipe.cs:23`, `Name` :26, `SendAsync` :38 (vérité terrain par `grep`) |
| T2 implémentation courante : tests xUnit de `ClaudeHook` | `sonnet`, `medium` | `claude-sonnet-5-5` | 26,0 s | 6 | **Compile et passe** : copié dans `tests/SpaceNotch.Core.Tests/`, `dotnet test --filter` → **12 réussis sur 12**, puis copie retirée |
| T3 analyse du cycle de vie WinUI : tous les `FrameClock.Rendering +=` | `opus`, `high` | `claude-opus-5-5` | 129,6 s | 24 | **7 sites sur 7** (`grep` : 7 `+=`). Mécanisme commun **confirmé dans le code** (§15.2) |
| T4a recherche de bogues, fichier à 2 défauts semés | `opus`, `high` | `claude-opus-5-5` | 27,7 s | 2 | **2 sur 2** (`DecisionSeconds` 55 → 65 ; délai de 60 s donné à `Stop` au lieu de `PermissionRequest`) |
| T4b même tâche | `sonnet`, `medium` | `claude-sonnet-5-5` | 20,9 s | 2 | **2 sur 2**, mêmes conséquences décrites |

Lecture, sans sur-interprétation :

- **Sur un fichier de 239 lignes à deux défauts, Sonnet moyen égale Opus élevé** (2/2 chacun, 20,9 s contre
  27,7 s). Un seul essai par cas : aucune portée statistique. La règle « Opus pour un bogue difficile » reste
  une prudence pour les bogues qui traversent plusieurs fichiers ; elle n'est **pas démontrée nécessaire** à
  cette échelle.
- **T3 est la seule tâche qui a justifié du temps et de l'effort** : 24 appels, 130 s, 3,1 M tokens de cache
  lus, et elle a trouvé quelque chose de réel (§15.2). Aucun comparateur moins cher n'a été lancé : je ne peux
  pas dire si Sonnet l'aurait trouvé.
- **Un sous-agent coûte environ 70 k tokens dès son premier tour** (T1 : 76 k tokens pour 3 appels d'outils).
  C'est le coût fixe mesuré en §7, payé à chaque délégation : déléguer une recherche d'une minute coûte plus
  qu'elle ne fait économiser. Ça confirme la règle de `CLAUDE.md` et de `outils.md` (ne déléguer que les pistes
  grandes et indépendantes).
- **Non vérifié** : le routage par `model:` au frontmatter d'un **skill** (`/audit`, `/cloture`) et celui de
  `winui-reviewer`. Seul le paramètre d'appel de l'outil Agent est démontré.

### 15.2 Défaut du produit trouvé par T3, vérifié dans le code (non corrigé)

`FrameFanOut.Fail` retire un abonné qui lève une exception (`src/SpaceNotch.Core/Animation/FrameFanOut.cs:113-117`).
Le seul rappel d'erreur branché, `FrameClock.cs:27`, **journalise seulement**. Les propriétaires gardent leur
drapeau à vrai (`_isRunning` du ressort, `_detachFramesHooked`, `_fxRunning`, `_running` de `ValueRoller`,
`_tintRunning`) : leur prochain démarrage sort tôt et ne se réabonne jamais.

- **Établi** : le mécanisme (code relu ici).
- **Non établi** : qu'une exception se produise en pratique (dans `ApplyGeometry`, `OnDetachFrame`…) et la
  gravité « bloquant » annoncée par le sous-agent pour le ressort. Je n'ai rien reproduit.
- Correction suggérée par T3 : un rappel vers le propriétaire dans `Fail`, ou un `try/finally` qui remet le
  drapeau dans chaque gestionnaire. Hors périmètre de cette PR : à inscrire dans la feuille de route
  (`/cloture`) si tu veux la suivre.

### 15.3 Réglages utilisateur (P4)

| Réglage | Fait ? | Détail |
|---|---|---|
| `autoCompactWindow: 400000` dans `~/.claude/settings.json` | **Oui** | JSON valide ✔ ; `claude doctor` ne signale aucun réglage invalide ✔. Effet **non mesuré** (il faut une session longue). Retour arrière : supprimer la ligne, ou `/autocompact auto` |
| `syncClaudeAiSkills: false` | **Non** | Les skills synchronisés comprennent `built-in-browser`, `chrome-browser` et `computer-use`, que le harnais impose de lire avant d'employer le navigateur intégré, Chrome ou le contrôle de l'ordinateur — 65 + 43 appels dans tes journaux. Les couper retirerait ces consignes de sécurité pour ~4 à 5 k tokens (estimé). À ta décision |
| Connecteurs Vercel, Figma, Claude Docs | **Non (à toi)** | Se coupent dans l'application ; ~18 k tokens (estimé). À mesurer avec `get_usage` dans une session neuve |

### 15.4 Nettoyage de la machine (P4) ✔

- **Copie npm de Claude Code** (2.1.274) désinstallée : `claude` pointe sur le natif 2.1.302 ;
  `claude doctor` : **« No installation issues found »**. Les autres paquets npm globaux n'ont pas été touchés.
- **Reliquats ECC déplacés, pas supprimés** : `bash-commands.log`, `cost-tracker.log`,
  `settings.json.spacenotch.bak` → `~/.claude/backups/reliquats-2026-10-18/` (1,5 Mo). Tu peux supprimer ce
  dossier quand tu veux.
- **MCP `playwright`** retiré de la portée locale du projet (`claude mcp remove playwright -s local`), après
  une copie de `~/.claude.json` dans le même dossier de sauvegarde. Vérifié : `claude mcp get playwright` ne
  le trouve plus ; il reste `context7`, `cwm-roslyn-navigator`, `microsoft-learn` ; `~/.claude.json` reste valide.

### 15.5 `winapp ui` sur la vraie notch (P3) ✔

Build Release (0 avertissement, 0 erreur, 38,6 s) → `winapp run … --args "--demo" --detach` (aucune notch ne
tournait) → `ui status`, `ui inspect --depth 4`, `ui get-property`, `ui screenshot`. Instance fermée ensuite
(`Stop-Process` sur son seul PID).

- L'arbre existe : `IslandBody` (groupe) porte un `Name` qui suit le contenu (« Alex, Discord », puis
  « AirPods Pro, Connecté »), avec `AutomationId`, rectangle `1113,0,334,78` et `IsKeyboardFocusable`.
- `inspect -i` : **0 élément interactif** dans cet état (repos de démonstration) : ce n'est pas une erreur.
- Deux fenêtres : « SpaceNotch » et « SpaceNotch Atmosphere » (le `-w` désigne la première).
- `/snapshot` a reçu l'étape UIA correspondante (commandes essayées, critères, interdiction d'injecter des
  entrées sans accord).
- **Non essayé** : `click`, `hover`, `send-keys` (ils déplacent ta souris), les scènes ouvertes, la fenêtre
  des Réglages.

### 15.6 Autres vérifications

| Vérification | Résultat |
|---|---|
| `dotnet build SpaceNotch.sln -c Release -p:Platform=x64` | **0 avertissement, 0 erreur** |
| `dotnet test SpaceNotch.sln -c Release` | **1 037 + 23 réussis, 0 échec** (cœur et greffon d'exemple) |
| `claude doctor` | « No installation issues found » (CLI native 2.1.302) |

### 15.7 État du dépôt

Fichier suivi modifié depuis la PR : `.claude/skills/snapshot/SKILL.md` (étape UIA). Ce §15 est lui aussi
nouveau dans le rapport. **Rien de cela n'est commité** : la PR #100 contient encore le contenu du §14.

---

## 16. Suite : essai de `/cloture`, règles `ask`, élagage des branches, FrameFanOut

### 16.1 Essai à blanc de `/cloture` sur la clôture du 17 (étape 1, lecture seule)

La commande de collecte du skill (`gh pr list --state merged --search "merged:>=2026-10-15" --json …`) renvoie
**19 PR, de #81 à #99**. Comparée à `docs/plans/relais-2026-10-17.md` :

- 16 des PR renvoyées sont citées dans le relais ;
- #83 et #84 n'y sont pas citées une à une (le relais écrit « #82 à #85 ») et #99 est la clôture elle-même,
  fusionnée après le relais : ce sont des écarts attendus ;
- #63, #71 et #72 sont citées par le relais mais la requête ne les renvoie pas (fusionnées avant le 15, ou
  non fusionnées : non vérifié).

Les sections du skill couvrent celles du relais du 17 (État, Fait, Relectures, Décisions de l'auteur,
Découvert en route, Non vérifié, Configuration de l'auteur, Décisions de cette session, Reste à faire), sauf
« Prochain brief », renvoyé à `/prompt-opus` à dessein. **Non joué** : l'écriture du relais (étape 2), dont les
preuves viennent de la conversation de l'époque, que je n'ai pas ; rien n'a été écrit dans `docs/`. Verdict :
la collecte est fiable ; le reste se constatera à la prochaine vraie clôture.

### 16.2 Règles `ask` — **constatées par l'auteur**

`git push --force --dry-run origin refs/heads/inexistante-test:…` n'a **pas été bloqué** pour moi : git a répondu
lui-même « src refspec … does not match any ». Deux explications : l'auteur a accepté une demande de
confirmation, ou la règle ne s'est pas déclenchée. La session est en mode `auto`, où la doc dit que les règles
`ask` demandent quand même. La règle `deny` voisine (`gh pr merge *--admin*`, même forme de motif) a bien
refusé, donc le motif est bon.

**Tranché par l'auteur** (2026-10-18) : il a vu une demande de confirmation pour le force-push de test **et**
pour le `git branch -D` de l'élagage. Les deux règles ont donc demandé, y compris en mode `auto`. Limites : c'est
un constat de l'auteur, pas une trace que je lis ; et rien ne distingue ici une demande venue de la règle `ask`
d'une demande du mode `auto` lui-même. Pour lever ce doute, il faudrait refaire l'essai en retirant la règle.

### 16.3 Élagage des branches locales ✔

67 branches locales avant, **10 après** : 57 supprimées. Critère : une PR fusionnée du même nom (par `gh`, car
le dépôt fusionne en squash et `git branch --merged` n'en verrait presque aucune) **et** un commit local
identique à la tête de la PR (42 branches) ou ancêtre de celle-ci (15 branches en retard sur elle).

- **Sauvegarde** : `~/.claude/backups/reliquats-2026-10-18/branches-locales-supprimees.txt` (nom et commit de
  chacune). Pour en recréer une : `git branch <nom> <commit>`.
- **Gardées** : `main`, `chore/claude-env-2026-10-18` (PR #100), `fix/setup-uninstall-launch` (aucune PR
  fusionnée de ce nom) et **sept branches dont la PR est fusionnée mais qui portent des commits locaux absents
  de la PR** : `chore/tutoiement`, `feat/installeur-signable`, `feat/me-prevenir-par-defaut`,
  `feat/raccourci-notch-visible`, `fix/a11y-boucles-menu`, `fix/minuteur-reprendre`, `fix/narrateur-reglages`.
  Travail non poussé ou historique réécrit : à regarder avant de les supprimer.
- Rien n'a été touché sur GitHub ; les 68 branches distantes sont intactes.

### 16.4 Défaut `FrameFanOut` (§15.2)

Confié à une session à part, « FrameFanOut : abonné fautif jamais réabonné » : reproduire par un test du
cœur, choisir le correctif, PR séparée depuis `main`, sans fusion. Résultat : PR #101 ouverte (un commit,
`FrameFanOut.Add(handler, onFailed)` et `FrameClock.Subscribe`, cinq propriétaires corrigés). Le diff a été
relu ici et correspond au mécanisme du §15.2 ; les tests (1043 + 23) et le build Release sont ceux **que la
session rapporte**, non relancés ici ; sa CI était en attente et rien n'a été vérifié à l'écran.

---

## 17. Révision du plan après tes demandes d'origine (2026-10-18)

Tu m'as remis la conversation qui a produit le brief. Je l'ai relue contre ce rapport et l'état réel du
dépôt. Les publicités qu'elle contient (Nango, Thunders AI, Xopero) ne sont pas des consignes ; seule la
partie « self-healing » est évaluée plus bas, parce que tu avais demandé de l'ajouter.

### 17.1 Écarts entre ta demande et ce rapport

| Ta demande d'origine | État avant | Maintenant |
|---|---|---|
| Évaluer les mods hamzafer et YeonwooSung **un par un** (fonction, compatibilité, dépendances, chevauchement, essai, désinstallation) | Rejet en bloc (ligne 3 du §2) | Tableau §17.2 |
| Règle de routage explicite **Haiku/Low · Sonnet/High pour l'architecture et les animations WinUI** | `CLAUDE.md` envoyait l'architecture sur Opus/high | Aligné sur ta règle après un essai comparateur (§17.3) |
| Configurer un MCP ou plugin de **mémoire** persistante | Rejeté sur lecture du README et du code, jamais essayé | **Essayé puis retiré** : critère « sans doublon » non rempli (§17.4) |
| Étapes de mise en œuvre sous PowerShell et retour arrière | Retours arrière dispersés | Tableau §17.5 |
| Règles de preuve : test non lancé ≠ réussi, pas de FPS sans mesure, compte rendu de fin de tâche | Seulement la règle « critère d'acceptation » | 4 lignes ajoutées à `CLAUDE.md` |
| Audit : statut démontré / probable / hypothèse ; DPI, écrans multiples, plein écran, réduction des animations ; bilan du setup | Absents de `/audit` | Ajoutés à `/audit` |
| Boucle d'amélioration : piège récurrent → proposition ciblée à valider ; la mémoire ne touche jamais aux permissions | Partielle | Ajouté à `/cloture` |
| Audit de référence avec le nouveau setup (étape 10 de ta conversation) | `/audit` jamais lancé en vrai | À lancer par toi, §17.6 |
| Tests « self-healing » (annonce Thunders AI) | Non évalué | **Écarté** : 40 exécutions sur 40 réussies sur `main` et **0 échec** sur les 60 dernières exécutions du workflow CI (toutes branches, `gh run list`) ; les tests du cœur n'ont pas d'interface. Aucune instabilité à réparer. Produit commercial non évalué |

### 17.2 Les mods, un par un

Faits communs, vérifiés : les deux dépôts sont récents et actifs (hamzafer : MIT, 268 ★, poussé le
2026-10-16 ; YeonwooSung : Apache-2.0, 982 ★, poussé le 2026-10-17) ; un mod est du code tiers qui tourne
avec **tes** permissions ; les workflows de CI des deux dépôts ne tournent que sur `ubuntu-latest` : **leur CI
ne teste pas Windows**. Les deux exigent Claude Code ≥ 2.1.287 (le tien : 2.1.302). Le bloc d'installation de
YeonwooSung est écrit « macOS/Linux, Bash ou Zsh ». **Rien n'a été installé.**

| Mod | Ce qu'il fait (README) | Problème ou chevauchement | Verdict |
|---|---|---|---|
| hamzafer · **context-bar** | Barre empilée du contexte, calculée localement, sans appel d'API | Même découpage que `/context` et `get_usage` (déjà mesuré : 79 899) ; aucun gain de tokens attendu | **Facultatif**, premier à essayer si tu veux une jauge permanente |
| hamzafer · token-weather, cache-clock, usage-meter | Remplissage, refroidissement du cache, usage du forfait | Doublon de `/usage` et `get_usage` ; `cache-clock` vit sous une ligne d'état (terminal), non vérifié dans l'application | Écartés |
| hamzafer · blast-radius | Retient `rm -r`, les force-push et les migrations, annule après 60 s | Doublon de P0 (règles `ask` : force-push, `reset --hard`, `clean`, `restore`, `checkout --`, `branch -D`) sans code tiers | Écarté |
| hamzafer · review-watch | Lignes en direct pour les revues Codex ou sous-agent | Écrit pour macOS et Linux (« macOS and Linux have both » `ps` et `tail`) ; nos revues passent par `winui-reviewer` et le suivi de PR de l'application | Écarté |
| hamzafer · md-preview | Markdown rendu comme GitHub | Chrome + terminal à images, « testé sur macOS » ; **envoie le texte de chaque .md édité à l'API GitHub** | Écarté |
| hamzafer · mission-control | Carte des agents et du code | Vue du code **macOS seulement** | Écarté |
| hamzafer · switchboard | Choisit le modèle de chaque sous-agent via l'API OpenAI ou Jev | **Envoie l'étiquette de chaque sous-agent à un tiers**, clé requise (`off` par défaut) ; ton routage est déjà déterministe et gratuit (frontmatter) | Écarté |
| hamzafer · where-am-i, next-steps, replay-theater, agent-radar, heatmap | Confort d'affichage | Aucun besoin identifié | Écartés |
| hamzafer · glance, merge-gate, rulebook-guard, session-saver | Réglages personnels de l'auteur (Slack, Linear, Codex CLI) | Hors sujet ; la protection de `main` (CI verte, branche à jour) est appliquée côté serveur, donc plus sûre que `merge-gate` | Écartés |
| YeonwooSung · **agent-profiler** | Où va le temps d'une session (attente du modèle, outils, sous-agents, relectures) ; export OTLP facultatif | Seul candidat qui mesure ce que je ne mesure qu'à la main (temps par étape). Windows non cité ; écrit des fichiers dans `~/.claude` | **Candidat à l'essai**, en dernier, portée locale |
| YeonwooSung · context-inspector | Gaspillage de contexte : remplissage, relectures, doublons, contenu gardé à la compaction | Refait le §7 sans script ; ce §7 a déjà établi que les lectures de `IslandWindow` ne gaspillent pas | Facultatif, doublon |
| YeonwooSung · time-machine | Instantanés git « fantômes » à chaque appel d'outil, restauration, branches | Le dépôt est déjà sous Git avec PR ; son analyse des tests ne connaît pas `dotnet test` (pytest, jest, go, cargo, node:test) | Écarté |
| YeonwooSung · loop-detective | Détecte les échecs Bash répétés | Reconnaît un code de sortie non nul, mais aucun format de test .NET ; le hook Stop et la CI donnent déjà le signal | Écarté |
| YeonwooSung · verify-board, decision-ledger, handoff-pack, review-queue, scope-guard, assumption-tracker, env-doctor | « Premières versions : du texte de commande, pas de panneau vivant » | Chaque fonction existe déjà, versionnée : preuves (règle du `CLAUDE.md` + Stop), ADR (`/adr`), relais (`/cloture`), `global.json` + CI | Écartés |
| YeonwooSung · dependency-lens, blast-radius | Dépendances npm ; imports inversés (JS) | Sans objet pour du NuGet/C#. **Deux mods portent le nom `blast-radius`, avec des fonctions différentes** : ne jamais installer les deux | Écartés |
| YeonwooSung · pin-memo, model-effort-band, command-shelf, token-run | Confort, bandeau modèle/effort, jeu | Aucun besoin | Écartés |

Bilan : **0 mod retenu d'office**. Deux candidats facultatifs (`context-bar`, puis `agent-profiler`), à
essayer en portée locale, avec `get_usage` avant et après dans une session neuve. Ils ont ensuite été
installés à ta demande (§17.9) ; tout le reste reste écarté.

### 17.3 Routage : essai comparateur Sonnet / high

Même analyse que T3 (§15.1) : cycle de vie de tous les abonnements à `FrameClock.Rendering`, en lecture
seule, confiée à un sous-agent `sonnet` à l'effort `high` (modèle réel lu dans le retour du sous-agent).

| | Opus 5.5 · high (T3) | Sonnet 5.5 · high |
|---|---|---|
| Durée · appels d'outils | 129,6 s · 24 | **48,4 s · 13** |
| Abonnements trouvés | 7 sur 7 | 7 sur 7 (fichier:ligne donnés) |
| Mécanisme commun (`Fail` retire l'abonné, le propriétaire garde son drapeau) | Trouvé | Trouvé, avec les six propriétaires concernés |
| En plus | — | Drapeaux posés **avant** le `+=` (vérifié pour le ressort : `IslandSpringAnimator.cs:221-223`) ; deux propriétaires non couverts par la PR #101 (`RestFace`, `Flash`) |

Limites : **un seul essai** ; l'énoncé n'était pas mot pour mot celui de T3 (je n'en ai plus le texte exact),
donc la comparaison est indicative, sans portée statistique. Les deux pistes en plus ne sont pas
vérifiées ici (hormis le ressort). Décision : `CLAUDE.md` envoie désormais l'architecture, le cycle de vie
WinUI, les animations et la machine à états sur **`sonnet` / `high`** (ta règle), `opus` / `high` restant pour
le bogue intermittent, le multi-couches et l'essai déjà raté ; `/audit` et `winui-reviewer` restent sur Opus.
À rouvrir si un cas réel montre Sonnet passant à côté.

### 17.4 Mémoire : MCP Memory, essayé puis retiré (2026-10-18, avec ton accord)

Tu demandais d'en configurer un. Mon rejet du §6 reposait sur la **lecture** du README et du code ; il a donc
été essayé pour de bon, en portée locale, avec un critère fixé **avant** l'essai : le garder seulement s'il
(1) retrouve une décision sans relire un fichier, (2) **sans** dupliquer ce que contiennent déjà `docs/` ou la
mémoire native, (3) pour un surcoût de contexte négligeable.

**Mise en place (exécutée).** Node v24.15.0, npm 11.12.1 ; paquet `@modelcontextprotocol/server-memory` 1.0.0
(modifié le 2026-10-11, 55 ko décompressé, dépendances `zod` et le SDK MCP ; licence « SEE LICENSE IN LICENSE »,
non relue). Sauvegarde de `~/.claude.json` d'abord, puis :

```powershell
New-Item -ItemType Directory -Force "$HOME\.claude\mcp-memory" | Out-Null
claude mcp add memory --transport stdio --scope local -e "MEMORY_FILE_PATH=C:/Users/bleta/.claude/mcp-memory/spacenotch.jsonl" -- cmd /c npx -y @modelcontextprotocol/server-memory
claude mcp get memory
```

- **Correction de ma syntaxe du §17.4 d'origine** : avec `--env … memory`, la commande a échoué
  (« Invalid environment variable format: memory ») car `--env` avale l'argument suivant. Le nom du serveur
  doit venir **avant** les options, et `-e` avant le `--`. Lancée depuis Git Bash, la ligne a aussi besoin de
  `MSYS_NO_PATHCONV=1` (sinon `/c` est réécrit en chemin) ; PowerShell n'en a pas besoin.
- `claude mcp get memory` : **✔ Connected**, `Args: /c npx -y @modelcontextprotocol/server-memory`, variable
  de chemin présente.

**Mesures** (script du protocole stdio avec la commande enregistrée ; les jetons sont **estimés** à
4 caractères par jeton, pas lus par `get_usage`) :

| Mesure | Résultat |
|---|---|
| Démarrage du serveur | 1,9 s (à froid : 1,8 s au redémarrage) |
| Outils exposés | 9 ; **instructions du serveur : 0 caractère** |
| Définitions complètes (`tools/list`) | 11 065 caractères ≈ **2,8 k jetons**, chargés à la demande ; les noms différés coûteraient ≈ 0,5 k (règle de ~55 jetons par nom, §7) |
| Persistance | Entité créée par un processus, **retrouvée par un nouveau processus** (`search_nodes`, 6 ms, 301 caractères) ✔ ; fichier JSONL lisible |
| Mise à jour d'une information obsolète | `add_observations` puis `delete_observations` : l'obsolète disparaît, le fait reste ✔ |
| Doublons | Recréer une entité existante est refusé avec un message, rien n'est dupliqué ✔ |
| `read_graph` (tout le graphe, sans pagination) | 11 entités ≈ 1,6 k jetons ; 41 ≈ **6,3 k** ; 121 ≈ **18,9 k** |
| Nettoyage | `delete_entities` : graphe vide, fichier réduit à un saut de ligne ✔ |

**Verdict par critère.**

1. *Retrouver sans relire un fichier* : possible (`search_nodes`), mais son schéma est un outil différé qu'il faut
   charger d'abord ; la mémoire native charge un index de 517 octets d'office puis une lecture suffit. **Pas de
   gain démontré.**
2. *Sans dupliquer* : **non rempli.** Les décisions, pièges et mesures vivent déjà dans `docs/` (ADR, relais,
   audits) ou dans la mémoire native ; je n'ai trouvé aucun fait utile qui n'ait pas déjà sa place. Le graphe ne
   ferait que les recopier dans un troisième endroit, ce que tu veux éviter (jugement, non une mesure).
3. *Surcoût négligeable* : vrai au départ (≈ 0,5 k, aucune instruction), faux à l'échelle (≈ 19 k pour un seul
   `read_graph` à 121 entités).

**Décision : critère non rempli → retiré.** `claude mcp remove memory --scope local` ✔ ; dossier de données
supprimé (1 octet) ; cache `npx` dédié à ce seul paquet (25 Mo) supprimé après vérification de son
`package.json` ; `~/.claude.json` comparé à la sauvegarde `claude.json.avant-essai-memory` : **identique**
(serveurs du projet et clés de premier niveau) ; `claude mcp list` : les trois serveurs d'origine, connectés ✔.

**Non mesuré** : le `get_usage` d'une session neuve avec le serveur chargé (inutile une fois retiré). À rouvrir
seulement si un besoin de relations entre entités, que ni `docs/` ni la mémoire native ne couvrent, apparaît.

### 17.5 Mise en œuvre sur un autre PC, et retour arrière

La partie du dépôt (`settings.json`, hooks, skills, agent, `CLAUDE.md`) voyage avec Git. Le reste est propre à
chaque machine.

| Étape | Commande ou action (PowerShell) | Vérification | Retour arrière |
|---|---|---|---|
| 1. Récupérer le dépôt (après fusion de la PR #100) | `git fetch; git switch main; git pull` | `git log -1 --oneline` ; ligne « État du dépôt au démarrage » à l'ouverture d'une session | `git revert <commit de fusion>` (squash : un seul parent, pas de `-m`) |
| 2. Une seule installation de Claude Code | `claude --version; claude doctor` ; si une copie npm subsiste : `npm uninstall -g @anthropic-ai/claude-code` | `claude doctor` : « No installation issues found » | Réinstaller la copie npm (inutile) |
| 3. Fenêtre de compaction | Ajouter `"autoCompactWindow": 400000` à `~/.claude/settings.json` (valable pour tous les modèles) | `claude doctor` sans réglage invalide | Supprimer la ligne, ou `/autocompact auto` |
| 4. MCP résiduel | `claude mcp list` ; si `playwright` : `claude mcp remove playwright --scope local` | `claude mcp get playwright` : introuvable | `claude mcp add playwright …` |
| 5. Connecteurs inutiles (Vercel, Figma, Claude Docs) | Application, réglages des connecteurs — **à toi** | `get_usage` en session neuve : fixe < 80 k | Les réactiver |
| 6. Nouvelle session | Ouvrir une session ; taper `/audit` et `/cloture` | Les deux sont listés ; `get_usage` ≈ 80 k | — |

### 17.6 Ce qui reste, dans l'ordre

1. Fusionner la PR #100 puis la #101 quand leur CI est verte — c'est toi qui fusionnes.
2. Lancer **une fois** `/audit <périmètre>` en session neuve (par exemple « fenêtre principale et animations »)
   : c'est l'audit de référence que ta conversation prévoyait, et la seule preuve que le `model: opus` du
   frontmatter est honoré. Lire ensuite le champ `model` du journal.
3. Décider des connecteurs et de `syncClaudeAiSkills` (§15.3).
4. Dans deux semaines : relancer `usage.js` pour mesurer l'effet de `autoCompactWindow`.
5. Dans la session neuve du point 2 : relever `get_usage` au premier tour et constater les deux mods (§17.9).

### 17.7 Écartés, confirmés après relecture de tes demandes

`.claude/rules/` (le `CLAUDE.md` pèse ~2 k tokens, §7 : rien à économiser), worktrees (décision du dépôt,
rien ne la contredit), Playwright (inutilisable pour WinUI 3), Filesystem MCP (outils natifs), LiteLLM et
OpenRouter (aucun besoin de routage externe), plugin de snapshots (Git suffit), compaction après chaque
modification (déconseillée, comme dans ta conversation).

### 17.8 Constaté depuis le §16

- Le hook `SessionStart` **s'est déclenché** à la reprise de cette session et a affiché la ligne d'état
  (branche, retard 0, avance 3, un fichier non suivi, dernier relais) ✔.
- Règles `ask` : tu as vu les demandes (§16.2) ; la source exacte reste non isolée.
- Non constaté : `/audit` et `/cloture` dans la liste des skills ; `model` du frontmatter.
- `CLAUDE.md` fait maintenant 117 lignes (111 au §14, cible initiale 110, limite de la doc 200). Frontmatter
  de `/audit` et `/cloture` relu après modification ; build et tests non relancés : aucun fichier de code
  n'a changé dans cette révision.

### 17.9 `context-bar` et `agent-profiler` installés (à ta demande, 2026-10-18)

**Lus avant d'installer** : manifestes, `hooks/` des deux mods et `hooks/core/` d'`agent-profiler`. Aucune
exécution de processus, aucun `eval` ; le seul accès réseau est le POST OTLP d'`agent-profiler`, qui reste
inactif tant que `otlpEndpoint` est vide. `claude plugin validate` : **passed** pour les deux (`context-bar`
écrit son état `isHidden`/`reading` ; `agent-profiler` lit `HOME`).

```powershell
claude plugin marketplace add hamzafer/claude-code-mods --scope local
claude plugin install context-bar@claude-code-mods --scope local
claude plugin marketplace add YeonwooSung/my-claude-code-mods --scope local
claude plugin install agent-profiler@my-claude-code-mods --scope local
claude plugin list
```

- **État constaté** : `context-bar` 0.1.3 et `agent-profiler` 0.1.5, portée locale, *enabled*. Déclarés dans
  `.claude/settings.local.json`, **ignoré par Git** : ils ne figurent pas dans la PR et ne voyagent pas vers tes
  autres PC.
- **Réglages d'`agent-profiler`** : les trois options sont « non définies » ; les défauts du manifeste sont
  les sûrs (`otlpEndpoint` vide, dossier `~/.claude/agent-profiler`, `exportCommandText` faux : ni prompts ni
  commandes exportés). Ne pas renseigner `otlpEndpoint` sans besoin.
- **Non vérifié** : aucun des deux n'a été **chargé dans une session** (ils ne se chargent qu'à l'ouverture
  d'une session neuve ou après `/reload-plugins`) ; ce que `context-bar` affiche dans l'application de bureau ;
  leur fonctionnement sous Windows (leur CI est sur Ubuntu).
- **Critère pour les garder** (ma proposition, fixée avant l'essai) : `context-bar` si le surcoût fixe mesuré
  par `get_usage` reste ≤ ~1 k jetons et que la barre s'affiche ; `agent-profiler` s'il produit un profil
  exploitable (temps par étape) sans ralentir les tours. Sinon, retrait :

```powershell
claude plugin uninstall context-bar@claude-code-mods
claude plugin uninstall agent-profiler@my-claude-code-mods
claude plugin marketplace remove claude-code-mods
claude plugin marketplace remove my-claude-code-mods
```

(Sous-commandes vérifiées par `--help`, retrait non exécuté.)

### 17.10 OmniRoute : écarté (évaluation, rien d'installé)

**Ce que c'est** (README, métadonnées GitHub et npm, lus le 2026-10-18) : une passerelle IA locale
(`localhost:20128`, API compatible OpenAI, 374 fournisseurs dont 150+ gratuits, repli automatique) avec un
serveur MCP qui la pilote (stdio `omniroute --mcp`, HTTP `/api/mcp/stream` : **110 outils**, 33 portées dont
l'application est **facultative** ; le README dit que l'agent « obtient les clés de toute la passerelle » ; la
recherche web cite 104 outils : les comptes diffèrent selon les sources). Dépôt MIT, 77 744 ★, 358 issues
ouvertes, créé le 2026-02-13, poussé le jour même ; paquet npm `omniroute` 3.8.51, **546 Mo décompressé**,
script `postinstall`, un seul mainteneur. Télémétrie désactivée par défaut, identifiants chiffrés (AES-256-GCM)
d'après le README. **Aucun audit de sécurité indépendant trouvé.** Je n'ai lu ni le code ni
`docs/frameworks/MCP-SERVER.md`.

| Question | Réponse | Preuve |
|---|---|---|
| Le « MCP » est-il la solution ? | Non : c'est la télécommande de la passerelle, inutile sans elle. La vraie question est donc d'ajouter un **proxy** | README |
| Besoin démontré de proxy ? | **Non.** Forfait Pro à ~5–6 % (§9), coût dû à la longueur des sessions (§7), routage déjà obtenu par `/model` et le frontmatter (§5, §17.3). Ton brief et ta conversation écartaient LiteLLM/OpenRouter « sans besoin démontré » | Rapport |
| Compatible avec ton forfait ? | **Risqué.** La page légale d'Anthropic (lue) interdit aux tiers de router des requêtes avec des identifiants Free/Pro/Max ou d'intermédier des jetons Claude.ai ; l'alternative est une clé d'API Console, donc une **facturation à l'usage**. Autre voie : des modèles gratuits d'autres fournisseurs, où « les prompts partent au fournisseur choisi » (README) et dont certains sont marqués `tos: avoid` | `code.claude.com/docs/en/legal-and-compliance` |
| Économie de tokens ? | Ses filtres RTK visent entre autres la sortie de `dotnet build/test`. **Mesuré dans tes journaux** (37 fichiers) : 23 appels `dotnet build/test`, ≈ **4 k jetons estimés** sur ≈ 347 k de sorties Bash (≈ 1 %). Rien à gagner. Les « 15 à 95 % » du README ne sont pas vérifiés | Script sur les `.jsonl` |
| Coût de contexte du MCP ? | **Extrapolation** d'après les 9 outils mesurés du MCP Memory (§17.4, ≈ 300 jetons par définition) : 110 outils ≈ 30 k jetons si les définitions sont chargées, ≈ 6 k pour les noms différés | Calcul, non mesuré |
| Surface et maintenance | 546 Mo, `postinstall`, un mainteneur, croissance très rapide en 8 mois : surface d'attaque large pour un outil qui verrait ton code et tes clés | npm, GitHub |

**Décision : écarté.** À rouvrir seulement si tu passes à l'API facturée et qu'un coût devient un problème
**mesuré** ; l'évaluation se ferait alors en conteneur, sans exposer le MCP d'administration à Claude Code.
