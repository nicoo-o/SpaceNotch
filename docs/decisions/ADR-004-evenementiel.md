# ADR-004 — Architecture événementielle, aucune scrutation

**Statut** : accepté
**Date** : 2026

## Contexte

Une application toujours visible qui consommerait du processeur en permanence serait inacceptable :
elle tourne pendant des heures sur une machine où l'utilisateur travaille.

## Décision

Le flux est **`Événement → Changement d'état → Animation → Repos`**, jamais
`Minuteur → Vérification → Vérification → …`.

Aucune boucle d'attente n'existe dans le projet :

```csharp
while (true) { Update(); Thread.Sleep(16); }   // interdit
```

## Justification

L'architecture événementielle n'est pas seulement une préférence de style : elle est **mesurable**.
`RuntimeDiagnostics.IdleFor` mesure le temps écoulé depuis la dernière image rendue. Sur une Island
inutilisée, cette valeur croît indéfiniment — c'est la définition opérationnelle du repos. Si une
scrutation existait, elle ne croîtrait pas.

Mesure associée : **0,24 % d'un cœur au repos**, et **aucune ligne de journal ajoutée** sur une
fenêtre d'observation de 20 secondes. Voir [performance.md](../performance.md).

## Conséquences

- Chaque fonctionnalité s'abonne à des **rappels système** : `AddClipboardFormatListener`,
  `IAudioEndpointVolume`, événements de session média, `UserNotificationListener`.
- Les minuteurs sont l'exception et doivent être bornés : un minuteur à usage unique pour la prochaine
  échéance d'expiration, un pour la coalescence des messages de moniteur, un pour le regroupement des
  écritures de configuration. Aucun n'est périodique.
- Une fonctionnalité désactivée **libère réellement** ses écouteurs. Ce n'est pas un masquage.
- Le bus interne (`IEventBus`) découple les producteurs des consommateurs : un composant s'abonne
  uniquement à ce qui l'intéresse.

## Alternatives écartées

- **Minuteur global à 60 Hz** : simple à écrire, mais exactement ce que le projet s'interdit.
- **Sondage à basse fréquence des fonctionnalités désactivées** : « seulement une fois par seconde »
  reste une charge permanente pour une fonctionnalité éteinte.
