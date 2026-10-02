# ADR-024 — Canal local, hooks de Claude Code et capture de texte

- **Statut** : accepté (vague 6b, septembre 2026)
- **Contexte** : la vague 6b fait entrer dans la notch ce qui se passe *ailleurs* : un
  agent IA dans un terminal (I4), un build ou un script (W1), du texte dans une image
  (W4). Il faut un moyen pour un autre processus de parler à la notch, et un moyen de lire
  l'écran. Ce sont deux capacités nouvelles ; aucune n'ajoute de capacité au manifeste.

## Décision 1 — Un tube nommé, réservé à l'utilisateur

- La notch écoute sur `\\.\pipe\SpaceNotch.Channel.<utilisateur>.<session>`, ouvert avec
  `PipeOptions.CurrentUserOnly` : les autres comptes et les autres sessions ne peuvent
  pas s'y connecter. **Rien n'écoute sur le réseau.**
- Le protocole est une ligne JSON par connexion (`ChannelProtocol`) : `progress`,
  `agent`, `notify`, `clear`. Tout est borné : identifiant `[a-z0-9._-]{1,40}`, textes coupés à
  80 caractères sans caractères de contrôle, 12 étapes au plus, ligne de 4 096
  caractères au plus, lecture abandonnée après 5 s. Un message mal formé est ignoré.
- Seule une question d'agent (`state: waiting` avec `question`) attend une réponse :
  la connexion reste ouverte jusqu'au clic sur « Autoriser » ou « Refuser », au plus
  60 s, et le serveur répond `allow` ou `deny`.
- Le client est l'exécutable lui-même : `SpaceNotch.exe --progress …` et
  `SpaceNotch.exe --hook`. Ces commandes sont traitées dans un `Main` écrit à la main
  (`DISABLE_XAML_GENERATED_MAIN`), **avant WinUI** : un hook s'exécute à chaque message
  de l'agent et doit rendre la main sans charger de XAML. Elles ne font jamais échouer
  l'appelant : sans notch, elles sortent à 0 en silence.
- `SpaceNotch.exe --notify --title … [--body …] [--source …] [--open <cible>]
  [--open-label …] [--done|--error]` : un script prévient. Avec `--open`, la carte
  propose « Ouvrir » (ou le libellé donné) et « Ignorer ». La cible n'est ouverte qu'au
  clic, et seulement si c'est un chemin absolu (`C:\…`, `\\serveur\…`) ou une page
  `http(s)` : ni commande, ni protocole d'application, ni chemin relatif.

## Décision 2 — Les hooks de Claude Code, installés à la demande

- Rien n'est installé d'office. Réglages › Agents IA › **Installer** ajoute à
  `~/.claude/settings.json` quatre hooks (`UserPromptSubmit`, `PermissionRequest`,
  `Notification`, `Stop`) qui appellent `"<SpaceNotch.exe>" --hook`. Le fichier est
  d'abord copié en `settings.json.spacenotch.bak` ; les autres hooks sont conservés ;
  un fichier illisible n'est pas touché. **Retirer** enlève ces hooks et eux seuls.
- `PermissionRequest` : la notch s'ouvre sur l'outil demandé (« Bash · dotnet test »).
  La réponse part vers Claude Code sous la forme
  `{"hookSpecificOutput":{"hookEventName":"PermissionRequest","decision":{"behavior":"allow|deny"}}}`.
  Sans réponse en 55 s, le hook se tait et Claude Code pose sa question dans le
  terminal, comme sans SpaceNotch. La notch ne décide jamais seule.
- Ce qui transite : le nom du dossier de travail, le nom de l'outil et le début de sa
  commande ou de son fichier. Pas de contenu de fichier, pas de transcription.

## Décision 3 — Capture de texte par l'OCR de Windows

- La capture (« ocr », « texte » dans la recherche, ou le menu contextuel) copie le
  moniteur sous le pointeur par GDI, **une fois**, et l'affiche figé dans une fenêtre
  plein écran où l'on trace un rectangle. Le morceau choisi est lu par
  `Windows.Media.Ocr`, **hors ligne**, dans les langues installées sur le poste ; le
  texte va au presse-papier.
- L'image ne quitte jamais la mémoire : rien n'est écrit sur le disque ni envoyé.
- Sans langue OCR installée, la notch le dit au lieu d'échouer en silence.

## Conséquences

- Un script peut dire à la notch où il en est, sans SDK : une ligne de commande suffit.
- Tout processus de l'utilisateur peut afficher une activité dans la notch — c'est le
  but. Il ne peut rien faire d'autre : pas d'action, pas de lecture, et une question
  n'aboutit qu'à un clic de l'utilisateur.
- Le `Main` manuel reproduit celui que XAML génère ; une évolution du modèle WinUI
  devra y être reportée.

## Complément — Clawd, la mascotte de Claude Code

- Pour Claude Code, la notch montre **Clawd** à la place de la grille qui tourne
  (`SpaceNotch.Core.Motion.Clawd`) : la silhouette de la bannière du terminal, rangées
  doublées (un pixel du terminal est deux fois plus haut que large), yeux de 2 × 2.
  Quatre humeurs : réfléchit, demande, terminé, erreur ou réponse attendue dans le terminal.
- Trois rendus au choix dans Réglages › Apparence › Clawd : **A · fidèle** (carrés pleins,
  par défaut), **C · entre les deux** (coins arrondis, joint presque invisible),
  **B · matière SpaceNotch** (pixels ronds, filigrane).
- Clawd appartient à Anthropic et n'a pas de licence publique. SpaceNotch est gratuit,
  ne vend rien à son effigie, n'utilise aucun logo d'Anthropic et dit dans les réglages
  qu'il n'est pas affilié à Anthropic. Les autres agents gardent la grille.
