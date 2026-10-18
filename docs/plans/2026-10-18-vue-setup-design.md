# Vue du setup Claude Code (`/vue-setup`) — conception

> **Statut : proposition, non approuvée.** Rien n'est construit. Ce document dit quoi faire et pourquoi ; le
> plan d'exécution (`superpowers:writing-plans`) ne s'écrit qu'après accord de l'auteur. Date : 2026-10-18.

## Intention

L'audit de l'environnement Claude Code ([rapport](../audit/2026-10-18-environnement-claude-code.md)) a produit
des dizaines de réglages, d'essais et de rejets, chacun avec une preuve plus ou moins solide. Aujourd'hui cette
histoire vit dans un rapport de 800 lignes : on ne **voit** ni ce qui a vraiment changé le coût d'une session,
ni ce qui n'a rien changé, ni la solidité de chaque chiffre.

`/vue-setup` rend cette histoire visible dans la session même : une **barre** en direct au-dessus de la zone de
saisie, et un **panneau** à quatre vues. Il doit aussi pouvoir se **montrer** (README, communauté open source du
projet) : c'est la vitrine d'une façon de régler son environnement en prouvant chaque affirmation.

## Ce que l'auteur a décidé (2026-10-18)

- Une **barre en session** (un mod) **et** une page, alimentées par **les mêmes mesures**.
- Tout reste **local** : rien n'est envoyé, aucun Artifact.
- Quatre vues : contexte fixe avant/après · registre des décisions · chronologie des sessions · comparateur
  de routage.
- Public : **pour le montrer**.
- La barre s'affiche dans l'app Desktop (constaté par l'auteur sur `context-bar`, session neuve).

## Buts, non-buts, anti-buts

**Buts**
1. La barre montre en direct le contexte, sa part fixe, l'écart avec la référence du 2026-10-18 et le point de
   compaction, en deux lignes au plus.
2. Chaque chiffre affiché porte un badge **mesuré · estimé · non vérifié** (règle « Preuves » de `CLAUDE.md`).
3. Le registre répond en un coup d'œil à : qu'est-ce qui est appliqué, essayé puis retiré, écarté, en attente ?
   avec quelle preuve et quelle commande de retour arrière ?
4. Le mod rend visible **son propre coût** de contexte.

**Non-buts**
- Remplacer `/context`, `/insights` ou ccusage. On réutilise leurs sources, on ne les refait pas.
- Un coût en euros : le forfait Pro n'a pas de facture au jeton, seules des estimations « équivalent API » existent.
- Grafana / OpenTelemetry, ou tout export vers un service (écarté : infrastructure sans besoin démontré).
- Modifier des réglages : le mod **ne fait que lire**.

**Anti-buts** (un échec même si « ça marche »)
- Un chiffre estimé présenté comme mesuré, ou une comparaison à n = 1 présentée comme un verdict.
- Le texte des conversations (invites, réponses, chemins de fichiers) lu au-delà de l'agrégation, stocké ou affiché.
- Un outil de mesure qui alourdit ce qu'il mesure sans le dire.

## Contraintes (documentation des mods, copie du 2026-10-18)

| Contrainte | Valeur | Conséquence |
|---|---|---|
| Version | mods actifs par défaut depuis Claude Code 2.1.287 (Desktop 2.1.286) ; certaines fonctions exigent plus récent (2.1.299 pour `weight`) ; installé ici : 2.1.302 | version du Desktop non relevée ; seule la barre y est constatée |
| Où dessine un panneau | terminal, Desktop, VS Code | le panneau est portable |
| `Svg` | Desktop et VS Code seulement, 131 072 caractères au plus | graphiques en SVG sur Desktop ; repli en `Box`/`Text` dans le terminal |
| Hook | 10 s de temps propre | rien de lourd dans un hook |
| `$.fs.read` | 4 Mio par fichier | un mod **ne peut pas** lire les journaux (jusqu'à 48 Mo) |
| `$.process.run` | 30 s par défaut, 10 min au plus | un script Node fait l'agrégation |
| `$.store` | 4 Mio de JSON, partagé entre sessions | assez pour des agrégats, pas pour des journaux |
| Module du mod | pas d'API Node, pas de minuterie globale | `$.clock`, `$.fs`, `$.process` seulement |

Le script d'agrégation existant (`usage.js`) lit les 11 sessions avec réponses du projet (dossier de 128 Mo) en
≈ 0,9 s sur cette machine : l'agrégation à la demande est réaliste.

## Architecture proposée

```
tools/vue-setup/                       un plugin de type « mod »
  .claude-plugin/plugin.json
  hooks/hooks.json                     { "modules": ["./register.tsx"] }
  hooks/register.tsx                   commande /vue-setup, barre, panneau
  types/index.d.ts                     état partagé du mod
  scripts/mesurer.mjs                  agrège les journaux → mesures.json (Node 22+)
  data/registre.json                   décisions de l'audit, une entrée chacune
  data/essais-routage.json             essais Opus/Sonnet, avec leurs limites
  demo/mesures-exemple.json            jeu anonymisé, pour la démo et les captures
  tests/                               node:test pour le script ; claude plugin test pour le mod
```

Flux : `mesurer.mjs` (lecture seule des journaux du **projet courant**) → `~/.claude/vue-setup/mesures.json`
(agrégats chiffrés uniquement, hors dépôt) → le mod lit ce petit fichier avec `$.fs.read`. La barre, elle, ne lit
**pas** ce fichier pour le direct : elle appelle `$.session.usage({ breakdown: 'summary' })`, la source qu'utilise
`context-bar` (estimée localement par le moteur, d'après son code).

### Modèle de données

`mesures.json` : version du schéma ; pour chaque session : date, modèles (tours, jetons d'entrée / écriture de
cache / lecture de cache / sortie), et par tour : taille du contexte = somme des trois champs d'entrée
(**dérivé, donc estimé**), modèle, repère de compaction. Aucun texte. Règles héritées de `usage.js` : un tour
est une réponse de l'assistant avec `usage`, dédoublonnée par `message.id` ; seules les sessions principales sont
comptées (les sous-agents ne le sont pas). Repère de compaction (heuristique, **estimée**) : le contexte chute de
plus de moitié d'un tour au suivant dans la même session.

`registre.json`, une entrée par décision :
`id`, `titre`, `statut` (`applique` · `essaye-puis-retire` · `ecarte` · `en-attente`), `date`, `preuve` (une
phrase), `badge` (`mesure` · `estime` · `non-verifie`), `section` (ancre du rapport), `retour` (commande de retour
arrière, si elle existe), `effet_contexte_fixe` (jetons, ou `null` = non mesuré).

`essais-routage.json` : `tache`, `config` (modèle + effort), `duree_s`, `appels`, `resultat`, `n`, `limites`.

**Source unique.** Le rapport garde le récit ; le registre garde les statuts. Un test vérifie que chaque `section`
du registre existe encore dans le rapport (sinon la dérive se voit tout de suite). Le registre initial se tire du
rapport : le `badge` reprend la marque du rapport quand il en porte une (✔ → `mesure`, ✖ → `non-verifie`), sinon
`non-verifie` ; jamais `mesure` par défaut.

**Un chiffre ne s'affiche que typé.** Toute valeur rendue est un objet `{ valeur, badge }` : le type interdit de
dessiner un nombre sans badge (critère 3).

**Panneau et surface.** Le rendu choisit `Svg` ou le repli `Box`/`Text` d'après `e.surface` (`desktop`, `terminal`,
`vscode`). L'état (barre masquée, onglet ouvert) vit dans `$.store`, comme `context-bar`.

## Les quatre vues

1. **Contexte fixe.** Barre empilée de la référence (79 899 jetons le 2026-10-18, **mesuré** : outils intégrés
   37 k, outils MCP 20 k, prompt système 9 k, skills 8,6 k, instructions MCP 4,4 k) face à la barre d'aujourd'hui
   (direct), écart par catégorie. Sous la barre : les changements du registre qui devaient la réduire, avec leur
   `effet_contexte_fixe` — souvent « non mesuré » ou « aucun », ce que la vue doit assumer : d'après le rapport,
   le coût vient de la **longueur des sessions**, pas de la part fixe.
2. **Registre.** Liste filtrable par statut ; badge de preuve sur chaque ligne ; lien vers le § du rapport ;
   commande de retour arrière copiable. Les ~10 outils jugés le 2026-10-18 et les rejets (OmniRoute, MCP Memory,
   Playwright…) y entrent.
3. **Chronologie.** Une piste par session : taille du contexte à chaque tour, couleur par modèle, marqueur de
   compaction ; des repères verticaux aux dates de changement (par exemple `autoCompactWindow`, dont l'effet reste
   à mesurer deux semaines plus tard).
4. **Comparateur de routage.** Un essai par ligne (aujourd'hui : Opus/high 129,6 s, 24 appels contre
   Sonnet/high 48,4 s, 13 appels, §17.3). La colonne **limites** reste toujours visible ; avec n = 1, aucune
   couleur de « gagnant ».

La **barre** : `◆ setup · contexte 83 k (fixe 80 k) · réf. 79,9 k · Sonnet/high · compacte à 400 k`, colorée selon
la part du seuil de compaction (comme `context-bar`). Commande `/vue-setup` ouvre le panneau, `/vue-setup barre`
la masque ou l'affiche, `/vue-setup actualiser` relance `mesurer.mjs`, `/vue-setup demo` bascule sur le jeu
d'exemple.

## Critères d'acceptation

| # | Critère | Preuve attendue |
|---|---|---|
| 1 | La barre apparaît au premier tour et se met à jour à chaque tour (Desktop, Windows 11) | capture, ou « non vérifié » |
| 2 | Le panneau s'ouvre et les quatre vues s'affichent en Desktop ; le terminal affiche le repli texte | captures des deux |
| 3 | Aucun nombre sans badge | le type `{ valeur, badge }` + un test sur le rendu texte de chaque vue |
| 4 | `mesurer.mjs` redonne les totaux de `usage.js` sur un journal d'essai et ne sort aucun texte du journal | test `node:test` sur un journal factice |
| 5 | Le registre et le rapport ne divergent pas | test de dérive des ancres |
| 6 | `claude plugin validate --strict` et `claude plugin test` passent | sorties collées |
| 7 | Le surcoût de contexte du mod est mesuré (fixe avant / après activation) et affiché au registre ; objectif ≤ ~1 k jetons estimés, le critère retenu pour `context-bar` et `agent-profiler` (rapport §17.9) | deux relevés |
| 8 | Une session sans `mesures.json` ne casse rien : la barre vit seule, le panneau dit « aucune mesure, lancer `/vue-setup actualiser` » | test |

## Risques

- **`Svg` en Desktop : levé le 2026-10-18.** La sonde jetable (étape 0, retirée depuis) a été essayée par l'auteur
  dans l'app Desktop : la barre empilée et son texte gris `<text>` s'affichent, et le bouton ajoute un segment
  (le `Svg` est redessiné après `$.ui.invalidate`). **Non vérifié** : thème sombre contre clair, panneau étroit,
  version exacte du Desktop, `Select` et `Input`.
- **Format des journaux non documenté.** `usage.js` s'appuie sur `message.usage` et `message.id` ; une forme
  inattendue doit donner « non mesuré », jamais une exception. Le dossier d'un projet se déduit de son chemin
  (`:`, `\` et espaces deviennent `-`) : règle **observée**, pas documentée ; prévoir un paramètre explicite.
- **`HOME` peut être absent sous Windows** (le dépôt de `ponytail` a eu ce défaut, #1143) : chercher
  `CLAUDE_CONFIG_DIR`, puis `USERPROFILE`, puis `HOME`.
- **L'effort (`high`, `medium`) dans la barre** n'est pas confirmé lisible par l'API (`$.session.model` l'est ;
  l'effort apparaît dans l'événement `turn.step`). Sinon la barre n'affiche que le modèle.
- **L'API des mods évolue.** La doc cite la v2.1.290 ; on prend les types de la version installée
  (`claude plugin validate` fait foi).
- **Le Desktop exact n'est pas connu.** La version minimale annoncée est 2.1.286.

## Découpage et ordre

0. ~~Sonde jetable~~ **faite et retirée** (2026-10-18) : le Desktop dessine et redessine un `Svg`. Le repli
   `Box`/`Text` reste nécessaire pour le terminal.
1. Script de mesure + journal factice + tests.
2. Registre et essais (reprise du rapport), test de dérive.
3. La barre.
4. Le panneau à quatre vues.
5. Démo anonymisée, README, captures, fichier de place de marché pour l'installation par d'autres.

Chaque étape se vérifie avant la suivante ; le plan détaillé viendra avec `superpowers:writing-plans`.

## Mes appels (à corriger si faux — ils ne viennent pas de l'auteur)

- **Un seul mod à deux faces** (barre + panneau) et non deux plugins.
- **Le panneau d'abord, une page HTML ensuite** : voir la décision 1 ci-dessous. Si l'export HTML est voulu, il se
  tire du même objet de données, sans second jeu de mesures.
- **Journaux du projet courant seulement**, jamais ceux des autres projets.
- **Textes bilingues** (`t("français", "English")`, comme `Lang.T` dans l'app), français par défaut.
- **Mesures hors dépôt** (`~/.claude/vue-setup/`), un jeu anonymisé commité pour la démo.
- **La barre ne réutilise pas le protocole d'empilement de `context-bar`** (fichier sous licence MIT, à recopier
  avec sa notice) : elle se pose sous les bandes existantes. À revoir si l'ordre pose problème.
- **Node 22 ou plus requis** pour `mesurer.mjs` (24.15 ici) ; le mod seul n'en a pas besoin.

## Décisions de l'auteur sur ce document (2026-10-18)

Panneau d'abord (export HTML éventuel plus tard) · le mod vit dans ce dépôt (`tools/vue-setup/`) · sonde jetable
autorisée (faite). **Le document dans son ensemble n'est pas encore approuvé** : rien de plus n'est construit.

## Décisions qui étaient à prendre (réponses ci-dessus)

1. **Panneau dans l'app ou page HTML ?** La doc des mods permet un panneau natif (onglets, boutons, graphiques
   `Svg` en Desktop) : même confidentialité, pas de fichier à ouvrir, interactif. Une page HTML serait
   plus facile à photographier et à publier. *Mon penchant : panneau d'abord, export HTML plus tard si utile.*
2. **Où vit le mod ?** Dans ce dépôt (`tools/vue-setup/`, un fichier de place de marché à la racine) ou dans un
   dépôt à part. *Mon penchant : ici d'abord, extraction possible ensuite ; le dépôt d'une app WinUI n'est pas
   l'endroit naturel d'un outil de mod, mais c'est là que sont les preuves.*
3. **La sonde jetable de l'étape 0 peut-elle être construite avant l'approbation de tout le document ?** Elle
   répond à la seule inconnue technique (le `Svg` en Desktop) ; elle ne s'écrit pas dans le dépôt.
