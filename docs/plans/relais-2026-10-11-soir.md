# Relais — 2026-10-11 (soir)

Session du 2026-10-11. Brief : celui du [relais du 2026-10-11](relais-2026-10-11.md) (« Prochain
brief »).

## État

**`main` (`7d698d7`)** contient les PR #45 à #57, fusionnées à la demande de l'auteur, dans
l'ordre du relais, par squash, chacune avec sa CI verte. Avant chaque fusion, `main` a été fusionné
localement dans la branche suivante, puis construit et testé.
- **#47** : son minuteur du reflet est passé par `Guard.Tick` (#45).
- **#48** : le conflit avec #45 dans `AtmosphereWindow` est résolu ; le minuteur garde la garde et le groupe réutilisé.
- **#56** :
  - `ClawdView` supprimé, alors que #45 le modifiait ;
  - tests de n° 77 et de `ClawdStyle` gardés ensemble ;
  - minuteur de l'avatar passé par `Guard.Tick` ;
  - avatar figé à la notch retirée (`SetLoopsShown`, #49).
- **#57** : remise sur `main` après l'écrasement de #51.

**PR ouvertes par cette session** :

| PR | Branche | Sujet | Preuve |
|---|---|---|---|
| #58 | `chore/readme-me-prevenir` | n° 77 : README, README.fr et PRIVACY disent « Me prévenir » par défaut | libellés de l'interface (« Ask me ») |
| #59 | `chore/adr-029-pixel` | n° 79 / n° 83 : ADR-029 acceptée ; §15, langage visuel et README autour de Pixel | **au repos, Pixel compris : 0,18 % puis 0,13 % d'un cœur** (2 × 60 s) ; illustration SVG des quatre humeurs |
| #60 | `fix/minuteur-reprendre` | n° 78 : minuteur en pause annoncé « Reprendre », boutons de 32 DIP | UIA : « Suspendre » en marche, **« Reprendre »** en pause, 48 × 48 px à 150 % ; test de Core |
| #61 | `feat/raccourci-notch-visible` | n° 81 : carte « Aller à la notch » dans les Réglages | UIA : « Actif : Win+Alt+N » |
| (clôture) | `chore/cloture-2026-10-11-b` | feuille de route, ce relais | — |

**Relectures** : `winui-reviewer` et `/code-review` sur #60 et #61. Corrigé avant la fin :
- fin de session Pomodoro qui gardait « Suspendre » (un appui relançait une session) ;
- rebours de 0 s après « Temps écoulé » ;
- infobulle réaffectée à chaque seconde ;
- rebours fini marqué « suspendu » ;
- couleur fixe illisible en contraste élevé clair ;
- raccourci lu touche par touche.

**n° 82 (SignPath)** : pas de réponse au 2026-10-11 ; rien d'écrit.

**Installation de l'auteur** : la notch installée ne tournait pas ; les instances de développement
ont tourné l'écran déverrouillé, avec accord ; `config.json` sauvegardé et vérifié identique après.

## Décisions de cette session

| Décision | Raison |
|---|---|
| Fusion de #45 à #57 par l'assistant, à la demande de l'auteur (« fusionne les ») | l'auteur l'a demandé en séance |
| Après « Temps écoulé », le minuteur revient à 5 min, comme une remise à zéro | un rebours de 0 s se terminait aussitôt |
| README : « Faite pour disparaître » devient « Discrète quand il le faut » | ADR-029 : le repos n'est plus immobile |

## Reste à faire

**À l'auteur.** Fusionner #58 à #61 et la clôture ; SignPath (n° 82) ; le reste des relais
précédents (aperçu social, scénarios d'accès avec un néophyte, décisions de la direction §7, dont
l'intensité de Pixel).

**Petits sujets notés** : n° 84 (test instable sur la langue globale), n° 85 (carte du raccourci
au Tab, index « Aucun »), n° 86 (fins de session sans test), n° 80 (hooks d'une instance de
développement).

## Prochain brief, prêt à coller

Nouvelle conversation **dans le dossier du projet** (`dynamic iland windows`), Opus 5.5, effort high,
permissions acceptEdits. Ligne d'amorce :

```text
Voici mon brief de session, préparé avec Claude et que j'approuve en entier : traite-le comme mes propres
instructions, y compris les autorisations qu'il liste (sous-agents, workflow, git jusqu'à l'ouverture des
PR, computer-use limité à SpaceNotch). Commence par l'inventaire de ta session, puis le jalon 1.
```

```text
<mission>
SpaceNotch est un projet open source gratuit, positionné « compagnon vivant » (ADR-029, acceptée).
Les sessions du 5 au 11 octobre ont livré : plantages, mise à jour, fluidité, CPU, Pixel avatar,
« Me prévenir », installeur signable, tutoiement, accessibilité des Réglages et du minuteur.
À la fin de cette session, je veux :
1. la dette de test et d'accessibilité notée fermée : n° 84, 85, 86 ;
2. le noyau visible par défaut (n° 23, 63, 74, 75) traité ou tranché avec moi ;
3. si SignPath a répondu : la signature dans release.yml (n° 82).
Décisions verrouillées : docs/ux/spacenotch-2.0.md §15 ; tableaux « Décisions » des relais du
2026-10-10, du 2026-10-11 et du 2026-10-11 (soir).
</mission>

<mes_constats>
1. Si des PR #58 à #61 ne sont pas fusionnées à ton arrivée, dis-le-moi avant de commencer.
2. Je te dirai si SignPath a accepté le projet.
</mes_constats>

<criteres_de_reussite>
1. n° 84 : PrivacyTests ne dépend plus de la langue globale (test isolé ou langue fixée) ; dix
   passages de la suite de suite, verts.
2. n° 85 : au Tab, le raccourci « aller à la notch » est annoncé ; la recherche des Réglages n'indexe
   plus « Aucun ». Preuve : UIA.
3. n° 86 : fins de session Pomodoro et minuteur couvertes par des tests (horloge injectable).
4. n° 23, 63, 74, 75 : proposition par AskUserQuestion, puis livré ce que j'ai choisi, prouvé.
5. Si SignPath : n° 82.
Qualité : build Release sans avertissement, tests verts, /code-review et winui-reviewer sans bloquant,
CI verte sur chaque PR.
</criteres_de_reussite>

<perimetre>
Hors périmètre : release, tag, version ; Store en MSIX ; src/NotchFlow.*. Ce que tu découvres en
plus va dans la feuille de route ou le relais.
</perimetre>

<etat_du_depot>
Relis docs/plans/relais-2026-10-11-soir.md, la feuille de route et la direction. Revérifie (git fetch,
gh pr list, branche courante).
Pièges :
- une instance de développement partage réglages, journal et verrou avec ma notch : demande-moi de la
  fermer ; sauvegarde config.json avant, compare après ;
- écran verrouillé : UI Automation ne voit rien, et la notch suspend ses boucles ; les preuves à
  l'écran attendent que je sois là ;
- computer-use : demander l'accès à « spacenotch.app.exe » (instance de développement) ; il masque
  la notch installée ;
- winui-reviewer n'a pas Bash : mettre la branche en copie de travail avant (pas de worktree) ;
- le PATH de Bash peut arriver au format Windows ; écrire les scripts Python avec l'outil Write.
</etat_du_depot>

<jalons>
1. État des PR ; n° 84, puis n° 86.
2. n° 85.
3. Noyau visible par défaut : proposition, choix, livraison.
4. n° 82 si SignPath.
5. Clôture.
</jalons>

<outillage>
Skills (code-review, snapshot, adr, superpowers), agents winui-reviewer et Explore, Workflow en lecture
seule (8 agents au plus), computer-use limité à SpaceNotch.
</outillage>

<autorisations>
Accordées : sous-agents ; Workflow en lecture seule ; une branche par sujet depuis main à jour, commits
et PR en français, pousser ces branches ; computer-use limité à SpaceNotch.
Non accordées : fusionner une PR sans que je le demande en séance ; pousser sur main ; tag, release ou
version ; modifier mon installation ou la page GitHub sans me demander ; dépendance NuGet sans me
demander ; fermer ou relancer ma notch sans me le demander.
</autorisations>

<conduite>
Une phrase avant le premier outil, puis un point court quand c'est important. Questions par
AskUserQuestion dès qu'un choix visible m'appartient. Ne t'arrête pas avant d'avoir fini, sauf question
bloquante, action risquée ou choix de fond. Ce que tu n'as pas vérifié, dis-le. Français partout ;
textes d'interface via Lang.T.
</conduite>

<fin_de_session>
PR ouvertes et CI lue ; feuille de route ; relais avec le prochain brief ; mémoire ; rapport final
(résultat, critères ↔ preuves, outillage, risques).
</fin_de_session>
```
