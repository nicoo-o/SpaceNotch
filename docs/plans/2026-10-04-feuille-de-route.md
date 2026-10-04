# Feuille de route « prête à commercialiser » — 2026-10-04

Inventaire priorisé de ce qui reste à faire pour vendre SpaceNotch à des utilisateurs non
techniciens. Méthode : celle de l'audit d'octobre — un agent par dimension, en parallèle et en
lecture seule (restes des audits et ADR ; parcours d'un utilisateur lambda ; robustesse et
performance ; marché et prérequis d'une app payante, sur le web) ; constats recoupés ; les plus
graves revérifiés dans le code (marqués ✔). Mesures : [constats du 2026-10-03](2026-10-03-constats.md).

Priorité = impact pour l'utilisateur × effort × risque. **P0** : bloque la vente ou se voit dès
la première minute. **P1** : gêne un usage normal. **P2** : qualité, dette, confort.
Effort : S (≤ 1 jour), M (quelques jours), L (une semaine et plus).

## P0 — bloque la vente ou se voit dès la première minute

| # | Sujet | Preuve | Impact | Effort | Risque | Dans cette session ? |
|---|---|---|---|---|---|---|
| 1 | **Repos trop large** : « Encoche · Personnalisée » sans largeur applique 200 DIP ; l'heure + météo est plus étroite que le repos | constats §2 ✔ (`CameraCutout.cs:26`) | fort | S | faible | **oui** (sujet 1) |
| 2 | **La copie occupe la notch sans fin** : carte sans durée ni politique, une entrée par réécriture | constats §3 ✔ (`ClipboardFeature.cs:475`) | fort | M | faible | **oui** (sujet 2) |
| 3 | **Zones vides et sauts** : passage (400–800 ms de forme vide), repli (scène masquée à t = 0), saut de 2 images à l'ouverture de la recherche | constats §1 ✔ (`Handoff.cs:107`, `xaml.cs:1075`) | fort | M | moyen | **oui** (sujet 3) |
| 4 | **Exécutable et installeur non signés** : SmartScreen « Windows a protégé votre PC », « Éditeur inconnu » ; Contrôle intelligent des applications peut bloquer | `release.yml:115` ✔ (certificat auto-signé) ; [Microsoft — code signing options](https://learn.microsoft.com/windows/apps/package-and-deploy/code-signing-options) | fort | M–L | coût récurrent | non (décision de distribution, voir 6) |
| 5 | **Une demande d'administrateur à chaque mise à jour** (nouveau certificat éphémère à chaque version), y compris en mise à jour automatique, notch fermée | `release.yml:115-135` ✔, `WindowsSetup.cs:193` | fort | M | refus = notifications perdues | non |
| 6 | **Modèle de vente non tranché** : licence MIT, binaires publics sur GitHub, mise à jour depuis le dépôt public | `LICENSE` ✔, `UpdateRules.cs:56` | fort | — (décision) | juridique | décision à prendre |
| 7 | **Mise à jour ratée = notch disparue** : relancée seulement en cas de succès ; échec ou UAC refusé sans message ; aucun retour arrière | `SetupRunner.cs:76-87` ✔, `WindowsSetup.cs:541-585` | fort | S–M | moyen | non |
| 8 | **Gestes indécouvrables** : menu, pile, note, étagère, détacher, aide Alt ; présentation rejouable seulement depuis les Réglages | constats §4 ✔ | fort | M | faible | **oui** (jalon 4) |
| 9 | **Clawd, mascotte d'Anthropic, justifiée par « SpaceNotch est gratuit »** | ADR-024:75 ✔ | faible (utilisateur) / fort (juridique) | S | juridique | non (à décider avant de vendre) |
| 10 | **Politique de confidentialité absente** (obligatoire au Store, même sans télémétrie) | [règlement du Store 10.5.1](https://learn.microsoft.com/windows/apps/publish/store-policies) ; aucun fichier | moyen | S | juridique | non |
| 13 | CPU à 26–37 % d'un cœur (fil temps critique) sur la 1.17.1, notch masquée ; non reproduit par les Réglages seuls (0,23–0,47 %). Cause probable : masquer la notch n'arrête ni la grille hypnotique, ni la respiration et la pluie de l'atmosphère, ni la boucle de l'accueil, ni Clawd (`SuspendLife` ne coupe que Pixel, l'aimant et la traîne) ✔ ; le Clawd de l'aperçu des Réglages n'est arrêté que sur `Unloaded` | `IslandWindow.xaml.cs:2247-2258` ✔, `HypnoticSurface.cs:331`, `AtmosphericSurface.cs:272-305`, `ClawdView.cs:58` | fort | S–M | faible | **oui** (F5, PR séparée) |
| 33 | Toute exception non gardée (minuteur, `async void` des Réglages) ferme l'application : `UnhandledException` journalise sans poser `Handled` | `App.xaml.cs:39-40` ✔ ; `SettingsWindow.xaml.cs:937, 1491` | fort (crash) | S | faible | non (court, à faire juste après) |
| 38 | **Licence, essai et activation** : rien dans le code (au-delà de la décision de modèle, n° 6) | aucun code de licence | fort | M | moyen | non |
| 39 | **Paquet MSIX complet pour le Store** : `runFullTrust` et capacités restreintes à justifier ; mise à jour GitHub et hooks `~/.claude` à désactiver dans cette version | ADR-023 (identité seule) | fort | L | élevé | non (ADR) |

## P1 — gêne un usage normal

| # | Sujet | Preuve | Impact | Effort | Risque |
|---|---|---|---|---|---|
| 11 | La notch disparaît derrière une fenêtre « toujours devant » qui couvre l'écran : `QUNS_BUSY` suffit, sans vérifier que le premier plan couvre l'écran ; et rien ne relit l'état quand cette fenêtre se ferme sans changer de premier plan | constats, accroche 1 ✔ (`FullscreenPresenceWatcher.cs:357`) | fort ? (dépend des apps installées) | S–M | régression plein écran |
| 12 | Notifications Windows impossibles à activer après coup (case décochée ou UAC refusé) ; réglages et présentation se contredisent | `SetupWindow.xaml.cs:546`, `SettingsWindow.xaml.cs:1481`, `WelcomeScene.xaml.cs:100` | moyen | M | faible |
| 14 | Fluidité : 25 % d'images en retard à 240 Hz ; 112 des 129 images > 16,7 ms dans le ressort de forme (`ApplyGeometry`) | constats §1 | moyen | L | élevé (DPI, multi-écran) |
| 15 | Textes en français dans l'interface anglaise (Activités, menu de la zone de notification, recherche, erreur fatale, titre des réglages, notes de version) | `ChannelFeature.cs:86` et suivants | moyen | S | faible |
| 16 | Désinstallation incomplète : dossier `updates` (~140 Mo), coffre Windows, certificat, hooks `~/.claude/settings.json` pointant vers un exe supprimé | `InstallLayout.cs:94-116`, `UpdateClient.cs:36` | moyen | S–M | faible |
| 17 | Mise à jour qui réapplique les choix d'installation (raccourci bureau, démarrage avec Windows) | `WindowsSetup.cs:207, 306` | moyen | S | faible |
| 18 | Miroir de la webcam actif par défaut (la caméra s'allume au survol de « Rejoindre ») | `AppSettings.cs:545` ✔ | moyen (confiance) | S | faible |
| 19 | Alt+Espace pris globalement (remplace le menu système des fenêtres) ; la présentation affiche Alt+Espace même si un autre raccourci a été retenu | `GlobalHotkey.cs:45`, `WelcomeScene.xaml.cs:198` | moyen | S | faible |
| 20 | Installeur : échec sans raison ni journal ; deux UAC de suite en « pour tous » | `SetupWindow.xaml.cs:540-599`, `WindowsSetup.cs:160-165` | moyen | S–M | faible |
| 21 | Notch minimisée par un tiers (ou Win+D, à vérifier) : aucune garde, elle ne revient pas | `IslandWindow.xaml.cs:280` (seul `IsMinimizable = false`) ✔ | moyen ? | S | faible |
| 22 | Mouvement réduit : coupure sèche au lieu du fondu promis ; changement Windows non suivi à chaud | `IslandController.cs:841-852`, `MotionPresets.cs:171` | moyen (accessibilité) | S–M | faible |
| 34 | Écran choisi mémorisé par `HMONITOR` : invalide après redémarrage ou rebranchement, repli sur un écran fictif 1920 × 1080 à 96 DPI | `AppSettings.cs:212`, `DisplayManager.cs:162-173` | moyen (multi-écrans) | S | faible |
| 35 | Ni mise en veille ni reprise traitées (`WM_POWERBROADCAST`) ; au verrouillage, les animations de composition continuent | `IslandWindow.PixelLife.cs:133-172` | moyen | S–M | faible |
| 36 | Coût par image du ressort : résolution d'écran Win32, 2–3 `PathGeometry` neuves (reflet jamais en cache), deux passes de mise en page, deux surfaces de composition, déplacement de la bulle | `IslandSpringAnimator.cs:244-299`, `IslandGeometryFactory.cs:84-90`, `AtmosphereWindow.xaml.cs:293-355` | (cause de 14) | M | moyen |
| 37 | Caméra du miroir rendue seulement à la sortie de survol ; crête-mètre réactivé toutes les 5 s ; cache d'icônes du lanceur sans borne | `IslandWindow.Phone.cs:225-267`, `AudioPeakMeter.cs:35`, `LauncherIconCache.cs:26` | faible | S | faible |
| 40 | Remontée des plantages sur accord explicite (sans télémétrie par défaut) : aujourd'hui seul le journal local existe | `MiniLogger` | moyen | M | vie privée |
| 41 | Support, FAQ, procédure de remboursement (droit de rétractation de 14 jours sauf renonciation expresse) | [Your Europe](https://europa.eu/youreurope/citizens/consumers/shopping/returns/index_en.htm) | moyen | S | juridique |
| 42 | Faux positifs antivirus à surveiller (écoute du presse-papier, raccourci global, `cmd`/`powershell` cachés de l'installeur, octets réécrits dans l'exe) | `IdentityManifestFile.cs:23-56`, `SelfDelete.cs:39-53` | moyen | M | réputation |
| 43 | Consommation sur batterie non mesurée | — | moyen (portables) | S | faible |
| 44 | Carte « actions sur copie » (12 s) qui redevient visible une fois la carte du presse-papier partie | `CopyAssistFeature.cs:134` | faible | S | faible |
| 23 | Réglages d'expert exposés à tous (hooks, applis développeur Spotify/Discord, physique du détachement) ; « Canal local » sans description | `SettingsWindow.xaml:494…1413` | moyen | M | faible |

## P2 — qualité, dette, confort

| # | Sujet | Preuve | Effort |
|---|---|---|---|
| 24 | Mémoire 214–311 Mo mesurés (doc : 96–99) ; Native AOT non évalué | constats ; `performance.md:59` | L |
| 25 | Restes de la phase D : scènes créées d'avance, bulle au démarrage, visages et passages à durée fixe, pas de toile hors du haut | `performance.md:142-148` ; `IslandWindow.xaml:471-487` | M–L |
| 26 | Machine à états toujours en ombre ; `IslandWindow` = 31 fichiers, ~12 400 lignes ; pas d'`AppHost` | audit §4 E/F | L |
| 27 | Spotify et Discord exigent que l'utilisateur crée sa propre application développeur | ADR-026 | L (dépend des tiers) |
| 28 | Greffon d'exemple qui figeait l'application : état inconnu | `performance.md:68-75` | M |
| 29 | Fenêtre de réglages (920 × 660) trop grande pour 1366 × 768 à 125 % ; icône de zone de notification cachée par défaut sous Windows 11 ; tutoiement / vouvoiement mélangés | `SettingsWindow.xaml.cs:148`, `SetupText.cs` | S |
| 30 | Documents périmés : README (CPU, UAC « une fois »), `project-overview.md`, `state-machine.md`, `design-language.md`, `windows-integration.md`, `plugin-api.md`, index des ADR (ADR-011 en double) | rapport de l'agent « restes » | S |
| 31 | `tools/ui-snapshot/capture.ps1` non DPI-aware (ne capture que 1 707 px à 150 %) | constats, inventaire | S |
| 32 | Pixel sonde le réseau toutes les 2 s ; ~529 couleurs écrites en dur | audit-10 §2.1 ; grep | S–M |

## Marché et prérequis d'une app payante (recherche du 2026-10-04)

**Apps équivalentes.**
- [NotchNook](https://www.imore.com/apps/mac-apps/this-dollar25-app-gives-my-macbook-pro-a-dynamic-island-and-it-was-worth-every-penny) (macOS) : 25 $ à vie ou 3 $/mois, essai de 48 h. Prix d'après des tests de 2024.
- [Boring Notch](https://github.com/TheBoredTeam/boring.notch) : gratuite et open source, mais non signée (macOS avertit à l'installation).
- [Notchify](https://www.windowscentral.com/software-apps/notchify-brings-macos-style-dynamic-island-flair-to-windows-11) : le concurrent direct sous Windows, vendu 1,99 $ sur le Microsoft Store. Critiquée pour sa CPU au repos.
- [DynamicWin](https://github.com/FlorianButz/DynamicWin) : gratuite, mais des performances jugées faibles.

Aucune de ces apps ne documente comment elle enseigne ses gestes : c'est **un créneau libre**.

**Distribution et signature.**
- Le **Microsoft Store**, avec un paquet MSIX, re-signe l'app gratuitement et n'affiche **jamais** d'avertissement SmartScreen ([Microsoft Learn](https://learn.microsoft.com/windows/apps/package-and-deploy/smartscreen-reputation)).
- Hors Store, Artifact Signing (~9,99 $/mois) est fermé aux particuliers de l'UE. Il reste ouvert à une organisation de l'UE qui a au moins trois ans d'historique fiscal ([Microsoft Learn](https://learn.microsoft.com/windows/msix/package/signing-package-overview)). Sinon, il faut un certificat OV (150 à 300 $/an), et l'avertissement persiste plusieurs semaines quand même.
- L'inscription au Store est gratuite. La commission est de 15 %, ou 0 % si l'app passe par son propre système de paiement ([Microsoft Learn](https://learn.microsoft.com/windows/apps/publish/publish-your-app/why-distribute-through-store)).
- Le règlement du Store impose un compte entreprise à quiconque agit dans le cadre d'une activité professionnelle, ainsi qu'une politique de confidentialité ([règlement v7.20](https://learn.microsoft.com/windows/apps/publish/store-policies)).

**Paiement hors Store.**
- Paddle et Lemon Squeezy prennent 5 % + 0,50 $ et collectent la TVA européenne en tant que marchand de référence.
- Sans marchand de référence, il faut s'inscrire au guichet unique de TVA (OSS).

**Conséquence pour SpaceNotch.** L'architecture actuelle combine un installeur autonome, un paquet d'identité qui sert seulement aux notifications, et une mise à jour depuis GitHub. Le passage au Store demande un **paquet MSIX complet**. C'est le chantier qui supprimerait à la fois les P0 4, 5 et 7. Il touche l'installeur et la mise à jour, que le brief exclut du périmètre : il faut un ADR et ta décision.

**Références UX pour la découvrabilité (jalon 4).**
- [Apple HIG, Live Activities](https://developer.apple.com/design/human-interface-guidelines/live-activities) : animer les éléments vers leur nouvelle place plutôt que de les retirer puis les remettre ; une alerte importante déplie brièvement l'île.
- [Fluent 2](https://fluent2.microsoft.design/motion) et le [TeachingTip](https://learn.microsoft.com/windows/apps/design/controls/dialogs-and-flyouts/teaching-tip) : un conseil par geste, de 3 à 5 mots, réparti sur plusieurs sessions.
- [Material 3](https://github.com/material-components/material-components-android/blob/master/docs/theming/Motion.md) : la « container transform » transforme le compact en vue dépliée ; les ressorts d'effet (opacité, couleur) ne dépassent jamais leur cible.

## Ordre proposé après cette session

1. Trancher le modèle de vente et la distribution (6, 9, 10), car ils conditionnent 4, 5 et 7. Recommandation : Microsoft Store en MSIX complet, ADR à écrire.
2. Fiabiliser la mise à jour et la désinstallation (7, 16, 17), tant que la distribution actuelle reste en place.
3. Régler les accroches P1 courtes (11, 12, 15, 18, 19, 21, 22).
4. Traiter la performance de fond (13, 14, 24, 25), mesure à l'appui.
