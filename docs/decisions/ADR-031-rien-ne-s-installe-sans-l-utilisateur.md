# ADR-031 — Rien ne s'installe sans l'utilisateur tant que les mises à jour ne sont pas signées

**Statut** : Accepté (version à préciser). Choix de l'auteur le 2026-10-16 (n° 52, PR #89). Amende
[ADR-027](ADR-027-mise-a-jour-automatique.md).

## Contexte

[ADR-027](ADR-027-mise-a-jour-automatique.md) a fait de la mise à jour une installation
automatique : la notch télécharge l'installeur de la dernière release GitHub, le vérifie contre
`SHA256SUMS.txt`, puis l'installe seule au premier moment calme (« Automatiques », d'abord le
défaut).

Cette vérification ne protège que d'un téléchargement abîmé. L'empreinte est publiée **dans la même
release** que l'installeur (`src/SpaceNotch.Platform.Windows/Update/UpdateClient.cs`) : qui peut
remplacer l'un peut remplacer l'autre. L'installeur n'est pas signé par un certificat reconnu
(n° 4), et la signature par SignPath (n° 82) est en pause : pas de réponse, et aucune attendue
pour l'instant (décision de l'auteur du 2026-10-16).

« Me prévenir » est le défaut depuis le 2026-10-06 (n° 77, #53), mais « Automatiques » restait au
choix, et une configuration pouvait le garder.

## Décision

Tant que les mises à jour ne sont pas signées, **rien ne s'installe sans l'utilisateur** :
- le mode « Automatiques » disparaît des Réglages ; restent « Me prévenir » (défaut) et
  « Désactivées » ;
- une configuration `"UpdateMode": "Automatic"` est lue comme « Me prévenir »
  (`AppSettings.Sanitize`) ; la valeur reste dans l'énumération pour relire les anciennes
  configurations ;
- `UpdateRules.Decide` ne rend plus jamais « installer » : la mise à jour est téléchargée,
  vérifiée, puis **proposée** dans la notch (`src/SpaceNotch.Core/Update/UpdateRules.cs`).

## Justification

- Choix de l'auteur, devant trois options présentées le 2026-10-16 : retirer « Automatiques »,
  signer avec notre propre clé, ou laisser tel quel.
- Une installation sans accord d'un exécutable vérifié seulement par une empreinte voisine est la
  seule voie par laquelle une release altérée atteindrait les machines sans que personne ne clique.
  Demander un clic ne vérifie rien de plus, mais laisse à l'utilisateur le temps de voir une
  version inattendue.
- C'est petit, sans dépendance, et réversible dès qu'une signature existe.

## Conséquences

- **Plus difficile** : les correctifs n'arrivent plus seuls. Un utilisateur qui ignore la carte
  (20 h de report à chaque fois) reste sur une version boguée — le problème qu'ADR-027 résolvait.
- La notch ne guette plus de « moment calme » : `UpdateAction.Install`, `UpdateAction.Wait` et
  `QuietIdle` sont retirés ; la vérification d'une mise à jour prête passe d'une minute à un quart
  d'heure (`IslandWindow.Update.cs`).
- Le bouton « Installer » de la carte garde le chemin d'ADR-027 (`--install --quiet --relaunch`,
  relance, « Mise à jour faite »).
- À rouvrir quand l'installeur sera signé (n° 82, n° 4) : l'installation automatique pourra revenir,
  au choix, en vérifiant la signature et non plus seulement l'empreinte.

## Alternatives écartées

- **Signer le fichier d'empreintes avec notre propre clé** (ECDSA, intégrée à .NET, sans NuGet) et
  embarquer la clé publique : protège d'une release remplacée, mais demande un secret GitHub créé
  par l'auteur et une étape de plus dans `release.yml` ; plus long, et un compte GitHub compromis
  pourrait encore modifier le flux de publication. Écarté pour l'instant par l'auteur.
- **Laisser tel quel** (« Me prévenir » par défaut suffit) : une configuration « Automatiques »
  continuait d'installer seule une release non signée.
