# Feuille de route — projet gratuit et open source (re-triée le 2026-10-06, mise à jour le 2026-10-19)

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
| 33 | Exception non gérée = notch fermée | #45 | fusionnée : minuteurs, travail posté, `async void`, minuteurs des fonctionnalités ; preuve `--fault-test` |
| 7 | Mise à jour ratée = notch disparue | #46 | fusionnée : relance dans tous les cas, carte, report ; preuve `--fault-install` |
| 47 | CPU quand un agent travaille | #47 | fusionnée : reflet à 30 images/s (15–22 % → ≤ 3 %) ; Clawd inchangé, par décision |
| 46 | GC pendant le ressort | #48 | fusionnée : cause mesurée = GC de génération 2 *demandés* après la création d'objets XAML à chaque image ; tracés réutilisés ; visite : 23,9 % → 14,1 % d'images en retard, 125 → 14 images > 16,7 ms, 0 > 50 ms (une fois sur trois : 74,7 ms à la première ouverture du menu rapide) |
| 48–50 | Minuteur sans nom, boucles notch retirée, menu rapide | #49 | fusionnée : 2 → 0 bouton sans nom (UIA) ; boucles arrêtées au retrait (journal) ; une seule source pour le menu |
| 53, 10, 69 | README, confidentialité, communauté | #50 | fusionnée ; page GitHub déjà réglée (description, topics, Discussions, signalement privé) |
| 55 | Hooks Claude Code | — | **vérifié, rien à corriger** (2026-10-10) : un vrai tour de `claude -p` s'affiche dans la notch installée (arbre UIA : « Claude Code · 0 s », puis « ✓ ») ; l'échec du 2026-10-06 ne touchait qu'une instance de développement (n° 80) |
| 51 | L'installeur réécrivait l'exécutable installé | #52 | fusionnée : la variante installée est refaite octet pour octet ; travail CI « Variantes de l'exécutable » : Authenticode « Valid » sur la téléchargée, l'installée et la refaite |
| 77 | « Me prévenir » par défaut | #53 | fusionnée : migration unique des configurations « Automatique » ; README et PRIVACY à ajuster après #50 |
| 76 | Tutoiement | #54 | fusionnée : installeur et derniers textes au vous |
| 58 | Narrateur des Réglages | #55 | fusionnée : les boutons gardent leur nom, l'aide dit la carte et sa description (UIA avant/après) ; barre de lecture nommée ; reste n° 81 |
| 79 | ADR-029 : Pixel avatar des agents, Clawd retiré | #56 | fusionnée : quatre humeurs capturées ; ADR-029 à passer « Accepté » après #51 ; README autour de Pixel : n° 83 |
| 78 | Minuteur en pause | #60 | fusionnée : annoncé « Reprendre », boutons de 32 DIP (UIA : 48 px à 150 %) ; la fin d'une session Pomodoro ne garde plus « Suspendre » |
| 81 | Raccourci « aller à la notch » | #61 | fusionnée : carte des Réglages, lue « Actif : Win+Alt+N » (UIA) |
| 77 | README et PRIVACY de « Me prévenir » | #58 | fusionnée |
| 83, 79 | ADR-029 acceptée ; README, langage visuel et §15 autour de Pixel | #59 | fusionnée : au repos, Pixel compris, 0,13 à 0,18 % d'un cœur (mesuré le 2026-10-11) |
| 84, 73 | Tests dépendant de la langue globale | #63 | fusionnée : les tests qui basculent la langue forment une collection sans parallélisme ; un test de garde refuse qu'un autre fichier la bascule (n° 87 : expression régulière, |=, &=, ^= compris) ; avant : 1 échec sur 10 passages, après : 10 sur 10 verts |
| 86 | Fins de session sans test | #64 | fusionnée : 4 tests, horloge injectée, battement joué à la main (le vrai neutralisé) ; défaire un correctif de #60 fait échouer 2 tests |
| 85 | Raccourci « aller à la notch » au Tab ; index « Aucun » | #65 | fusionnée : UIA, élément focalisable « Aller à la notch : Win+Alt+N », avec l'aide de la carte ; chercher « aucun » ne montre plus la carte |
| 74 | Deux échantillonneurs du processeur | #66 | fusionnée : une mesure partagée ; 13 mesures par minute au lieu de 44 (test) ; CPU au repos non concluant (bruit) |
| 23 | Réglages d'expert exposés à tous | #67 | fusionnée : interrupteur « Afficher les réglages avancés », éteint ; 23 cartes masquées, la recherche les trouve (UIA) |
| 63 | Tuile Presse-papier grisée | #68 | fusionnée : historique éteint, la tuile et le menu proposent de l'activer ; refermer vaut « Plus tard » ; **prouvé à l'écran** (et le sous-titre des cartes passe enfin à la ligne) |
| 75 | Pomodoro en doublon, Discord et Canal local | #69 | fusionnée : « Focus 25 min » dans le Minuteur ; Discord éteint par défaut, allumé en le reliant ; **prouvé à l'écran** (menu agrandi pendant un focus) |
| 22 | Mouvement réduit | #71 | fusionnée : la forme apparaît en fondu (140 ms) au lieu de sauter ; Pixel calmé, immobile, humeurs gardées (choix de l'auteur) ; tests de Core ; pas vu à l'écran |
| 57 | Réglages en contraste élevé | #72 | fusionnée : 10 jetons de thème, couleurs du système en contraste élevé ; UIA : sept pages ; pas vu en contraste élevé |
| 59 | Taille du texte de Windows | #73 | fusionnée : la notch garde la taille de son texte, les Réglages suivent (choix de l'auteur) ; **prouvé à l'écran à 150 %** |
| 88 | Minuteur et focus ensemble | #74 | fusionnée : ils alternaient chaque seconde en tête de pile ; test qui échoue sans le correctif |
| 54, direction §7.9 | Intensité de Pixel ; « Pixel actif par défaut » | #75 | fusionnée, **prouvée à l'écran** (Calme : yeux fixes ; Expressif : le regard fait le tour) : Calme / Normal / Expressif (choix de l'auteur), Normal par défaut ; le n° 54 est tranché par ADR-029 (Pixel vit au repos) |
| 92 | Raccourci ou écran changé dans les Réglages appliqué au redémarrage seulement | #76 | fusionnée : photo des réglages appliqués ; journal avant/après sur `main` et sur la branche ; un écran choisi pendant que la notch flotte est respecté |
| 93 | Barre des tâches en haut (ADR-030) | #78 | fusionnée : mesuré d'abord (notch de `main` à y = 0, sur la barre) ; détection par la zone de travail ; question une fois ; sous la barre (y = 60 sous une barre de 60 px) ; languette ; Réglages › Notch ; preuves avec une AppBar de test |
| 94 | Modèles d'issue et de PR bilingues | #79 | fusionnée |
| 95 | Protection de `main` sur GitHub | — | **faite par l'auteur** (2026-10-16) |
| 96 | Barre en haut : goutte, raccrochage, épaules | #81 | fusionnée : la goutte part du bas de la barre ; à la relecture, un bloquant (le raccrochage ne finissait jamais sous une barre plus haute que la forme) et un saut de 40 DIP corrigés ; épaules gardées (choix de l'auteur) ; **prouvé à l'écran** (glisser par `mouse_event`, journal « raccrochée au bord Top » 5 fois sur 5) |
| 18 | Miroir de la webcam actif par défaut | #82 | fusionnée : éteint sur une installation neuve ; une configuration existante garde sa valeur (comme Discord, n° 75) ; tests |
| 62 | Tuile Minuteur | #83 | fusionnée : « 15 min » affiché sur la tuile (capture) ; un minuteur en pause n'est plus écrasé, la tuile le montre (`HasMeasure`, test, **prouvé par UIA**) ; à la relecture, l'indice masqué et l'épingle relâchée devant un minuteur en cours |
| 15 | Noms des fonctionnalités en français dans l'interface anglaise | #84 | fusionnée : 23 noms par `Lang.T` ; test de garde |
| 56 | Aucun chemin pour signaler un problème | #85 | fusionnée : carte « Signaler un problème » (À propos) : dossier du journal, rapport GitHub prérempli (version, Windows), rien d'envoyé seul ; capture de la carte ; à la relecture, rangée sous « AIDE » au lieu d'« AVANCÉ », trouvable par « bug » |
| 11 | Notch disparue derrière une surcouche « toujours devant » | #87 | fusionnée : trois causes — surcouche traversable (le shell dit « occupé »), fenêtre **agrandie** prise pour un plein écran avec la barre des tâches masquée automatiquement, et notch jamais revenue (aucun événement à la fin du plein écran) ; la première fenêtre opaque de haut en bas décide ; journal `[PLEIN ÉCRAN]` avec la raison ; **prouvé à l'écran** (surcouche de test, plein écran opaque) |
| 21 | Notch réduite par un tiers : ne revenait pas | #88 | fusionnée : notch, halo et bulle rétablis, aussi après une réduction pendant un plein écran ; **prouvé** (réduction par un autre processus, relevés à 30 ms… 3 s) |
| 52 | Mise à jour vérifiée seulement par une empreinte publiée sur GitHub même | #89 | fusionnée : « Automatiques » retiré, rien ne s'installe sans l'utilisateur tant que rien n'est signé ([ADR-031](../decisions/ADR-031-rien-ne-s-installe-sans-l-utilisateur.md), choix de l'auteur) |
| 34 | Écran mémorisé par `HMONITOR` | #90 | fusionnée : le handle n'est plus lu ; l'écran se retrouve par son rectangle |
| 65, 66, 17 | « Démarrer avec Windows » faux ; copie de dev qui inscrit son chemin ; mise à jour qui réapplique les choix d'installation | #91 | fusionnée : `StartupApproved` lu ; l'exécutable installé visé ; la mise à jour passe les choix d'aujourd'hui ; **état à l'écran non vu** (registre virtualisé dans l'instance de dev) |
| 61 | Présentation du premier lancement | #92 | fusionnée : le clic et ses quatre raccourcis ; capture des cartes |
| 64 | Molette au repos sans aperçu au survol | #93 | fusionnée : la pose du pointeur suffit ; test |
| 97 | Goutte éteinte : celle du dernier arrachement redessinée | #94 | fusionnée |
| 35 | Veille et reprise | #95 | fusionnée : veille, reprise, et écran éteint (veille moderne S0, celle du PC de l'auteur) ; **prouvé** pour veille et reprise par messages simulés ; écran éteint non simulable |
| 16 | Désinstallation incomplète | #96 | fusionnée : installeurs téléchargés toujours effacés, hooks de Claude Code retirés |
| 20 | Installeur : échec muet, deux UAC | #97 | fusionnée : raison et chemin du journal affichés ; une seule élévation pour tous |
| 12 | Notifications Windows impossibles à activer après coup | #98 | fusionnée : « Activer » dans les Réglages, puis redémarrage de la notch |
| 19 | Alt+Espace pris globalement | — | **clos sans changement** : l'auteur garde Alt+Espace (2026-10-16) |
| 37, 43, 44 | Caméra du miroir, crête-mètre ; économiseur d'énergie ; carte « actions sur copie » qui revient | #102 | fusionnée : économiseur d'énergie = mouvement réduit automatique (choix de l'auteur) |
| 100 | Présentation, carte 3 : le raccourci réellement retenu | #103 | fusionnée |
| 99 | Test d'allocation instable | #104 | fusionnée |
| 89 | Notch agrandie avec la taille du texte de Windows | — | **clos sans changement** : l'auteur garde la notch à texte fixe (2026-10-19, voir n° 59) |

## Sans objet pour un projet gratuit

| # | Sujet | Pourquoi |
|---|---|---|
| 6 | Modèle de vente | pas de vente |
| 38 | Licence, essai, activation | pas de vente |
| 41 | Remboursement, droit de rétractation | pas de vente ; le support et la FAQ restent, voir n° 56 |
| — | Paddle, Lemon Squeezy, TVA (OSS), compte entreprise du Store | pas de vente |

## En pause — signature

Décision de l'auteur (2026-10-16) : SignPath n'a pas répondu, et aucune réponse n'est attendue pour
l'instant. La signature et ce qui en dépend sortent de l'ordre proposé ; on y reviendra si SignPath
répond ou si une autre voie de signature se présente.

| # | Sujet | Preuve | Impact | Effort | Risque |
|---|---|---|---|---|---|
| 82 | Signature par SignPath : étapes à poser dans `release.yml` **avant** l'extraction de la signature de l'installée (#52) ; PRIVACY.md : un installeur signé lit la signature de l'installée sur la release de sa version | #52 | fort (n° 4) | S | faible |
| 4 | **Non signé** : SmartScreen « Windows a protégé votre PC » à chaque version ; le Contrôle intelligent des applications bloque | `release.yml:115` ✔ ; [SmartScreen](https://learn.microsoft.com/windows/apps/package-and-deploy/smartscreen-reputation) | fort | M | faible (SignPath) |
| 5 | Une demande d'administrateur à chaque mise à jour (certificat éphémère par version) | `release.yml:115-135` ✔ | fort | M | lié à n° 4 |
| 67 | winget : **après la signature** (décision du 2026-10-13, direction §7.4) | `SetupCommand.cs:54` | — | S | — |
| 68 | ARM64 natif (PC Copilot+) : **plus tard, après la signature** (direction §7.5) | `release.yml:140` | — | M | — |

## P0 — se voit dès la première minute, ou fait perdre confiance

| # | Sujet | Preuve | Impact | Effort | Risque |
|---|---|---|---|---|---|

## P1 — gêne un usage normal

| # | Sujet | Preuve | Impact | Effort | Risque |
|---|---|---|---|---|---|
| 46b | Suite de n° 46. **PR #112 ouverte** : rendus découpés en étapes sous `--frames` ; onglets gardés dans l'arbre (« onglets·bande » 178–227 → 42–49 ms par visite) ; état du PC lu hors du fil d'interface (13 ms → 0). Reste : « scène remplie » ≈ 250 ms par visite (lanceur : `BuildRows` reconstruit toutes ses lignes), « forme compacte » ≈ 160 ms, GC de génération 2 « provoqués » sans `GC.Collect` (cause non établie), 0 à 2 rendus > 50 ms à l'ouverture du menu rapide ou du lanceur ; scènes à créer d'avance (n° 25) | #48, #112 | moyen | M | moyen |
| 36 | Coût par image du ressort (géométries neuves, mises en page) — voir n° 46b, PR #112 | voir n° 46 | (cause de 46) | M | moyen |
| 40 | Remontée des plantages sur accord explicite | `MiniLogger` | moyen | M | vie privée |
| 42 | Faux positifs antivirus | `IdentityManifestFile.cs:23-56` | moyen | M | réputation |

## P2 — qualité, dette, confort

| # | Sujet | Preuve | Effort |
|---|---|---|---|
| 90 | ~~Réglages : contraste élevé suivi seulement à la réouverture~~ — **PR #106 ouverte** (avec n° 29) | relecture de #72 | S |
| 91 | ~~Mouvement réduit : halo et ombre qui sautent ; « Rebond » éteint en fondu~~ — **PR #107 ouverte** | #71 | S |
| 98 | Écran changé pendant l'arrachement (0,34 s) : la trace se redessine sur le nouvel écran ; forcer la fin de la goutte au changement | relecture de #81, `IslandWindow.Detach.cs:809` | S |
| 80 | Une instance de développement (`winapp run`) ne reçoit pas `SpaceNotch.exe --hook` lancé par Git Bash, alors que la notch installée le reçoit ; cause non établie (identité de paquet, propriétaire du tube) | essais du 2026-10-06 et du 2026-10-10 | S |
| 39 | **Store en MSIX complet** : à étudier, pas à construire (voir direction) ; le Store n'accepte un installeur exe que signé | ADR-023 ; [exigences exe/MSI du Store](https://learn.microsoft.com/windows/apps/publish/publish-your-app/msi/app-package-requirements) | L |
| 70 | Rapide : arrêter un nouveau `TryEnqueue`/`Tick` nu (analyseur de symboles interdits — dépendance NuGet à approuver) | #45 | S |
| 71 | Après des fautes répétées dans le rendu, proposer de relancer plutôt que de continuer en silence | #45 | S |
| 72 | Reflet par le compositeur (~0 % du fil d'interface) | #47 | M |
| 24 | Mémoire 214–311 Mo. **PR #113 ouverte** : ligne `[MÉMOIRE]` au journal ; au repos, tas .NET 3 Mo, mémoire privée 125 Mo (native : WinUI, composition, pilote, runtime), ≈ 89 Mo partagés (DLL). Pistes : Native AOT (compatibilité WinUI à vérifier), coût des fenêtres cachées (halo, bulle), descripteurs (1 748) après une journée | `performance.md:59`, #113 | L |
| 25 | Restes de la phase D | `performance.md:142-148` | M–L |
| 26 | Machine à états en ombre ; `IslandWindow` = 31 fichiers. **PR #111 ouverte** : seul écart de l'usage récent corrigé (arrachée ou déplacée pendant l'aperçu). Reste : écarts « Presence = Withdrawn » plus anciens (version antérieure à #30 ? non établi), puis une région confiée à la machine, et le découpage | audit §4, #111 | L |
| 27 | Spotify et Discord exigent une application développeur | ADR-026 | L |
| 28 | Greffon d'exemple qui figeait l'application : état inconnu | `performance.md:68-75` | M |
| 29 | ~~Fenêtre de réglages trop grande à 1366 × 768~~ — **PR #106 ouverte** | `SettingsWindow.xaml.cs:148` | S |
| 30 | ~~Documents périmés~~ — **PR #110 ouverte** (à fusionner après #107) | enquête du 2026-10-06 | S |
| 31 | ~~`capture.ps1` non DPI-aware~~ — **PR #109 ouverte** | constats | S |
| 101 | La visite (`--tour`) rend la valeur de Pixel en mémoire seulement : les Réglages qu'elle ouvre enregistrent entre-temps `ShowPixel` faux dans `config.json` | `IslandWindow.Tour.cs:65`, constaté à chaque visite du 2026-10-18 | S |
| 102 | Manifeste d'identité : `MinVersion` 10.0.22000, sous le reste du dépôt (22631) ; le changer change la variante installée et sa signature — à trancher avec la signature | `packaging/identity/AppxManifest.xml`, #110 | S |
| 103 | `ThemeBrushes.Get` crée un `AccessibilitySettings` à chaque appel (deux par rendu dans `UpdateTabs`) | relecture de #112, `ThemeBrushes.cs:37` | S |
| 104 | Onglets de la notch : 24 DIP de haut (cible de 32) ; pas de motif de sélection UI Automation, le Narrateur ne dit pas l'onglet choisi | relecture de #112, `TabStripView.cs` | S |
| 32 | ~529 couleurs écrites en dur | grep | S–M |

## Ordre proposé

1. **Fusion, si l'auteur le demande** : #106, #107, #108, #109, #111 (CI verte) ; #110 après #107 ; #112 et #113 (CI à lire).
2. **Preuves à faire** :
   - à l'écran, avec le réglage système changé par l'auteur : contraste élevé Réglages ouverts (#106), mouvement réduit (#107), thème changé notch ouverte à plusieurs onglets (#112) ;
   - centrage des onglets après un passage de 4 à 2 (#112) ;
   - sur l'installation après la prochaine version : « Démarrer avec Windows » (#91), `[VEILLE]` après une vraie veille (#95).
3. **Courts** : 101, 103, 104, 98.
4. **Chantiers longs** : 46b (lanceur, scènes d'avance), 26 (une région confiée à la machine, découpage), 24 (AOT, fenêtres cachées) ; 40 et 42 à trancher avec l'auteur.
