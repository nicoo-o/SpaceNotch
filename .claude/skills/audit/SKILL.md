---
name: audit
description: Audit en lecture seule d'un périmètre SpaceNotch (une fonctionnalité, une couche, tout le dépôt) — constats classés par gravité, preuves fichier:ligne, constats graves revérifiés dans le code, rapport dans docs/audit/. Ne corrige rien.
disable-model-invocation: true
model: opus
effort: xhigh
context: fork
background: false
argument-hint: "<périmètre> — ex. « fonctionnalité Weather », « fenêtre principale et animations », « tout »"
---

# Audit SpaceNotch

Périmètre : $ARGUMENTS

Périmètre vide : écrire « Périmètre manquant : indiquer une fonctionnalité, une couche ou "tout" » et
s'arrêter (ce skill tourne dans un sous-agent, qui ne peut pas poser la question).

**Lecture seule.** Aucun fichier de code modifié, aucun commit, aucune instance de la notch lancée ni
fermée. Le seul fichier écrit est le rapport ; si l'écriture est refusée, rendre le rapport complet dans
la réponse.

## 1. Cadrer

- `docs/architecture.md`, l'index `docs/decisions/README.md` (ADR du périmètre), `docs/state-machine.md`
  si la notch est concernée.
- Le dernier audit voisin dans `docs/audit/` : ses « décisions de l'auteur » sont acquises, ne pas les
  rouvrir. Ce qui y est déjà constaté et corrigé ne se redécouvre pas.
- Écrire en cinq lignes : le périmètre, les dimensions retenues, ce qui est hors périmètre.

## 2. Dimensions — celles du périmètre, pas toutes

| Dimension | Où regarder |
|---|---|
| Architecture, couches | `cwm-roslyn-navigator` : `get_project_graph`, `detect_circular_dependencies`, `find_dead_code`, `detect_antipatterns` |
| Fil d'interface, durée de vie | ADR-011, ADR-004 : abonnements sans désabonnement, timers périodiques, `IDisposable` — la liste de `.claude/agents/winui-reviewer.md` |
| P/Invoke, Win32 | `Platform.Windows/Win32/NativeMethods.cs`, `NativeConstants` |
| États, géométrie | `docs/state-machine.md`, `Core/Machine/NotchMachine.cs`, `IslandScreenBounds()` |
| Performance, animation | `docs/performance.md` (18 s de stabilisation), `docs/animation-system.md`. Une mesure exige l'instance de développement, notch de l'auteur **fermée par lui** : sinon « estimé d'après le code » |
| UX, accessibilité | `docs/ux/design-language.md` : noms accessibles, cibles ≥ 32 DIP, texte via `Lang.T` |
| Sécurité | réseau, secrets, greffons (ADR-010) |
| Tests | `get_test_coverage_map` : comportements de Core sans test |

## 3. Chercher

- Naviguer avec `cwm-roslyn-navigator` (`find_symbol`, `find_callers`, `get_file_outline`,
  `get_symbol_source`) ; lire `IslandWindow*.cs` par `offset`/`limit`, jamais en entier.
- Une fonctionnalité : comparer ses points de branchement à ceux de `/new-feature` (`FeatureKeys`,
  `AppSettings`, `IslandWindow`, `SettingsWindow`).
- Un constat = `fichier:ligne` + le scénario concret qui casse + la correction proposée. Pas de style.

## 4. Revérifier les constats graves

Tout constat 🔴 ou 🟠 : relire le code cité et essayer de le **réfuter** — le chemin est-il atteignable ?
un test le couvre-t-il ? un ADR le justifie-t-il ? Retirer ou rétrograder ce qui ne tient pas, et le dire
(« écarté après revérification »).

## 5. Écrire `docs/audit/AAAA-MM-<slug>.md`

Gabarit de `docs/audit/2026-10-audit.md`, en français, chemins relatifs à `src/` :

1. Titre, méthode en trois lignes, légende 🔴 critique · 🟠 haute · 🟡 moyenne · ⚪ basse.
2. Synthèse (dix lignes au plus, chiffres compris).
3. Constats par dimension : tableau n° · gravité · constat · preuve · correction · effort (S, M, L).
4. `## Non vérifié` : ce qui n'a pas pu l'être, et pourquoi.
5. `## Décisions à prendre` : ce qui revient à l'auteur.

Ni ADR ni modification de la feuille de route ici : les constats retenus y entrent à la clôture
(`/cloture`).

## 6. Rendre

Le chemin du rapport, cinq lignes de synthèse, les trois constats les plus graves, ce qui n'a pas été
vérifié.
