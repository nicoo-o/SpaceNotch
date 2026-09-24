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

Il y en a exactement trois, tous bornés et à usage unique :

| Minuteur | Rôle | Particularité |
|---|---|---|
| Ressort | Évaluation de l'animation | Se termine sur un critère mathématique, pas sur une durée arbitraire |
| Expiration des activités | Retrait des activités échues | Armé pour la **prochaine** échéance, désarmé quand il n'y a plus rien |
| Coalescence d'environnement | Regroupe les rafales de messages de moniteur | Un changement de résolution en produit plusieurs ; un seul recalcul suffit |

Le minuteur de persistance des réglages en est un quatrième, et il ne se déclenche que sur action
utilisateur : un curseur déplacé produit des dizaines de valeurs par seconde, écrire le fichier à
chaque pixel serait du travail inutile. Les écritures sont regroupées sur 300 ms.

## Mesures

Les chiffres sont mesurés, pas estimés, par `RuntimeDiagnostics` — exposé dans le menu de la zone de
notification. Les compteurs sont alimentés par les composants concernés, jamais par une boucle de
mesure, qui créerait précisément la charge qu'elle prétend mesurer.

| Mesure | Cible | Résultat mesuré |
|---|---|---|
| CPU au repos | ~0 % | **0,00 % d'un cœur** (0,000 s sur 10 s, sous la résolution du chronomètre) ; 0,24 à 0,31 % sur des relevés plus longs |
| Encombrement au repos | veille 34 × 28 | **34 × 28 DIP**, centré sur l'écran, à 150 % d'échelle |
| Mémoire, Island fermée | 30–60 MB | **96–99 MB** — au-dessus de la cible, voir ci-dessous |
| Mémoire, croissance | nulle | stable sur 20 s, threads décroissants |
| Journal pendant l'inactivité | aucune ligne | **aucune ligne ajoutée** sur 15 s d'affilée |

Le chiffre CPU est donné en **fourchette**, sur plusieurs relevés : il dépend du moment de la mesure
et une valeur unique donnerait une fausse impression de précision. Le dernier point est le plus
parlant — le processus est resté strictement silencieux pendant 15 secondes, avec **10 fonctionnalités
actives sur 10**, dont la surveillance du presse-papier.

### Défaut connu et ouvert : le greffon d'exemple fige l'application

Les mesures ci-dessus valent **greffon d'exemple retiré**. Installé, il fige l'Island en cours de
morphing (136 × 39 DIP, reproduit à chaque démarrage) et fait tourner deux fils à 25–55 % d'un cœur,
sans erreur au journal. La publication depuis un fil de travail en est le déclencheur ; le mécanisme
n'est **pas** établi (détail et hypothèses écartées : [ADR-011](decisions/ADR-011-fil-interface.md)).
Le greffon est livré comme exemple, il n'est pas requis par l'application : tant que le défaut est
ouvert, il est recommandé de ne pas le laisser installé.

### Écart assumé sur la mémoire

La cible de 30–60 MB n'est pas atteinte : le runtime .NET et WinUI 3 posent un plancher
incompressible. La réduction viendrait de Native AOT, dont la compatibilité WinUI doit être vérifiée
avant d'être promise. La mesure reste publiée telle quelle — une cible manquée est une information,
pas une gêne.

### Combien de fonctionnalités sont actives

Le journal annonce `Fonctionnalités actives : N/10` pour les fonctionnalités intégrées. Le
presse-papier est **désactivé par défaut** : sur une configuration neuve, le compte est donc `9/10`,
et `10/10` après activation explicite. Ce n'est pas une anomalie, c'est la règle « aucune capture
sans consentement » rendue visible.

Un greffon installé **s'ajoute au dénominateur** : avec le greffon d'exemple, le journal annonce
`11/11`. Il suit exactement le même cycle de vie, et une panne de sa part ne fait pas échouer les
autres.

## Mesurer soi-même

```bash
dotnet build -c Release
EXE="src/SpaceNotch.App/bin/x64/Release/net10.0-windows10.0.26100.0/win-x64/SpaceNotch.App.exe"

"$EXE" &            # ou lancer depuis l'Explorateur
sleep 18            # stabilisation : le démarrage domine sinon la mesure
```

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
