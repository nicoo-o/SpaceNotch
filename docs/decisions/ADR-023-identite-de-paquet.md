# ADR-023 — Identité de paquet non signée pour les notifications

- **Statut** : accepté (vague 3 de l'audit, septembre 2026)
- **Contexte** : `UserNotificationListener`, qui permet d'afficher dans la notch les
  notifications des autres applications, exige une *identité de paquet*. SpaceNotch est
  une application WinUI 3 non empaquetée : sans identité, l'écoute échoue en silence.

## Options étudiées

1. **Paquet non signé à emplacement externe** (Windows 11) : un paquet qui ne contient que
   son manifeste et trois logos, lié au dossier d'installation. L'éditeur porte l'OID
   réservé aux paquets non signés ; Windows l'accepte avec `AllowUnsigned`.
2. Certificat généré sur le PC à l'installation, clé privée jetée après signature.
3. Retirer honnêtement l'affichage des notifications.

## Décision

Option 1, choisie par l'utilisateur. Aucun certificat, aucune clé à protéger. Windows 10
n'est pas pris en charge pour cette fonctionnalité (l'application cible Windows 11).

## Mise en œuvre

- `packaging/identity/AppxManifest.xml` : identité `SpaceNotch.Identity`, capacité
  `userNotificationListener`, `uap10:AllowExternalContent`, application `win32App` en
  `mediumIL`, sans entrée dans la liste des applications.
- Le workflow Release l'empaquette avec MakeAppx (`artifacts/identity/…msix`) ; le projet
  l'embarque à côté de l'exécutable.
- `app.manifest` : élément `<msix>` qui relie `SpaceNotch.exe` à ce paquet. Ignoré tant que
  le paquet n'est pas enregistré — l'exécutable portable tourne comme avant.
- L'installeur copie le paquet dans le dossier d'installation et l'enregistre **depuis le
  processus de l'utilisateur** (`PackageManager.AddPackageByUriAsync`,
  `ExternalLocationUri` = dossier d'installation). Un échec n'empêche pas l'installation.
- Le désinstalleur porte lui-même l'identité : le retrait (`Remove-AppxPackage`) est joué
  par le script de fin, une fois le processus sorti, avant l'effacement du dossier.
- L'accès aux notifications n'est plus demandé au démarrage : la présentation du premier
  lancement et Réglages › Général le demandent, là où l'on explique à quoi il sert.
- Si l'événement `NotificationChanged` est refusé hors UWP, l'écouteur relit la liste des
  notifications toutes les deux secondes et n'annonce que les nouvelles.

## Vérification

Le workflow Release installe, vérifie `Get-AppxPackage SpaceNotch.Identity`, lance la
notch installée et exige au journal « window activated » et « Identité de paquet :
présente », puis désinstalle et vérifie que le paquet a disparu.
