# ADR-022 — Un logo, et un installeur qui est la notch elle-même

**Statut** : Accepté

## Contexte

La v1.0.0 se distribuait en `SpaceNotch.exe` portable : pas d'installeur, et pas d'icône — l'exe
portait l'icône par défaut du modèle de projet, qui n'était même pas reliée à l'exécutable.
L'utilisateur demande un installeur personnalisé, à l'identité de l'application, et un logo.

Réponses aux questions interactives :

- logo : **la grille 3×3 seule**, **cyan avec le pixel central blanc** ;
- installeur : **sur mesure**, qui **est la notch elle-même** — elle descend du haut de l'écran,
  s'ouvre sur ses choix, et se referme en notch ;
- portée : **au choix pendant l'installation** (pour moi / pour tous) ;
- options : **lancer au démarrage**, **raccourci bureau + menu Démarrer**, **désinstalleur
  assorti**, et **garder le .exe portable** ;
- langue : **celle de Windows** (français ou anglais) ;
- déjà installée : **mise à jour directe**, choix et réglages conservés.

## Recherche

- Icône d'application Windows : au minimum 16, 24, 32, 48 et 256 px dans le `.ico` ; 20, 40 et
  64 couvrent la barre de titre, les menus et les écrans à haute densité. Microsoft dessine ses
  icônes sur une grille de 48 ; une icône posée sur une plaque est admise quand l'identité l'exige
  (*Design guidelines for Windows app icons*, *Construct your Windows app's icon*).
- Installeurs habillables : Inno Setup 6.6–6.7 sait désormais le mode sombre, des couleurs et des
  images de fond ; WiX demande une application d'amorçage écrite à la main ; Velopack propose une
  image de chargement et les mises à jour, peu de personnalisation. Aucun ne peut *être* la notch.
- Installation par utilisateur sans droits : `%LocalAppData%\Programs\<produit>` et l'entrée de
  désinstallation sous `HKCU\…\Uninstall` — la voie de VS Code, Discord, Spotify.
- Un désinstalleur ne peut pas effacer l'exécutable qui tourne : la technique classique confie la
  fin à `cmd.exe` (attendre, puis `rd /s /q`).

## Décision

1. **Logo** : la grille hypnotique au repos — centre blanc, croix cyan, coins en veille — sur une
   plaque noire OLED à coins de 26 %, liseré à peine visible pour les barres des tâches sombres.
   Chaque taille jusqu'à 64 px est **dessinée pour elle-même**, les pixels de la grille tombant sur
   des pixels entiers de l'écran. Généré par `tools/brand/make_icons.py` : `.ico` (16 à 256), tuiles,
   et `docs/assets/brand`. Relié à l'exe (`ApplicationIcon`) et posé sur les fenêtres
   (`AppIcon.ApplyTo`), dont la zone de notification.
2. **Un seul exécutable.** `SpaceNotch-Setup.exe` est `SpaceNotch.exe` renommé : un nom contenant
   « setup » (ou `--install`) démarre l'installeur au lieu de l'Island. L'installeur se copie
   lui-même sous le nom de l'application : rien n'est empaqueté deux fois, et ce qui s'installe est
   exactement ce qui a été essayé.
3. **L'installeur est la notch** (`SetupWindow`) : même silhouette, même noir, même ressort que
   l'Island. Elle naît au repos en haut de l'écran principal, s'ouvre à la hauteur de ce qu'elle a
   à dire, change de hauteur à chaque étape (choix, travail, fin), la grille jouant *lire*,
   *traiter*, *achevé* ou *échec*. À la fin, elle se referme au repos et la vraie notch est lancée
   au même endroit.
4. **Portées** (`InstallLayout`) : *pour moi* dans `%LocalAppData%\Programs\SpaceNotch`, entrée
   sous HKCU, sans droits ; *pour tous* dans `Program Files\SpaceNotch`, entrée sous HKLM,
   raccourcis communs.
5. **L'interface n'est jamais élevée.** Pour tous, seul le travail d'administrateur passe dans un
   processus élevé sans fenêtre (`--install-worker`, `--uninstall-worker`). Le lancement au
   démarrage — une préférence, dans la ruche de l'utilisateur — et le premier lancement de la notch
   restent dans le processus de l'utilisateur : lancée depuis un processus élevé, la notch
   tournerait élevée.
6. **Paramètres › Applications** (`UninstallEntry`) : nom, version, éditeur, icône, taille, lien du
   projet, commandes de désinstallation (normale et silencieuse), plus les choix de l'installation,
   relus par la mise à jour.
7. **Mise à jour** : une version installée plus ancienne donne « Mise à jour 1.0.0 → 1.1.0 », sans
   redemander les choix ; la notch en cours est fermée, l'exécutable remplacé (copie à côté, puis
   remplacement), la notch relancée. Réglages intacts.
8. **Désinstallation** : même notch ; raccourcis, entrée et lancement au démarrage retirés, puis
   `cmd.exe` efface le dossier, les caches d'extraction de l'exécutable unique et, sur demande, les
   réglages et journaux. Les greffons restent : ce sont les fichiers de l'utilisateur.
9. **Silencieux** : `--quiet` (avec `--scope=`, `--startup=`, `--desktop=`) installe ou désinstalle
   sans fenêtre, codes de sortie de Windows Installer (0, 1602, 1603).
10. **Release** : `SpaceNotch-Setup.exe`, `SpaceNotch.exe`, `SpaceNotch-win-x64.zip`. L'exécutable
    unique est compressé (`EnableCompressionInSingleFile`). Le workflow installe en silence,
    vérifie fichier, raccourcis, entrée et démarrage, lance la notch installée, désinstalle, et
    vérifie que rien ne reste.

## Conséquences

- L'installeur n'est pas signé : SmartScreen reste à franchir au premier lancement, comme pour la
  version portable.
- La plateforme élevée tourne sans interface : un échec s'y lit dans le journal
  (`%LocalAppData%\SpaceNotch\logs`) et se traduit par « L'installation n'a pas abouti ».
- Le dossier zippé n'est pas un installeur : renommer son `SpaceNotch.exe` en « Setup » installerait
  un exécutable privé de ses bibliothèques. L'installeur est l'exécutable unique.
