# Feuille de route — projet gratuit et open source (re-triée le 2026-10-06, mise à jour le 2026-10-10)

Première version : 2026-10-04, écrite « pour vendre ». L'auteur l'a précisé le 2026-10-05 :
SpaceNotch reste **gratuite et open source**, sans vente. Cette version re-trie tout pour ce cadre.
Les sujets de vente restent listés, marqués **sans objet**, pour qu'on sache pourquoi ils ont disparu.

Priorité = impact pour l'utilisateur × effort × risque.
- **P0** : se voit dès la première minute, ou fait perdre confiance.
- **P1** : gêne un usage normal.
- **P2** : qualité, dette, confort.

Effort : S (≤ 1 jour), M (quelques jours), L (une semaine et plus).
Preuves : [constats du 2026-10-03](2026-10-03-constats.md), enquête du 2026-10-06 (direction : voir
[le document de direction](2026-10-06-direction.md)). Les constats marqués ✔ ont été revérifiés dans le code.

## Fait depuis le 2026-10-04

| # | Sujet | PR | État |
|---|---|---|---|
| 1 | Repos trop large | #38 | fusionnée |
| 2 | La copie occupe la notch | #39 | fusionnée ; à la relecture, deux défauts du signal ont été corrigés |
| 3 | Zones vides et sauts | #41 | fusionnée ; à la relecture, le saut de 34 DIP au repli d'une scène à onglets a été corrigé |
| 8 | Gestes indécouvrables | #43, #44 | fusionnées ; ADR-028 acceptée ; la leçon de geste désaccordée a été corrigée |
| 13 | CPU de la notch retirée | #42 | fusionnée |
| 45 | Un build de développement mettait à jour l'installation | #40 | fusionnée |
| 33 | Exception non gérée = notch fermée | #45 | **ouverte** : minuteurs, travail posté, `async void`, minuteurs des fonctionnalités ; preuve `--fault-test` |
| 7 | Mise à jour ratée = notch disparue | #46 | **ouverte** : relance dans tous les cas, carte, report ; preuve `--fault-install` |
| 47 | CPU quand un agent travaille | #47 | **ouverte** : reflet à 30 images/s (15–22 % → ≤ 3 %) ; Clawd inchangé, par décision |
| 46 | GC pendant le ressort | #48 | **ouverte** : cause mesurée = GC de génération 2 *demandés* après la création d'objets XAML à chaque image ; tracés réutilisés ; visite : 23,9 % → 14,1 % d'images en retard, 125 → 14 images > 16,7 ms, 0 > 50 ms (une fois sur trois : 74,7 ms à la première ouverture du menu rapide) |
| 48–50 | Minuteur sans nom, boucles notch retirée, menu rapide | #49 | **ouverte** : 2 → 0 bouton sans nom (UIA) ; boucles arrêtées au retrait (journal) ; une seule source pour le menu |
| 53, 10, 69 | README, confidentialité, communauté | #50 | **ouverte** ; page GitHub déjà réglée (description, topics, Discussions, signalement privé) |

## Sans objet pour un projet gratuit

| # | Sujet | Pourquoi |
|---|---|---|
| 6 | Modèle de vente | pas de vente |
| 38 | Licence, essai, activation | pas de vente |
| 41 | Remboursement, droit de rétractation | pas de vente ; le support et la FAQ restent, voir n° 56 |
| — | Paddle, Lemon Squeezy, TVA (OSS), compte entreprise du Store | pas de vente |

## P0 — se voit dès la première minute, ou fait perdre confiance

| # | Sujet | Preuve | Impact | Effort | Risque |
|---|---|---|---|---|---|
| 4 | **Non signé** : SmartScreen « Windows a protégé votre PC » à chaque version ; le Contrôle intelligent des applications bloque | `release.yml:115` ✔ ; [SmartScreen](https://learn.microsoft.com/windows/apps/package-and-deploy/smartscreen-reputation) | fort | M | faible (SignPath) |
| 51 | **L'installeur réécrit l'exécutable installé** (`IdentityManifestFile.Set`) : toute signature serait invalidée. Préalable à n° 4 : produire deux variantes signées dans la CI | `WindowsSetup.cs:286` ✔ | fort (bloque n° 4) | M | moyen |
| 5 | Une demande d'administrateur à chaque mise à jour (certificat éphémère par version) | `release.yml:115-135` ✔ | fort | M | lié à n° 4 |
| 52 | **Mise à jour automatique par défaut, sans signature indépendante de GitHub** ; 27 versions en 10 jours installées seules | `UpdateClient.cs:90` ✔ (empreinte prise dans la même release) ; `AppSettings.cs:591` | fort (confiance) | S–M | moyen |
| 53 | **Le README promet une « recherche de mises à jour »** alors que le réglage par défaut installe seul ; et « < 0,1 % CPU », démenti par les mesures | `README.md:80,82` ✔ ; `performance.md:57` | fort (confiance) | S | faible |
| 54 | **Pixel actif par défaut**, réactivé une fois par migration : contredit « Alive, never busy » (§15 : aucune animation permanente) | `AppSettings.cs:569, 833-836` ✔ | fort (identité) | S | décision (direction) |
| 9 | **Clawd**, élément de marque d'Anthropic : fidèle par défaut, impossible à désactiver ; les règles de marque d'Anthropic n'ont pas d'exception « gratuit » | `Clawd.cs:20-30` ✔ ; ADR-024:75 ; [Anthropic trademark guidelines](https://www.anthropic.com/legal/trademark-guidelines) | moyen (juridique) | S–M | décision (direction) |
| 10 | Page de confidentialité (`PRIVACY.md`) : ce qui sort de la machine, et quand | aucun fichier | moyen | S | faible |
| 55 | **Hooks Claude Code peut-être muets** : `SpaceNotch.exe --hook` lancé par Git Bash n'atteint pas le tube d'une notch de développement ; écrit directement par PowerShell, le même message passe (2026-10-06). Hypothèse à tester : le client ouvre le tube avec `PipeOptions.CurrentUserOnly`, qui exige que le propriétaire du serveur soit l'utilisateur courant, ce qu'une notch lancée avec une identité de paquet pourrait ne pas remplir. Test : la notch installée, et un vrai tour de Claude Code | `ChannelPipe.cs:46` ; mesure n° 47 | fort (pour les utilisateurs de Claude Code) | S | faible |

## P1 — gêne un usage normal

| # | Sujet | Preuve | Impact | Effort | Risque |
|---|---|---|---|---|---|
| 46b | Suite de n° 46 : première mise en page d'une scène pendant le ressort (74,7 ms une fois) ; tableaux de points du ressort (GC de génération 0) ; scènes à créer d'avance (n° 25) | #48 | moyen | M | moyen |
| 11 | La notch disparaît derrière une fenêtre « toujours devant » qui couvre l'écran | `FullscreenPresenceWatcher.cs:357` ✔ | fort ? | S–M | régression plein écran |
| 12 | Notifications Windows impossibles à activer après coup | `SetupWindow.xaml.cs:546` | moyen | M | faible |
| 15 | Textes en français dans l'interface anglaise | `ChannelFeature.cs:86`… | moyen | S | faible |
| 16 | Désinstallation incomplète (dossier `updates`, coffre, certificat, hooks) | `InstallLayout.cs:94-116` | moyen | S–M | faible |
| 17 | Mise à jour qui réapplique les choix d'installation (démarrage, raccourci) : **non constaté** sur l'installation de l'auteur (choix identiques), code à revoir | `WindowsSetup.cs:207, 306` | moyen | S | faible |
| 18 | Miroir de la webcam actif par défaut | `AppSettings.cs:545` ✔ | moyen | S | faible |
| 19 | Alt+Espace pris globalement | `GlobalHotkey.cs:45` | moyen | S | faible |
| 20 | Installeur : échec sans raison ; deux UAC de suite en « pour tous » | `SetupWindow.xaml.cs:540-599` | moyen | S–M | faible |
| 21 | Notch minimisée par un tiers : ne revient pas | `IslandWindow.xaml.cs:280` ✔ | moyen ? | S | faible |
| 22 | Mouvement réduit : coupure sèche au lieu du fondu promis | `IslandController.cs:851` ✔ | moyen (a11y) | S | faible |
| 34 | Écran mémorisé par `HMONITOR`, invalide après redémarrage | `AppSettings.cs:212` | moyen | S | faible |
| 35 | Veille et reprise non traitées | `IslandWindow.PixelLife.cs:133-172` | moyen | S–M | faible |
| 56 | **Aucun chemin pour signaler un problème** : le bouton « Fichiers » ouvre `%AppData%`, le journal est dans `%LocalAppData%` ; pas de modèle d'issue | `SettingsWindow.xaml.cs:1159` ; `MiniLogger.cs:43-48` | moyen | S | faible |
| 57 | **Réglages en contraste élevé** : cartes et descriptions en couleurs codées en dur | `Controls.xaml:284-309` | moyen (a11y) | M | faible |
| 58 | **Narrateur** : le titre de carte écrase l'action des boutons des Réglages (« Retirer » lu « Clé Claude ») ; barre de lecture sans nom ; raccourci « aller à la notch » jamais montré | `SettingsWindow.xaml.cs:1344` ; `MediaExpandedScene.xaml:83` ; `GlobalHotkey.cs:80` | moyen (a11y) | S | faible |
| 59 | Taille du texte de Windows (jusqu'à 225 %) ignorée | aucun `TextScaleFactor` dans `src` | moyen (a11y) | M | moyen |
| 60 | Barre des tâches en haut (StartAllBack, ExplorerPatcher) : la notch la recouvre — touche la règle n° 1 du §15, ADR nécessaire | `IslandWindow.xaml.cs:2227-2230` | moyen | M | décision |
| 61 | Présentation du premier lancement : enseigne encore le clic droit, pas les tuiles d'ADR-028 ; README français faux sur le clic droit | `WelcomeScene.xaml.cs:85-87` ; `README.fr.md:111` | moyen | S | faible |
| 62 | Tuile Minuteur : lance 15 min sans le dire ; écrase un minuteur en pause | `QuickMenuTiles.cs:32` ; `IslandWindow.QuickMenu.cs:110` | moyen | S | faible |
| 63 | Tuile Presse-papier grisée sur une installation neuve (historique éteint par défaut) | `AppSettings.cs:469` ; `LauncherScene.xaml.cs:1332` | moyen | S | décision |
| 64 | Molette au repos : avec « Survoler pour aperçu » éteint, la pile du presse-papier est injoignable | `IslandWindow.xaml.cs:3251` | faible | S | faible |
| 65 | Réglages « Démarrer avec Windows » affiché activé alors que Windows l'a désactivé (Gestionnaire des tâches) | constaté sur l'installation de l'auteur le 2026-10-05 | moyen | S | faible |
| 66 | Une copie de développement peut encore réécrire la clé de démarrage partagée (Réglages ouverts depuis le build) | `SettingsWindow.xaml.cs:387` | faible (dev) | S | faible |
| 76 | Tutoiement partout (décision du 2026-10-06) : installeur (`SetupText.cs`) et textes restants au vous | `SetupText.cs:102-131` | moyen | S | faible |
| 77 | « Me prévenir » par défaut pour les mises à jour (décision du 2026-10-06), puis README à ajuster | `AppSettings.cs:591` | fort (confiance) | S | faible |
| 78 | Minuteur en pause annoncé « Démarrer » (pas « Reprendre ») ; boutons de 30 DIP | relecture de #49 | faible (a11y) | S | faible |
| 79 | ADR-029 à appliquer : Clawd retiré, Pixel avatar des agents (humeurs), README et langage visuel autour de Pixel | ADR-029 | fort (identité) | M | moyen |
| 36 | Coût par image du ressort (géométries neuves, mises en page) | voir n° 46 | (cause de 46) | M | moyen |
| 37 | Caméra du miroir, crête-mètre, cache d'icônes sans borne | `IslandWindow.Phone.cs:225` | faible | S | faible |
| 40 | Remontée des plantages sur accord explicite | `MiniLogger` | moyen | M | vie privée |
| 42 | Faux positifs antivirus | `IdentityManifestFile.cs:23-56` | moyen | M | réputation |
| 43 | Consommation sur batterie non mesurée ; économiseur d'énergie ignoré | aucun `EnergySaverStatus` | moyen (portables) | S | faible |
| 44 | Carte « actions sur copie » qui revient | `CopyAssistFeature.cs:134` | faible | S | faible |
| 23 | Réglages d'expert exposés à tous ; ~79 interrupteurs sur 7 pages | `SettingsWindow.xaml:494…` | moyen | M | faible |

## P2 — qualité, dette, confort

| # | Sujet | Preuve | Effort |
|---|---|---|---|
| 39 | **Store en MSIX complet** : à étudier, pas à construire (voir direction) ; le Store n'accepte un installeur exe que signé | ADR-023 ; [exigences exe/MSI du Store](https://learn.microsoft.com/windows/apps/publish/publish-your-app/msi/app-package-requirements) | L |
| 67 | winget : possible dès maintenant (avec `--notifications=off`), plus simple une fois signé | `SetupCommand.cs:54` | S |
| 68 | ARM64 natif (PC Copilot+) | `release.yml:140` | M |
| 69 | Fichiers de communauté : CONTRIBUTING, SECURITY, code de conduite, modèles d'issue et de PR, signalement privé de vulnérabilités | `gh api community/profile` (42 %) | S |
| 70 | Rapide : arrêter un nouveau `TryEnqueue`/`Tick` nu (analyseur de symboles interdits — dépendance NuGet à approuver) | #45 | S |
| 71 | Après des fautes répétées dans le rendu, proposer de relancer plutôt que de continuer en silence | #45 | S |
| 72 | Reflet par le compositeur (~0 % du fil d'interface) | #47 | M |
| 73 | Test instable : `HudActivityTests.Muted_SaysSo_AndEmptiesTheLevel` dépend de la langue globale | session du 2026-10-05 | S |
| 74 | Moniteur CPU interrogé en continu, activé par défaut, doublon de l'échantillonneur de Pixel | `SystemMonitorFeature.cs:72` ; `PixelLife.cs:19` | S |
| 75 | Pomodoro en doublon du Minuteur ; Discord et Canal local sans description, activés par défaut | `AppSettings.cs:670-688` | S–M |
| 24 | Mémoire 214–311 Mo mesurés ; Native AOT non évalué | `performance.md:59` | L |
| 25 | Restes de la phase D | `performance.md:142-148` | M–L |
| 26 | Machine à états en ombre ; `IslandWindow` = 31 fichiers | audit §4 | L |
| 27 | Spotify et Discord exigent une application développeur | ADR-026 | L |
| 28 | Greffon d'exemple qui figeait l'application : état inconnu | `performance.md:68-75` | M |
| 29 | Fenêtre de réglages trop grande à 1366 × 768 ; tu/vous mélangés | `SettingsWindow.xaml.cs:148`, `SetupText.cs` | S |
| 30 | Documents périmés (README, performance.md, ADR-009 contre le manifeste, index des ADR) | enquête du 2026-10-06 | S |
| 31 | `capture.ps1` non DPI-aware | constats | S |
| 32 | ~529 couleurs écrites en dur | grep | S–M |
| 48 | Boutons du Minuteur sans nom pour Narrateur | `TimerScene.xaml:64-99` ✔ | S |
| 49 | Boucles non suspendues notch retirée (icône d'appel, accueil, paroles ; bulle et scène média) | relecture de #42 | S |
| 50 | Menu rapide à aligner sur les tuiles (une seule source) | ADR-028 | S |

## Ordre proposé

1. Fusionner #45 à #51 (à relire par l'auteur) ; après #45, envelopper les `Tick` et `TryEnqueue` ajoutés par #47 et #48 (`Guard.Tick`, `TryEnqueueSafely`).
2. Confiance : n° 55 (hooks), n° 77 (« Me prévenir »), n° 76 (tutoiement).
3. Signature : n° 51 puis n° 4 et n° 5 (SignPath Foundation en premier), puis n° 67 (winget).
4. Direction appliquée : n° 79 (ADR-029 : Pixel avatar, Clawd retiré), noyau visible par défaut (n° 23, 63, 74, 75).
5. Accessibilité : n° 57, 58, 59, 22.
6. Le reste des P1 courts.
