# ADR-010 — Le répertoire des scènes appartient à l'hôte

**Statut** : accepté
**Date** : 2026

## Contexte

Une fonctionnalité déclare une **clé de scène** ; la fenêtre résout cette clé en vue et en
encombrement. C'est ce qui permet d'ajouter une fonctionnalité sans modifier la fenêtre.

Mais `IslandSceneCatalog` est un dictionnaire statique, et une clé inconnue retombait sur la pilule
fermée. Conséquence pour un auteur de greffon : il n'avait **aucune scène à sa disposition**. Ses
seules options étaient de détourner une clé existante — déclarer une météo comme « Bluetooth » pour
qu'elle s'affiche, ce qui inscrit un mensonge sémantique dans son code — ou de renoncer à l'affichage.

Second manque, découvert en écrivant le greffon d'exemple : la scène générique ne rendait pas les
`Actions` déclarées. Un greffon pouvait donc déclarer un contrôle que **personne n'aurait affiché** —
une API qui promet un résultat sans le fournir est pire qu'une API absente.

## Décision

1. Ajouter `IslandSceneCatalog.Card`, scène générique **destinée au contenu tiers** (340 × 132 DIPs,
   dimensionnée pour un titre, un sous-titre et une rangée de contrôles).
2. Rendre les `Actions` déclarées dans la scène générique.
3. Accepter qu'une `IconKey` soit un **glyphe littéral** (un unique caractère) en plus d'une clé
   logique, afin qu'un greffon apporte sa propre icône.
4. Signaler une clé de scène inconnue **une fois** au journal, avec la liste des clés valides.

Le répertoire reste **la propriété de l'hôte** : un greffon choisit parmi les clés déclarées, il
n'ajoute pas la sienne.

## Justification

**Pourquoi ne pas laisser un greffon déclarer sa scène ?** Parce qu'une scène n'est pas qu'une clé :
c'est un encombrement **validé pour tenir à l'écran**, sur tous les moniteurs et tous les DPI. Un
greffon qui déclarerait une scène de 2000 × 1200 DIPs ferait sortir l'Island de l'écran, et l'hôte
n'aurait aucun moyen de le lui refuser. Rendre le répertoire extensible déplace l'autorité de
l'hôte vers le code tiers, sans bénéfice proportionné.

**Pourquoi `Card` plutôt qu'un accès aux scènes existantes ?** Parce qu'une clé est un contrat
sémantique. Réutiliser `Notification` pour une météo marcherait techniquement, mais rendrait le code
du greffon incompréhensible et couplerait son affichage à une scène que l'hôte peut légitimement
faire évoluer pour ses propres besoins.

**Pourquoi le glyphe littéral ?** Sans lui, un greffon apportant un domaine que l'hôte ne connaît pas
n'aurait aucune icône pertinente — il devrait se rabattre sur `Info`, c'est-à-dire renoncer à dire ce
qu'il affiche. Accepter un caractère isolé comme glyphe est la plus petite extension possible, et
elle ne demande à l'hôte de connaître aucun domaine.

## Conséquences

- Un greffon peut s'afficher, porter une icône juste et exposer des contrôles, **sans qu'aucune ligne
  du rendu ne mentionne son domaine**. La fenêtre ne connaît que `InfoScene`.
- Le signalement des clés inconnues rend une erreur de configuration **visible** au lieu de la
  laisser se manifester par une absence. Il est émis une fois par clé, donc il ne remplit pas le
  journal lors des republications.
- **Aucune vue personnalisée n'est possible.** Une météo tient dans la carte générique ; un
  visualiseur audio n'y tiendrait pas. C'est une limite réelle, documentée comme telle dans
  [plugin-api.md](../plugin-api.md), et non un manque qu'on espère passager.
- La scène générique doit rester sobre : c'est un contrat public, toute mise en page qu'on y ajoute
  devient une promesse faite aux greffons.

## Alternatives écartées

- **Greffon déclarant sa propre scène** : déplace l'autorité de l'hôte vers du code tiers, sans
  garde-fou sur l'encombrement.
- **Détournement des clés existantes** : mensonge sémantique inscrit dans le code du greffon.
- **Rendu de texte libre par le greffon** : transformerait la carte en petite application, avec les
  questions de mise en page, de DPI et d'accessibilité que cela implique.
- **Actions non rendues** : laissait une API qui promet un contrôle sans l'afficher.
