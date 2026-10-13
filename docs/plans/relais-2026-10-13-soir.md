# Relais — 2026-10-13 (soir)

Session du 2026-10-13 au soir. Brief : celui du [relais du 2026-10-13](relais-2026-10-13.md).

## État

**`main`** contient #63 à #77. #76 (n° 92) et #77 (clôture) avaient été fusionnées à la demande de
l'auteur à la fin de la session précédente : le jalon 1 était déjà fait à l'arrivée.

**PR ouvertes** (CI lue à la clôture) :

| PR | Branche | Sujet | Preuve |
|---|---|---|---|
| #78 | `feat/barre-en-haut` | n° 93 : barre des tâches en haut (ADR-030, passée à « Accepté (version à préciser) ») | mesure avant, captures, UIA, tests de Core ; relue par `winui-reviewer`, cinq défauts majeurs corrigés |
| #79 | `chore/modeles-bilingues` | n° 94 : modèles d'issue et de PR bilingues | relecture du YAML |
| (clôture) | `chore/cloture-2026-10-13-soir` | feuille de route, ce relais | — |

**Simulation de la barre** : une AppBar de test, proposée à l'auteur et acceptée.
- Une bande de 40 DIP en haut de l'écran principal, qui réserve le haut comme StartAllBack.
- Elle se retire seule (ABM_REMOVE) au bout de quelques minutes, ou sur un fichier « stop ».
- La zone de travail est revenue à 0 après chaque essai.
- Script hors du dépôt : `appbar-test.ps1`, dans le dossier de travail de la session.

**Mesure avant** : avec la barre, la notch de `main` se posait à y = 0, **par-dessus** la barre.

**Preuves de #78** :
- Au démarrage, la carte « Barre des tâches en haut. Où placer la notch ? » apparaît **sous** la
  barre ; le journal note `[BARRE] … (60 px) : la notch demande où se placer`.
- « Sous la barre » : `UnderBar`, et la fenêtre de la notch commence à y = 60, sans chevauchement.
- Réglages › Notch lit « Sous la barre ».
- « Sur le côté » (par la carte, puis par les Réglages) : `SideTab` et `DockEdge: Right`, avec la
  languette au bord droit.
- Avec « Sur le côté », la notch ramenée en haut reste sous la barre.
- **Non vérifié à l'écran** : l'arrachement et le raccrochage sous la barre.

**Pièges appris** :
- La capture computer-use masque les fenêtres non autorisées, dont l'AppBar de test, et peut replier
  la notch. Pour ces preuves, tout s'est fait sans computer-use : captures System.Drawing en
  processus DPI-aware, clics par UIA (`InvokePattern`).
- PowerShell : une méthode statique nommée `New` entre en conflit avec `::new` ; un champ de
  structure imbriquée se modifie sur une copie.

**Configuration de l'auteur** : `HideOverFullscreen` et `TopBarPlacement` ont été changés pour les
preuves. `config.json` et `config.json.bak` ont ensuite été restaurés, identiques.

**n° 82 (SignPath)** : pas de réponse ; rien d'écrit.

## Décisions de cette session

| Décision | Raison |
|---|---|
| Une barre en haut se lit à la zone de travail ; une barre masquée automatiquement ne compte pas | elle ne réserve rien, et glisse par-dessus comme toute fenêtre |
| Accrochée en haut, la notch se pose sous une barre présente **quel que soit le choix** ; « Sur le côté » ne décide que du bord, au moment du choix | relecture de #78 : sinon une notch ramenée en haut recouvrait la barre |
| La question attend la notch fermée et la présentation finie, une fois par démarrage ; refermer garde « sous la barre » | ne pas voler l'écran ni la présentation du premier lancement |
| Modèles d'issue et de PR bilingues | direction §7.2 et §7.8 |

## Reste à faire

**À l'auteur.**
- Fusionner #78, #79 et la clôture si tu le demandes.
- Protéger `main` (n° 95).
- SignPath (n° 82).

**Suite.**
- n° 96 : finitions de la barre en haut (épaules, goutte, preuve de l'arrachement).
- Preuves de #71 et #72.

## Prochain brief, prêt à coller

Nouvelle conversation **dans le dossier du projet** (`dynamic iland windows`), Opus 5.5, effort high,
permissions acceptEdits. **Coller le brief en entier**, de `<mission>` à `</fin_de_session>`.

```text
Voici mon brief de session, préparé avec Claude et que j'approuve en entier : traite-le comme mes propres
instructions, y compris les autorisations qu'il liste (sous-agents, workflow, git jusqu'à l'ouverture des
PR, computer-use limité à SpaceNotch). Commence par l'inventaire de ta session, puis le jalon 1.
```

```text
<mission>
SpaceNotch est un projet open source gratuit, positionné « compagnon vivant » (ADR-029, acceptée).
main contient #63 à #77. Ouvertes à l'arrivée : #78 (n° 93, barre en haut), #79 (n° 94) et la clôture
du 2026-10-13 (soir).
À la fin de cette session, je veux :
1. #78, #79 et la clôture fusionnées, si je le demande en séance ;
2. n° 96 : finitions de la barre en haut — arrachement et raccrochage sous la barre prouvés à l'écran
   (glisser simulé), goutte calée sur la barre, épaules sous la barre ;
3. si SignPath a répondu : la signature dans release.yml (n° 82) ;
4. sinon : le prochain P1 court de la feuille de route, proposé par AskUserQuestion.
Décisions verrouillées : docs/ux/spacenotch-2.0.md §15 ; tableaux « Décisions » des relais du
2026-10-10 au 2026-10-13 (soir) ; ADR-030.
</mission>

<mes_constats>
1. Je te dirai si SignPath a accepté le projet.
2. Pour les preuves, tu peux lancer l'AppBar de test (40 DIP, retrait automatique), et changer dans ma
   config.json, après sauvegarde, puis restaurer : HideOverFullscreen, TopBarPlacement, DockEdge.
</mes_constats>

<criteres_de_reussite>
1. n° 96 : sous une barre, tirer la notch fait naître la pastille au bout de la forme étirée, sous la
   main ; la raccrocher en haut la pose sous la barre sans saut ; la goutte part du bas de la barre.
   Preuves : rafale recadrée (glisser par mouse_event), tests de Core si une logique y descend.
2. Si SignPath : n° 82.
Qualité : build Release sans avertissement, tests verts, /code-review et winui-reviewer sans bloquant,
CI verte sur chaque PR.
</criteres_de_reussite>

<perimetre>
Hors périmètre : release, tag, version ; Store en MSIX ; src/NotchFlow.*. Ce que tu découvres en
plus va dans la feuille de route ou le relais.
</perimetre>

<etat_du_depot>
Relis docs/plans/relais-2026-10-13-soir.md, la feuille de route et ADR-030. Revérifie (git fetch,
gh pr list, branche courante).
Pièges :
- une instance de développement partage réglages, journal et verrou avec ma notch : demande-moi de la
  fermer ; sauvegarde config.json avant, compare après ;
- la notch se retire quand Windows signale une application en plein écran
  (SHQueryUserNotificationState = 2) ou qu'une fenêtre couvre tout l'écran ;
- computer-use masque les fenêtres non autorisées (dont l'AppBar de test) et peut replier la notch :
  pour les preuves de la barre, captures System.Drawing (processus DPI-aware) et clics par UIA ;
- une question AskUserQuestion fait apparaître dans la notch de développement une carte d'autorisation
  (« Autoriser / Refuser ») : ne jamais la toucher, relancer l'instance pour l'effacer ;
- après 30 s sans entrée réelle, Pixel s'assoupit : SetCursorPos ne compte pas, mouse_event si ;
- la notch ouverte ne se referme pas quand le pointeur s'en va : Win+Alt+N puis Échap ;
- la sortie française de dotnet test contient des espaces insécables : grep -E "chec[^0-9]*0," ;
- PowerShell 5.1 lit un script UTF-8 sans BOM comme ANSI : pas d'accents dans les scripts UIA ;
- PowerShell : pas de méthode statique nommée New (conflit avec ::new) ; un champ de structure
  imbriquée se modifie sur une copie — passer par un petit bloc C# ;
- git show "origin/main:chemin" dans Git Bash : préfixer par MSYS_NO_PATHCONV=1 ;
- SettingsService.Update modifie les réglages sur place : comparer à une photo (AppliedSettings) ;
- winui-reviewer n'a pas Bash : mettre la branche en copie de travail avant (pas de worktree) ;
- le PATH de Bash peut arriver au format Windows ; écrire les scripts Python avec l'outil Write ;
- un script qui supprime des lignes de la feuille de route par numéro ne vise que les listes à faire.
</etat_du_depot>

<jalons>
1. État des PR ; fusions si je les demande.
2. n° 96.
3. n° 82 si SignPath ; sinon le prochain P1 court.
4. Clôture.
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
demander ; fermer ou relancer ma notch sans me le demander ; changer un réglage système.
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
