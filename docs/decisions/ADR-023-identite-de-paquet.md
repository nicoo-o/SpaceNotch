# ADR-023 — Identité de paquet signée par un certificat éphémère

- **Statut** : accepté (vague 3 de l'audit, septembre 2026), révisé après essai
- **Contexte** : `UserNotificationListener`, qui permet d'afficher dans la notch les
  notifications des autres applications, exige une *identité de paquet*. SpaceNotch est
  une application WinUI 3 non empaquetée : sans identité, l'écoute échoue en silence.

## Première décision, abandonnée : paquet non signé

L'utilisateur avait choisi un paquet non signé (éditeur portant l'OID
`2.25.311729368913984317654407730594956997722`, accepté par Windows 11 avec
`AllowUnsigned`). Essai sur un vrai Windows (`tools/identity-probe`) : le chargeur de
Windows **refuse cet éditeur dans le manifeste d'un exécutable** (« The msix publisher
must be valid according to MSIX packaging rules », erreur 14001) — sous toutes ses
formes. Or l'exécutable doit porter le même éditeur que le paquet. La voie est fermée,
et, pire, l'exécutable ne démarrait plus du tout : le portable aussi était cassé.

## Décision

Choisie par l'utilisateur après l'essai : **paquet signé par le projet**, mis en œuvre
avec un **certificat éphémère** :

- Le workflow Release crée à chaque version un certificat auto-signé `CN=SpaceNotch`,
  signe le paquet d'identité, exporte le certificat **public**, puis détruit la clé
  privée (fichier `.pfx` et magasin). Aucune clé n'est conservée ni stockée dans les
  secrets : il n'y a rien à voler.
- L'installeur approuve ce certificat public **pour l'ordinateur** (magasin « Personnes
  autorisées » de la machine) : la sonde a montré que Windows refuse un certificat
  auto-signé approuvé pour l'utilisateur seul (0x800B0109). D'où **une demande
  administrateur**, faite par un processus élevé qui ne fait que cela — et qui
  n'approuve que le certificat dont l'**empreinte est inscrite dans l'exécutable** par
  le workflow Release : un fichier .cer remplacé dans le dossier de l'utilisateur est
  refusé. L'ancien certificat SpaceNotch est retiré au passage. Refus de l'utilisateur :
  SpaceNotch s'installe sans les notifications Windows.
- La désinstallation pour tous (déjà élevée) retire le certificat ; une désinstallation
  personnelle le laisse — sans clé privée, il ne peut plus rien signer. Le paquet est
  retiré par le script de fin, une fois le processus sorti (il porte lui-même
  l'identité).

## Mise en œuvre

- `packaging/identity/AppxManifest.xml` : identité `SpaceNotch.Identity`, éditeur
  `CN=SpaceNotch`, capacité `userNotificationListener`, `uap10:AllowExternalContent`,
  application `win32App` en `mediumIL`, sans entrée dans la liste des applications.
- `app.manifest` : élément `<msix>` qui relie `SpaceNotch.exe` au paquet ; sans paquet
  enregistré (exécutable portable), il est ignoré.
- L'accès aux notifications n'est plus demandé au démarrage : la présentation du
  premier lancement et Réglages › Général le demandent.
- Si l'événement `NotificationChanged` est refusé hors UWP, l'écouteur relit la liste
  toutes les deux secondes et n'annonce que les nouvelles notifications.

## Vérification

`tools/identity-probe` (workflow « Sonde d'identité ») rejoue la chaîne complète sur un
Windows de GitHub. Le workflow Release installe, vérifie le paquet enregistré, lance la
notch installée et exige au journal « window activated » et « Identité de paquet :
présente », puis désinstalle et vérifie que le paquet a disparu.

## Révision v1.13.2 — un enregistrement cassé ne doit jamais bloquer l'installeur

Constat sur un vrai PC (v1.13.1) : au lancement de `SpaceNotch-Setup.exe`, Windows
affichait « Désolé… Nous ne pouvons pas ouvrir cette application… sélectionner Réparer ».
Windows refuse de lancer **tout** exécutable qui déclare une identité de paquet dont
l'enregistrement est cassé, et l'installeur était le même fichier que l'application :
il ne pouvait plus démarrer pour réparer. L'enregistrement se cassait à la mise à jour :
l'ancien certificat était retiré *avant* l'enregistrement du nouveau paquet, si bien
qu'un enregistrement qui échouait (ou une demande administrateur refusée au mauvais
moment) laissait un paquet signé par un certificat disparu.

Décisions :

- **Identité éteinte dans tout ce qui est téléchargé** (installeur, portable, dossier) :
  le workflow Release remplace l'élément `<msix>` du manifeste intégré par un
  commentaire XML **de même longueur** (`<msix …></msix>` ⇄ `<!--x …></ms-->`), sans
  décaler aucun octet de l'exécutable. L'installeur la **rallume dans la copie
  installée** (`IdentityManifest`, `IdentityManifestFile`). Le portable tourne sans
  identité, comme il l'a toujours fait.
- **Réparation au début de chaque installation** : un paquet d'identité que Windows juge
  en mauvais état (`Package.Status.VerifyIsOK()`) est retiré avant tout le reste.
- **Le certificat précédent le plus récent est gardé** : il signe le paquet encore
  enregistré tant que le nouveau ne l'a pas remplacé. Seuls les plus anciens partent.
  Sans clé privée, un certificat laissé ne peut rien signer.
- **`ForceTargetAppShutdown`** à l'enregistrement : une notch encore ouverte ne fait plus
  échouer la mise à jour du paquet.
- **Essai bloquant dans le workflow Release** : installer la version publiée précédente,
  mettre à jour, vérifier l'identité ; retirer les certificats (enregistrement cassé),
  relancer l'installeur, qui doit démarrer et réparer ; désinstaller.
