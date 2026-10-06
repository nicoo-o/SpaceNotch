# Direction de SpaceNotch — 2026-10-06

Où va le projet, maintenant qu'il reste **gratuit et open source**, et comment le rendre plus
attrayant. Le document vient d'une enquête en sept dimensions, menée en lecture seule par un agent
chacune : accessibilité, UX pour un néophyte, inventaire des fonctions, positionnement, page
GitHub, confiance et distribution, angles morts. Les résultats ont été recoupés, et les constats
graves revérifiés dans le code (✔). Les choix de fond sont **ceux de l'auteur**, faits le
2026-10-06. La feuille de route qui en découle : [feuille de route re-triée](2026-10-04-feuille-de-route.md).

## 1. Ce que SpaceNotch est, et n'est pas

**SpaceNotch est un compagnon vivant en haut de l'écran.** Pixel, ses yeux, vit au repos : il
regarde, cligne, réagit à la machine. Il montre aussi le travail des agents IA (il réfléchit,
demande, fête la fin). Autour de lui, une notch native qui dit ce qui se passe : musique, volume,
téléchargements, notifications, minuteur, recherche. [ADR-029](../decisions/ADR-029-compagnon-vivant-pixel-identite.md).

- **Pour qui** : quiconque sous Windows 11, y compris un non-technicien, qui aime qu'un outil ait
  une personnalité. C'est le public de NotchNook et de Boring Notch sur macOS.
- **Ce qu'elle est** : native (WinUI 3, sans WebView), gratuite (MIT), sans compte ni télémétrie,
  avec un langage visuel rigoureux (noir OLED, ressorts, forme continue).
- **Ce qu'elle n'est pas** : un tableau de bord de widgets, un produit payant, ni un outil
  réservé aux développeurs (le canal local et les hooks restent, en « Avancé »).
- **Ce qui change** : le §15 de [SpaceNotch 2.0](../ux/spacenotch-2.0.md) est rouvert pour Pixel
  (« vit au repos », sans budget de CPU, calmé par le seul mouvement réduit). Clawd, personnage
  d'Anthropic, est remplacé par Pixel.

## 2. Face aux apps équivalentes

| Projet | Plateforme | Modèle | Ce qui attire | Sources |
|---|---|---|---|---|
| Boring Notch | macOS | gratuit, open source | ~11 000 étoiles ; installation Homebrew ; Discord, feuille de route publique, Ko-fi | [GitHub](https://github.com/TheBoredTeam/boring.notch) |
| NotchNook | macOS | 25 $ à vie | personnalité, étagère, finition | [iMore](https://www.imore.com/apps/mac-apps/this-dollar25-app-gives-my-macbook-pro-a-dynamic-island-and-it-was-worth-every-penny) (page officielle inaccessible pendant l'enquête) |
| Alcove | macOS | 14,99 $ | finition visuelle | [tryalcove.com](https://tryalcove.com/) |
| Notchify | Windows | 1,99 $, Store | présent sur le Store ; critiqué pour sa CPU au repos | [Windows Central](https://www.windowscentral.com/software-apps/notchify-brings-macos-style-dynamic-island-flair-to-windows-11) |
| DynamicWin | Windows | gratuit, code non publié | idée identique | [GitHub](https://github.com/FlorianButz/DynamicWin) |
| TranslucentTB | Windows | gratuit, open source | ~20 500 étoiles ; une seule chose bien faite ; sur le Store | [GitHub](https://github.com/TranslucentTB/TranslucentTB) |

**Forces de SpaceNotch.** Native et sans WebView ; gratuite sous MIT ; sans télémétrie (vérifié :
par défaut, seul `api.github.com` est contacté) ; un langage visuel soigné ; des gestes enseignés au
moment utile (ADR-028), ce qu'aucune app équivalente ne documente ; un canal local unique pour les
agents IA.

**Faiblesses.**
- **Visibilité quasi nulle** : 0 étoile, 0 fork, aucun topic, description en français, 27 versions
  en 10 jours.
- **Confiance** : rien n'est signé, et SmartScreen avertit à chaque version. Le README promet une
  « recherche de mises à jour », alors que le réglage par défaut installe seul (✔). Il annonce
  aussi « < 0,1 % CPU », que les mesures démentent (✔).
- Pas de Store, pas de winget, pas d'ARM64.
- Aucun chemin pour signaler un problème.

## 3. Axes, dans l'ordre choisi

1. **Confiance et installation.**
   - Mises à jour en « Me prévenir » par défaut tant que l'app n'est pas signée.
   - README honnête (n° 53).
   - Signature par [SignPath Foundation](https://signpath.org/terms), gratuite pour l'open source sous
     conditions. Préalable : l'installeur ne doit plus réécrire l'exécutable (n° 51 ✔). Vient
     ensuite la fin de l'UAC à chaque mise à jour (n° 5).
   - winget (n° 67) et page de confidentialité (n° 10).
   - Le Store en MSIX complet (n° 39) est étudié, pas construit : le Store n'accepte un installeur
     exe que s'il est déjà signé
     ([exigences](https://learn.microsoft.com/windows/apps/publish/publish-your-app/msi/app-package-requirements)).
     Le MSIX complet reste la seule voie qui supprime SmartScreen et l'UAC sans certificat, au
     prix d'une refonte de l'installeur et de la mise à jour : chantier au relais.
2. **Personnalité de Pixel.**
   - Pixel devient l'avatar des agents (fin de Clawd).
   - Pixel est assumé dans le README (animation, capture) et dans le langage visuel.
   - Ses humeurs couvrent les quatre états d'un agent.
3. **Accessibilité.**
   - Narrateur (n° 48, 58).
   - Contraste élevé des Réglages (n° 57).
   - Taille du texte (n° 59).
   - Mouvement réduit en fondu, et qui calme vraiment Pixel (n° 22).
4. **Simplicité pour néophytes.**
   - Présentation alignée sur les tuiles (n° 61) et tuiles claires (n° 62, 63).
   - Section « Avancé » dans les Réglages (n° 23).
   - « Signaler un problème » (n° 56).
   - Un seul registre, tu ou vous (n° 29).

## 4. Chaque fonction jugée

Inventaire du registre de la notch (26 fonctions). Preuves : `AppSettings.cs:456-569, 670-688`,
`IslandSceneCatalog.cs`, minuteurs relevés dans `src/SpaceNotch.Features`.

| Fonction | Par défaut | Coût | Verdict |
|---|---|---|---|
| Média (paroles, Spotify) | activée | événements ; une requête par morceau | **garder** (Spotify en « Avancé ») |
| Volume | activé | Core Audio | **refondre le texte** : « remplace l'indicateur de Windows » est faux (rien ne le masque) |
| Luminosité | toujours | événements | **garder**, sous la bascule Volume |
| Notifications | activées | événements + lecture de « Ne pas déranger » toutes les 3 s | **garder** |
| Bluetooth, téléchargements, micro et caméra, charge | activés | événements | **garder** |
| Moniteur CPU | activé | minuteur de 5 s permanent, doublon de l'échantillonneur de Pixel | **refondre** : un seul échantillonneur partagé avec Pixel |
| Rendez-vous | activés | minuteur ≤ 10 min | **garder** ; miroir de la webcam désactivé par défaut (n° 18) |
| Presse-papier | désactivé | écouteur | **garder** ; tuile à revoir (n° 63) |
| Étagère | activée | aucun | **garder** ; chemin visible à créer |
| Partage (QR) | toujours | TCP à la demande | **garder** |
| Recherche + tuiles, menu rapide, présentation, note, minuteur, rappels, mise à jour | toujours | faible | **garder** |
| Pomodoro | toujours, sans bascule | 1 s pendant la session | **refondre** : fusion dans le Minuteur (« Focus 25 min ») |
| Actions sur copie | désactivées | écouteur partagé | **refondre** : fusion dans le Presse-papier ou les Rappels |
| Météo | sans ville, éteinte | 30 min | **garder** |
| Téléphone | activé | 1 s pendant un appel | **garder** |
| Canal local | activé | tube nommé | **garder**, en « Avancé » ; vérifier les hooks (n° 55) |
| Clawd | activé, sans style « aucun » | 16 images/s, 3 à 5 % d'un cœur quand un agent travaille | **retirer** : remplacé par Pixel (ADR-029) |
| Discord | activé, inerte sans application développeur | reconnexion toutes les 20 s | **refondre** : « Avancé », désactivé par défaut |
| Pixel | activé | 80 ms éveillé, sondage toutes les 2 s | **garder**, c'est l'identité (ADR-029) |

Rien n'est retiré, hors Clawd : deux fusions, et les fonctions d'expert rangées à part.

## 5. Page GitHub, README, communauté (proposé, à appliquer après ton accord)

Ce qui suit est **proposé**. La page GitHub elle-même ne change pas sans ton accord ; les
fichiers du dépôt passent par une PR.

- **Description** (anglais) : « A living Dynamic Island for Windows 11 — native, free, private.
  Media, notifications, downloads, timers and AI agents, with Pixel keeping you company. »
- **Topics** : `dynamic-island`, `windows-11`, `notch`, `winui3`, `desktop-customization`,
  `media-controls`, `open-source`.
- **Aperçu social** : `docs/assets/readme/teaser-poster.jpg` (à vérifier : aucune donnée
  personnelle).
- **Discussions** : ouvertes avec une catégorie Q&A, pour l'aide aux non-techniciens.
- **Signalement privé de vulnérabilités** : activé.
- **Fichiers** : `CONTRIBUTING.md`, `SECURITY.md`, `CODE_OF_CONDUCT.md` (Contributor Covenant),
  `PRIVACY.md` bilingue (services, données envoyées, quand) et `.github/ISSUE_TEMPLATE/`
  (bogue : version, Windows, échelle, écrans, journal ; idée ; `config.yml`), plus un modèle de PR.
- **README et README.fr** :
  - Pixel en tête (une animation), gestes à jour (clic : recherche et tuiles ; clic droit : menu).
  - « Mises à jour : vous prévient (réglable) ».
  - Chiffres mesurés et datés, avec Pixel compris.
  - SmartScreen et Smart App Control expliqués.
  - Confidentialité liée à `PRIVACY.md`.
  - Le README français réaligné sur l'anglais.
  - Captures sans donnée personnelle.
- **Notes de version** : une section bilingue « Nouveautés » en langage d'usage, et un canal
  stable séparé des versions de test (angle mort n° 52).

## 6. Décisions prises le 2026-10-06

| Décision | Choix | Raison en une ligne |
|---|---|---|
| Positionnement | **B · le compagnon vivant** | la personnalité est ce qui distingue SpaceNotch |
| Pixel | **gardé tel quel**, actif par défaut | c'est l'identité ; le §15 est rouvert pour lui (ADR-029) |
| Règle du repos | **vivant sans limite** | choix de l'auteur ; seul le mouvement réduit calme Pixel |
| Clawd | **remplacé par Pixel** | une mascotte propre, sans risque de marque, une seule identité |
| Mises à jour | **« Me prévenir » par défaut** + **SignPath** | ne plus installer seul un exécutable non signé |
| Axes | confiance, Pixel, accessibilité, simplicité | dans cet ordre |
| Reflet (n° 47) | 30 images/s, ≤ 3 % ; Clawd inchangé | Clawd part de toute façon (ADR-029) |

## 7. Décisions qui restent à prendre

1. **Tu ou vous** : un seul registre pour l'installeur, la notch, les Réglages et le README.
2. **Langue des issues et des notes de version** : anglais, français, ou les deux.
3. **FUNDING.yml** : accepter des dons, ou affirmer « gratuit, sans don ».
4. **winget maintenant** (non signé, `--notifications=off`) ou après la signature.
5. **ARM64 natif** (PC Copilot+) : une seconde chaîne de construction et de test.
6. **Barre des tâches en haut** (StartAllBack, ExplorerPatcher) : décaler la notch sous la barre
   (règle n° 1 du §15) ou proposer un bord latéral. Il faut un ADR.
7. **Plus de deux langues un jour** : si oui, changer `Lang.T` avant qu'il ne grossisse encore.
8. **Ouvrir aux contributions** : protection de `main`, CI obligatoire, CONTRIBUTING.
9. **Pixel : un réglage d'intensité** (calme, normal, expressif), qui ne contredit pas
   « vivant sans limite ».
