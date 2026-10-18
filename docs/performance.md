# Performance

## La règle

> **Fonctionnalité inactive = zéro travail.**

Une fonctionnalité désactivée libère ses écouteurs système. Ce n'est pas un masquage d'affichage :
`AddClipboardFormatListener` est réellement appelé à l'activation et `RemoveClipboardFormatListener`
à la désactivation. L'état du presse-papier n'est observé que si l'utilisateur l'a demandé.

## Interdit

```csharp
while (true)
{
    Update();
    Thread.Sleep(16);
}
```

Aucune boucle de ce type n'existe dans le projet. Le flux est inversé :

```
Événement → Changement d'état → Animation → Repos
```

et non :

```
Minuteur → Vérification → Vérification → Vérification → …
```

## Les minuteurs qui existent, et pourquoi

Les principaux minuteurs d'interface sont :

| Minuteur | Rôle | Particularité |
|---|---|---|
| Ressort | Évaluation de l'animation | Se termine sur un critère mathématique, pas sur une durée arbitraire |
| Expiration des activités | Retrait des activités échues | Armé pour la **prochaine** échéance, désarmé quand il n'y a plus rien |
| Coalescence d'environnement | Regroupe les rafales de messages de moniteur | Un changement de résolution en produit plusieurs ; un seul recalcul suffit |
| Pixel | Suit le pointeur éveillé (80 ms), vérifie le réveil endormi (5 s), cligne | Arrêté quand ses yeux ne sont pas visibles |

Le minuteur de persistance des réglages ne se déclenche que sur action utilisateur : un curseur
déplacé produit des dizaines de valeurs par seconde, écrire le fichier à chaque pixel serait du
travail inutile. Les écritures sont regroupées sur 300 ms. Pixel, lui, n'a un minuteur actif que
lorsque ses yeux sont visibles.

## Mesures

Les chiffres sont mesurés, pas estimés, par `RuntimeDiagnostics` — exposé dans le menu de la zone de
notification. Les compteurs sont alimentés par les composants concernés, jamais par une boucle de
mesure, qui créerait précisément la charge qu'elle prétend mesurer.

| Mesure | Cible | Résultat mesuré |
|---|---|---|
| CPU au repos | ~0 % | **0,00 % d'un cœur** (0,000 s sur 10 s, sous la résolution du chronomètre) ; 0,24 à 0,31 % sur des relevés plus longs. **Pixel compris** (ADR-029) : 0,18 % puis 0,13 % sur deux fois 60 s, après 18 s de stabilisation, curseur immobile (2026-10-11, `main` 7d698d7, instance de développement, Release) |
| Encombrement au repos | lèvre 80 × 18 | **80 × 18 DIP** déclarés (`IslandFootprint.Idle`, SpaceNotch 2.0) ; le 34 × 28 mesuré avant 2.0 n'existe plus. Non remesuré à l'écran depuis |
| Mémoire, Island fermée | 30–60 MB | **214 Mo** (build de développement au repos) et **311 Mo** (notch installée), 2026-10-03 — au-dessus de la cible, voir ci-dessous. Les 96–99 MB d'avant SpaceNotch 2.0 ne valent plus |
| Mémoire, croissance | nulle | stable sur 20 s, threads décroissants |
| Journal pendant l'inactivité | aucune ligne | **aucune ligne ajoutée** sur 15 s d'affilée |

Le chiffre CPU est donné en **fourchette**, sur plusieurs relevés : il dépend du moment de la mesure
et une valeur unique donnerait une fausse impression de précision. Le dernier point est le plus
parlant — le processus est resté strictement silencieux pendant 15 secondes, avec **10 fonctionnalités
actives sur 10**, dont la surveillance du presse-papier.

### Défaut connu, état inconnu : le greffon d'exemple figeait l'application

Constaté avant SpaceNotch 2.0 et **non revérifié depuis** (n° 28 de la feuille de route). Les mesures
ci-dessus valent **greffon d'exemple retiré**. Installé, il fige l'Island en cours de
morphing (136 × 39 DIP, reproduit à chaque démarrage) et fait tourner deux fils à 25–55 % d'un cœur,
sans erreur au journal. La publication depuis un fil de travail en est le déclencheur ; le mécanisme
n'est **pas** établi (détail et hypothèses écartées : [ADR-011](decisions/ADR-011-fil-interface.md)).
Le greffon est livré comme exemple, il n'est pas requis par l'application : tant que le défaut est
ouvert, il est recommandé de ne pas le laisser installé.

### Écart assumé sur la mémoire

La cible de 30–60 MB n'est pas atteinte, et l'écart s'est creusé avec SpaceNotch 2.0 (n° 24 de la
feuille de route) : le runtime .NET et WinUI 3 posent un plancher, et la cause du reste n'est pas
établie. La réduction viendrait de Native AOT, dont la compatibilité WinUI doit être vérifiée
avant d'être promise. La mesure reste publiée telle quelle — une cible manquée est une information,
pas une gêne.

### Combien de fonctionnalités sont actives

Le journal annonce `Fonctionnalités actives : N/M` : `M` compte les fonctionnalités chargées,
`N` celles qui tournent. Le compte dépend des réglages — `26/26` sur la machine de l'auteur le
2026-10-18. Le presse-papier et Discord sont **éteints par défaut**, et la météo attend une ville : sur une
configuration neuve, `N` est donc plus petit que `M`. Ce n'est pas une anomalie, c'est la règle « aucune capture
sans consentement » rendue visible.

Un greffon installé **s'ajoute au dénominateur**. Il suit exactement le même cycle de vie, et une panne de sa part ne fait pas échouer les
autres.

## Mesurer soi-même

```bash
dotnet build SpaceNotch.sln -c Release -p:Platform=x64
WINAPP="$HOME/.nuget/packages/microsoft.windows.sdk.buildtools.winapp/0.6.1/tools/win-x64/winapp.exe"
"$WINAPP" run src/SpaceNotch.App/bin/x64/Release/net10.0-windows10.0.26100.0/win-x64 \\
  --manifest src/SpaceNotch.App/Package.appxmanifest --exe SpaceNotch.App.exe --detach
sleep 18            # stabilisation : le démarrage domine sinon la mesure
```

Le build local est non empaqueté : pour le lancer avec l'identité de développement, suivre la
procédure `winapp run` du [guide de construction](development/building.md#lancer-build-local-non-empaqueté).

Pour la fluidité, `--tour --frames` (après `--exe SpaceNotch.App.exe --args`) rejoue une visite des
formes et écrit au journal une ligne `[IMAGES]` par rafale : images en retard, images au-delà de
16,7 ms et de 50 ms, et la sous-étape lente.

Puis, dans le menu de la zone de notification, **Diagnostics** affiche l'état, la mémoire, le nombre
d'images rendues et le temps de repos. Pour une mesure CPU fiable, relever deux fois le temps
processeur à 20 secondes d'intervalle et calculer la différence — une mesure incluant le démarrage
attribuerait au repos le coût de l'initialisation.

## Journalisation

`MiniLogger` n'écrit pas depuis le fil appelant : les entrées sont mises dans une file, vidée par un
fil d'arrière-plan, avec rotation à 2 MB. Une fonctionnalité qui journalise dans un rappel système
fréquent ne peut donc pas introduire de latence dans ce rappel.

## Pièges connus

- **Sessions média** : `WindowsMediaSessionManager` écoute les événements de session. Il ne
  scrute pas. Une seule session est suivie à la fois — la plus récente active.
- **Notifications** : l'écoute passe par un `UserNotificationListener` avec une permission
  explicite. Sans permission, la fonctionnalité se déclare indisponible au lieu de boucler en
  tentant de la redemander.
- **Bluetooth** : l'observation est événementielle. Une absence de périphérique n'engage aucun cycle
  processeur.

## Horloge d'images unique et caches (phase D de l'audit)

- **`FrameClock`** (`App/Animations`, logique `FrameFanOut` en Core) : un seul abonnement à
  `CompositionTarget.Rendering`, posé seulement tant qu'une animation tourne et redistribué à toutes.
  Il sert au ressort de la forme, au détachement, au visage, aux passages, à la teinte, aux
  compteurs et à la goutte, qui avait jusque-là son propre minuteur à 16 ms.
  Un abonné qui lève une erreur est retiré, les autres continuent. Une animation qui garde un drapeau
  « je tourne » s'abonne par `FrameClock.Subscribe(handler, onFailed)` et y remet le drapeau à faux :
  avec `Rendering +=`, le drapeau restait vrai et l'animation ne repartait plus.
- **`SilhouetteCache`** : les contours sont arrondis au quart de DIP et les 96 derniers sont gardés,
  car un ressort repasse par les mêmes tailles.
- **Pochettes** décodées à 192 pixels au lieu de leur taille d'origine (souvent 600 à 1 000), avec
  4 entrées en cache au lieu d'une.
- **Crête-mètre audio partagé** entre l'égaliseur et Pixel.
- **Échelle de la bulle** interruptible : une nouvelle cible repart de la valeur affichée.

Ces points de la phase D restent à faire et demandent un essai sur une vraie machine (100 / 150 /
200 %, deux écrans, bords, notch flottante) :

- l'enveloppe de fenêtre fixe avec `WS_EX_TRANSPARENT` pendant la course ;
- le halo dimensionné une fois ;
- les scènes créées à la demande ;
- les visages et passages en canaux de ressort.
