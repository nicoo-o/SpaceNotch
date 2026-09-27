# ADR-011 — Calendrier, météo et partage local

**Statut** : accepté
**Date** : 2026

## Contexte

La vague 5c ajoute trois fonctions qui sortent du périmètre tenu jusqu'ici — « aucun service cloud,
aucune capacité au-delà de `runFullTrust` et de l'écoute des notifications » :

- **F2 Prochain rendez-vous** lit le calendrier de Windows ;
- **F10 Aperçu météo** interroge un service en ligne ;
- **F8 Partage PC → téléphone** ouvre un port sur le réseau local.

## Décision

1. **Calendrier** : capacité `appointments` dans le manifeste d'identité, accès
   `AllCalendarsReadOnly` — lecture seule, jamais d'écriture. Sans identité de paquet, la lecture
   échoue et la fonctionnalité reste muette. Aucun rendez-vous ne quitte la machine.
2. **Météo** : **désactivée tant qu'aucune ville n'est saisie** dans les réglages. Le service est
   Open-Meteo (ouvert, sans clé, sans compte). Seuls le nom de la ville (géocodage) puis ses
   coordonnées sont envoyés, toutes les 30 minutes au plus ; aucune position n'est lue sur la
   machine.
3. **Partage** : un serveur HTTP à **usage unique**, lancé par un geste explicite (« Partager » sur
   un fichier de l'étagère), qui ne sert qu'un fichier, à une adresse portant un jeton aléatoire de
   128 bits, pendant dix minutes au plus, puis s'arrête. Il écoute seulement tant que le partage
   est ouvert. Le premier partage déclenche la demande du pare-feu de Windows : c'est la
   confirmation à la première utilisation.

## Conséquences

- Le README et la page de confidentialité doivent mentionner ces trois accès.
- Chaque fonction se désactive dans les réglages ; la météo est désactivée par défaut.
