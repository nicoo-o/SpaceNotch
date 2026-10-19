# Avertissements de Windows et des antivirus · Windows and antivirus warnings

**Français** · [English](#english)

SpaceNotch n'est pas encore signé par un certificat de développeur. Windows et certains antivirus se méfient
des programmes non signés et peu téléchargés : un avertissement ne veut pas dire qu'un virus a été trouvé.
Cette page dit pourquoi il apparaît, et comment vérifier que le fichier est bien celui publié ici.

## Pourquoi ces avertissements

- **SmartScreen** (« Windows a protégé votre ordinateur ») : le fichier n'est pas signé et n'a pas encore de
  réputation chez Microsoft. Choisis **Informations complémentaires › Exécuter quand même**.
- **Contrôle intelligent des applications** : s'il est actif, Windows bloque toute application non signée
  sans proposer de passer outre. SpaceNotch ne peut pas s'installer tant que l'application n'est pas signée ;
  la signature est prévue.
- **Antivirus** : quelques moteurs signalent par prudence les programmes qui, comme SpaceNotch :
  - se copient eux-mêmes à l'installation (l'installeur est l'application) ;
  - modifient leur propre manifeste à l'installation, pour pouvoir lire tes notifications ;
  - demandent une approbation d'administrateur pour lire tes notifications ;
  - téléchargent leurs mises à jour depuis GitHub.

  Ces comportements sont décrits dans le code source et dans [PRIVACY.md](../PRIVACY.md).

## Vérifier le fichier téléchargé

Chaque version publie `SHA256SUMS.txt` : l'empreinte SHA-256 de chaque fichier de la version.

1. Sur la page de la [dernière version](https://github.com/nicoo-o/SpaceNotch/releases/latest), télécharge
   `SHA256SUMS.txt` à côté du fichier (`SpaceNotch-Setup.exe`, `SpaceNotch.exe` ou `SpaceNotch-win-x64.zip`).
2. Dans PowerShell, dans le dossier des téléchargements :

   ```powershell
   (Get-FileHash .\SpaceNotch-Setup.exe -Algorithm SHA256).Hash.ToLower()
   ```

3. Compare avec la ligne de `SpaceNotch-Setup.exe` dans `SHA256SUMS.txt`. Elles doivent être identiques,
   caractère pour caractère. Sinon, ne lance pas le fichier et télécharge-le de nouveau depuis la page des
   versions.

La ligne `SpaceNotch-installed.exe` est l'empreinte de l'exécutable une fois installé, dont le manifeste a été
modifié : c'est normal qu'elle diffère de celle de l'installeur.

**Ce que cette vérification prouve** : le fichier n'a pas été abîmé ni remplacé en route (miroir, proxy,
téléchargement interrompu). **Ce qu'elle ne prouve pas** : les empreintes sont publiées au même endroit que
les fichiers. Si la page des versions elle-même était compromise, elles le seraient aussi. Seule une
signature y répondra.

Chaque version est construite par GitHub Actions à partir du code public
([`release.yml`](../.github/workflows/release.yml)) ; ses exécutions sont visibles dans l'onglet *Actions* du dépôt.

## Un antivirus bloque SpaceNotch

- Vérifie d'abord l'empreinte (ci-dessus).
- Si elle correspond, c'est un faux positif. Tu peux le signaler à l'éditeur de l'antivirus ; pour Microsoft
  Defender : [soumettre un fichier](https://www.microsoft.com/wdsi/filesubmission), en choisissant
  « Incorrectly detected as malware ».
- Ouvre aussi un [ticket](https://github.com/nicoo-o/SpaceNotch/issues) avec le nom de l'antivirus et de la
  détection : cela aide à savoir quel comportement l'inquiète.

Ne désactive pas ton antivirus pour installer SpaceNotch.

---

<a id="english"></a>

## English

SpaceNotch is not code-signed yet. Windows and some antivirus products distrust unsigned programs that few
people have downloaded: a warning does not mean malware was found. This page explains why it shows up and
how to check that your file is the one published here.

### Why these warnings

- **SmartScreen** ("Windows protected your PC"): the file is unsigned and has no reputation with Microsoft
  yet. Choose **More info › Run anyway**.
- **Smart App Control**: when it is on, Windows blocks every unsigned app with no way through. SpaceNotch
  cannot be installed until it is signed; signing is planned.
- **Antivirus**: a few engines flag, as a precaution, programs that, like SpaceNotch:
  - copy themselves on install (the installer is the app);
  - rewrite their own manifest on install so they can read your notifications;
  - ask for administrator approval to read your notifications;
  - download their updates from GitHub.

  All of this is in the source code and in [PRIVACY.md](../PRIVACY.md).

### Check the downloaded file

Every release publishes `SHA256SUMS.txt`, the SHA-256 hash of each file in the release.

1. From the [latest release](https://github.com/nicoo-o/SpaceNotch/releases/latest), download
   `SHA256SUMS.txt` next to your file (`SpaceNotch-Setup.exe`, `SpaceNotch.exe` or `SpaceNotch-win-x64.zip`).
2. In PowerShell, in your downloads folder:

   ```powershell
   (Get-FileHash .\SpaceNotch-Setup.exe -Algorithm SHA256).Hash.ToLower()
   ```

3. Compare it with the `SpaceNotch-Setup.exe` line in `SHA256SUMS.txt`. They must match character for
   character. If they don't, do not run the file, and download it again from the releases page.

The `SpaceNotch-installed.exe` line is the hash of the executable once installed, after its manifest has been
rewritten, so it is expected to differ from the installer's.

**What this proves**: the file was not damaged or swapped on the way (mirror, proxy, broken download).
**What it does not prove**: the hashes are published in the same place as the files, so if the releases page
itself were compromised, they would be too. Only a signature will cover that.

Each release is built by GitHub Actions from the public code
([`release.yml`](../.github/workflows/release.yml)); its runs are visible in the repository's *Actions* tab.

### An antivirus blocks SpaceNotch

- Check the hash first (above).
- If it matches, it is a false positive. You can report it to the antivirus vendor; for Microsoft Defender,
  [submit a file](https://www.microsoft.com/wdsi/filesubmission) as "Incorrectly detected as malware".
- Please also open an [issue](https://github.com/nicoo-o/SpaceNotch/issues) with the antivirus and detection
  names: it helps find which behavior worries it.

Don't turn off your antivirus to install SpaceNotch.
