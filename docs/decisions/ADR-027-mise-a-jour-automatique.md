# ADR-027 — Mise à jour automatique depuis les releases GitHub

**Statut** : Accepté (v1.17.0)

## Contexte

Chaque correctif (la v1.16.1 en dernier) devait être téléchargé et installé à la
main. Les utilisateurs restaient donc sur des versions boguées, et un défaut
corrigé le jour même continuait de toucher tout le monde.

## Décision

La notch se met à jour elle-même, depuis les releases publiques du dépôt :

1. **Vérification** : 2 min après le démarrage, puis toutes les 6 h, lecture de
   `releases/latest`. Les brouillons et préversions sont ignorés, tout comme un
   lien qui ne pointe pas vers `github.com/nicoo-o/SpaceNotch/releases/download/`.
2. **Téléchargement** : `SpaceNotch-Setup.exe` va dans
   `%LocalAppData%\SpaceNotch\updates`. Sa taille et son **SHA-256** sont
   vérifiés contre `SHA256SUMS.txt`, désormais publié avec chaque version. Sans
   empreinte, rien n'est installé.
3. **Installation** :
   - **Automatiques** (par défaut) : l'installation a lieu au premier moment
     calme (trois minutes sans saisie, notch au repos, aucune activité
     présentée). La notch lance `SpaceNotch-Setup.exe --install --quiet --relaunch`
     avec les options de l'installation existante, puis se ferme proprement.
     L'installeur remplace les fichiers et relance la notch, qui affiche
     « Mise à jour faite ».
   - **Me prévenir** : une carte propose *Installer* ou *Plus tard* (20 h).
   - **Désactivées** : aucune vérification.
4. **Version portable** (non installée) : la carte mène à la page de la version.

Les règles sont pures et testées (`SpaceNotch.Core.Update.UpdateRules`) ; le
réseau est dans `Platform.Windows.Update.UpdateClient` ; l'orchestration est
dans `IslandWindow.Update.cs`.

## Justification

- La release GitHub est déjà la source unique des binaires, signés par le même
  workflow. Pas de serveur à maintenir.
- L'installeur sait déjà mettre à jour par-dessus et réparer (essais du workflow
  Release). La mise à jour automatique ne fait que l'appeler, et le workflow
  vérifie désormais aussi la relance (`--relaunch`).
- Le « moment calme » évite de couper l'utilisateur au milieu d'un appel ou
  d'une saisie.

## Conséquences

- Seules les versions qui contiennent ce programme se mettent à jour seules :
  la v1.17.0 doit être installée une fois à la main.
- Le workflow Release doit continuer de publier `SHA256SUMS.txt`, faute de quoi
  les mises à jour s'arrêtent sans bruit (journal : « [MISE À JOUR] »).
- L'API GitHub non authentifiée est limitée à 60 requêtes par heure et par IP ;
  quatre vérifications par jour en sont loin.

## Alternatives écartées

- **MSIX / App Installer** : il imposerait un paquet complet signé par un
  certificat de confiance. L'identité actuelle ne sert qu'aux notifications
  (ADR-023).
- **Remplacer l'exécutable en place sans installeur** : cela dupliquerait la
  logique de l'installeur (raccourcis, registre, paquet d'identité) et sa
  réparation.
