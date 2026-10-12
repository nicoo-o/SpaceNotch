# Relais — 2026-10-12 (nuit)

Session du 2026-10-12 au soir. Brief : celui du [relais du 2026-10-12 (soir)](relais-2026-10-12-soir.md).

## État

**`main`** contient **#63 à #74**, fusionnées à la demande de l'auteur (« fusionne tout »), dans
l'ordre du relais, par squash, chacune avec sa CI verte. Avant chaque fusion, `main` a été fusionné
localement dans la branche, puis construit en Release et testé. Trois conflits ont été résolus, puis
reconstruits et retestés avant d'être poussés :
- **#67** : `ShowHotkey()` (#65) et l'application des réglages avancés (#67) sont gardés tous les deux ;
- **#69** : `ClipboardOff` (#68) et `FocusActive` (#69) sont gardés tous les deux, dans
  `QuickMenuPayload` et dans le menu ;
- **#74** : `TickPeriod` (#64) et `byUser: true` (#74) sont gardés tous les deux ; 983 tests verts,
  trois passages sur trois.

**#70** (clôture) est fusionnée en dernier, avec ce relais.

**PR ouverte** : **#75** `feat/intensite-pixel`, l'intensité de Pixel :
- choix Calme / Normal / Expressif, Normal par défaut, choisi par l'auteur ;
- preuves : tests de Core ; UIA de la carte (page Notch, trois choix, Normal sélectionné) ;
- relue par `winui-reviewer`, un bloquant corrigé ;
- **pas vue à l'écran** en Calme ou en Expressif.

**Preuves à l'écran faites avec l'auteur** :
- **#68**, prouvée :
  - le menu rapide affiche « Presse-papier — désactivé », et la tuile du lanceur est active ;
  - les deux ouvrent la carte, « Activer » allume l'historique, « Plus tard » le laisse éteint ;
  - refermer la notch retire la carte.
  - *Corrigé pendant la preuve* : le sous-titre des cartes ne passait jamais à la ligne
    (`MaxLines=2` sans `TextWrapping`).
- **#69**, prouvée :
  - puces 5 min, 15 min et « Focus 25 min » ;
  - pendant un focus, « Arrêter le focus » et les puces 5 et 15 min ;
  - Discord et Canal local absents tant que les réglages avancés sont éteints.
  - *Corrigé pendant la preuve* : le menu ne grandissait pas pendant un focus, et « Quitter » était
    coupé ; la puce Focus touchait le chevron.
- **#73**, prouvée à 150 % : la notch reste nette, les Réglages grandissent sans rien couper.
- **#71 et #72** : l'auteur a choisi de passer ces preuves ; elles restent prouvées par les tests
  et par UIA seulement.

**Diagnostic corrigé** : les sessions précédentes avaient conclu à un « écran verrouillé ». C'était
faux.
- Les vraies raisons : la fenêtre Claude couvrait tout l'écran, et Windows signalait une
  application en plein écran (`SHQueryUserNotificationState` = 2). La notch se retirait donc.
- La capture ne montrait que le fond d'écran parce que computer-use masque les fenêtres non
  autorisées.
- Pour les preuves, avec l'accord de l'auteur, `HideOverFullscreen` et `ShowClipboard` ont été
  changés le temps des preuves.
- `config.json` et `config.json.bak` ont ensuite été restaurés, identiques (mêmes empreintes que la
  sauvegarde).

**Carte d'autorisation du hook** : une question AskUserQuestion fait apparaître dans la notch de
développement une carte « Claude attend ta réponse » avec « Autoriser » et « Refuser ». Ces boutons
répondent à une session Claude Code : n'y jamais toucher. Pour s'en débarrasser, relancer
l'instance de développement, et éviter les questions pendant qu'elle tourne.

**n° 82 (SignPath)** : pas de réponse ; rien d'écrit.

## Décisions de cette session

| Décision | Raison |
|---|---|
| Intensité de Pixel : Calme / Normal / Expressif, Normal par défaut ; Calme = Pixel du mouvement réduit ; Expressif = regard qui vagabonde quand le pointeur reste immobile, coups d'œil plus longs, bâillements deux fois plus fréquents | choix de l'auteur (direction §7, point 9) |
| Le mouvement réduit de Windows calme Pixel quelle que soit l'intensité | ADR-029 |
| n° 54 clos : Pixel reste actif par défaut | tranché par ADR-029 ; choix de l'auteur |
| Le sous-titre d'une carte passe à la ligne (deux lignes au plus) | `CardFit` le prévoyait déjà ; la proposition du presse-papier était coupée |

## Reste à faire

**À l'auteur.**
- #75 : la regarder en Calme et en Expressif, puis demander sa fusion.
- n° 92 : une tâche à part est proposée dans l'application (« Réparer la détection de changement de
  réglages »).
- SignPath (n° 82).
- Les décisions de la direction §7, points 2 à 8.

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
main contient #63 à #74. Reste ouverte : #75 (intensité de Pixel).
À la fin de cette session, je veux :
1. #75 prouvée à l'écran (Calme : Pixel immobile ; Expressif : il regarde autour de lui quand le
   pointeur ne bouge pas), puis fusionnée si je le demande en séance ;
2. n° 92 : un changement de raccourci de recherche ou d'écran dans les Réglages s'applique tout de
   suite (aujourd'hui, seulement au redémarrage) ;
3. si SignPath a répondu : la signature dans release.yml (n° 82) ;
4. sinon : les décisions de la direction §7 (points 2 à 8), proposées par AskUserQuestion.
Décisions verrouillées : docs/ux/spacenotch-2.0.md §15 ; tableaux « Décisions » des relais du
2026-10-10 au 2026-10-12 (nuit).
</mission>

<mes_constats>
1. Je te dirai si SignPath a accepté le projet.
2. Pour les preuves, tu peux changer dans ma config.json, après sauvegarde, et restaurer ensuite :
   PixelIntensity, HideOverFullscreen (la notch se retire sinon, Windows signalant une application
   en plein écran).
</mes_constats>

<criteres_de_reussite>
1. #75 : en Calme, Pixel ne cligne pas et regarde au centre ; en Expressif, pointeur immobile 4 s,
   le regard fait le tour. Preuve : rafale /snapshot recadrée sur la notch.
2. n° 92 : journal « Raccourci de recherche : … » juste après un changement dans les Réglages, sans
   redémarrage ; test de Core si une logique y descend.
3. Si SignPath : n° 82.
4. Direction : chaque point tranché par AskUserQuestion est écrit dans la direction et la feuille de
   route.
Qualité : build Release sans avertissement, tests verts, /code-review et winui-reviewer sans bloquant,
CI verte sur chaque PR.
</criteres_de_reussite>

<perimetre>
Hors périmètre : release, tag, version ; Store en MSIX ; src/NotchFlow.*. Ce que tu découvres en
plus va dans la feuille de route ou le relais.
</perimetre>

<etat_du_depot>
Relis docs/plans/relais-2026-10-12-nuit.md, la feuille de route et la direction. Revérifie (git fetch,
gh pr list, branche courante).
Pièges :
- une instance de développement partage réglages, journal et verrou avec ma notch : demande-moi de la
  fermer ; sauvegarde config.json avant, compare après ;
- la notch se retire quand Windows signale une application en plein écran
  (SHQueryUserNotificationState = 2) ou qu'une fenêtre couvre tout l'écran : ce n'est pas un écran
  verrouillé ; computer-use masque les fenêtres non autorisées, la capture ne montre alors que le fond ;
- une question AskUserQuestion fait apparaître dans la notch de développement une carte d'autorisation
  (« Autoriser / Refuser ») : ne jamais la toucher, relancer l'instance pour l'effacer ;
- la notch ouverte ne se referme pas quand le pointeur s'en va : Win+Alt+N puis Échap ;
- la sortie française de dotnet test contient des espaces insécables : vérifier « échec » avec
  grep -E "chec[^0-9]*0," ;
- PowerShell 5.1 lit un script UTF-8 sans BOM comme ANSI : pas d'accents dans les scripts UIA ;
- un élément replié (Collapsed) est absent de l'arbre UIA ; un élément hors de la vue y est ;
- computer-use : demander l'accès à « spacenotch.app.exe » (instance de développement) ;
- winui-reviewer n'a pas Bash : mettre la branche en copie de travail avant (pas de worktree) ;
- le PATH de Bash peut arriver au format Windows ; écrire les scripts Python avec l'outil Write ;
- un script qui supprime des lignes de la feuille de route par numéro ne vise que les listes à faire.
</etat_du_depot>

<jalons>
1. État des PR ; preuve de #75 avec moi.
2. n° 92.
3. n° 82 si SignPath ; sinon direction §7.
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
